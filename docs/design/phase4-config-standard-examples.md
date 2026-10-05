# 詳細設計 Phase 4: Standard Config Examples

Status: Complete / P4-03 supporting artifact  
Tracking: Issue #16  
Parent: `phase4-config-specification.md`  
Performance addendum: `phase4-performance-budget.md`

## 1. 目的

P4-03 Config schema `1.0`の全standard componentについて、default completion後に生成可能なcanonical TOML例を示す。

本書のexampleはschema field名・default value確認用であり、deployment固有URI/secret referenceは環境ごとに置換する。

Canonical writerはtable/keyをfield path ASCII orderへnormalizeしてよい。人間向けtable配置そのものはConfigDigestのauthorityではない。

## 2. Simulation Core

```toml
[meta]
format = "machiverse-config"
schema_version = "1.0"
component = "simulation-core"

[simulation.step-rate]
numerator = 30
denominator = 1

[runtime]
worker-count = 4
domain-timeout-ms = 30000

[scheduling]
min-lead-steps = 2
default-deadline-window-steps = 90
grace-steps = 15
late-policy = "defer-within-grace"

[detail]
promotion-hysteresis-steps = 30
demotion-quiet-steps = 300
minimum-residence-steps = 300
bound-resident-floor = "d0-entity"
active-transaction-floor = "d0-entity"
promotion-max-regions-per-step = 4
promotion-max-records-per-step = 20000
demotion-max-regions-per-step = 8
demotion-max-records-per-step = 50000

[detail.domain.spatial]
d0-cadence-steps = 1
d1-cadence-steps = 10
d2-cadence-steps = 60
d3-cadence-steps = 600

[detail.domain.environment]
d0-cadence-steps = 1
d1-cadence-steps = 5
d2-cadence-steps = 30
d3-cadence-steps = 300

[detail.domain.physical_built]
d0-cadence-steps = 1
d1-cadence-steps = 5
d2-cadence-steps = 30
d3-cadence-steps = 300

[detail.domain.participation]
d0-cadence-steps = 1
d1-cadence-steps = 1
d2-cadence-steps = 5
d3-cadence-steps = 30

[detail.domain.resident]
d0-cadence-steps = 1
d1-cadence-steps = 5
d2-cadence-steps = 30
d3-cadence-steps = 300

[detail.domain.society_economy]
d0-cadence-steps = 5
d1-cadence-steps = 30
d2-cadence-steps = 300
d3-cadence-steps = 1800

[detail.domain.governance_security]
d0-cadence-steps = 10
d1-cadence-steps = 60
d2-cadence-steps = 600
d3-cadence-steps = 3600

[detail.domain.infrastructure_information]
d0-cadence-steps = 1
d1-cadence-steps = 5
d2-cadence-steps = 30
d3-cadence-steps = 300

[persistence]
snapshot-interval-steps = 18000
snapshot-retain-count = 12
snapshot-compression = "zstd"
snapshot-zstd-level = 3
recovery-verify-state-digest = true

[publication]
delta-enabled = true
full-interval-steps = 900
max-chunk-bytes = 1048576
queue-capacity = 64

[master]
heartbeat-interval-ms = 1000
heartbeat-timeout-ms = 5000
min-ready-heartbeats = 2

[queue]
protocol-ingress-capacity = 8192
accepted-operation-admission-limit = 65536
persistence-capacity = 8192

[observability]
log-level = "info"
metric-export-interval-ms = 1000
state-digest-every-steps = 1
```

### 2.1 Alpha 1.1 standard runtime profile override

上のTOMLはConfig schema `1.0` のdefault completion例であり、StepRate default `30/1` は互換性のため変更しない。Alpha 1.1 standard runtime / release profileでは、同じschemaに対して次を明示設定する。

```toml
[simulation.step-rate]
numerator = 10
denominator = 1
```

これはschema defaultの変更ではない。runtimeで切り替える場合もSimulation-affecting Config changeとしてexplicit effective StepとConfig historyへ記録する。

## 3. Gateway

