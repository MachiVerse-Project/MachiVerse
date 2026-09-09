# Alpha 1.1 / INT-03 — 残り正本判断の一括確定

Status: Decided normative design / implementation pending  
Tracking: Issue #240  
Implementation: Draft PR #265  
Applies to: `perf.reference.v1` and the minimum production schema evolution required by Alpha 1.1

## 1. 目的

本書は Alpha 1.1 / INT-03 で実装を止めていた canonical reference-world 9 blocker と canonical workload 3 blockerについて、実装開始に必要な正本判断を一括して固定する。

本書の決定後も、対応コード・migration・materializer・Snapshot/recovery・full benchmark が完成するまでは既存 failure code を除去しない。したがって設計確定だけを理由に `referenceWorldMaterialized=true`、`authoritativeStepLoopAvailable=true`、release evidence PASS を出してはならない。

## 2. 設計原則

1. standard Domain partition は 97 のまま。新しい benchmark 専用 authority partition は作らない。
2. standard Snapshot は 6 Core + 97 Domain = exact 103 sections のまま。
3. benchmark 固有の初期値・分布は `perf.reference.v1` profile rule とし、一般 gameplay default に昇格しない。
4. ownership repair が必要な `physical.occupancy`、`infrastructure.network_topology`、`society.market_transaction` は同一 schema id の major `2.0` を使用する。
5. existing v1 persisted record は変更しない。v1 -> v2 は明示 migration のみ許可する。
6. identity / collection order / random context / scalar generation は wall-clock、thread completion、hash-map iterationへ依存しない。
7. all required Ref は actual target recordへ閉じる。benchmark fixture用の架空 Ref、digest-only placeholderは禁止。
8. candidate object を authoritative persistence とみなさない。
9. synthetic/reduced test materialは full canonical material / release evidence とみなさない。

## 3. 共通 benchmark genesis primitive

### 3.1 Spatial lattice

`perf.reference.v1` の benchmark world coordinate lattice を次で固定する。

```text
regional tiles       = 64 x 64 = 4096
tile width           = 512,000 mm
tile height          = 512,000 mm
D0 terrain spacing   = 250 mm
D0 brick cells       = 8 x 8 x 8
D0 brick width       = 2,000 mm
D0 brick slots/tile  = 256 x 256
D3 terrain spacing   = 64,000 mm
D3 root brick width  = 512,000 mm
```

Tile index `t`:

```text
tile_row = t / 64
tile_col = t % 64
origin_x_mm = tile_col * 512000
origin_y_mm = tile_row * 512000
```

benchmark root world frame は1 record、tile scopeは4096 recordsとする。identityは `DerivedIdentity.DeriveEntityId` を用い、purpose tokenをそれぞれ:

```text
perf.world-frame
perf.tile-scope
perf.tile-detail-region
perf.terrain-root
perf.terrain-root-brick
```

とする。

### 3.2 Canonical benchmark hash/value source

profile-specific deterministic value sourceを次で固定する。

```text
H(record_id, field_tag) = HashSuite.DomainHash(
  "mv.perf-reference-genesis-value.v1",
  [profile_id, WorldSeed, record_id, field_tag]
)
U64 = first 8 bytes of H as unsigned big-endian uint64
```

`field_tag` は本書または対象 schema の canonical field nameをASCIIで使用する。

Benchmark genesisの一般 scalar default:

```text
Step / started / effective-from          = 0
record revision                           = 1
payload generation/revision              = 1 unless separately fixed
optional end/retired/terminal Step       = NONE
status/lifecycle                          = "active"
available                                = true
availability/integrity/capacity ppm      = 1,000,000
other bounded ratio ppm                  = 500,000 + U64 % 400,001
positive count/quantity                   = 1 + U64 % 1,000
non-negative count                        = U64 % 1,000
money microunit                           = 1,000 + U64 % 1,000,000
positive length mm                        = 1,000 + U64 % 10,001
temperature mK                            = 288,000 + U64 % 1,001
pressure Pa                               = 101,000 + U64 % 651
signed score                              = int(U64 % 2,001) - 1,000
small velocity component                  = int(U64 % 2,001) - 1,000
Digest                                    = H(record_id, field_tag)
```

個別 schema invariant がより狭い場合は個別 ruleを優先する。optional Refは個別表で要求しない限り `NONE`、optional RefListは空、required Refは本書の selector ruleで actual recordへ解決する。

この scalar ruleは benchmark profile用であり、本編の環境・経済・健康等の自然値を主張しない。

### 3.3 Common Ref selectors

canonical materializerは次の selectorを提供する。

```text
Resident(i)        = resident.identity_lifecycle ordinal (i mod 1,000,000)
Physical(i)        = physical.presence ordinal (i mod 500,000)
TileScope(i)       = spatial.scope_registry tile (i mod 4096)
Organization(i)    = society.organization record (i mod 10,000)
Polity(i)          = governance.polity record (i mod 1,000)
Institution(i)     = governance.institution record (i mod 5,000)
Jurisdiction(i)    = governance.jurisdiction record (i mod 10,000)
Authority(i)       = governance.public_authority record (i mod 25,000)
InfoClaim(i)       = society.information_claim record (i mod 25,000)
```

