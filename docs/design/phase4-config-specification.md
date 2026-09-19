# 詳細設計 Phase 4: Config Specification

Status: In Progress / P4-03  
Tracking: Issue #16  
Parent: `phase4-implementation-ready-design.md`  
Predecessor: `phase1-config-contract.md`  
Protocol dependency: `phase4-protocol-payload-catalog.md`

## 1. 目的

Simulation Core、Gateway、General View、Admin Viewが所有するUTF-8 TOML 1.0 Configについて、実装時に追加判断が不要となるようstable key、exact type、default、range、impact、mutability、runtime apply boundary、cross-field constraintを固定する。

Phase 1 Config契約を変更せず、次を具体化する。

- component別Config schema `1.0`
- current default value
- runtime-safe / restart / world-regeneration boundary
- simulation-affecting effective Step
- protocol exposure
- unknown/missing/secret handling
- domain detail cadence/hysteresis baseline

## 2. 共通document header

全component Configは次を必須とする。

```toml
[meta]
format = "machiverse-config"
schema_version = "1.0"
component = "<component-token>"
```

standard component token:

```text
simulation-core
gateway
general-view
admin-view
```

`meta`はruntime change対象外。component mismatchは起動拒否。

## 3. Common Config behavior

- unknown key: reject。
- duplicate key: TOML parse error。
- missing field with default: current schema defaultで補完しatomic write-back。
- missing required field without default: reject。
- unsupported newer minor: reject。
- major mismatch: explicit migrationなしではreject。
- NaN / ±Infinity: reject。
- secret material実値: Configへ保存禁止。secret referenceのみ。
- field path sort: ASCII bytewise ascending。
- normalized Config digest: Phase 1 `DomainHash("mv.config.v1", ...)`。

## 4. Unit conventions

Config valueのunit token:

```text
steps
milliseconds
seconds
bytes
mebibytes
count
permille
ratio
frames-per-second
```

durationはinteger milliseconds/secondsを使用し、裸のbinary64 secondsを使用しない。

`binary64`はpresentation-only ratio等に限定する。

## 5. Runtime apply classes

### 5.1 SIMULATION + RUNTIME_SAFE

Admin Config changeがacceptされた場合、Coreがauthoritative `effective_step=S` を確定し、`State(S) -> State(S+1)` transitionからnew effective valueを使用する。

change setは同一effective Stepでatomic。

### 5.2 OPERATIONAL + RUNTIME_SAFE

owner component内のquiescent boundaryでatomic applyする。

- connection timer: next timer schedule boundary
- queue admission limit: next admission boundary
- retry/backoff: next retry scheduling boundary
- log/metric setting: next emission/query boundary

既にaccepted/custody-held Operationのidentity/semanticを変更しない。

### 5.3 PRESENTATION + RUNTIME_SAFE

next render/UI update boundaryでatomic apply可能。

authoritative worldへ送信しない。

## 6. Simulation Core schema

Schema identity:

```text
config.simulation-core / 1.0
```

### 6.1 Step rate

| key | type | default | range | impact | mutability |
|---|---|---:|---:|---|---|
| `simulation.step-rate.numerator` | uint32 | 30 | 1..240 | SIMULATION | RUNTIME_SAFE |
| `simulation.step-rate.denominator` | uint32 | 1 | 1..1000 | SIMULATION | RUNTIME_SAFE |

Cross constraints:

- reduced rational formへnormalizeする。
- effective rate `numerator/denominator` は `1/10 .. 240/1` steps/sec。
- denominator=0は禁止。
- rate changeごとに`RateGeneration`を+1。
- rate generation wrap前にworld migration required。

### 6.2 Worker/runtime

| key | type | default | range | impact | mutability |
|---|---|---:|---:|---|---|
| `runtime.worker-count` | uint16 | 4 | 1..4096 | OPERATIONAL | RUNTIME_SAFE |
| `runtime.domain-timeout-ms` | uint32 | 30000 | 100..300000 | OPERATIONAL | RUNTIME_SAFE |

`runtime.worker-count`を変えてもStateDiagnosticが変化してはならない。

