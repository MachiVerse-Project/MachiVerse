# Phase 4 QA-04 canonical reference world materialization 監査

Status: 実装阻害要因の監査中  
Tracking: #240  
Implementation: Draft PR #265  
Profile: `perf.reference.v1`

## 目的

この文書は、QA-04 が固定済みの deterministic descriptor と、実際の production authoritative material の境界を記録する。

`Qa04ReferenceLoadV1` は以下を既に固定している。

- 8 initial-world class の件数
- record creation ordinal から導出する identity
- Resident detail 分布
- 64 × 64 regional tile 分布
- D0 dense-region 選択
- `perf.reference.position.v1` による tile 内位置 descriptor

`Qa04ReferenceScenariosV1` は transaction mix、market scope/order identity、Infrastructure node/edge/request identity、各 load selector を固定している。

ただし descriptor が deterministic であることと、`referenceWorldMaterialized=true` は同義ではない。各 class は exact P4-05 payload と実在する Ref target を持つ production authority へ materialize されなければならない。

## 現在の binding 状態

| `perf.reference.v1` class | count | production binding | 状態 |
|---|---:|---|---|
| `resident.persistent-identity` | 1,000,000 | `resident.identity_lifecycle` | 実 materializer 完了 |
| `physical.d0-presence` | 500,000 | `physical.presence` + `physical.occupancy/collision_shape` | ownership 決定済み、exact lossless v2 collision-shape schema/materializer 未確定 |
| `environment.d0-cell-cohort` | 1,000,000 | Environment 13 partitions | class-to-partition split / canonical initial values 未確定 |
| `environment.d1-aggregate` | 250,000 | Environment 13 partitions | aggregate-to-partition split / canonical initial values 未確定 |
| `society-governance.active-record` | 2,000,000 | Society/Governance 33 partitions | decomposition 未確定 |
| `infrastructure.active-record` | 500,000 | `infrastructure.network_topology/{network,node,edge}` + service authority | ownership 決定済み、exact v2 node/edge schema/materializer 未確定 |
| `spatial.hot-terrain-brick` | 500,000 | `spatial.terrain_geometry/terrain_brick` v2 | production migration path 完了、canonical Terrain content 未確定 |
| `transaction.active-cross-domain` | 10,000 | 未確定 | candidate は non-authoritative、persistent/reconstruction authority 未確定 |

## Terrain 500,000 の現在地

Terrain は authority/schema/wire/recovery の問題と benchmark content の問題を分離する。

### 完了済み

- `root_brick_ref` owner = `spatial.terrain_geometry/terrain_brick`
- `terrain_root` / `terrain_brick` record schema 2.0
- `TerrainBrickV1` の exact lossless v2 record mapping
- 729 SDF sample / 512 surface material id の exact wire
- v1 root -> v2 root migration
- exact-97 authority/provider integration
- recovery Phase 1 / Phase 2
- actual record schema 2.0 を保持する all-97 resolver
- Spatial v2 owner material seam
- exact-97 / exact-103 semantic canary
- QA-04 hot-terrain-brick descriptor count = 500,000
- descriptor record id / D0 classification
- D0 sample spacing = 250 mm
- descriptor id と supplied `TerrainBrickV1.brick_id` を fail-closed で結ぶ materialization boundary

`Qa04TerrainBrickDescriptorMaterializerV1` は、QA-04 descriptor identity を exact Terrain v2 brick record へ結ぶ。ただし値を生成しない。cell origin、SDF、surface material、revision は明示的な content source からのみ受け取る。

### 未確定

現在の正本仕様には、次を canonical に決める規則がない。

1. 64 × 64 regional tile と tile 内 descriptor を `SpatialCellKeyV1` の `(level,x,y,z)` へ変換する物理座標規則
2. 500,000 brick の 729 SDF sample を WorldSeed から生成する規則
3. 512 surface material id を生成・割り当てする規則
4. Terrain surface class の canonical token 集合
5. root record と scope record の個数・identity・対応関係
6. root brick 選択と octree/root topology
7. `connectivity_refs` の canonical population
8. Terrain revision / lineage の initial-world 規則

P4-04 は SBO-SDF の表現、sample cardinality、符号意味、D0=250 mm spacing、octree traversal を固定している。しかし benchmark 固有の地形形状そのものは固定していない。

そのため flat terrain、random noise、all-solid、all-void などを勝手に採用して canonical benchmark material と扱ってはならない。

既存 failure code `qa04.material.terrain-brick-authority-undefined` は、名称が現在の詳細理由より広いが、500,000 canonical Terrain material が成立するまで conservative release gate として維持する。

## Physical D0 500,000

ownership は次で決定済み。

```text
physical.presence.shape_ref
  -> physical.occupancy / collision_shape
```

残る問題は authority owner ではなく、Sphere / Capsule / OrientedBox / ConvexPolytope / TriangleMeshStatic / 許可する SDF reference form を lossless に表現する exact v2 `collision_shape` record arm である。

P4-04 の runtime algorithm type だけから field layout、canonical ordering、Ref semantics を推測して v2 schema を作らない。

## Environment D0 / D1

P4-06 `perf.reference.v1` は次だけを canonical input として固定する。

