# Phase 4 QA-04 measurement convention

Status: Harness convention / operational only  
Tracking: #240  
Implementation: Draft PR #265  
Applies to: `perf.reference.v1`

## 1. Purpose

P4-06 fixes the performance thresholds but does not define an interpolation formula for percentiles or the exact sampling convention for the continuous 60-second Step mean. This document fixes those harness-only conventions so separate process runs are compared consistently.

These values are **not simulation authority**. Wall-clock samples, memory samples, GC pauses, and metric observer failures must never alter Step order, random context, detail level, solver iteration limits, accepted Operation retention, or committed world state.

## 2. Duration representation

The in-process collector stores duration as .NET `TimeSpan` ticks (100 ns) and retains every measurement-phase sample. No histogram approximation is used for QA-04 acceptance percentiles.

Warm-up and initialization samples are excluded. A canonical run must provide exactly 18,000 finalized-Step samples for the measurement phase.

## 3. Percentile convention

For a sorted duration series of `N` samples, percentile `p` uses nearest-rank:

```text
rank = ceil((p / 100) * N)
value = sorted[rank - 1]
```

The convention is used for p50, p95, and p99 acceptance comparisons.

## 4. Continuous 60-second Step mean

Every finalized Step records:

- monotonic elapsed wall time since measurement-phase start;
- finalized Step wall duration.

For every sample whose completion elapsed time is at least 60 seconds, the harness computes the arithmetic mean of Step durations whose completion timestamps are inside the trailing `(t - 60s, t]` wall-time window. The maximum such trailing-window mean across the measurement run is compared with the P4-06 `<= 30 ms` target.

The monotonic completion clock is diagnostic only and cannot be used as world time.

## 5. SQLite COMMIT sample boundary

`core.persistence.commit_ms` measures only the synchronous SQLite `transaction.Commit()` call after all transition/history/operation/meta writes have been prepared inside the transaction.

A sample is emitted only after COMMIT returns successfully. Failed commits are correctness failures and are not mixed into the successful-COMMIT latency percentile.

The metric observer is invoked after the durable COMMIT. Observer exceptions are swallowed so diagnostics cannot turn an already-successful COMMIT into an apparent authority failure. Such observer exceptions increment a separate failure count, and a non-zero count fails QA-04 measurement acceptance.

A canonical 18,000-Step measurement run therefore requires exactly 18,000 successful COMMIT latency samples.

## 6. Snapshot / GC / memory

- snapshot COW barrier duration is a raw duration series; at least one sample is required in the canonical measurement interval and p95 must be <= 5 ms;
- observed GC pause p99 must be <= 20 ms when GC pause samples exist;
- Core working-set samples retain the maximum observed value; the standard target is <= 22 GiB and hard operational guard is 28 GiB.

Instrumentation hooks for Step duration, Snapshot COW, GC, and memory are connected incrementally as their production execution boundaries are assembled. Missing required measurement samples fail closed; they are never treated as zero.

## 7. Functional performance guards

A complete QA-04 run also fails when either is non-zero:

- accepted Operation loss count;
- hidden solver-iteration reduction count.

Performance pressure must not change authoritative semantics to improve timing results.
