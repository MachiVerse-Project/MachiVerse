# Phase 4 `spatial.terrain_geometry` v2 production migration

Status: Decided / migration-aware exact-97 path implemented / canonical 508,192-record production proof complete  
Tracking: #240  
Implementation: Draft PR #265  
Record schema migration: `domain.spatial.terrain_geometry.record / 1.0 -> 2.0`

## 1. Purpose

This document defines the production Snapshot/recovery activation boundary for the exact Terrain v2 record schema frozen in `phase4-terrain-geometry-record-v2.md`.

The migration must not weaken the standard 97-partition contract, accept arbitrary future schema versions, rewrite actual recovered record schemas back to v1, or mutate the global standard registry baseline.

The standard partition identity remains:

```text
partition_id      = spatial.terrain_geometry
owner_domain      = spatial
partition_schema  = domain.spatial.terrain_geometry / 1.0
```

Only the record schema is migration-aware:

```text
source = domain.spatial.terrain_geometry.record / 1.0
target = domain.spatial.terrain_geometry.record / 2.0
```

The partition schema is unchanged because `PartitionStateHeaderV1` identifies the partition schema, while its canonical digest commits each record schema/version and semantic payload digest.

## 2. Explicit migration registry

`StandardDomainRecordSchemaMigrationRegistryV1` remains the only WorldState-level permission source for a standard record-schema migration.

Its Terrain entry is exactly:

```text
spatial.terrain_geometry: 1.0 -> 2.0
```

Allowed record schemas for this partition are exactly 1.0 and 2.0. Terrain 2.1, Terrain 3.0, or unrelated v2 record families remain invalid.

A migration registration changes only the permitted record schema. `partition_id`, owner, owner rank, partition schema, primary-key kind, persistence class, and canonical-order kind must still equal the standard identity exactly.

`StandardDomainPartitionRegistry` remains the v1 baseline.

## 3. Authority boundary

The existing materialized `SpatialTerrainGeometrySnapshotAuthorityV2` remains supported for migration canaries and small materialized authorities.

The canonical `perf.reference.v1` production path additionally uses `SpatialTerrainGeometryStreamingSnapshotAuthorityV2`, which:

- binds **508,192** canonical Terrain records;
- uses lightweight locators for 500,000 hot D0 bricks + 4,096 roots + 4,096 D3 anchors;
- generates record payloads on demand rather than retaining a full Terrain partition state;
- exposes canonical RecordId order without duplicating payload storage;
- streams canonical record envelopes through bounded-memory partition-header hashing;
- checks each generated RecordId against the expected locator RecordId;
- preserves Terrain record schema 2.0 and the standard partition identity.

`DomainPartitionSnapshotAuthoritySetV1` accepts the migrated authority only through the explicit migration registry. No generic v2 relaxation is introduced.

## 4. Production provider / section composition

The existing materialized provider remains valid for materialized Terrain v2 authorities.

Full canonical Terrain uses the parallel streaming production path:

```text
Domain 96 non-Terrain sections -> existing production providers
Terrain 1 section              -> SpatialTerrainGeometryStreamingFragmentSourceV2
Core 6 sections                -> existing canonical Core material
```

The resulting Snapshot still contains exactly:

```text
6 Core + 97 Domain = 103 sections
```

Terrain fragment generation is two-pass and bounded-memory:

1. determine canonical fragment boundaries from record wire lengths;
2. regenerate only the records required for the current fragment and emit the existing Terrain v2 fragment wire.

Canonical wire/schema/hash domains are unchanged by streaming.

## 5. Production all-97 reference resolver

The production reference resolver preserves the actual Terrain v2 record schema.

For the full canonical path, the compact resolver indexes identity/schema material without retaining every Terrain payload. Terrain root references are validated against actual canonical root/brick identities.

The rule remains:

```text
actual target schema must be exactly standard OR exactly a registered migration target
```

No unknown-version fallback is permitted.

## 6. Cross-partition Ref validation

Terrain production validation closes:

- root `scope_ref` against canonical `spatial.scope_registry` TileScope records;
- root `root_brick_ref` against actual D3 anchor Terrain brick identities;
- root N/E/S/W connectivity against actual Terrain root identities;
- recovered record kinds for root and brick arms.

The same staged Snapshot is used to recover the Scope Registry identity set used by Terrain semantic verification; pre-Snapshot in-memory authority is not substituted for recovered authority.

## 7. Recovery Phase 1

The full staged path reads actual `MVCHNK01` chunks, decompresses Zstd payloads, and validates section/fragment order across chunk boundaries.

Terrain Phase 1 builds only compact recovered material:

- canonical RecordId array;
- recovered record-kind array;
- root-only closure relations.

Large 729-SDF / 512-material brick payloads are not retained after the current fragment is processed.

## 8. Recovery Phase 2

Terrain Phase 2 re-reads the staged Snapshot and performs semantic verification without rebuilding a full `SpatialTerrainGeometryPartitionStateV2`.

It:

1. decodes canonical Terrain v2 record fragments;
2. validates schema/order/range/count metadata;
3. validates recovered Scope/root/brick references against Phase-1 identities;
4. streams semantic record material into the canonical partition digest;
5. requires equality with the frozen Terrain partition header/digest.

This preserves the two-phase recovery rule while bounding retained memory.

## 9. Exact-97 migration canary

The existing reduced migration canary remains useful and intentionally non-benchmark. It proves schema migration/provider selection mechanics independently from the full canonical Terrain population.

It must not be treated as release evidence and is not the reason the Terrain parent blocker is released.

## 10. Full canonical production proof

The dedicated `INT-03 full Terrain production validation` workflow proves the current production boundary with:

```text
hot D0 bricks = 500,000
roots         =   4,096
D3 anchors    =   4,096
Terrain total = 508,192
```

The proof covers:

1. full streaming Terrain authority/header construction;
2. exact-103 streaming composition;
3. actual Zstd `MVCHNK01` chunk staging;
4. durable `manifest.pb` materialization/readback;
5. same-staged-Snapshot two-pass recovery;
6. all 508,192 recovered RecordIds matching the frozen authority one-for-one;
7. root/anchor kind and Ref closure;
8. post-recovery Terrain semantic rehash equality.

The proof is Terrain-specific production evidence. It is stronger than preflight/reduced smoke, but it is not the full reference-world release evidence because other world classes remain unresolved.

## 11. Fail-closed invariants

The migration/streaming path must continue rejecting at least:

- unregistered record-schema versions;
- record-schema migration with any other partition-identity field changed;
- migrated authority with a frozen-header mismatch;
- generated RecordId differing from its canonical locator identity;
- migrated provider/source unavailable for a claimed target schema;
- resolver source whose schema is neither standard nor registered;
- Terrain fragment schema/order/range/count violations;
- recovered root whose `scope_ref`, `root_brick_ref`, or connectivity target is missing/wrong-kind;
- semantic partition rehash mismatch.

No permissive unknown-field/schema-version fallback is introduced.

## 12. Current release state

The former compatibility blocker:

```text
qa04.material.terrain-brick-authority-undefined
```

is no longer active after the successful full 508,192-record production Snapshot/recovery proof.

The following still remain outside Terrain scope:

- Society/Governance full 2,000,000 canonical production material / Ref closure;
- Infrastructure full 500,000 canonical production material / Ref closure;
- full authoritative QA-04 workload bindings / 27,000-Step execution;
- 12-run performance/determinism release evidence;
- 24-hour soak evidence.

Accordingly:

```text
referenceWorldMaterialized = false
authoritativeStepLoopAvailable = false
```

remain required until those independent gates are complete.
