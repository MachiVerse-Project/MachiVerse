# Alpha 1.1 DetailRegion reference authority

Status: Proposed / approval pending  
Tracking: #305  
Release tracking: #240  
Implementation PR: #265

## 1. Purpose

`perf.reference.v1` canonical workload の detail-transition path が要求する `spatial.detail_regions` initial-world authority を、既存の canonical TileScope と既存 workload mapping を変更せずに定義する。

本書は benchmark-only initial-world authority の proposal である。approval前に implementation、dependency blocker解除、release flag変更を行ってはならない。

## 2. Existing normative surface

既に固定済みの事項:

- `spatial.detail_regions` は Spatial owner partition。
- payload fields:
  - `scope_ref: Ref`
  - `level_by_domain: ordered map<Token,uint8>`
  - `lineage_generation: uint32`
  - `last_transition_step: Step`
  - `active_guards: TokenList`
- `DetailRegionStateV1` は non-zero `detail_region_id` / non-zero `spatial_scope_ref` と、registered standard-domain tokenだけを受理する。
- canonical TileScope authority は 64 x 64 = **4,096** records。
- canonical detail-transition cadence は 300 Stepごと、89 cadence。
- 1 cadenceあたり promotion 6 + demotion 10 = 16 requests。
- canonical request total は **1,424**。
- `Qa04CanonicalDetailTransitionBindingV1` が各requestの TileIndex / SpatialScopeId / DomainToken / CurrentLevel / TargetLevelを既に固定している。
- 1,424 canonical requirementsのTileIndexは全てunique。
- promotion requirementは `D1LocalAggregate -> D0Entity`。
- demotion requirementは `D0Entity -> D1LocalAggregate`。
- request bindingは actual regionの `GetLevel(DomainToken)` が requirement `CurrentLevel` と一致しなければ fail closedする。

Config schemaはdetail cadence、hysteresis、guard floor、per-step budgetを定義するが、initial-world `level_by_domain` profile自体は定義していない。したがってConfig defaultからgenesis profileを推測してはならない。

## 3. Population authority

`spatial.detail_regions` canonical initial populationを **4,096 records** とする。

```text
TileScope[0]    <-> DetailRegion[0]
TileScope[1]    <-> DetailRegion[1]
...
TileScope[4095] <-> DetailRegion[4095]
```

各canonical TileScopeにつきexactly one DetailRegion recordを持つ。

同じTileScopeを指す複数DetailRegion、またはcanonical TileScopeに対応しないbenchmark DetailRegionはinitial populationへ含めない。

## 4. DetailRegion identity proposal

TileIndex `t`, `0 <= t < 4096` に対し:

```text
DetailRegionId(t) = DerivedIdentity.DeriveEntityId(
    world_id       = perf.reference.v1 WorldId,
    creation_step  = 0,
    creator_domain = spatial,
    creator_entity = ZERO,
    creation_kind  = perf.detail-region,
    local_ordinal  = t)
```

`creation_kind = perf.detail-region` をbenchmark-only StableToken authorityとする。

このidentity recipeは:

- Step 0で独立導出可能
- process / worker / iteration order非依存
- TileScope identity recipeとcreation kindが異なるためsemantic identityを混同しない
- TileIndexからdeterministically再構成可能

という性質を持つ。

## 5. Scope binding

DetailRegion `t` のpayload:

```text
scope_ref = Qa04SpatialTileScopeAuthorityV1.ScopeRef(t)
```

actual resolverは `spatial.scope_registry` のactual TileScope recordを解決できなければならない。

ZERO / missing / wrong partition / wrong TileIndex scope referenceはfail closedする。

## 6. Envelope genesis

全4,096 record共通:

```text
record_revision = 1
created_step = 0
retired_step = NONE
detail_level = D2RegionalAggregate
lineage_ref = NONE
```

partition stateのbasis Stepは0、initial logical revisionはproduction materializerが使用するcanonical initial revisionへ一致させる。

Envelopeの`detail_level`とpayload `level_by_domain`は別概念である。前者はこのpartition record material自体のSnapshot/detail classification、後者はDetailRegionが管理するdomain別runtime detail stateである。

## 7. Genesis level_by_domain

### 7.1 Complete domain map

各DetailRegionは `StandardDomainExecutionPlanV1.Create().Entries` に登録された8 standard domainを**全て** `level_by_domain` に保持する。

DomainTokenはASCII ascendingでcanonicalizeする。

8 domain:

```text
spatial
environment
physical_built
participation
resident
society_economy
governance_security
infrastructure_information
```

実際のmap orderingはtoken ASCII ascendingとし、上記の記述順をserialization orderとして使用しない。

### 7.2 Baseline

全4,096 region x 8 domainをまず:

```text
D2RegionalAggregate
```

とする。

これはbenchmark-only genesis baselineであり、一般worldのdefault detail profileを定義するものではない。

### 7.3 Canonical workload override

`Qa04CanonicalDetailTransitionBindingV1.CanonicalRequirements()` の各 requirement `r` について:

```text
region = DetailRegion[r.TileIndex]
region.level_by_domain[r.DomainToken] = r.CurrentLevel
```

