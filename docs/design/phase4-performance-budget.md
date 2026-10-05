# 詳細設計 Phase 4: Performance / Memory / Cadence / Detail Budget

Status: In Progress / P4-06  
Tracking: Issue #16  
Parent: `phase4-implementation-ready-design.md`  
Predecessors: P4-03 Config, P4-04 Persistence, P4-05 Algorithm

## 1. 目的

MachiVerse standard profileが30Hz authoritative SimulationStepを品質目標としつつ、決定論・durability・detail semanticsを性能都合で破壊しないよう、wall-clock target、CPU/memory、persistence、publication、queue、promotion/demotionの実装予算を固定する。

本書のbudgetは二種類を区別する。

- **semantic limit**: 超過時のworld behaviorが定義されるためSIMULATION contractの一部。
- **operational target**: hardware sizing/SLO。未達でもworld semanticsを変更せずlag/backpressure/degraded publicationで処理する。

wall-clock pressureだけを理由にsolver iteration、random、same-Step order、detail levelを変更しない。

## 2. Reference standard node

Phase 4 acceptance benchmarkのreference nodeを次とする。

```text
CPU: 8 physical cores / 16 logical threads or better
RAM: 32 GiB
Storage: local NVMe SSD
  sustained sequential write >= 500 MiB/s
  durable fsync p95 <= 5 ms under benchmark load
Network:
  Core<->Gateway reference: >= 1 Gbit/s full duplex LAN/local host equivalent
OS/Runtime:
  supported 64-bit .NET runtime
```

`runtime.worker-count` standard defaultは4だが、1/4/8/16 workerでdeterministic result一致を要求する。1/4/8/16はPhase 4/Alpha 1.1の標準受入matrixであり、Core runtime capabilityの上限ではない。production CPU work executorは16を超えるworker budgetを同一実装で利用可能とし、Domain数を並列度上限にしてはならない。

より多コアのhostでは、independent work itemをcanonical unitへ分割し、parallel compute後にcanonical reduce/commit barrierへ戻すことでCPU parallelismを拡張する。worker数増加によってSameStepOrder、authority commit順、StateDiagnostic、Operation terminal semantic resultを変更してはならない。

Reference node未満でも起動禁止とは限らない。performance acceptance profileを満たさないdeploymentとしてwarningを出せる。

## 3. Step wall-clock target

Standard StepRate:

```text
30/1 steps/sec
wall period = 33.333... ms
```

Operational target:

| Metric | Target |
|---|---:|
| Step finalized wall time p50 | <= 20 ms |
| Step finalized wall time p95 | <= 33.333 ms |
| Step finalized wall time p99 | <= 50 ms |
| continuous 60 s mean | <= 30 ms |
| startup/recovery benchmark excluded | yes |

Step skipは禁止。

Overrun時:

```text
elapsed > target period
 -> authoritative Stepは1だけ進む
 -> wall-clock lagをmetric化
 -> next Stepを通常semanticで計算
```

wall clockへ追いつくため複数Stepを1つに統合したり、solver iterationを削減したりしない。

## 4. Step critical-path budget

Reference benchmarkでp95 critical pathを次の目標へ割り当てる。

| phase | p95 wall target |
|---|---:|
| input freeze / execution-plan | 1.0 ms |
| domain calculation critical path | 19.0 ms |
| deterministic merge / conflict | 3.0 ms |
| cross-domain transaction / invariant | 2.0 ms |
| candidate state digest/build | 2.0 ms |
| persistence durable commit | 4.0 ms |
| finalize / publication enqueue | 1.5 ms |
| reserve | 0.8 ms |
| total target | 33.3 ms |

Domain calculationはparallel CPU workを含むためdomain別CPU時間の和とwall timeは一致しない。

## 5. Domain CPU budget

1 Stepあたりreference nodeでの**aggregate CPU time target**:

