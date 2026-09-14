# Alpha 1.1 Society HistoryLineage authority proposal

Status: **Review-only / explicit project-owner approval required**

Tracking: #240
Implementation target: #265

## Purpose

This document proposes the exact benchmark-only authority needed to materialize the remaining `society.history_lineage` 19,800-record slice for `perf.reference.v1` Alpha 1.1.

Nothing in the current schema chooses a Society history subject population, history-kind vocabulary, genesis predecessor set, or `causality_digest` recipe. The choices below are therefore a new explicit benchmark proposal. They must not be implemented, synchronized to `develop`, or counted as accepted authority before project-owner approval.

## Audit result

The existing contracts establish only the following:

- `society.history_lineage` payload fields are `subject_ref`, `history_kind`, `parent_refs`, `basis_step`, and `causality_digest`;
- the Phase 4 payload rule requires history refs to remain canonical;
- the required derived index is `society.history-by-subject`;
- the QA-04 Society/Governance decomposition assigns local ordinals `0..19,799` to global ordinals `1,580,200..1,599,999`, with ordinary descriptor identity and D2 detail;
- the already-approved Society Employment production authority materializes 80,000 actual `society.employment` records and therefore provides a closed, proven Society-owned subject population large enough for this slice.

Existing lineage/history authorities do **not** supply a reusable Society rule. In particular, Environment D0 lineage hashes subject identity using the Environment-specific domain `mv.perf-reference-environment-lineage-source.v1`, while Environment D1 uses `mv.perf-reference-environment-d1-source.v1`. Participation history also has a different payload shape. Those domain-specific rules are not authority for Society.

## Proposed H1 — subject population

For local HistoryLineage ordinal `h = 0..19,799`:

```text
subject_ref = SocietyEmployment[h]
```

`SocietyEmployment[h]` means the actual production-materialized record from the already-approved `Qa04SocietyEmploymentCanonicalAuthorityV1` ordinal `h`, not a descriptor-only Ref.

Proposed benchmark meaning:

- the first 19,800 accepted Employment records receive one genesis HistoryLineage record each;
- the mapping is one-to-one by local ordinal;
- all 19,800 subject refs are distinct;
- this does not assert that Employment is the only Society object that may have history;
- it does not define history records for Employment ordinals `19,800..79,999` or for other Society partitions.

This choice is proposed because it closes entirely over already-proven Society production authority without inventing a new cross-domain subject class. It is still a semantic choice and therefore requires explicit approval.

## Proposed H2 — history kind

For every record:

```text
history_kind = perf.genesis-employment-history
```

This token means only: “Alpha 1.1 benchmark genesis history marker for the selected Employment subject.”

It is not a general Society history taxonomy and does not define lifecycle-event, legal-event, transaction-event, production-event, or causal-event classes.

## Proposed H3 — genesis predecessor set and basis step

For every record:

```text
parent_refs = []
basis_step  = 0
```

The empty parent set means the record is the benchmark genesis history marker and has no predecessor HistoryLineage record.

It must not be interpreted as evidence that the represented Employment has no conceptual antecedents outside this benchmark fixture. No predecessor record is fabricated merely to make a chain non-empty.

## Proposed H4 — causality digest

A new Society-specific, benchmark-only genesis digest recipe is proposed. For the proposed payload values above:

```text
causality_digest = HashSuite.DomainHash(
  "mv.perf-reference-society-history-genesis.v1",
  canonical array [
    subject_ref.partition_id ASCII text,
    subject_ref.record_id 16-byte canonical bytes,
    history_kind ASCII text,
    empty parent-ref array,
    basis_step unsigned integer
  ])
```

Equivalent implementation shape:

```csharp
HashSuite.DomainHash("mv.perf-reference-society-history-genesis.v1", writer =>
{
    writer.WriteArrayStart(5);
    writer.WriteAsciiText(subjectRef.PartitionId.Value);
    writer.WriteBytes(subjectRef.RecordId.ToBytes());
    writer.WriteAsciiText(historyKind.Value);
    writer.WriteArrayStart(0);
    writer.WriteUnsigned(basisStep);
});
```

Required exact constants:

```text
domain       = mv.perf-reference-society-history-genesis.v1
history_kind = perf.genesis-employment-history
basis_step   = 0
parent_refs  = []
```

Proposed meaning and boundary:

