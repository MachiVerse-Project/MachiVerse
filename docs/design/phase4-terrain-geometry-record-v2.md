# Phase 4 spatial.terrain_geometry record schema v2

Status: Decided / standalone schema + wire + semantic Snapshot foundation implemented / exact-97 activation pending  
Tracking: #240  
Implementation: Draft PR #265  
Applies to: `domain.spatial.terrain_geometry.record` **2.0**

## 1. Purpose

`spatial.terrain_geometry` v1 contains the terrain-root record defined by P4-05, while P4-04 independently defines authoritative `TerrainBrickV1` SBO-SDF state. `perf.reference.v1` requires 500,000 hot terrain bricks and the v1 root contains a required `root_brick_ref`, so both root and brick records must exist under the same existing Spatial authority without introducing a 98th partition.

This document freezes the exact v2 record contract and implements the standalone semantic Snapshot chain through record wire, target-kind closure, mixed-record partition state, semantic payload digest, frozen authority, fragment wire, section materialization, and recovered rehash. It does **not** activate v2 in the exact-97 production composition, populate the canonical 500,000 brick contents, or mark the QA-04 reference world materialized.

## 2. Schema identity

The stable schema id is unchanged and the major version advances:

```text
v1 = domain.spatial.terrain_geometry.record / 1.0
v2 = domain.spatial.terrain_geometry.record / 2.0
```

The standard runtime registry remains v1 until the partition-wide migration is activated atomically across runtime authority, all-97 reference resolution, Snapshot recovery, and cross-partition schema validation.

The partition/container schema remains unchanged:

```text
domain.spatial.terrain_geometry / 1.0
```

`PartitionStateHeaderV1` therefore continues to use the existing partition schema. Record schema id/major/minor are already included per record in the canonical partition digest.

## 3. Common record envelope

Both v2 arms use the existing Phase 4 common record envelope:

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

No duplicate `brick_id` or `revision` field is added to the payload.

For v1 `terrain_root` migration, all common envelope fields are preserved exactly and only the record schema advances from 1.0 to 2.0.

## 4. Record kinds

Canonical record kinds are:

```text
terrain_brick
terrain_root
```

The payload field at ordinal 1 is always `record_kind:Token`. Unknown record kinds fail closed.

## 5. `terrain_root` arm

Exact payload descriptor order:

| ordinal | field | type | optional |
|---:|---|---|---|
| 1 | `record_kind` | Token | no |
| 2 | `scope_ref` | Ref | no |
| 3 | `root_brick_ref` | Ref | no |
| 4 | `geometry_revision` | UInt64 | no |
| 5 | `surface_classes` | TokenList | no |
| 6 | `connectivity_refs` | RefList | no |
| 7 | `archive_anchor` | Digest(32) | yes |

`record_kind` must equal `terrain_root`.

`root_brick_ref.partition_id` must equal `spatial.terrain_geometry`. A synthetic target in another partition is invalid.

The other fields are the exact P4-05 v1 terrain-root fields. Therefore v1 -> v2 root migration is deterministic and lossless when the root brick reference satisfies the decided same-partition ownership rule.

## 6. `terrain_brick` arm

Exact payload descriptor order:

| ordinal | field | type | optional |
|---:|---|---|---|
| 1 | `record_kind` | Token | no |
| 2 | `level` | UInt8 | no |
| 3 | `cell_origin` | SpatialCellKeyV1 | no |
| 4 | `sample_spacing_mm` | UInt32 | no |
| 5 | `sdf_mm` | fixed Int32 list, exactly 729 | no |
| 6 | `surface_material_id` | fixed UInt16 list, exactly 512 | no |

`record_kind` must equal `terrain_brick`.

The arm is a lossless mapping of the P4-04 `TerrainBrickV1` algorithm state after moving `brick_id` and `revision` into the common record envelope.

### 6.1 `SpatialCellKeyV1`

Standalone v2 wire fields:

| field | type |
|---:|---|
| 1 | `level:uint8` |
| 2 | `x:sint32` |
| 3 | `y:sint32` |
| 4 | `z:sint32` |

