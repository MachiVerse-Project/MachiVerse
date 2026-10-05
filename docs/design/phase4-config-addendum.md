# 詳細設計 Phase 4: Config Specification Addendum

Status: Complete / P4-03 final addendum  
Tracking: Issue #16  
Parent: `phase4-config-specification.md`  
Cross-review: P4-04 / P4-06 / P4-07

## 1. 目的

P4-03初期Config specification作成後、P4-06 performance budgetとP4-07 observability/audit設計で追加が必要になったstandard Config fieldを、Phase4 Config schema 1.0公開前のfinal addendumとして固定する。

本addendumは`phase4-config-specification.md`と合わせてConfig schema 1.0の正本を構成する。

## 2. Simulation Core追加field

### 2.1 Detail materialization budget

| key | type | default | range | impact | mutability |
|---|---|---:|---:|---|---|
| `detail.promotion-max-regions-per-step` | uint16 | 4 | 1..1024 | SIMULATION | RUNTIME_SAFE |
| `detail.promotion-max-records-per-step` | uint32 | 20000 | 100..10000000 | SIMULATION | RUNTIME_SAFE |
| `detail.demotion-max-regions-per-step` | uint16 | 8 | 1..2048 | SIMULATION | RUNTIME_SAFE |
| `detail.demotion-max-records-per-step` | uint32 | 50000 | 100..20000000 | SIMULATION | RUNTIME_SAFE |

適用:

- Core確定effective Stepからatomic。
- changeはConfig historyへ保存。
- wall-clock lagで自動変更しない。
- budget不足時はcanonical detail transition queueでdeterministic defer。
- Diver-bound resident / active transaction floorをbudget都合でdemoteしない。

### 2.2 Core log retention

| key | type | default | range | impact | mutability |
|---|---|---:|---:|---|---|
| `observability.log-retention-days` | uint16 | 14 | 1..365 | OPERATIONAL | RUNTIME_SAFE |

これはdiagnostic log retentionでありP4-04 world history retentionを変更しない。

## 3. Gateway追加field

| key | type | default | range | impact | mutability |
|---|---|---:|---:|---|---|
| `audit.retention-days` | uint16 | 400 | 30..3650 | OPERATIONAL | RUNTIME_SAFE |
| `audit.query-max-page-size` | uint16 | 1000 | 100..10000 | OPERATIONAL | RUNTIME_SAFE |

Cross constraint:

```text
Admin View audit.default-page-size <= Gateway audit.query-max-page-size
```

Audit retentionはsecurity/management auditへ適用し、Core world execution historyのfull retentionを短縮しない。

## 4. Standard field count audit

`meta.*`を除くschema field count:

| component | base P4-03 | addendum | final 1.0 |
|---|---:|---:|---:|
| Simulation Core | 63 | 5 | 68 |
| Gateway | 34 | 2 | 36 |
| General View | 20 | 0 | 20 |
| Admin View | 12 | 0 | 12 |
| **total** | **129** | **7** | **136** |

Simulation Core 68には8 domain x 4 detail cadence = 32 fieldを含む。

## 5. Impact audit

Final 136 fieldについて:

- General View: SIMULATION field 0。
- Admin View: SIMULATION field 0。
- Gateway: SIMULATION field 0。Core配布scheduling policyをGateway Configで上書きしない。
- Simulation Core: StepRate/scheduling/detail cadence/detail budgetがSIMULATION。worker/persistence physical/publication/health/queue/observabilityはOPERATIONAL。

## 6. Mutability audit

- OIDC deployment identity/secret reference/origin: RESTART_REQUIRED。
- Core recovery digest validation: RESTART_REQUIRED。
- world-generation-only fieldは現在standard schema 1.0には追加していない。将来追加時はWORLD_REGENERATION_REQUIREDを使用する。
- その他standard runtime tuningはschema表どおりRUNTIME_SAFE。

RUNTIME_SAFEはatomic apply可能であることを意味し、arbitrary immediate mutationを意味しない。

## 7. Performance cross-review

P4-06 reference budgetと既定値を照合した。

承認:

- StepRate 30/1
- worker-count 4、range 1..4096（標準受入matrixは1/4/8/16、実装能力は16固定上限を持たない）
- D0〜D3 cadence defaults
- detail hysteresis / minimum residence
- promotion/demotion per-Step budget
- snapshot interval 18000 Step
- retained Snapshot 12
- zstd level 3
- publication full interval 900 Step
- Gateway publication buffer 1000 ms
- Core/Gateway queue limits

これらはinitial standard profileとしてP4-08 benchmark対象にする。

## 8. Persistence cross-review

P4-04と整合:

- Snapshot intervalはStep basis。
- compressionはlogical SnapshotDigestへ影響しない。
- retained snapshot count >= 2。
- recovery verify fieldはstartup/recovery behaviorだけを変更し、world replay semanticsをsilent変更しない。
- history full retentionはConfigから短縮できないv1.0 policy。

## 9. Observability cross-review

P4-07と整合:

- general diagnostic log: 14 days default。
- Gateway security/management audit: 400 days default。
- metric export interval: 1000 ms default。
- telemetry sampling/exporter failureはConfigGeneration/world outcomeへ影響しない。

Audit retention change自体をaudit eventとして記録する。

## 10. Protocol limit cross-review

- Core publication max chunk <= P4-02 1 MiB publication chunk limit。
- Gateway aggregation max operations <= batch hard limit 4096。
- aggregation uncompressed bytes <= envelope 8 MiB hard limit。
- Admin audit page default 200 <= Gateway server max default 1000。

## 11. Default completion

Addendum fieldもConfig schema 1.0 current defaultとして扱う。

Phase4 completion前に古いdraft Configを読み込む場合、欠落addendum fieldはschema default completionとして補完しatomic write-backする。

正式1.0公開後に同じfieldを新規追加する場合はminor migrationが必要だが、Phase4設計中のdraftには適用しない。

## 12. Canonical examples

4 componentのcomplete exampleは`phase4-config-standard-examples.md`を正本とする。

Exampleは:

- all component meta
- Core final addendum field
- Gateway required OIDC references
- View/Admin presentation defaults

を含む。

## 13. Error behavior

Addendum fieldにもP4-03 common errorを使用する。

```text
config.unknown-key
config.type-mismatch
config.out-of-range
config.cross-constraint
config.stale-generation
```

SIMULATION detail budget changeがinvalidな場合、change set全体をrejectしcurrent effective Configを維持する。

## 14. Acceptance criteria

- final standard Config field count 136を一意にenumerateできる。
- all 4 component canonical sampleがある。
- P4-04/P4-06/P4-07 cross-reviewで未解決default conflict 0件。
- addendum SIMULATION fieldはexplicit effective Stepを持つ。
- Audit/log retentionがworld history retentionを短縮しない。
- View/Admin Configからworld resultを変更できない。
- unknown/missing/default/write-back contractを維持する。

blocker: なし。