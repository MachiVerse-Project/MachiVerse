# Simulation Core 実装ロードマップ

ImplementationWorkId: `SIM-01..SIM-15`  
Base branch: `simulation`  
Upper roadmap: `/ROADMAP.md`

## 1. 実装baseline

Standard runtime profile:

```text
.NET 10 LTS
C# 14
```

Simulation Core は authoritative World State、SimulationStep、deterministic execution、Persistence、domain simulation、Core-owned Protocol semantics を所有する。

Gateway / View / Administration View の production DLL や内部型へ依存しない。

## 2. Milestone mapping

| Global milestone | Work package | Dependency |
|---|---|---|
| M1 | `SIM-01` | contract fixture と並列開始可 |
| M2 | `SIM-02`, `SIM-03`, `SIM-04` | `SIM-01` |
| M3 | `SIM-05`, `SIM-06` | Config/Persistence/WorldState |
| M4 | `SIM-07..SIM-12`, `SIM-15` | stable DomainRuntime / `SIM-06` |
| M5 | `SIM-13`, `SIM-14` | domain packages / common runtime |
| M6 | integration support | `INT-*`, `QA-*` から検証 |

## 3. Foundation

### SIM-01 — Core project scaffold / deterministic primitives

Scope:

- executable / test project
- checked integer / fixed-point
- stable ID / Hash / StableToken
- MV-DCBOR / DomainHash / deterministic random
- SameStep ordering primitive
- worker abstraction

DoD gate:

- deterministic golden vectorsを通過
- worker/thread schedulingをsemantic orderへ使用しない

## 4. Common runtime

### SIM-02 — Core Config coordinator

- `config.simulation-core/1.0`
- default completion / write-back
- ConfigGeneration / digest
- runtime safe change / effective Step

Dependency: `SIM-01`。

### SIM-03 — Persistence engine

- SQLite WAL/FULL durable profile
- history / dedup / scheduler / metadata
- Snapshot chunk / manifest
- migration / export / import

Dependency: `SIM-01`, `QA-01` fixtures。

### SIM-04 — WorldState / 97 partition registry

- WorldStateV1
- 8 domain / 97 authoritative partition
- DomainRecordEnvelope
- codec / index rebuild
- StateDiagnostic

Dependency: `SIM-01`。

## 5. Runtime spine

### SIM-05 — Operation lifecycle / scheduling / dedup

Lifecycle:

```text
UNSEEN
 -> ACCEPTED_DURABLE
 -> SCHEDULED_DURABLE
 -> TERMINAL_DURABLE
```

- immutable Operation digest
- deadline / grace / Pause semantics
- world-lifetime dedup tombstone
- scheduler persistence

Dependencies: `SIM-02`, `SIM-03`。

### SIM-06 — StepCoordinator / deterministic merge / transaction engine base

- external input freeze
- execution plan
- DomainRuntime context
- canonical intent merge / conflict
- StepCandidate
- invariant barrier
- durable finalize

Dependencies: `SIM-03`, `SIM-04`, `SIM-05`。

`SIM-06` 完了を domain implementation の主要開始gateとする。

## 6. Domain parallel implementation

### SIM-07 — Spatial / Environment

- SBO-SDF terrain
- frame / scope / containment
- atmosphere / hydrology / ocean / geology / ecology

### SIM-08 — Physical / Built

- presence / occupancy / built
- spatial index / collision / motion
- pathfinding
- item / material / worksite handoff

Fixture dependency: `SIM-07` spatial query contract。

### SIM-09 — Resident / Participation

- lifecycle / health / perception / belief / memory / psychology
- goal / skill / bounded GOAP
- Diver binding / control / absence / detail floor

Fixture dependency: `SIM-08` physical interface。

### SIM-10 — Society / Economy

- organization / employment / contract / property
- double-entry accounting
- market / production / logistics
- culture / reputation / claim

### SIM-11 — Governance / Security

- polity / institution / jurisdiction
- declarative law AST
- permission / tax / diplomacy
- incident / investigation / case / enforcement / military / border

### SIM-12 — Infrastructure / Information

- service / queue / dependency graph
- power / water / transport / communication
- information delivery / media / record / address
- outage / recovery

### SIM-15 — Core observability / telemetry

- structured log
- canonical metrics / spans
- StateDiagnostic export
- performance instrumentation

`SIM-07..SIM-12` は `SIM-04` + stable `SIM-06` API成立後、可能な範囲で並列実装する。

## 7. Cross-domain / protocol completion

### SIM-13 — Cross-domain transactions / detail transitions

- 17 CrossDomainTransactionKind
- participant / invariant assembly
- D0〜D3 promotion / demotion
- identity / stock / obligation / flow / provenance conservation
- materialization budget

Dependencies: `SIM-07..SIM-12`。

### SIM-14 — Core protocol boundary / publication projection

- Core↔Gateway gRPC bidirectional stream
- Gateway registration / heartbeat
- MasterGeneration
- Operation / Batch / status
- FULL / DELTA publication
- StateContinuityToken / resync

Dependencies: `SIM-03`, `SIM-05`, `SIM-06`。Domain projectionはincrementalに開発可能だが、final acceptanceは`SIM-13`後。

## 8. Simulation Core completion gate

Component-level completeには少なくとも次を要求する。

- 97 partition minimum implementation
- deterministic worker 1/4/8/16 equivalence
- persistence crash/recovery contract
- Operation dedup / replay
- 17 cross-domain transaction atomicity
- detail conservation
- Core protocol fixture / real Gateway integration compatibility
- required performance/observability instrumentation

Release完了は本componentだけでは判定せず、`INT-01..INT-03` と `QA-04` を通過すること。
