# Alpha 1.1 / INT-03 — `BodyRegionStateV1` nested schema bindings

Status: unresolved normative dependency audit  
Tracking: #240  
Implementation: Draft PR #265  
Applies to: `perf.reference.v1`

## 1. 目的

`resident.body_health.body_region_states` は P4-05 で `ordered list<BodyRegionStateV1>` として要求されているが、`BodyRegionStateV1` 自体の exact nested schema は正本化されていない。

本書は、親 `resident.body_health` payload で確定済みの境界と、nested schema を実装する前に追加で正本化が必要な項目を分離する。runtime の whole-resident health state や Phase 3 の概念モデルから body-region field を推測して新しい schema を作らない。

machine-readable mirror は `Qa04BodyRegionStateSchemaDependencyContractV1`。

この contract は `Qa04ReferenceWorldDependencyContractV1` の `NestedPayloadSchema` blocker 1件の**下位分解**であり、reference-world blocker 数を 9 から増やさない。

## 2. 既に固定済みの親 payload

P4-05 と `StandardDomainPayloadSchemaRegistry` は `resident.body_health` を次の exact field order で固定している。

```text
resident_ref: Ref
development_ppm: Ratio
health_capacity_ppm: Ratio
body_region_states: OrderedNestedList
injury_refs: RefList
disease_refs: RefList
recovery_ppm: Ratio
```

`body_region_states` 自体は required で、collection は ordered nested list。

P4-05 は collection rule として **region token canonical** を要求している。このため body-region value には canonical ordering を成立させる region identity が必要だが、region token の vocabulary、nested field 名、wire ordinal までは定義していない。

## 3. Phase 3 で固定済みの semantic boundary

Phase 3 Resident design は Body / Health が少なくとも次を扱うことを要求している。

- 身体・成長・加齢
- 健康
- 負傷
- 疾病
- 障害
- 回復
- 死亡
- 粗い身体部位が移動・仕事・生活へ与える差

一方、Phase 3 の `ResidentBodyHealthState` は概念モデルであり、Phase 4 wire schema の field set / scalar kind / optionality / ordinal を規定していない。

## 4. runtime の coarse health state との境界

`ResidentHealthStateV1` は whole-resident の deterministic runtime state として次を持つ。

```text
HealthCapacity
Pain
Stress
Fatigue
```

これは body-region identity を持たず、`BodyRegionStateV1` の exact persistence representation と同一である根拠はない。

したがって、例えば `region_token + pain_ppm + injury_ppm` のような nested schema を runtime shape から補完してはならない。

同様に、親 payload の `injury_refs` / `disease_refs` が存在することだけから、body-region nested value がそれらの Ref を重複保持する、あるいは保持しない、と決めてはならない。

## 5. 未解決の nested-schema dependencies

### 5.1 exact field set

Failure code:

`qa04.body-region.field-set-undefined`

正本化が必要:

- 1 body-region value が保持する exact field 集合
- region identity をどの field で表すか
- region-local capability / condition を保持するか
- parent-level `injury_refs` / `disease_refs` との責務分離

### 5.2 field ordinal / wire order

Failure code:

`qa04.body-region.field-order-undefined`

正本化が必要:

- nested record field ordinal
- canonical wire order
- schema evolution時に ordinal をどう維持するか

collection 自体の region-token canonical order と、1 nested value 内の field order は別契約として扱う。

### 5.3 scalar semantics / units

Failure code:

`qa04.body-region.scalar-semantics-undefined`

正本化が必要:

- 各 field の scalar kind
- ppm / count / token / ref 等の unit family
- bound / overflow behavior
- zero と absence の意味

whole-resident の `ResidentPpmV1` が存在することだけを理由に全 body-region 数値を ppm にしない。

### 5.4 optionality

Failure code:

`qa04.body-region.optionality-undefined`

正本化が必要:

- required / optional field
- absent と zero / empty collection の区別
- lifecycle/detail level による field absence が許可されるか

### 5.5 injury / disease / impairment representation

Failure code:

`qa04.body-region.condition-representation-undefined`

正本化が必要:

- region-local injury の表現
- disease manifestation の表現
- chronic condition / impairment / disability の表現
- parent `injury_refs` / `disease_refs` と nested state の authority boundary

Phase 3 の semantic list は exact representation を指定していない。

### 5.6 region vocabulary / extensibility

Failure code:

`qa04.body-region.region-vocabulary-undefined`

P4-05 の `region token canonical` を実装するため、少なくとも次が必要。

- canonical region token set または versioned registry rule
- token comparison / ordering
- unknown/future token の扱い
- detail level に応じた粗密 region の関係

文化・外見・人種等の presentation taxonomy を body-health persistence vocabulary として暗黙導入しない。

### 5.7 nested Ref / target-schema closure

Failure code:

`qa04.body-region.reference-closure-undefined`

正本化が必要:

- nested value が Ref を持つか
- 持つ場合の exact field 名
- target partition / record kind / schema version
- optionality
- canonical Ref closure / recovery validation rule

## 6. 上位 blocker との関係

`Qa04ReferenceWorldDependencyContractV1` の BodyRegionState entry は引き続き exactly one:

- dependency: `resident.body-health.body-region-states-schema`
- kind: `NestedPayloadSchema`
- partition: `resident.body_health`
- field: `body_region_states`
- compatibility failure code: `qa04.material.body-region-state-schema-undefined`

新しい7 failure codesは診断用 subdependency code であり、reference-world `FailureCodes` へ追加しない。

したがって:

```text
reference-world blockers = 9
BodyRegionState top-level world blockers = 1
BodyRegionState schema subdependencies = 7
```

を維持する。

## 7. top-level blocker を解消できる条件

次をすべて満たすまで `qa04.material.body-region-state-schema-undefined` を除去しない。

1. 上記7 subdependencyが versioned normative rule として確定する。
2. exact nested schema id/version を固定する。
3. `ICanonicalDomainNestedValueV1` 実装を追加する。
4. canonical region ordering / duplicate rule を固定する。
5. nested codec registryへ exact schema を登録する。
6. Ref がある場合は target-schema closure を recoveryまで検証する。
7. `ResidentBodyHealthPayloadV1` の Snapshot -> recovery -> semantic rehash で nested value が lossless に一致する。
8. canonical reference-world materializer が actual body-region state を生成する。

この時点までは:

- `qa04.material.body-region-state-schema-undefined` を維持
- `referenceWorldMaterialized=false`
- BodyRegionState exact nested schema incomplete

とする。
