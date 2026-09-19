# Phase 4 QA-04 Gate4 Step2 compact batch clarification

Status: Normative clarification for `phase4-qa04-gate4-step2-determinism-authority.md` §§4.1-4.2 and §9.2  
Tracking: #240, #371, #372

## 1. Precedence

This document replaces the physical-retention wording in §§4.1-4.2 and the accumulator append granularity in §9.2 of the parent Step2 authority document wherever they differ.

The semantic requirement remains unchanged: every canonical `perf.reference.v1` generated Operation, its immutable scheduling identity, and its terminal result participate in deterministic authority. The clarification only removes redundant physical expansion that is exactly reconstructible from the canonical generator.

## 2. Scheduled batch semantic digest

For one injection Step, let the generated Operations be bound and sorted by:

```text
SameStepOrderKey ASC, then OperationId bytewise ASC
```

Define the normalized scheduled batch semantic value as:

```text
array [
  array [
    operation_id bytes16,
    payload_digest bytes32,
    same_step_order_key bytes55
  ], ...
]
```

The array has exactly `OperationCountForStep(injection_step)` items and no duplicate OperationId.

The semantic digest is:

```text
scheduled_batch_digest =
  DomainHash("mv.qa04-operation-scheduled-batch.v1", normalized_array)
```

The implementation MUST compute the digest over every ordered item. It MAY stream the canonical encoding directly into the hash implementation and is not required to allocate the entire array at once.

## 3. Compact scheduled history record

The authoritative `qa04.operation-batch.scheduled.v1` normalized payload is compact:

```text
map {
  0: "perf.reference.v1",
  1: injection_step uint64,
  2: effective_step uint64,
  3: operation_count uint64,
  4: scheduled_batch_digest bytes32
}
```

The physical history payload MUST NOT contain the full generated tuple array merely for retention. The full tuple set is exactly reconstructible from `(profile_id, injection_step)` plus the canonical binding contract; on recovery or retry, Core regenerates the ordered tuple set and recomputes `scheduled_batch_digest` before accepting the compact record.

Thus the phrase “persist ordered OperationId + immutable payload digest + effective Step + SameStepOrderKey facts for every Operation” in the parent §4.1 means **persist their complete semantic commitment**, not one physical row/item per generated Operation.

A recovered compact batch is valid only if regenerated cardinality and digest exactly match the stored fields. A mismatch or generator collision fails closed.

## 4. Terminal batch semantic digest

For one effective Step, terminal outcomes use the same canonical applied-operation order. Define the normalized terminal batch value as:

```text
array [
  array [
    operation_id bytes16,
    terminal_status uint,
    result_code ASCII StableToken,
    [] | [rich_result_payload bytes]
  ], ...
]
```

The terminal batch digest is:

```text
terminal_batch_digest =
  DomainHash("mv.qa04-operation-terminal-batch.v1", normalized_array)
```

Every terminal Operation participates exactly once in this array. The implementation MAY stream the encoding into the digest.

For the canonical benchmark, terminal cardinality for the Step MUST equal scheduled cardinality and OperationIds MUST match position-by-position.

## 5. `operation_terminal_semantic_digest` accumulator

The run-level accumulator is transition-granular rather than per-Operation-hash-granular.

Genesis remains:

```text
D0 = DomainHash("mv.qa04-operation-terminal.v1.genesis", array [])
```

For transition ordinal `n = 1..27000`:

```text
item_digest = DomainHash("mv.qa04-operation-terminal-step.v1",
  array [
    effective_step uint64,
    operation_count uint64,
    terminal_batch_digest bytes32
  ])

Dn = DomainHash("mv.qa04-operation-terminal.v1.append",
  array [ D(n-1) bytes32, n uint64, item_digest bytes32 ])
```

Expected accumulator append count is exactly 27,000.

Separately, the run maintains an exact checked `terminal_operation_count` sum. The final count for injection Steps `0..26999` MUST equal **136,450,000**. Therefore changing the append granularity does not permit Operations to be omitted: each Step digest commits the ordered complete terminal array and the exact cardinality is independently verified.

This definition is the canonical meaning of `operation_terminal_semantic_digest` for Gate4 Step2.

## 6. Recovery and retry

For a compact scheduled or terminal batch, validation regenerates the canonical descriptor/binding order for that Step and recomputes the corresponding batch digest. No probabilistic membership structure is authoritative.

A cache/index MAY accelerate lookup but remains non-authoritative. The canonical generator coordinate and the committed batch digest are the source of truth.

## 7. Non-effects

This clarification does not change:

- workload counts or burst cadence;
- OperationId/payload derivation;
- `SameStepOrderKey` ordering;
- domain execution or typed mutation semantics;
- terminal status/result semantics;
- Step numbering;
- the final five-digest Step2 evidence tuple;
- ordinary external Operation persistence outside the closed QA-04 generated set;
- `releaseEvidenceCapable=false` during Step2.