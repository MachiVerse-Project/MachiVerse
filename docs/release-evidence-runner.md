# INT-03 release evidence runner

Status: implementation under #236 / parent #234

## Purpose

`tools/MachiVerse.ReleaseEvidenceRunner` bridges the QA-04 external JSONL target-adapter contract and the fail-closed `MachiVerse.ReleaseAcceptance` evaluator.

It does not load Simulation Core, Gateway, View, or Administration View production assemblies. The assembled runtime is driven by an external target adapter process. This preserves the Phase 4 rule that repository QA/release tooling must not depend on component internal types.

The runner supports the staged Alpha 1.1 release path plus bounded contract validation:

```bash
dotnet run --project tools/MachiVerse.ReleaseEvidenceRunner --configuration Release -- verify

dotnet run --project tools/MachiVerse.ReleaseEvidenceRunner --configuration Release -- \
  run-step3 <contract-smoke|release> <source-commit> <adapter-executable> <plan-directory> <output-directory>

dotnet run --project tools/MachiVerse.ReleaseEvidenceRunner --configuration Release -- \
  run-step4 <contract-smoke|release> <source-commit> <adapter-executable> <plan-directory> <step3-evidence.json> <output-directory>

dotnet run --project tools/MachiVerse.ReleaseEvidenceRunner --configuration Release -- \
  run <contract-smoke|release> <source-commit> <adapter-executable> <plan-directory> <output-directory>

dotnet run --project tools/MachiVerse.ReleaseEvidenceRunner --configuration Release -- \
  apply <qa04-evidence-fragment.json> <base-evidence.json> <output-evidence.json>
```

Formal Alpha 1.1 Gate 4 uses `run-step3` followed by `run-step4` on the same candidate commit. The monolithic `run` path remains available for contract/compatibility use but does not replace the staged release authority.

## Canonical plan

Materialize the QA-04 profile before invoking a target adapter:

```bash
dotnet run --project tools/MachiVerse.PerformanceHarness --configuration Release -- \
  materialize artifacts/qa04-plan
```

The evidence runner requires the canonical QA-04 manifest SHA-256:

`4cdd020abcc8ce37a54944181ce718fb4ae6de8f562bf4f846d669dbdf155a06`

It consumes the materialized benchmark matrix, persistence profile, publication profile, soak plan, and benchmark summary. The reference benchmark matrix must remain 1/4/8/16 workers × 3 process runs with 9000 warmup + 18000 measurement Steps.

## External adapter protocol

The runner starts the configured adapter once per request, writes exactly one compact JSON object followed by a newline to stdin, and requires exactly one JSON response line on stdout.

Every request contains:

- `schemaVersion = 1.0`
- request kind
- `executionClass`
- stable request id
- exact release candidate `sourceCommit`
- canonical QA-04 manifest digest
- profile id
- materialized profile object
- benchmark run descriptor when applicable

Request/response pairs are:

| Request | Response |
|---|---|
| `benchmark-run` | `performance-benchmark-report-v1` |
| `persistence-stress` | `persistence-stress-report-v1` |
| `publication-stress` | `publication-stress-report-v1` |
| `soak-run` | `soak-report-v1` |

The runner rejects response kind, request id, execution class, source commit, manifest digest, or profile mismatches.

## Reference benchmark release extensions

The QA-04 canonical `PerformanceBenchmarkReportV1` fields remain required. For final acceptance calculation the external adapter must additionally expose these operational summary fields in each benchmark report:

- `step_mean_60s_ms`
- `persistence_commit_p99_ms`
- `snapshot_summary.cow_barrier_p95_ms`
- `accepted_operation_loss`
- `hidden_solver_iteration_reduction`

For formal Alpha 1.1 Step 3, `run-step3` selects worker 8 and 16 from the canonical matrix, three runs each, and applies the 10 tick/s release gate: p99 authoritative processing <=100ms and deadline miss ratio <=1%. Pacing/wait time is excluded from processing latency. Accepted Operation loss, hidden solver reduction, production CPU worker evidence, and deterministic digest checks remain fail-closed. Historical 30Hz-oriented p95/p99/rolling-mean values remain telemetry rather than Alpha 1.1 release blockers. A target-provided `passed=true` cannot override a runner-detected failure.

## Persistence and publication reports

`perf.persistence.v1` must report the complete 30-case crash matrix and confirm no durable fact loss, no uncommitted candidate publication, and a valid history chain.

`perf.publication.v1` must preserve the canonical 1 Gateway / 100 View / 10 slow-consumer profile and confirm slow-consumer isolation plus continuity after coalesce/resync.

## 12-hour soak anti-shortcut rule

`performance.soak.12h` has two independent duration checks in `release` mode:

1. the adapter report must claim at least 43200 seconds;
2. the adapter process itself must remain running for at least 43200 monotonic elapsed seconds as measured by the evidence runner.

The evidence duration is the smaller of reported and measured duration. Therefore an adapter cannot satisfy the release gate by immediately returning a fabricated `duration_seconds = 43200` report.

The adapter process is expected to remain alive while it orchestrates the assembled runtime for the continuous soak. Run this on a dedicated release host whose process/job lifetime permits a continuous 12-hour execution; ordinary PR CI is only for contract validation. A longer 24-hour run may be retained as optional extended endurance evidence.

## Artifact integrity

Every adapter response is persisted beneath the output directory and SHA-256 hashed by the runner. The `perf.reference.v1` aggregate is separately materialized and hashed.

`apply` re-reads every referenced report and recomputes its SHA-256 before modifying INT-03 release evidence. Relative paths that escape the fragment directory are rejected.

## Contract-smoke is not release evidence

`contract-smoke` exists only to verify the JSONL protocol and acceptance logic in short CI. Its fragment always has:

```text
executionClass = contract-smoke
releaseEligible = false
```

`apply` rejects it unconditionally.

`tests/MachiVerse.PerformanceAdapter.ContractFixture` is synthetic and also refuses any request whose `executionClass` is `release`.

## Release flow

On the exact release candidate checkout:

```bash
candidate=$(git rev-parse HEAD)

dotnet run --project tools/MachiVerse.PerformanceHarness --configuration Release -- \
  materialize artifacts/qa04-plan

dotnet run --project tools/MachiVerse.ReleaseEvidenceRunner --configuration Release -- \
  run-step3 release "$candidate" /path/to/real-assembled-runtime-adapter \
  artifacts/qa04-plan artifacts/gate4-step3

dotnet run --project tools/MachiVerse.ReleaseEvidenceRunner --configuration Release -- \
  run-step4 release "$candidate" /path/to/real-assembled-runtime-adapter \
  artifacts/qa04-plan artifacts/gate4-step3/gate4-step3-benchmark-evidence.json artifacts/qa04-release

dotnet run --project tools/MachiVerse.ReleaseEvidenceRunner --configuration Release -- \
  apply artifacts/qa04-release/qa04-evidence-fragment.json \
  artifacts/release/base-evidence.json artifacts/release/evidence.json

dotnet run --project tools/MachiVerse.ReleaseAcceptance --configuration Release -- \
  evaluate artifacts/release/evidence.json artifacts/release/ReleaseAcceptanceRecordV1.json "$candidate"
```

A full release still requires the non-performance P4-08 suite evidence in `base-evidence.json`. The evidence runner only owns the QA-04 performance/persistence/publication/soak slice.

## Remaining runtime-side work

This runner standardizes and validates orchestration, provenance, duration, and report integrity. A real assembled-runtime target adapter must still drive the canonical reference world and return the required reports. The contract fixture is intentionally incapable of serving that role.
