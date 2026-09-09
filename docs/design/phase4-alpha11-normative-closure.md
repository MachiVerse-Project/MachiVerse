# Alpha 1.1 / INT-03 — 残り正本判断の一括確定

Status: Decided normative design / implementation pending  
Tracking: Issue #240  
Implementation: Draft PR #265

## 1. 目的

Alpha 1.1 / INT-03 で「設計不足のため実装を止めていた」canonical reference-world 9 blocker と canonical workload 3 blockerについて、実装開始に必要な正本判断をすべて確定したことを示す親インデックス。

本書以降、「仕様が未決定」であることを理由に12 blockerの実装を止めない。残るのは実装・migration・materialization・recovery・full benchmark / release evidenceである。

ただし、**設計確定は実装完了ではない**。対応コードとproduction-path proofが完成するまで既存failure codeを除去せず、`referenceWorldMaterialized=false`、`authoritativeStepLoopAvailable=false`、PR #265 Draftを維持する。

## 2. 不変境界

以下は今回の設計でも変更しない。

```text
standard Domain partitions = 97
standard Snapshot sections = 6 Core + 97 Domain = 103
StandardDomainPartitionRegistry = v1 baseline
CrossDomainTransactionCandidateV1.IsAuthoritative = false
benchmark/reduced fixture != release evidence
```

Existing persisted v1 schemaはimmutable。ownership repairは同一schema idのexplicit major `2.0` と registered migrationで行う。

## 3. Normative document precedence

実装時の優先順位:

1. このindexから参照するAlpha 1.1専用spec
2.既存P4-01..P4-08の標準設計
3. 旧audit文書

専用specと旧auditが衝突する場合、専用specが正本。旧auditはhistory/背景説明としてのみ扱う。

## 4. Reference-world blocker 9件 — 全設計確定

### 4.1 Physical collision shape

正本:

- `phase4-alpha11-physical-occupancy-v2.md`

決定:

- owner=`physical.occupancy`
- schema=`domain.physical.occupancy.record /2.0`
- kinds=`occupancy`,`collision_shape`
- Sphere/Capsule/OBB/Convex/StaticMesh/TerrainSdfRef exact arms
- v1 occupancy lossless migration
- 500k Physical descriptorのpresence/occupancy/shape material rule

Implementation blocker codeは実装完了まで維持:
`qa04.material.physical-presence-shape-authority-undefined`

### 4.2 Environment D0 / D1

正本:

- `phase4-alpha11-reference-world-decomposition.md`

決定:

- D0 exact 1,000,000 partition split
- D1 exact 250,000 split
- partitionごとにD0 4件 -> D1 1件、全D0 source exactly-once
- canonical genesis token/scalar/Ref/topology rule

Implementation blocker codes:

- `qa04.material.environment-d0-partition-mapping-undefined`
- `qa04.material.environment-d1-partition-mapping-undefined`

### 4.3 Society / Governance 2,000,000

正本:

- `phase4-alpha11-reference-world-decomposition.md`
- Market部分は `phase4-alpha11-market-transaction-v2.md`

決定:

```text
Society = 1,600,000
Governance = 400,000
total = 2,000,000
```

1,000,000 active Market orders + 100 MarketStateはこの2Mの内数。

Implementation blocker:
`qa04.material.society-governance-partition-mapping-undefined`

### 4.4 Market authority

正本:

- `phase4-alpha11-market-transaction-v2.md`

決定:

- owner=`society.market_transaction`
- schema=`domain.society.market_transaction.record /2.0`
- kinds=`market_state`,`order_or_offer`,`transaction_or_price_fact`
- v1 fact lossless migration
- 100 market states + 1,000,000 open orders exact genesis

Implementation blocker:
`qa04.material.market-ref-authority-undefined`

### 4.5 Infrastructure network/node/edge

正本:

- `phase4-alpha11-infrastructure-network-v2.md`
- count decomposition=`phase4-alpha11-reference-world-decomposition.md`

決定:

- owner=`infrastructure.network_topology`
- schema=`domain.infrastructure.network_topology.record /2.0`
- kinds=`network`,`node`,`edge`
- exact 100 network /20k node /100k edge
- total Infrastructure active material=500k

Implementation blocker:
`qa04.material.infrastructure-node-edge-authority-undefined`

### 4.6 Terrain canonical material

正本:

- `phase4-alpha11-terrain-canonical-generation.md`

決定:

- 64x64 tile lattice /512m tile
- 4096 terrain roots +4096 D3 anchors
- existing 500k hot descriptors -> exact D0 cell origins
- WorldSeed-based canonical height/SDF
- 4 material ids / surface vocabulary
- coarse D3 fallback + sparse D0 refinement
- N/E/S/W root connectivity
- exact revision/lineage semantics

