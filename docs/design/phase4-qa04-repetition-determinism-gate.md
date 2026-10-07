# Phase 4 QA-04 repetition / determinism gate

Status: Harness evidence contract  
Tracking: #240  
Implementation: Draft PR #265

## 1. Canonical run matrix

`perf.reference.v1` requires four worker counts and three independent process runs per worker count:

```text
workers = [1, 4, 8, 16]
runs_per_worker = 3
total_process_runs = 12
```

The gate rejects missing, duplicate, out-of-range, or non-canonical worker/run keys. Every process run must contain the complete 18,000-Step measurement series before it can participate in aggregation.

## 2. Worker-16 p95 aggregation

P4-06 explicitly defines the performance result as the median of the three run-level p95 values. For worker count 16 the three p95 values are sorted and the middle value is compared with the standard `33.333 ms` target.

Worker counts 1/4/8 remain scaling/determinism data and are not required by this aggregation gate to satisfy the 30 Hz Step p95 target.

Other per-run measurement coverage and metric thresholds remain the responsibility of the raw measurement acceptance contract; this gate does not reinterpret unspecified aggregation rules.

## 3. Cross-worker determinism evidence

Every one of the 12 process runs must provide 32-byte evidence digests for:

- final `StateDiagnostic.state_digest`;
- transition-committed semantic sequence;
- Operation terminal semantic results;
- Config generation/history;
- promotion deferral order.

The gate requires each evidence digest to be byte-identical across all worker counts and all repetitions. The aggregate digests are harness evidence only and do not become world authority.

## 4. Boundary

This contract defines validation of completed process-run evidence. It does not create the missing canonical reference world, operation-kind mapping, detail workload, or benchmark samples. Release evidence remains fail-closed until the full canonical runtime can produce all 12 real process runs.