とする。

canonical requirementsは1,424 TileIndexが全てuniqueなので、同一regionへの複数overrideはない。

既存workload mappingは変更しない。

### 7.4 Exact override cardinality

89 cadenceそれぞれで:

- promotion: 6
- demotion: 10

よって:

```text
promotion current D1 overrides = 89 * 6  = 534
demotion current D0 overrides  = 89 * 10 = 890
---------------------------------------------
total overrides                           1,424
```

promotionは各cadenceで Environment 3 / Resident 3:

```text
environment D1 = 267
resident D1    = 267
```

demotionは各cadenceで Environment 5 / PhysicalBuilt 5:

```text
environment D0    = 445
physical_built D0 = 445
```

all domain-map entry count:

```text
4,096 * 8 = 32,768
```

therefore genesis entry cardinality:

```text
D0 =    890
D1 =    534
D2 = 31,344
--------------
     32,768
```

untouched TileScope count:

```text
4,096 - 1,424 = 2,672
```

その2,672 regionは全8 domainがD2 baselineのまま。

## 8. Other payload genesis

全4,096 records:

```text
lineage_generation = 0
last_transition_step = 0
active_guards = []
```

理由:

- genesis以前のdetail transitionを表すauthorityは存在しない。
- `DetailRegionStateV1.Apply` はtransition成功ごとにlineage generationをincrementするため、initial generation 0をexplicit benchmark authorityとする。
- canonical workload開始前のtransition Stepはないためinitial last transitionを0とする。
- bound-resident / active-transaction guardをinitially activeとするcanonical benchmark authorityは存在しないためemptyを明示する。

これらはproposal値であり、approvalによって初めてnormative benchmark authorityとなる。

## 9. Workload compatibility proof

approval後のproduction implementationは、全1,424 canonical requirementsについて少なくとも次を検証する:

```text
region.DetailRegionId == DetailRegionId(requirement.TileIndex)
region.SpatialScopeRef == requirement.SpatialScopeId
region.GetLevel(requirement.DomainToken) == requirement.CurrentLevel
```

さらに `Qa04CanonicalDetailTransitionBindingV1.BindForStep(...)` をcanonical 89 cadenceすべてに対してactual DetailRegion resolverで実行し、合計1,424 requestがbinding可能であることを証明する。

TileIndex reuseが将来発生した場合、このgenesis recipeをそのまま継続してはならない。複数requestが同一tileへ要求する時系列stateを明示的に再設計する。

## 10. Snapshot / recovery requirement

approval後はproduction `DomainPartitionSnapshotAuthorityV1<SpatialDetailRegionsPayloadV1>` / generic Domain-partition codec pathを使用し、full 4,096 recordsについて:

1. actual TileScope resolverを用いたencode validation
2. canonical PartitionStateHeader生成
3. fragment encode/decode
4. same actual reference authorityでsemantic recovery validation
5. recovered item count = 4,096
6. recovered canonical digest = source canonical digest

を証明する。

fixture/permissive resolverをrelease proofとして使用しない。

## 11. Negative proof

少なくとも以下をfail closedで検証する:

- missing TileScope target
- scope partition mismatch
- region-to-scope TileIndex mismatch
- duplicate DetailRegionId
- missing standard domain entry
- unregistered domain token
- requirement CurrentLevel mismatch
- non-empty unexpected genesis guard
- lineage generation drift
- last transition Step drift

## 12. Dependency impact after successful implementation

approval + documentation merge + develop sync + production implementation + current-head proofが全て成功した場合のみ:

```text
DetailRegion direct dependencies: 2 -> 0
direct canonical dependencies:    5 -> 3
```

remaining direct dependenciesは Participation `control_mode` の3件となる。

Detail-transition workload parent blockerの解除は、actual production bindingが成立したことをcurrent-head testで確認した後にのみ行う。

このpackage単独では:

- `referenceWorldMaterialized` をtrueにしない
- `authoritativeStepLoopAvailable` をtrueにしない
- Society/Governance parent blockerを変更しない
- Infrastructure parent blockerを変更しない

## 13. Implementation order after approval

1. 本書をApproved normativeへ更新
2. documentationへmerge
3. developへsync
4. #265 branchを最新develop authorityへ同期
5. `Qa04DetailRegionCanonicalAuthorityV1` identity/genesis builder
6. actual 4,096 TileScope resolver closure
7. `SpatialDetailRegionsPayloadV1` 4,096-record materialization
8. 1,424 canonical request binding proof
9. full Snapshot/recovery proof
10. fail-closed negative tests
11. dependency contract 2 -> 0
12. current-head full CI
13. #240 / #265 checkpoint同期

## 14. Non-goals / invariant boundaries

- View camera/FPSをdetail level authorityにしない。
- Config cadence/floor値から未定義のnormal initial profileを推測しない。
- benchmark-only D2 baselineを一般world defaultへ昇格しない。
- canonical request Tile permutation / cadence / domain selectionを変更しない。
- new partition/schema versionを作らない。
- `StandardDomainPartitionRegistry` v1を変更しない。
- synthetic/reduced proofをrelease evidenceとして扱わない。
