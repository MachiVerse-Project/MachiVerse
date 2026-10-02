# Alpha 1.1 — `perf.reference.v1` Environment lineage authority

Status: Decided normative design / implementation pending  
Tracking: #240  
Implementation: Draft PR #265  
Parent: `phase4-alpha11-reference-world-decomposition.md`

## 1. Purpose

The reference-world decomposition fixes 10,000 D0 and 2,500 D1 records in `environment.environment_lineage`, but previously left `subject_ref` selection implicit. This document fixes the exact subject and parent authority without changing record counts or the standard Environment schema.

Lineage records describe provenance of Environment material. They never use a lineage record itself as `subject_ref`.

## 2. Eligible subject pool

For detail level `L` (`D0` or `D1`) and regional tile `t`, the eligible pool is every canonical Environment record at detail level `L` whose:

- partition is one of the first 12 Environment partitions in the canonical decomposition (all partitions except `environment.environment_lineage`), and
- descriptor `RegionalTileIndex == t`.

The pool is sorted canonically by:

```text
partition_id ASCII ascending
record_id ascending
```

The pool must be non-empty for every tile used by a lineage descriptor. Empty pool is a materialization failure.

## 3. D0 lineage subject

For D0 lineage partition-local ordinal `j`:

```text
t = lineage descriptor RegionalTileIndex
pool = EligibleSubjects(D0, t)
subject = pool[j mod pool.Count]
```

Genesis D0 lineage payload:

```text
subject_ref          = subject
parent_refs          = []
generation           = 1
materialization_kind = perf.genesis
source_digest         = HashSuite.DomainHash(
                          "mv.perf-reference-environment-lineage-source.v1",
                          MV-DCBOR [subject.partition_id, subject.record_id])
```

The source digest commits to canonical subject identity. It does not duplicate or substitute the subject record's semantic digest.

## 4. D1 lineage subject

For D1 lineage partition-local ordinal `j`:

```text
t = D1 lineage descriptor RegionalTileIndex
pool = EligibleSubjects(D1, t)
subject = pool[j mod pool.Count]
```

The four source D0 lineage records are the existing same-partition decomposition sources. D1 lineage payload is:

```text
subject_ref          = subject
parent_refs          = Ref(environment.environment_lineage, each source D0 lineage RecordId)
                       sorted canonically by Ref
generation           = max(source lineage generation) + 1
materialization_kind = perf.aggregate-d1
source_digest         = hash(canonical source lineage semantic digests)
```

This preserves the already-decided four-to-one aggregate provenance while making the aggregate's own subject independent of source enumeration order.

## 5. Invariants

- `subject_ref` never targets `environment.environment_lineage`.
- D0 `parent_refs` is empty.
- D1 `parent_refs` contains exactly four distinct canonical D0 lineage refs.
- Subject selection depends only on canonical descriptors and partition/RecordId ordering.
- D0 and D1 subjects stay within the lineage descriptor's own regional tile.
- D1 source refs stay within `environment.environment_lineage` and are the exact four decomposition sources.
- no ZERO, missing, retired-at-genesis, or synthetic refs are accepted.

## 6. Acceptance

The Environment lineage binding subdependencies may be removed only after production helpers and smoke prove:

- all 10,000 D0 lineage descriptors resolve an actual non-lineage D0 subject;
- all 2,500 D1 lineage descriptors resolve an actual non-lineage D1 subject;
- all D1 lineage parent sets are exact four-source closure;
- selection is deterministic under reversed/shuffled candidate enumeration;
- invalid/empty/foreign candidate pools fail closed.

Environment D0/D1 parent world blockers remain until full canonical materialization, Ref closure, Snapshot/recovery and semantic rehash evidence are complete.