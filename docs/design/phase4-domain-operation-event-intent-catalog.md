# 詳細設計 Phase 4: Domain Operation / Event / Intent Catalog

Status: Complete / P4-05 semantic registry  
Tracking: Issue #16  
Parent: `phase4-implementation-ready-design.md`  
Predecessors: Phase 3 domain/cross-domain design, `phase4-domain-payload-schema.md`

## 1. 目的

Phase 3でsemantic contractとして定義したworld action、domain fact、cross-domain mutation request、semantic transactionを、実装registryへ登録可能なstable `OperationKind` / `EventKind` / `IntentKind` / `TransactionKind`へ固定する。

このcatalogはUI command名やmethod名ではない。wire/persistence/historyへ現れるstable machine tokenの初期標準registryである。

## 2. Kind共通規則

```text
OperationKind := StableToken
EventKind     := StableToken
IntentKind    := StableToken
TransactionKind := StableToken
```

命名:

```text
<domain>.<subject>.<verb-or-fact>
```

一度history/wireへ出たtokenの意味を変更しない。

Payload schema:

```text
operation.<kind> / 1.0
event.<kind> / 1.0
intent.<kind> / 1.0
transaction.<kind> / 1.0
```

## 3. Operation registration

```text
OperationKindRegistrationV1 {
  operation_kind: OperationKind,
  owner_domain: DomainToken,
  payload_schema: SchemaRefV1,
  conflict_mode: ConflictResolutionModeV1,
  world_affecting: bool,
  accepted_source_classes: ordered list<Token>,
  result_event_kinds: ordered list<EventKind>
}
```

Standard world-affecting OperationはすべてPhase 1 scheduling/dedup/durability contractへ従う。

## 4. Standard OperationKind registry

### 4.1 Spatial — 4

| token | minimum payload | conflict mode |
|---|---|---|
| `spatial.geometry.carve` | target scope/ref, geometry revision, volume/shape ref, material handoff ref | `custom_deterministic` |
| `spatial.geometry.fill` | target scope/ref, geometry revision, volume/shape ref, source material ref | `custom_deterministic` |
| `spatial.geometry.deform` | target scope/ref, geometry revision, deformation field ref | `custom_deterministic` |
| `spatial.detail.request` | detail region ref, requested level, reason token | `exclusive_first_valid` |

Normal gameplayではgeometry operationは通常foreign domain intentから生成され、直接external submissionを必須としない。Admin/import toolingが送る場合も同じOperation semanticsを使用する。

### 4.2 Environment — 5

| token | minimum payload | conflict mode |
|---|---|---|
| `environment.resource.extract-request` | deposit ref, quantity/mass, worksite ref | `sequential` |
| `environment.resource.return` | resource kind, mass, target scope | `deterministic_reduce` |
| `environment.contaminant.release` | contaminant kind, mass, source scope | `deterministic_reduce` |
| `environment.hazard.inject` | hazard kind, scope, intensity, duration constraint | `sequential` |
| `environment.detail.request` | region ref, requested level, reason | `exclusive_first_valid` |

### 4.3 Physical / Built — 10

| token | minimum payload | conflict mode |
|---|---|---|
| `physical.move.request` | subject ref, desired displacement/velocity, target ref? | `custom_deterministic` |
| `physical.item.pickup` | actor ref, item ref, destination container ref | `exclusive_first_valid` |
| `physical.item.drop` | actor/item ref, target pose/container | `exclusive_first_valid` |
| `physical.item.transfer` | item ref, source container, target container | `exclusive_first_valid` |
| `built.opening.set-state` | opening ref, requested mechanism state | `exclusive_first_valid` |
| `built.construction.start` | worksite scope, design ref, contract/work refs | `exclusive_first_valid` |
| `built.construction.work` | worksite ref, worker/tool refs, work units | `deterministic_reduce` |
| `built.demolition.start` | structure/worksite ref, authority/work refs | `exclusive_first_valid` |
| `physical.repair.perform` | subject ref, worker/tool/material refs, work units | `deterministic_reduce` |
| `physical.combustion.ignite` | subject ref, ignition source ref, energy | `exclusive_first_valid` |

### 4.4 Participation — 5

