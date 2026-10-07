# Alpha 1.1 — `physical.occupancy` record schema v2

Status: Decided normative design / implementation pending  
Tracking: #240  
Parent: `phase4-alpha11-normative-closure.md`

## 1. Schema identity

```text
schema_id = domain.physical.occupancy.record
version   = 2.0
partition = physical.occupancy
```

`record_kind` is required and exactly one arm is encoded:

```text
occupancy
collision_shape
```

Unknown record kind、unknown major、arm間field混在はrejectする。

## 2. `occupancy` arm

v1 payloadをlosslessに保持する。

```text
presence_ref: Ref
 aabb_min: Vec3Mm
 aabb_max: Vec3Mm
 contact_refs: RefList
 occupancy_flags: uint32
 collision_layer: uint32
```

Canonical rule:

- `aabb_min <= aabb_max` component-wise
- `contact_refs` canonical Ref order / duplicate禁止
- `presence_ref` target = `physical.presence /1.x`

v1 -> v2 migrationはこのarmへのfield-for-field移送のみ。

## 3. `collision_shape` arm

Common:

```text
shape_kind: Token
```

Allowed v2 values and exact fields:

### 3.1 `sphere`

```text
center_mm: Vec3Mm
radius_mm: int64   // >0
```

### 3.2 `capsule`

```text
segment_start_mm: Vec3Mm
segment_end_mm: Vec3Mm
radius_mm: int64   // >0
```

start=endは禁止。

### 3.3 `oriented_box`

```text
center_mm: Vec3Mm
half_extents_mm: Vec3Mm  // all >0
orientation: QuaternionQ30
```

QuaternionはP4 canonical normalization/sign ruleを満たす。

### 3.4 `convex_polytope`

```text
vertices_mm: ordered list<Vec3Mm>
```

- 1..256 entries
- duplicate vertex禁止
- orderは authoritative canonical vertex index
- decoder/materializerはsortしない
- GJK support tieはこのindexの小さい方

### 3.5 `triangle_mesh_static`

```text
triangles: ordered list<StaticTriangleV1>
StaticTriangleV1 {
  canonical_index: uint32
  a_mm: Vec3Mm
  b_mm: Vec3Mm
  c_mm: Vec3Mm
}
```

- 1..65,535 triangles
- `canonical_index` strict ascending / duplicate禁止
- dynamic concave meshとして使用しない
- physical queryはcanonical_index orderを維持

### 3.6 `terrain_sdf_ref`

```text
terrain_root_ref: Ref
```

Exact target:

```text
partition = spatial.terrain_geometry
schema    = domain.spatial.terrain_geometry.record /2.0
kind      = terrain_root
```

Alpha 1.1では arbitrary SDF blob、anonymous terrain handle、dynamic SDF bodyを許可しない。

## 4. Ref closure

`physical.presence.shape_ref` exact target:

```text
physical.occupancy /2.0 / collision_shape
```

`collision_shape`と同じRecordIdをoccupancy armへ流用してはならない。1 envelope = 1 record kind。

## 5. Canonical digest / wire

Wire field order:

```text
1 record_kind
2 shape_kind              // shape only
10.. arm-specific fields
```

`occupancy` armはP4-05 field orderを維持し、shape armは上記section順。logical digestはprotobuf bytesではなくdecoded normalized semantic valueのMV-DCBOR-v1。

Vec3はx,y,z order。listsはschema-defined order。forbidden arm fieldが存在すればreject。

## 6. `perf.reference.v1` material

500,000 Physical descriptor each:

```text
1 physical.presence
1 physical.occupancy /2.0 / occupancy
1 physical.occupancy /2.0 / collision_shape
```

Shape id:

```text
DerivedIdentity(world,0,performance,presence_id,"perf.physical-shape",0)
```

Occupancy id:

```text
DerivedIdentity(world,0,performance,presence_id,"perf.physical-occupancy",0)
```

Shape mix by `physicalOrdinal % 100`:

| range | kind |
|---|---|
| 0..49 | sphere |
| 50..69 | capsule |
| 70..89 | oriented_box |
| 90..97 | convex_polytope |
| 98 | triangle_mesh_static |
| 99 | terrain_sdf_ref |

Canonical benchmark geometry:

```text
sphere radius = 300 + ordinal % 201 mm
capsule start=(0,-400,0), end=(0,400,0), radius=200+ordinal%101
OBB center=(0,0,0)
OBB half=(300+o%101, 200+o%101, 500+o%201)
OBB orientation=(0,0,0,1<<30)
convex = 8 corners of the same OBB extents, fixed xyz-bit vertex order 0..7
mesh = two triangles forming local square (-1000,-1000,0)..(1000,1000,0), indices 0,1
terrain_sdf_ref = terrain root of descriptor regional tile
```

Presence `shape_ref` must point to its exact shape record. Occupancy AABB is computed from the selected shape using integer exact bounds; `contact_refs=[]` at genesis, `occupancy_flags=0`, `collision_layer=1`。

## 7. Migration / recovery acceptance

Blocker removal requires:

- explicit 1.0 -> 2.0 migration registry entry
- mixed-kind authority/state
- strict wire round-trip
- actual-schema preservation in Ref resolver
- target-kind closure for `shape_ref`
- exact-97/exact-103 provider/recovery semantic rehash
- 500,000 benchmark shape materialization

Schema design completion alone does not remove `qa04.material.physical-presence-shape-authority-undefined`.
