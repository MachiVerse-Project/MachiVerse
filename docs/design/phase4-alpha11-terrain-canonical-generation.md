# Alpha 1.1 — `perf.reference.v1` Terrain canonical generation

Status: Decided normative design / implementation pending  
Tracking: #240  
Parent: `phase4-alpha11-normative-closure.md`

## 1. 目的

既存 Terrain v2 representation / wire / migrationを変更せず、未決定だった500,000 hot D0 bricksのcell origin、SDF、material、root/scope、connectivity、revision/lineageを固定する。

## 2. Benchmark lattice

```text
tiles = 64 x 64 = 4096
tile width = 512,000 mm
D0 spacing = 250 mm
D0 brick = 8 cells = 2,000 mm
D0 slots/tile = 256 x 256 = 65,536
D3 spacing = 64,000 mm
D3 brick = 8 cells = 512,000 mm = one tile width
```

Tile `t`:

```text
row = t / 64
col = t % 64
tile_origin_x_mm = col * 512000
tile_origin_y_mm = row * 512000
tile_center_x_mm = tile_origin_x_mm + 256000
tile_center_y_mm = tile_origin_y_mm + 256000
```

## 3. Root material

Every tile has exactly:

```text
1 terrain_root
1 D3 root-anchor terrain_brick
```

IDs are derived at Step0 with creation kinds:

```text
perf.terrain-root
perf.terrain-root-brick
```

Root:

```text
scope_ref = TileScope(t)
root_brick_ref = D3 anchor(t)
geometry_revision = 1
surface_classes = [terrain.rock, terrain.sediment, terrain.soil] // ASCII order
connectivity_refs = N/E/S/W existing neighbor terrain roots, then canonical Ref sort
archive_anchor = NONE
```

Connectivity is target-kind checked against same partition /2.0 / `terrain_root`.

## 4. Height function

For any signed world X/Y millimetre coordinate:

```text
TH(x,y) = HashSuite.DomainHash(
  "mv.perf-reference-terrain-height.v1",
  MV-DCBOR array [WorldSeed bytes32, x int64, y int64])

u = unsigned_big_endian_uint32(TH[0..4])
height_mm(x,y) = int64(u % 8001) - 4000
```

Range is exactly `[-4000,+4000] mm`.

Negative coordinate encoding uses MV-DCBOR signed int semantics; string formatting is never part of the preimage.

## 5. D3 anchor placement

```text
anchor_brick_z = floor_div(height_mm(tile_center_x_mm,tile_center_y_mm), 512000)
```

`floor_div` is mathematical floor toward negative infinity, not truncation toward zero.

```text
level = 3
sample_spacing_mm = 64000
cell_origin.level = 3
cell_origin.x = col * 8
cell_origin.y = row * 8
cell_origin.z = anchor_brick_z * 8
revision = 1
```

This guarantees the tile-center terrain surface falls within the D3 anchor's vertical 512m span.

## 6. 500,000 hot D0 brick placement

Group existing hot descriptors by `RegionalTileIndex`. Within each tile sort descriptor `RecordId` ascending and assign local rank `r=0..count-1`.

Materializer must assert `count < 65536` for every tile.

```text
slot = (r * 40503 + t * 17) mod 65536
local_brick_x = slot & 255
local_brick_y = slot >> 8
```

40503 is odd; multiplication is therefore bijective modulo `2^16`, so ranks within one tile cannot collide.

```text
global_brick_x = col*256 + local_brick_x
global_brick_y = row*256 + local_brick_y
center_x_mm = global_brick_x*2000 + 1000
center_y_mm = global_brick_y*2000 + 1000
brick_z = floor_div(height_mm(center_x_mm,center_y_mm), 2000)

level = 0
sample_spacing_mm = 250
cell_origin.level = 0
cell_origin.x = global_brick_x * 8
cell_origin.y = global_brick_y * 8
cell_origin.z = brick_z * 8
revision = 1
```

Hot descriptor RecordId is the terrain brick record id exactly; no replacement ID is generated.

## 7. SDF samples

Existing sample index remains:

```text
index = ((z*9)+y)*9+x
x,y,z = 0..8
```

For sample `(sx,sy,sz)`:

```text
wx = (cell_origin.x + sx) * sample_spacing_mm
wy = (cell_origin.y + sy) * sample_spacing_mm
wz = (cell_origin.z + sz) * sample_spacing_mm
sdf_mm = checked_int32(wz - height_mm(wx,wy))
```

All inputs in this benchmark lattice keep the result inside int32. Materializer still performs checked conversion and fails on overflow.

Because SDF depends only on absolute world sample coordinates and WorldSeed, any two bricks sharing the same sample point must encode the same SDF value. This is the boundary equality invariant.

## 8. Surface material samples

Existing cell index:

```text
index = ((z*8)+y)*8+x
x,y,z=0..7
```

Cell center uses exact half spacing:

```text
cx_mm = (2*(cell_origin.x+x)+1) * sample_spacing_mm / 2
cy_mm = (2*(cell_origin.y+y)+1) * sample_spacing_mm / 2
cz_mm = (2*(cell_origin.z+z)+1) * sample_spacing_mm / 2
d = cz_mm - height_mm(cx_mm,cy_mm)
```