`runtime.worker-count`はCPU実行予算であり、Domain数や16という固定値を実装上の並列度上限にしてはならない。Coreは少なくとも1/4/8/16 workerを標準受入profileとして扱い、同一実装で16を超えるworker budgetも受理できなければならない。実行時の同時CPU work数はwork item数・dependency・host schedulerによってrequested値未満になり得るが、Coreがrequested値を16へsilent clampしてはならない。requested worker countがhost logical processor countを超える場合はoperational oversubscriptionとして許可し、必要ならwarningを出せるが、world semanticsを変更しない。

`domain-timeout-ms`はoperational failure detectionでありsimulation deadlineではない。

### 6.3 Operation scheduling policy

| key | type | default | range/enum | impact | mutability |
|---|---|---:|---|---|---|
| `scheduling.min-lead-steps` | uint32 | 2 | 0..300 | SIMULATION | RUNTIME_SAFE |
| `scheduling.default-deadline-window-steps` | uint32 | 90 | 1..36000 | SIMULATION | RUNTIME_SAFE |
| `scheduling.grace-steps` | uint32 | 15 | 0..3600 | SIMULATION | RUNTIME_SAFE |
| `scheduling.late-policy` | enum | `defer-within-grace` | `reject`, `defer-within-grace` | SIMULATION | RUNTIME_SAFE |

`default-deadline-window-steps`をdisabledにする場合はminor schema追加でoptional化し、magic zeroを使用しない。v1.0ではrequired positive integer。

### 6.4 Detail common policy

| key | type | default | range/enum | impact | mutability |
|---|---|---:|---|---|---|
| `detail.promotion-hysteresis-steps` | uint32 | 30 | 0..36000 | SIMULATION | RUNTIME_SAFE |
| `detail.demotion-quiet-steps` | uint32 | 300 | 0..360000 | SIMULATION | RUNTIME_SAFE |
| `detail.minimum-residence-steps` | uint32 | 300 | 0..360000 | SIMULATION | RUNTIME_SAFE |
| `detail.bound-resident-floor` | enum | `d0-entity` | `d0-entity`, `d1-local-aggregate` | SIMULATION | RUNTIME_SAFE |
| `detail.active-transaction-floor` | enum | `d0-entity` | `d0-entity`, `d1-local-aggregate` | SIMULATION | RUNTIME_SAFE |

`bound-resident-floor=d1-local-aggregate`は将来performance profile用に許可するが、P4-05/P4-08でResident participation semanticsを満たすことが確認できる実装だけが使用可能。standard defaultはD0。

### 6.5 Domain cadence baseline

値は「authoritative SimulationStep上で、該当detail stateのperiodic rate updateを評価する最長間隔」。event/Operationによるrequired same-Step処理を遅延させる根拠にはしない。

| domain | D0 | D1 | D2 | D3 |
|---|---:|---:|---:|---:|
| `spatial` | 1 | 10 | 60 | 600 |
| `environment` | 1 | 5 | 30 | 300 |
| `physical_built` | 1 | 5 | 30 | 300 |
| `participation` | 1 | 1 | 5 | 30 |
| `resident` | 1 | 5 | 30 | 300 |
| `society_economy` | 5 | 30 | 300 | 1800 |
| `governance_security` | 10 | 60 | 600 | 3600 |
| `infrastructure_information` | 1 | 5 | 30 | 300 |

Config key format:

```text
detail.domain.<domain-token>.d0-cadence-steps
detail.domain.<domain-token>.d1-cadence-steps
detail.domain.<domain-token>.d2-cadence-steps
detail.domain.<domain-token>.d3-cadence-steps
```

全key:

- type: uint32
- range: 1..360000
- impact: SIMULATION
- mutability: RUNTIME_SAFE

Cross constraint:

```text
D0 <= D1 <= D2 <= D3
```

per domain。

このbaselineはP4-06 performance budgetで測定し、Phase4 completion前に必要なら同じschema内でdefaultを再調整する。Phase4完了後にdefault意味を変更する場合はConfig schema minor updateとmigration/default historyを要求する。

### 6.6 Snapshot / persistence operational policy

