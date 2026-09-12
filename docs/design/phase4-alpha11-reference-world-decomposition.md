# Alpha 1.1 — `perf.reference.v1` reference-world decomposition

Status: Decided normative design / implementation pending  
Tracking: #240  
Implementation: Draft PR #265  
Parent: `phase4-alpha11-normative-closure.md`

## 1. 目的

`perf.reference.v1` の Environment D0/D1、Society/Governance 2,000,000、Infrastructure 500,000 を、実装時に件数・ordinal・owner解釈が分岐しない exact decomposition として固定する。

本書の値は benchmark profile 専用であり、一般 gameplay の自然分布・人口構成・経済構成を意味しない。

## 2. 共通 ordinal rule

各 top-level benchmark class の descriptor ordinal は `0..count-1`。表の range は inclusive。

- 非specialized recordは descriptor `RecordId` を authoritative record idとして使用する。
- specialized identity helperが既に存在する Market / Infrastructure node-edge-request は本書指定のIDを使用し、descriptor ordinal -> specialized id の1:1 mapping evidenceをmaterializerが出力する。
- record revisionはgenesisで1。
- required Refは actual target recordへ閉じる。

## 3. Environment D0 = 1,000,000

| ordinal range | partition | count |
|---|---|---:|
| 0..39,999 | `environment.geology` | 40,000 |
| 40,000..99,999 | `environment.soil` | 60,000 |
| 100,000..119,999 | `environment.resource_deposit` | 20,000 |
| 120,000..199,999 | `environment.groundwater` | 80,000 |
| 200,000..379,999 | `environment.atmosphere` | 180,000 |
| 380,000..409,999 | `environment.climate` | 30,000 |
| 410,000..589,999 | `environment.weather` | 180,000 |
| 590,000..709,999 | `environment.surface_water` | 120,000 |
| 710,000..719,999 | `environment.ocean` | 10,000 |
| 720,000..879,999 | `environment.ecosystem` | 160,000 |
| 880,000..979,999 | `environment.contaminant` | 100,000 |
| 980,000..989,999 | `environment.hazard` | 10,000 |
| 990,000..999,999 | `environment.environment_lineage` | 10,000 |
| | **total** | **1,000,000** |

`spatial_scope` は descriptor `RegionalTileIndex` の canonical tile scope。

## 4. Environment D1 = 250,000

D1は各partitionのD0を**exactly 4 records -> 1 aggregate**へまとめる。これによりD1総数はD0総数/4に厳密一致し、全D0 sourceを重複・欠落なく1回だけ消費する。

| D1 ordinal range | partition | D0 source count | D1 count |
|---|---|---:|---:|
| 0..9,999 | `environment.geology` | 40,000 | 10,000 |
| 10,000..24,999 | `environment.soil` | 60,000 | 15,000 |
| 25,000..29,999 | `environment.resource_deposit` | 20,000 | 5,000 |
| 30,000..49,999 | `environment.groundwater` | 80,000 | 20,000 |
| 50,000..94,999 | `environment.atmosphere` | 180,000 | 45,000 |
| 95,000..102,499 | `environment.climate` | 30,000 | 7,500 |
| 102,500..147,499 | `environment.weather` | 180,000 | 45,000 |
| 147,500..177,499 | `environment.surface_water` | 120,000 | 30,000 |
| 177,500..179,999 | `environment.ocean` | 10,000 | 2,500 |
| 180,000..219,999 | `environment.ecosystem` | 160,000 | 40,000 |
| 220,000..244,999 | `environment.contaminant` | 100,000 | 25,000 |
| 245,000..247,499 | `environment.hazard` | 10,000 | 2,500 |
| 247,500..249,999 | `environment.environment_lineage` | 10,000 | 2,500 |
| | **total** | **1,000,000** | **250,000** |

Partition P の D1 local ordinal `j` のsourceは:

```text
D0_P[4*j + 0]
D0_P[4*j + 1]
D0_P[4*j + 2]
D0_P[4*j + 3]
```

source refsは RecordId ascendingへnormalizeする。

Aggregation:

- mass/volume/population/stock/count: checked sum
- ratio/temperature/pressure/vector: round-to-even arithmetic mean
- token: mode、同数tieはASCII ascending
- RefList: canonical set union
- generation/revision-like payload field: max(source)+1
- begin/basis Step: max(source)
- optional end Step: 全source absentならNONE、それ以外はpresent valueのminimum
- status: 全source同一ならそのtoken、異なる場合 `active`

D1 lineage semantics:

```text
materialization_kind = perf.aggregate-d1
generation = max(source generation)+1
source_digest = hash(canonical source semantic digests)
```

## 5. Environment genesis vocabulary/value rule

固定 token:

```text
material_class        perf.rock
soil_class            perf.soil
resource_kind         perf.resource
climate_regime        perf.climate
weather_class         perf.clear
water_body_class      perf.channel
species_or_cohort     perf.cohort
contaminant_kind      perf.marker
hazard_kind           perf.synthetic-hazard
materialization_kind  perf.genesis
```

Atmosphere gas map:

```text
perf.gas-a = 780,000,000 ppb
perf.gas-b = 210,000,000 ppb
perf.gas-c =  10,000,000 ppb
```

Groundwater neighbor、surface-water downstream、ocean neighborは同partition内 RecordId ascending sequenceの次recordを1件参照し、末尾は先頭へwrapする。single-record partitionの場合のみself topologyは禁止のためlistをemptyとする。本profileの該当partitionは複数件ある。

その他scalarは `phase4-alpha11-normative-closure.md` の benchmark genesis value sourceを使用する。

