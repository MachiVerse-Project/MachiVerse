# 詳細設計 Phase 4: Test Strategy / Acceptance Criteria

Status: Complete / P4-08  
Tracking: Issue #16  
Parent: `phase4-implementation-ready-design.md`  
Predecessors: P4-01〜P4-07

## 1. 目的

Phase 1〜4で確定した契約を、implementation完成後に客観的にPASS/FAIL判定できるtest suiteへ写像する。

本書ではunit test framework名やCI vendorは固定しない。固定するのはtest category、fixture、input、expected invariant、acceptance thresholdである。

## 2. Acceptance hierarchy

```text
L0 schema/unit
L1 domain/property
L2 component integration
L3 cross-component contract
L4 persistence/recovery
L5 determinism/replay
L6 performance/soak
L7 security/observability
L8 release acceptance
```

上位levelは下位level PASSを前提とする。

## 3. Test identity

```text
TestCaseId := StableToken
```

standard prefix:

```text
schema.*
determinism.*
protocol.*
config.*
persistence.*
domain.*
transaction.*
detail.*
performance.*
observability.*
security.*
component.*
release.*
```

TestCaseIdの意味をin-place変更しない。

## 4. Golden artifact policy

Golden fixtureは次をversion管理する。

- canonical MV-DCBOR vectors
- protobuf logical payload examples
- state diagnostic digest fixtures
- Config canonical examples
- persistence history chain fixture
- Snapshot manifest/chunk fixture
- law AST fixture
- deterministic random vectors
- same-Step ordering vectors

Golden更新はschema/algorithm version変更を伴う場合だけ行い、test failureを消すためのblind regenerateを禁止する。

## 5. Schema / primitive tests

| ID | Test | Acceptance |
|---|---|---|
| `schema.id128.binary` | 16-octet ID encode/decode | exact round-trip, ZERO reject where required |
| `schema.hash256.binary` | 32-octet digest | exact round-trip |
| `schema.stable-token.valid` | valid token corpus | all accept |
| `schema.stable-token.invalid` | Unicode/uppercase/length/punctuation invalid corpus | all reject |
| `schema.u64be.order` | uint64 DB encoding | bytewise order == numeric order |
| `schema.same-step-key.db-order` | 55-byte DB key | bytewise sort == logical tuple sort |
| `schema.fixed-round-even` | signed/unsigned quotient vectors | exact expected result |
| `schema.int128-overflow` | overflow boundary | explicit failure, no wrap |
| `schema.quaternion.canonical` | equivalent q/-q | one canonical representation |
| `schema.partition-count` | standard registry | exactly 97 |
| `schema.config-field-count` | config registry | exactly 136 excluding meta |

## 6. Canonical encoding/hash tests

| ID | Acceptance |
|---|---|
| `determinism.dcbor.vector` | canonical bytes match golden fixture |
| `determinism.domain-hash.vector` | SHA-256 domain separated values match fixture |
| `determinism.entity-id.vector` | same creation context => same EntityId |
| `determinism.intent-id.vector` | same source/ordinal => same IntentId |
| `determinism.transaction-id.vector` | same semantic context => same TransactionId |
| `determinism.random.vector` | same RandomContext => same output across runs/platforms |
| `determinism.random.order-independence` | iteration order permutation does not change per-subject random result |

## 7. Same-Step ordering/conflict tests

| ID | Scenario | Acceptance |
|---|---|---|
| `determinism.order.permutation` | same intents in 100 random arrival permutations | identical canonical order |
| `determinism.order.thread-count` | generate candidates with 1/4/8/16 worker | identical order |
| `determinism.conflict.exclusive-first-valid` | conflicting candidates | expected first valid stable key wins |
| `determinism.conflict.sequential` | sequential same scope | exact canonical final value |
| `determinism.conflict.set-merge` | duplicates/permutations | same normalized set |
| `determinism.conflict.reduce` | integer reduction permutations | exact same result |
| `determinism.conflict.custom` | domain custom resolver | pure result independent of arrival |

## 8. Domain registry/payload tests

For all 97 partition schemas, parameterized suite:

```text
domain.partition.schema-roundtrip
domain.partition.required-fields
domain.partition.scalar-range
domain.partition.reference-validation
domain.partition.canonical-list-order
domain.partition.record-revision
domain.partition.retirement
domain.partition.secondary-index-rebuild
```

Acceptance:

- 97/97 pass。
- foreign owner direct mutation API absent or rejected。
- index rebuild from records yields same query result as preexisting index。

## 9. Spatial tests