| key | type | default | range | impact | mutability |
|---|---|---:|---:|---|---|
| `persistence.snapshot-interval-steps` | uint64 | 18000 | 30..100000000 | OPERATIONAL | RUNTIME_SAFE |
| `persistence.snapshot-retain-count` | uint16 | 12 | 2..1024 | OPERATIONAL | RUNTIME_SAFE |
| `persistence.snapshot-compression` | enum | `zstd` | `none`, `zstd` | OPERATIONAL | RUNTIME_SAFE |
| `persistence.snapshot-zstd-level` | int8 | 3 | -5..19 | OPERATIONAL | RUNTIME_SAFE |
| `persistence.recovery-verify-state-digest` | bool | true | bool | OPERATIONAL | RESTART_REQUIRED |

Persistence logical format/commit/fsync contractはP4-04を正本とする。

compression選択でauthoritative digestを変えない。

### 6.7 Publication policy

| key | type | default | range | impact | mutability |
|---|---|---:|---:|---|---|
| `publication.delta-enabled` | bool | true | bool | OPERATIONAL | RUNTIME_SAFE |
| `publication.full-interval-steps` | uint32 | 900 | 1..360000 | OPERATIONAL | RUNTIME_SAFE |
| `publication.max-chunk-bytes` | uint32 | 1048576 | 16384..1048576 | OPERATIONAL | RUNTIME_SAFE |
| `publication.queue-capacity` | uint16 | 64 | 4..4096 | OPERATIONAL | RUNTIME_SAFE |

`max-chunk-bytes`はP4-02 hard limit 1 MiB以下。

### 6.8 Master health policy

| key | type | default | range | impact | mutability |
|---|---|---:|---:|---|---|
| `master.heartbeat-interval-ms` | uint32 | 1000 | 100..60000 | OPERATIONAL | RUNTIME_SAFE |
| `master.heartbeat-timeout-ms` | uint32 | 5000 | 500..300000 | OPERATIONAL | RUNTIME_SAFE |
| `master.min-ready-heartbeats` | uint8 | 2 | 1..20 | OPERATIONAL | RUNTIME_SAFE |

Cross constraint:

```text
heartbeat-timeout-ms >= 3 * heartbeat-interval-ms
```

Master selection timingはworld same-Step orderingへ使用しない。

### 6.9 Core queue/admission

| key | type | default | range | impact | mutability |
|---|---|---:|---:|---|---|
| `queue.protocol-ingress-capacity` | uint32 | 8192 | 256..1048576 | OPERATIONAL | RUNTIME_SAFE |
| `queue.accepted-operation-admission-limit` | uint32 | 65536 | 1024..16777216 | OPERATIONAL | RUNTIME_SAFE |
| `queue.persistence-capacity` | uint32 | 8192 | 256..1048576 | OPERATIONAL | RUNTIME_SAFE |

accepted durable Operationをcapacity pressureでdropしない。limit到達時はnew admissionへbackpressure/rejectする。

### 6.10 Core observability

| key | type | default | range/enum | impact | mutability |
|---|---|---:|---|---|---|
| `observability.log-level` | enum | `info` | `trace`,`debug`,`info`,`warn`,`error` | OPERATIONAL | RUNTIME_SAFE |
| `observability.metric-export-interval-ms` | uint32 | 1000 | 100..60000 | OPERATIONAL | RUNTIME_SAFE |
| `observability.state-digest-every-steps` | uint32 | 1 | 1..10000 | OPERATIONAL | RUNTIME_SAFE |

`state-digest-every-steps`はdiagnostic export cadenceのみ。transition commitに必要なpartition/state digest生成を省略する設定ではない。

## 7. Gateway schema

Schema identity:

```text
config.gateway / 1.0
```

### 7.1 Protocol / reconnect

| key | type | default | range | impact | mutability |
|---|---|---:|---:|---|---|
| `network.connect-timeout-ms` | uint32 | 10000 | 500..120000 | OPERATIONAL | RUNTIME_SAFE |
| `network.reconnect-initial-ms` | uint32 | 250 | 50..60000 | OPERATIONAL | RUNTIME_SAFE |
| `network.reconnect-max-ms` | uint32 | 10000 | 1000..300000 | OPERATIONAL | RUNTIME_SAFE |
| `network.reconnect-multiplier-permille` | uint16 | 2000 | 1000..10000 | OPERATIONAL | RUNTIME_SAFE |
| `network.reconnect-jitter-permille` | uint16 | 200 | 0..1000 | OPERATIONAL | RUNTIME_SAFE |

