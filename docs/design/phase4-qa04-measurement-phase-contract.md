# Phase 4 QA-04 measurement phase contract

Status: Harness execution convention  
Tracking: #240  
Implementation: Draft PR #265

## 1. Finalized-Step convention

The reference process starts from genesis `State(0)`. Initialization itself is excluded from timing.

```text
State(0)          initialization / not sampled
State(1..9000)    warm-up: 9,000 finalized Steps / not sampled
State(9001..27000) measurement: 18,000 finalized Steps / sampled
State(27001+)     cooldown or post-measurement snapshot drain / not sampled
```

This is a harness-only classification. It cannot change scheduling, operation acceptance, domain execution, random contexts, solver limits, or committed world state.

## 2. Standard Snapshot trigger

The standard running-Snapshot interval is 18,000 finalized Steps. Under the canonical phase convention the 18,000-Step measurement window contains exactly one normal trigger:

```text
State(18000)
```

The trigger remains inside measurement sampling. Snapshot background drain after the frozen cut may continue outside the Step barrier as defined by the persistence design.

## 3. Fail-closed drift check

The executable contract cross-checks the phase counts against `Qa04ReferenceLoadV1` and the trigger against `RunningSnapshotCoordinatorV1.StandardIntervalSteps`. A future change to either normative count therefore cannot silently move the measurement or Snapshot boundary.