All four are encoded explicitly in canonical field order.

`TerrainBrickV1.level` and `TerrainBrickV1.cell_origin.level` are preserved independently. Existing P4-04 runtime state does not assert that they are equal, so v2 must not silently normalize one from the other.

### 6.2 Fixed arrays

`SDF` list:

```text
729 × sint32
index = ((z * 9) + y) * 9 + x
```

Surface material list:

```text
512 × uint16
index = ((z * 8) + y) * 8 + x
```

The standalone codec rejects any other element count or a material value outside uint16 range.

## 7. Standalone protobuf record wire

`SpatialTerrainGeometryRecordWireCodecV2` preserves the existing Domain record-envelope field numbers:

| field | value |
|---:|---|
| 1 | record id bytes(16) |
| 2 | schema id |
| 3 | schema version |
| 4 | revision |
| 5 | created step, omitted only when zero |
| 6 | retired step, optional |
| 7 | detail level, omitted only for D0/default |
| 8 | lineage id bytes(16), optional |
| 9 | v2 discriminated payload |

Decoder rules are fail-closed:

- field order must be strictly canonical for singular messages;
- unknown or duplicate/non-canonical record fields are rejected;
- schema id/version must be exactly the v2 identity;
- unknown record kind is rejected;
- arm-specific unknown/missing/wrong-wire fields are rejected;
- root brick target must remain inside `spatial.terrain_geometry`;
- `SpatialCellKeyV1` requires all four scalar fields;
- SDF/material fixed cardinalities are enforced;
- decode -> encode returns the canonical same byte sequence for valid material.

The codec intentionally does not modify the production v1 `DomainPartitionSnapshotWireCodecV1` record decoder.

## 8. Semantic target-kind closure and mixed state

`SpatialTerrainGeometryRecordSetV2` holds v2 records in canonical `record_id` byte order and validates every `terrain_root.root_brick_ref` against the actual record set.

A valid root target must satisfy all of the following:

1. target partition is exactly `spatial.terrain_geometry`;
2. target record id exists in the same v2 record set;
3. target payload arm is exactly `terrain_brick`.

Missing targets fail with `spatial.terrain-v2.root-brick-missing`; an existing non-brick target fails with `spatial.terrain-v2.root-brick-kind`.

`SpatialTerrainGeometryPartitionStateV2` then places the closed root + brick set into the existing ordered `DomainPartitionStateV1<SpatialTerrainGeometryPayloadV2>` container using a versioned identity that differs from the standard identity only in record schema `2.0`.

The standard runtime registry is explicitly checked to remain at record schema `1.0` during this standalone phase.

## 9. Semantic payload and partition digest

`SpatialTerrainGeometryPayloadCanonicalDigestV2` is independent of protobuf bytes and follows the existing standard Domain semantic digest structure:

```text
HashDomain = mv.domain-payload.v1
material = {
  partition_id,
  record_schema_id,
  record_schema_major,
  record_schema_minor,
  ordered semantic fields
}
```

Reusing the hash domain is safe because schema id + explicit major/minor are inside the hashed material. A v1 and v2 payload therefore do not become equivalent merely because they share the same partition id.

For `terrain_root`, the digest validates canonical TokenList/RefList order and optional reference existence when a resolver is provided.

For `terrain_brick`, the digest covers both level values, spacing, all 729 SDF entries in canonical array order, and all 512 material ids.

`PartitionStateHeaderV1.CreateCanonical` can therefore hash the mixed v2 partition without changing the partition header schema. Tests prove identical semantic material reproduces the same partition digest and that changing an individual SDF sample changes the digest.

## 10. Standalone Snapshot authority and recovery chain

### 10.1 Frozen authority

`SpatialTerrainGeometrySnapshotAuthorityV2` implements the common `IDomainPartitionSnapshotAuthorityV1` boundary without weakening the generic v1 authority class.

It verifies:

- exact Terrain v2 partition identity;
- unchanged standard partition id / owner / partition schema;
- actual item count and canonical record-id order;
- root -> brick target-kind closure;
- recomputed `PartitionStateHeaderV1` including v2 record schema identities and semantic payload digests.

