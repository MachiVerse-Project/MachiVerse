# Alpha 1.1 — `perf.reference.v1` canonical workload binding

Status: Decided normative design / implementation pending  
Tracking: #240  
Parent: `phase4-alpha11-normative-closure.md`

## 1. 目的

既存 `Qa04ReferenceLoadV1` / `Qa04ReferenceScenariosV1` descriptorを production Operation scheduler、CrossDomainTransaction、DetailTransition runtimeへ一意に写像する。

本書は benchmark workload profileであり、一般player input policyではない。

## 2. Operation scheduling common rule

All external QA-04 Operations:

```text
SameStepOrderKey.phase = 1            // external_input
semantic_priority = 0
intent_id = OperationId
admission_basis_step = injection_step
requested_not_before_step = NONE
requested_deadline_step = NONE
profile scheduling min_lead_steps = 1
late_policy = REJECT
canonical/effective step = injection_step + 1
```

Domain rankは StandardDomainExecutionPlanのowner rank。

Conflict scope:

```text
HashSuite.DomainHash(
  "mv.perf-reference-operation-scope.v1",
  MV-DCBOR [operation_kind, primary_target_id])
```

## 3. Operation family binding

| QA-04 family | production OperationKind | owner domain | primary target |
|---|---|---|---|
| `participation-control-resident-action` | `resident.action.request` | resident | Resident(ordinal) |
| `physical-item-movement-work` | `physical.move.request` | physical_built | Physical(ordinal) |
| `society-market-payment-contract` | `society.market.order-place` | society_economy | MarketScope(ordinal%100) |
| `infrastructure-service-delivery` | `infrastructure.service.reserve` | infrastructure_information | Service(ordinal) |
| `governance-security` | `governance.incident.register` | governance_security | Resident(ordinal) |
| `environment-spatial-admin-synthetic` | `environment.hazard.inject` | environment | TileScope(ordinal%4096) |

No new benchmark-only OperationKind is created.

## 4. Exact benchmark Operation payloads

### 4.1 Resident

```text
resident_ref = Resident(ordinal)
action_token = Qa04ReferenceScenariosV1.ResidentActivity(residentOrdinal, injectionStep)
target_refs = []
parameters = ordered map { "perf.ordinal" -> ordinal }
```

### 4.2 Physical

```text
subject_ref = Physical(ordinal)
desired_velocity_um_s = (
  signed_small(operation_id,"vx"),
  signed_small(operation_id,"vy"),
  0)
target_ref = NONE
```

`signed_small(id,tag) = int(H(id,tag)%2001)-1000` using benchmark genesis hash source.

### 4.3 Market

```text
market = MarketScope(ordinal%100)
owner = Resident(ordinal)
side = ordinal%2==0 ? buy : sell
price = side==buy ? 100000+ordinal%1000 : 99500+ordinal%1000
quantity = 1+ordinal%20
```

### 4.4 Infrastructure

Stable service pool = actual records from:

```text
infrastructure.transport_service
infrastructure.water_service
infrastructure.power_service
infrastructure.communication_service
infrastructure.facility_service
```

sorted `(partitionId ASCII, RecordId)`.

```text
requester = Resident(ordinal)
service_ref = servicePool[ordinal % servicePool.Count]
units = 1+ordinal%100
eligible_from = injectionStep+1
eligible_until = injectionStep+30
```

### 4.5 Governance

```text
incident_kind = perf.incident
subject_refs = [Resident(ordinal)]
scope_ref = TileScope(Qa04ReferenceLoadV1.RegionalTileIndex(subject_id))
fact_event_refs = [InfoClaim(ordinal)]
```

### 4.6 Environment

```text
hazard_kind = perf.synthetic-hazard
scope_ref = TileScope(ordinal%4096)
intensity_ppm = 100000 + ordinal%800001
duration_constraint_steps = 30
```

## 5. Operation identity / payload digest

Current descriptor-only digest is not release authority. Implementation must make:

```text
Qa04OperationDescriptorV1.PayloadDigest
  == digest(normalized immutable production Operation payload + scheduling admission)
  == operation_state.payload_digest after durable ACCEPTED
```

OperationId remains deterministic and stable across retry. Burst/steady generation affects quantity only; it must not alter immutable payload for the same OperationId.

The accepted-operation loss guard consumes this exact descriptor identity/digest set.

## 6. CrossDomainTransaction kind mapping

11 named benchmark kinds map to `transaction.<name>` exactly.

`other-registered-transactions` maps round-robin across the six standard kinds not otherwise represented:

```text
transaction.demolition
transaction.birth
transaction.death
transaction.disease-transmission
transaction.public-record
transaction.military-operation
```

Within any canonical descriptor batch, order by descriptor TransactionId then assign `otherOrdinal % 6`.

For the initial 10,000 mix, `other` count=200; kinds 0,1 receive34 each, kinds2..5 receive33 each.

## 7. Transaction participant binding

For a mapped kind:

- include every `RequiredDomains` domain;
- for each `RequiredAnyDomainGroups`, include the member with lowest standard domain rank;
- include every `OptionalDomains` domain;
- required/direct and chosen any-of participants use `required=true`;
- optional participants use `required=false`.

Canonical owner partition by domain:

```text
spatial                    -> spatial.scope_registry
environment                -> environment.hazard
physical_built             -> physical.presence
participation              -> participation.control_mode
resident                   -> resident.identity_lifecycle
society_economy            -> society.contract_claim
governance_security        -> governance.permission_license
infrastructure_information -> infrastructure.service_queue
```