Both standard D0/D3 spacings are even, so division by2 is exact.

Material ids:

```text
0 = void
1 = soil
2 = rock
3 = sediment
```

Rule:

```text
d > 0             -> 0
-500 < d <= 0     -> 1
-2000 < d <= -500 -> 3
d <= -2000        -> 2
```

## 9. Sparse refinement semantics

Persisted authority is:

```text
D3 anchor = complete coarse fallback for tile
D0 hot brick = local refinement override
```

Lookup at a point:

1. collect authority bricks covering the point;
2. choose smallest `sample_spacing_mm`;
3. if equal spacing, choose RecordId ascending first;
4. interpolate with existing fixed-point Terrain rule.

Intermediate octree/index nodes are `DERIVED_REBUILDABLE` and are not additional authoritative records. They may be discarded/rebuilt without changing StateDigest.

## 10. Lineage

Genesis root/anchor/hot brick:

```text
DomainRecordEnvelope.revision = 1
created_step = 0
retired_step = NONE
lineage_ref = NONE
```

Root payload `geometry_revision=1`.

A later promotion/demotion that creates a new terrain record must create `spatial.geometry_lineage` material with source records as canonical `parent_refs`; it must not forge genesis lineage retroactively.

## 11. Canonical validation

Before claiming 500,000 canonical hot bricks:

- exact hot count 500,000
- exact root count 4,096
- exact D3 anchor count 4,096
- no duplicate `(level,cell_origin)` among same-level hot records
- sample count 729 each
- material count 512 each
- shared sample SDF equality
- every root scope actual
- every root_brick target kind=`terrain_brick`
- every connectivity target kind=`terrain_root`
- all hot descriptor IDs preserved
- partition semantic rehash after exact103 recovery equal

Only after these production-path checks pass may the Terrain canonical-material blocker be removed.

## 12. Bounded-memory production Snapshot / recovery

The canonical Terrain v2 partition contains exactly:

```text
500,000 hot D0 terrain_brick
4,096 terrain_root
4,096 D3 anchor terrain_brick
= 508,192 records
```

The production path must not require all 508,192 Terrain payloads, all Terrain fragment payloads, or all recovered Terrain records to coexist in memory.

The following bounded-memory rules are normative for the Alpha 1.1 release path:

1. **Canonical identity index**
   - it is permitted to retain compact deterministic locator metadata such as `RecordId`, record kind, and source ordinal/tile;
   - it is not permitted to pre-materialize and retain all 729-SDF / 512-material payloads merely to establish canonical order;
   - the complete Terrain record stream is ordered by `RecordId` ascending, with no duplicate identity across hot/root/anchor records.

2. **On-demand materialization**
   - hot records use the canonical descriptor identity and the generation rules in this document;
   - root and D3 anchor records use the same canonical root/anchor materializers and actual TileScope authority;
   - repeated passes over the same frozen authority must regenerate byte-identical record material.

3. **Partition semantic digest**
   - the digest remains the existing `mv.state-diagnostic.v1` canonical digest;
   - streaming hash computation may feed the existing canonical MV-DCBOR bytes incrementally, but must be byte-for-byte equivalent to the materialized computation;
   - no new hash domain, record identity, schema version, or digest preimage is introduced for bounded-memory operation.

4. **Section fragmentation**
   - `SpatialTerrainGeometrySnapshotFragmentWireV2` remains unchanged;
   - the existing `32 MiB` target and `64 MiB` hard uncompressed limits remain unchanged;
   - because each fragment carries `fragment_count`, a deterministic two-pass implementation is permitted:
     1. first pass regenerates records one at a time, measures the existing v2 record wire length, and retains only per-fragment item counts/boundaries;
     2. second pass regenerates the same canonical record stream and emits one fragment at a time;
   - fragment ranges, item counts, and payload bytes must match the existing materialized provider for the same record stream.

5. **Chunk drain**
   - physical chunk packing must be able to consume canonical fragments in order without first aggregating all section fragments into an array;
   - chunk target/hard limits and `SnapshotChunkPayloadWireCodecV1` remain unchanged;
   - a completed chunk may be encoded/compressed/written before the next chunk is assembled.

6. **Recovery / semantic verification**
   - recovered Terrain fragments are processed in canonical fragment/record order;
   - semantic payload validation and partition rehash must not require retaining all recovered payload records simultaneously;
   - compact recovered identity/type indexes may be retained when required for `terrain_root -> terrain_brick`, connectivity, TileScope, and exact-97/all-reference closure checks;
   - recovery must still prove exact `508,192` Terrain record count and the same canonical partition digest.

7. **Release boundary**
   - bounded-memory preflight or fragment-equivalence tests alone are not release evidence;
   - the Terrain parent blocker remains until the actual production exact-103 path completes Snapshot -> compression/staging -> recovery -> semantic rehash with all canonical Terrain records and required reference closure.

This bounded-memory path is an implementation boundary only. It does not alter the existing Terrain v2 representation, Snapshot wire, standard partition identity, migration activation rule, or blocker acceptance criteria.
