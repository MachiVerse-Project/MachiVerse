# Phase 4 QA-04 canonical reference-world materialization audit

Status: Open implementation blocker audit  
Tracking: #240  
Implementation: PR #265  
Profile: `perf.reference.v1`

## Purpose

This note records the exact boundary between deterministic QA-04 input descriptors and production authoritative material. It does not redefine P4-05 payload semantics and must not be used to fabricate records merely to satisfy benchmark counts.

`Qa04ReferenceLoadV1` already fixes the eight initial-world class counts, deterministic identities, detail levels, 64x64 tile distribution, and dense-region selection. `Qa04ReferenceScenariosV1` additionally fixes transaction mix, market scope/order identities, infrastructure node/edge/request identities, and load selectors.

Those deterministic descriptors are not sufficient by themselves to claim `referenceWorldMaterialized=true`. Every class must map to actual production authority with complete P4-05 payloads and valid cross-partition reference closure.

## Current binding audit

| perf.reference.v1 class | count | current production binding | status |
|---|---:|---|---|
| `resident.persistent-identity` | 1,000,000 | `resident.identity_lifecycle` / `ResidentIdentityLifecyclePayloadV1` | production materializer available |
| `physical.d0-presence` | 500,000 | candidate `physical.presence` | blocked: `shape_ref` authority/target schema is not defined |
| `environment.d0-cell-cohort` | 1,000,000 | none | blocked: class-to-P4-05 partition/material split is not defined |
| `environment.d1-aggregate` | 250,000 | none | blocked: aggregate-to-P4-05 partition/material split is not defined |
| `society-governance.active-record` | 2,000,000 | none | blocked: allocation across Society/Governance authoritative partitions is not defined; P4-06 market load also lacks an authoritative target for `society.market_transaction.market_ref` |
| `infrastructure.active-record` | 500,000 | none | blocked: QA-04 defines node/edge/request identities, but no authoritative node/edge record partition exists in the 97-partition contract |
| `spatial.hot-terrain-brick` | 500,000 | candidate `spatial.terrain_geometry` root | blocked: `TerrainBrickV1` exists as SBO-SDF algorithm state, but no standard partition owns a brick record targeted by `root_brick_ref` |
| `transaction.active-cross-domain` | 10,000 | runtime transaction candidate only | blocked: `CrossDomainTransactionCandidateV1.IsAuthoritative` is explicitly false and no persistent active-transaction authority is defined |

The eight-class machine-readable mirror is `Qa04ReferenceWorldMaterialContractV1`. The finer-grained unresolved authority/mapping/nested-schema dependencies are machine-readable in `Qa04ReferenceWorldDependencyContractV1`; this includes the market reference target gap described below.

## Physical D0 blocker

P4-05 defines:

```text
physical.presence {
  subject_ref: Ref,
  frame_ref: Ref,
  position: Vec3,
  orientation: Quat,
  linear_velocity: Vec3,
  angular_rate_urad_s: Vec3,
  shape_ref: Ref,
  containment_ref?: Ref,
  presence_mode: Token
}
```

`subject_ref` can be closed against actual Resident or other owner identity records and `frame_ref` can in principle reference Spatial frame authority. `shape_ref` cannot currently be closed without inventing a target record. P4-04 lists standard collision geometry forms (`SphereV1`, `CapsuleV1`, `OrientedBoxV1`, `ConvexPolytopeV1`, `TriangleMeshStaticV1`) but the 97-partition P4-05 registry does not define an authoritative collision-shape record partition or an embedded shape payload for `physical.presence`.

Therefore the 500,000 presence target must remain unmaterialized until the authority/target contract is fixed. Pointing `shape_ref` at an unrelated Spatial or Physical record is prohibited.

## Terrain brick blocker

P4-04 normatively defines `TerrainBrickV1` and D0 spacing of 250 mm. The production type exists and enforces 729 SDF samples and 512 material cells.

P4-05, however, stores only:

```text
spatial.terrain_geometry {
  scope_ref: Ref,
  root_brick_ref: Ref,
  geometry_revision: uint64,
  ...
}
```