selector index は source record の canonical local ordinalまたは `U64` を使用する。Ref target schema/version/kindは owner schema registryで検証する。

## 4. Physical collision shape — 設計確定

### 4.1 Schema

`physical.occupancy` は record schema:

```text
domain.physical.occupancy.record / 2.0
```

を導入し、record kindを:

```text
occupancy
collision_shape
```

の2種へ固定する。

`occupancy` armは v1 payloadを lossless に保持する。

```text
presence_ref: Ref
 aabb_min: Vec3
 aabb_max: Vec3
 contact_refs: RefList
 occupancy_flags: uint32
 collision_layer: uint32
```

`collision_shape` arm:

```text
shape_kind: Token
sphere | capsule | oriented_box | convex_polytope | triangle_mesh_static | terrain_sdf_ref
```

arm-specific exact fields:

```text
sphere:
  center_mm: Vec3
  radius_mm: int64 > 0

capsule:
  segment_start_mm: Vec3
  segment_end_mm: Vec3
  radius_mm: int64 > 0
  start != end

oriented_box:
  center_mm: Vec3
  half_extents_mm: Vec3  // each > 0
  orientation: QuaternionQ30

convex_polytope:
  vertices_mm: ordered list<Vec3>
  // non-empty, duplicate vertex forbidden
  // order is authoritative canonical vertex index; decoder must not sort it

triangle_mesh_static:
  triangles: ordered list<StaticTriangleV1>
  StaticTriangleV1 = { canonical_index:uint32, a_mm:Vec3, b_mm:Vec3, c_mm:Vec3 }
  // list sorted canonical_index ascending, duplicate index forbidden

terrain_sdf_ref:
  terrain_root_ref: Ref
  // exact target: spatial.terrain_geometry / 2.0 / terrain_root
```

Alpha 1.1では arbitrary SDF blob / dynamic concave meshを追加しない。

### 4.2 v1 -> v2 migration

既存 `physical.occupancy /1.0` recordは `occupancy` armへ field-for-field lossless migrationする。`collision_shape` recordは migrationで捏造せず、新規 materialization時だけ生成する。

### 4.3 `perf.reference.v1` Physical genesis

500,000 `physical.d0-presence` descriptorごとに:

- 1 `physical.presence` record
- 1 `physical.occupancy /2.0 occupancy` record
- 1 `physical.occupancy /2.0 collision_shape` record

を生成する。

Presence record idは既存 descriptor RecordIdを使用し、occupancy/shape idは presence idを creatorにして purpose `perf.physical-occupancy` / `perf.physical-shape` で導出する。

shape mixは `physicalOrdinal % 100`:

```text
00..49 sphere               50%
50..69 capsule              20%
70..89 oriented_box         20%
90..97 convex_polytope       8%
98     triangle_mesh_static  1%
99     terrain_sdf_ref       1%
```

primitive local geometryは mm integerのみ使用する。identity quaternionは `(0,0,0,1<<30)`。

- sphere radius = `300 + ordinal % 201` mm
- capsule endpoints = `(0,-400,0)` / `(0,400,0)`, radius = `200 + ordinal % 101` mm
- OBB half extents = `(300 + ordinal%101, 200 + ordinal%101, 500 + ordinal%201)` mm
- convex polytope = local axis-aligned 8-corner box vertices, fixed canonical vertex order
- static mesh = 2 triangles forming a 2m x 2m local square, canonical indices 0,1
- terrain SDF ref = presence tileの terrain root

`physical.presence.shape_ref` は必ず同 subjectの `collision_shape` recordを指す。

## 5. Environment D0 / D1 — 設計確定

### 5.1 D0 exact decomposition

`environment.d0-cell-cohort` 1,000,000 descriptorは1 descriptor = 1 authoritative primary recordとし、次の contiguous ordinal rangesへ写像する。

| partition | count |
|---|---:|
| `environment.geology` | 40,000 |
| `environment.soil` | 60,000 |
| `environment.resource_deposit` | 20,000 |
| `environment.groundwater` | 80,000 |
| `environment.atmosphere` | 180,000 |
| `environment.climate` | 30,000 |
| `environment.weather` | 180,000 |
| `environment.surface_water` | 120,000 |
| `environment.ocean` | 10,000 |
| `environment.ecosystem` | 160,000 |
| `environment.contaminant` | 100,000 |
| `environment.hazard` | 10,000 |
| `environment.environment_lineage` | 10,000 |
| **total** | **1,000,000** |

Descriptor RecordIdを authoritative record idとして保持する。`spatial_scope` は descriptor `RegionalTileIndex` の `TileScope`。

### 5.2 D1 exact decomposition