Cross constraint:

```text
reconnect-max-ms >= reconnect-initial-ms
```

retry/backoff/jitterをworld schedulingへ使用しない。

### 7.2 Peer heartbeat

| key | type | default | range | impact | mutability |
|---|---|---:|---:|---|---|
| `peer.heartbeat-interval-ms` | uint32 | 1000 | 100..60000 | OPERATIONAL | RUNTIME_SAFE |
| `peer.heartbeat-timeout-ms` | uint32 | 5000 | 500..300000 | OPERATIONAL | RUNTIME_SAFE |

`timeout >= 3 * interval`。

### 7.3 Aggregation

| key | type | default | range | impact | mutability |
|---|---|---:|---:|---|---|
| `aggregation.window-ms` | uint32 | 10 | 0..1000 | OPERATIONAL | RUNTIME_SAFE |
| `aggregation.max-operations` | uint16 | 512 | 1..4096 | OPERATIONAL | RUNTIME_SAFE |
| `aggregation.max-uncompressed-bytes` | uint32 | 4194304 | 16384..8388608 | OPERATIONAL | RUNTIME_SAFE |

`max-operations`はP4-02 batch hard limit 4096以下。

aggregation window arrival orderをauthoritative merge orderへ使用しない。

### 7.4 Gateway queue/admission

| key | type | default | range | impact | mutability |
|---|---|---:|---:|---|---|
| `queue.connection-ingress-capacity` | uint32 | 4096 | 128..1048576 | OPERATIONAL | RUNTIME_SAFE |
| `queue.local-operation-capacity` | uint32 | 16384 | 512..4194304 | OPERATIONAL | RUNTIME_SAFE |
| `queue.custody-admission-limit` | uint32 | 65536 | 1024..16777216 | OPERATIONAL | RUNTIME_SAFE |
| `queue.peer-batch-capacity` | uint32 | 2048 | 64..262144 | OPERATIONAL | RUNTIME_SAFE |
| `queue.publication-capacity` | uint16 | 64 | 4..4096 | OPERATIONAL | RUNTIME_SAFE |
| `queue.result-capacity` | uint32 | 8192 | 256..1048576 | OPERATIONAL | RUNTIME_SAFE |

custody-held Operationをsilent dropしない。

### 7.5 Publication/cache

| key | type | default | range | impact | mutability |
|---|---|---:|---:|---|---|
| `publication.buffer-ms` | uint32 | 1000 | 0..10000 | OPERATIONAL | RUNTIME_SAFE |
| `publication.max-client-backlog` | uint16 | 8 | 1..256 | OPERATIONAL | RUNTIME_SAFE |
| `cache.max-mebibytes` | uint32 | 1024 | 64..65536 | OPERATIONAL | RUNTIME_SAFE |
| `cache.max-confirmed-publications` | uint16 | 120 | 2..4096 | OPERATIONAL | RUNTIME_SAFE |

buffer/coalesceはView freshnessだけを変え、Core world stateを変更しない。

### 7.6 Result / custody retention

| key | type | default | range | impact | mutability |
|---|---|---:|---:|---|---|
| `result.rich-retention-seconds` | uint32 | 86400 | 60..2592000 | OPERATIONAL | RUNTIME_SAFE |
| `custody.local-terminal-retention-seconds` | uint32 | 86400 | 60..2592000 | OPERATIONAL | RUNTIME_SAFE |

Core world-lifetime dedup tombstoneの代替ではない。

### 7.7 OIDC/BFF deployment

| key | type | default | constraint | impact | mutability |
|---|---|---|---|---|---|
| `auth.oidc.issuer` | string | NONE | absolute HTTPS URI, <=2048 bytes | OPERATIONAL | RESTART_REQUIRED |
| `auth.oidc.client-id` | string | NONE | 1..512 UTF-8 bytes | OPERATIONAL | RESTART_REQUIRED |
| `auth.oidc.client-secret-ref` | string | NONE | secret reference, 1..512 bytes | OPERATIONAL | RESTART_REQUIRED |
| `auth.oidc.redirect-base-uri` | string | NONE | absolute HTTPS URI, no fragment | OPERATIONAL | RESTART_REQUIRED |
| `auth.allowed-origins` | array<string> | NONE | 1..64 exact HTTPS origins | OPERATIONAL | RESTART_REQUIRED |

