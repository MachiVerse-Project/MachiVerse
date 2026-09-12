# Phase 4 `spatial.terrain_geometry` record schema v2

Status: Decided / production activation complete / full Terrain proof complete  
Tracking: #240  
Implementation: Draft PR #265  
Applies to: `domain.spatial.terrain_geometry.record` **2.0**

## 1. Purpose

`spatial.terrain_geometry` v1 contains the P4-05 terrain-root record, while P4-04 defines authoritative `TerrainBrickV1` SBO-SDF state. `perf.reference.v1` requires 500,000 hot terrain bricks and the root contains a required `root_brick_ref`, so root and brick records share the existing Spatial partition without introducing a 98th Domain partition.

This document freezes the exact v2 record contract. The v2 path is now active in the QA-04 production Snapshot composition through explicit migration-aware provider/recovery selection; the standard global registry remains the v1 baseline.

## 2. Schema identity

```text
v1 = domain.spatial.terrain_geometry.record / 1.0
v2 = domain.spatial.terrain_geometry.record / 2.0
partition schema = domain.spatial.terrain_geometry / 1.0
partition id = spatial.terrain_geometry
```

Only the record schema advances. `partition_id`, owner, primary-key kind, persistence class, canonical-order kind, and partition schema remain unchanged.

`StandardDomainPartitionRegistry` remains at record schema `1.0`. v2 acceptance is explicit through the registered record-schema migration path; arbitrary 2.x/3.x versions are not accepted.

## 3. Common record envelope

Both v2 arms use the standard Domain record envelope:

```text
record_id
record_schema = domain.spatial.terrain_geometry.record / 2.0
revision
created_step
retired_step?
detail_level
lineage_ref?
payload
```

For `terrain_brick`:

```text
TerrainBrickV1.brick_id -> record_id
TerrainBrickV1.revision -> revision
```

For migrated v1 `terrain_root`, all common envelope fields are preserved exactly and only the record schema advances from 1.0 to 2.0.

## 4. Record kinds

Canonical arms:

```text
terrain_brick
terrain_root
```

Unknown arms fail closed.

## 5. `terrain_root` arm

Exact payload fields:

| ordinal | field | type | optional |
|---:|---|---|---|
| 1 | `record_kind` | Token | no |
| 2 | `scope_ref` | Ref | no |
| 3 | `root_brick_ref` | Ref | no |
| 4 | `geometry_revision` | UInt64 | no |
| 5 | `surface_classes` | TokenList | no |
| 6 | `connectivity_refs` | RefList | no |
| 7 | `archive_anchor` | Digest(32) | yes |

`record_kind = terrain_root`。

`root_brick_ref.partition_id` must be `spatial.terrain_geometry` and the actual target record must use the `terrain_brick` arm.

`scope_ref` closes to actual `spatial.scope_registry` authority. QA-04 canonical roots use actual TileScope records.

## 6. `terrain_brick` arm

Exact payload fields:

| ordinal | field | type | optional |
|---:|---|---|---|
| 1 | `record_kind` | Token | no |
| 2 | `level` | UInt8 | no |
| 3 | `cell_origin` | SpatialCellKeyV1 | no |
| 4 | `sample_spacing_mm` | UInt32 | no |
| 5 | `sdf_mm` | fixed Int32 list, exactly 729 | no |
| 6 | `surface_material_id` | fixed UInt16 list, exactly 512 | no |

`record_kind = terrain_brick`。

### 6.1 `SpatialCellKeyV1`

```text
level:uint8
x:sint32
y:sint32
z:sint32
```

All fields are encoded explicitly. `TerrainBrickV1.level` and `cell_origin.level` remain independent authoritative values.

### 6.2 Fixed arrays

```text
SDF samples = 729 = 9^3
surface material ids = 512 = 8^3
```

The codec rejects all other cardinalities and rejects material ids outside UInt16 range.

## 7. Canonical record wire

`SpatialTerrainGeometryRecordWireCodecV2` preserves the standard Domain record-envelope field order:

| field | value |
|---:|---|
| 1 | record id bytes(16) |
| 2 | schema id |
| 3 | schema version |
| 4 | revision |
| 5 | created step |
| 6 | retired step? |
| 7 | detail level |
| 8 | lineage id? |
| 9 | v2 discriminated payload |

