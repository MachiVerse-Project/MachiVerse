# Alpha 1.1 / INT-03 — 残り正本判断の一括確定

Status: Decided normative index / implementation in progress  
Tracking: Issue #240  
Implementation: Draft PR #265

## 1. 目的

Alpha 1.1 / INT-03 で、canonical reference-world / workload を production authority へ接続するために確定した正本判断の親インデックス。

本書は詳細field schemaを再定義しない。各scopeの専用specを正本とし、実装完了状態は machine-readable dependency contract と Issue #240 を優先する。

設計確定だけを理由に blocker を外してはならない。対応production implementation、negative test、Snapshot/recovery または runtime proof が同一branchで成立したscopeだけを解除する。

## 2. 現在の implementation checkpoint

current branchでは、旧9 reference-world blockerのうち7 scopeがproduction proofまで完了している。

active reference-world blockerは **2件**:

```text
society-governance.active-record.partition-mapping
infrastructure.network-topology.node-edge-targets
```

active canonical workload blockerは **3件**:

```text
qa04.workload.operation-authority-binding-undefined
qa04.workload.transaction-creation-binding-undefined
qa04.workload.detail-transition-request-binding-undefined
```

維持するrelease境界:

```text
referenceWorldMaterialized = false
authoritativeStepLoopAvailable = false
PR #265 = Draft
```

Terrainは full **500,000 hot + 4,096 root + 4,096 D3 anchor = 508,192 records** の production exact-103 Snapshot / Zstd staging / staged recovery / semantic rehashが成功済みで、旧 `qa04.material.terrain-brick-authority-undefined` はactive blockerではない。

## 3. 不変境界

```text
standard Domain partitions = 97
standard Snapshot sections = 6 Core + 97 Domain = 103
StandardDomainPartitionRegistry = v1 baseline
CrossDomainTransactionCandidateV1.IsAuthoritative = false
benchmark/reduced fixture != release evidence
```

Existing persisted v1 schemaはimmutable。ownership repairは同一schema idのexplicit major `2.0` と registered migrationで行う。

## 4. Normative document precedence

実装時の優先順位:

1. このindexから参照するAlpha 1.1専用spec
2. 既存P4-01..P4-08の標準設計
3. 旧audit文書

専用specと旧auditが衝突する場合、専用specとcurrent machine-readable contractを優先する。旧auditはhistory/背景説明としてのみ扱う。

## 5. Reference-world scopes

### 5.1 Physical collision shape — implemented

正本: `phase4-alpha11-physical-occupancy-v2.md`

- owner=`physical.occupancy`
- schema=`domain.physical.occupancy.record /2.0`
- kinds=`occupancy`,`collision_shape`
- v1 occupancy lossless migration
- QA-04 Physical descriptor materialization / production proof済み

旧 `qa04.material.physical-presence-shape-authority-undefined` はactive blockerではない。

### 5.2 Environment D0 / D1 — implemented

正本: `phase4-alpha11-reference-world-decomposition.md`

- D0 exact **1,000,000**
- D1 exact **250,000**
- D1はpartitionごとにD0 4件 -> 1件
- full materialization / Ref closure / Snapshot recovery / semantic rehash済み

旧Environment D0/D1 blockerはactiveではない。

### 5.3 Society / Governance 2,000,000 — active

正本:

- `phase4-alpha11-reference-world-decomposition.md`
- Market: `phase4-alpha11-market-transaction-v2.md`

```text
Society = 1,600,000
Governance = 400,000
total = 2,000,000
```

Market **1,000,100** と Household **40,000** はactual materialization済み。追加sliceは、required Token vocabularyやRef ownershipが既存正本で確定しているものだけを実装する。未定義Tokenをbenchmark用の推測値で埋めてはならない。

active parent blocker:

```text
society-governance.active-record.partition-mapping
```

### 5.4 Market authority — implemented

正本: `phase4-alpha11-market-transaction-v2.md`

