# 詳細設計 Phase 4: Deterministic Performance Benchmark Profile

Status: Complete / P4-06 benchmark definition  
Tracking: Issue #16  
Parent: `phase4-performance-budget.md`

## 1. 目的

P4-06 performance targetを、implementation/team/hardwareごとに異なるad-hoc worldで測定しないよう、reference world seed、entity count、detail distribution、Operation load、measurement interval、pass/fail aggregationを固定する。

本書はbenchmark結果を規定せず、再現可能な負荷入力を規定する。

## 2. Benchmark Profile ID

```text
BenchmarkProfileId = perf.reference.v1
```

WorldSeed:

```text
000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f
```

Benchmark WorldIdはprofile/seedからdeterministicにderiveし、実ユーザーworldと混同しない。

## 3. Config baseline

`phase4-config-standard-examples.md` Simulation Core defaultを使用する。

差分:

```text
runtime.worker-count = test parameter {1,4,8,16}
observability.log-level = warn
```

SIMULATION Configはworker count run間で完全一致する。

## 4. Initial world population

At measurement start:

| state class | count |
|---|---:|
| Resident persistent identity | 1,000,000 |
| Resident D0 | 100,000 |
| Resident D1 | 300,000 |
| Resident D2 | 400,000 |
| Resident D3 | 200,000 |
| Physical D0 presence | 500,000 |
| Environment D0 cells/cohorts | 1,000,000 |
| Environment D1 aggregate | 250,000 |
| Society/Governance active records | 2,000,000 |
| Infrastructure active records | 500,000 |
| hot TerrainBrickV1 | 500,000 |
| active CrossDomainTransaction | 10,000 |

Countはrecord creation ordinalからdeterministic identityをderiveする。

## 5. Spatial distribution

Reference worldは64 x 64 regional tilesへ対象を分散する。

```text
tile_index = DomainHash(subject_id) low 12 bits mod 4096
```

各tile内positionはaddressable random context:

```text
perf.reference.position.v1
```

からderiveし、iteration orderを使わない。

D0 clusterの25%を4つのdense urban/interaction regionへ集中し、remaining 75%をuniform regional distributionにする。

## 6. Resident activity mix

Per Resident activation:

| behavior class | ratio |
|---|---:|
| idle/routine | 35% |
| local movement | 25% |
| social communication | 10% |
| market/consumption | 10% |
| employment/work | 10% |
| infrastructure service use | 5% |
| health/medical | 3% |
| governance/security interaction | 2% |

Selectionはresident id + activation stepからaddressable randomで固定する。

## 7. External Operation load

Steady benchmark injection:

```text
5,000 world-affecting Operation / Step average
```

Distribution:

| Operation family | share |
|---|---:|
| participation/control/resident action | 35% |
| physical item/movement/work | 20% |
| society market/payment/contract | 20% |
| infrastructure service/delivery | 15% |
| governance/security | 5% |
| environment/spatial/admin synthetic | 5% |

Burst test every 900 Step:

```text
50,000 Operation injected for same scheduling window
```

OperationId/payload are pre-derived from profile id + injection step + family + ordinal。

## 8. Cross-domain transaction mix

Steady active transaction target 10,000, with per 300 Step creation mix:

```text
market-sale-delivery 35%
employment-work 20%
food-consumption 10%
information-transmission 10%
medical-service 5%
construction 5%
mining-excavation 3%
crime-justice 3%
border-crossing 3%
infrastructure-outage-cascade 2%
natural-disaster-cascade 2%
other registered transactions 2%
```

## 9. Environment load

- 10% D0 environment cells receive precipitation each environment update.
- 1% cells participate in surface flow threshold crossing.
- 0.1% active hazard intensity change per 30 Step.
- ecosystem cohort update follows configured cadence.
- contaminant transport active in 2% D0 cells.

Selection is cell id / step addressable random.

## 10. Physical collision load

D0 physical presence distribution targets:

```text
80% broad-phase zero contact
15% 1..4 candidate contacts
4% 5..16 candidate contacts
1% dense 17..64 candidate contacts
```

Dense region includes mixed sphere/capsule/box/convex/static mesh/SDF contact.

## 11. Market load

Reference market:

```text
100 market scopes
10,000 active orders / scope average
```

At market cadence, 5% orders replaced/added/cancelled.

Price/quantity values are integer and generated from stable market/order context.

## 12. Infrastructure load

- 20,000 network nodes across transport/power/water/communication.
- 100,000 stable edges.
- 250,000 queued service requests.
- one deterministic cascading outage scenario every 9000 Step.

Outage source node fixed by profile ordinal, not wall clock.

## 13. Detail transition load

Every 300 Step:

```text
promotion requests: 6 regions, 30,000 candidate records
demotion requests: 10 regions, 80,000 candidate records
```

P4-06 semantic budgets therefore force deterministic defer in reference scenario and exercise queue order.

## 14. Warm-up / measurement

Per worker-count run:

```text
initialization: excluded
warm-up: 9,000 Steps
measurement: 18,000 Steps
cooldown/snapshot drain: excluded from Step distribution
```

Measurement interval intentionally equals one default Snapshot interval so a normal Snapshot trigger occurs during the run。

## 15. Repetition

Each worker count:

```text
3 independent process runs
same seed/config/history
```

Performance result uses median of 3 run-level p95 values。

Determinism requires all run final StateDiagnostic digest exactly equal。

## 16. Performance pass criteria

Reference node:

- median run p95 Step <= 33.333 ms at worker count 16.
- p99 <= 50 ms.
- 60-second mean <= 30 ms.
- Core steady memory <= 22 GiB target and never >28 GiB guard.
- SQLite COMMIT p95 <=4ms, p99<=8ms.
- snapshot COW barrier p95 <=5ms.
- no accepted Operation loss.
- no hidden solver iteration reduction.

Worker 1/4/8 are scaling/determinism data and need not each achieve 30Hz, unless separately claimed by product profile。

## 17. Determinism pass criteria

All worker counts 1/4/8/16:

```text
final StateDiagnostic.state_digest identical
transition committed digest identical per Step
Operation terminal semantic result identical
Config generation/history identical
promotion deferral order identical
```

Wall-clock metric/trace/log sequence need not match。

## 18. Persistence stress subprofile

```text
perf.persistence.v1
```

- generate 16 GiB compressed-class Snapshot fixture.
- generate 10 minutes equivalent history tail.
- validate chunk/digest/load/replay.
- inject process crash at each P4-04 commit stage using deterministic crash-point ordinal.

Pass: no durable accepted/finalized fact lost and no uncommitted candidate published。

## 19. Publication subprofile

```text
perf.publication.v1
```

- 1 Gateway.
- 100 General View subscribers.
- class mix: 60 Spectator / 35 Diver / 4 Moderator / 1 General View Administrator.
- View consumption rates include 10 intentionally slow consumers.

Pass:

- Core->Gateway average/burst budget target.
- per-client bandwidth class target.
- slow consumers do not block Core custody/result.
- continuity remains valid after coalescing/resync.

## 20. Benchmark report schema

```text
PerformanceBenchmarkReportV1 {
  benchmark_profile_id,
  build_version,
  runtime_version,
  hardware_profile_digest,
  config_digest,
  worker_count,
  run_ordinal,
  step_count,
  step_p50_ms,
  step_p95_ms,
  step_p99_ms,
  domain_cpu_summary,
  max_memory_bytes,
  persistence_commit_p95_ms,
  snapshot_summary,
  publication_summary,
  final_state_digest,
  failure_codes
}
```

Hardware descriptive data is diagnostic only and not world input。

## 21. Acceptance

P4-08は本profileをperformance acceptance suiteのnormative inputとして参照する。

Profile変更でload semanticsを変える場合、`perf.reference.v2`等new profile idを作り、過去benchmark結果と混同しない。