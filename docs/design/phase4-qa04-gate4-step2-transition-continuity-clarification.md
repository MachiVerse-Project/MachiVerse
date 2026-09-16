# Phase 4 QA-04 Gate4 Step2 transition continuity clarification

Status: Normative clarification for `phase4-qa04-gate4-step2-determinism-authority.md` §6.3  
Tracking: #240, #373

## 1. Precedence

This document replaces §6.3 `Transition normalized payload` of `phase4-qa04-gate4-step2-determinism-authority.md` wherever the two texts differ.

The clarification is required because the existing canonical continuity rule computes the resulting State continuity token from the `transition.committed.v1` history `RecordDigest`. Therefore the resulting continuity token cannot also participate in the normalized semantic payload from which that same `RecordDigest` is derived.

`HistoryRecordMaterial` already separates physical `payload_bytes` from `normalized_payload_bytes`; this clarification uses that existing separation and does not change the history digest or continuity algorithms.

## 2. Transition semantic body

The normalized semantic payload used by `HistoryRecordMaterial.RecordDigest` for `transition.committed.v1` is the **semantic body** below:

```text
map {
  0: effective_step uint64,
  1: resulting_step uint64,
  2: active_config_generation uint64,
  3: active_config_digest bytes32,
  4: applied_operation_ids [bytes16...],
  5: operation_outcomes [OperationOutcome...],
  6: previous_state_continuity_token bytes32,
  7: state_diagnostic_hash bytes32,
  8: partition_digests [PartitionDigest...]
}
```

The `OperationOutcome` and `PartitionDigest` normalized items and ordering rules are those in §§6.1-6.2 of the parent Step2 authority document.

The semantic body MUST be derived from the same candidate/finalization material used by the SQLite transition transaction. A parallel evidence-only body is forbidden.

The history material is then constructed using:

```text
normalized_payload_bytes  = canonical MV-DCBOR semantic body above
normalized_payload_digest = Hash256(normalized_payload_bytes)
record_digest             = existing mv.history-record.v1 rule
```

## 3. Resulting continuity derivation

After `record_digest` is known, the resulting continuity token is computed by the existing canonical rule:

```text
resulting_state_continuity_token =
  HistoryIntegrity.ComputeTransitionContinuityToken(
    world_id,
    resulting_step,
    previous_state_continuity_token,
    record_digest)
```

No fixed-point search, iterative hashing, truncation, worker-dependent salt, or alternate continuity function is permitted.

## 4. Physical transition wire payload

The physical `payload_bytes` stored for `transition.committed.v1` MAY and, for the QA-04 production path, MUST carry the complete recoverable wrapper:

```text
map {
  0: effective_step uint64,
  1: resulting_step uint64,
  2: active_config_generation uint64,
  3: active_config_digest bytes32,
  4: applied_operation_ids [bytes16...],
  5: operation_outcomes [OperationOutcome...],
  6: previous_state_continuity_token bytes32,
  7: resulting_state_continuity_token bytes32,
  8: state_diagnostic_hash bytes32,
  9: partition_digests [PartitionDigest...]
}
```

Thus the outer physical wire remains compatible with the complete field set already described by the persistence design, while history hashing remains acyclic.

A decoder MUST reconstruct the semantic body from this physical wrapper by removing field `7` and re-keying physical fields `8 -> semantic 7` and `9 -> semantic 8` exactly as defined above. It then MUST recompute:

1. `normalized_payload_digest`;
2. `record_digest` from sequence / previous record digest / record type / semantic body;
3. `resulting_state_continuity_token` from that `record_digest`.

The stored physical field `7` is accepted only if it equals the recomputed resulting continuity token.

## 5. Persistence cross-checks before COMMIT

Before the SQLite COMMIT, persistence MUST verify all of the following against the same transaction material:

- every Operation outcome equals the terminal mutation being committed;
- active Config generation/digest equal the Config authority being committed;
- previous continuity equals the pre-transition recovery head;
- the semantic-body `record_digest` is the exact transition history digest to be inserted;
- resulting continuity equals the existing continuity function applied to that exact record digest;
- state diagnostic hash equals the resulting authoritative State diagnostic digest;
- partition digests equal the committed resulting partition candidates.

Any disagreement fails closed before authoritative Step advancement.

## 6. Determinism evidence binding

For `transition_committed_digest`, the `item_digest` is the transition history material's `normalized_payload_digest`, i.e. the digest of the 9-field semantic body in §2, **not** a digest of the 10-field physical wrapper.

This makes the aggregate independent of physical serialization while keeping the complete resulting continuity value recoverable and validated.

All 12 Step2 runs MUST therefore compare the same acyclic semantic authority.