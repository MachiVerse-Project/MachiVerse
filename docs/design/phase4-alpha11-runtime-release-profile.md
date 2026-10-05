# Alpha 1.1 runtime / release profile amendment

Status: **Approved normative amendment for Alpha 1.1 release profile**
Tracking: Issue #556
Related implementation: #548 / #554 / #555 / #558 / #559

## 1. Scope and precedence

This amendment defines the current Alpha 1.1 runtime and Gate 4 release-evidence profile. It does not rewrite historical Phase 1–4 decisions or change Config schema `1.0` defaults in-place.

Within the Alpha 1.1 release-profile scope, this document overrides older Phase 4 text that describes 30Hz performance acceptance or a mandatory 24-hour soak. Historical completion/review documents remain records of the contract that existed when they were written.

## 2. StepRate authority

The authoritative time axis remains integer `SimulationStep`. `StepRate` remains owned by Simulation Core Config and remains `SIMULATION + RUNTIME_SAFE` with explicit effective Step, ConfigGeneration, ConfigDigest, and replay/history authority.

Config schema `1.0` defaults remain unchanged for compatibility:

```text
simulation.step-rate.numerator = 30
simulation.step-rate.denominator = 1
```

Alpha 1.1 standard runtime does **not** redefine that schema default. The Alpha 1.1 runtime profile explicitly selects:

```text
simulation.step-rate.numerator = 10
simulation.step-rate.denominator = 1
```

Therefore `30/1` is the schema default while `10/1` is the Alpha 1.1 standard runtime/release profile value.

## 3. Gate 4 Step 3 — Alpha 1.1 10 tick/s acceptance

The canonical workload identity remains `perf.reference.v1`; changing the release threshold does not authorize workload reduction or semantic shortcuts.

Alpha 1.1 formal Step 3:

- target runtime rate: **10 tick/s**;
- authoritative processing budget: **100 ms/tick**;
- hard latency boundary: **p99 authoritative Step processing time <= 100 ms**;
- hard deadline boundary: **deadline miss ratio <= 1%**;
- pacing/wait time is excluded from authoritative processing latency;
- release execution profiles: worker count **8** and **16**, three independent runs each;
- requested worker budget must be proven to reach production CPU execution rather than remaining metadata;
- accepted Operation loss must be zero;
- hidden solver-iteration reduction is forbidden;
- determinism/durability/COMMIT-before-publication requirements remain unchanged.

The historical 30Hz-oriented p95 `33.333 ms`, p99 `50 ms`, and 60-second mean `30 ms` values may remain as historical/reference telemetry, but they are not Alpha 1.1 release blockers.

## 4. Gate 4 Step 4 — 12-hour endurance acceptance

Alpha 1.1 hard endurance TestCaseId is:

```text
performance.soak.12h
```

Release evidence requires at least **43,200 wall-clock seconds**. Both adapter-reported duration and monotonic process elapsed time are checked; the smaller duration is authoritative for release eligibility.

The duration reduction is the only intentional relaxation. The following remain mandatory:

- parallel verifier digest match;
- post-warmup memory growth guard;
- accepted Operation loss = 0;
- history/audit chain validity;
- no unrecoverable queue deadlock;
- persistence/publication preflight PASS;
- same release-candidate commit as the preceding Step 3 evidence.

A run longer than 12 hours, including 24-hour endurance validation, may be retained as optional extended evidence but is not required for Alpha 1.1 hard release acceptance.

## 5. Fail-closed evidence boundary

Short `contract-smoke` or synthetic CI runs never become release evidence. `releaseEvidenceCapable=true` requires formal release execution and complete 12-hour duration evidence. A PR CI run is not a substitute for the wall-clock endurance run.

## 6. Canonical QA-04 binding

The Alpha 1.1 12-hour QA-04 manifest SHA-256 is:

```text
4cdd020abcc8ce37a54944181ce718fb4ae6de8f562bf4f846d669dbdf155a06
```

Current release tooling and validation workflows must bind this digest consistently.