全てrequired。secret material実値ではなくsecret reference。

### 7.8 Auth/session lifetime

| key | type | default | range | impact | mutability |
|---|---|---:|---:|---|---|
| `auth.login-transaction-lifetime-seconds` | uint32 | 600 | 60..1800 | OPERATIONAL | RUNTIME_SAFE |
| `auth.session-idle-lifetime-seconds` | uint32 | 3600 | 300..86400 | OPERATIONAL | RUNTIME_SAFE |
| `auth.session-absolute-lifetime-seconds` | uint32 | 43200 | 900..604800 | OPERATIONAL | RUNTIME_SAFE |
| `auth.max-active-sessions-per-account` | uint16 | 16 | 1..1024 | OPERATIONAL | RUNTIME_SAFE |

Cross constraint:

```text
session-absolute-lifetime-seconds >= session-idle-lifetime-seconds
```

session expiryだけでParticipation bindingを解除しない。

### 7.9 Gateway observability

| key | type | default | range/enum | impact | mutability |
|---|---|---:|---|---|---|
| `observability.log-level` | enum | `info` | standard log levels | OPERATIONAL | RUNTIME_SAFE |
| `observability.metric-export-interval-ms` | uint32 | 1000 | 100..60000 | OPERATIONAL | RUNTIME_SAFE |
| `observability.log-retention-days` | uint16 | 14 | 1..365 | OPERATIONAL | RUNTIME_SAFE |

Audit retentionはP4-07別authority。

## 8. General View schema

Schema identity:

```text
config.general-view / 1.0
```

General View ConfigはSIMULATION keyを持たない。

### 8.1 Rendering

| key | type | default | range | impact | mutability |
|---|---|---:|---:|---|---|
| `render.target-fps` | uint16 | 60 | 15..240 | PRESENTATION | RUNTIME_SAFE |
| `render.max-pixel-ratio` | binary64 | 2.0 | 0.5..4.0 | PRESENTATION | RUNTIME_SAFE |
| `render.lod-bias` | int8 | 0 | -4..4 | PRESENTATION | RUNTIME_SAFE |
| `render.max-visible-objects` | uint32 | 100000 | 1000..2000000 | PRESENTATION | RUNTIME_SAFE |

render limitでauthoritative entity existenceを変更しない。

### 8.2 Interpolation/prediction

| key | type | default | range | impact | mutability |
|---|---|---:|---:|---|---|
| `interpolation.delay-ms` | uint16 | 100 | 0..1000 | PRESENTATION | RUNTIME_SAFE |
| `prediction.enabled` | bool | true | bool | PRESENTATION | RUNTIME_SAFE |
| `prediction.max-horizon-ms` | uint16 | 150 | 0..1000 | PRESENTATION | RUNTIME_SAFE |
| `reconcile.soft-duration-ms` | uint16 | 120 | 0..2000 | PRESENTATION | RUNTIME_SAFE |
| `reconcile.max-soft-duration-ms` | uint16 | 500 | 0..5000 | PRESENTATION | RUNTIME_SAFE |

`soft-duration <= max-soft-duration`。

prediction valueをWorldContext basis/effective Stepへ流用しない。

### 8.3 Client resource/cache

| key | type | default | range | impact | mutability |
|---|---|---:|---:|---|---|
| `cache.max-mebibytes` | uint32 | 256 | 32..4096 | PRESENTATION | RUNTIME_SAFE |
| `asset.max-concurrency` | uint8 | 8 | 1..32 | PRESENTATION | RUNTIME_SAFE |
| `protocol.receive-queue-capacity` | uint16 | 256 | 16..4096 | OPERATIONAL | RUNTIME_SAFE |
| `operation.pending-capacity` | uint16 | 1024 | 16..65535 | OPERATIONAL | RUNTIME_SAFE |

pending capacity pressureで既送信Operationをnew identityにしない。

