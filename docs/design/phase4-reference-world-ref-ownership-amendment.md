# Phase 4 QA-04 reference-world Ref ownership amendment

状態: 正本として決定済み / Terrain production migration path 実装済み / その他 repaired schema は未完了  
追跡: #240  
実装: Draft PR #265  
対象: `perf.reference.v1`

## 1. 目的

Stage 2 materialization 監査では、P4-05 に必須 `Ref` / `RefList` がある一方、参照先の semantic family は既存 97 standard partitions のいずれかに属するにもかかわらず、現在の v1 record schema に target を所有できる record arm がないケースが4件見つかった。

この amendment は **ownership** を固定し、決定済み target を実 materialize 可能にするための record-schema repair を追跡する。すべての repaired v2 partition が production-active であることや、canonical benchmark material が投入済みであることは意味しない。production migration/materializer と独立 dependency が残る間、`Qa04ReferenceWorldDependencyContractV1` の blocker は維持し、`referenceWorldMaterialized` は false のままとする。

## 2. 不変条件

repair は次をすべて維持しなければならない。

- standard Domain partition count は正確に **97** のまま。
- Snapshot section count は正確に **6 Core + 97 Domain = 103** のまま。
- benchmark のためだけに新しい authority partition を追加しない。
- Ref を意味的に無関係な record へ向けない。
- 既存の persisted v1 record schema は immutable とする。
- repaired partition で mixed record kinds を materialize する前に、明示的な record-schema **major version 2.0** を使用する。
- Snapshot recovery は serialized record schema version を保持し、未知 version / record kind を fail-closed する。

## 3. 決定済み ownership

| source field | authoritative target partition | required target record kind | required v2 record kinds |
|---|---|---|---|
| `physical.presence.shape_ref` | `physical.occupancy` | `collision_shape` | `collision_shape`, `occupancy` |
| `spatial.terrain_geometry.root_brick_ref` | `spatial.terrain_geometry` | `terrain_brick` | `terrain_brick`, `terrain_root` |
| `infrastructure.network_topology.node_refs` | `infrastructure.network_topology` | `node` | `edge`, `network`, `node` |
| `infrastructure.network_topology.edge_refs` | `infrastructure.network_topology` | `edge` | `edge`, `network`, `node` |
| `society.market_transaction.market_ref` | `society.market_transaction` | `market_state` | `market_state`, `order_or_offer`, `transaction_or_price_fact` |

machine-readable mirror は `Qa04ReferenceWorldRefOwnershipContractV1` とする。

## 4. ownership の理由

### 4.1 Collision shape -> `physical.occupancy`

P4-01 は collision / occupancy state を `physical_built` に割り当て、`physical.occupancy` は既存 standard partition のうち collision occupancy semantics を所有する。P4-04 も standard collision geometry family を定義している。したがって `collision_shape` record arm は、98番目の partition を作ったり無関係な Spatial material に向けたりせず、既存 Physical/Built authority 内に置く。

依存 blocker を外す前に、Sphere / Capsule / OrientedBox / ConvexPolytope / TriangleMeshStatic / permitted SDF reference form を lossless に表す exact v2 shape arm の canonical fields を固定する必要がある。

runtime では Sphere / Capsule / OrientedBox / ConvexPolytope に加え、static triangle mesh の canonical triangle index と頂点表現までは存在する。一方、terrain SDF collision を persistence `collision_shape` として表す target/ref/transform fields は未確定なので、runtime CLR shape だけから v2 schema を推測しない。

### 4.2 Terrain brick -> `spatial.terrain_geometry`

P4-01 は natural terrain solid/void geometry authority を `spatial` に割り当て、P4-04 は `TerrainBrickV1` を SBO-SDF algorithm state として定義している。したがって root record と brick records は同じ `spatial.terrain_geometry` authority family に属する。

exact v2 record shape は `phase4-terrain-geometry-record-v2.md` で固定し、Terrain v2 record/state/wire contract として実装済み。

- `terrain_root` は明示的 `record_kind` を持つ lossless v1-root arm。
- `terrain_brick` は `TerrainBrickV1.brick_id -> record_id`、`TerrainBrickV1.revision -> record revision` を保持。
- brick payload は `level`、`SpatialCellKeyV1`、spacing、729 SDF samples、512 material ids を lossless に保持。
- valid v2 material は decode -> encode で canonical。
- local root reference は actual `terrain_brick` arm に解決。
- semantic payload digest / partition-header rehash が mixed record material を拘束。

production migration boundary は `phase4-terrain-v2-production-migration.md` で別途固定済み。

