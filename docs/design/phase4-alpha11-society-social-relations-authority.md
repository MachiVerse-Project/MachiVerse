# Alpha 1.1 Society social-relations benchmark authority

Status: **Approved / normative benchmark authority**

Tracking: #240
Implementation: #265
Approval: explicit project-owner approval on 2026-09-13

## Scope

This document fixes the `perf.reference.v1` benchmark authority for three Society slices:

- `society.education`: 25,000
- `society.culture`: 30,000
- `society.reputation`: 30,000
- total: **85,000**

The authority is benchmark-only. It does not define universal education, culture, belief, reputation, or social-value taxonomies.

Approval alone does not change accepted accounting. Accepted accounting moves only after #265 proves full production materialization, actual reference closure, required secondary indexes, Snapshot/recovery semantic rehash, fail-closed negative cases, and current-head full CI.

## Existing authority used by this package

The package may use only authority already accepted or canonical:

- Organization: 10,000 accepted production records;
- Resident identity: 1,000,000 canonical production records;
- existing QA-04 Society/Governance descriptor slots for the three partitions;
- existing typed payloads, payload validators, secondary-index registry, and generic Snapshot codecs.

No PropertyRight, CurrencyMoney, FinanceAccount, ContractClaim, Facility, cargo, RuleAst, or other undecided authority may be synthesized to satisfy this package.

## Common envelope authority

Every record uses its existing QA-04 Society/Governance descriptor RecordId for the corresponding partition-local ordinal.

```text
revision     = 1
created_step = 0
retired_step = NONE
detail_level = D2
lineage_ref  = NONE
```

No new RecordId recipe is introduced.

# S1 — `society.education` 25,000

For local ordinal `e = 0..24,999`:

```text
provider_ref  = Organization[e mod 10,000]
learner_ref   = Resident[e]
program_token = perf.education-program
status        = active
progress_ppm  = 0
skill_refs    = []
started_step  = 0
ended_step    = NONE
```

Normative properties:

- all 25,000 learner Refs are distinct canonical Residents;
- every provider Ref resolves to one of the 10,000 accepted Organization records;
- provider/learner pairs are unique because every learner is unique;
- Organization ordinals `0..4,999` receive exactly 3 records each;
- Organization ordinals `5,000..9,999` receive exactly 2 records each;
- `progress_ppm = 0` is active genesis participation before benchmark progress;
- `skill_refs = []` deliberately avoids inventing skill-state or education-content authority;
- `perf.education-program` is benchmark-only;
- the fixture does not change the Phase 3 boundary: actual Resident knowledge/skill change remains Resident-owned.

Required secondary indexes:

- `society.education-by-learner`
- `society.education-by-provider`

Required production proof:

1. exact 25,000 materialized records;
2. exact descriptor/envelope binding;
3. actual Organization / Resident Ref closure;
4. exact provider 3/2 distribution and one record per learner;
5. unique provider/learner pairs and unique RecordIds;
6. exact Token/status/progress/Step/NONE/empty-skill semantics;
7. production payload validation;
8. rebuild and validate both required secondary indexes from production records;
9. full Snapshot encode/recovery/semantic rehash for all 25,000;
10. fail-closed negative proof for missing/wrong provider or learner, Token/status/progress/Step/list drift, duplicate identity/relation;
11. current-head full CI.

# S2 — `society.culture` 30,000

For local ordinal `c = 0..29,999`:

```text
subject_ref     = Organization[c mod 10,000]
trait_ordinal   = floor(c / 10,000)
trait_token     = perf.culture-trait-0 | perf.culture-trait-1 | perf.culture-trait-2
affiliation_ppm = 1,000,000
adoption_step   = 0
source_refs     = []
status          = active
```

Normative properties:

- every subject Ref resolves to an accepted Organization record;
- every Organization receives exactly 3 Culture records;
- each Organization receives exactly one of each approved benchmark trait token;
- all 30,000 `(subject_ref, trait_token)` keys are unique;
- the three trait tokens are opaque benchmark social-trait identities;
- the three traits are not mutually exclusive shares, so their `affiliation_ppm` values are not aggregated together;
- `source_refs = []` deliberately avoids fabricating transmission/history/evidence records;
- Organization subjects preserve the Society-vs-Resident boundary: this fixture does not claim authority over Resident private belief, knowledge, or practice.

Required secondary indexes:

- `society.culture-by-subject`
- `society.culture-by-trait`

Required production proof:

1. exact 30,000 materialized records;
2. exact descriptor/envelope binding;
3. actual Organization Ref closure;
4. exactly three approved traits per Organization;
5. unique `(subject_ref, trait_token)` keys and unique RecordIds;
6. exact Token vocabulary / affiliation / Step / empty-source / status semantics;
7. production payload validation and ratio bounds;
8. rebuild and validate both required secondary indexes from production records;
9. full Snapshot encode/recovery/semantic rehash for all 30,000;
10. fail-closed negative proof for missing/wrong subject, Token/ppm/Step/status/source drift, duplicate key/identity;
11. current-head full CI.

# S3 — `society.reputation` 30,000

For local ordinal `r = 0..29,999`:

```text
subject_ref        = Organization[r mod 10,000]
dimension_ordinal  = floor(r / 10,000)
audience_scope_ref = NONE
dimension_token    = perf.reputation-dimension-0 | perf.reputation-dimension-1 | perf.reputation-dimension-2
score              = 0
confidence_ppm     = 0
evidence_refs      = []
updated_step       = 0
```

Normative properties:

- every subject Ref resolves to an accepted Organization record;
- every Organization receives exactly 3 Reputation records;
- each Organization receives exactly one of each approved benchmark dimension token;
- all 30,000 `(subject_ref, dimension_token)` keys are unique;
- `audience_scope_ref = NONE` avoids inventing regional/group audience identity;
- `score = 0`, `confidence_ppm = 0`, and empty evidence mean initialized benchmark reputation dimensions without a confident evidence-backed assertion;
- the three dimension tokens are benchmark-only and do not define universal social-value dimensions;
- Reputation remains a social projection, not Core truth about the Organization and not Resident-private belief.

Required secondary indexes:

- `society.reputation-by-subject`
- `society.reputation-by-dimension`

Required production proof:

1. exact 30,000 materialized records;
2. exact descriptor/envelope binding;
3. actual Organization Ref closure;
4. exactly three approved dimensions per Organization;
5. unique `(subject_ref, dimension_token)` keys and unique RecordIds;
6. exact NONE audience / neutral score / zero confidence / empty evidence / Step 0 semantics;
7. production payload validation and ratio bounds;
8. rebuild and validate both required secondary indexes from production records;
9. full Snapshot encode/recovery/semantic rehash for all 30,000;
10. fail-closed negative proof for missing/wrong subject, audience injection, Token/score/confidence/evidence/Step drift, duplicate key/identity;
11. current-head full CI.

## Package acceptance gate

Only after all three slices have passed their production proof may accepted accounting move by +85,000:

```text
Society/Governance accepted = 1,521,100 / 2,000,000
Society/Governance remaining =   478,900
Society remaining            =   219,900
Governance remaining         =   259,000
```

The Society/Governance parent reference-world blocker remains active after this package. `referenceWorldMaterialized` remains false.

This package does not change workload authority. `authoritativeStepLoopAvailable` remains false.

## Explicit non-decisions

This authority does not decide:

- realistic education institutions, curricula, learning rates, qualifications, or skill ontology;
- Resident private knowledge/skill state;
- universal culture/language/religion categories or exclusivity;
- realistic cultural strength/distribution/transmission/history;
- universal reputation dimensions, scoring, evidence models, or audience ontology;
- PropertyRight asset ownership;
- currency/finance authority;
- production/logistics semantics;
- any remaining Governance slice.
