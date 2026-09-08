# Gateway・Admin View間Protocol設計書

## 1. 所有者

本protocolのownerはGatewayです。

ProtocolIdは `mv.gateway-admin-view` とする。

共通 envelope / version / Capability / result / error / correlation contractは `docs/design/phase1-protocol-envelope.md` を正本とする。

## 2. 目的

Admin ViewがMachiVerse各componentを運用・診断し、許可されたConfig change、operational command、simulation Admin Operationを要求するためのexternal contractです。

Admin ViewはGeneral Viewの上位roleではなく、system operator向けの別auth/authz domainです。

## 3. 設計原則

- Admin ViewとGatewayはcode、DLL、internal type、shared DTO libraryを共有しない。
- General View roleとAdmin View permissionを分離する。
- General View AdministratorをAdmin View operatorとして自動認可しない。
- Admin Viewからcomponent internal implementationへ直接アクセスしない。
- Admin requestのauthn/authz、operation format、target、Admin operationとしてのallowed conditionをGatewayが検証する。
- Target component自身が所有するstate invariantやConfig consistencyは、そのcomponentの責務として維持する。
- UI表示制御だけでmanagement permissionを完結させない。
- Admin Operationは追跡・監査可能にする。
- generic Undoを設けない。元へ戻す場合もnew Operation/change requestとして実行する。

## 4. Common envelope / Version / Capability

normal messageは `ProtocolEnvelopeV1` の共通意味を持つ。

- protocol id: `mv.gateway-admin-view`
- negotiated ProtocolVersion / NegotiationGenerationを明示する。
- MessageId / CorrelationId / CausationIdをtraceに使用できる。
- world-related request/resultはWorldContextV1を利用する。
- world/system mutation requestはOperationContextV1を利用する。
- connect時にrequired / provided Capabilityをnegotiationする。
- required Capability不足をsilentに無視しない。
- reconnect時にnegotiationをやり直す。

Addonについてstandard protocolで交換するのはcompatibility/safety metadataだけとし、Addon functional payload/commandを載せない。

## 5. Auth / login / session

Admin ViewはGeneral Viewとは別domainでauthentication/authorizationする。

- userはconnected Gatewayへlogin requestを送る。
- Q241に従いGatewayはlogin requestをMaster Gatewayへproxyし、login処理をMasterで確定する。
- non-Master Gatewayが独立に同じloginをfinalizeしない。
- Master switch/live migrationでsession consistencyを壊さない。
- authorization outage時もpermission checkをbypassしない。
- severe privilege revokeではexisting session/credentialをinvalidate可能にする。
- login request/resultはCorrelationIdで追跡可能にする。

MessageId / CorrelationId / ComponentInstanceIdをcredentialとして扱わない。

具体credential、token、IdP、session storageはauth詳細設計で決定する。

## 6. Communication category

### 6.1 Component health / status

Admin Viewは各componentの公開可能なhealth、processing state、diagnostic stateを参照できる。

少なくともarchitecture上、次を表示可能にする。

- Core current Simulation Step / lag
- Master Gateway identity / generation
- resyncing Gateway
- protocol / Capability mismatch
- Config generation / validation error
- Operation retry growth / relevant dedup diagnostics
- save / recovery state
- generic CPU / memory / connection metrics等

World state basisを持つstatusはWorldContext `basis_step` を使用できる。

Exact metrics/schemaはobservability詳細設計で定義する。

### 6.2 Log

各componentの公開可能なstructured logを検索・参照できるcontractを持つ。

Log retention、capacity、rotation等はcomponent Configで管理し、audit logとhigh-volume diagnostic logを同じretentionへ固定しない。

CorrelationId、OperationId、BatchId、MasterGeneration、SimulationStep等のcontextを必要に応じ検索可能にする。

Exact query/stream mechanismは未確定。

### 6.3 Config read

各componentが所有するConfigのうちAdmin Viewへ公開可能な項目、current effective value、classification、ConfigGeneration、必要なvalidation stateを参照可能にする。

Admin ViewやGatewayが他componentのConfig fileを直接読むことをcontractにしない。

ConfigGenerationのownerを曖昧にしない。Gateway自身のgenerationをWorldContextへ載せる場合と、target component Config generationをpayloadで返す場合を区別する。

### 6.4 Config change

Config changeはexplicit requestとしてGatewayへ送る。

- GatewayがAdmin permission、format、target、operationとしてのallowed conditionを検証する。
- Target componentが自身のConfig type/range/cross-constraintとstate consistencyを検証する。
- startup Config不整合を抱えたcomponentは起動しない。
- runtime change可能項目はsafe explicit boundaryでatomicにapplyする。
- simulation-affecting Config changeはSimulation Stepとhistoryへ結び付ける。
- invalid runtime change setはpartial applyせずrejectする。
- 元の値へ戻す場合もgeneric Undoではなくnew change requestとして実行する。

P1-03のConfigChangeSet contractに従い、少なくとも次を扱えるようにする。

- stable OperationId / immutable request digest
- expected base ConfigGeneration
- normalized change set
- simulation-affecting場合のcandidate/effective Step semantics
- resulting ConfigGeneration
- before/after ConfigDigestが必要な場合のdiagnostic/audit context

Admin ViewがConfig fileを直接editすることはstandard contractとしない。

### 6.5 Operational command

Defined component operational commandをrequestできる。

Commandごとにpermission、target、parameter、idempotency、timeout、simulation-affecting classification等を定義する必要がある。