| ID | Acceptance |
|---|---|
| `domain.spatial.sdf.sign` | solid/void/boundary semantics exact |
| `domain.spatial.sdf.interpolation` | fixed Q32.32 expected vectors |
| `domain.spatial.sdf.octree-order` | child traversal 0..7 stable |
| `domain.spatial.cave-overhang` | geometry supports multiple vertical surfaces at same XY |
| `domain.spatial.containment` | deterministic containment relation |
| `domain.spatial.index.rebuild` | hierarchical AABB grid same candidate set/order |
| `domain.spatial.geometry-revision` | stale revision intent reject |

## 10. Physical / collision tests

| ID | Acceptance |
|---|---|
| `domain.physical.integration` | semi-implicit Euler golden vector |
| `domain.physical.gjk` | convex corpus exact intersection classification |
| `domain.physical.epa` | penetration result within 1mm deterministic tolerance representation |
| `domain.physical.terrain-contact` | SDF conservative advancement bounded result |
| `domain.physical.contact-order` | pair/contact permutation -> same impulse result |
| `domain.physical.nonconvergence` | explicit failure, no alternate platform fallback |
| `domain.physical.item-transfer-exclusive` | two pickups same item -> one canonical winner |

## 11. Pathfinding tests

| ID | Acceptance |
|---|---|
| `domain.path.astar.tie` | equal cost -> node id tie-break |
| `domain.path.astar.optimal` | reference graphs shortest route exact |
| `domain.path.hierarchical` | local/regional composed route deterministic |
| `domain.path.no-route` | stable `path.no-route` |
| `domain.path.budget` | bounded search emits stable budget-exceeded result |

## 12. Environment tests

| ID | Acceptance |
|---|---|
| `domain.environment.flux.conservation` | shared face A loss == B gain |
| `domain.environment.atmosphere.permutation` | cell worker order does not alter state |
| `domain.environment.climate.recurrence` | integer recurrence golden vector |
| `domain.environment.hydrology.mass` | total water conserved minus explicit source/sink |
| `domain.environment.groundwater.jacobi` | fixed iteration exact digest |
| `domain.environment.ocean.flux` | volume/salinity/thermal stock conserved |
| `domain.environment.erosion.material` | geometry/material transaction no partial effect |
| `domain.environment.ecology.random` | per-cohort result order independent |
| `domain.environment.contaminant.mass` | stock conservation |

## 13. Resident tests

| ID | Acceptance |
|---|---|
| `domain.resident.lifecycle` | birth/alive/death legal transition only |
| `domain.resident.health.bounds` | ppm scalars never silent overflow |
| `domain.resident.disease.random` | addressable random repeatable |
| `domain.resident.goal.tie` | utility/priority/id exact tie-break |
| `domain.resident.goap.bound` | <=256 expansion; deterministic fallback |
| `domain.resident.skill.curve` | golden integer learning vectors |
| `domain.resident.belief.delivery-separation` | delivery alone does not directly set belief without perception path |
| `domain.resident.physical-separation` | action decision does not mutate pose directly |

## 14. Society/economy tests

| ID | Acceptance |
|---|---|
| `domain.market.clearing` | reference order book exact price/quantity |
| `domain.market.arrival-independence` | order arrival permutations same clearing |
| `domain.market.tie-price` | quantity/imbalance/lowest-price rule exact |
| `domain.ledger.double-entry` | balanced transaction accepts |
| `domain.ledger.unbalanced` | reject, no partial account update |
| `domain.payment.insufficient` | stable failure/no wrap |
| `domain.production.conservation` | integer recipe stock exact |
| `domain.property.physical-separation` | economic transfer does not teleport item |

## 15. Governance/security tests

| ID | Acceptance |
|---|---|
| `domain.law.ast.decode` | only registered AST nodes accepted |
| `domain.law.arbitrary-code-reject` | executable payload impossible/rejected |
| `domain.law.applicability` | jurisdiction/effective Step exact |
| `domain.law.resolution-order` | priority/specificity/id exact |
| `domain.law.conflict` | unresolved conflict returns stable conflict result |
| `domain.enforcement.physical-separation` | order alone does not move/damage subject |
| `domain.border.permission-crossing` | legal permission and physical crossing represented separately |

## 16. Infrastructure tests

| ID | Acceptance |
|---|---|
| `domain.infrastructure.dijkstra.tie` | exact stable route |
| `domain.infrastructure.queue.order` | eligible_step/priority/id exact |
| `domain.infrastructure.wdrr` | stable integer fairness sequence |
| `domain.infrastructure.power.jacobi` | fixed iteration exact digest |
| `domain.infrastructure.water.jacobi` | fixed iteration exact digest |
| `domain.infrastructure.outage-cascade` | dependency chain exact and bounded |
| `domain.information.delivery-belief-separation` | delivered fact does not directly mutate belief |

