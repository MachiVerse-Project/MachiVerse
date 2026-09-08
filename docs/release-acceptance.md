# Release acceptance runbook

Status: INT-03 implementation / P4-08 release gate

## Purpose

`tools/MachiVerse.ReleaseAcceptance` is the repository-level evaluator for the Phase 4 `ReleaseAcceptanceRecordV1` contract.

It deliberately separates two things:

1. **contract validation** — short CI can build the evaluator, run negative/self tests, and prove that missing or invalid evidence fails closed;
2. **release evidence collection** — the release candidate must provide real same-commit suite artifacts, full `perf.reference.v1`, `perf.persistence.v1`, `perf.publication.v1`, and the real `performance.soak.24h` result.

A PR workflow is never a substitute for 24 wall-clock hours of soak evidence.

## Source contracts

The evaluator follows:

- `docs/design/phase4-test-acceptance.md`
- `docs/design/phase4-test-acceptance-addendum.md`
- `docs/design/phase4-performance-benchmark-profile.md`
- `tests/performance-fixtures/v1/harness-manifest.json`
- `tests/release-acceptance-fixtures/v1/acceptance-manifest.json`

The INT-03 manifest pins the canonical QA-04 manifest SHA-256. Evaluation fails if the checkout's QA-04 profile contract does not match that pinned digest.

## Commands

Verify the release-gate contract and built-in negative cases:

```bash
dotnet run --project tools/MachiVerse.ReleaseAcceptance --configuration Release -- verify
```

Create an intentionally incomplete evidence template:

```bash
dotnet run --project tools/MachiVerse.ReleaseAcceptance --configuration Release -- template artifacts/release/evidence.json
```

Evaluate collected evidence and materialize `ReleaseAcceptanceRecordV1`. The expected commit must be the exact release-candidate source commit:

```bash
candidate_commit="$(git rev-parse HEAD)"
dotnet run --project tools/MachiVerse.ReleaseAcceptance --configuration Release -- \
  evaluate artifacts/release/evidence.json \
  artifacts/release/ReleaseAcceptanceRecordV1.json \
  "$candidate_commit"
```

Exit codes:

- `0`: `PASS`
- `2`: valid evidence document evaluated as `INCOMPLETE` or `FAIL`
- `1`: malformed contract/evidence or evaluator error

## Evidence binding

Every required suite/performance/soak artifact is bound to the same `sourceCommit` used by the release record. The evaluator additionally compares that source commit with the explicit expected release-candidate commit supplied on the command line. A mismatch is `INCOMPLETE`, not silently accepted.

Suite and report references are accompanied by lowercase SHA-256 digests so the evidence file identifies immutable artifact content rather than trusting a mutable path or URL alone.

The release evidence document carries:

- build/source identity;
- schema/algorithm/config registry digests;
- test-suite version and passed TestCaseIds;
- per-suite status, artifact reference, and artifact digest;
- performance profile report reference and report digest;
- 24-hour soak report reference and report digest;
- determinism digest summary;
- waivers and observed failure codes.

## Required suite evidence

`p4-08.v1` requires evidence for the standard component packages and repository acceptance layers:

- Simulation Core standard package;
- Gateway standard package;
- General View standard package;
- Administration View standard package;
- QA-01 contract suite;
- QA-02 determinism/replay suite;
- QA-03 crash/fuzz/security suite;
- QA-04 performance/soak contract suite;
- INT-01 single-Gateway E2E;
- INT-02 multi-Gateway failover/resync;
- P4-08 addendum, including protobuf schema and internal mTLS acceptance.

The manifest also pins critical TestCaseIds such as `protocol.view.slow-client`, `persistence.history.hash-chain`, `observability.audit.hash-chain`, `schema.protobuf.compile`, `schema.protobuf.registry-complete`, `security.internal-mtls.required`, and `determinism.protobuf-not-hash-source`.

## Performance evidence

A release candidate must provide passing reports for all three QA-04 profiles:

- `perf.reference.v1`
- `perf.persistence.v1`
- `perf.publication.v1`

Each performance entry must identify the exact candidate commit, immutable report reference/digest, pass state, and failure-code set.

The QA-04 repository workflow only validates the profile/adapter contracts. It does not generate release performance evidence.

## 24-hour soak evidence

`performance.soak.24h` must report at least `86400` wall-clock seconds and satisfy all pinned guards:

- parallel verifier digest matches;
- post-warmup memory growth does not exceed 10%;
- accepted Operation loss is zero;
- history/audit chains remain valid;
- no unrecoverable queue deadlock occurs.

The soak evidence must also bind the exact candidate commit and immutable report digest. A shorter run is always `INCOMPLETE` even when every observed metric is otherwise healthy.

## Waivers

Known waivers are recorded, but cannot override non-waivable authority/data-loss/security failures. The evaluator returns `FAIL` when a non-waivable failure is observed, even if a waiver for the same code is present.

Non-waivable classes include determinism divergence, accepted Operation loss/double apply, undetected history corruption, partial transaction commit, unauthorized mutation bypass, credential leakage, required protocol compatibility failure, and world mutation driven by View camera/telemetry/wall-clock races.

## Release completion

INT-03 and roadmap #69 are complete only when a real release candidate evidence set evaluates to `PASS` and the resulting `ReleaseAcceptanceRecordV1` is retained with the release artifacts. Until then the release gate remains open.