World/system stateへ影響するcommandはstable OperationIdを持たせる。MessageId / CorrelationIdをdedup identityとして使用しない。

具体command一覧は詳細設計で決定する。

### 6.6 Simulation Admin Operation

Admin ViewからSimulation Coreへ影響するOperationはAdmin View→Gateway→Core経路を使用する。

責務分離:

- Gateway: Admin authn/authz、format、target、Admin operationとしてのvalidity/allowed condition。
- Core: UI roleを解釈せず、全Operation共通のworld-state invariant / state-transition consistency。

GatewayでAdmin operationとして許可したことだけを理由にCoreがinconsistent World Stateを作ってはならない。

Simulation-affecting Admin OperationをAdmin由来というだけで無条件最優先にしない。Simulation-non-affecting Admin Operationに限り最優先としてよいものとする。

Candidate StepをWorldContext `effective_step` として表現しない。Core確定後のapplied resultでauthoritative effective Stepを返す。

### 6.7 High-impact operation confirmation

World destruction、大量変更、time control、大規模Config change等のhigh-impact operationは追加確認・audit対象とする。

確認UIの一時token等をOperationIdの代替にしない。

Concrete high-impact category、confirmation UX、multi-person approval有無は未確定。

### 6.8 Result

共通ResultStatus / stable ResultCode / RetryAdviceを使用する。

少なくとも次を区別可能にする。

- accepted / pending
- success / no-change
- authorization reject
- format/target/allowed-condition reject
- target component state/config invariant reject
- duplicate/already processed
- stale ConfigGeneration
- late Operation
- temporarily unavailable / resyncing
- version/Capability incompatibility
- internal failure

Machine behaviorはdiagnostic textの文字列比較へ依存しない。

ACK / acceptedはtarget componentのterminal effect successと同一視しない。

## 7. Admin Operation identity / audit

Worldまたはsystem stateへ影響するAdmin actionをtraceable Operation/change requestとして扱う。

Auditでは少なくとも次を追跡可能にする。

- actor
- OperationId / immutable payload digest
- CorrelationId
- operation type
- target
- requested content
- request time
- application Simulation Step / effective boundary（該当する場合）
- result / stable code
- reject reason
- related ConfigGeneration / Config change

Audit historyをgeneral Undoで消さない。

## 8. Config ownership

- 各componentが自身のConfig fileを所有する。
- Admin View / Gatewayはother component Config fileへdirect dependencyを持たない。
- Cross-componentに必要なsetting/stateはowner componentがprotocolを介して公開する。
- Old Configにnew fieldが不足する場合、owner componentはdefaultを有効化し、そのfieldをConfig fileへ追加するQ214要件に従う。
- saved world restore時のsimulation-affecting Config truthはP1-03 contractに従う。

## 9. Component reachability

Admin Viewとのexternal boundary ownerはGateway。

ただしCore以外のcomponent自身に対するすべてのmanagement requestをGatewayがproxyするか、component-specific management protocolを別途設けるかは未確定。

この未確定事項は「Admin Viewがcomponent internal APIへ直接依存してよい」という意味ではない。どの方式でもprotocol contractとcomponent independenceを維持する。

Coreへのsimulation Admin OperationはGatewayを経由する。

## 10. Pause / World Timeとの関係

- simulation-affecting Admin Operationはauthoritative Simulation Step semanticsへ従う。
- Pause中に受信・auth/queueすることは可能だが、simulation-affecting Operationをstopped Stepへ曖昧applyしない。
- Resume後のexplicit valid Stepへdeterministicにassignmentする。
- simulation-non-affecting operational commandはPause中でも実行可能なcategoryを持てる。
- Pause queue / candidate Step / deadline / graceの具体ruleはP1-06で定義する。

## 11. Version / Capability

Common handshakeはP1-04正本に従う。

- common version不在はconnection reject。
- same MajorのMinor backward compatibilityを維持する。
- required Capability不足を明示する。
- reconnectをCapability renegotiationの基本境界とする。
- optional live renegotiationは双方Capabilityと個別barrier設計がある場合のみ許可する。

## 12. Forbidden

- General View AdministratorのAdmin View permissionへのautomatic promotion
- Admin Viewからcomponent internal implementationへのdirect access
- Admin View/Gatewayによるother component Config fileのdirect edit
- UI-only authorization
- unauthorized Admin Operation forwarding
- generic Undoによるhistory消去
- simulation-affecting Admin Operationのunconditional highest priority
- MessageId / CorrelationIdをOperation dedup keyにすること
- candidate Stepをauthoritative effective_stepとして扱うこと
- stale ConfigGenerationをexpected baseとしてsilent applyすること
- incompatible negotiated versionでnormal management communication
- required Capability不足のsilent degradation
- standard protocolへのaddon functional payload
- shared DTO library dependency
- ACKをterminal effect successと同一視すること

## 13. 詳細設計へ残す事項

P1-04で共通化済み:

- common envelope / tracing identity
- version / Capability handshake
- NegotiationGeneration
- WorldContext / OperationContext
- common result/error/retry
- immutable Operation digest boundary

残る個別事項:

- physical transport / serialization / compression
- Admin View credential/session technology
- permission model / operation matrix
- health/metrics/log payload schema
- Config read/change payload schema
- operational command set
- high-impact operation category/confirmation flow
- Operation audit schema/retention
- each component management reachability architecture
- retry / timeout / idempotency retention
- candidate Step / deadline / Pause semantics
