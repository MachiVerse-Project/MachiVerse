# Admin View設計

## 1. 目的

Admin ViewはMachiVerse各componentを運用・診断・設定するためのoperator向けUIです。

General Viewの利用者roleとは別系統であり、General View Administrator権限をAdmin View permissionとして扱いません。

Admin Viewは各componentの公開可能なlog、health/status、metrics、Config、operational command、simulation Admin Operation等を扱います。

## 2. Auth / permission domain

- General ViewとAdmin Viewのauth/authz domainを明確に分離する。
- General View Administratorを理由にAdmin View accessを自動付与しない。
- Admin View userはGatewayを通じてloginする。
- Q241に従い、login requestはconnected GatewayからMaster Gatewayへproxyされ、Masterでloginを確定する。
- privilege change/revokeは接続中にも反映可能とし、old privilegeでnew Admin Operationを継続させない。
- auth outage時もpermission checkをbypassしない。

Credential、token、IdP、session technologyは未確定です。

## 3. 主な責務

- component health/status表示
- structured log参照
- metrics / diagnostic state表示
- Config current value・classification・validation stateの表示
- permitted Config change request UI
- operational command UI
- simulation Admin Operation UI
- high-impact operation confirmation
- audit trailの参照
- protocol / Capability mismatchの診断表示
- Gateway resync / Master generation等のarchitecture-specific status表示
- save / replay / recovery status表示
- 将来のaddon managementの運用入口候補

## 4. 責務外

- General View利用者向け参加機能
- Diver / Spectator / Moderator / General View Administrator roleの提供
- authoritative World State管理
- simulation rule implementation
- Core internal mutable stateへのdirect access
- other component Config fileのdirect edit
- component internal code/APIへのdirect dependency
- UIだけで完結するauthorization
- generic Undoによるhistory消去
- addon implementationそのものをAdmin View内部で実行・改変すること

## 5. Component health / metrics

Admin Viewは一般的なhealth metricに加え、MachiVerse固有の状態を確認可能にします。

少なくとも次を対象にできます。

- CPU / memory / connection等
- Simulation Core current Simulation Step
- standard 30Hz targetに対するlag
- Master Gateway identity / generation
- resyncing Gateway
- Gateway publication buffer state
- Operation retry増加
- relevant dedup/idempotency diagnostic
- protocol / Capability mismatch
- Config validation error
- save / recovery state

Exact metric name、sampling interval、alert thresholdは詳細設計で決定し、調整可能な数値はcomponent Configへ置きます。

## 6. Structured log

各componentはstructured logを基本とします。

Admin Viewでは必要に応じ次のcontextを使って検索・関連付けられる方向とします。

- component / instance
- Simulation Step / World Time
- Operation ID
- Batch ID
- Master generation
- session/user audit context

Log retention、capacity、rotationはcomponentごとの外部Configで設定し、audit logとhigh-volume diagnostic logを同じ保持条件へ固定しません。

Storage/collector/search technologyは未確定です。

## 7. Config参照・変更

### 7.1 Ownership

各componentが自身のConfig fileを所有します。

Admin Viewはother component Config fileをdirect read/writeするのではなく、管理protocolを通じて公開値とchange requestを扱います。

### 7.2 Classification

Admin Viewは必要に応じ、Config itemが次のどのclassificationかを表示できる方向とします。

- simulation-affecting / display-or-ops-only
- runtime mutable
- restart required
- world regeneration required
- other explicit safe boundary required

### 7.3 Change semantics

- GatewayがAdmin permission、request format、target、allowed conditionを検証する。
- Target componentが自身のtype/range/cross-constraintを検証する。
- invalid change setをpartial applyしない。
- simulation-affecting runtime changeはexplicit Simulation Stepへatomicに適用しhistoryへ記録する。
- startup Configに不整合があればcomponent/worldを起動しない。
- generic Undoは設けない。元の値へ戻す場合もnew Admin Operation/change requestとして実行する。

### 7.4 Old Config compatibility

Old Configにnew fieldが不足する場合、owner componentはdefaultを適用し、そのfieldをConfig fileへ追加するQ214要件に従います。

Admin View自身がそのfile migrationを直接実行するとは限りません。

## 8. Operational command

Admin Viewはdefined operational commandをtarget componentへrequestできます。

Commandごとに少なくとも次を明確にする必要があります。

- target
- permission
- parameter
- simulation-affecting classification
- safe execution boundary
- idempotency / retry
- timeout
- result / error semantics

Concrete command listは詳細設計で決定します。

## 9. Simulation Admin Operation

Simulationへ影響するAdmin OperationはAdmin View→Gateway→Simulation Coreの経路を使用します。

Q235/Q275に従い責務を分離します。

### Gateway

- Admin authn/authz
- operation format
- target
- Admin operationとしてのallowed condition
- protocol-level validation

### Simulation Core

