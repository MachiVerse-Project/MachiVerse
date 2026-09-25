# 詳細設計 Phase 3: Participation Domain設計

Status: Complete / P3-04  
Tracking: Issue #15  
Parent: `phase3-world-domain-design.md`  
Common contract: `phase3-domain-common-contract.md`  
Resident dependency: `phase3-resident-domain-design.md`

## 1. 目的

`participation` domainは、Diverと既存Residentのworld-affecting binding、binding lifecycle、Diver不在時行動priority policyのeffective state/history、およびResident側へ渡すauthoritative control modeをSimulation Core内で所有する。

本domainはlogin/session/authentication/authorizationやsocket接続状態を所有しない。それらはPhase 2どおりGateway責務である。ParticipationはGatewayでadmitされたworld-affecting参加Operationを受け、CoreのResident identity/world invariantと整合するbinding stateだけを確定する。

## 2. Responsibility / Non-responsibility

### 2.1 Participationが所有する責務

- opaque Diver identity referenceとResidentIdのbinding
- bindingの生成・維持・終了・再binding history
- 1 Resident : 1 active Diver binding invariant
- Diverごとのcurrent binding state
- Resident死亡等によるbinding operability transition
- absence behavior priority policyのeffective state/history
- active control availabilityをResidentへ渡すworld-effective control mode
- bound Residentに必要なsimulation detail floor signal
- binding/policyのpersistence/replay

### 2.2 Participationが所有しない責務

- account credential、login、session、token: Gateway
- authentication/authorization: Gateway
- duplicate login/exclusive control transport admission: Gateway
- UIでのResident検索/候補表示: Gateway/View projection
- Resident lifecycle/health/action decision: `resident`
- actual physical movement: `physical_built`
- local prediction/reconcile: General View
- Diver identityそのもののaccount directory

Participationが保持する`diver_ref`はGateway境界から渡されるstable opaque identity referenceであり、credential内容をCore world stateへ持ち込まない。

## 3. DomainDefinition

```text
DomainDefinitionV1 participation {
  domain_token = "participation"
  domain_family = "participation"
  state_partitions = [
    participation.binding,
    participation.absence_policy,
    participation.control_mode,
    participation.history,
    participation.detail_requirement
  ]
  update_phases = [PREPARE, AGENT_INTERNAL, AGENT_ACTION, CONSEQUENCE, VALIDATE]
}
```

## 4. Binding state

### 4.1 `ParticipationBindingState`

```text
ParticipationBindingState {
  binding_id,
  diver_ref,
  resident_id,
  binding_status,
  created_step,
  effective_from_step,
  ended_step?,
  end_reason?,
  binding_generation,
  absence_policy_ref?,
  causality_refs
}
```

### 4.2 binding status

semantic statusは少なくとも次を区別可能にする。

```text
ACTIVE
RESIDENT_DECEASED
RELEASED
SUPERSEDED
```

network disconnectだけを理由に`ACTIVE` bindingを解除しない。

### 4.3 binding identity

binding自体もstable identity/historyを持つ。

同一Diverが後に別Residentへbindする場合、過去bindingを上書き削除せず、新しいbinding generation/historyとして追跡する。

## 5. Resident selection / binding creation

Diverは既存Residentだけを対象にする。

新規Residentを参加目的で生成するOperationは標準では存在させない。

概念flow:

```text
Diver preference/request
 -> Gateway authentication/authorization/admission
 -> candidate Resident selection/query
 -> admitted BindResident Operation
 -> Participation world validation
 -> binding commit
 -> Resident control context update
```

### 5.1 Gateway validation

Gateway側:

- actor/session identity
- authorization
- operation form
- exclusive-control admission
- policy上の参加可否

### 5.2 Core Participation validation

Participation側:

- ResidentIdがworldに存在する
- target Residentがbinding可能なlifecycle state
- target Residentへ別active Diver bindingがない
- Diver側に矛盾するactive bindingがない、または明示的transitionである
- effective Step/orderが有効
- world invariantを破らない

Gatewayが許可しただけでCore上の重複bindingを作らない。

## 6. Candidate preference semantics

Diverは対象Residentについてcoarse preferenceを指定可能にするが、結果を保証しない。