| domain | CPU-ms/Step target | critical-path wall target |
|---|---:|---:|
| spatial | 12 | 3 ms |
| environment | 32 | 7 ms |
| physical_built | 48 | 9 ms |
| participation | 4 | 1 ms |
| resident | 48 | 9 ms |
| society_economy | 16 | 4 ms |
| governance_security | 8 | 2 ms |
| infrastructure_information | 24 | 5 ms |
| cross-domain/merge support | 20 | 4 ms |
| total aggregate target | 212 CPU-ms | parallel |

これはoperational targetであり、budget超過を理由にworld resultを変更しない。

## 6. Algorithm work budget

P4-05のdeterministic algorithm boundはsemantic constantとして維持する。

| algorithm | standard bound |
|---|---:|
| GJK | 32 iterations |
| EPA | 32 iterations |
| terrain conservative advancement | 16 iterations |
| contact sequential impulse | 12 iterations |
| groundwater Jacobi | 16 iterations |
| power Jacobi | 32 iterations |
| water-service Jacobi | 32 iterations |
| Resident GOAP expanded nodes | 256 / planning activation |

performance pressureでこれらをwall-clock adaptiveに下げない。

P4-06 benchmarkでtargetを達成できない場合、data layout/index/culling/parallelismを先に最適化し、semantic bound変更はAlgorithmVersion reviewを要求する。

## 7. Memory top-level budget

Reference 32 GiB nodeでSimulation Core steady-state target:

| category | target | hard operational guard |
|---|---:|---:|
| authoritative current state | <= 10 GiB | 14 GiB |
| derived secondary/spatial indexes | <= 5 GiB | 7 GiB |
| Step candidate / merge workspace | <= 2 GiB | 4 GiB |
| snapshot COW/frozen view | <= 4 GiB | 6 GiB |
| persistence/cache/buffers | <= 2 GiB | 3 GiB |
| runtime/GC/other reserve | <= 3 GiB | 4 GiB |
| Core total steady target | <= 22 GiB | 28 GiB |

28 GiB guard到達時、新規nonessential cache allocationを停止し、derived rebuildable cache evictionを行う。

authoritative stateをmemory pressureだけで破棄しない。

## 8. Record memory target

Implementation acceptance時のserialized canonical payload + envelope + primary referencesの目標値。

| record class | p50 target | p95 target |
|---|---:|---:|
| Resident D0 total across resident-owned records / resident | <= 12 KiB | <= 24 KiB |
| Physical presence+occupancy+condition / active subject | <= 2 KiB | <= 4 KiB |
| Society/Governance relation record | <= 768 B | <= 2 KiB |
| Infrastructure node/service/queue record | <= 512 B | <= 1.5 KiB |
| Environment scalar cell/cohort record | <= 384 B | <= 1 KiB |
| Terrain SDF brick raw payload | <= 4.5 KiB | <= 5 KiB |

These are layout targets, not semantic truncation limits. Variable history/evidence/list data that exceeds p95 must use normalized child records/content-addressed immutable blobs rather than unbounded inline arrays where possible。

## 9. Standard active-world capacity target

Reference benchmark scenario:

```text
D0 residents:                  100,000
D0 physical presences:        500,000
D0 environment cells/cohorts: 1,000,000
resident persistent identities total: 1,000,000+
active service/queue records: 500,000
active society/governance records: 2,000,000
terrain SDF bricks loaded/hot: 500,000
```

Persistent identity count may exceed D0 update count。detail降格によりidentityを削除しない。

上記scenarioはinitial capacity targetでありworld schema hard maximumではない。

## 10. Detail cadence cross-review

P4-03 default cadenceをperformance baselineとして承認する。

