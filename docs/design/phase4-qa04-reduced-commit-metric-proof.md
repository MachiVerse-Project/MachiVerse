# Phase 4 QA-04 reduced SQLite COMMIT metric proof

Status: Stage 2 instrumentation proof  
Tracking: #240  
Implementation: Draft PR #265

## 1. Scope

This proof connects the QA-04 SQLite COMMIT latency observer to the existing reduced 30-Step authoritative bridge without changing the bridge's world-authority logic.

The reduced bridge is still not the canonical 27,000-Step `perf.reference.v1` execution and does not enable release evidence.

## 2. Observation boundary

`SqlitePersistenceStore.PersistTransitionCommitAsync` records elapsed wall time only around the synchronous SQLite `transaction.Commit()` call. A sample is emitted only after COMMIT returns successfully.

`SqlitePersistenceStore.PushAmbientCommitMetricSink` installs a diagnostics-only observer in the current async control flow. The scope is based on `AsyncLocal` and is restored on dispose. It exists so process/benchmark wrappers can instrument a production path even when the store instance is owned internally by that path.

An instance-attached observer and an ambient observer may coexist. The same sink instance is not double-called.

Observer failure never changes the result of an already-successful COMMIT. The store counts observer failures separately.

## 3. Reduced proof

`Qa04InstrumentedAuthoritativeStepLoopBridgeV1` wraps the existing `Qa04AuthoritativeStepLoopBridgeV1` and requires:

```text
SQLite successful COMMIT sample count == authoritative reduced Step count == 30
```

It also requires the underlying bridge's durable SQLite recovery proof to remain true.

The smoke fixture executes the real reduced bridge with one actual Resident record and verifies 30 successful COMMIT samples for 30 authoritative reduced Steps.

## 4. Boundary

This proves that the COMMIT metric is connected to the same SQLite durability boundary used by the reduced authority path. It does not prove the P4-06 latency threshold, because the reduced fixture is not the canonical reference workload and CI runner timing is not reference-node benchmark evidence.

Full QA-04 acceptance still requires the canonical reference world, 9,000 warm-up Steps, 18,000 measured Steps, full workload injection, Snapshot COW measurement, memory/GC collection, three process repetitions per worker count, and reference-node result aggregation.