## 17. Participation tests

| ID | Acceptance |
|---|---|
| `domain.participation.one-diver-per-resident` | second active bind rejects |
| `domain.participation.one-resident-per-diver` | conflicting bind canonical resolution |
| `domain.participation.disconnect-preserves-binding` | network disconnect alone leaves binding active |
| `domain.participation.death-transition` | deceased resident marks operability without identity loss |
| `domain.participation.absence-policy-generation` | stale generation reject |
| `domain.participation.detail-floor` | bound resident maintains configured minimum detail |

## 18. Cross-domain transaction atomicity

All 17 TransactionKind are parameterized by:

```text
transaction.<kind>.success
transaction.<kind>.required-participant-failure
transaction.<kind>.invariant-failure
transaction.<kind>.crash-before-commit
transaction.<kind>.replay
```

Acceptance:

- success: all required participant effects appear in State(S+1)。
- any required failure: no participant authoritative effect finalized。
- crash before durable transition: State(S) authority。
- replay: same transaction identity/outcome/digest。

Special golden scenarios:

```text
transaction.mining-excavation
transaction.market-sale-delivery
transaction.death
transaction.natural-disaster-cascade
transaction.military-operation
```

## 19. Detail transition tests

| ID | Acceptance |
|---|---|
| `detail.identity-preservation` | persistent record id survives D0->D3->D0 |
| `detail.stock-conservation` | stock exact before/after transition |
| `detail.obligation-preservation` | contract/debt/binding reference preserved |
| `detail.materialization-repeatability` | same aggregate/context => same materialized ids/state |
| `detail.hysteresis` | thresholds use Step, not wall clock |
| `detail.budget.defer-order` | >budget queue same order across workers |
| `detail.bound-resident-floor` | bound Resident not demoted below floor |
| `detail.active-transaction-floor` | active transaction entities not demoted below floor |
| `detail.camera-independence` | View camera/FPS changes do not alter detail state |

## 20. Protocol common tests

| ID | Acceptance |
|---|---|
| `protocol.envelope.valid` | valid WireEnvelope accepted |
| `protocol.envelope.size-limit` | >8MiB reject |
| `protocol.wrong-protocol` | stable reject code |
| `protocol.version.no-common` | connection reject |
| `protocol.version.highest-common` | deterministic version selection |
| `protocol.capability.required-missing` | connection/message reject, no silent downgrade |
| `protocol.negotiation-stale` | stale generation reject |
| `protocol.unknown-message` | stable reject |
| `protocol.uint64.browser-lossless` | JS boundary exact representation |
| `protocol.protobuf-unknown-compatible` | same-major allowed optional field handling according schema |

## 21. Operation protocol tests

| ID | Acceptance |
|---|---|
| `protocol.operation.retry-same-id` | one world effect |
| `protocol.operation.same-id-different-digest` | mismatch reject, old state unchanged |
| `protocol.operation.candidate-not-effective` | candidate Step never treated authoritative |
| `protocol.operation.pause-floor` | Pause new accepted op >= P+1 |
| `protocol.operation.deadline-reject` | exact policy result |
| `protocol.operation.defer-grace` | exact effective Step/result |
| `protocol.operation.status-after-reconnect` | current authoritative lifecycle returned |
| `protocol.batch.same-id-different-digest` | wrapper reject |
| `protocol.batch.partial` | per-operation semantics preserved |

## 22. Master/Gateway failover tests

| ID | Acceptance |
|---|---|
| `protocol.master.stale-generation` | stale batch reject, contained op not terminalized |
| `protocol.master.failover-unknown-ack` | retry converges without double apply |
| `protocol.master.receipt-not-core-accept` | custody states distinct |
| `protocol.gateway.arrival-order` | peer timing permutations same final logical batch |
| `protocol.gateway.resync-gate` | no new authoritative admission without confirmed basis |

## 23. View publication tests

| ID | Acceptance |
|---|---|
| `protocol.publication.full` | full rebuild exact projection |
| `protocol.publication.delta` | valid base applies exact result |
| `protocol.publication.bad-base` | resync required, no blind apply |
| `protocol.publication.coalesce` | continuity maintained |
| `protocol.view.prediction-not-authority` | predicted state never receives confirmed token |
| `protocol.view.slow-client` | does not block custody/result path |

## 24. Auth/session tests