- `environment.d0-cell-cohort` = 1,000,000
- `environment.d1-aggregate` = 250,000
- D0/D1 detail class
- deterministic identity / tile distribution
- precipitation、surface-flow、hazard、ecosystem、contaminant の load selector

Phase 3 Environment design と P4-05 は、13 semantic partitions と各 partition の意味・payload を固定する。

しかし両仕様を結ぶ次の rule は存在しない。

- D0 1,000,000 descriptor が atmosphere / weather / soil / ecosystem 等のどれへ何件 materialize されるか
- 1 descriptor が単一 record なのか複数 coupled record なのか
- D1 250,000 の exact owner split
- D0/D1 の canonical initial payload values
- coupled Environment record 間の initial Ref closure

したがって benchmark count/detail だけから partition split を導出してはならない。`qa04.material.environment-d0-partition-mapping-undefined` と `qa04.material.environment-d1-partition-mapping-undefined` は維持する。

## Society / Governance 2,000,000

33 partitions 全体に対する exact decomposition が未確定。

market ownership は次で決定済み。

```text
society.market_transaction.market_ref
  -> society.market_transaction / market_state
```

ただし `market_state` / `order_or_offer` / `transaction_or_price_fact` の exact v2 field schema と、2,000,000 aggregate class との関係は未確定。

## Infrastructure 500,000

ownership は次で決定済み。

```text
infrastructure.network_topology.node_refs -> same partition / node
infrastructure.network_topology.edge_refs -> same partition / edge
```

P4-06 は 20,000 node、100,000 edge、250,000 queued service request を固定しているが、`network/node/edge` v2 record の exact payload field と残り active-record class の構成が未確定。

## active CrossDomainTransaction 10,000

QA-04 は 10,000 deterministic descriptor と transaction mix を持つ。

現在の `CrossDomainTransactionCandidateV1` は Step candidate であり `IsAuthoritative == false`。candidate を Snapshot persistent authority として保存してはならない。

必要なのは persistent active-transaction owner、または 103 sections 内の既存 durable authority から 10,000 active state を exact に再構成できる normative rule である。

## nested payload schema

### 解消済み: `resident.perception.perceived_facts`

既存 runtime authority `ResidentPerceptionObservationV1` を exact source として、`domain.resident.perceived-fact / 1.0` の nested schema / codec / runtime reconstruction を固定済み。

- fact identity
- resident / subject identity
- proposition
- source delivery
- confidence
- perceived step
- fact-id canonical order
- standard payload wire roundtrip / semantic digest

CLR reflection / JSON fallback は使用しない。

### 解消済み: `governance.law_rule` Rule AST

既存 runtime authority `LawPredicateNodeV1` / `LawEffectV1` を exact source として次を固定済み。

```text
domain.governance.rule-predicate-ast / 1.0
domain.governance.rule-effect-ast / 1.0
```

predicate は runtime の8 fieldを lossless に保持し、`children` に限って同一 schema への explicit self-recursion を許可する。他 nested schema の再帰は引き続き fail-closed。

runtime AST -> nested wire -> runtime AST、decode->encode canonicality、full `governance.law_rule` payload、production provider/recovery semantic rehash を smoke で確認済み。

### 未解消: `resident.body_health.body_region_states`

P3 Resident design は粗い身体部位・負傷・疾病・障害・回復等を扱える conceptual body/health state を要求するが、`BodyRegionStateV1` の exact field set は固定していない。

P4-05 が固定するのは:

```text
body_region_states : ordered list<BodyRegionStateV1>
```

と region token canonical order までであり、次は未定義。

- region record の exact fields
- scalar kinds / units
- required / optional
- injury/disease との nested-vs-Ref 境界
- capability / integrity / pain 等を region record に持つか
- canonical wire field order

runtime の `ResidentHealthStateV1(HealthCapacity, Pain, Stress, Fatigue)` は whole-resident state であり、これを body-region state とみなす根拠はない。

そのため `qa04.material.body-region-state-schema-undefined` は維持する。

## dependency contract checkpoint

exact nested schema 2件の解消により、`Qa04ReferenceWorldDependencyContractV1` の unresolved blocker は **9件**。

- Physical shape authority
- Environment D0 mapping
- Environment D1 mapping
- Society/Governance decomposition
- Market authority
- Infrastructure node/edge authority
- Terrain canonical material gate
- active CrossDomainTransaction persistent authority
- BodyRegionState nested schema

## 完了条件

`referenceWorldMaterialized=true` は、8 initial-world class すべてについて次が成立した場合のみ許可する。

1. header/count placeholder ではなく実 authoritative record を生成する
2. exact owner payload/schema contract を使う
3. required Ref が同一 world の実 record に closure する
4. material から canonical partition/state digest を生成する
5. 実 6 Core + 97 Domain = 103 section Snapshot -> Zstd/chunk -> manifest -> staging -> recovery -> semantic rehash に参加する

reduced canary の typed-empty root や synthetic Terrain content は infrastructure test としてのみ使用でき、non-empty `perf.reference.v1` material として数えない。

PR #265 は引き続き Draft とし、Stage 2 / Alpha 1.1 release gate は未完了のままとする。
