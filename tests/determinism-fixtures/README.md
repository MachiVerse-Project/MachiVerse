# QA-02 Determinism / Replay Fixtures

This directory is the version-controlled source for `QA-02` deterministic execution matrix and semantic trace comparison contracts.

## Authority boundary

QA-02 does not define world semantics, protocol semantics, Config semantics, persistence semantics, or component implementation details. Those remain owned by the canonical design/protocol sources and the responsible component.

The harness defines only:

- the P4-08 semantic-equivalent execution dimensions;
- stable matrix/run identity;
- the semantic trace fields that must compare equal;
- operational metadata that must not become world authority;
- a neutral external adapter boundary.

The harness does not reference production component DLLs/internal types.

## Commands

```text
dotnet run --project tools/MachiVerse.DeterminismHarness -- verify
dotnet run --project tools/MachiVerse.DeterminismHarness -- materialize <output-directory>
dotnet run --project tools/MachiVerse.DeterminismHarness -- compare <baseline-trace.json> <candidate-trace.json>
```

## P4-08 matrix

The standard matrix is the full cross product:

```text
worker count:              1, 4, 8, 16
process restart:           none, scenario-defined deterministic checkpoint
Gateway count:             1, 2, 4
Gateway route permutation: 4 variants
View subscribers:          0, 100
logging level:             warn, debug
telemetry exporter:        enabled, disabled, failing
```

This produces exactly:

```text
4 × 2 × 3 × 4 × 2 × 2 × 3 = 1152 runs
```

Run identity is derived only from those semantic-equivalent execution dimensions plus `perf.reference.v1`. Manifest enumeration order does not change the resulting matrix digest.

## Restart checkpoint rule

The Phase 4 design requires restart at deterministic checkpoints but does not authorize QA tooling to invent a new authoritative checkpoint Step.

For `restartMode = scenario-checkpoint`, the target scenario adapter must supply a stable `restartCheckpointToken` from its own canonical scenario contract. QA-02 only transports that token.

## Semantic trace comparator

Each committed Step contains four semantic digest families:

```text
stateDigest
terminalOperationDigest
transactionResultDigest
configHistoryDigest
```

All four must match for every committed Step across semantic-equivalent runs.

The trace validator requires a non-empty, contiguous, strictly ascending committed Step sequence. Missing, duplicate, or gapped Step traces fail closed.

The comparator reports the first semantic divergence by Step and digest family.

Operational data is explicitly non-semantic. Differences such as these do not fail the semantic comparator:

```text
elapsedMs
logDigest
telemetryDigest
traceId
```

This does not mean operational acceptance is ignored; performance/observability acceptance remains owned by its own TestCaseIds. It means operational timing/trace identity cannot change authoritative world results.

## External adapter

The materialized `target-adapter-contract.json` describes a JSONL boundary. A component-local or integration runner receives one matrix descriptor and returns a semantic trace without QA-02 linking to its runtime assembly.

The adapter must preserve `runId` and `profileId`. Exact component launch, restart, Gateway routing, View churn, telemetry setup, and persistence replay mechanics remain target-owned.

## Materialized outputs

`materialize` writes:

```text
run-matrix.json
matrix-summary.json
target-adapter-contract.json
semantic-trace-contract.json
acceptance-test-case-coverage.json
```

Generated output is reproducible test material, not a replacement for the source manifest or canonical design.

## Baseline policy

Do not regenerate or replace a semantic baseline merely to make a failure disappear. A semantic mismatch requires either an implementation fix or an explicit design/version amendment.
