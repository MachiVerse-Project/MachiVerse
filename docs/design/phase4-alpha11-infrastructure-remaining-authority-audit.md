# Alpha 1.1 Infrastructure remaining 89,900 authority audit

Status: **Audit complete / normative proposal pending**

Tracking: #303, #240, #265

## 1. Purpose

This document audits the remaining `perf.reference.v1` Infrastructure / Information benchmark material after the accepted topology, network-service, and ServiceQueue authority.

It does **not** assign new benchmark genesis semantics. Its purpose is to separate existing actual authority surfaces from decisions still required before implementation and to make the smallest dependency-closed next package reviewable without treating the proposal as approval.

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

The Ref side is now sufficiently closed to formulate one concrete benchmark-only proposal. Exact values remain review-only until explicit approval.

## 5. Recommended dependency package — review only

This subsection is a **proposal, not normative authority**.

### 5.1 Cardinality and identity

```text
population = 20,000
local ordinal d = 0..19,999
```

Recommended record identity follows the already-used QA-04 benchmark authority pattern:

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

### 5.3 Proposed benchmark genesis payload

Recommended exact values:

```text
dependency_kind      = perf.power-supply
minimum_service_ppm  = 1,000,000
degradation_curve_ref = NONE
fallback_refs         = []
status                = active
```

Rationale:

- `perf.power-supply` makes the benchmark-only nature explicit and matches the Phase 3 power-dependency examples without creating a general dependency taxonomy;
- accepted PowerService genesis is `availability_ppm = 1,000,000`, so `minimum_service_ppm = 1,000,000` makes the initial state internally satisfied;
- runtime outage cascade is currently binary failure propagation rather than a general degradation-curve evaluator, so no synthetic degradation authority is invented;
- no accepted fallback service authority exists for these relations, so the candidate uses an explicit empty fallback list rather than fake refs;
- all participating services are accepted as `active` at benchmark genesis, so `active` is the proposed dependency status.

`minimum_service_ppm = 1,000,000` is a benchmark fixture threshold only. It MUST NOT be generalized into a claim that real water or communication systems universally require 100% nominal electrical service.

### 5.4 Envelope proposal

To avoid inventing a new detail distribution, recommended envelope detail is the actual `consumer_ref` record's DetailLevel.

Other genesis envelope fields follow the standard initial-authority pattern:

```text
revision      = 1
created_step  = 0
retired_step  = NONE
lineage_ref   = NONE
```

Mirroring the consumer envelope is preferred over forcing all 20,000 dependency records to D0 because the latter would add an unrelated entity-exact detail policy.

### 5.5 Required production proof after approval

After normative approval and `documentation -> develop` integration, #265 would need to prove at minimum:

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

Hard upstream authority:

- the exact semantic owner/target of `facility_ref` is not fixed for the benchmark package;
- Phase 3 separates physical facility existence from Infrastructure service operation, so choosing an arbitrary Physical/Built record merely because it is type-compatible is forbidden.

Decision required after facility target authority is selected:

- exact facility mapping;
- `service_kind`;
- capacity and genesis active load;
- required resource refs or canonical empty-list decision;
- availability and status.

This slice must not be bundled into the dependency package.

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

first decision-ready proposal:
  infrastructure.dependency 20,000

possible after separate semantic decisions:
  information.delivery
  information.media_distribution
  information.record_store

requires additional target-ownership decision:
  infrastructure.facility_service
  information.address_place_index

ordered after dependency/other remaining authority when those refs are used:
  infrastructure.failure_recovery
  infrastructure.lineage
```

This ordering does not approve the dependency proposal.

## 14. Release impact

This audit/proposal changes no accepted counts or release flags.

Until production proof succeeds:

- Infrastructure accepted remains `410,100 / 500,000`;
- Infrastructure remaining remains `89,900`;
- Infrastructure parent reference-world blocker remains active;
- `referenceWorldMaterialized` remains `false`.

If and only if the 20,000 dependency proposal is approved, integrated, implemented, and production-proven, the intermediate accounting may become:

```text
Infrastructure accepted  = 430,100 / 500,000
Infrastructure remaining =  69,900
```

The parent blocker still remains active at that intermediate point.

## 15. Approval boundary

The recommended dependency package is intentionally **review only**.

A semantic approval should explicitly adopt or replace:

1. the 10k Water + 10k Communication consumer split;
2. PowerService as provider pool;
3. ordinal one-to-one mapping;
4. provider->consumer runtime orientation;
5. `perf.power-supply` Token;
6. `minimum_service_ppm = 1,000,000`;
7. `degradation_curve_ref = NONE`;
8. `fallback_refs = []`;
9. `status = active`;
10. record identity recipe;
11. consumer DetailLevel mirror;
12. production proof requirements.

Before explicit approval:

- do not mark this document complete normative authority;
- do not merge #304 as an authority decision;
- do not implement these values in #265;
- do not increase accepted Infrastructure count;
- do not change release flags.