`environment.d1-aggregate` 250,000 descriptor:

| partition | count |
|---|---:|
| `environment.geology` | 3,000 |
| `environment.soil` | 5,000 |
| `environment.resource_deposit` | 1,000 |
| `environment.groundwater` | 20,000 |
| `environment.atmosphere` | 50,000 |
| `environment.climate` | 40,000 |
| `environment.weather` | 40,000 |
| `environment.surface_water` | 30,000 |
| `environment.ocean` | 5,000 |
| `environment.ecosystem` | 30,000 |
| `environment.contaminant` | 15,000 |
| `environment.hazard` | 10,000 |
| `environment.environment_lineage` | 1,000 |
| **total** | **250,000** |

D1 record idも descriptor RecordIdを使用する。

### 5.3 D0 genesis payload

required scalarは §3.2を使用する。固定 token:

```text
material class        = perf.rock
soil class            = perf.soil
resource kind         = perf.resource
climate regime        = perf.climate
weather class         = perf.clear
water body class      = perf.channel
species/cohort        = perf.cohort
contaminant kind      = perf.marker
hazard kind           = perf.synthetic-hazard
materialization kind  = perf.genesis
```

Atmosphere gas mapは canonical token orderで:

```text
perf.gas-a = 780,000,000 ppb
perf.gas-b = 210,000,000 ppb
perf.gas-c =  10,000,000 ppb
```

required RefListで explicit topologyを必要としない fieldは genesisでは空を許可する。Groundwater / ocean neighbor、surface-water downstreamは各 partition内 `record_id` ascending ringの next recordを1件参照し、graph workloadを成立させる。

`environment.environment_lineage` は `parent_refs=[]`, `generation=0`, `materialization_kind=perf.genesis`, `source_digest=H(record_id,"source_digest")`。

### 5.4 D1 aggregation semantics

D1 partition P の local ordinal `j` は、同じ P の D0 recordsから canonical source quartetを取る。

```text
source_k = D0_P[(4*j + k) mod D0_P.count], k=0..3
```

source listは RecordId ascendingへnormalizeする。

Aggregation:

- stock / mass / volume / population / counts: checked sum
- ratio / temperature / pressure / vector: round-to-even arithmetic mean
- token: frequency mode、tieはASCII token ascending
- RefList: canonical set union
- generation: max(source generation)+1
- basis/start Step: max source Step
- optional end Step: all source absentならNONE、otherwise minimum present value
- status:全source同一ならそのtoken、異なる場合 `active`

D1 lineageは source quartetの canonical digestを `source_digest` へbindし、`materialization_kind=perf.aggregate-d1` とする。

## 6. Society / Governance 2,000,000 — 設計確定

### 6.1 Market loadとの関係

100 market scopes × 10,000 active orders = 1,000,000 active ordersは `society-governance.active-record` 2,000,000 の**内数**とする。

100 `market_state` recordsも2,000,000内数。genesis時の `transaction_or_price_fact` は0件。

### 6.2 Society exact counts = 1,600,000

| partition | count |
|---|---:|
| `society.organization` | 10,000 |
| `society.membership_role` | 80,000 |
| `society.employment` | 80,000 |
| `society.household` | 40,000 |
| `society.contract_claim` | 60,000 |
| `society.property_right` | 50,000 |
| `society.currency_money` | 100 |
| `society.finance_account` | 80,000 |
| `society.market_transaction` | 1,000,100 |
| `society.business_production` | 30,000 |
| `society.logistics_obligation` | 40,000 |
| `society.education` | 25,000 |
| `society.culture` | 30,000 |
| `society.reputation` | 30,000 |
| `society.information_claim` | 25,000 |
| `society.history_lineage` | 19,800 |
| **total** | **1,600,000** |

### 6.3 Governance exact counts = 400,000

| partition | count |
|---|---:|
| `governance.polity` | 1,000 |
| `governance.institution` | 5,000 |
| `governance.law_rule` | 30,000 |
| `governance.jurisdiction` | 10,000 |
| `governance.territorial_claim` | 10,000 |
| `governance.effective_control` | 20,000 |
| `governance.public_authority` | 25,000 |
| `governance.tax_fiscal` | 50,000 |
| `governance.permission_license` | 70,000 |
| `governance.diplomacy` | 10,000 |
| `governance.security_incident` | 45,000 |
| `governance.investigation` | 30,000 |
| `governance.judicial_case` | 25,000 |
| `governance.enforcement` | 30,000 |
| `governance.military_authority` | 10,000 |
| `governance.border_control` | 10,000 |
| `governance.lineage` | 19,000 |
| **total** | **400,000** |

All 2,000,000 records are D2 at genesis, matching current benchmark descriptor detail classification.

### 6.4 Identity mapping

market slice以外は `society-governance.active-record` descriptorの RecordIdを authoritative record idに使用する。

Market:

- `market_state.record_id = Qa04ReferenceScenariosV1.MarketScopeId(scopeOrdinal)`
- `order_or_offer.record_id = Qa04ReferenceScenariosV1.MarketOrderId(scopeOrdinal, orderOrdinal)`

Generic active-record descriptorは count/decomposition assignmentのidentityとして保持し、market専用 identityとの mapping tableを deterministic materialization evidenceへ出力する。

### 6.5 Genesis Ref/value rules

P4-05 required fieldsを変更しない。required Refは §3.3 selectorを使用する。

- member / worker / holder / learner / subject / debtor / suspect / investigator -> Resident
- employer / provider / issuer / unit-or-org -> Organization
- polity relation -> Polity
- institution relation -> Institution
- jurisdiction relation -> Jurisdiction
- authority relation -> Authority
- asset / cargo -> Physical
- spatial scope / origin / destination -> TileScope
- claim/evidence when required -> InfoClaim

optional Refは原則NONE。required RefListは最低1件必要な semantic fieldだけ selector 1件、emptyが許される listはempty。status/lifecycle=`active`。

Law Rule ASTは既存 `domain.governance.rule-predicate-ast /1.0` と `rule-effect-ast /1.0` の canonical constant predicate/effect fixtureを profile正本として使用し、実装済み codecを通す。

## 7. Society Market record schema v2 — 設計確定

Schema:

```text
domain.society.market_transaction.record / 2.0
```

record kinds:

```text
market_state
order_or_offer
transaction_or_price_fact
```

### 7.1 `market_state`

```text
scope_ref: Ref                    // spatial.scope_registry
instrument_token: Token
currency_token: Token
status: Token
clearing_cadence_steps: uint64 > 0
last_clearing_step: Step?
last_clearing_price_microunit: int64?
```

### 7.2 `order_or_offer`

```text
market_ref: Ref                   // same partition / market_state
owner_ref: Ref
instrument_token: Token
side: Token                       // buy | sell
limit_price_microunit: int64 >= 0
quantity: int64 > 0
remaining_quantity: int64         // 0..quantity
eligible_step: Step
status: Token                     // open | filled | cancelled | expired
```

### 7.3 `transaction_or_price_fact`

v1 lossless migrationを可能にするため、P4-05 v1 semantic fieldsを保持する。

```text
fact_kind: Token
market_ref: Ref
instrument_token: Token
order_side: Token?
limit_price_microunit: int64?
quantity: int64
clearing_price_microunit: int64?
buyer_ref: Ref?
seller_ref: Ref?
eligible_step: Step
status: Token
```

v1 -> v2は `transaction_or_price_fact`、`fact_kind=market.legacy-v1` へ field-for-field移送する。migration時に `market_state` / `order_or_offer` を推測生成しない。

### 7.4 Benchmark market genesis

```text
instrument_token = perf.instrument
currency_token   = perf.currency
status           = active/open
clearing cadence = 30 Steps
last clearing    = NONE
```

Order global ordinal `g = scope*10000 + order`:

```text
owner = Resident(g)
side = even(order) ? buy : sell
buy limit price  = 100000 + (order % 1000)
sell limit price =  99500 + (order % 1000)
quantity = 1 + (order % 20)
remaining_quantity = quantity
eligible_step = 0
```

5% order change ruleは既存 `MarketOrderChanges` を使用し、変更時も OrderIdは維持して record revisionのみ+1する。

## 8. Infrastructure 500,000 + network v2 — 設計確定

### 8.1 Exact decomposition

| class/partition | count |
|---|---:|
| network records (`infrastructure.network_topology /2.0`) | 100 |
| node records (`infrastructure.network_topology /2.0`) | 20,000 |
| edge records (`infrastructure.network_topology /2.0`) | 100,000 |
| `infrastructure.transport_service` | 10,000 |
| `infrastructure.water_service` | 10,000 |
| `infrastructure.power_service` | 10,000 |
| `infrastructure.communication_service` | 10,000 |
| `infrastructure.dependency` | 20,000 |
| `infrastructure.facility_service` | 15,000 |
| `infrastructure.service_queue` | 250,000 |
| `information.delivery` | 20,000 |
| `information.media_distribution` | 5,000 |
| `information.record_store` | 10,000 |
| `information.address_place_index` | 5,000 |
| `infrastructure.failure_recovery` | 10,000 |
| `infrastructure.lineage` | 4,900 |
| **total** | **500,000** |

### 8.2 Network topology v2

Schema:

```text
domain.infrastructure.network_topology.record / 2.0
```

record kinds:

```text
network
node
edge
```

`network` armは v1 payloadをlossless保持:

```text
network_kind: Token
node_refs: RefList
edge_refs: RefList
operator_refs: RefList
scope_refs: RefList
status: Token
topology_revision: uint64
```

`node`:

```text
network_ref: Ref        // same partition / network
node_kind: Token
scope_ref: Ref
capacity_units: uint64
availability_ppm: uint32
status: Token
```

`edge`:

```text
network_ref: Ref        // same partition / network
from_node_ref: Ref      // same partition / node
to_node_ref: Ref        // same partition / node
edge_kind: Token
cost: uint64
capacity_units: uint64
availability_ppm: uint32
status: Token
```

self edge禁止。networkの node_refs / edge_refs は target-kind closure必須。

v1 -> v2 は `network` armのみ field-for-field migrationし、node/edgeを推測生成しない。

### 8.3 Benchmark network topology

Network idは purpose `perf.infrastructure-network`, ordinal 0..99。

```text
nodes/network = 200
edges/network = 1000
network(nodeOrdinal) = nodeOrdinal / 200
network(edgeOrdinal) = edgeOrdinal / 1000
```

Node record idは既存 `InfrastructureNodeId`、Edge record idは既存 `InfrastructureEdgeId`。

Edge local ordinal `e`:

```text
fromLocal = e % 200
stride = 1 + ((e / 200) % 199)
toLocal = (fromLocal + stride) % 200
```

network kindは network ordinal %4 で `transport`, `water`, `power`, `communication`。status=`active`, topology_revision=1, node_kind=`junction`, edge_kind=`link`。

Network scopeは `TileScope(networkOrdinal * 4096 / 100)`、operatorは `Organization(networkOrdinal)`。

250,000 queue record idは既存 `InfrastructureServiceRequestId`。service_refは transport/water/power/communication/facility service recordsを canonical round-robin、requester=Resident(requestOrdinal)、eligible_step=0、semantic_priority=0、requested_units=`1+ordinal%100`、allocated_units=0、status=`queued`。

## 9. Terrain canonical material — 設計確定

### 9.1 Root/scope

4096 tile scopesそれぞれに:

- 1 `terrain_root`
- 1 D3 root-anchor `terrain_brick`

を追加する。500,000 hot descriptor bricksは別に全件D0としてmaterializeする。

Root id=`perf.terrain-root(tile)`、anchor brick id=`perf.terrain-root-brick(tile)`。

`terrain_root.scope_ref=TileScope(tile)`、`root_brick_ref=anchor brick`、`geometry_revision=1`。

### 9.2 D3 anchor

D3 anchor:

```text
level = 3
sample_spacing_mm = 64000
cell_origin.level = 3
cell_origin.x = tile_col * 8
cell_origin.y = tile_row * 8
```

Z originは §9.4 height ruleのtile center heightを 512,000mm D3 brick spanではなく、8-cell vertical spanに収めるよう floor divisionで決定する。

### 9.3 D0 hot brick placement

Tile内 hot descriptorを RecordId ascendingでrank `r` にする。

```text
slot = (r * 40503 + tileIndex * 17) mod 65536
localBrickX = slot & 255
localBrickY = slot >> 8
globalBrickX = tile_col * 256 + localBrickX
globalBrickY = tile_row * 256 + localBrickY
cell_origin.level = 0
cell_origin.x = globalBrickX * 8
cell_origin.y = globalBrickY * 8
level = 0
sample_spacing_mm = 250
```

40503はoddで2^16に対してbijectiveなので、同tile内 `r < 65536` ならslot衝突しない。canonical materializerは全4096 tileについて `count<65536` をassertする。

### 9.4 Canonical height / SDF

World XY mmから heightを一意に定義する。

```text
TH(x_mm,y_mm) = HashSuite.DomainHash(
  "mv.perf-reference-terrain-height.v1",
  [WorldSeed, x_mm, y_mm]
)
height_mm = int32(BE32(TH[0..4]) % 8001) - 4000
```

各brick center XYの `height_mm` を使い、2,000mm spanのD0 brickについてsurfaceがbrick vertical spanに入るよう:

```text
brickZ = floor_div(height(centerX,centerY), 2000)
cell_origin.z = brickZ * 8
```

SDF sample `(sx,sy,sz)`:

```text
world_x_mm = (cell_origin.x + sx) * sample_spacing_mm
world_y_mm = (cell_origin.y + sy) * sample_spacing_mm
world_z_mm = (cell_origin.z + sz) * sample_spacing_mm
sdf_mm = checked_int32(world_z_mm - height(world_x_mm, world_y_mm))
```

同じworld sample coordinateは常に同じ値となるため、neighbor brick/detail boundaryで値が一致する。

### 9.5 Surface material

material id:

```text
0 void
1 soil
2 rock
3 sediment
```

cell center SDF `d`:

```text
d > 0       -> 0
-500 < d<=0 -> 1
-2000<d<=-500 -> 3
d <= -2000  -> 2
```

`terrain_root.surface_classes` は ASCII ascendingで:

```text
terrain.rock
terrain.sediment
terrain.soil
```

### 9.6 Topology / connectivity

Persisted authoritative geometryは「coarse brick + sparse refinement」方式とする。D3 anchorはtile全体のfallback、D0 hot brickは局所refinement。中間 lookup/octree nodeは `(level,cell_origin)` から構築する `DERIVED_REBUILDABLE` indexであり、追加 authoritative recordを要求しない。