### 10.2 Fragment wire

`SpatialTerrainGeometrySnapshotFragmentWireV2` keeps the existing Domain fragment envelope:

```text
field 1 = existing PartitionStateHeaderV1 wire
field 2 = repeated terrain record-schema-2.0 record wire
```

The header remains first and unchanged. Records must be canonical by record id and cannot have created/retired steps after the frozen header basis step. Unknown fragment fields fail closed.

A decoded fragment can be re-encoded canonically, reconstructed into a v2 partition, and rebound to the original frozen header; semantic rehash must match exactly.

### 10.3 Section provider

`SpatialTerrainGeometrySnapshotSectionProviderV2` implements the common Domain section-provider interface while remaining **unselected** by the current exact-97 production composition.

It:

- accepts only `SpatialTerrainGeometrySnapshotAuthorityV2`;
- emits the standard section id and unchanged partition section schema;
- uses the frozen authority item count and partition digest as section logical authority;
- fragments only at record boundaries using the standard 32 MiB target / 64 MiB hard limit;
- records exact first/last record-id ranges and item counts;
- reconstructs all fragments, checks repeated headers/ranges/order, rebuilds the v2 mixed-record partition, reruns target-kind closure, and recomputes the frozen partition digest;
- can validate cross-partition Ref existence through the common reference-resolution context when such a resolver is supplied.

This completes a truthful standalone `record -> partition -> header -> section fragments -> recovery -> semantic rehash` chain for Terrain v2.

## 11. Migration boundary

### 11.1 v1 root -> v2 root

Deterministic recipe:

1. require source record schema `domain.spatial.terrain_geometry.record / 1.0`;
2. preserve `record_id`, `revision`, `created_step`, `retired_step`, `detail_level`, `lineage_ref`;
3. set record schema to the same stable id at `2.0`;
4. set `record_kind = terrain_root`;
5. copy all six v1 root payload values exactly;
6. require the decided same-partition `root_brick_ref` owner.

No benchmark-specific value is fabricated by migration.

### 11.2 `TerrainBrickV1` -> v2 brick

Deterministic mapping:

1. `brick_id -> record_id`;
2. `revision -> record revision`;
3. caller supplies the common record lifecycle/detail/lineage metadata from authoritative creation context;
4. copy `level`, `cell_origin`, `sample_spacing_mm`, all 729 SDF samples, and all 512 material ids exactly.

### 11.3 Exact-97 activation must be atomic

The current production path still assumes the standard v1 record schema in several coordinated places. Activation must not relax only one of them.

The next migration slice must update together:

- the frozen canonical world terrain header/material binding;
- the 8-owner / exact-97 authority set identity policy;
- provider selection so only `spatial.terrain_geometry` uses the v2 provider;
- all-97 production reference resolver to preserve the actual v2 target record schema;
- cross-partition Ref schema validation so the explicitly migrated Terrain schema is accepted and arbitrary major versions are still rejected;
- recovered-reference construction so Terrain fragments use the v2 decoder before the snapshot-wide resolver is built;
- second-phase semantic recovery/target-kind validation.

Until those are switched and validated together, the generic v1 production path remains unchanged.

## 12. What remains blocked

This implementation removes Terrain ownership, exact v2 field-shape, standalone record wire, target-kind, mixed-state, semantic digest, frozen-authority, fragment, section, and recovered-rehash ambiguity. The canonical terrain benchmark class is still not materially populated or active in exact-97 production recovery.

Still required before the QA-04 blocker can be removed:

- perform the atomic exact-97 Terrain v2 activation described above;
- define the canonical `perf.reference.v1` SDF/material contents for all 500,000 hot bricks;
- materialize those records from the fixed benchmark seed/context;
- include the resulting authority in the full exact-97 / exact-103 canonical proof and recovered all-97 resolver.

Accordingly `qa04.material.terrain-brick-authority-undefined` remains active for now and `referenceWorldMaterialized` remains false.