Phase 3ではpreferenceをworld mutationではなくcandidate query/admission inputとして扱う。

```text
ResidentPreferenceRequest {
  preference_classes,
  constraint_or_preference_mode,
  request_context
}
```

具体的filter項目、ranking、privacy、query schemaはPhase 4/protocolへ持ち越す。

任意Residentを無条件に奪取する意味論は作らない。

## 7. One-to-one binding invariant

active bindingについて:

```text
one resident_id -> at most one active diver_ref
one diver_ref   -> at most one active resident_id
```

標準では同一Residentを複数Diverが同時操作しない。

Gateway側exclusive admissionとCore側binding invariantの二層で守る。

## 8. Connection/control availabilityとの境界

Gatewayはsession/connectivity truthを所有するが、Residentのworld behaviorを変える必要がある場合はeffective Stepを持つworld-affecting control availability transitionをParticipationへ渡す。

```text
ControlAvailabilityFact {
  diver_ref,
  binding_id,
  availability,
  effective_step,
  source_generation,
  causality_refs
}
```

`availability`例:

- `CONTROL_AVAILABLE`
- `CONTROL_UNAVAILABLE`

network error種別そのものをResident behavior ruleへ入力しない。

通常切断とerror切断で同じ`CONTROL_UNAVAILABLE` semanticsを適用する。

## 9. Resident control mode

ParticipationはResident向けに次を提供する。

```text
ResidentControlContext {
  resident_id,
  binding_id?,
  control_mode,
  effective_absence_policy_ref?,
  basis_step
}
```

`control_mode`:

- `AUTONOMOUS`: Diver bindingなし
- `DIVER_CONTROL_AVAILABLE`: active binding + admitted control availability
- `DIVER_ABSENT_POLICY`: active bindingだがcontrol unavailable
- `BOUND_RESIDENT_DECEASED`: binding historyはあるがResident死亡により通常action不可

Resident domainはGateway sessionを直接参照せず、このcontextだけでworld behavior modeを決める。

## 10. Diver action admission to world

Diver操作はGatewayでadmitされたworld OperationとしてCoreへ到達する。

Participationは少なくとも次を照合する。

- diver_ref
- active binding_id/generation
- resident_id
- effective Step
- control mode

validな場合、Resident action inputへforward可能なworld action intentを生成する。

Diver actionでもResidentのhealth、physical capability、inventory、legal/social consequence等の通常world ruleをskipしない。

invalid/stale binding generationのactionはworldへ適用しない。

## 11. Absence behavior policy

### 11.1 requirement

Diverは不在時にResidentが優先する行動方針を事前設定可能にする。

policyはResident autonomous decisionのpriority modifierであり、scripted teleport、invulnerability、world pauseではない。

### 11.2 `AbsenceBehaviorPolicyState`

```text
AbsenceBehaviorPolicyState {
  policy_id,
  diver_ref,
  binding_id?,
  policy_revision,
  priority_rules,
  constraint_rules,
  created_step,
  effective_from_step,
  retired_step?,
  causality_refs
}
```

`priority_rules`の例示的semantic class:

- safety / survival
- maintain routine
- work / obligation
- return/stay at preferred place
- avoid high-risk action
- social/family priority
- resource conservation

具体catalogと数値priorityはPhase 4。世界ruleを無効化するpolicyは禁止する。

### 11.3 effective application

`CONTROL_UNAVAILABLE`へtransitionした時点のeffective policyをResidentへ渡す。

policy未設定の場合はConfigで定義された通常autonomous default behaviorを使う。

通常切断/error切断で別defaultにしない。

## 12. Policy update

Absence policy変更はworld-affecting behaviorへ影響するため、effective Stepを持つOperationとして記録する。

```text
old policy revision
 -> admitted policy update
 -> deterministic effective Step
 -> new effective revision
```

View側local settingだけを変更してCoreへ未記録のままworld outcomeを変えない。

## 13. Disconnect / Reconnect

### 13.1 disconnect

```text
ACTIVE binding + CONTROL_AVAILABLE
 -> ControlUnavailable effective fact
 -> ACTIVE binding remains
 -> control_mode = DIVER_ABSENT_POLICY
 -> Resident autonomous behavior continues
```