```toml
[meta]
format = "machiverse-config"
schema_version = "1.0"
component = "gateway"

[network]
connect-timeout-ms = 10000
reconnect-initial-ms = 250
reconnect-max-ms = 10000
reconnect-multiplier-permille = 2000
reconnect-jitter-permille = 200

[peer]
heartbeat-interval-ms = 1000
heartbeat-timeout-ms = 5000

[aggregation]
window-ms = 10
max-operations = 512
max-uncompressed-bytes = 4194304

[queue]
connection-ingress-capacity = 4096
local-operation-capacity = 16384
custody-admission-limit = 65536
peer-batch-capacity = 2048
publication-capacity = 64
result-capacity = 8192

[publication]
buffer-ms = 1000
max-client-backlog = 8

[cache]
max-mebibytes = 1024
max-confirmed-publications = 120

[result]
rich-retention-seconds = 86400

[custody]
local-terminal-retention-seconds = 86400

[auth.oidc]
issuer = "https://idp.example.invalid/"
client-id = "machiverse-gateway"
client-secret-ref = "secret://gateway/oidc-client-secret"
redirect-base-uri = "https://machiverse.example.invalid/"

[auth]
allowed-origins = ["https://machiverse.example.invalid"]
login-transaction-lifetime-seconds = 600
session-idle-lifetime-seconds = 3600
session-absolute-lifetime-seconds = 43200
max-active-sessions-per-account = 16

[observability]
log-level = "info"
metric-export-interval-ms = 1000
log-retention-days = 14
```

`client-secret-ref`はsecret materialそのものではない。

## 4. General View

```toml
[meta]
format = "machiverse-config"
schema_version = "1.0"
component = "general-view"

[render]
target-fps = 60
max-pixel-ratio = 2.0
lod-bias = 0
max-visible-objects = 100000

[interpolation]
delay-ms = 100

[prediction]
enabled = true
max-horizon-ms = 150

[reconcile]
soft-duration-ms = 120
max-soft-duration-ms = 500

[cache]
max-mebibytes = 256

[asset]
max-concurrency = 8

[protocol]
receive-queue-capacity = 256

[operation]
pending-capacity = 1024

[network]
reconnect-initial-ms = 250
reconnect-max-ms = 10000
reconnect-multiplier-permille = 2000

[ui]
locale = "auto"
reduced-motion-default = false
show-prediction-indicator = true

[observability]
client-log-level = "warn"
```

General View schemaはSIMULATION impact fieldを持たない。

## 5. Admin View

```toml
[meta]
format = "machiverse-config"
schema_version = "1.0"
component = "admin-view"

[dashboard]
refresh-ms = 1000

[metrics]
local-history-samples = 3600
max-series = 200

[log]
default-page-size = 200
local-window-records = 5000

[audit]
default-page-size = 200

[request]
presentation-timeout-ms = 30000

[confirmation]
ux-timeout-seconds = 120

[cache]
max-mebibytes = 128

[network]
reconnect-initial-ms = 250
reconnect-max-ms = 10000

[observability]
client-log-level = "warn"
```

Admin View schemaはSIMULATION impact fieldを持たず、target component Config/audit retentionを上書きしない。

## 6. P4-06追加field

P4-06でstandard Core schema 1.0へ次の4 fieldを追加した。Phase4 completion前のschema未公開状態であるためmigrationは不要。

| key | type | default | range | impact | mutability |
|---|---|---:|---:|---|---|
| `detail.promotion-max-regions-per-step` | uint16 | 4 | 1..1024 | SIMULATION | RUNTIME_SAFE |
| `detail.promotion-max-records-per-step` | uint32 | 20000 | 100..10000000 | SIMULATION | RUNTIME_SAFE |
| `detail.demotion-max-regions-per-step` | uint16 | 8 | 1..2048 | SIMULATION | RUNTIME_SAFE |
| `detail.demotion-max-records-per-step` | uint32 | 50000 | 100..20000000 | SIMULATION | RUNTIME_SAFE |

これらのeffective changeはCoreが確定したSimulationStepからatomicに適用する。

## 7. Canonical validation

Examplesは次を満たす。

- duplicate keyなし
- unknown standard keyなし
- required metaあり
- cross-field cadence `D0<=D1<=D2<=D3`
- heartbeat timeout >= 3 * interval
- reconnect max >= initial
- General View soft reconcile <= max soft reconcile
- Gateway absolute session lifetime >= idle lifetime
- P4-02 wire hard limitとpublication/batch size整合

## 8. Example security rule

本書にcredential/password/private key/token実値を記載しない。

`example.invalid`および`secret://...`はdocumentation用例示でありproduction endpoint/secret locationではない。