### 8.4 Reconnect

| key | type | default | range | impact | mutability |
|---|---|---:|---:|---|---|
| `network.reconnect-initial-ms` | uint32 | 250 | 50..60000 | OPERATIONAL | RUNTIME_SAFE |
| `network.reconnect-max-ms` | uint32 | 10000 | 1000..300000 | OPERATIONAL | RUNTIME_SAFE |
| `network.reconnect-multiplier-permille` | uint16 | 2000 | 1000..10000 | OPERATIONAL | RUNTIME_SAFE |

`max >= initial`。

### 8.5 UI defaults

| key | type | default | constraint | impact | mutability |
|---|---|---|---|---|---|
| `ui.locale` | string | `auto` | `auto` or BCP47-like app-supported tag | PRESENTATION | RUNTIME_SAFE |
| `ui.reduced-motion-default` | bool | false | bool | PRESENTATION | RUNTIME_SAFE |
| `ui.show-prediction-indicator` | bool | true | bool | PRESENTATION | RUNTIME_SAFE |
| `observability.client-log-level` | enum | `warn` | standard log levels | OPERATIONAL | RUNTIME_SAFE |

user-local preference may override presentation defaults without modifying shared component Config; such preference is not authoritative ConfigGeneration input。

## 9. Admin View schema

Schema identity:

```text
config.admin-view / 1.0
```

Admin View ConfigはSIMULATION keyを持たない。

### 9.1 Dashboard/metrics

| key | type | default | range | impact | mutability |
|---|---|---:|---:|---|---|
| `dashboard.refresh-ms` | uint32 | 1000 | 250..60000 | PRESENTATION | RUNTIME_SAFE |
| `metrics.local-history-samples` | uint32 | 3600 | 60..100000 | PRESENTATION | RUNTIME_SAFE |
| `metrics.max-series` | uint16 | 200 | 10..5000 | PRESENTATION | RUNTIME_SAFE |

### 9.2 Log/audit presentation

| key | type | default | range | impact | mutability |
|---|---|---:|---:|---|---|
| `log.default-page-size` | uint16 | 200 | 1..1000 | PRESENTATION | RUNTIME_SAFE |
| `log.local-window-records` | uint32 | 5000 | 100..100000 | PRESENTATION | RUNTIME_SAFE |
| `audit.default-page-size` | uint16 | 200 | 1..1000 | PRESENTATION | RUNTIME_SAFE |

server retentionを変更しない。

### 9.3 Request/confirmation presentation

| key | type | default | range | impact | mutability |
|---|---|---:|---:|---|---|
| `request.presentation-timeout-ms` | uint32 | 30000 | 1000..300000 | PRESENTATION | RUNTIME_SAFE |
| `confirmation.ux-timeout-seconds` | uint32 | 120 | 10..1800 | PRESENTATION | RUNTIME_SAFE |

`presentation-timeout-ms`到達をserver Operation cancel/terminal rejectと解釈しない。

### 9.4 Resource/reconnect

| key | type | default | range | impact | mutability |
|---|---|---:|---:|---|---|
| `cache.max-mebibytes` | uint32 | 128 | 16..2048 | PRESENTATION | RUNTIME_SAFE |
| `network.reconnect-initial-ms` | uint32 | 250 | 50..60000 | OPERATIONAL | RUNTIME_SAFE |
| `network.reconnect-max-ms` | uint32 | 10000 | 1000..300000 | OPERATIONAL | RUNTIME_SAFE |
| `observability.client-log-level` | enum | `warn` | standard log levels | OPERATIONAL | RUNTIME_SAFE |

Admin View Configでtarget component permission/config/audit retentionを変更しない。

## 10. Protocol exposure

Config field metadataはAdmin protocol `ConfigEntryWireV1`へ次を公開可能。

- key
- effective value（sensitiveでない場合）
- impact
- mutability
- current owner ConfigGeneration
- validation constraints summary

secret reference field:

```text
protocol_exposure = METADATA_ONLY
```

とし、valueを返さない。

Standard sensitive fields:

```text
auth.oidc.client-secret-ref
```

secret reference名自体もdeployment情報を含み得るためAdmin readではdefault非表示、`is_configured=true`のみ返す。

