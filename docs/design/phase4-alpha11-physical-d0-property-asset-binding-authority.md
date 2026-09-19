# Alpha 1.1 Physical D0 PropertyRight asset binding authority

Status: **Approved normative authority for perf.reference.v1 Alpha 1.1**

Tracking: #240
Approval record: #240 comment `5650161684`
Implementation: #265
Downstream PropertyRight authority: `phase4-alpha11-society-property-right-authority-proposal.md`

## Purpose

The approved `society.property_right` benchmark authority requires `asset_ref = PhysicalPresence[p]` for `p = 0..49,999`, and requires those refs to close to actual records produced by the canonical Physical D0 production materialization path.

`Qa04PhysicalD0MaterializerV1` intentionally leaves external genesis bindings to their owners. This authority fixes the minimum exact support material needed to produce those first 50,000 Presence records without promoting test-only IDs or inferring cross-partition semantics.

This authority is benchmark-only. It does not define Physical ordinals `50,000..499,999`.

## Existing authority reused unchanged

The implementation must reuse, without redefining:

- `Qa04ReferenceLoadV1` Physical descriptor identity, regional tile assignment, and `perf.reference.position.v1` addressable random position source;
- canonical Resident identity/materialization, including Resident ordinals `0..49,999`;
- `Qa04SpatialTileScopeAuthorityV1` 4,096 actual `spatial.scope_registry` TileScope records;
- `Qa04TerrainRootMaterializerV1` actual 4,096 Terrain roots and 4,096 D3 anchors;
- `Qa04TerrainCanonicalContentSourceV1` tile width and deterministic terrain height;
- `Qa04PhysicalShapeMaterializerV1` exact Physical shape mix and payloads;
- `Qa04PhysicalD0MaterializerV1` actual Presence, occupancy, and collision-shape materialization;
- the existing QA-04 Physical descriptor RecordIds and current domain record schemas.

No test-only opaque ID may be promoted to production authority.

## P1 — benchmark TileFrame support authority

Materialize exactly 4,096 support records in the existing `spatial.world_frame` partition, one for canonical regional tile `t = 0..4,095`.

Identity:

```text
TileFrameId[t] = DerivedIdentity(
  world_id      = Qa04ReferenceLoadV1.WorldId,
  creation_step = 0,
  domain        = spatial,
  subject_id    = TileScopeId[t],
  creation_kind = perf.tile-frame,
  ordinal       = 0)
```

Envelope:

```text
record_id    = TileFrameId[t]
revision     = 1
created_step = 0
retired_step = NONE
detail_level = D2
lineage_ref  = NONE
```

Payload:

```text
frame_kind         = perf.world-aligned-tile-frame
parent_frame       = NONE
translation        = (0, 0, 0)
rotation           = (0, 0, 0, 1<<30)
valid_scope        = TileScope[t]
transform_revision = 1
```

Normative semantics:

- each TileFrame is a benchmark-only world-coordinate-aligned chart whose validity is restricted to its actual regional TileScope;
- zero translation and identity rotation mean the Presence positions below are already expressed in the benchmark world metric coordinate basis;
- `parent_frame=NONE` intentionally avoids inventing an unmaterialized world-root frame or world-scope geometry record;
- this support set does not complete the general `spatial.world_frame` hierarchy and does not define future local-frame semantics.

Required closure:

- every `valid_scope` closes to the actual `Qa04SpatialTileScopeAuthorityV1` record for the same tile;
- all 4,096 TileFrame RecordIds are distinct;
- `(valid_scope, frame_kind)` is unique in this benchmark support set.

## P2 — first-50,000 Physical Presence genesis binding

For Physical ordinal `p = 0..49,999`:

```text
d = Qa04ReferenceLoadV1.Record(physical.d0-presence, p)
t = d.RegionalTileIndex
(u, v) = Qa04ReferenceLoadV1.PositionWithinTile(d.RecordId, step=0)

row = floor(t / 64)
col = t mod 64
W   = Qa04TerrainCanonicalContentSourceV1.TileWidthMm

x_mm = col * W + floor(u * W)
y_mm = row * W + floor(v * W)
z_mm = Qa04TerrainCanonicalContentSourceV1.HeightMm(x_mm, y_mm)
```

Exact binding:

```text
subject_ref                  = Resident[p]
frame_ref                    = TileFrame[t]
position                     = (x_mm, y_mm, z_mm)
orientation                  = (0, 0, 0, 1<<30)
linear_velocity              = (0, 0, 0)
angular_rate_urad_per_second = (0, 0, 0)
containment_ref              = NONE
presence_mode                = perf.free-moving
```

Normative semantics:

- the selected first 50,000 Physical D0 records use actual Resident ordinals `0..49,999` as their subjects, one-to-one by ordinal;
- this does not assert that all Physical Presence records are Residents;
- this authority does not define ordinals `50,000..499,999`;
- `perf.free-moving` is benchmark-only vocabulary, not a complete PresenceMode ontology;
- position uses the already-authoritative descriptor tile distribution and addressable random source, never iteration order;
- Z is the canonical deterministic terrain surface height at generated XY, with no additional clearance/support/navigation semantics implied;
- identity orientation and zero genesis velocities are exact benchmark initialization values only;
- no containment identity is fabricated.

The existing `Qa04PhysicalShapeMaterializerV1` shape mix remains unchanged, including `terrain_sdf_ref` where `p mod 100 = 99`.

## P3 — Terrain SDF root binding and occupancy bounds

For every tile `t`:

```text
terrain_root_ref = TerrainRoot[t]
```

The occupancy AABB supplied to `Qa04PhysicalD0MaterializerV1` for a Terrain-SDF shape must be derived from the actual canonical D3 anchor for tile `t`.

Let `Anchor[t]` be that canonical D3 anchor. Using its authoritative brick origin and sample spacing:

```text
min_x = Anchor.origin.X * Anchor.sample_spacing_mm
min_y = Anchor.origin.Y * Anchor.sample_spacing_mm
min_z = Anchor.origin.Z * Anchor.sample_spacing_mm

width = TerrainBrickV1.CellsPerAxis * Anchor.sample_spacing_mm

max_x = min_x + width
max_y = min_y + width
max_z = min_z + width
```

Then:

```text
occupancy_aabb_min = (min_x, min_y, min_z)
occupancy_aabb_max = (max_x, max_y, max_z)
```

The implementation must derive this AABB from actual canonical anchor material rather than creating an unrelated synthetic box.

The AABB is a finite broad-phase benchmark bound for the existing Terrain root's canonical D3 anchor. It does not limit the semantic extent of the Terrain root or modify Terrain geometry/root/hot-brick/recovery authority.

## Mandatory production proof

Approval permits implementation but does not itself change accepted accounting. #265 must prove on one current head:

1. exactly 4,096 actual TileFrame support records through the standard `spatial.world_frame` payload validator;
2. exact TileFrame identity/envelope/payload and closure to all 4,096 actual TileScopes;
3. exactly 50,000 actual canonical Resident subjects for ordinals `0..49,999`;
4. exactly 50,000 Presence records produced through `Qa04PhysicalD0MaterializerV1` for Physical ordinals `0..49,999`;
5. exact QA-04 Physical descriptor RecordId preservation for every Presence;
6. exact `Presence[p].subject_ref = Resident[p]` one-to-one mapping;
7. exact descriptor tile -> TileFrame mapping and same-tile TileFrame scope closure;
8. exact deterministic XY and terrain-height Z generation above;
9. exact identity orientation, zero motion, containment NONE, and `perf.free-moving` values;
10. all existing shape/occupancy closure required by `Qa04PhysicalD0MaterializerV1`;
11. each Terrain-SDF shape closes to the actual Terrain root and actual D3-anchor AABB for its descriptor tile;
12. fail-closed negatives for missing/wrong Resident, wrong subject partition/ordinal, missing/wrong TileFrame, frame/scope mismatch, position drift, orientation/motion drift, containment injection, presence-mode drift, missing/wrong Terrain root, Terrain AABB drift, duplicate Presence RecordId, and population drift;
13. the already-approved `society.property_right` 50,000 production proof consumes these actual Presence records and satisfies every PropertyRight gate;
14. generic Simulation Core smoke and full current-head CI are green.

## Accounting gate

The TileFrame/Physical support material does not add a Society/Governance accepted count.

Until PropertyRight production proof succeeds:

```text
Society/Governance accepted  = 1,601,200 / 2,000,000
Society/Governance remaining =   398,800
Society remaining            =   139,800
Governance remaining         =   259,000
```

Only after the approved PropertyRight 50,000 proof and full current-head CI succeed:

```text
Society/Governance accepted  = 1,651,200 / 2,000,000
Society/Governance remaining =   348,800
Society remaining            =    89,800
Governance remaining         =   259,000
```

All other reference-world, Infrastructure, workload, Operation, and release flags remain unchanged.

## Explicit non-decisions

This authority does not decide:

- Physical ordinals `50,000..499,999`;
- the full Resident / vehicle / item / animal / equipment subject mix for Physical D0;
- a universal world-root frame identity or complete frame hierarchy;
- general collision/support semantics at genesis;
- ownership of an underlying Resident subject from ownership of its Presence;
- Infrastructure facility identity;
- any remaining Society/Governance partition semantics.