worldを巻き戻さない。Residentを消さない。別Diverへ自動reassignしない。

### 13.2 reconnect

同じDiver identityを使い、bindingが有効なら:

```text
ACTIVE binding + CONTROL_UNAVAILABLE
 -> Gateway session/auth recovery
 -> admitted ControlAvailable fact
 -> control_mode = DIVER_CONTROL_AVAILABLE
```

同じResidentへ復帰する。

Viewはその時点のconfirmed world stateへ同期する。

## 14. Resident death

Resident死亡はResident domainの通常lifecycle eventとして発生する。

Participationは`ResidentDied` eventを受け:

```text
ACTIVE
 -> RESIDENT_DECEASED
```

へtransitionする。

- Residentを復活させない
- Diver identityは失わない
- binding historyを削除しない
- dead Residentへ新規control actionを適用しない

再参加可能な場合は、後に明示的な新binding Operationで別の既存Residentへbindする。

参加目的の新規Resident生成は禁止する。

## 15. Binding release / rebind

release/rebindは明示的Operationとして扱う。

概念:

```text
ACTIVE old binding
 -> explicit release/transition
 -> terminal old binding history
 -> validate new existing Resident
 -> new binding generation
```

同一Stepのrelease+bind conflictはcanonical ordering/conflict scopeで決定論化する。

exact user policyはPhase 4/requirements追加で固定可能だが、implicit disconnect reassignmentはしない。

## 16. Update phases

### 16.1 PREPARE

- effective binding/policy operations freeze
- Gateway-origin control availability facts freeze
- Resident lifecycle read basis freeze
- binding generation validation

### 16.2 AGENT_INTERNAL

- ResidentControlContext生成
- detail requirement更新
- absent policy effective revision選択

### 16.3 AGENT_ACTION

- Diver action binding validation
- valid actionをResident action pipelineへ渡す
- absent時はResident autonomous pipelineを使う

### 16.4 CONSEQUENCE

- Resident death
- binding target invalidation
- explicit release/rebind result

を反映する。

### 16.5 VALIDATE

- one-to-one uniqueness
- Resident reference validity
- binding generation monotonicity
- policy reference validity
- control mode consistency

を検証する。

## 17. Same-Step dependency

基本:

```text
Participation State(S)
 + Resident lifecycle State(S)
 + scheduled Gateway-admitted facts
 -> ControlContext(S)
 -> Resident action decision/input
 -> external world consequences
 -> Participation consequence State(S+1)
```

Resident死亡等のterminal eventを同一Stepでcontrol停止へ反映する必要がある場合は、Residentからmerge済み`ResidentDied` factをParticipation CONSEQUENCEへ渡すexplicit same-step dependencyを持てる。

mutual direct writeはしない。

## 18. Intent / Operation catalog

Participationが受ける主要world Operation:

- `participation.binding.create`
- `participation.binding.release`
- `participation.binding.rebind`
- `participation.control.available`
- `participation.control.unavailable`
- `participation.absence_policy.set`
- `participation.absence_policy.revise`

Residentへ出す主要intent/context:

- `participation.resident_control_context`
- `participation.diver_action_forward`
- `participation.detail_floor_request`

## 19. Event catalog

- `ParticipationBindingCreated`
- `ParticipationBindingReleased`
- `ParticipationBindingSuperseded`
- `ParticipationControlAvailabilityChanged`
- `ParticipationAbsencePolicyChanged`
- `ParticipationBoundResidentDied`
- `ParticipationResidentControlModeChanged`

## 20. Conflict scope

```text
participation/diver/{diver_ref}
participation/resident/{resident_id}
participation/binding/{binding_id}
participation/policy/{diver_ref}/{binding_generation}
```

同一Residentへの複数bind requestは`exclusive_first_valid`をcanonical total orderで解決する。

到着順、Gateway identity、Master identityをtie-breakerにしない。

## 21. Shared invariant

### 21.1 `INV-PARTICIPATION-RESIDENT-UNIQUENESS`

1 Residentへ複数active Diver bindingを持たない。

### 21.2 `INV-PARTICIPATION-DIVER-UNIQUENESS`

