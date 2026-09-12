# Alpha 1.1 Society social-relations authority proposal

Status: **Review only / #240 approval pending**

Tracking: #240, implementation after approval: #265

## Purpose

This document makes three low-dependency Society slices decision-ready for `perf.reference.v1` without inventing missing Property/Currency/Finance/Physical authority:

- `society.education`: 25,000
- `society.culture`: 30,000
- `society.reputation`: 30,000
- package total: **85,000**

This proposal is **not normative authority until #240 explicitly approves it**. No semantic values below may be implemented in #265 before approval.

Current production accepted accounting after the completed Employment package is:

```text
Society/Governance accepted = 1,436,100 / 2,000,000
Society/Governance remaining =   563,900
Society remaining            =   304,900
Governance remaining         =   259,000
```

If all three proposed slices are approved and subsequently production-proven, the accounting would become:

```text
Society/Governance accepted = 1,521,100 / 2,000,000
Society/Governance remaining =   478,900
Society remaining            =   219,900
Governance remaining         =   259,000
```

Approval alone must not change accepted accounting.

## Why these three slices are grouped

All three can close their required Ref endpoints using authority already accepted or canonical:

- Organization: 10,000 accepted production records;
- Resident identity: 1,000,000 canonical production records;
- QA-04 descriptor slots for all three partitions;
- typed payloads, standard payload validation, secondary-index registry, and generic Snapshot codecs already exist.

They do not require PropertyRight, CurrencyMoney, FinanceAccount, ContractClaim, Facility, cargo, RuleAst, or other undecided authority.

Phase 3 boundaries are preserved:

- Education records the social participation relation; actual Resident knowledge/skill change remains Resident-owned;
- Culture is kept on Organization subjects so Society does not claim authority over a Resident's private belief/knowledge/practice;
- Reputation remains a social projection and is not Core truth about the subject or a Resident's private belief.

## Common envelope boundary

For every proposed record, use the existing QA-04 Society/Governance descriptor identity:

```text
revision     = 1
created_step = 0
retired_step = NONE
detail_level = D2
lineage_ref  = NONE
```

No new RecordId recipe is proposed.

# S1 — `society.education` 25,000

Payload surface:

```text
provider_ref: Ref
learner_ref: Ref
program_token: Token
status: Token
progress_ppm: Ratio
skill_refs: RefList
started_step: Step
ended_step: Step?
```

Recommended benchmark authority for local ordinal `e = 0..24,999`:

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

Recommended properties:

- all 25,000 learner Refs are distinct canonical Residents;
- provider/learner pairs are unique because every learner is unique;
- Organization ordinals `0..4,999` receive 3 records each;
- Organization ordinals `5,000..9,999` receive 2 records each;
- `progress_ppm = 0` represents active genesis participation before benchmark progress;
- `skill_refs = []` avoids fabricating Resident skill-state identity or education-content authority;
- `perf.education-program` is benchmark-only and does not define a universal education taxonomy;
- Organization providers are a benchmark fixture only; Phase 3 still permits school/apprentice/family/self and other historically formed education paths.

Required production proof after approval:

1. exact 25,000 records;
2. actual Organization and Resident Ref closure;
3. exact provider 3/2 distribution and one-per-learner mapping;
4. unique provider/learner pairs;
5. exact Token/status/progress/Step/NONE/empty-skill semantics;
6. production payload validation;
7. rebuild `society.education-by-learner` and `society.education-by-provider`;
8. Snapshot encode/recovery/semantic rehash for all 25,000;
9. fail-closed tests for wrong/missing refs, Token/status/progress/Step/list drift, duplicate identity/relation;
10. current-head full CI.

# S2 — `society.culture` 30,000

Payload surface:

```text
subject_ref: Ref
trait_token: Token
affiliation_ppm: Ratio
adoption_step: Step
source_refs: RefList
status: Token
```

Recommended benchmark authority for local ordinal `c = 0..29,999`:

```text
subject_ref     = Organization[c mod 10,000]
trait_ordinal   = floor(c / 10,000)
trait_token     = perf.culture-trait-0 | perf.culture-trait-1 | perf.culture-trait-2
affiliation_ppm = 1,000,000
adoption_step   = 0
source_refs     = []
status          = active
```

Recommended properties:

- every Organization receives exactly three Culture records;
- `(subject_ref, trait_token)` is unique for all 30,000 records;
- the three trait Tokens are benchmark-only opaque social-trait identities;
- Culture traits are explicitly **not mutually exclusive shares**, so three 1,000,000-ppm trait relations on one Organization do not imply a 3,000,000-ppm aggregate distribution;
- `source_refs = []` avoids fabricating transmission/history/evidence records at genesis;
- using Organization as subject keeps this Society fixture distinct from Resident-owned private belief/knowledge/practice.

Required production proof after approval:

1. exact 30,000 records;
2. actual Organization Ref closure;
3. exactly three distinct benchmark traits per Organization;
4. unique `(subject_ref, trait_token)` keys;
5. exact trait Token vocabulary, affiliation, Step, source list, and status;
6. production payload validation and ratio bounds;
7. rebuild `society.culture-by-subject` and `society.culture-by-trait`;
8. Snapshot encode/recovery/semantic rehash for all 30,000;
9. fail-closed tests for wrong/missing subject, Token/ppm/Step/status/source drift, duplicate key/identity;
10. current-head full CI.

# S3 — `society.reputation` 30,000

Payload surface:

```text
subject_ref: Ref
audience_scope_ref: Ref?
dimension_token: Token
score: Int32
confidence_ppm: Ratio
evidence_refs: RefList
updated_step: Step
```

Recommended benchmark authority for local ordinal `r = 0..29,999`:

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

Recommended properties:

- every Organization receives exactly three Reputation records;
- `(subject_ref, dimension_token)` is unique for all 30,000 records;
- `audience_scope_ref = NONE` means this minimal benchmark does not invent a regional/group audience identity;
- neutral `score = 0` plus `confidence_ppm = 0` and `evidence_refs = []` represents an initialized social-reputation dimension with no supporting evidence/confidence, rather than asserting a confident reputation fact;
- the three dimension Tokens are benchmark-only and do not define universal social-value dimensions;
- Reputation remains a social projection, not Core truth about the Organization.

Required production proof after approval:

1. exact 30,000 records;
2. actual Organization Ref closure;
3. exactly three distinct dimensions per Organization;
4. unique `(subject_ref, dimension_token)` keys;
5. exact NONE audience, neutral score, zero confidence, empty evidence, Step 0 semantics;
6. production payload validation and ratio bounds;
7. rebuild `society.reputation-by-subject` and `society.reputation-by-dimension`;
8. Snapshot encode/recovery/semantic rehash for all 30,000;
9. fail-closed tests for wrong/missing subject, audience injection, Token/score/confidence/evidence/Step drift, duplicate key/identity;
10. current-head full CI.

## Package-level production gate

If #240 approves this proposal, the allowed flow is:

1. convert this review-only proposal into normative documentation with the exact approved values;
2. merge to `documentation`;
3. minimal one-file sync to `develop`;
4. implement the three materializers/proofs in #265;
5. prove all 85,000 records through actual upstream Ref authority, required secondary indexes, Snapshot/recovery semantic rehash, negative tests, and full current-head CI;
6. only then update accepted accounting by +85,000.

The Society/Governance parent blocker remains active after this package. `referenceWorldMaterialized` and `authoritativeStepLoopAvailable` remain false.

## Explicit non-decisions

This proposal does not decide:

- realistic education institutions, curricula, learning rates, qualifications, or skill ontology;
- Resident private knowledge/skill state;
- universal culture/language/religion categories or exclusivity;
- realistic cultural strength/distribution/transmission/history;
- universal reputation dimensions, social scoring, evidence models, or audience ontology;
- PropertyRight asset ownership;
- currency/finance authority;
- production/logistics semantics;
- any Governance remaining slice.

## Approval boundary

Until #240 explicitly approves S1/S2/S3, all values in this document remain review-only recommendations and **must not be implemented in #265**.