| token | minimum payload | conflict mode |
|---|---|---|
| `participation.binding.create` | diver ref, resident ref or preference selector, expected binding generation | `exclusive_first_valid` |
| `participation.binding.release` | binding ref, expected generation, reason | `exclusive_first_valid` |
| `participation.binding.rebind` | current binding ref, new resident ref/selector | `exclusive_first_valid` |
| `participation.absence-policy.set` | diver ref, expected policy generation, ordered policy rules | `exclusive_first_valid` |
| `participation.control.submit` | binding ref, resident action payload, client basis ref | `sequential` |

### 4.5 Resident — 9

| token | minimum payload | conflict mode |
|---|---|---|
| `resident.action.request` | resident ref, action token, target refs, parameters | `sequential` |
| `resident.goal.adopt` | resident ref, goal token/target refs | `set_merge` |
| `resident.goal.cancel` | resident ref, goal ref | `exclusive_first_valid` |
| `resident.consume.request` | resident ref, consumable physical ref, amount | `exclusive_first_valid` |
| `resident.sleep.request` | resident ref, location/support ref | `exclusive_first_valid` |
| `resident.communicate.request` | resident ref, recipient refs, content claim ref/message | `sequential` |
| `resident.learn.request` | resident ref, skill/program/source ref | `sequential` |
| `resident.medical-treatment.request` | resident ref, provider/treatment ref | `sequential` |
| `resident.detail.request` | resident/scope ref, requested level, reason | `exclusive_first_valid` |

### 4.6 Society / Economy — 13

| token | minimum payload | conflict mode |
|---|---|---|
| `society.organization.create` | founder refs, organization class, purpose | `sequential` |
| `society.membership.join` | organization/member/role refs | `sequential` |
| `society.membership.leave` | membership ref | `exclusive_first_valid` |
| `society.employment.offer` | employer, worker, job terms | `sequential` |
| `society.employment.accept` | employment offer/contract ref | `exclusive_first_valid` |
| `society.contract.propose` | parties, contract kind, terms digest | `sequential` |
| `society.contract.accept` | contract ref, accepting party | `sequential` |
| `society.property.transfer` | asset ref, from/to holder, right/share | `exclusive_first_valid` |
| `society.market.order-place` | market, side, price, quantity, owner | `sequential` |
| `society.market.order-cancel` | order ref, owner | `exclusive_first_valid` |
| `society.payment.request` | source/destination account, currency, amount, obligation ref? | `sequential` |
| `society.logistics.create` | shipper/consignee/cargo/origin/destination/due | `sequential` |
| `society.information.publish-claim` | claimant, subject refs, claim/content digest | `sequential` |

### 4.7 Governance / Security — 12

| token | minimum payload | conflict mode |
|---|---|---|
| `governance.law.enact` | polity/institution, rule AST, effective range | `sequential` |
| `governance.law.repeal` | rule ref, effective step | `sequential` |
| `governance.permission.issue` | authority, subject, kind, scope, validity | `sequential` |
| `governance.permission.revoke` | permission ref, authority | `exclusive_first_valid` |
| `governance.tax.assess` | polity/tax kind/debtor/base/amount | `sequential` |
| `governance.incident.register` | fact refs, scope, incident kind | `set_merge` |
| `governance.investigation.open` | incident, authority, investigator refs | `sequential` |
| `governance.judicial-case.open` | jurisdiction, parties, claim/charge refs | `sequential` |
| `governance.judicial-case.decide` | case ref, decision/effect AST | `exclusive_first_valid` |
| `governance.enforcement.issue` | authority, order kind, subjects/targets | `sequential` |
| `governance.military.issue-order` | authority/unit/mission/objective refs | `sequential` |
| `governance.border.request-crossing` | subject/vehicle/cargo refs, boundary/checkpoint | `sequential` |

### 4.8 Infrastructure / Information — 11

| token | minimum payload | conflict mode |
|---|---|---|
| `infrastructure.transport.request` | requester, origin, destination, service constraints | `sequential` |
| `infrastructure.service.reserve` | requester, service ref, units, eligible range | `sequential` |
| `infrastructure.service.cancel` | queue/reservation ref | `exclusive_first_valid` |
| `infrastructure.network.switch-state` | network/node/edge ref, requested operational state | `exclusive_first_valid` |
| `infrastructure.repair.request` | failed subject, service/work refs | `sequential` |
| `information.delivery.send` | sender, recipient refs, content ref/digest, channel constraints | `sequential` |
| `information.media.publish` | publisher, claim ref, channel/audience refs | `sequential` |
| `information.record.create` | record kind, authority?, subjects, content digest | `sequential` |
| `information.record.supersede` | previous record ref, new content/version | `exclusive_first_valid` |
| `information.record.retrieve` | requester, record/query ref | `sequential` |
| `infrastructure.detail.request` | network/service/scope ref, requested level | `exclusive_first_valid` |