lookupは同一点をcoverするbrickのうち sample spacingが最小のものを使用し、tieは RecordId ascending。

`terrain_root.connectivity_refs` はN/E/S/W隣接tileの `terrain_root` refsをcanonical Ref orderで保持する。world edgeでは存在するneighborのみ。

Genesisの root/brick envelope `lineage_ref=NONE`、root `geometry_revision=1`。将来detail/materializationで新recordを生成する場合のみ `spatial.geometry_lineage` recordを作り、source record refsをparentとしてbindする。

## 10. `BodyRegionStateV1` nested schema — 設計確定

Schema:

```text
domain.resident.body-region-state / 1.0
```

exact field order / ordinal:

```text
1 region_token: Token
2 integrity_ppm: uint32
3 function_capacity_ppm: uint32
4 pain_ppm: uint32
5 injury_load_ppm: uint32
6 disease_load_ppm: uint32
7 impairment_ppm: uint32
8 recovery_ppm: uint32
```

all ppm = 0..1,000,000、全field required。nested Refは持たない。

Parent `injury_refs` / `disease_refs` は persistent condition identity/evidence authorityを保持し、nested `injury_load_ppm` / `disease_load_ppm` はregion-local manifestation intensityのみを表す。重複authorityではない。

v1 region vocabularyはexactly:

```text
body.arm.left
body.arm.right
body.head
body.leg.left
body.leg.right
body.systemic
body.torso
```

collectionは region token ASCII ascending、duplicate禁止。1.0 decoderはunknown tokenをrejectする。vocabulary追加は nested schema minor updateを要求する。

`perf.reference.v1` genesisは全Resident body-health materialについて7 entriesを持つ。

```text
integrity_ppm          = 1,000,000
function_capacity_ppm  = 1,000,000
pain_ppm               = 0
injury_load_ppm        = 0
disease_load_ppm       = 0
impairment_ppm         = 0
recovery_ppm           = 1,000,000
```

## 11. Active CrossDomainTransaction persistent authority — 設計確定

### 11.1 Authority owner

CrossDomainTransactionはsingle Domain ownerへ押し込まず Core effect-custody authorityとする。

exact-103を維持するため新sectionは増やさない。既存 `core.operation-state` sectionを schema `2.0`へ上げ、logical payloadを:

```text
CoreEffectCustodyStateV2 {
  operations: ordered list<DurableOperationStateV1>
  cross_domain_transactions: ordered list<CrossDomainTransactionStateV1>
}
```

とする。section idは互換性のため `core.operation-state` のまま。SQL `operation_state` tableへtransaction rowを混在させない。

### 11.2 Transaction persistent state

```text
CrossDomainTransactionStateV1 {
  transaction_id: Id128
  transaction_kind: Token
  lifecycle: ACTIVE | COMMITTED | ABORTED
  created_step: Step
  updated_step: Step
  terminal_step: Step?
  root_causality: CausalityRefV1
  subject_ids: ordered list<Id128>
  participants: ordered list<PersistentTransactionParticipantV1>
  invariant_results: ordered list<InvariantResultV1>
  state_digest: Digest
}

PersistentTransactionParticipantV1 {
  domain_token: Token
  partition_id: Token
  intent_ids: ordered list<Id128>
  required: bool
  outcome: READY | FAILED
  candidate_effect_digest: Digest
  diagnostic_code: Token?
}
```

canonical participant orderは production `TransactionParticipantCandidateV1` と同じ domain rank -> domain token -> partition id。subject/id/invariant listも既存 candidate canonical orderを維持する。

Candidate `CrossDomainTransactionCandidateV1.IsAuthoritative` は引き続き `false`。valid candidateをCOMMITした後に上記 persistent stateがauthorityになる。

### 11.3 SQLite

新table:

```text
cross_domain_transaction_state
  transaction_id BLOB(16) PRIMARY KEY
  lifecycle INTEGER NOT NULL
  updated_step BLOB(8) NOT NULL      // U64BE
  state_wire BLOB NOT NULL
  state_digest BLOB(32) NOT NULL
```

`state_wire` は versioned protobuf、semantic digestはMV-DCBOR normalized stateから計算。terminal tombstoneもAlpha 1.1では削除しない。

### 11.4 History / atomic commit

existing record type semantic family `transition.committed.v1` を維持しつつ payload schema `persistence.transition-committed /2.0` を導入する。legacy suffix `v1` は record-type token compatibilityのため変更しない。

v2はexisting fieldsを不変で保持し、required repeated field:

```text
transaction_state_changes
```

を追加する。0件を許可し、transaction_id ascending。各entryは `before_state_digest?`, `after_state`, lifecycle transitionを含む。

Domain participant effect、Operation terminalization、transaction state change、history anchor、continuity tokenを**同一 SQLite transaction**でcommitする。