Each participant points at an actual record selected by transaction slot ordinal modulo the target partition record count.

Intent ID:

```text
Trunc128(HashSuite.DomainHash(
  "mv.perf-reference-transaction-intent.v1",
  [transaction_id, domain_token, partition_id]))
```

Candidate effect digest:

```text
HashSuite.DomainHash(
  "mv.perf-reference-transaction-effect.v1",
  [transaction_id, basis_step, domain_token, partition_id, intent_id])
```

All initial participant outcomes are READY. This initial candidate effect is a reservation/preparation semantic and does not silently mutate Domain authority.

Required transaction invariants from the production registry are emitted as PASS in invariant-id canonical order for benchmark genesis/turnover fixtures.

## 8. Transaction turnover

Persistent authority follows `phase4-alpha11-core-effect-custody-v2.md`.

Initial active slots = exactly10,000.

```text
cohort = slot % 10
lifetime = 3000 Steps
initial terminal basis = 300*(cohort+1)
```

At each nonzero `S % 300 == 0`:

- terminalize due 1,000 ACTIVE transactions as COMMITTED in State(S+1);
- create 1,000 replacement ACTIVE transactions in the same State(S+1);
- keep active count exactly10,000.

Replacement preserves the slot's two benchmark subject IDs. Thus active-transaction spatial guard coverage is stable across turnover. New TransactionId is derived with root causality = prior TransactionId and stable local ordinal `slot + generation*10000`.

## 9. Detail transition interpretation

`Qa04ReferenceScenariosV1.DetailTransitionBatches(S)` fields mean **candidate budget workload**:

```text
promotion: 6 requests, total EstimatedRecordCount=30,000
  => 5,000/request
demotion: 10 requests, total EstimatedRecordCount=80,000
  => 8,000/request
```

`EstimatedRecordCount` is exactly the existing `DetailTransitionRequestV1.EstimatedRecordCount`: it is deterministic materialization cost/budget input. It is not a requirement that all estimated records commit in the injection Step.

Existing standard policy therefore remains authoritative:

```text
promotion hysteresis = 30 Steps
promotion max regions/Step = 4
promotion max records/Step = 20,000
demotion quiet/min residence = 300 Steps
demotion max regions/Step = 8
demotion max records/Step = 50,000
```

Planner deferral caused by these limits is expected benchmark behavior, not workload loss.

## 10. Detail region selection

A 27,000-Step run executes transitions for basis Steps `0..26,999`; nonzero cadence Steps are `300..26,700`, exactly89 cadence points.

Each cadence uses 16 distinct canonical tile detail regions. Across the full run this consumes `89*16=1,424 < 4,096`, so no region is reused and no pending target collides with a later request.

```text
cadence_ordinal = S/300 - 1           // 0..88
request_ordinal = cadence_ordinal*16 + local  // 0..1423
tile = (request_ordinal*4051 + 17) mod 4096
```

4051 is odd, therefore this is a permutation over modulo `2^12=4096`; first1,424 request ordinals are unique.

Use the actual canonical tile detail-region id and TileScope for that tile.

Local:

```text
0..5  = promotion
6..15 = demotion
```

## 11. Detail request fields

Promotion:

```text
CurrentLevel = D1
TargetLevel = D0
EstimatedRecordCount = 5000
```

Demotion:

```text
CurrentLevel = D0
TargetLevel = D1
EstimatedRecordCount = 8000
```

Common:

```text
RequiredEffectiveStep = S
SemanticPriority = 0
TriggerSource = ConfigPolicy
TriggerId = DetailTransitionTriggerAuthorityV1.ConfigPolicyTriggerId(active_config_generation,active_config_digest)
TriggerObservedStep = S
```

Domain mapping deliberately uses domains with standard D0/D1 representations in the reference world:

```text
promotion domains = [environment, resident]
  domain = promotionDomains[(cadence_ordinal + local) % 2]

demotion domains = [environment, physical_built]
  domain = demotionDomains[(cadence_ordinal + local) % 2]
```

Genesis canonical detail directory precomputes all1,424 `(tile,domain)` pairs. Because tile is never reused, each pair is unique. Promotion pairs initialize D1; demotion pairs initialize D0. Other tile/domain levels retain their normal profile value.

A DetailTransition request changes the region/domain authority level only when selected by the existing planner and conservation validation passes. Deferred/not-yet-eligible requests remain in `DetailDirectoryV1.PendingTransitions` exactly as current runtime specifies.

## 12. Detail conservation

For each selected region transition, domain materializer produces before/after `DetailConservationSnapshotV1` from actual authoritative material belonging to that tile/domain.

Required invariants:

- persistent identity exact set preserved;
- stock totals preserved;
- active obligations preserved;
- in-flight flows preserved;
- provenance refs preserved.

`EstimatedRecordCount` is not inserted into conservation state and cannot justify creating/deleting dummy records.

If the actual tile/domain cannot produce a semantically valid detail representation, transition fails/defer according to existing authority; benchmark must not fake a low-detail result.

## 13. Workload blocker removal

Design completion does not remove the three workload failure codes.

They are removed independently only when:

1. Operation descriptor -> immutable production Operation -> durable scheduler path is implemented and digest-equal;
2. transaction descriptor -> production candidate -> persistent ACTIVE/turnover path is implemented;
3. detail batch -> authoritative trigger/request/planner/pending/conservation path is implemented.

Full benchmark still must run the exact9,000 warm-up +18,000 measurement Steps with the resulting workload.
