# Alpha 1.1 — Core effect custody / active CrossDomainTransaction authority v2

Status: Decided normative design / implementation pending  
Tracking: #240  
Parent: `phase4-alpha11-normative-closure.md`

## 1. Authority owner

Active CrossDomainTransactionはsingle Domainへ押し込まずCore effect-custody authorityとする。

Exact-103 Snapshotを維持するためsectionを増やさない。既存section id:

```text
core.operation-state
```

をlogical schema 2.0へ上げる。

```text
CoreEffectCustodyStateV2 {
  operations: ordered list<DurableOperationStateV1>
  cross_domain_transactions: ordered list<CrossDomainTransactionStateV1>
}
```

Section idは互換性のため変更しないが、schema/versionはmanifest/fragment wireで明示する。

## 2. Persistent transaction state

```text
CrossDomainTransactionStateV1 {
  transaction_id: Id128
  transaction_kind: Token
  lifecycle: TransactionLifecycleV1
  created_step: Step
  updated_step: Step
  terminal_step: Step?
  root_causality: CausalityRefV1
  subject_ids: ordered list<Id128>
  participants: ordered list<PersistentTransactionParticipantV1>
  invariant_results: ordered list<InvariantResultV1>
}

TransactionLifecycleV1 := ACTIVE | COMMITTED | ABORTED

PersistentTransactionParticipantV1 {
  domain_token: Token
  partition_id: Token
  intent_ids: ordered list<Id128>
  required: bool
  outcome: READY | FAILED
  candidate_effect_digest: Digest
  diagnostic_code: Token?
}
```

Important: `state_digest` is **not a field inside the logical state**. It is computed from the normalized state to avoid self-referential hashing and is stored alongside the wire material in SQLite/Snapshot descriptors.

Rules:

- transaction_id non-zero / unique
- transaction_kind must be registered in `CrossDomainTransactionKindRegistryV1`
- ACTIVE => terminal_step NONE
- COMMITTED/ABORTED => terminal_step present and `terminal_step >= created_step`
- `updated_step >= created_step`
- subjects RecordId ascending / duplicate禁止
- participant order = standard domain rank, domain token, partition id
- intent IDs ascending / duplicate禁止
- invariant order = invariant id, severity
- participant requiredness must match registered transaction kind semantics

Candidate `CrossDomainTransactionCandidateV1.IsAuthoritative` remains `false`。candidateのVALID状態はpersistent ACTIVE/terminal authorityそのものではない。

## 3. Candidate -> persistent transition

A VALID candidate can produce one state change at durable transition COMMIT:

```text
NONE -> ACTIVE
ACTIVE -> ACTIVE       // participant/evidence refresh if semantic event requires
ACTIVE -> COMMITTED
ACTIVE -> ABORTED
```

Forbidden:

```text
COMMITTED -> ACTIVE
ABORTED -> ACTIVE
COMMITTED <-> ABORTED
NONE -> COMMITTED/ABORTED without an explicit direct-terminal history extension
```

Alpha 1.1 benchmark uses only `NONE->ACTIVE` and `ACTIVE->COMMITTED`.

`ACTIVE->ACTIVE` must preserve transaction id/kind/created_step/root causality/subjects and may only update participant/invariant material plus updated_step.

## 4. Canonical digest

```text
transaction_state_digest = HashSuite.DomainHash(
  "mv.cross-domain-transaction-state.v1",
  normalized CrossDomainTransactionStateV1)
```

Normalized field order is the declaration order in §2. No process-local cache, candidate object identity or wall clock is included.

Core effect-custody section digest hashes:

```text
[operations canonical state, transactions sorted transaction_id]
```

## 5. SQLite authority

New table:

```text
cross_domain_transaction_state(
  transaction_id BLOB PRIMARY KEY CHECK(length(transaction_id)=16),
  lifecycle INTEGER NOT NULL,
  created_step BLOB NOT NULL CHECK(length(created_step)=8),
  updated_step BLOB NOT NULL CHECK(length(updated_step)=8),
  terminal_step BLOB NULL,
  state_wire BLOB NOT NULL,
  state_digest BLOB NOT NULL CHECK(length(state_digest)=32)
)
```

All Step values are U64BE. `terminal_step` length=8 when present。

