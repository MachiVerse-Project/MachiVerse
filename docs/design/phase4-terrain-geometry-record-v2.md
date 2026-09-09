# Phase 4 spatial.terrain_geometry record schema v2

Status: Decided / standalone schema + wire implemented / production activation pending  
Tracking: #240  
Implementation: Draft PR #265  
Applies to: `domain.spatial.terrain_geometry.record` **2.0**

## 1. Purpose

`spatial.terrain_geometry` v1 contains the terrain-root record defined by P4-05, while P4-04 independently defines authoritative `TerrainBrickV1` SBO-SDF state. `perf.reference.v1` requires 500,000 hot terrain bricks and the v1 root contains a required `root_brick_ref`, so both root and brick records must exist under the same existing Spatial authority without introducing a 98th partition.

This document freezes the exact v2 record contract and its lossless standalone wire. It does **not** activate v2 in `StandardDomainPartitionRegistry`, populate the canonical 500,000 brick contents, or mark the QA-04 reference world materialized.

## 2. Schema identity

The stable schema id is unchanged and the major version advances:

```text
v1 = domain.spatial.terrain_geometry.record / 1.0
v2 = domain.spatial.terrain_geometry.record / 2.0
```

The standard runtime registry remains v1 until the partition-wide migration, mixed-record state container, production snapshot/recovery integration, semantic target-kind validation, and canonical benchmark material are complete.

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

The codec intentionally does not modify the production v1 `DomainPartitionSnapshotWireCodecV1`. It provides the exact lossless material contract needed before production partition migration can be implemented safely.

## 8. Migration boundary

### 8.1 v1 root -> v2 root

Deterministic recipe:

1. require source record schema `domain.spatial.terrain_geometry.record / 1.0`;
2. preserve `record_id`, `revision`, `created_step`, `retired_step`, `detail_level`, `lineage_ref`;
3. set record schema to the same stable id at `2.0`;
4. set `record_kind = terrain_root`;
5. copy all six v1 root payload values exactly;
6. require the decided same-partition `root_brick_ref` owner.

No benchmark-specific value is fabricated by migration.

### 8.2 `TerrainBrickV1` -> v2 brick

Deterministic mapping:

1. `brick_id -> record_id`;
2. `revision -> record revision`;
3. caller supplies the common record lifecycle/detail/lineage metadata from authoritative creation context;
4. copy `level`, `cell_origin`, `sample_spacing_mm`, all 729 SDF samples, and all 512 material ids exactly.

## 9. What remains blocked

This schema decision removes the **terrain v2 field-shape ambiguity**, but does not make the canonical terrain class materialized.

Still required before the QA-04 blocker can be removed:

- integrate v2 mixed-record support into the production partition state/snapshot/recovery path;
- migrate the actual terrain-root authority to v2;
- add recovered Ref target-kind validation so `root_brick_ref` must resolve to a `terrain_brick` arm, not merely any record in the partition;
- define the canonical `perf.reference.v1` SDF/material contents for all 500,000 hot bricks;
- materialize those records from the fixed benchmark seed/context;
- include the resulting authority in the full exact-97 / exact-103 canonical proof.

Accordingly `qa04.material.terrain-brick-authority-undefined` remains active for now and `referenceWorldMaterialized` remains false.