- `society.market_transaction /2.0`
- market_state **100**
- open order **1,000,000**
- exact-97 / exact-103 migration, recovery, semantic proof済み

旧 `qa04.material.market-ref-authority-undefined` はactive blockerではない。

### 5.5 Infrastructure network/node/edge — active

正本:

- `phase4-alpha11-infrastructure-network-v2.md`
- `phase4-alpha11-reference-world-decomposition.md`

specialized topologyは **120,100 / 500,000** materialized。remaining generic recordsとactual Society Organization authorityへのRef closureが残る。

active parent blocker:

```text
infrastructure.network-topology.node-edge-targets
```

### 5.6 Terrain canonical material — implemented / blocker released

正本:

- `phase4-alpha11-terrain-canonical-generation.md`
- `phase4-terrain-geometry-record-v2.md`
- `phase4-terrain-v2-production-migration.md`

production proof:

- hot D0 500,000
- terrain root 4,096
- D3 anchor 4,096
- actual TileScope closure
- exact-103 streaming composition
- Zstd `MVCHNK01` + `manifest.pb`
- staged 2-pass recovery
- recovered RecordId全件一致
- root/anchor kind closure
- semantic rehash一致

旧 `qa04.material.terrain-brick-authority-undefined` はactive blockerではない。

### 5.7 `BodyRegionStateV1` — implemented

正本: `phase4-alpha11-body-region-state-v1.md`

exact nested schema / codec / material boundaryが実装済み。旧 body-region schema blockerはactiveではない。

### 5.8 Active CrossDomainTransaction persistent authority — implemented

正本:

- `phase4-alpha11-core-effect-custody-v2.md`
- `phase4-alpha11-cross-domain-transaction-persistent-authority.md`

persistent authority / Snapshot / recovery / detail-guard reconstructionがproduction pathへ接続済み。`CrossDomainTransactionCandidateV1` は引き続きnon-authoritative。

旧 `qa04.material.cross-domain-transaction-authority-undefined` はactive blockerではない。

## 6. Canonical workload scopes — 3件 active

正本: `phase4-alpha11-canonical-workload-v1.md`

### 6.1 Operation authority binding

`qa04.workload.operation-authority-binding-undefined`

### 6.2 Transaction creation binding

`qa04.workload.transaction-creation-binding-undefined`

### 6.3 Detail transition request binding

`qa04.workload.detail-transition-request-binding-undefined`

設計は確定済みだが、full authoritative 27,000-Step workloadへのproduction bindingが完了するまでは解除しない。

## 7. 共通 benchmark value/identity rule

Profile-specific scalar materialは各専用specを優先する。専用ruleがないscalarについては `mv.perf-reference-genesis-value.v1` の既存benchmark genesis value sourceを使用する。

これはarbitrary Token vocabularyを生成する規則ではない。`organization_class`、`job_token`、`contract_kind`、`right_kind`、`currency_token`、`dimension_token` 等について、専用正本にcanonical vocabularyがない場合は値を推測してはならない。

## 8. Current Stage 2 order

1. Society/Governance remaining materialを既存正本だけでmaterialize可能なsliceから接続
2. required Token vocabulary未定義境界を正本で解消
3. actual Organization authorityを成立させる
4. Infrastructure remaining material + actual Ref closure
5. world blockers `2 -> 0`
6. workload 3 bindings -> full authoritative Step loop
7. full canonical benchmark / persistence / publication evidence
8. workers 1/4/8/16 ×3 determinism evidence
9. 24h soak
10. `ReleaseAcceptanceRecordV1.result = PASS`

## 9. Blocker removal policy

machine-readable contractがcurrent source of truthである。各blockerは、そのscopeのimplementation + negative test + production-path proofが同一branchでgreenになった時点でのみ外す。

最後のworld blockerが外れても、actual full reference-world exact-103 recovery proof前に `referenceWorldMaterialized=true` を返してはならない。workload blockerが残る間は `authoritativeStepLoopAvailable=true` を返してはならない。