| ID | Acceptance |
|---|---|
| `security.oidc.pkce-required` | missing/invalid verifier fails |
| `security.session.cookie-properties` | configured secure BFF contract enforced |
| `security.token-not-browser-exposed` | raw upstream token absent from View protocol/local storage contract |
| `security.session-revoke` | new protected requests denied |
| `security.role-domain-separation` | General Administrator != Admin View permission |
| `security.disconnect-binding-separation` | session expiry does not automatically release participation binding |

## 25. Config tests

| ID | Acceptance |
|---|---|
| `config.default-completion` | missing default fields added deterministically |
| `config.writeback.atomic` | crash injection never leaves accepted partial config |
| `config.unknown-key` | reject |
| `config.type-range` | invalid corpus reject |
| `config.cross-constraint` | cadence/heartbeat/reconnect/session constraints exact |
| `config.stale-generation` | reject no partial apply |
| `config.simulation-effective-step` | new value begins exact assigned transition |
| `config.atomic-change-set` | one invalid field -> none applied |
| `config.restore-saved-authority` | local simulation config does not override save history |
| `config.view-world-independence` | View/Admin Config permutations -> same Core state digest |
| `config.worker-count-independence` | 1/4/8/16 same state digest |

## 26. Persistence transaction crash matrix

Crash injection points for:

```text
Operation acceptance
Operation scheduling
Transition commit
Snapshot commit
Migration generation switch
Audit append
```

For each write stage:

```text
before DB begin
mid write
before fsync/commit
immediately after commit
before response/publication
```

Acceptance:

- durable fact retained。
- non-durable candidate not exposed as durable success。
- no half transition。
- recovery converges to valid chain。

## 27. Snapshot tests

| ID | Acceptance |
|---|---|
| `persistence.snapshot.required-sections` | exactly required 103 logical sections present |
| `persistence.snapshot.chunk-digest` | corruption detected |
| `persistence.snapshot.logical-digest-compression-independent` | none/zstd same logical digest |
| `persistence.snapshot.orphan-staging` | not recovery candidate |
| `persistence.snapshot.latest-corrupt-fallback` | older + intact history reconstructs same latest state |
| `persistence.snapshot.latest-corrupt-no-history` | startup reject, no silent rollback |

## 28. History/dedup tests

| ID | Acceptance |
|---|---|
| `persistence.history.hash-chain` | missing/reordered/replaced record detected |
| `persistence.history.unknown-authoritative-type` | startup/replay reject |
| `persistence.dedup.world-lifetime` | terminal tombstone survives snapshot/restart |
| `persistence.rich-result-expired` | dedup still prevents reapply |
| `persistence.history.full-retention` | v1.0 compaction does not remove semantic history |

## 29. Recovery/replay tests

| ID | Acceptance |
|---|---|
| `persistence.recovery.clean` | latest durable State reconstructed |
| `persistence.recovery.pending-operation` | accepted/scheduled state restored |
| `persistence.recovery.config-history` | exact historical generations/effective Steps |
| `persistence.recovery.continuity-token` | exact committed token sequence |
| `persistence.replay.historical` | target State(T) digest matches original |
| `persistence.replay.fresh-process` | process restart does not change logical result |
| `persistence.replay.no-wall-time` | different replay speed same digest |

## 30. Migration tests

| ID | Acceptance |
|---|---|
| `persistence.migration.non-destructive` | source generation unchanged |
| `persistence.migration.crash-before-switch` | source remains current |
| `persistence.migration.crash-after-switch` | target remains current/valid |
| `persistence.migration.identity` | World/Entity/Operation identity preserved |
| `persistence.migration.invalid-target` | CURRENT not switched |

## 31. Determinism matrix

Normative scenario: `perf.reference.v1`。

Run dimensions:

```text
worker count: 1,4,8,16
process restart: no/yes at deterministic checkpoints
Gateway count: 1,2,4
Gateway route permutation: 4 variants
View connected: 0/100 subscribers
logging level: warn/debug
telemetry exporter: enabled/disabled/failing
```

All semantic-equivalent runs require:

- each committed Step state digest equal。
- terminal Operation semantic outcomes equal。
- transaction result equal。
- Config history equal。

Operational timings/logs need not equal。

## 32. Performance acceptance

Use `phase4-performance-benchmark-profile.md` and the Alpha 1.1 override in `phase4-alpha11-runtime-release-profile.md`。

Alpha 1.1 release performance profile requires:

- standard runtime StepRate = 10/1 steps/sec（10 tick/s）。
- worker 8 / 16, three independent process runs each。
- p99 authoritative Step processing time <=100ms。
- deadline miss ratio <=1%。
- pacing/wait excluded from authoritative processing latency。
- production CPU worker budget evidence valid。
- Core memory / persistence / Snapshot / publication guards remain valid through the target reports and QA-04 subprofiles。
- no accepted Operation loss。
- no hidden solver iteration reduction。
- deterministic final-state evidence一致。