## 11. Config change validation order

1. authz / target validation（Gateway）
2. owner component existence/schema version
3. expected base ConfigGeneration一致
4. key existence
5. type validation
6. scalar range/enum validation
7. cross-field validation
8. mutability validation
9. impact classification
10. simulation changeならeffective Step scheduling
11. full change-set normalization / digest
12. atomic apply or schedule
13. ConfigGeneration +1
14. audit/history
15. normalized TOML atomic write-back

途中failureでpartial applyしない。

## 12. ConfigGeneration rules

- initial EffectiveConfig generation = 1。
- successful atomic runtime change setごとに+1。
- no-change requestはgenerationを増やさない。
- migration/default completion startup write-backは、新しいworld/runtime historyがまだ存在しない初期loadではgeneration 1の内容として扱う。
- existing persisted component Config continuationでmigrationを行う場合はmigration recordとresulting generationを保持する。
- wrap禁止。

## 13. Default completion ordering

Current schema default completionはfield path ASCII ascendingで実行する。

default間の計算依存を禁止する。default valueは他fieldのruntime valueから計算せずschema constantとする。

これによりhardware/environment差でdefaultが変わらない。

## 14. Environment/deployment inputs

次はMachiVerse Config semantic fieldとは別deployment inputとしてよい。

- Config file path
- bind/listen address
- TLS certificate/private-key secret reference injection
- process working directory
- OS service identity

ただしdeployment inputでConfig schema fieldをsilent overrideしない。

override機能を将来追加する場合はsource precedenceとEffectiveConfig digestへの反映を明示する。

## 15. Restore / replay

Saved world continuationではCore SIMULATION Config historyをauthorityとする。

startup local TOMLのSIMULATION valueがsaved world current valueと異なる場合:

- saved worldをlocal TOMLでsilent上書きしない。
- restore current effective value/historyを採用する。
- operatorに差分をdiagnostic表示する。
- current local fileへrestore valueを書き戻すか、explicit continuation profileを生成する。

OPERATIONAL/PRESENTATION valueはcurrent deployment Configを使用できる。

## 16. Migration policy v1

Current schema `1.0`のためmigration stepはまだ存在しない。

future minor migrationは:

```text
1.0 -> 1.1 -> ...
```

隣接version transformationを必須とする。

field rename時にold/new両方を同時accepted aliasとして無期限維持しない。

migration完了後はcurrent keyだけをcanonical TOMLへwrite-backする。

## 17. Error codes

```text
config.invalid
config.stale-generation
config.unknown-key
config.missing-required
config.type-mismatch
config.out-of-range
config.cross-constraint
config.restart-required
config.world-regeneration-required
config.schema-incompatible
config.migration-failed
config.write-back-failed
config.secret-value-forbidden
```

Simulation Config failureでState(S)を変更しない。

## 18. Acceptance criteria

- 4 component Config schemaを`1.0`として一意に識別できる。
- every listed field has exact type/default/range/impact/mutability。
- Gateway OIDC fields以外のrequired valueはschema constant defaultを持つ。
- missing default fieldを補完しcanonical TOMLへwrite-backできる。
- unknown keyをrejectできる。
- SIMULATION runtime changeをauthoritative effective Stepへscheduleできる。
- OPERATIONAL/PRESENTATION changeがworld diagnostic digestを変えない。
- worker-count 1/4/16でsame world resultを維持できる。
- domain cadence ConfigがD0<=D1<=D2<=D3 constraintを満たす。
- heartbeat timeout/interval、reconnect min/max、session lifetime cross-constraintを検証できる。
- secret materialをConfig/history/protocolへ露出しない。
- restore時にsaved simulation Config/historyをlocal fileでsilent overrideしない。

## 19. Remaining P4-03 work

P4-03 completion前に次を行う。

- P4-06 performance measurement assumptionとdomain cadence defaultsのcross-review
- P4-04 persistence parametersとのcross-review
- P4-07 observability/audit retention fieldsとのcross-review
- complete canonical TOML sampleを4 component分追加
- Config key count/completeness audit

blocker: なし。上記は後続Phaseとのcross-review itemであり、schema implementationは本書から開始可能。