Implementation blocker:
`qa04.material.terrain-brick-authority-undefined`

### 4.7 `BodyRegionStateV1`

正本:

- `phase4-alpha11-body-region-state-v1.md`

決定:

- schema=`domain.resident.body-region-state /1.0`
- exact 8 required fields
- exact 7-region vocabulary
- ppm semantics/order/canonical wire
- healthy benchmark genesis values

Implementation blocker:
`qa04.material.body-region-state-schema-undefined`

### 4.8 Active CrossDomainTransaction persistent authority

正本:

- `phase4-alpha11-core-effect-custody-v2.md`

決定:

- owner=Core effect custody
- exact103維持: existing `core.operation-state` section schema2.0へ拡張
- Operationとtransaction stateをlogical collection分離
- SQLite `cross_domain_transaction_state`
- `persistence.transition-committed /2.0` transaction state changes
- Snapshot/replay/detail-guard reconstruction
- exact10k active +300-Step/1000件 turnover

Implementation blocker:
`qa04.material.cross-domain-transaction-authority-undefined`

## 5. Canonical workload blocker 3件 — 全設計確定

正本:

- `phase4-alpha11-canonical-workload-v1.md`

### 5.1 Operation authority binding

6 benchmark familyを既存production OperationKindへ固定。SameStepOrderKey、admission、effective Step、conflict scope、payload selector、descriptor/durable digest equalityまで確定。

Implementation blocker:
`qa04.workload.operation-authority-binding-undefined`

### 5.2 Transaction creation binding

12 benchmark bucketsを17-kind registryへexact mapping。other bucketの6-kind allocation、participant domain/partition、intent/digest/invariant、turnoverまで確定。

Implementation blocker:
`qa04.workload.transaction-creation-binding-undefined`

### 5.3 Detail transition request binding

`CandidateRecordCount`はproduction `EstimatedRecordCount` budget inputとして固定。27,000-Step runで89 cadence ×16=1,424 unique tile regionsを使用し、trigger/request/hysteresis/budget/defer/conservation semanticsを確定。

Implementation blocker:
`qa04.workload.detail-transition-request-binding-undefined`

## 6. 共通 benchmark value/identity rule

Profile-specific arbitrary scalar materialが必要な場合、hidden defaultを置かず次を使う。

```text
H(record_id, field_tag) = HashSuite.DomainHash(
  "mv.perf-reference-genesis-value.v1",
  MV-DCBOR ["perf.reference.v1", WorldSeed, record_id, field_tag])
```

Scalar conversionは各専用specのrange/unitを優先する。専用ruleがない場合のみ:

```text
Step/start = 0
record revision = 1
optional terminal/end = NONE
status/lifecycle = active
availability/integrity ppm = 1,000,000
other bounded ppm = 500,000 + U64(H)%400,001
positive count = 1 + U64(H)%1,000
money = 1,000 + U64(H)%1,000,000
small signed value = int(U64(H)%2,001)-1,000
Digest = H
```

これはbenchmark profile専用であり、ゲーム本編の自然値/defaultを意味しない。

## 7. 設計完了判定

この文書群で、#240に記録されていた「正本判断がなく実装不能」な項目はすべて設計済みとする。

今後のStage 2残作業はimplementation gate:

1. Physical v2
2. Environment D0/D1 materializer
3. Society/Governance 2M materializer
4. Market v2
5. Infrastructure v2 +500k materializer
6. Terrain canonical generator
7. BodyRegion nested codec/materializer
8. Core effect custody/transaction persistence v2
9. canonical Operation workload path
10. canonical transaction turnover path
11. canonical detail transition path
12. full exact97 state -> exact103 Snapshot/recovery
13. full27,000-Step authoritative loop
14. workers1/4/8/16×3 deterministic benchmark
15. persistence/publication real evidence
16. 24h soak
17. `ReleaseAcceptanceRecordV1.result=PASS`

## 8. Blocker removal policy

`Qa04ReferenceWorldDependencyContractV1` 9件と `Qa04CanonicalWorkloadDependencyContractV1` 3件は、設計済みになっても**対応production implementationが存在しない間は failure codeを維持**する。

各codeは、そのscopeのimplementation + negative test + production-path Snapshot/recoveryまたはruntime proofが同一branchでgreenになった時点で外す。

最後のcodeが外れてもactual full reference world / exact103 recovery proof前に `referenceWorldMaterialized=true` を返してはならない。