Historical 30Hz-oriented p95/p99/rolling-mean values remain reference telemetry but are not Alpha 1.1 release blockers。

Performance failure does not authorize semantic shortcut; release profile fails。

## 33. Soak test

```text
TestCaseId = performance.soak.12h
```

Reference:

- 12 wall-clock hours（43,200 seconds minimum）continuous Alpha 1.1 standard simulation load。
- periodic snapshot/recovery checkpoint validation。
- synthetic Gateway reconnect/failover every 30 min operational schedule。
- View churn/slow consumer load。

Acceptance:

- no state digest divergence against parallel verifier checkpoints。
- no unbounded memory growth >10% after warm steady state excluding retained world state growth。
- no accepted operation loss。
- history/audit chain valid。
- no unrecoverable queue deadlock。

## 34. Observability tests

| ID | Acceptance |
|---|---|
| `observability.metric.cardinality` | <=5000 expected series, warning before 10000 |
| `observability.metric.no-id-label` | registry/runtime prohibits high-cardinality IDs |
| `observability.trace.w3c-propagation` | trace context preserved across supported boundaries |
| `observability.trace.world-independence` | tracing on/off same world digest |
| `observability.log.redaction` | secret corpus absent from emitted logs |
| `observability.audit.hash-chain` | tamper/gap detected |
| `observability.audit.retention-anchor` | deleted prefix has valid anchor |
| `observability.audit.writer-fail-closed` | protected Admin forwarding blocked when required audit unavailable |
| `observability.exporter-failure` | world Step/digest unchanged |

## 35. Security malformed/fuzz tests

Fuzz targets:

- protobuf envelope/payload
- WebSocket frame/message assembly
- StableToken parser
- Config TOML parser/schema layer
- Rule AST
- Snapshot manifest/chunk parser
- history payload decoder
- log/audit query filter

Acceptance:

- no process memory corruption。
- no credential leakage。
- malformed untrusted input does not mutate authoritative world before validation。
- bounded size/count/recursion limits enforced。

## 36. Component independent contract tests

Each component must build/run contract tests without other production component binary。

### Core

Mock protocol peer + persistence fixtures。

### Gateway

Mock Core/peer/View/Admin protocol endpoints。

### General View

Protocol fixture server + rendering-independent publication tests。

### Admin View

Protocol fixture server + Config/audit/query tests。

Shared production DTO assemblyをtest convenienceの契約正本にしない。

## 37. Compatibility tests

For each protocol/config/persistence schema:

- same major older minor -> documented compatible behavior。
- unknown newer required semantic -> explicit reject。
- major mismatch -> reject/migration required。
- field rename/removal test migration path。
- unknown optional protobuf field preservation/ignore according boundary contract。

## 38. CI required suites

Per change category:

| change | required suites |
|---|---|
| common schema/hash/order | L0 + determinism matrix reduced |
| domain algorithm | domain/property + determinism + transaction impacted |
| protocol | protocol compatibility + fuzz + component contract |
| Config | config + restore/replay |
| persistence | crash matrix + replay + migration |
| performance-sensitive | benchmark reduced + memory regression |
| security/auth | security + audit + protocol |
| release candidate | all suites + Alpha 1.1 Step3 perf.reference.v1 + 12h soak |

## 39. Release acceptance record

```text
ReleaseAcceptanceRecordV1 {
  build_version,
  source_commit,
  schema_registry_digest,
  algorithm_registry_digest,
  config_schema_digest,
  test_suite_version,
  passed_test_ids,
  performance_report_refs,
  determinism_digest_summary,
  known_waivers,
  result
}
```

Waiver cannot override authority/data-loss/security-critical failure for standard release profile。

## 40. Non-waivable failures

Standard release cannot PASS with:

- determinism divergence
- accepted Operation loss/double apply
- persistence history/hash corruption not detected
- partial cross-domain transaction commit
- unauthorized mutation bypass
- credential leakage test failure
- required protocol compatibility failure
- world state mutation from View camera/telemetry/wall-clock race

## 41. P4-08 completion criteria

- all Phase4 area mapped to test category。
- exact deterministic matrix defined。
- crash injection matrix defined。
- protocol/config/recovery/detail transaction tests defined。
- performance benchmark and threshold linked。
- observability/security tests defined。
- independent component contract test requirement defined。
- release non-waivable failure defined。

unresolved P4-08 blocker: 0。

## 42. Completion decision

P4-08を`Complete`と判定する。