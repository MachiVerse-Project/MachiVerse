# Phase 4 QA-04 cross-run determinism evidence report binding

Status: Decided implementation contract
Tracking: #240
Implementation: Draft PR #265
Applies to: `perf.reference.v1`

## 1. Purpose

P4-06 requires deterministic equality across all worker-count / process-run combinations for more than the final State digest. The release evidence runner previously compared only `final_state_digest`, which is insufficient to prove the full P4-06 determinism gate.

This contract adds an implementation evidence envelope to a successful `performance-benchmark-report-v1`. It does not change world state, Simulation semantics, or the P4-06 load profile.

## 2. Required evidence on a successful benchmark response

A `perf.reference.v1` benchmark response with `passed = true` and no target/report failure code MUST contain:

```text
determinism_evidence {
  final_state_digest,
  transition_committed_digest,
  operation_terminal_semantic_digest,
  config_history_digest,
  promotion_deferral_order_digest
}
```

Each value is exactly 32 bytes represented as 64 lowercase hexadecimal characters and MUST NOT be all zero.

`determinism_evidence.final_state_digest` MUST equal the report's existing `final_state_digest` field.

A failed/incomplete benchmark response is already release-ineligible and is not required to fabricate determinism evidence.

## 3. Cross-run acceptance

When all 12 canonical benchmark runs are target-successful, the release evidence runner MUST require exact equality of all five evidence digests across:

```text
worker_count = 1,4,8,16
run_ordinal = 1,2,3
```

Any missing field, malformed digest, or cross-run mismatch fails the evidence run closed.

## 4. Determinism digest summary

After all 12 rows pass equality validation, the release runner computes the diagnostic summary digest as SHA-256 over UTF-8 bytes:

```text
qa04.determinism-evidence.v1\n
<run_id>\0<final_state_digest>\0<transition_committed_digest>\0<operation_terminal_semantic_digest>\0<config_history_digest>\0<promotion_deferral_order_digest>\n
...
```

Rows are sorted by `run_id` using ordinal string order. The trailing newline is present for every row.

The resulting lowercase SHA-256 is bound into both `perf.reference.v1.aggregate.json` and `qa04-evidence-fragment.json`. The aggregate artifact digest in the fragment is recomputed after rebinding.

## 5. Boundary

This contract does not make the current QA-04 target release-capable. Until the canonical reference world, authoritative full Step loop, persistence/publication profiles, and soak are complete, target responses continue to fail closed. This contract only prevents a future successful target from proving determinism with final State alone.
