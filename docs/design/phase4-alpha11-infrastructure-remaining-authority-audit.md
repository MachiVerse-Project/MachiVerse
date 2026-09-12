# Alpha 1.1 Infrastructure dependency正本仕様・残authority監査

Status: **Complete / normative benchmark authority（dependency 20,000のみ）; 他69,900は未承認の監査**

Tracking: #240, #265

## 1. Purpose

This document audits the remaining `perf.reference.v1` Infrastructure / Information benchmark material after the accepted topology, network-service, and ServiceQueue authority.

[Issue #240の2026-09-12承認決定](https://github.com/MachiVerse-Project/MachiVerse/issues/240#issuecomment-5644534723)により、§5のdependency 20,000 recommended packageだけをbenchmark正本仕様として採択した。§6〜12の他69,900は未承認の監査であり、FacilityServiceのfacility identityやInformationDelivery等のmapping / Token / digest / genesisを確定しない。

Current post-proof decomposition:

```text
accepted topology v2                 120,100
accepted network services             40,000
accepted ServiceQueue                250,000
--------------------------------------------
accepted                              410,100 / 500,000
remaining                              89,900
```

Remaining slices:

```text
infrastructure.dependency             20,000
infrastructure.facility_service       15,000
information.delivery                  20,000
information.media_distribution         5,000
information.record_store              10,000
information.address_place_index        5,000
infrastructure.failure_recovery       10,000
infrastructure.lineage                 4,900
--------------------------------------------
total                                 89,900
```

## 2. Audit classification

Each field is classified as one of:

- **existing surface**: an actual authority pool exists that could satisfy the field type, but an exact benchmark mapping may still require a decision;
- **decision required**: benchmark mapping, Token, scalar, Step, digest, lifecycle state, or exact target semantics are not fixed;
- **hard upstream authority**: a semantically valid actual target pool is not yet selected/available and must be resolved first;
- **optional decision**: the field is optional, but `NONE` versus an actual reference is itself benchmark authority and cannot be inferred.

Existence of a type-compatible Ref target is never treated as an approved mapping.

## 3. Existing authority and runtime constraints relevant to the next package

The accepted network-service package already provides actual production authority for:

```text
TransportService       10,000
WaterService           10,000
PowerService           10,000
CommunicationService   10,000
```

All four pools use stable canonical ordinals `0..9,999`. Accepted genesis has service `status = active`; Water, Power, and Communication use `availability_ppm = 1,000,000`.

Phase 3 explicitly lists dependency examples including:

- water pump -> electric power;
- communication site -> electric power.

Runtime `InfrastructureDependencyV1` and `DeterministicOutageCascadeV1` already fix important graph semantics:

- dependency endpoints must be non-zero;
- self-dependency is invalid;
- outage propagation is directed from upstream provider to downstream dependent subject;
- dependency graphs must be acyclic unless a separate coupled-resolution policy exists;
- the existing deterministic cascade rejects cycles with `infrastructure.dependency-cycle-requires-coupled-policy`.

These facts constrain a benchmark dependency proposal, but they do not by themselves decide the persistent payload Token/scalar/optional fields.

## 4. `infrastructure.dependency` — 20,000

Schema:

```text
consumer_ref: Ref
provider_ref: Ref
dependency_kind: Token
minimum_service_ppm: Ratio
degradation_curve_ref: Ref?
fallback_refs: RefList
status: Token
```

Existing surfaces:

- actual topology v2 records;
- actual Transport / Water / Power / Communication service records;
- actual ServiceQueue records where semantically appropriate.

Ref closureを構成できる§5のexact valuesは#240で承認済み。一般世界の普遍仕様ではなくbenchmark fixtureに限定する。

## 5. 承認済みdependency package — normative benchmark authority

本節は#240承認済みの正本仕様である。documentation統合→develop同期の完了後、既存#265へ実装する。

### 5.1 Cardinality and identity

```text
population = 20,000
local ordinal d = 0..19,999
```

承認済みrecord identityは次のとおり。It follows the already-used QA-04 benchmark authority pattern:

```text
DerivedIdentity.DeriveEntityId(
  world_id       = perf.reference.v1 WorldId,
  creation_step  = 0,
  creator_domain = infrastructure_information,
  creator_entity = ZERO,
  creation_kind  = perf.infrastructure-dependency,
  local_ordinal  = d)
```

The dependency record MUST have its own identity; service RecordIds are not reused as dependency RecordIds.

### 5.2 Consumer/provider mapping

For `d = 0..9,999`:

```text
consumer_ref = actual WaterService[d]
provider_ref = actual PowerService[d]
```

For `d = 10,000..19,999`, let `i = d - 10,000`:

```text
consumer_ref = actual CommunicationService[i]
provider_ref = actual PowerService[i]
```

Consequences:

- exactly 10,000 Water -> Power dependencies;
- exactly 10,000 Communication -> Power dependencies;
- each Water and Communication service has exactly one benchmark dependency;
- each Power service is provider for exactly two dependencies;
- Power records are provider-only in this package, so the graph is a strict two-layer DAG;
- no self-edge can be created because consumer and provider are records from different partitions;
- the mapping is independent of dictionary/filesystem/task enumeration order.

The persistent fields are consumer/provider oriented, while runtime outage propagation is upstream/downstream oriented. Runtime binding MUST therefore map:

```text
upstream   = provider_ref
downstream = consumer_ref
```

and not reverse the edge.

### 5.3 承認済みbenchmark genesis payload

承認済みexact values:

```text
dependency_kind       = perf.power-supply
minimum_service_ppm   = 1,000,000
degradation_curve_ref = NONE
fallback_refs          = []
status                 = active
```

Rationale:

- `perf.power-supply` makes the benchmark-only nature explicit and matches the Phase 3 power-dependency examples without creating a general dependency taxonomy;
- accepted PowerService genesis is `availability_ppm = 1,000,000`, so `minimum_service_ppm = 1,000,000` makes the initial state internally satisfied;
- runtime outage cascade is currently binary failure propagation rather than a general degradation-curve evaluator, so no synthetic degradation authority is invented;
- no accepted fallback service authority exists for these relations, so the approved fixture uses an explicit empty fallback list rather than fake refs;
- all participating services are accepted as `active` at benchmark genesis, so `active` is the approved dependency status.

`minimum_service_ppm = 1,000,000` is a benchmark fixture threshold only. It MUST NOT be generalized into a claim that real water or communication systems universally require 100% nominal electrical service.

### 5.4 承認済みEnvelope

To avoid inventing a new detail distribution, approved envelope detail is the actual `consumer_ref` record's DetailLevel.

Other genesis envelope fields follow the standard initial-authority pattern:

```text
revision      = 1
created_step  = 0
retired_step  = NONE
lineage_ref   = NONE
```

Mirroring the consumer envelope is preferred over forcing all 20,000 dependency records to D0 because the latter would add an unrelated entity-exact detail policy.

### 5.5 必須production proof

`documentation -> develop`統合後、#265で少なくとも以下を実証する:

1. exact 20,000 record materialization;
2. actual Water / Communication / Power Ref closure;
3. exact 10,000 + 10,000 mapping distribution;
4. no duplicate dependency RecordId;
5. no self-edge;
6. provider->consumer runtime orientation;
7. full graph acyclic validation through production runtime logic;
8. exact Token/scalar/optional-field validation;
9. production Snapshot encode / recovery / semantic rehash for all 20,000 records;
10. negative tests for reversed refs, missing service, wrong service partition, duplicate identity, self-edge, cycle, invalid Ratio/Token, and genesis drift;
11. current-head full CI.

Only after this proof may Infrastructure accepted material move from `410,100` to `430,100`; the Infrastructure parent blocker remains active because `69,900` records would still remain.

## 6. `infrastructure.facility_service` — 15,000

Schema:

```text
facility_ref: Ref
service_kind: Token
capacity_per_step: uint32
active_load: uint32
required_resource_refs: RefList
availability_ppm: Ratio
status: Token
```

### 6.1 Confirmed design boundary

Phase 3 separates **physical facility existence** from **Infrastructure service operation/capacity**. `FacilityServiceState` represents services such as medical, administrative, retail, education, transport-terminal, and entertainment service, while effective capacity can depend on staff, equipment, rooms/beds/seats/desks/machines, utilities, schedule, and failures.

Physical/Built separately owns actual structures/spaces/equipment. `BuiltStructure` includes buildings, bridges, utility physical structures, etc., and explicitly leaves service/network meaning to Infrastructure.

The already-accepted Infrastructure service package (#301) intentionally excluded all 15,000 `infrastructure.facility_service` records because **physical facility authority was separately required**. This is therefore an existing normative gate, not a newly introduced implementation preference.

### 6.2 Current QA-04 physical authority is not a facility pool

The current canonical QA-04 Physical class is `physical.d0-presence` with 500,000 descriptors. Its production materializer creates:

```text
physical.presence
physical occupancy state
collision shape
```

for each descriptor.

That proves actual D0 physical presence/shape authority, but it does **not** materialize a canonical `built.structure` population or otherwise classify a subset as semantic facilities.

Therefore the following shortcuts are forbidden:

- use arbitrary `physical.presence` records as `facility_ref` merely because the field type is `Ref`;
- use occupancy/collision-shape records as facilities;
- use TileScope as facility identity;
- infer that the first 15,000 Physical records are buildings/facilities;
- derive a facility mapping from hash/order without an owning normative rule.

A type-compatible non-zero Ref is not sufficient; `facility_ref` must point to an actual record that semantically owns the facility identity used by the benchmark.

### 6.3 Hard upstream authority still required

Before `facility_service` can become decision-ready, a normative upstream decision must establish at least:

1. which existing/new canonical partition owns benchmark facility identity (for example, an explicitly materialized Built authority if that is adopted; this audit does **not** choose it);
2. exact facility population available to the 15,000 service records;
3. deterministic facility-to-service mapping and reuse/cardinality rules;
4. the relation between facility identity and existing Physical presence/shape/space authority, if any;
5. whether every facility-service target must have physical capacity/equipment evidence at genesis.

Only after that upstream authority exists can this document safely propose:

- `service_kind` vocabulary/mapping;
- `capacity_per_step`;
- `active_load`;
- `required_resource_refs` or an explicit canonical empty-list rule;
- `availability_ppm`;
- `status`;
- facility-service RecordId/envelope authority.

No values for those fields are proposed here.

### 6.4 Workload impact

The normative Alpha 1.1 `infrastructure-service-delivery` Operation uses a stable service pool containing:

```text
transport_service
water_service
power_service
communication_service
facility_service
```

The first four pools are actual authority; `facility_service` is not. Consequently Operation authority is now **5 / 6**, and `infrastructure-service-delivery` must remain fail-closed until FacilityService authority and its production binding are real.

This workload dependency does not justify inventing facility world semantics merely to reach 6 / 6.

## 7. `information.delivery` — 20,000

Schema:

```text
content_ref: Ref
sender_ref: Ref
recipient_refs: RefList
channel_ref: Ref
eligible_step: Step
delivered_step: Step?
priority: int32
status: Token
content_digest: Digest
```

Existing surfaces:

- actual Society InformationClaim authority;
- actual Resident identity authority;
- actual CommunicationService authority.

Runtime already defines `InformationDeliveryStatusV1` with `Queued`, `Delivered`, and `Failed`, and only permits `Queued -> Delivered` through `MarkDelivered`.

That runtime authority narrows lifecycle vocabulary semantics, but persistent StableToken spelling, exact mappings, delivered-step policy, and immutable `content_digest` derivation are still separate normative decisions. No placeholder digest is acceptable.

## 8. `information.media_distribution` — 5,000

Schema:

```text
claim_ref: Ref
publisher_ref: Ref
channel_refs: RefList
audience_scope_refs: RefList
published_step: Step
reach_count: uint64
status: Token
```

Existing surfaces include InformationClaim, Organization, CommunicationService, and TileScope. Exact mapping, list cardinality/order, published step, authoritative reach semantics, and status remain undecided.

## 9. `information.record_store` — 10,000

Schema:

```text
record_kind: Token
authority_ref: Ref?
subject_refs: RefList
content_digest: Digest
version: uint32
created_step: Step
available: bool
supersedes_ref: Ref?
```

PublicAuthority, InformationClaim, Resident, Organization, and other actual records may be type-compatible surfaces, but truth-separation semantics, record kind, authority/subject mapping, immutable digest material, version, availability, and supersedes policy remain undecided.

## 10. `information.address_place_index` — 5,000

Schema:

```text
place_ref: Ref
address_token: Token
scope_ref: Ref
valid_from: Step
valid_until: Step?
aliases: TokenList
```

TileScope authority can satisfy the spatial scope side only. The semantic owner/target of `place_ref`, address-token derivation, place-to-scope mapping, validity, and aliases still require explicit authority. TileScope MUST NOT automatically be reused as social place identity.

## 11. `infrastructure.failure_recovery` — 10,000

Schema:

```text
subject_ref: Ref
failure_kind: Token
severity_ppm: Ratio
started_step: Step
recovery_progress_ppm: Ratio
expected_restore_step: Step?
dependency_refs: RefList
status: Token
```

Actual topology/network-service records already exist. If failure records reference dependency records, this slice should be ordered after the dependency package. The runtime benchmark outage scenario does not by itself define 10,000 active genesis failure records.

## 12. `infrastructure.lineage` — 4,900

Schema:

```text
subject_ref: Ref
predecessor_refs: RefList
change_kind: Token
effective_step: Step
source_digest: Digest
```

Existing Infrastructure records can supply subjects, but predecessor semantics, change kind, effective step, and exact source material/digest derivation remain normative decisions. Placeholder digests are forbidden.

## 13. Dependency ordering

Updated implementation dependency shape:

```text
already actual:
  topology v2
  network services
  ServiceQueue
  Resident / TileScope
  selected Society/Governance authority
  Physical D0 presence / occupancy / shape

approved package; production proof pending:
  infrastructure.dependency 20,000

possible after separate semantic decisions:
  information.delivery
  information.media_distribution
  information.record_store

hard upstream facility-identity authority first:
  canonical physical/built facility identity
    -> infrastructure.facility_service 15,000
    -> infrastructure-service-delivery Operation binding

requires additional target-ownership decision:
  information.address_place_index

ordered after dependency/other remaining authority when those refs are used:
  infrastructure.failure_recovery
  infrastructure.lineage
```

dependencyは#240で承認済み。この順序はfacility identity ownerや他sliceを採択するものではない。

## 14. Release impact

本正本化はaccepted countやrelease flagsを変更しない。

Until production proof succeeds:

- Infrastructure accepted remains `410,100 / 500,000`;
- Infrastructure remaining remains `89,900`;
- Infrastructure parent reference-world blocker remains active;
- Operation authority remains `5 / 6` while FacilityService is unavailable;
- workload Operation parent blocker remains active;
- `referenceWorldMaterialized` remains `false`;
- `authoritativeStepLoopAvailable` remains `false`.

承認済み20,000 dependencyを統合・実装しproduction proofが成功した場合にのみ、実績値を次へ更新できる:

```text
Infrastructure accepted  = 430,100 / 500,000
Infrastructure remaining =  69,900
```

The parent blocker still remains active at that intermediate point.

## 15. 承認範囲と統合境界

#240の承認は§5の10k Water + 10k Communication、actual PowerServiceへのordinal mapping、provider→consumer、exact Token / Ratio / optional fields / status、identity recipe、consumer DetailLevel mirror、production proof要件に限定する。

FacilityService / InformationDelivery / MediaDistribution / RecordStore / AddressPlaceIndex / FailureRecovery / Lineageは未承認である。型互換Refやruntimeの例からworld semanticsを推測しない。特にFacilityServiceのidentity owner/mappingは別途決定が必要である。

#304をdocumentationへ統合し、developへPR同期した後に#265でdependencyを実装する。production proof前にaccepted countを加算せず、Infrastructure parent blockerと最後のOperation familyのfail-closedを維持する。
