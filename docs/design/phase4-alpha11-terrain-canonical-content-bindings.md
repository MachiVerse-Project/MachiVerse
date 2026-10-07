# Alpha 1.1 / INT-03 — canonical Terrain content bindings

Status: Decided normative design / production implementation proven  
Tracking: #240  
Implementation: Draft PR #265

## 1. 現在の正本

Terrain v2 production migration / Snapshot / recovery pathと、`perf.reference.v1` canonical content generationは実装・production proofまで完了した。

Exact canonical generation spec:

- `phase4-alpha11-terrain-canonical-generation.md`

Parent:

- `phase4-alpha11-normative-closure.md`

## 2. 確定・実装済みの内容

- 64x64 tile / 512m tile lattice
- 4,096 `terrain_root`
- 4,096 D3 root-anchor bricks
- existing 500,000 hot descriptor -> collision-free D0 slot/cell origin
- WorldSeed + absolute XYによるcanonical terrain height
- exact 729 SDF generation
- exact 512 surface material generation
- material ids 0..3 / surface class vocabulary
- D3 fallback + D0 sparse refinement lookup
- N/E/S/W root connectivity
- genesis record revision / geometry revision / lineage semantics
- shared sample boundary equality
- 508,192-record lightweight locator / on-demand production source
- streaming frozen Terrain authority/header
- 2-pass bounded-memory fragment source
- exact-97 / exact-103 streaming production composition
- actual Zstd `MVCHNK01` / `manifest.pb` staging
- same staged Snapshotを使う2-pass recovery
- recovered 508,192 RecordIdの全件一致
- post-recovery Terrain semantic rehash一致

## 3. machine-readable blocker

旧 compatibility failure code:

```text
qa04.material.terrain-brick-authority-undefined
```

は、full **500,000 hot + 4,096 root + 4,096 D3 anchor = 508,192 records** がproduction exact-103 Snapshot / Zstd staging / staged recovery / semantic rehashをcurrent branchで完走したため、active `Qa04ReferenceWorldDependencyContractV1` から解除済み。

`Qa04TerrainCanonicalContentDependencyContractV1.ParentWorldDependencyId` / `ParentWorldFailureCode` は、再発検出用のcompatibility regression constantとしてのみ保持する。active blockerとして再登録してはならない。

## 4. release境界

Terrain-specific production proofの完了は、full reference world全体の完成を意味しない。

引き続き:

```text
referenceWorldMaterialized = false
```

を維持する。理由は Society/Governance と Infrastructure のcanonical production material / Ref closureが未完成だからである。

また、Terrain preflight / reduced canaryは今後もregression evidenceとして保持するが、Terrain blocker解除の根拠はそれらではなくfull 508,192-record production proofである。