- the digest commits deterministically to the benchmark genesis subject binding and exact genesis fields;
- it is not a hash of an external event log, transaction, legal instrument, causal graph, or scheduler trace;
- it does not define the digest rule for non-genesis Society history records;
- it does not reuse Environment, Participation, Governance, Infrastructure, or FinanceAccount digest semantics.

## Proposed H5 — record envelope

Use only the existing QA-04 Society/Governance descriptor for local ordinal `h`:

```text
record_id    = existing society.history_lineage descriptor RecordId for h
revision     = 1
created_step = 0
retired_step = NONE
detail_level = D2
lineage_ref  = NONE
```

No specialized identity or additional lineage identity recipe is introduced.

## Proposed full mapping

For local ordinal `h = 0..19,799`:

```text
subject_ref       = SocietyEmployment[h]
history_kind      = perf.genesis-employment-history
parent_refs       = []
basis_step        = 0
causality_digest  = DomainHash("mv.perf-reference-society-history-genesis.v1", exact H4 encoding)
```

Envelope is H5 above.

## Required derived index

Rebuild the standard derived index:

```text
society.history-by-subject
```

Required benchmark cardinality if this proposal is approved:

```text
19,800 keys
1 HistoryLineage record per selected Employment subject
19,800 total indexed records
```

## Mandatory production proof if approved

Accepted accounting must remain unchanged until #265 proves all of the following on one current head:

1. exactly 19,800 `society.history_lineage` records through the production payload validator;
2. exact descriptor slice `1,580,200..1,599,999`, local ordinal binding, and H5 envelope on every record;
3. upstream `Qa04SocietyEmploymentCanonicalAuthorityV1` production materialization is executed and all selected subject refs close to actual Employment records `0..19,799`;
4. exactly 19,800 distinct selected Employment subjects and exact one-to-one ordinal mapping;
5. exact `perf.genesis-employment-history` Token on all records;
6. exact empty `parent_refs` and `basis_step=0` on all records;
7. recomputation of every H4 digest from the exact selected subject and genesis fields, with 32-byte equality on every record;
8. `society.history-by-subject` rebuilt with exactly 19,800 keys x1;
9. full 19,800-record Snapshot encode / recovery / semantic rehash;
10. fail-closed negatives for missing Employment authority, wrong Employment ordinal, wrong subject partition, subject duplication, history-kind drift, parent injection, basis-step drift, digest mutation, digest computed for a different subject, duplicate RecordId, population drift, and index cardinality drift;
11. generic Simulation Core smoke including the HistoryLineage authority proof;
12. full current-head CI with failure 0.

Descriptor-only subject refs, exists-everywhere resolvers, Environment digest recipes, or synthetic predecessor records are forbidden substitutions.

## Acceptance accounting gate

Before approval and before successful production proof, accounting remains:

```text
Society/Governance accepted  = 1,651,200 / 2,000,000
Society/Governance remaining =   348,800
Society remaining            =    89,800
Governance remaining         =   259,000
```

Only if this proposal is explicitly approved and every mandatory proof above succeeds:

```text
Society/Governance accepted  = 1,671,000 / 2,000,000
Society/Governance remaining =   329,000
Society remaining            =    70,000
Governance remaining         =   259,000
```

All Infrastructure, reference-world, workload, Operation, and release flags remain unchanged.

## Remaining Society audit after this proposal

This proposal does not resolve the other two remaining Society slices:

### `society.business_production` — 30,000

The persistent payload uses `input_refs` / `output_refs`, while the existing deterministic production recipe mechanics identify recipe materials by Tokens. No approved authority currently maps those persistent Refs to runtime material Tokens, nor fixes the recipe/input/output/work/energy genesis semantics. Those values must not be inferred.

### `society.logistics_obligation` — 40,000

The persistent contract requires shipper, consignee, cargo refs, quantity, origin, destination, status, and optional carrier/due step. The Phase 4 rule requires cargo identity conservation, but no approved benchmark authority currently selects those endpoint/cargo/location populations or their genesis obligation semantics. Those values must not be inferred.

## Explicit non-decisions

Approval of this proposal, if granted, would not decide:

- a general Society history ontology;
- non-genesis predecessor or branching semantics;
- update/append rules after Step 0;
- retention, archival, pruning, or compaction;
- causal relationships to transactions, claims, law, production, logistics, or scheduler events;
- a universal causality-digest protocol outside this exact genesis package;
- history for the remaining Employment records or other Society partitions;
- `society.business_production` or `society.logistics_obligation`;
- Governance or Infrastructure lineage semantics.
