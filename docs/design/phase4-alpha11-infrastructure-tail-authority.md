# Alpha 1.1 / INT-03 — Remaining Infrastructure/Information benchmark authority

Status: **Normative benchmark authority**  
Tracking: #240  
Implementation: Draft PR #265

## 1. Scope

This document closes the final 19,900 records of the `perf.reference.v1` Infrastructure/Information 500,000-record decomposition:

- `information.address_place_index` 5,000
- `infrastructure.failure_recovery` 10,000
- `infrastructure.lineage` 4,900

The rules below are benchmark-only. They do not define a universal postal/address system, real-world failure model, or general lineage policy.

## 2. Upstream authority

This package depends on the already-approved and production-proven QA-04 authorities:

- FacilityService package and its 15,000 `built.structure` facility identities;
- the actual 55,000-record Infrastructure service pool;
- actual regional TileScope records;
- existing Infrastructure reference decomposition descriptor identities.

No PhysicalPresence identity is reused as a place/facility identity.

## 3. `information.address_place_index` 5,000

Use the existing slice at global Infrastructure ordinals `480,100..485,099`. RecordId is the existing descriptor RecordId.

For local ordinal `a = 0..4,999`:

```text
place_ref     = BuiltStructure[a]
address_token = perf.address.<zero-padded 5-digit a>
scope_ref     = TileScope(PhysicalPresence[a].regional_tile_index)
valid_from    = 0
valid_until   = NONE
aliases       = []
```

`address_token` is only a deterministic benchmark locator token. It is not a postal address, civic address, or user-facing string.

Envelope:

```text
revision      = 1
created_step  = 0
retired_step  = NONE
detail_level  = descriptor D2
lineage_ref   = NONE
```

The relation MUST close `AddressPlaceIndex -> BuiltStructure` and `AddressPlaceIndex -> actual TileScope`, one-to-one for the first 5,000 benchmark facilities.

## 4. `infrastructure.failure_recovery` 10,000

Use the existing slice at global Infrastructure ordinals `485,100..495,099`. RecordId is the existing descriptor RecordId.

The benchmark does not invent an active world failure at genesis. Instead it records a deterministic already-restored recovery proof over the first 10,000 entries of the exact canonical 55,000-service pool.

For local ordinal `r = 0..9,999`:

```text
subject_ref           = canonicalServicePool[r]
failure_kind          = perf.benchmark-recovery-proof
severity_ppm          = 0
started_step          = 0
recovery_progress_ppm = 1,000,000
expected_restore_step = NONE
dependency_refs       = []
status                = restored
```

This is a benchmark genesis evidence record, not a claim that production failures have zero severity or empty dependency sets.

Envelope is revision 1 / created Step 0 / descriptor D2 / no retirement / no lineage ref.

## 5. `infrastructure.lineage` 4,900

Use the existing slice at global Infrastructure ordinals `495,100..499,999`. RecordId is the existing descriptor RecordId.

For local ordinal `l = 0..4,899`:

```text
subject_ref      = FacilityService[l]
predecessor_refs = []
change_kind      = perf.genesis
effective_step   = 0
source_digest    = FacilityService[l].CanonicalDigest()
```

The empty predecessor list is the explicit benchmark genesis boundary. `source_digest` MUST be the actual canonical payload digest of the referenced FacilityService record and MUST fail closed if that subject payload drifts.

Envelope is revision 1 / created Step 0 / descriptor D2 / no retirement / no lineage ref.

## 6. Required production proof

Before this package counts as accepted:

1. materialize exact counts 5,000 / 10,000 / 4,900 using existing descriptor RecordIds;
2. validate every AddressPlaceIndex place/scope ref against actual BuiltStructure and TileScope records;
3. prove AddressPlaceIndex RecordId, place relation, and address token uniqueness;
4. validate every FailureRecovery subject against the exact canonical service pool and exact restored-genesis scalar/token values;
5. validate every Lineage subject against the exact FacilityService record and exact canonical payload digest;
6. prove no duplicate RecordIds or duplicate one-to-one relations in each benchmark mapping;
7. run production Snapshot encode / recovery / semantic rehash for all three partitions;
8. fail closed on missing/wrong partition refs, reversed/mismatched ordinal relations, token/scalar/optional/list drift, source-digest drift, duplicate identities/relations, and population drift;
9. keep all unrelated world and release gates unchanged;
10. run current-head CI.

## 7. Release boundary

After full proof:

```text
Infrastructure accepted 480,100 -> 500,000 / 500,000
Infrastructure remaining  19,900 ->       0
```

This closes the Infrastructure/Information 500,000-record material decomposition. It does **not** by itself close the separate reference-world parent blockers for topology/schema compatibility, Society/Governance completion, authoritative full Step loop, determinism matrix, or soak evidence.

`referenceWorldMaterialized` and `authoritativeStepLoopAvailable` remain false until their independent gates are satisfied.
