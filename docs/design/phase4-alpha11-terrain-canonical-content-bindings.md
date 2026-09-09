# Alpha 1.1 / INT-03 — canonical Terrain content bindings

Status: unresolved normative dependency audit  
Tracking: #240  
Implementation: Draft PR #265  
Applies to: `perf.reference.v1`

## 1. 目的

Terrain v2 の production migration / Snapshot / recovery path は実装済みだが、`perf.reference.v1` の 500,000 hot `TerrainBrickV1` を canonical initial-world material として生成する規則はまだ揃っていない。

本書は既存正本から確定済みの境界と、canonical Terrain content を作るために追加で正本化が必要な項目を分離する。未定義の SDF 値、material ID、cell origin、root topology を新しく提案しない。

machine-readable mirror は `Qa04TerrainCanonicalContentDependencyContractV1`。

この contract は `Qa04ReferenceWorldDependencyContractV1` の Terrain `CanonicalMaterial` blocker 1件の**下位分解**であり、reference-world blocker 数を 9 から増やさない。

## 2. 既に固定済み

### Benchmark descriptor

- profile: `perf.reference.v1`
- fixed WorldSeed: `000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f`
- hot Terrain descriptor count: **500,000**
- all hot Terrain descriptors: D0
- regional distribution: **64 × 64 = 4,096 tiles**
- descriptor identity: `Qa04ReferenceLoadV1.Record("spatial.hot-terrain-brick", ordinal)`
- regional tile identity/distribution is deterministic
- D0 sample spacing: **250 mm**

`Qa04ReferenceLoadV1.PositionWithinTile` supplies deterministic normalized X/Y benchmark distribution input, but there is no normative rule that maps this pair to Terrain `SpatialCellKeyV1(X,Y,Z)` or authoritative physical coordinates. A 2D normalized position must therefore not be promoted to a 3D Terrain cell mapping by implementation convention.

### SBO-SDF representation

`TerrainBrickV1` is fixed as:

- `brick_id`
- `level: uint8`
- `cell_origin: SpatialCellKeyV1`
- `sample_spacing_mm: uint32`
- `sdf_mm: int32[729]`
- `surface_material_id: uint16[512]`
- `revision: uint64`

SDF semantics:

- negative = solid
- zero = boundary
- positive = void

Canonical array order and Terrain v2 wire/schema/recovery are already implemented.

### Terrain v2 production path

Implemented:

- `terrain_root` / `terrain_brick` schema 2.0
- exact mixed-record state
- strict wire and semantic digest
- v1-root -> v2-root migration
- registered migration only
- exact-97 / exact-103 provider/recovery canaries
- actual serialized record-schema preservation
- `Qa04TerrainBrickDescriptorMaterializerV1`

The descriptor materializer requires exact descriptor/brick identity and D0 spacing but intentionally accepts cell origin, SDF, surface material, and revision only from an explicit content source. Supplying 500,000 records therefore proves descriptor coverage only; it does not prove canonical Terrain.

## 3. 未解決の canonical content dependencies

### 3.1 descriptor -> `SpatialCellKeyV1` mapping

Failure code:

`qa04.terrain.cell-origin-mapping-undefined`

正本化が必要:

- regional tile + descriptor identity/ordinal -> `SpatialCellKeyV1.Level/X/Y/Z`
- tile coordinate system and 3D origin relation
- duplicate/collision avoidance rule
- relationship between brick `Level` and `CellOrigin.Level`

既存 `TerrainBrickV1` constructor が両 level の一致を強制しないため、暗黙に同じと仮定しない。

### 3.2 SDF[729] generation

Failure code:

`qa04.terrain.sdf-generation-undefined`

正本化が必要:

- fixed WorldSeed / descriptor / sample coordinate から各 int32 SDF mm を得る exact recipe
- random context / domain-separation token if randomness is used
- range / overflow behavior
- neighboring brick boundary consistency rule
- terrain shape/height/solid-volume semantics

Synthetic smoke の平面、定数、ordinal-derived sample を benchmark genesis へ昇格しない。

### 3.3 surface material[512] generation

Failure code:

`qa04.terrain.surface-material-generation-undefined`

正本化が必要:

- each 8×8×8 cell -> uint16 material id の exact recipe
- SDF boundary/solid stateとの関係
- deterministic material assignment context
- material id reserved/invalid range if any

### 3.4 surface class vocabulary

Failure code:

`qa04.terrain.surface-class-tokens-undefined`

`terrain_root.surface_classes` を canonical にするため、少なくとも:

- token set
- token ordering
- material id -> surface class relation
- extensibility/versioning rule

が必要。

### 3.5 root / scope identity

Failure code:

`qa04.terrain.root-scope-identity-undefined`

正本化が必要:

- Terrain root record count
- root record identity derivation
- `scope_ref` owner/identity
- 500,000 hot bricks の root/scope assignment

### 3.6 root topology / connectivity

Failure code:

`qa04.terrain.root-topology-connectivity-undefined`

正本化が必要:

- `root_brick_ref` selection
- SBO octree logical topology for the 500,000 hot bricks
- parent/child relation or reconstruction rule
- `connectivity_refs` contents/order
- closure requirements for every referenced record

P4-04 の child-index/traversal ruleだけから benchmark topologyそのものは推測しない。

### 3.7 initial revision / lineage

Failure code:

`qa04.terrain.revision-lineage-undefined`

正本化が必要:

- initial brick/root revision
- geometry revision relation
- creation/basis step relation
- promotion/demotion lineage rule required by genesis material

`revision > 0` という runtime validationだけから canonical initial revisionを選ばない。

## 4. 上位 blocker との関係

`Qa04ReferenceWorldDependencyContractV1` の Terrain entry は引き続き exactly one:

- dependency: `spatial.terrain-geometry.root-brick-target`
- kind: `CanonicalMaterial`
- partition: `spatial.terrain_geometry`
- field: `root_brick_ref`
- compatibility failure code: `qa04.material.terrain-brick-authority-undefined`

新しい7 failure codesは診断用 subdependency code であり、reference-world `FailureCodes` へ追加しない。

したがって:

```text
reference-world blockers = 9
Terrain top-level world blockers = 1
Terrain canonical-content subdependencies = 7
```

を維持する。

## 5. Terrain top-level blockerを解消できる条件

次をすべて満たすまで `qa04.material.terrain-brick-authority-undefined` を除去しない。

1. 上記7 subdependencyが versioned normative rule として確定する。
2. production content source がその rule を実装する。
3. actual 500,000 hot bricks を materialize する。
4. root/scope/topology/connectivity/surface-class closure を materialize する。
5. exact v2 target-kind / Ref closure が通る。
6. normal exact-97 owner composition に入る。
7. exact-103 Snapshot -> compression/chunk/staging -> recovery -> semantic rehash が actual canonical materialで一致する。

この時点までは:

- `referenceWorldMaterialized=false`
- `authoritativeStepLoopAvailable=false`
- Terrain canonical benchmark material incomplete

を維持する。
