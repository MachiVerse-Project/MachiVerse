# Alpha 1.1 / INT-03 — canonical Terrain content bindings

Status: Decided normative design / implementation pending  
Tracking: #240  
Implementation: Draft PR #265

## 1. 現在の正本

Terrain v2 production migration / Snapshot / recovery pathに加え、以前未決定だった `perf.reference.v1` canonical contentも設計済み。

Exact canonical generation spec:

- `phase4-alpha11-terrain-canonical-generation.md`

Parent:

- `phase4-alpha11-normative-closure.md`

## 2. 確定した内容

- 64x64 tile /512m tile lattice
- 4096 `terrain_root`
- 4096 D3 root-anchor bricks
- existing500,000 hot descriptor -> collision-free D0 slot/cell origin
- WorldSeed + absolute XYによるcanonical terrain height
- exact729 SDF generation
- exact512 surface material generation
- material ids 0..3 / surface class vocabulary
- D3 fallback + D0 sparse refinement lookup
- N/E/S/W root connectivity
- genesis record revision / geometry revision / lineage semantics
- shared sample boundary equality

## 3. machine-readable blocker

Terrain canonical-content dependencyは設計上解消したが、production materializerが未完成なので次を維持する。

```text
qa04.material.terrain-brick-authority-undefined
```

`Qa04TerrainCanonicalContentDependencyContractV1` の旧subdependency codeもimplementation migration中のdiagnostic compatibilityとして保持してよい。実装が各項目をproveした時点で段階的にassertへ置換する。

500,000 actual D0 bricks +4096 roots/anchorsがexact103 Snapshot/recovery/semantic rehashを通るまで `referenceWorldMaterialized=true` を返さない。