## 6. Society / Governance = 2,000,000

Market 100 scopes × 10,000 active orders = 1,000,000 order recordsはこの2,000,000の**内数**。さらに100 `market_state` recordsも内数とする。genesisの `transaction_or_price_fact` は0。

### 6.1 Society = 1,600,000

| global descriptor ordinal range | partition | count |
|---|---|---:|
| 0..9,999 | `society.organization` | 10,000 |
| 10,000..89,999 | `society.membership_role` | 80,000 |
| 90,000..169,999 | `society.employment` | 80,000 |
| 170,000..209,999 | `society.household` | 40,000 |
| 210,000..269,999 | `society.contract_claim` | 60,000 |
| 270,000..319,999 | `society.property_right` | 50,000 |
| 320,000..320,099 | `society.currency_money` | 100 |
| 320,100..400,099 | `society.finance_account` | 80,000 |
| 400,100..1,400,199 | `society.market_transaction /2.0` | 1,000,100 |
| 1,400,200..1,430,199 | `society.business_production` | 30,000 |
| 1,430,200..1,470,199 | `society.logistics_obligation` | 40,000 |
| 1,470,200..1,495,199 | `society.education` | 25,000 |
| 1,495,200..1,525,199 | `society.culture` | 30,000 |
| 1,525,200..1,555,199 | `society.reputation` | 30,000 |
| 1,555,200..1,580,199 | `society.information_claim` | 25,000 |
| 1,580,200..1,599,999 | `society.history_lineage` | 19,800 |
| | **total** | **1,600,000** |

Market slice mapping:

```text
local market ordinal 0..99
  -> market_state scope 0..99
local market ordinal 100..1,000,099
  -> global order g = local-100
  -> scope = g / 10,000
  -> order = g % 10,000
```

Market record idは専用 `MarketScopeId` / `MarketOrderId` を使用する。descriptor RecordIdを捨てず、mapping evidenceに `descriptor_id -> authoritative_market_record_id` をbindする。

### 6.2 Governance = 400,000

| global descriptor ordinal range | partition | count |
|---|---|---:|
| 1,600,000..1,600,999 | `governance.polity` | 1,000 |
| 1,601,000..1,605,999 | `governance.institution` | 5,000 |
| 1,606,000..1,635,999 | `governance.law_rule` | 30,000 |
| 1,636,000..1,645,999 | `governance.jurisdiction` | 10,000 |
| 1,646,000..1,655,999 | `governance.territorial_claim` | 10,000 |
| 1,656,000..1,675,999 | `governance.effective_control` | 20,000 |
| 1,676,000..1,700,999 | `governance.public_authority` | 25,000 |
| 1,701,000..1,750,999 | `governance.tax_fiscal` | 50,000 |
| 1,751,000..1,820,999 | `governance.permission_license` | 70,000 |
| 1,821,000..1,830,999 | `governance.diplomacy` | 10,000 |
| 1,831,000..1,875,999 | `governance.security_incident` | 45,000 |
| 1,876,000..1,905,999 | `governance.investigation` | 30,000 |
| 1,906,000..1,930,999 | `governance.judicial_case` | 25,000 |
| 1,931,000..1,960,999 | `governance.enforcement` | 30,000 |
| 1,961,000..1,970,999 | `governance.military_authority` | 10,000 |
| 1,971,000..1,980,999 | `governance.border_control` | 10,000 |
| 1,981,000..1,999,999 | `governance.lineage` | 19,000 |
| | **total** | **400,000** |

All generic Society/Governance descriptors begin D2 unless a later authoritative detail transition changes the owning region. Detail workloadの `EstimatedRecordCount` は別契約であり、この2M countを追加生成しない。

## 7. Society/Governance Ref selectors

Required relation refsは次の canonical poolsへmodulo mappingする。

```text
member/worker/holder/learner/subject/debtor/suspect/investigator -> Resident
employer/provider/issuer/unit-or-org -> Organization
polity -> Polity
institution -> Institution
jurisdiction -> Jurisdiction
authority -> PublicAuthority
asset/cargo -> PhysicalPresence
spatial scope/origin/destination -> TileScope
claim/evidence when required -> InformationClaim
```

optional Refは原則NONE。semantic上non-emptyを要求する required RefListは1 actual refを入れ、empty permitted listはempty。status/lifecycleは `active`。

Law ASTは既存 versioned nested codecを使う。benchmark genesisは deterministic constant predicate/effect fixtureとし、runtime executable delegateやsource codeは保存しない。

## 8. Infrastructure = 500,000

| material class | count |
|---|---:|
| `network` (`infrastructure.network_topology /2.0`) | 100 |
| `node` (`infrastructure.network_topology /2.0`) | 20,000 |
| `edge` (`infrastructure.network_topology /2.0`) | 100,000 |
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

`node` / `edge` / queue requestは既存 specialized identity helperを使用する。残りは `infrastructure.active-record` descriptor ordinal rangeを material class table orderで割当て、descriptor->record mapping evidenceを出力する。

`service_queue` 250,000 recordは既存 `InfrastructureServiceRequestId(requestOrdinal)` と1:1。

## 9. Count acceptance

Canonical materializerはbuild/CIで少なくとも次をassertする。

```text
Environment D0 = 1,000,000
Environment D1 =   250,000
D1 source coverage = every D0 exactly once
Society = 1,600,000
Governance = 400,000
Society + Governance = 2,000,000
Market order = 1,000,000
Market state = 100
Infrastructure = 500,000
```

count mismatch、duplicate specialized mapping、missing Ref targetは materialization failure。件数調整のためのdummy recordは禁止。