Standard OperationKind count: **69**。

## 5. EventKind registry

Eventはsource domainで成立したimmutable factである。Event payloadはresult stateのcopyではなく、factを識別するための必要十分なfieldとcausality refを保持する。

### 5.1 Spatial — 8

```text
spatial.geometry.carved
spatial.geometry.filled
spatial.geometry.deformed
spatial.scope.created
spatial.scope.retired
spatial.containment.changed
spatial.boundary.changed
spatial.detail.changed
```

### 5.2 Environment — 15

```text
environment.resource.depleted
environment.resource.extracted
environment.weather.changed
environment.precipitation.occurred
environment.flood.started
environment.flood.ended
environment.ocean.condition-changed
environment.erosion.occurred
environment.deposition.occurred
environment.ecosystem.population-changed
environment.disease-vector.changed
environment.contaminant.changed
environment.hazard.started
environment.hazard.intensity-changed
environment.hazard.ended
```

### 5.3 Physical / Built — 18

```text
physical.movement.completed
physical.movement.blocked
physical.contact.occurred
physical.item.picked-up
physical.item.dropped
physical.item.transferred
built.opening.changed
built.construction.started
built.construction.progressed
built.construction.completed
built.demolition.started
built.demolition.completed
physical.damage.occurred
physical.repair.completed
physical.combustion.started
physical.combustion.ended
physical.material-handoff.prepared
physical.material-handoff.committed
```

### 5.4 Participation — 8

```text
participation.binding.created
participation.binding.released
participation.binding.superseded
participation.binding.resident-deceased
participation.absence-policy.changed
participation.control-mode.changed
participation.control.rejected
participation.detail-requirement.changed
```

### 5.5 Resident — 19

```text
resident.born
resident.died
resident.health.changed
resident.injury.occurred
resident.disease.acquired
resident.disease.recovered
resident.need.threshold-crossed
resident.perceived
resident.belief.changed
resident.memory.formed
resident.memory.decayed
resident.stress.changed
resident.goal.adopted
resident.goal.completed
resident.goal.failed
resident.skill.changed
resident.relationship.changed
resident.action.decided
resident.action.result-observed
```

### 5.6 Society / Economy — 20

```text
society.organization.created
society.organization.retired
society.membership.changed
society.employment.started
society.employment.ended
society.contract.created
society.contract.fulfilled
society.contract.defaulted
society.property.transferred
society.currency.supply-changed
society.payment.settled
society.payment.failed
society.market.order-accepted
society.market.cleared
society.trade.executed
society.production.completed
society.logistics.created
society.logistics.delivered
society.reputation.changed
society.information.claim-created
```

### 5.7 Governance / Security — 22

```text
governance.polity.created
governance.polity.retired
governance.institution.changed
governance.law.enacted
governance.law.repealed
governance.jurisdiction.changed
governance.territorial-claim.changed
governance.effective-control.changed
governance.permission.issued
governance.permission.revoked
governance.tax.assessed
governance.diplomacy.changed
governance.incident.registered
governance.investigation.opened
governance.investigation.closed
governance.judicial-case.opened
governance.judicial-case.decided
governance.enforcement.issued
governance.enforcement.outcome-recorded
governance.military.order-issued
governance.border.crossing-approved
governance.border.crossing-denied
```

### 5.8 Infrastructure / Information — 19

```text
infrastructure.network.changed
infrastructure.service.available
infrastructure.service.degraded
infrastructure.service.unavailable
infrastructure.queue.requested
infrastructure.queue.allocated
infrastructure.transport.reserved
infrastructure.transport.completed
infrastructure.power.outage-started
infrastructure.power.restored
infrastructure.water.outage-started
infrastructure.water.restored
infrastructure.communication.outage-started
infrastructure.communication.restored
information.delivery.queued
information.delivery.delivered
information.delivery.failed
information.record.created
information.record.retrieved
```

Standard EventKind count: **129**。

