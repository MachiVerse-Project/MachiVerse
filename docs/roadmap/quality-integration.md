# QA / Integration 実装ロードマップ

ImplementationWorkId: `QA-01..QA-04`, `INT-01..INT-03`  
Integration base: component responsibility branches → `develop`  
Upper roadmap: `/ROADMAP.md`

## 1. 役割

本ロードマップは component 単体の完了では検出できない以下を横断検証する。

- schema / golden fixture consistency
- deterministic execution / replay
- crash / recovery / fuzz / security
- performance / soak
- single-Gateway end-to-end
- multi-Gateway failover / resync
- release acceptance

component の production implementation を QA 用 shared runtime DLL へ集約しない。

## 2. Milestone mapping

| Global milestone | Work package | Main dependency |
|---|---|---|
| M1 | `QA-01` | none |
| M5 | `QA-02`, `QA-03` | common runtime / parsers / auth |
| M6 | `INT-01`, `INT-02`, `QA-04`, `INT-03` | component flows |

## 3. Contract foundation

### QA-01 — Contract fixtures / schema golden vectors

Scope:

- stable token / ID / Hash vectors
- MV-DCBOR / DomainHash vectors
- protobuf fixtures
- Config standard example validation
- Persistence fixture generator
- TestCaseId registry

`QA-01` は M1 component scaffold と並列開始可能であり、各componentの最終foundation acceptanceの共通基準になる。

## 4. Determinism / replay

### QA-02 — Determinism / replay harness

Scope:

- worker count `1 / 4 / 8 / 16`
- process restart checkpoints
- Gateway / View / telemetry permutation
- same input / same Config / same Seed の Step-by-Step StateDiagnostic比較
- retry / arrival / thread scheduling perturbation

Dependency: `SIM-06` 以降。Domain mergeごとにcorpusを拡張する。

Non-negotiable:

- worker count差でauthoritative outcomeを変更しない
- Gateway数 / Master個体 / renderer backend / telemetry有無をworld randomnessへ混入させない

## 5. Crash / fuzz / security

### QA-03 — Crash / fuzz / security harness

Scope:

- Persistence crash matrix
- protobuf malformed corpus
- WebSocket boundary fuzz
- TOML / rule AST / Snapshot fuzz
- OIDC/session negative corpus
- audit redaction / tamper detection

Dependencies: `SIM-03`, `GW-04`, protocol/parser implementations。

Gate:

- malformed external inputでsilent downgradeしない
- credential/token/session secretをnormal log / WorldStateへ混入させない
- crash後にdurable contractを破らない

## 6. Single Gateway E2E

### INT-01 — Single Gateway end-to-end

Scenario:

```text
Simulation Core
  ↕
Gateway x1
  ↕
General View + Administration View
```

Scope:

- login / session
- confirmed state publication
- Diver binding / action
- Operation result
- Config read / change
- save / restart / recovery
- basic audit / observability

Dependencies:

- `SIM-14`
- `GW-06`
- `VIEW-05`
- `ADMIN-03`
- component minimum viable domain flow

Exit gate:

- external user actionがGatewayを経由してCoreへ到達し、confirmed result/stateがViewへ戻る
- restart後もidentity / persistence / continuity semanticsを維持する

## 7. Multi-Gateway reliability

### INT-02 — Multi-Gateway failover / resync

Scenario:

```text
Simulation Core + Gateway x4 + View churn
```

Scope:

- Master selection / reassignment
- stale MasterGeneration
- Master failover / live transition
- custody acceptance unknown convergence
- duplicate retry
- Gateway reconnect / resync
- slow consumer / publication coalesce
- client reconnect churn

Dependencies: `GW-03`, `GW-06`, `SIM-14`, `QA-02`。

Exit gate:

- failover/retryによりOperation loss / double applyを起こさない
- stale generation outputをcurrent authorityとして受理しない
- resync中のinconsistent stateをnormal confirmed stateとして公開しない

## 8. Performance / soak

### QA-04 — Performance / soak harness

Scope:

- `perf.reference.v1`
- Core Step performance
- persistence / snapshot profile
- publication bandwidth / latency
- queue / backpressure
- memory budget
- General View renderer presentation budget
- 24h soak orchestration

Dependencies: `SIM-13`, `SIM-14`, `GW-06`, `QA-02`。

Reference design targetsは `phase4-performance-*` を正本とし、本ロードマップで数値を再定義しない。

性能不足をdeterminism / durability / world semanticsのshortcutで解消しない。

## 9. Release acceptance

### INT-03 — Release acceptance

Scope:

- P4-08 all required suites
- deterministic/replay matrix
- crash/recovery/fuzz/security
- `perf.reference.v1`
- persistence stress
- publication stress
- 24h soak
- component independent contract tests
- `ReleaseAcceptanceRecordV1`

Dependencies: all standard implementation work packages, `INT-01`, `INT-02`, `QA-04`。

`INT-03` が全体ロードマップの release completion gate である。

## 10. Integration原則

- 各 component feature は自身の responsibility branchへ先に統合する
- cross-component testのために責任分野を1 feature branchへ混在させない
- `develop` 統合時は protocol/schema fixtureとproduction implementationの両方でcontract mismatchを検出する
- component implementation未完成をmock/fixtureで先行検証することは許容するが、release acceptanceをmockだけで完了扱いにしない
