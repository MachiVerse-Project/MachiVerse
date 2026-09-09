# Alpha 1.1 / INT-03 — 残り正本判断

## 目的

`perf.reference.v1` の canonical reference world を実 materialize するために、現在コードや既存設計から安全に導出できない判断だけを列挙する。

この文書は値や schema を新しく定義しない。既存事実と、実装開始に必要な最小の未決定事項を分離する。

machine-readable gate は `Qa04ReferenceWorldDependencyContractV1` を正とし、現在の unresolved blocker は 9 件。

## 共通原則

- benchmark count だけから partition split を推測しない。
- runtime CLR 型だけから persistence schema を自動生成しない。
- Ref owner が決定済みでも、target record schema が未定義なら record を捏造しない。
- synthetic smoke の値を canonical benchmark genesis に昇格しない。
- `CrossDomainTransactionCandidateV1` は `IsAuthoritative == false` のため durable authority とみなさない。
- `referenceWorldMaterialized=true` は以下の判断と actual materialization が完了するまで禁止。

## 1. Physical collision shape

### 確定済み

- `physical.presence.shape_ref` の owner は `physical.occupancy / collision_shape`。
- runtime には Sphere / Capsule / OrientedBox / ConvexPolytope の形状表現がある。
- ownership amendment は TriangleMeshStatic と permitted SDF reference form も考慮対象としている。

### 決定が必要

`physical.occupancy` v2 `collision_shape` record の exact discriminated schema:

- arm の集合
- 各 arm の field 名 / scalar kind / unit
- optionality
- canonical field order
- TriangleMeshStatic の vertex/index authority
- permitted SDF reference の target schema / closure
- initial revision / lineage rule

### 決定後の実装

schema 2.0 -> codec -> runtime lossless projection -> materializer -> Ref closure -> recovery rehash。

## 2. Environment D0 mapping

### 確定済み

- D0 total = 1,000,000。
- Environment は 13 standard partitions。
- P4-05 は各 partition payload family を固定済み。

### 決定が必要

1 D0 descriptor をどの authoritative record 群へ展開するか。

最低限:

- 13 partitions への exact count split
- 1 descriptor = 1 record か、複数 coupled records か
- record identity derivation
- canonical initial payload values
- cross-partition Ref closure

## 3. Environment D1 mapping

### 確定済み

- D1 total = 250,000。

### 決定が必要

- D1 aggregate の owner partitions
- exact count split
- aggregate key / scope identity
- initial aggregation payload
- D0 との lineage / source relation

## 4. Society / Governance 2,000,000 decomposition

### 確定済み

- Society/Economy 16 partitions + Governance/Security 17 partitions = 33 partitions。
- aggregate active-record target = 2,000,000。
- market load には 100 scopes × 10,000 active orders = 1,000,000 orders の別契約が存在する。

### 決定が必要

- 2,000,000 の 33 partitions への exact decomposition
- market 1,000,000 orders が 2,000,000 の内数か外数か
- class ごとの detail level
- genesis record identity / revision / initial values
- coupled Ref closure

## 5. Market record schema

### 確定済み

- `society.market_transaction.market_ref` owner は同 partition の `market_state`。
- runtime には `MarketOrderV1` の exact order fields がある。
- benchmark は 100 market scopes × 10,000 active orders/scope を固定。

### 決定が必要

`market_state` / `order_or_offer` / `transaction_or_price_fact` の exact v2 schema:

- record-kind arms
- market scope identity
- instrument identity/token
- order -> market Ref
- transaction/price fact fields
- canonical ordering
- initial market-state values

`MarketOrderV1` に存在しない値を補完して schema を作らない。

## 6. Infrastructure network/node/edge schema

### 確定済み

- `node_refs` / `edge_refs` owner は `infrastructure.network_topology` same partition。
- load のうち 20,000 nodes / 100,000 edges / 250,000 queued service requests は固定。
- runtime `InfrastructureEdgeV1` は edge id / from / to / cost を持つ。

### 決定が必要

- `network` / `node` / `edge` exact v2 record schema
- node class / scope / capacity / state
- edge capacity/state 等、runtime edge 型にない persistence fields の扱い
- 500,000 total に対する残り 130,000 records の class decomposition
- queued service request の owner / relation
- initial Ref closure

## 7. Terrain canonical material

### 確定済み

- production record schema 2.0 と migration/recovery path は実装済み。
- hot brick count = 500,000。
- D0 sample spacing = 250 mm。
- brick は SDF[729] / surface material[512]。
- `Qa04TerrainBrickDescriptorMaterializerV1` は descriptor ID と supplied `TerrainBrickV1` を厳密に binding する。

### 決定が必要

- 64×64 regional tile + descriptor -> `SpatialCellKeyV1` physical origin mapping
- WorldSeed -> 729 SDF sample generation
- 512 surface material ID generation
- canonical surface class tokens
- root / scope count and identity
- root brick / octree topology
- `connectivity_refs`
- initial revision / lineage

synthetic smoke valuesは canonical material にしない。

## 8. active CrossDomainTransaction 10,000 authority

### 確定済み

- deterministic benchmark descriptor / mix は存在。
- `CrossDomainTransactionCandidateV1.IsAuthoritative == false`。
- current SQLite production schema に active transaction table はない。
- current transition COMMIT durable API は active participants/status/invariants を lossless に保持しない。
- restart path から active transaction lifecycle を復元する recipe は存在しない。

### 決定が必要

次のどちらかを正本化する。

1. versioned persistent active-transaction authority schema
2. 既存 durable facts のみから lossless に復元できる exact versioned reconstruction recipe

必要情報:

- transaction identity
- participant set
- current lifecycle/status
- invariant/decision state
- basis step / revision
- restart reconstruction order

## 9. `BodyRegionStateV1` nested schema

### 確定済み

- parent field は `resident.body_health.body_region_states`。
- canonical order は region token 基準まで固定。
- whole-resident health runtime state は存在するが、body-region record と同一とする根拠はない。

### 決定が必要

- exact field set
- field ordinal / wire order
- scalar kind / units
- optionality
- injury / disease / impairment representation
- region token vocabulary / extensibility rule
- nested Ref の有無と target schema

## 判断後の実装順

依存を最小化する推奨順序:

1. `BodyRegionStateV1`
2. Physical v2 schema
3. Infrastructure v2 schema
4. Market v2 schema
5. Environment D0/D1 decomposition
6. Society/Governance decomposition
7. CrossDomainTransaction persistent/reconstruction authority
8. Terrain canonical genesis rule
9. full canonical world composition / Ref closure
10. canonical workload
11. 12 benchmark runs
12. persistence / publication evidence
13. 24h soak
14. `ReleaseAcceptanceRecordV1 = PASS`

この順序は schema を先に閉じ、巨大 materialization を最後にまとめて再実行できるようにするための実装順であり、未定義 semantics の提案ではない。
