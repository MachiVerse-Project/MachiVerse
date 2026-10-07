# Alpha 1.1 Physical D0 full reference-world genesis authority

Status: **Decided normative authority for `perf.reference.v1` Gate 2 Step 13**  
Tracking: Issue #240  
Implementation: Draft PR #265

## Purpose

Gate 2 Step 13 requires the production reference-world `physical.presence` population to exist as actual typed authority, not as a count-only header or a reduced fixture. The already-approved PropertyRight support authority fixes exact Physical Presence genesis for ordinals `0..49,999`, but explicitly leaves ordinals `50,000..499,999` undecided.

This document closes only that benchmark-specific gap. It does **not** define general MachiVerse Physical subject ontology, vehicle/item/animal/equipment identity, universal frame semantics, ownership semantics, or future world-generation policy.

## Existing authority reused unchanged

This authority reuses without redefinition:

- `Qa04ReferenceLoadV1` Physical descriptor identity, regional tile assignment, and `perf.reference.position.v1` addressable position source;
- canonical Resident identity/materialization;
- `Qa04SpatialTileScopeAuthorityV1` 4,096 TileScopes;
- the approved 4,096 benchmark TileFrames from `phase4-alpha11-physical-d0-property-asset-binding-authority.md`;
- `Qa04TerrainRootMaterializerV1` canonical Terrain roots and D3 anchors;
- `Qa04TerrainCanonicalContentSourceV1` tile width and deterministic terrain height;
- `Qa04PhysicalShapeMaterializerV1` exact 500,000-record shape distribution;
- `Qa04PhysicalD0MaterializerV1` Presence, occupancy, and collision-shape production path;
- existing standard and explicit v2 partition/record schema identities.

No new opaque identity class, placeholder Ref, arbitrary Token vocabulary, or synthetic Terrain binding is introduced.

## Full 500,000 Presence genesis binding

For every Physical ordinal `p = 0..499,999`:

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

The Physical Presence record identity remains the existing QA-04 Physical descriptor RecordId. Presence records remain D0. The referenced Resident retains its own canonical Resident detail level; this benchmark binding does not promote Resident detail state to D0.

For ordinals `0..49,999`, this rule is required to be exactly identical to the previously approved PropertyRight support authority. The new decision only extends the same benchmark genesis mapping to ordinals `50,000..499,999`.

## Shape, Terrain, and occupancy binding

The existing full 500,000 `Qa04PhysicalShapeMaterializerV1` authority is unchanged. For every Terrain-SDF shape, the Terrain root and occupancy AABB are the same canonical bindings defined by P3 of `phase4-alpha11-physical-d0-property-asset-binding-authority.md`:

- same-tile actual Terrain root;
- AABB derived from the actual canonical D3 anchor;
- no unrelated synthetic box;
- no change to Terrain semantic extent.

`physical.occupancy /2.0` continues to contain one occupancy record and one collision-shape record per Physical descriptor, for exactly 1,000,000 records.

## Required production invariants

The Step 13 production proof must fail closed unless all of the following hold on the same current head:

1. exactly 500,000 Presence records are produced through `Qa04PhysicalD0MaterializerV1`;
2. Presence RecordId equals the QA-04 Physical descriptor RecordId at every ordinal;
3. `Presence[p].subject_ref = Resident[p]` for all 500,000 ordinals;
4. descriptor tile -> approved TileFrame mapping is exact;
5. deterministic XY and Terrain-height Z are exact;
6. orientation, velocities, containment, and `perf.free-moving` values are exact;
7. ordinals `0..49,999` are byte/semantic-compatible with the previously approved PropertyRight support binding;
8. every Presence `shape_ref` closes to the actual collision-shape record;
9. every occupancy record closes back to its actual Presence;
10. Terrain-SDF roots and AABBs close to actual canonical Terrain authority;
11. all Presence RecordIds and subject Refs are unique in the 500,000-record benchmark population;
12. the exact shape distribution remains 250,000 sphere / 100,000 capsule / 100,000 OBB / 40,000 convex / 5,000 static mesh / 5,000 Terrain-SDF;
13. the resulting typed partition headers are derived from actual materialized records and canonical payload digests;
14. Gate 2 Step 13 uses this full production state rather than a reduced or touched-record-only fixture.

## Explicit non-generalization

This authority is strictly `perf.reference.v1` benchmark genesis material. It does not assert that:

- every Physical Presence in MachiVerse is a Resident;
- every future QA profile must use a Resident-only Physical population;
- vehicle/item/animal/equipment identity is represented by Resident identity;
- a Presence is universally an economic/legal asset;
- TileFrame is a universal world-root frame or complete frame hierarchy;
- zero motion or `perf.free-moving` is a general Physical default;
- ownership, possession, occupancy, effective control, or territorial claim are equivalent;
- Infrastructure facility identity is changed.

The approved PropertyRight authority remains limited to its original first 50,000 economic asset targets. Extending Physical genesis to 500,000 does not extend PropertyRight ownership.