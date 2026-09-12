# Alpha 1.1 Infrastructure remaining 89,900 authority audit

Status: **Audit complete / normative proposal pending**

Tracking: #303, #240, #265

## 1. Purpose

This document audits the remaining `perf.reference.v1` Infrastructure / Information benchmark material after the accepted topology, network-service, and ServiceQueue authority.

It does **not** assign new benchmark genesis semantics. Its purpose is to separate existing actual authority surfaces from decisions still required before implementation.

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

## 3. `infrastructure.dependency` — 20,000

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
- actual ServiceQueue records where semantically appropriate as a participant surface.

Decision required:

- exact consumer pool and provider pool;
- consumer/provider ordinal mapping;
- `dependency_kind` benchmark Token;
- `minimum_service_ppm`;
- whether `degradation_curve_ref` is `NONE` or references an actual authority;
- fallback mapping or canonical empty list;
- genesis `status`;
- a graph construction that satisfies the production cycle validator.

This slice is mechanically close to implementation because its likely target surfaces already exist, but **no canonical dependency graph is currently decided**.

## 4. `infrastructure.facility_service` — 15,000

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

- the exact semantic owner/target of `facility_ref` is not fixed for the benchmark package.
- Phase 3 separates physical facility existence from Infrastructure service operation, so choosing an arbitrary Physical/Built record merely because it is type-compatible is forbidden.

Decision required after facility target authority is selected:

- exact facility mapping;
- `service_kind`;
- capacity and genesis active load;
- required resource refs or canonical empty-list decision;
- availability and status.

This slice must not be bundled into another package until `facility_ref` ownership/mapping is explicitly decided.

## 5. `information.delivery` — 20,000

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

Existing surfaces that may be reused after an explicit mapping decision:

- actual Society `InformationClaim` authority for content;
- actual Resident identity authority for sender/recipient;
- actual CommunicationService authority for channel.

Decision required:

- exact content/sender/recipient/channel mapping;
- recipient cardinality;
- `eligible_step`;
- whether `delivered_step` is `NONE` or present;
- priority;
- lifecycle status;
- immutable `content_digest` derivation and its binding to the selected content authority.

A placeholder or unrelated hash is not acceptable as `content_digest` authority.

## 6. `information.media_distribution` — 5,000

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

Existing surfaces that may be reused after explicit mapping decisions:

- actual Society `InformationClaim`;
- actual Society `Organization`;
- actual CommunicationService;
- actual TileScope.

Decision required:

- claim/publisher/channel/audience mapping;
- channel and audience list cardinality/order;
- `published_step`;
- exact authoritative `reach_count` semantics at genesis;
- status.

The schema explicitly distinguishes exact authoritative delivered/reach count from a merely derived estimate, so an arbitrary load number cannot be inserted.

## 7. `information.record_store` — 10,000

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

Existing surfaces that could participate after an explicit decision include PublicAuthority, InformationClaim, Resident, Organization, and other actual world records.

Decision required:

- `record_kind`;
- whether `authority_ref` is `NONE` or an actual authority and its mapping;
- subject pool/list mapping;
- immutable content serialization/domain and `content_digest` derivation;
- version and created step;
- availability;
- whether `supersedes_ref` is `NONE` or an actual previous record.

The record carrier is not Core truth itself; therefore content/subject mappings must preserve the Phase 3 truth-separation boundary.

## 8. `information.address_place_index` — 5,000

Schema:

```text
place_ref: Ref
address_token: Token
scope_ref: Ref
valid_from: Step
valid_until: Step?
aliases: TokenList
```

Existing surface:

- actual TileScope authority can satisfy the spatial scope side after an explicit mapping decision.

Hard/semantic decision:

- exact owner and actual target pool for `place_ref`;
- normalized benchmark `address_token` vocabulary/derivation;
- place-to-scope mapping;
- validity range;
- aliases or canonical empty-list decision.

A TileScope cannot automatically be reused as both social place identity and spatial scope without an explicit normative decision.

## 9. `infrastructure.failure_recovery` — 10,000

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

Existing surfaces:

- actual topology records;
- actual network-service records;
- future actual dependency records, if this slice is ordered after `infrastructure.dependency`.

Decision required:

- subject class/pool and mapping;
- failure kind;
- severity;
- start step;
- recovery progress;
- optional expected restore step;
- dependency refs and whether this slice must wait for dependency authority;
- status.

The benchmark profile fixes a deterministic cascading outage scenario during runtime, but that does not by itself define 10,000 active genesis failure records.

## 10. `infrastructure.lineage` — 4,900

Schema:

```text
subject_ref: Ref
predecessor_refs: RefList
change_kind: Token
effective_step: Step
source_digest: Digest
```

Existing surfaces:

- actual topology, network-service, and ServiceQueue records;
- future actual records from the remaining Infrastructure slices.

Decision required:

- subject class and mapping;
- predecessor mapping or canonical empty-list semantics;
- `change_kind`;
- effective step;
- exact source material bound by `source_digest` and its canonical digest derivation.

A placeholder digest must not be used.

## 11. Dependency ordering

The audit establishes the following implementation dependency shape without deciding genesis values:

```text
already actual:
  topology v2
  network services
  ServiceQueue
  Resident / TileScope
  selected Society/Governance authority

possible after new semantic decision, no obvious missing target pool:
  infrastructure.dependency
  information.delivery
  information.media_distribution
  information.record_store

requires additional target-ownership decision:
  infrastructure.facility_service
  information.address_place_index

ordered after other remaining slices when their refs are used:
  infrastructure.failure_recovery
  infrastructure.lineage
```

This is an authority dependency graph, not an instruction to choose the listed candidate mappings.

## 12. Minimal-package conclusion

No additional remaining slice is implementation-ready from existing normative material alone.

The closest candidates are:

- `infrastructure.dependency` 20,000;
- `information.delivery` 20,000;
- `information.media_distribution` 5,000.

Their actual Ref surfaces largely exist, but all still require explicit benchmark-only mapping and genesis decisions. Therefore implementation must wait for a follow-up normative proposal/approval rather than deriving values from generic hashes or ordinal convenience.

`infrastructure.facility_service` is the strongest hard upstream blocker because `facility_ref` semantics must be fixed first.

## 13. Release impact

This audit changes no accepted counts or release flags.

Until all 89,900 records are actual production authority and production Snapshot/recovery semantic proof succeeds:

- Infrastructure accepted remains `410,100 / 500,000`;
- Infrastructure remaining remains `89,900`;
- the Infrastructure reference-world parent blocker remains active;
- `referenceWorldMaterialized` remains `false`.

## 14. Next normative work

A follow-up proposal should choose one dependency-closed package and explicitly fix:

- actual Ref mappings;
- benchmark-only Token vocabulary;
- numeric genesis values;
- Step values;
- optional `NONE` decisions;
- digest derivation domains/material;
- lifecycle/status values;
- any ordering/cycle invariants.

Only after that proposal is integrated through `documentation` and synchronized into `develop` may #265 implement the chosen package.