- 明示的な `1.0 -> 2.0` record-schema migration registry entry。future version wildcard acceptance は行わない。
- exact-97 authority set は registered migration のみ許可。
- production/recovered all-97 reference resolver は actual schema を保持。
- cross-partition Ref schema validation は explicit migration registry を使用。
- caller の通常 97-provider set を維持したまま、authority schema から `SpatialTerrainGeometrySnapshotSectionProviderV2` を選択。
- recovery Phase 1 で Terrain v2 structural recovery。
- recovery Phase 2 で semantic reconstruction / target-kind closure / partition rehash。
- exact-97 production canary は 96 standard-v1 authorities + 1 registered Terrain-v2 authority。

`StandardDomainPartitionRegistry` はこの migration でも意図的に record schema v1 のままとする。production persistence は global registry を書き換えず、actual authority schema から registered v2 path を選択する。

これにより Terrain ownership、field-schema、mixed-record state、wire、provider-selection、reference-schema、recovery-path の曖昧さは解消済み。ただし `perf.reference.v1` の 500,000 hot bricks の canonical contents は未定義であり、benchmark SDF/material generation と actual canonical materialization は引き続き未解決。

### 4.3 Network node/edge -> `infrastructure.network_topology`

P4-01 は `infrastructure.network_topology` を logical network topology authority とし、P4-06 は stable node / edge identities を固定している。したがって `network` / `node` / `edge` は同じ既存 partition の record kinds とする。追加 Infrastructure partition は作らない。

500,000 Infrastructure benchmark class を materialize する前に、exact v2 node/edge payload fields と reference constraints を固定する必要がある。

### 4.4 Market state -> `society.market_transaction`

Phase 3 は conceptual MarketState を定義し、P4-06 は 100 market scopes × 10,000 active orders/scope を固定している。P4-01 は `society.market_transaction` に offer / demand / trade / price history の責務を与えている。したがって同 partition の `market_state` arm を `market_ref` の authority target とする。

exact v2 MarketState/order/transaction record fields と、それらが Society/Governance 2,000,000 aggregate class にどう含まれるかは引き続き正本判断が必要。

## 5. 互換性規則

影響する4つの v1 schema に optional fields を足して wire-compatible とみなしてはならない。v2 contract は同じ stable schema id と `SchemaVersion { major = 2, minor = 0 }` を使用し、次を定義する。

1. required canonical `record_kind` discrimination
2. arm-specific required / forbidden field sets
3. 各 arm の exact descriptor ordering
4. semantic Ref target-kind validation
5. exact recipe が存在する場合のみ deterministic v1 -> v2 migration
6. unknown arm/version の recovery rejection と serialized schema identity preservation

`spatial.terrain_geometry` は6項目すべてについて migration/recovery path を実装済み。ただしこれは persistence machinery が exact Terrain v2 records を表現・復元できることを意味するだけで、canonical 500,000 benchmark records やその値が定義済みという意味ではない。

他の3 repaired record family は exact v2 contract / migration が固定・検証されるまで standard runtime/persistence path を v1 のままとする。standalone v2 schema/codec を将来追加しても、production partition を暗黙に v2 へ切り替えてはならない。

## 6. 現在の残り blocker

この decision により、orphan target の **owner partition** に関する曖昧さは解消済み。Terrain については加えて exact `terrain_root` / `terrain_brick` v2 shape と version-aware production Snapshot/recovery path も解消済み。

`Qa04ReferenceWorldDependencyContractV1` の unresolved blocker は現在 **9件**。

1. Physical collision shape — `RecordSchema`
2. Environment D0 mapping — `PartitionMapping`
3. Environment D1 mapping — `PartitionMapping`
4. Society/Governance 2,000,000 decomposition — `PartitionMapping`
5. Market family — `RecordSchema`
6. Infrastructure network/node/edge — `RecordSchema`
7. Terrain canonical contents — `CanonicalMaterial`
8. active CrossDomainTransaction — `PersistentAuthority`
9. `BodyRegionStateV1` — `NestedPayloadSchema`

独立した未解決事項:

- Environment D0 1,000,000 / D1 250,000 の exact partition mapping と canonical initial payload values。
- Society/Governance 2,000,000 の33 owner partitions への decomposition。
- exact nested schema は現在 `resident.body_health.body_region_states` の `BodyRegionStateV1` のみ未解決。
- active CrossDomainTransaction 10,000 の persistent/reconstruction authority。
- 500,000 hot `TerrainBrickV1` の canonical SDF/material content と fixed-seed genesis/materialization rule。
- full `perf.reference.v1` における canonical Terrain root/scope/connectivity/material closure。
- `physical.occupancy` / `infrastructure.network_topology` / `society.market_transaction` の exact v2 field schema/migration。

解消済み nested schema:

- `resident.perception.perceived_facts` -> `domain.resident.perceived-fact / 1.0`
- governance Rule AST -> `domain.governance.rule-predicate-ast / 1.0` と `domain.governance.rule-effect-ast / 1.0`

`Qa04ReferenceWorldDependencyContractV1` は実装固有 blocker が実際に除去されるまで release gate として維持する。typed-empty root や synthetic Ref target を benchmark evidence に昇格することは禁止する。