- UI上のAdmin roleを解釈しない。
- 全Operation共通のWorld State invariant/state-transition consistencyを維持する。
- Gateway-approved Admin Operationでもcommon invariantを破壊するstate transitionを無条件適用しない。

Simulation-affecting Admin Operationは、単にAdmin由来という理由でunconditional highest priorityにしません。Simulation-non-affecting operationに限りAdmin highest priorityを許容します。

Login以外のAdmin Core OperationをMaster Gateway pathへ統一するかは未確定です。

## 10. High-impact operation

World destruction、大量変更、time control、大規模Config change等のhigh-impact Admin Operationは追加確認・audit対象とします。

- actor
- target
- requested content
- Operation ID
- applicable Simulation Step
- result
- reject reason

等を追跡可能にします。

Exact high-impact category、confirmation count、multi-person approval、UI flowは詳細設計で決定します。

## 11. Audit / no generic Undo

Admin Operationは少なくとも次をaudit可能にします。

- actor
- request time
- Operation ID / request ID
- operation type
- target
- request content
- application Simulation Step / effective boundary
- result
- reject reason
- related Config change

Generic Undoは設けません。

Past Operationをhistoryから消して戻すのではなく、以前のvalue/stateへ近づけるためのnew Operationを実行し、そのnew Operationもauditします。

Savepoint recovery / replayは別のrecovery conceptです。

## 12. Pauseとの関係

- Pause中もAdmin requestの受信・auth・queue保持を可能にする。
- simulation-affecting OperationはPause中のstopped Simulation Stepへ曖昧applyしない。
- Resume後のexplicit valid Stepへdeterministicにassignmentする。
- simulation-non-affecting operational actionはPause中でも実行可能なcategoryを持てる。

## 13. Addon managementの位置付け

MachiVerseはofficial addonとthird-party addonを許容します。

Addonはcomponent単位で設定可能です。

Admin Viewをaddon install/update/disable/removeの運用入口にすることは望ましい方向ですが、**Admin Viewからのaddon installation機能そのものはまだ確定済みstandard requirementではありません。**

将来その機能を持たせる場合も、Admin Viewがtarget component内部へ無制限direct accessする構造にはしません。

## 14. Addon protocol boundary

- Standard protocolへaddon functional payload、addon command、generic extension data areaを載せない。
- Addon install/identity/version/required/provided Capability等のconnection safety/compatibility meta informationはstandard protocolで交換可能。
- Addon-specific cross-component functional communicationはprotocol framework addon等とadditional protocol側へ分離する。

Concrete addon API/package/runtime loading方式は未確定です。

## 15. Addon compatibility / startup safety

- addon target component/protocol version、required/provided Capability、dependency addon等を検証可能にする。
- addon configurationに不整合があれば重大度に関係なくtarget componentを起動しない。
- saved worldが依存するaddon/version/Capabilityに不整合がある場合も、explicit migrationが完全成功しない限りworldを起動しない。
- addon updateはexplicit operationとし、apply前にcompatibility/Capability/Config impactを確認する。
- simulation-affecting addon updateはsafe Simulation Step/restart boundary等でapplyする。
- addon disable/remove前にdependencyとpersistent world/save impactを確認する。

## 16. Official addon trust

Official addonはstore-style distribution routeを持つ方向とし、少なくともhash verificationまたは同等のintegrity verificationを行います。

- Hash aloneはpublisher identity proofではない。
- exact hash algorithm、signature、metadata、verification granularity、failure handlingは未確定。
- Official addonとして提供する保証範囲はthird-party addonと区別する。

## 17. Third-party addon trust

Third-party addonはoperator/user自身の責任で導入する方針です。

- Official addonと同等の保証を自動的に与えない。
- UI上でofficial / third-partyのtrust differenceを明確に区別する方向とする。
- Third-party addonであってもMachiVerse standard component/protocol boundaryを黙って破壊してよいわけではない。

Sandbox、permission model、signature requirement等の具体security mechanismは未確定です。

## 18. Protocol / Capability error display

Admin Viewではoperatorが少なくとも次を診断可能にする方向です。

- Major protocol mismatch
- required Capability missing
- addon compatibility mismatch
- Config invalid
- Master generation/problem
- Gateway resync
- save/recovery incompatibility

Exact error code/UIは詳細設計で決定します。

## 19. Component reachability

Admin Viewとのexternal management boundaryはGatewayが所有します。

ただしCore以外のcomponentへのすべてのmanagement requestをGatewayがproxyするか、component-specific management protocolを別途設けるかは未確定です。

どの方式でもcomponent code independenceとprotocol-only communicationを維持します。

## 20. 詳細設計へ残す事項

- Admin auth/token/session technology
- exact permission matrix
- metrics/log schema and collector
- Config management message schema
- operational command list
- high-impact confirmation flow
- audit storage/retention
- component management reachability architecture
- addon install/update UIをstandardに含めるかの最終決定
- addon package/signature/hash詳細
- official store metadata/distribution
- third-party trust/security mechanism
- protocol/Capability error UI
