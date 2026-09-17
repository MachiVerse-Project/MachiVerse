# QA-04 performance / soak harness v1

This directory is the version-controlled orchestration contract for Phase 4 `QA-04`.

## Canonical sources

- `docs/design/phase4-performance-benchmark-profile.md`
- `docs/design/phase4-test-acceptance.md`
- `docs/design/phase4-test-acceptance-addendum.md`
- `docs/design/phase4-implementation-work-breakdown.md`

`harness-manifest.json` fixes the `perf.reference.v1` seed, reference-world load, worker/process repetition matrix, performance thresholds, persistence/publication stress subprofiles, `performance.soak.24h`, report fields, and external target-adapter boundary.

## Commands

```text
dotnet run --project tools/MachiVerse.PerformanceHarness -- verify
dotnet run --project tools/MachiVerse.PerformanceHarness -- materialize <output-directory>
```

`verify` validates the versioned profile and its self-tests. `materialize` emits machine-readable execution artifacts for component-local or integration runners.

## Execution boundary

The repository harness is an orchestrator/contract validator. It does not load production component DLLs or internal types, and it does not invent world semantics.

The real `perf.reference.v1` runs, persistence/publication stress runs, and 24 wall-clock hour soak are executed by target adapters against the assembled runtime. A short CI validation must never be reported as the 24h acceptance result.

Target adapters must preserve the canonical seed/config/history and return reports matching the materialized contracts. Wall-clock timing is diagnostic only and must not become authoritative world input.

## Release handoff

`INT-03` consumes QA-04 execution artifacts and real target reports. A release remains failed when performance/soak acceptance fails; reducing simulation semantics or solver work to obtain a passing number is not an allowed remediation.
