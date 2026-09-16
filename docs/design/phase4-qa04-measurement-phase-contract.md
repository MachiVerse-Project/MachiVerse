# Phase 4 QA-04 measurement phase contract

Status: Normative harness execution convention  
Tracking: #240, #365  
Implementation: Draft PR #265

## 1. Purpose

This contract fixes the State / Operation / measurement identity used by the actual `perf.reference.v1` production run. It does not change simulation scheduling semantics. It aligns the QA-04 harness with the already-canonical production reference-world basis and ordinary Operation scheduling rules.

## 2. Production initialization boundary

The production reference world is durably established at `State(1)` before benchmark workload timing begins.

```text
State(0)  persistence genesis authority
State(1)  canonical production reference-world basis / initialization / not sampled
```

The `State(0) -> State(1)` production-basis persistence transition is initialization evidence. It is not one of the 9,000 warm-up workload transitions and is not sampled as benchmark Step latency.

## 3. Operation / basis / resulting-State identity

For canonical workload transition ordinal `t` where `t` starts at 1:

```text
injectionStep          = t - 1
effectiveStep          = t
execution basis State  = State(t)
resulting finalized    = State(t + 1)
```

Equivalent relation for injection step `n`:

```text
injectionStep=n
  -> effectiveStep=n+1
  -> execute from basis State(n+1)
  -> finalize State(n+2)
```

This follows the existing `Qa04CanonicalOperationBindingV1`, `Qa04CanonicalOperationDurableAdmissionV1`, `OperationSchedulerStateV1`, and `StepInputFreezerV1` contracts. The harness must not renumber Operations or alter effective Step values to make measurement numbering convenient.

## 4. Warm-up / measurement ranges

The canonical production benchmark performs exactly 27,000 workload transitions after the durable `State(1)` initialization basis.

```text
State(1)           initialization basis / not sampled
State(2..9001)     warm-up results: 9,000 finalized workload Steps / not sampled
State(9002..27001) measurement results: 18,000 finalized workload Steps / sampled
State(27002+)      cooldown or post-measurement drain / not sampled
```

Therefore:

- warm-up workload transition ordinals are `1..9000`;
- measurement workload transition ordinals are `9001..27000`;
- the canonical final State for one complete benchmark run is `State(27001)`;
- every worker-count / repetition determinism comparison binds to the `State(27001)` final State diagnostic and the same ordered transition history.

`Qa04BenchmarkRunMeasurementSessionV1` classifies the resulting finalized State, so its phase constants must reflect these ranges.

## 5. Running-Snapshot trigger

The standard running-Snapshot interval remains 18,000 finalized State steps. Under the production numbering above, the measurement range contains exactly one standard trigger:

```text
State(18000)
```

`State(18000)` is inside `State(9002..27001)`, so the trigger remains part of measurement sampling. Snapshot background drain may continue outside the measured Step barrier as defined by persistence design.

This contract changes no Snapshot interval and does not shift the trigger to `State(18001)`.

## 6. Determinism and evidence binding

Release evidence must bind the following identities together without translation or off-by-one normalization:

- canonical production basis `State(1)`;
- exact Operation `injectionStep` and `effectiveStep` values;
- exact 9,000 warm-up and 18,000 measurement workload transitions;
- final `State(27001)` state digest;
- per-Step committed transition digest / Operation terminal semantics / Config and history identity;
- the standard `State(18000)` Snapshot trigger.

Reduced, preflight, synthetic, or differently-numbered runs cannot be promoted to release evidence.

## 7. Fail-closed drift checks

The executable contract must cross-check this document against the canonical profile and production authority:

- warm-up transition count = `Qa04ReferenceLoadV1.WarmupSteps = 9,000`;
- measurement transition count = `Qa04ReferenceLoadV1.MeasurementSteps = 18,000`;
- initialization basis = `Qa04ProductionReferenceWorldAssemblerV1.CanonicalBasisStep = 1`;
- first workload injection `0` binds to effective Step `1` and executes from `State(1)`;
- final benchmark State = initialization basis + warm-up count + measurement count = `27,001`;
- exactly one standard Snapshot trigger exists inside the measurement resulting-State range and it is `State(18000)`.

Any drift fails closed rather than silently shifting the sampled window or final determinism digest.