No standard partition owns the `TerrainBrickV1` record referenced by `root_brick_ref`. In addition, `perf.reference.v1` does not define canonical SDF/material sample content for the 500,000 hot bricks. A flat, random, or all-empty terrain fixture would therefore be an invented benchmark semantic and is not acceptable as canonical material.

## Environment aggregate blocker

The benchmark profile fixes aggregate counts and environment load percentages, while P4-05 owns 13 semantic partitions such as atmosphere, climate, weather, surface water, ecosystem, contaminant, and hazard. The profile does not specify whether one `environment.d0-cell-cohort` descriptor materializes one record, several coupled records, or which partitions receive the 1,000,000 count. The same ambiguity applies to the 250,000 D1 aggregate descriptors.

Counts must not be assigned to an arbitrary convenient partition.

## Society / Governance blocker

The 2,000,000 active-record target spans two owners with 33 total partitions. P4-06 separately defines a market load of 100 scopes and 10,000 active orders per scope, and QA-04 derives deterministic market scope/order IDs, but the initial-world 2,000,000 aggregate class is not normatively decomposed into market transactions, organizations, accounts, contracts, laws, jurisdictions, or other owner records.

A benchmark implementation must define that decomposition and all required Ref targets before materialization can be considered canonical.

### Market reference authority sub-blocker

Phase 3 defines a distinct conceptual `MarketState` with market identity/scope, tradable class, participant access, demand/supply summary, transaction refs, price state, and activity state. P4-06 then fixes 100 deterministic market scopes and 10,000 active orders per scope.

P4-05, however, exposes `society.market_transaction` with a required `market_ref: Ref` but the 97 standard partitions do not include an authoritative market-state record partition that this reference can target. `society.organization`, `society.contract_claim`, or another convenient record cannot be substituted without changing the domain semantics.

Therefore the one-million-order market load cannot be promoted to canonical reference-world material merely from the existing deterministic market/order IDs. The missing `market_ref` target authority must be specified first. This dependency is fixed in code as `qa04.material.market-ref-authority-undefined`.

## Infrastructure blocker

P4-06 fixes 20,000 network nodes, 100,000 stable edges, and 250,000 queued service requests. QA-04 already derives stable IDs for all three groups. P4-05 has `infrastructure.network_topology` with `node_refs` and `edge_refs`, and `infrastructure.service_queue` for service requests, but the standard 14 Infrastructure/Information partitions do not define authoritative node/edge payload records that those refs can target.

The missing node/edge authority must be resolved before the 500,000 active-record class can be closed honestly.

## Cross-domain transaction blocker

QA-04 derives 10,000 deterministic active transaction descriptors and their profile mix. The current runtime type `CrossDomainTransactionCandidateV1` is a Step candidate and explicitly reports `IsAuthoritative == false`.

Snapshot material cannot persist those candidates as if they were world authority. The reference profile needs an authoritative active-transaction state owner or an explicit rule proving that the 10,000 target belongs to another durable authority already represented in the 103 sections.

## Nested payload blockers retained

The Stage 2 audit also rechecked:

- `resident.body_health.body_region_states : ordered list<BodyRegionStateV1>`;
- `resident.perception.perceived_facts : ordered list<PerceivedFactV1>`;
- `governance.law_rule.rule_ast : RuleAst`.

P4-05 names these nested values and gives their top-level canonical ordering, while Phase 3 gives conceptual health/perception/legal state only. Exact nested field schemas are not defined. Existing Snapshot codecs therefore continue to fail closed for non-empty values rather than inferring CLR fields, JSON, or ad-hoc protobuf layouts.

## Completion rule

`referenceWorldMaterialized=true` is permitted only after all eight initial-world classes have production materializers that:

1. create actual authoritative records, not headers/count placeholders;
2. use the exact owner payload/schema contract;
3. close every required `PartitionRecordRefV1` against actual material in the same world;
4. produce canonical partition/state digests from that material;
5. participate in the actual 103-section production Snapshot -> staging -> recovery -> semantic rehash proof.

The reduced infrastructure canaries may use genuinely typed empty runtime roots where the actual reduced world is empty, but those empties never count as material for a non-empty `perf.reference.v1` target class.

Until then PR #265 remains Draft and Stage 2 remains open.
