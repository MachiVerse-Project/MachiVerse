# P4-04 amendment: `core.operation-state /2.0`

Status: normative for the Stage 2 canonical Snapshot/recovery implementation on PR #265.

## 1. Scope

The required Snapshot set remains exactly **97 domain sections + 6 Core sections = 103 logical sections**. No seventh Core section is introduced.

`core.operation-state` is upgraded from schema `core.operation-state /1.0` to `core.operation-state /2.0` so that the Core effect-custody Snapshot contains both durable Operation authority and persistent `CrossDomainTransactionStateV1` authority.

The exact protobuf transport schema is `docs/design/proto/phase4-core-operation-state-v2.proto`.

## 2. Logical items

A v2 fragment contains `basis_step` plus an ordered repeated `items` sequence. Each item is exactly one of:

1. `DurableOperationStateV1`;
2. `CrossDomainTransactionStateV1`.

Digest-only transaction records are forbidden. A decoder must reconstruct the full logical transaction state, including root causality, subjects, participants, participant intent IDs/effect digests, and invariant results.

## 3. Canonical ordering

Logical item order is independent of fragment boundaries.

1. All durable Operation items precede all CrossDomainTransaction items.
2. Durable Operation items are strictly ascending by `OperationId` bytes.
3. CrossDomainTransaction items are strictly ascending by `TransactionId` bytes.
4. Duplicate logical IDs in either arm are invalid.
5. A oneof with zero or multiple arms is invalid.

Nested order is the order enforced by the authoritative runtime constructors:

- transaction `SubjectIds`: ascending `OpaqueId128`;
- participants: standard `DomainRank`, then ASCII `DomainToken`, then ASCII `PartitionId`;
- participant `IntentIds`: ascending `OpaqueId128`;
- invariant results: ASCII `InvariantId`, then `Severity`;
- invariant `ParticipantRefs`: preserved exactly as authority material because `CrossDomainTransactionStateV1.CanonicalDigest()` includes that order.

A decoded value must successfully reconstruct the corresponding runtime authoritative type. Constructor validation is part of Snapshot validation.

## 4. Item count

`logical_item_count` for `core.operation-state /2.0` is:

`durable_operation_count + cross_domain_transaction_state_count`.

An empty section still emits one metadata fragment with `basis_step` and zero items.

## 5. Semantic authority

Protobuf bytes, compression, and fragment boundaries never enter semantic authority.

For a frozen v2 operation-state owner, define:

- `operation_digest = DurableOperationSubstateV1.Canonicalize(operations).CanonicalDigest`;
- `transaction_digest = CrossDomainTransactionStateSetV1(states)` canonical aggregate digest defined below.

The v2 section logical digest is:

`DomainHash("mv.core-operation-state.v2", DCBOR([operation_digest, transaction_digest]))`.

The canonical aggregate transaction digest is:

`DomainHash("mv.cross-domain-transaction-state-set.v1", DCBOR([state.CanonicalDigest() for state in TransactionId ascending order]))`.

This composition intentionally reuses each existing authority's semantic digest and does not hash protobuf bytes.

`WorldStateV1.OperationState` must use schema `core.operation-state /2.0` and this v2 logical digest once persistent CrossDomainTransaction authority is enabled. SQLite remains the durable live owner between Snapshots; the Snapshot owner cut freezes a same-Step copy of both authorities.

## 6. Lifecycle and Step consistency

For a Snapshot at `basis_step`:

- every durable Operation must satisfy the existing Operation authority invariants;
- every transaction must satisfy `CreatedStep <= UpdatedStep <= basis_step`;
- `ACTIVE` requires `TerminalStep = null`;
- terminal transactions require `TerminalStep = UpdatedStep`;
- no transaction may contain a causality `BasisStep` greater than `basis_step`;
- all tokens/enums/partition ownership/participant requiredness must validate through the production runtime types.

## 7. v1 compatibility

A `core.operation-state /1.0` section contains only durable Operation items. Recovery migrates it losslessly to the v2 logical model by:

- preserving every `DurableOperationStateV1` exactly;
- supplying an empty CrossDomainTransaction state set;
- recomputing the v2 section digest from the preserved Operation authority plus the canonical empty transaction-set digest.

No v2 Snapshot may be emitted with schema `/1.0` after CrossDomainTransaction persistent authority is enabled.

Unknown v2 fields, duplicate singular fields, malformed IDs/digests, undefined enums, noncanonical ordering, invalid partition ownership, invalid transaction-kind participant contracts, and Step inconsistencies fail closed.

## 8. Recovery reconstruction

Recovery of v2 must produce both:

1. durable Operation authority;
2. persistent CrossDomainTransaction authority.

After transaction recovery, `detail.guard.active-transaction` is **derived**, not independently trusted from transaction state. The detail directory recovered from `core.detail-directory` must be validated/rebuilt against recovered ACTIVE transaction subjects using the canonical tile → detail-region authority. A mismatch fails recovery.

## 9. Acceptance

This amendment is implemented only when all of the following are green:

- v2 strict wire round-trip and malformed/unknown-field negatives;
- `/1.0 -> /2.0` lossless migration smoke;
- same-Step frozen owner recomputation;
- exact six-Core section registry still contains six sections;
- Snapshot write/read/reassembly recovers transaction state and reproduces the v2 logical digest;
- detail guard reconstruction from recovered ACTIVE transactions matches the recovered detail authority;
- existing v1 Snapshot recovery remains compatible.