v1 transition historyはtransaction state changeを持たないものとしてdecodeできるが、active transaction persistent authorityを使用するrunではv2 transition recordを必須とする。

### 11.5 Recovery

Snapshot `core.operation-state /2.0` から operation + transaction stateを復元し、anchor後の transition v2 historyをsequence順にapplyする。before digestがcurrent state digestと一致しない場合recovery reject。

Detail guardはrecovered ACTIVE setから再構築し、Snapshot内 detail guard stateと一致しなければrecovery reject。

### 11.6 Benchmark turnover

Initial active set = exactly 10,000, `created_step=0`。

10 cohorts × 1,000 transactions:

```text
initial cohort = ordinal % 10
initial retirement basis step = 300 * (cohort + 1)
```

各 `S % 300 == 0` の transition S -> S+1で:

- due cohort 1,000件を COMMITTED terminalへ移行
- exactly 1,000 replacement ACTIVE transactionsを作成
- resulting State(S+1) active countを10,000へ維持

replacement lifetime = exactly 3,000 Steps。replacementの root causalityは置換元 transaction id、subject idsは deterministic derivationを維持する。

### 11.7 Detail guard binding

4096 tile detail regionsを使用する。ACTIVE transactionの各 subject idを `Qa04ReferenceLoadV1.RegionalTileIndex(subject)` へ写像し、そのtile detail regionへ `detail.guard.active-transaction` を付与する。

同じregionを複数transactionがguardする場合 reference count semantics。最後のACTIVE transactionが消えた時だけguardを除去する。

## 12. Canonical Operation workload binding — 設計確定

All QA-04 external workload Operations:

```text
SameStepOrderKey.phase = 1  // external_input
semantic_priority = 0
intent_id = OperationId
admission_basis_step = injection_step
requested_not_before = NONE
requested_deadline = NONE
late policy = REJECT
profile scheduling min_lead_steps = 1
required effective step = injection_step + 1
```

Domain rankは StandardDomainExecutionPlan owner rankを使用する。conflict scope digestは:

```text
HashSuite.DomainHash("mv.perf-reference-operation-scope.v1", [operation_kind, primary_target_id])
```

family -> production OperationKind:

| QA-04 family | production kind | owner |
|---|---|---|
| `participation-control-resident-action` | `resident.action.request` | resident |
| `physical-item-movement-work` | `physical.move.request` | physical_built |
| `society-market-payment-contract` | `society.market.order-place` | society_economy |
| `infrastructure-service-delivery` | `infrastructure.service.reserve` | infrastructure_information |
| `governance-security` | `governance.incident.register` | governance_security |
| `environment-spatial-admin-synthetic` | `environment.hazard.inject` | environment |

Payload binding:

- Resident: `resident_ref=Resident(ordinal)`, action token = existing `ResidentActivity(resident, step)`, target refs empty, benchmark parameters contain family ordinal.
- Physical: subject=`Physical(ordinal)`, desired displacement/velocity are deterministic small signed vectors from §3.2, target absent.
- Market: market scope=`ordinal%100`, owner=Resident(ordinal), side parity, price/quantity same formulas as §7.4.
- Infrastructure: requester=Resident(ordinal), service record canonical round-robin, units=`1+ordinal%100`, eligible `[S+1,S+30]`.
- Governance: incident kind=`perf.incident`, subject=Resident(ordinal), scope=`TileScope(RegionalTileIndex(subject))`, fact ref=`InfoClaim(ordinal)`.
- Environment: hazard kind=`perf.synthetic-hazard`, scope=`TileScope(ordinal%4096)`, intensity=`100000 + ordinal%800001`, duration constraint=30 Steps.

`Qa04OperationDescriptorV1.PayloadDigest` は最終 normalized immutable production Operation payload digestへ変更する。現在の descriptor-only hashは実装移行時に置換し、full canonical runでは descriptor digestと durable operation payload digestを一致させる。

## 13. Transaction creation workload binding — 設計確定

### 13.1 Kind mapping

11 named benchmark bucketsは `transaction.<benchmark-kind>` へ1:1。

`other-registered-transactions` 20 permilleは remaining 6 kindsへ descriptor ordinal ascending round-robin:

```text
transaction.demolition
transaction.birth
transaction.death
transaction.disease-transmission
transaction.public-record
transaction.military-operation
```

200件/10,000 initial distributionでは先頭2 kindsが34件、残り4 kindsが33件となる。replacement batchも同じ modulo-6 rule。

### 13.2 Participants

Registry `RequiredDomains` は全件含む。各 `RequiredAnyDomainGroups` は domain rank最小の1 domainを選ぶ。`OptionalDomains` は全件含める。Required/conditional participantは `required=true`、optionalはfalse。

Canonical participant partition:

```text
spatial                    -> spatial.scope_registry
environment                -> environment.hazard
physical_built             -> physical.presence
participation              -> participation.control_mode
resident                   -> resident.identity_lifecycle
society_economy            -> society.contract_claim
governance_security        -> governance.permission_license
infrastructure_information -> infrastructure.service_queue
```