Secondary index:

```text
(lifecycle, updated_step, transaction_id)
```

is derived/rebuildable. Terminal rows are retained through Alpha 1.1 logical-history retention; no automatic tombstone deletion.

`operation_state` remains Operation-only. Transaction rows must never be encoded as fake Operations.

## 6. Transition history v2

Keep record type token:

```text
transition.committed.v1
```

for semantic-family compatibility, but introduce payload schema:

```text
persistence.transition-committed / 2.0
```

All v1 fields retain their exact meaning/order. Add required:

```text
transaction_state_changes: ordered list<TransactionStateChangeWireV1>
```

Empty list is valid.

```text
TransactionStateChangeWireV1 {
  transaction_id: Id128
  before_state_digest: Digest?
  after_state: CrossDomainTransactionStateWireV1
  after_state_digest: Digest
}
```

Rules:

- sorted transaction_id ascending / duplicates forbidden
- create: before digest absent
- update/terminalize: before digest required and exact-match current SQLite authority
- after digest recomputed from decoded state and exact-match required
- each after state transaction_id equals change transaction_id

## 7. Atomicity

One authoritative transition SQLite transaction commits all of:

1. `transition.committed.v1` history row with payload v2;
2. terminal Operation rows;
3. scheduled-operation removals;
4. cross_domain_transaction_state changes;
5. config state changes when applicable;
6. finalized step / continuity token / history anchor.

A failure in any item rolls back all. No transaction state may become observable before the matching transition history/continuity commit.

## 8. Snapshot v2

`core.operation-state /2.0` fragment logical payload:

```text
basis_step
operations[]
cross_domain_transactions[]
```

Operation encoding reuses current canonical Operation state wire. Transaction encoding is the exact state in §2 plus independently carried/recomputed state digest.

Canonical order:

```text
operations by OperationId
transactions by TransactionId
```

v1 Snapshot decode maps:

```text
operations = existing v1 operations
cross_domain_transactions = []
```

No active transaction may be invented during this migration. If a history range proves active transaction semantics are required but the Snapshot/v1 history cannot reconstruct them, recovery fails closed.

## 9. Recovery

1. restore Snapshot state;
2. verify every transaction state digest;
3. replay post-anchor history sequence;
4. for transition payload v2 apply transaction changes in transaction_id order;
5. require before-state digest exact match;
6. recompute Core effect-custody digest;
7. reconstruct active-transaction detail guard reference counts;
8. compare reconstructed guard set with recovered `core.detail-state`;
9. mismatch => recovery rejection.

## 10. `perf.reference.v1` active set

Initial active set exactly 10,000, created_step=updated_step=0, terminal_step=NONE。

Initial cohort:

```text
cohort = ordinal % 10
retirement transition basis step = 300 * (cohort + 1)
```

At every `S != 0 && S % 300 == 0`:

- due cohort 1,000 ACTIVE records -> COMMITTED, terminal_step=S+1;
- create exactly 1,000 replacement ACTIVE records, created_step=updated_step=S+1;
- resulting authoritative active count = exactly 10,000.

Replacement generation `g` is the number of completed lifetimes for that slot. Stable slot ordinal stays `0..9999`.

Replacement transaction identity derives from:

```text
world_id
mapped transaction kind
basis step S
root causality = prior transaction id
subject ids
stable local ordinal = slot + g*10000
```

Each replacement lifetime is 3,000 Steps.

## 11. Detail guard reference counts

For every ACTIVE transaction subject:

```text
tile = Qa04ReferenceLoadV1.RegionalTileIndex(subject_id)
region = canonical tile detail region(tile)
```

Maintain deterministic reference count per detail region. `detail.guard.active-transaction` exists iff count>0.

Turnover applies decrement for terminalized states and increment for replacements in the same Step transition before resulting detail-state digest is prepared. Counts themselves are derived from authoritative ACTIVE transactions and are not a second authority.

## 12. Blocker removal

`qa04.material.cross-domain-transaction-authority-undefined` remains until:

- runtime persistent state implementation
- SQLite table + atomic transition v2
- Core Snapshot section v2
- v1 compatibility reader
- recovery/replay
- detail guard reconstruction
- exact10k initial material + turnover
- exact103 semantic rehash

all pass production-path tests.