Decoder is fail-closed for unknown/duplicate/non-canonical fields, wrong schema, unknown arm, missing arm-specific fields, invalid root ownership, invalid cell key, and invalid fixed-array cardinality.

Valid decode -> encode must reproduce canonical bytes.

## 8. Semantic target-kind closure

`SpatialTerrainGeometryRecordSetV2` validates every root target against actual v2 material.

A valid `root_brick_ref` requires:

1. partition = `spatial.terrain_geometry`
2. target RecordId exists
3. target arm = `terrain_brick`

Missing target and wrong target kind are distinct fail-closed errors.

Canonical QA-04 root connectivity is also recovered and validated against actual Terrain root identities.

## 9. Semantic payload / partition digest

`SpatialTerrainGeometryPayloadCanonicalDigestV2` uses the standard semantic Domain hash boundary and includes the actual v2 schema identity.

For roots the digest covers scope/root-brick refs, geometry revision, canonical token/ref lists, and optional archive anchor.

For bricks the digest covers level, cell origin, spacing, all 729 SDF samples, and all 512 material ids.

The same semantic record stream must reproduce the frozen `PartitionStateHeaderV1` digest after recovery.

## 10. Production Snapshot authority

Two compatible paths remain available:

- materialized `SpatialTerrainGeometrySnapshotAuthorityV2` for bounded fixtures/canaries
- `SpatialTerrainGeometryStreamingSnapshotAuthorityV2` for full canonical Terrain

Full QA-04 Terrain uses the streaming authority to avoid retaining all record payloads simultaneously.

Canonical count:

```text
hot D0 bricks = 500,000
terrain roots =   4,096
D3 anchors    =   4,096
total         = 508,192
```

The canonical source stores compact record identity/locator metadata and regenerates payloads deterministically on demand.

## 11. Fragmentation and exact-103 production composition

Terrain v2 keeps the existing Domain fragment envelope and standard Snapshot section identity.

Full material uses bounded-memory two-pass fragmentation:

1. planning pass computes fragment item counts without retaining all payloads
2. emission pass regenerates byte-identical records and emits one fragment-sized group at a time

Existing limits remain unchanged:

```text
fragment target = 32 MiB
fragment hard max = 64 MiB
```

Terrain v2 is selected only for `spatial.terrain_geometry`; the other Domain providers keep their registered schemas. Core 6 + Domain 97 remains exact **103 sections**.

## 12. Production recovery

Recovery remains two-phase and bounded-memory.

Phase 1:

- staged `MVCHNK01` chunks are read one at a time
- Terrain fragments are decoded one at a time
- compact RecordId/kind index is retained
- root closure metadata is retained
- actual `spatial.scope_registry` identities are recovered from the same staged Snapshot

Phase 2:

- Terrain fragments are read again
- record identity/kind matches are validated
- root refs and scope refs resolve against actual recovered authority
- semantic payload digests stream into the canonical partition-header hash
- recovered digest must equal the frozen Terrain authority

Full partition payload reconstruction is not required.

## 13. Canonical QA-04 Terrain generation

The benchmark-specific content is frozen by `phase4-alpha11-terrain-canonical-generation.md`.

Key invariants include:

- 64x64 tile lattice
- tile width 512,000 mm
- D0 spacing 250 mm
- D0 brick width 2,000 mm
- 500,000 hot D0 descriptors
- one D3 anchor per tile
- one root per tile
- actual TileScope ownership
- N/E/S/W root connectivity
- canonical SDF/material generation from the fixed benchmark context
- no duplicate same-level cell origin

Preflight/sampled checks are supplemental regression evidence only; they are not release evidence.

## 14. Current release state

The former Terrain failure code:

```text
qa04.material.terrain-brick-authority-undefined
```

is **not active**.

Dedicated `INT-03 full Terrain production validation` has successfully proven the full **508,192-record** path:

1. canonical streaming authority/header
2. exact-103 streaming composition
3. actual Zstd staging + `manifest.pb`
4. staged two-pass recovery
5. all 508,192 recovered RecordIds equal frozen authority identities
6. 4,096 root / 4,096 D3 anchor kind closure
7. actual TileScope closure
8. post-recovery semantic rehash equality

Terrain completion does not imply `referenceWorldMaterialized=true`. Society/Governance and Infrastructure remain active reference-world blockers, and canonical workload bindings remain incomplete.