## 6. IntentKind registry

Intentはsource domainがtarget authoritative ownerへmutation candidateを要求する。target ownerはvalidation/conflict ruleにより拒否可能。

### 6.1 Target spatial

```text
spatial.intent.geometry-carve
spatial.intent.geometry-fill
spatial.intent.geometry-deform
spatial.intent.detail-promote
spatial.intent.detail-demote
```

### 6.2 Target environment

```text
environment.intent.resource-consume
environment.intent.resource-return
environment.intent.contaminant-add
environment.intent.contaminant-remove
environment.intent.hazard-driver-add
environment.intent.water-exchange
environment.intent.ecosystem-pressure
```

### 6.3 Target physical_built

```text
physical.intent.move
physical.intent.apply-force
physical.intent.transfer-item
physical.intent.set-opening
physical.intent.apply-damage
physical.intent.repair
physical.intent.ignite
physical.intent.extinguish
physical.intent.create-worksite
physical.intent.apply-work
physical.intent.material-handoff
```

### 6.4 Target participation

```text
participation.intent.create-binding
participation.intent.release-binding
participation.intent.set-absence-policy
participation.intent.set-control-mode
participation.intent.require-detail
```

### 6.5 Target resident

```text
resident.intent.apply-injury
resident.intent.expose-disease
resident.intent.consume-nutrition
resident.intent.apply-environment-stress
resident.intent.deliver-perception
resident.intent.add-memory-evidence
resident.intent.modify-skill
resident.intent.relationship-effect
resident.intent.action-result
resident.intent.lifecycle-transition
```

### 6.6 Target society_economy

```text
society.intent.create-obligation
society.intent.fulfill-obligation
society.intent.transfer-property
society.intent.post-market-order
society.intent.settle-payment
society.intent.record-production
society.intent.update-employment
society.intent.update-reputation
society.intent.create-information-claim
society.intent.update-logistics
```

### 6.7 Target governance_security

```text
governance.intent.register-incident
governance.intent.classify-action
governance.intent.create-public-claim
governance.intent.update-effective-control
governance.intent.record-enforcement-outcome
governance.intent.border-crossing-fact
governance.intent.create-case-evidence
```

### 6.8 Target infrastructure_information

```text
infrastructure.intent.change-physical-availability
infrastructure.intent.request-capacity
infrastructure.intent.release-capacity
infrastructure.intent.create-delivery
infrastructure.intent.mark-delivery-result
infrastructure.intent.store-record
infrastructure.intent.propagate-outage
infrastructure.intent.request-repair-service
```

Standard IntentKind count: **63**。

## 7. Intent common validation

Target ownerは必ず次の順に検証する。

1. common `MutationIntentHeaderV1`
2. `target_domain`とkind registry owner一致
3. basis Step
4. source domainがkind registryで許可されているか
5. target record/scope存在
6. payload schema/range
7. owner-local precondition
8. canonical conflict resolution
9. shared invariant

unknown IntentKindはsilent ignoreせず`request.invalid` / internal contract violationとして扱う。

## 8. CrossDomainTransactionKind registry

Phase 3のsemantic transactionを次のstable tokenへ固定する。

| transaction kind | required participant domains | commit-blocking invariant |
|---|---|---|
| `transaction.mining-excavation` | spatial, environment, physical_built, society_economy? | geometry/resource/material conservation |
| `transaction.construction` | physical_built, society_economy, governance_security?, spatial? | material/work/property/permission consistency |
| `transaction.demolition` | physical_built, spatial?, society_economy, governance_security? | structure/material/geometry consistency |
| `transaction.birth` | resident, society_economy? | new identity uniqueness/family relation |
| `transaction.death` | resident, participation?, society_economy?, governance_security? | lifecycle/binding/obligation consequence |
| `transaction.disease-transmission` | environment?, resident, infrastructure_information? | exposure/source/result causality |
| `transaction.food-consumption` | resident, physical_built, society_economy? | physical quantity + nutrition consequence |
| `transaction.market-sale-delivery` | society_economy, physical_built, infrastructure_information? | money/property/physical handoff atomicity |
| `transaction.information-transmission` | society_economy?, infrastructure_information, resident? | claim/delivery/receipt separation |
| `transaction.public-record` | governance_security?, information_record, society_economy? | authority/content/store consistency |
| `transaction.crime-justice` | resident/physical fact sources, governance_security, society_economy? | fact/legal classification separation |
| `transaction.border-crossing` | governance_security, physical_built, infrastructure_information? | permission + actual crossing fact |
| `transaction.natural-disaster-cascade` | environment, spatial?, physical_built, resident, infrastructure_information, governance_security? | source hazard/fact/consequence consistency |
| `transaction.infrastructure-outage-cascade` | infrastructure_information, physical_built?, society_economy?, resident? | dependency causal propagation |
| `transaction.medical-service` | resident, infrastructure_information, society_economy?, physical_built? | treatment/service/payment/physical availability |
| `transaction.employment-work` | society_economy, resident, physical_built, infrastructure_information? | work fact/compensation obligation |
| `transaction.military-operation` | governance_security, resident, physical_built, infrastructure_information, society_economy? | authority/order/physical result separation |