1 Diverが同時に複数Residentをactive control bindingしない。

### 21.3 `INV-PARTICIPATION-RESIDENT-EXISTS`

active bindingは有効なpersistent Resident identityを参照する。

### 21.4 `INV-PARTICIPATION-NO-AUTO-REASSIGN`

disconnect/control unavailableだけをcauseにresident_idを変更しない。

### 21.5 `INV-PARTICIPATION-POLICY-EFFECTIVE-HISTORY`

world outcomeへ使ったabsence policy revisionとeffective Stepを追跡できる。

### 21.6 `INV-PARTICIPATION-NORMAL-RULES`

Diver control modeを理由にResident/Physical/Society/Governance invariantをbypassしない。

## 22. Detail level

Participation stateは小さくpersistentなので、binding/history自体を地域detailのためにaggregate消失させない。

### 22.1 `D0_ENTITY`

- exact binding/control mode
- active policy revision
- action binding validation
- local detail floor

### 22.2 `D1_LOCAL_AGGREGATE`

binding identity/stateは保持。control unavailable時のaction validation負荷を低下可能。

### 22.3 `D2_REGIONAL_AGGREGATE`

binding/history/policy identityは保持。Resident側update cadenceだけ低下可能。

### 22.4 `D3_BOUNDARY_SUMMARY`

bindingとpolicy effective historyはpersistent Core stateとして保持し、Residentが境界を跨ぐ場合もbindingを移し替えない。

つまりParticipationは原則「状態をaggregateして消す」より「更新頻度を下げる」domainである。

## 23. Detail floor signal

active Diver controlが可能なResidentは、Resident/PhysicalBuilt/Spatial/必要周辺domainへdetail floor requestを出せる。

control unavailable時もbindingは維持するが、Residentのinteraction状況が低ければConfigに従いdetail cadenceを下げられる。

camera位置だけではworld detailを決めない。

## 24. Persistence / Replay

永続化対象:

- binding history
- binding generation
- resident/diver references
- absence policy revisions/effective steps
- control availability world-effective transitions
- death/release/rebind cause

replay時にnetwork disconnect timingそのものを再現する必要はなく、world-affecting effective transitionとして記録された入力を再現する。

## 25. Gateway / Protocol boundary

GatewayからCoreへはcredential/session objectを渡さず、protocol契約上のadmitted actor/binding/control factへ正規化する。

Participationは「このuserがlogin可能か」を判定しない。

一方、Core world invariantとしてResident存在・binding uniqueness等はParticipationが必ず再検証する。

## 26. Publication boundary

Gateway/Viewへ次をauthorized projectionできる。

- current binding status
- bound Resident reference where permitted
- control availability/effective mode
- absence policy revision/status
- rebinding requirement after death

認証情報はpublicationしない。

## 27. Traceability

| Requirement | Coverage |
|---|---|
| Q190〜Q194 | bound Residentにdetail floor、境界でもbinding continuity |
| Q232 | Core confirmed ResidentをView predictionのreconcile targetに維持 |
| Q233 | reconnectはcurrent world stateへ同期し同binding復帰 |
| Q234 | disconnect中もResident継続、不在priority policy、disconnect種別同一扱い |
| Q240〜Q244 | auth/session責務をGatewayへ残す境界 |
| Q260 | Diver専用Residentを生成しない |
| Q261 | coarse preferenceは可能、成功保証なし |
| Q262 | 1 Resident : 1 Diver、disconnectで変更なし |
| Q263 | reconnectでも同一Diver identity |
| Q264 | Resident死亡は通常死亡、Diver identity継続、再bindは既存Resident |
| Q268 | Gateway不在でもworld進行を止めない |
| Phase 2 cross-component review | Gateway admissionとCore binding/effective policy stateを分離 |

## 28. Phase 4 handoff

Phase 4で確定する事項:

- binding/policy schema
- binding generation encoding
- candidate query/preference schema
- absence behavior priority catalog
- control availability protocol payload
- Resident action binding proof/reference schema
- release/rebind exact policy
- detail floor parameters
- authorized publication fields

Phase 4はno-new-Resident、one-to-one binding、no-auto-reassignment、normal-world-rule principleを変更してはならない。