| domain | D0 | D1 | D2 | D3 |
|---|---:|---:|---:|---:|
| spatial | 1 | 10 | 60 | 600 |
| environment | 1 | 5 | 30 | 300 |
| physical_built | 1 | 5 | 30 | 300 |
| participation | 1 | 1 | 5 | 30 |
| resident | 1 | 5 | 30 | 300 |
| society_economy | 5 | 30 | 300 | 1800 |
| governance_security | 10 | 60 | 600 | 3600 |
| infrastructure_information | 1 | 5 | 30 | 300 |

required event/Operation/same-Step dependency処理はcadence待ちにしない。

Cadenceを変える場合はSIMULATION Config historyへ記録する。

## 11. Promotion / demotion semantic budget

Detail materializationのsingle-Step explosionを決定論的にboundedにするため、P4-03 Configへ次を追加することをP4-06で要求する。

```text
detail.promotion-max-regions-per-step       uint16 default 4 range 1..1024
detail.promotion-max-records-per-step       uint32 default 20000 range 100..10000000
detail.demotion-max-regions-per-step        uint16 default 8 range 1..2048
detail.demotion-max-records-per-step        uint32 default 50000 range 100..20000000
```

classification:

```text
impact = SIMULATION
mutability = RUNTIME_SAFE
```

Promotion queue canonical key:

```text
(required_effective_step,
 semantic_priority,
 detail_region_id,
 domain_rank,
 trigger_id)
```

Budget不足でrequired promotionが当該Stepに完了できない場合:

- active world-affecting interactionをfake low-detail resultで処理しない。
- request/interactionを`detail.materialization-pending`としてdeterministicにdeferする。
- Diver-bound residentの既存D0 floorはdemoteしない。

wall-clock lagをpromotion trigger/priorityへ使用しない。

## 12. Detail hysteresis review

P4-03 defaultsを維持する。

```text
promotion hysteresis: 30 steps
quiet before demotion: 300 steps
minimum detail residence: 300 steps
bound resident floor: D0
active transaction floor: D0
```

30Hz標準時のhuman-readable換算は約1秒/10秒/10秒だが、authorityはStep数である。

## 13. Candidate / merge workspace budget

Per Step target:

| item | target |
|---|---:|
| MutationIntent count | <= 2,000,000 |
| DomainEvent transient count | <= 2,000,000 |
| CrossDomainTransaction candidates | <= 250,000 |
| changed partition records | <= 1,000,000 |
| candidate workspace memory | <= 2 GiB target |

これらはreference benchmark target。

Semantic hard guardを越えるimplementation conditionではsilent dropせずStep abort + diagnostic、またはConfig-defined admission/backpressureで次Stepへ送る。already accepted/effective Step inputを勝手に後送しない。

## 14. Core queue budget cross-review

P4-03 defaults:

```text
protocol ingress capacity                 8,192
accepted operation admission limit      65,536
persistence queue capacity               8,192
```

accepted-operation admission limitは「durable accepted recordのdrop limit」ではなくnew acceptance backpressure threshold。

Target occupancy:

- normal p95 <= 25%
- sustained > 75% for 10 s: warning
- >= 90%: reject/backpressure new eligible admission where contract permits

world-authoritative accepted Operationは保持する。

## 15. Gateway queue / memory budget

Reference Gateway process:

```text
RAM target: <= 4 GiB
confirmed cache default: 1 GiB
publication queue: 64 logical publications
local operation queue: 16,384
custody admission: 65,536
peer batch queue: 2,048
result queue: 8,192
```

Per View publication backlog default 8。

slow ViewによってCore custody/result pathをblockしない。

## 16. Publication bandwidth budget

### Core -> Gateway

Per Gateway standard target:

| Metric | Target |
|---|---:|
| steady compressed delta | <= 20 MiB/s average |
| p95 1-second burst | <= 80 MiB/s |
| full snapshot publication | chunked <= 1 MiB/envelope |
| full publication interval default | 900 steps |

### Gateway -> General View

Per active client target:

| class | average | p95 burst |
|---|---:|---:|
| Spectator regional view | <= 1 MiB/s | <= 4 MiB/s |
| Diver local D0/D1 view | <= 2 MiB/s | <= 8 MiB/s |
| Moderator/Admin broad view | <= 4 MiB/s | <= 16 MiB/s |

Role/interest filterで非許可stateを送らない。bandwidth pressure時はconfirmed intermediate publicationをcoalesceできるが、continuity/base dependencyを破壊するdeltaをskipしない。

## 17. Publication latency target

Core commit -> Gateway confirmed cache:

```text
p95 <= 100 ms LAN/local reference
```

Gateway configured buffer default 1000 msを含むView-visible confirmed state latency:

```text
p95 <= 1.25 s
```

Diver UXはlocal predictionを利用できるがpredictionをauthorityにしない。

## 18. Persistence throughput budget

### History

Standard workload target:

```text
compressed logical history growth average <= 512 MiB/hour
p95 one-minute rate <= 1 GiB/hour equivalent
```

full retention v1.0のためcapacity planning reference:

```text
~12 GiB/day average target
~360 GiB/30 days average target
```

これはstorage SLOであり、rate超過を理由にhistory factを削除しない。

### SQLite durable transaction

```text
p95 COMMIT <= 4 ms
p99 COMMIT <= 8 ms
```

reference NVMe benchmark target。

## 19. Snapshot budget

P4-03:

```text
snapshot interval = 18,000 steps
```

30Hzではlogical 10分だがStep authorityでscheduleする。

Snapshot targets:

| Metric | Target |
|---|---:|
| frozen/COW barrier | <= 5 ms p95 |
| background snapshot duration | <= 120 s p95 |
| logical read throughput | >= 250 MiB/s |
| compressed write throughput | >= 100 MiB/s |
| COW extra memory | <= 4 GiB target / 6 GiB guard |

COW guard超過が予測される場合、P4-04 stop-the-world fallbackをStep boundaryで使用できる。

Snapshot I/O lagをworld Step orderへ使用しない。

## 20. Snapshot size / retention planning

Reference benchmark target:

```text
compressed Snapshot <= 16 GiB p95
retained snapshots = 12
```

Worst reference retained Snapshot capacity planning:

```text
>= 192 GiB + history + migration/export reserve
```

Production recommended persistence volume initial sizing:

```text
>= 1 TiB for reference-world long-running development profile
```

storage exhaustion前にoperator warning/backpressureを行うが、committed historyをsilent deleteしない。

## 21. Recovery performance target

Reference state:

```text
latest snapshot <= 16 GiB compressed
history tail <= 10 minutes typical
```

Targets:

| Metric | Target |
|---|---:|
| manifest/index validation | <= 10 s |
| snapshot load/decompress | <= 120 s |
| history replay throughput | >= 5,000 Steps/s when domain replay can use recorded transition/checkpoint optimization; otherwise measured deterministic replay profile |
| normal recovery to READY typical | <= 5 min |

Recovery speedはcorrectnessより下位。target未達でもhistory skip/old snapshot rollbackを行わない。

## 22. GC / allocation target

Managed runtime standard implementation target:

```text
steady-state allocation <= 256 MiB/s Core
Gen2/LOH stop-the-world pause p99 <= 20 ms
```

Authoritative record churnはcopy-on-write chunk/arena/pool等で抑制してよい。

object poolingのreuse orderをidentity/order/randomへ使用しない。

## 23. Backpressure order

Performance pressure時の優先順位:

1. authoritative state integrity
2. persistence durability
3. accepted Operation retention
4. Step deterministic execution
5. terminal result delivery
6. confirmed continuity
7. state publication freshness
8. diagnostics detail
9. derived cache completeness
10. presentation quality

上位を守るため下位をcoalesce/evict/degradeできる。

## 24. Allowed operational degradation

World semanticsを変えず許可:

- derived cache eviction/rebuild
- publication coalescing
- View render LOD低下
- optional diagnostic sampling低下
- snapshot background concurrency低下
- new external admission backpressure
- non-authoritative result detail retention短縮（minimum contract内）

許可しない:

- Step skip
- solver iteration削減
- random sample省略によるworld result変更
- wall-clock-triggered authoritative detail demotion
- accepted Operation drop
- history commit省略
- invariant validation省略
- different conflict resolverへのfallback

## 25. Lag policy

Core lag metric:

```text
lag_ms = max(0, expected_wall_progress - finalized_step_wall_projection)
```

lagはdiagnosticだけでworld timeではない。

Operational response:

| condition | action |
|---|---|
| p95 Step >33.3 ms 10 s | warning + profiler sample |
| lag >250 ms 10 s | publication coalesce強化、new admission soft backpressure |
| lag >1000 ms | new world-affecting admission temporary reject where protocol permits、existing accepted保持 |
| lag >10000 ms | operator critical alert; no semantic degradation |

## 26. Performance observability requirements

P4-07へ次のmetricを引き渡す。

```text
core.step.duration_ms
core.step.lag_ms
core.domain.cpu_ms
core.domain.wall_ms
core.intent.count
core.event.count
core.transaction.count
core.candidate.changed_records
core.memory.authoritative_bytes
core.memory.index_bytes
core.memory.candidate_bytes
core.gc.pause_ms
core.persistence.commit_ms
core.persistence.history_bytes
core.snapshot.duration_ms
core.snapshot.size_bytes
core.publication.bytes
core.publication.queue_depth
core.detail.promotion_records
core.detail.demotion_records
gateway.publication.client_bytes
gateway.queue.depth
```

label cardinalityにentity/operation idを使用しない。

## 27. P4-03 cross-review result

現在のP4-03 defaultのうち次を承認する。

- worker-count 4
- 30Hz standard StepRate
- D0〜D3 domain cadence table
- promotion/demotion hysteresis values
- snapshot interval 18000 steps
- snapshot retain 12
- zstd level 3
- publication full interval 900 steps
- Core/Gateway queue defaults
- Gateway publication buffer 1000 ms

追加必須Config 4件を11節で定義した。P4-03 completion前にConfig registry/sampleへ反映する。

## 28. P4-04 cross-review result

- SQLite WAL/FULL durable transaction targetと矛盾なし。
- 32 MiB target/64 MiB max snapshot chunkは100 MiB/s write targetに適合する。
- full history retentionはperformanceではなくstorage capacityで吸収する。
- snapshot COW 6 GiB guard超過時はStep-boundary fallbackを使用する。

P4-04 semantic変更不要。

## 29. P4-05 cross-review result

Algorithm semantic constantsをperformance pressureで変更しない方針を承認する。

Reference target未達時の改善順:

1. data layout
2. derived index
3. work partition
4. allocation/cache
5. SIMD等bit-identical optimization
6. deterministic parallelization
7. cadence/detail Configのexplicit simulation change
8. AlgorithmVersion改訂

## 30. Acceptance criteria

P4-06 completionにはreference benchmark harnessで少なくとも次を確認する。

- 30Hz p95 Step target
- worker count 1/4/8/16 result digest一致
- production CPU workがworker budgetを16またはDomain数へsilent clampしないこと
- 16超worker budgetでも同一semantic outputを維持できるexecutor contract
- 100k D0 resident scenario
- 500k physical presence scenario
- memory guard内
- SQLite p95 commit target
- snapshot 16 GiB class throughput
- Core/Gateway publication bandwidth
- promotion budget canonical deferral
- lag/backpressureでaccepted Operation loss 0

## 31. Remaining P4-06 work

- benchmark scenario/data generator exact seed/spec
- performance test acceptance thresholdをP4-08へ登録
- P4-03へpromotion/demotion budget Configを追加
- P4-07 metrics registryとのcross-review

blocker: なし。