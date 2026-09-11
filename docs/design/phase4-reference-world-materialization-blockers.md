# Phase 4 QA-04 canonical reference world materialization 監査

Status: Current implementation audit  
Tracking: #240  
Implementation: Draft PR #265  
Profile: `perf.reference.v1`

## 1. 目的

この文書は、QA-04 deterministic descriptor と production authoritative material の現在の境界を記録する。

実装状態のsource of truthは `Qa04ReferenceWorldDependencyContractV1` と Issue #240。旧auditで記録したblocker名がcurrent machine contractと衝突する場合、current machine contractを優先する。

## 2. Current checkpoint

active reference-world blockerは **2件**:

```text
society-governance.active-record.partition-mapping
infrastructure.network-topology.node-edge-targets
```

release flags:

```text
referenceWorldMaterialized = false
authoritativeStepLoopAvailable = false
```

current materialized checkpoint:

| class / scope | current authority |
|---|---|
| Resident identity | 1,000,000 actual records |
| Physical | implemented production authority |
| Environment D0 | 1,000,000 actual records |
| Environment D1 | 250,000 actual records |
| Society Market | 1,000,100 actual records |
| Society Household | 40,000 actual records |
| Governance Polity | 1,000-record materializer added; CI verification required before counting as accepted checkpoint |
| Terrain | 508,192 actual v2 records, full production proof complete |
| CrossDomainTransaction | persistent authority implemented |
| Infrastructure topology | 120,100 / 500,000 specialized material |

## 3. Resolved former blockers

以下は旧auditでは未解消だったが、current branchではactive world blockerではない。

### Physical shape authority

`physical.occupancy /2.0` のcollision-shape ownership / migration / materialization / production proofが成立済み。

### Environment D0 / D1 mapping

exact D0 1,000,000 / D1 250,000 のpartition decomposition、genesis values、Ref closure、Snapshot recovery、semantic rehashが成立済み。

### Market authority

`society.market_transaction /2.0` で market_state 100 + open order 1,000,000 のactual authorityが成立済み。

### Terrain canonical material

旧 failure code:

```text
qa04.material.terrain-brick-authority-undefined
```

はactiveではない。

full production proofは次を満たす。

- hot D0 500,000
- terrain root 4,096
- D3 anchor 4,096
- total 508,192 records
- canonical payload on-demand generation
- actual TileScope closure
- exact-103 streaming Snapshot composition
- Zstd `MVCHNK01` staging + durable `manifest.pb`
- same staged SnapshotからScope/Terrainを2-pass recovery
- recovered RecordId全件一致
- root/anchor kind closure
- post-recovery semantic rehash一致

### `resident.body_health.body_region_states`

exact nested schema / codec / runtime material boundaryは実装済み。旧 body-region schema blockerはactiveではない。

### active CrossDomainTransaction 10,000

persistent authority / SQLite state / Snapshot / recovery / reconstruction pathは実装済み。`CrossDomainTransactionCandidateV1` は引き続きnon-authoritativeであり、candidate自体をpersistent authorityとして保存しない。

## 4. Active blocker: Society / Governance 2,000,000

exact decomposition:

```text
Society = 1,600,000
Governance = 400,000
total = 2,000,000
```

accepted material before the new Polity CI checkpoint:

```text
Market     = 1,000,100
Household  =    40,000
total      = 1,040,100
```

残りpartitionの多くはrequired arbitrary Tokenに依存する。例:

```text
society.organization.organization_class
society.employment.job_token
society.contract_claim.contract_kind
society.property_right.right_kind
society.currency_money.currency_token
society.reputation.dimension_token
```

`phase4-alpha11-normative-closure.md` のscalar hash ruleはarbitrary Token vocabularyを生成する根拠ではない。専用正本にcanonical token setがない値を `perf.*` などで推測してはならない。

required relation Refは `phase4-alpha11-reference-world-decomposition.md` のcanonical poolsへ閉じる。

current active parent blocker:

```text
society-governance.active-record.partition-mapping
```

解除条件:

1. exact 2,000,000 production records
2. required Token vocabularyが正本で定義済み
3. actual cross-partition Ref closure
4. exact-97 authority / exact-103 Snapshot
5. staging / recovery / semantic rehash
6. machine blocker removal smoke green

## 5. Active blocker: Infrastructure 500,000

current specialized topology:

```text
network =    100
node    = 20,000
edge    =100,000
total   =120,100
```

remaining generic materialは **379,900**。

内部node/edge closureとTileScope authorityは成立済み。残る `operator_refs` 等のclosureにはactual `society.organization` authorityが必要であり、synthetic Organization targetを作ってはならない。

current active parent blocker:

```text
infrastructure.network-topology.node-edge-targets
```

解除条件:

1. full 500,000 production material
2. actual Society/Spatial Ref closure
3. exact-97 / exact-103 production Snapshot
4. staging / recovery / semantic rehash
5. machine blocker removal smoke green

## 6. 完了条件

`referenceWorldMaterialized=true` は、8 initial-world classすべてについて次が成立した場合のみ許可する。

1. placeholderではなくactual authoritative record
2. exact owner payload/schema contract
3. required Refが同一worldのactual recordへclosure
4. canonical partition/state digest
5. real 6 Core + 97 Domain = 103 section Snapshot
6. Zstd/chunk -> manifest -> staging -> recovery -> semantic rehash

synthetic/reduced/preflight evidenceをrelease evidenceとして数えない。PR #265はStage 2完了までDraftを維持する。