Intent id:

```text
Trunc128(HashSuite.DomainHash(
  "mv.perf-reference-transaction-intent.v1",
  [transaction_id, domain_token, partition_id]))
```

participant outcome=`READY`、candidate effect digestは同tuple + basis Stepを domain hashする。creation時の participant effectは**reservation/preparation semanticのみ**で、Domain authoritative payloadを暗黙変更しない。実Domain mutationは別Operation/transaction terminal effectとして明示される場合だけ行う。

Required invariantsは registry列挙を全て `PASS` として生成し、invariant id canonical orderを維持する。

Initial transaction basis=0。replacement creation basis=S、authoritative ACTIVE stateの created_step=S+1。

## 14. Detail transition request binding — 設計確定

### 14.1 Actual detail region selection

既存 `DetailTransitionRegionId(kind, ordinal)` は benchmark batch descriptor identityとして保持し、persistent `DetailRegionStateV1.DetailRegionId` には使用しない。

4096 tile detail region recordsとは別に、transition用 spatial macro-regionを16x16 regional tilesで表す。各 cadence `c=S/300` と region ordinalから top-left tileを deterministic hashで選び、wrap-aroundする16x16 windowとする。macro region idは:

```text
DerivedIdentity(... creationStep=0, kind=perf.detail-macro-region,
  ordinal = c*16 + batchOffset + regionOrdinal)
```

同じ27,000-Step run内で macro region idを再利用しない。

### 14.2 Request fields

Promotion 6 regions:

```text
CurrentLevel = D1
TargetLevel = D0
EstimatedRecordCount = 5000  // 30,000 / 6
```

Demotion 10 regions:

```text
CurrentLevel = D0
TargetLevel = D1
EstimatedRecordCount = 8000  // 80,000 / 10
```

Common:

```text
RequiredEffectiveStep = S
SemanticPriority = 0
TriggerSource = ConfigPolicy
TriggerId = DetailTransitionTriggerAuthorityV1.ConfigPolicyTriggerId(active generation,digest)
TriggerObservedStep = S
```

DomainTokenは participationを除く7 domainsを canonical rank order:

```text
spatial, environment, physical_built, resident,
society_economy, governance_security, infrastructure_information
```

で `(c + regionOrdinal + directionOffset) % 7` 選択。`directionOffset=0` promotion、`3` demotion。

### 14.3 Candidate record set

対象domainの canonical authoritative recordsのうち macro-regionに属する recordsを RecordId ascendingで列挙し、先頭 `EstimatedRecordCount` を対象とする。16x16=256 tiles windowを使用するため、canonical reference world materializerは各対象domain/windowが8,000件以上を持つことを事前検証する。満たさない場合profile materialization failureとし、wrap/duplicateで水増ししない。

Selected recordは identity/payload semanticを維持し、record envelope detail levelと lineage generationだけを canonical transition ruleで更新する。Promotion/Demotionでrecordを暗黙増減させない。

Conservation proofは selected RecordId set、before/after semantic payload digest、identity multiset、stock summaryを既存 `DetailConservationInvariantV1` へ渡し、同一set/stockを要求する。

Genesis detail directoryは27,000 Stepsのcanonical scheduleを先にmaterializeし、promotion対象 pairをD1、demotion対象 pairをD0として初期化する。同一 `(region,domain)` が両方向に現れるprofileはinvalid。

## 15. 設計確定後も残る implementation gate

本書で正本判断は確定したが、以下は未実装なら blockerのまま。

1. Physical occupancy v2 record/state/wire/migration/materializer/recovery
2. Environment D0/D1 materializer + aggregation/recovery
3. Society/Governance 2M materializer + Ref closure
4. Market v2 record/state/wire/migration/materializer/recovery
5. Infrastructure network v2 + 500k materializer/recovery
6. Terrain canonical generator + 4096 root anchors + actual 500k hot bricks
7. BodyRegionState nested codec + body-health materializer
8. Core effect-custody v2 + SQLite transaction state + transition history v2 + recovery
9. canonical Operation materializer/scheduler binding
10. canonical transaction creation/turnover binding
11. detail macro-region/request/conservation binding
12. full exact-97 state / exact-103 Snapshot -> Zstd -> staging -> recovery -> rehash
13. full 27,000-Step authoritative loop
14. 12 benchmark runs + persistence/publication + 24h soak

## 16. Blocker運用

`Qa04ReferenceWorldDependencyContractV1` 9件と `Qa04CanonicalWorkloadDependencyContractV1` 3件の failure codeは、**設計確定では除去しない**。

各 blockerは対応実装と production-path smokeが完成したcommitで1件ずつ除去する。最後の blockerを外すcommitでも `referenceWorldMaterialized=true` は actual full materialization + exact-103 recovery proofが同一commitで成功するまで設定しない。

PR #265は Stage 2 /実evidence完了まで Draftを維持する。