`?` participantはtransaction instanceのsemantic kindによりoptionalだが、存在する場合は同一transaction candidateへ参加させる。

## 9. Transaction identity

```text
TransactionId = Trunc128(DomainHash(
  "mv.transaction.v1",
  {
    world_id,
    transaction_kind,
    basis_step,
    root_causality_ref,
    ordered_subject_refs,
    stable_local_ordinal
  }
))
```

thread/worker/order of discoveryを含めない。

## 10. Source-domain authorization matrix

IntentKindはtargetだけでなくsource classもregistry化する。

原則:

- Resident -> PhysicalBuilt: action/move/item intent
- PhysicalBuilt -> Spatial: excavation/fill/deform
- PhysicalBuilt -> Environment: extracted/returned material effect
- Environment -> Spatial: erosion/deposition/collapse geometry intent
- Environment -> Resident: exposure/stress consequence intent
- SocietyEconomy -> PhysicalBuilt/Infrastructure: delivery/work/capacity request
- GovernanceSecurity -> PhysicalBuilt/Resident/Infrastructure: enforcement/order/service authority intent
- InfrastructureInformation -> Resident/Society: delivery/service result event/intent
- Participation -> Resident: control mode/action-source input

任意domainから任意foreign ownerへgeneric mutation intentを送る機構は禁止する。kind registryに明示されたsource-target pairのみ許可する。

## 11. Event persistence / publication

全DomainEventを永久に独立history recordとして保存する必要はない。

次のいずれかを満たすeventはdurable causalityとしてhistory/partition state/transition outcomeから復元可能にする。

- external Operation terminal result根拠
- cross-domain transaction根拠
- persistent identity creation/retirement
- legal/economic obligation fact
- publicationでcontinuity上必須なfact
- replay divergence diagnosticsで必要なfact

high-volume derived eventはtransition digestへ含めつつretained diagnostic detailを有限化できる。

## 12. Conflict mode registry rule

Operation/Intent kindごとに以下の1つを固定する。

```text
exclusive_first_valid
sequential
set_merge
deterministic_reduce
custom_deterministic
```

runtime Configやarrival orderでmodeを切替えない。mode変更はkind/schema semantic version update。

## 13. Error mapping

```text
operation.unknown-kind
operation.payload-invalid
operation.owner-mismatch
operation.precondition-failed
event.unknown-kind
event.payload-invalid
intent.unknown-kind
intent.source-not-allowed
intent.target-owner-mismatch
intent.precondition-failed
transaction.participant-missing
transaction.invariant-failed
transaction.identity-conflict
```

wire external resultはP4-02 common stable result codeへmappingし、internal detailをdiagnostic/auditへ保持する。

## 14. Count audit

| registry | count |
|---|---:|
| OperationKind | 69 |
| EventKind | 129 |
| IntentKind | 63 |
| CrossDomainTransactionKind | 17 |

## 15. Acceptance criteria

- 8 domainすべてにworld-affecting OperationKindが定義されている。
- Phase 3主要event/intent semanticをstable tokenへ写像できる。
- foreign direct mutable writeをgeneric escape hatchで再導入していない。
- Intent target ownerとallowed sourceを検証できる。
- 17 semantic transactionをstable transaction identityへ写像できる。
- Operation/Intent conflict modeがnetwork arrival/thread順に依存しない。
- retry時にOperationKind/payload digestが不変。
- replayでkind/schema/versionを検証可能。

blocker: なし。