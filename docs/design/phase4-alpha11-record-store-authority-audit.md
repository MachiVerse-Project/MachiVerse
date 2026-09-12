# Alpha 1.1 `information.record_store` authority proposal

Status: **Review-only / decision-ready proposal — NOT normative until explicit #240 approval**  
Tracking: #240, #265  
Parent design: `phase3-infrastructure-information-domain-design.md`  
Schema: `phase4-domain-payload-schema.md`

## 1. Purpose and approval boundary

This document makes the remaining `information.record_store` 10,000-record QA-04 benchmark slice decision-ready without treating proposed mapping, Token, optional-ref policy, version, availability, or genesis values as approved authority.

Phase 3 requires strict truth separation: a stored/public/private record is not Core truth merely because it exists. InfrastructureInformation owns record carrier/store/version/retrieval availability; semantic claim provenance remains SocietyEconomy and legal effect/authority remains Governance.

The proposal therefore models a benchmark stored copy of already-authoritative InformationClaim material without granting that stored copy independent legal authority.

**Nothing in §4–§8 may be implemented as canonical benchmark authority before explicit #240 approval.**

## 2. Existing schema and hard constraints

Canonical payload schema:

```text
information.record_store {
  record_kind: Token
  authority_ref: Ref?
  subject_refs: RefList
  content_digest: Digest
  version: uint32
  created_step: Step
  available: bool
  supersedes_ref: Ref?
}
```

Standard secondary indexes:

```text
information.record-by-subject
information.record-by-kind
```

Existing actual authority relevant to this proposal:

- `society.information_claim`: 25,000 records;
- every accepted InformationClaim already has an immutable `content_digest`;
- every accepted InformationClaim already has `created_step` and claimant provenance;
- standard Infrastructure/Information descriptor identities already reserve exactly 10,000 RecordStore records.

The proposal deliberately does **not** use InformationClaim claimant as `authority_ref`: claimant provenance does not automatically mean legal/public-record authority.

## 3. Existing QA-04 identity slice

`Qa04InfrastructureReferenceDecompositionV1` fixes:

```text
material_class = record_store
partition      = information.record_store
start_ordinal  = 470,100
count          = 10,000
specialized_identity = false
```

For local ordinal `r = 0..9,999`, proposed RecordStore identity is:

```text
Qa04InfrastructureReferenceDecompositionV1
  .Bind(470_100 + r)
  .Descriptor.RecordId
```

No InformationClaim RecordId is reused as a RecordStore RecordId.

## 4. Recommended exact mapping — review only

For each local ordinal `r = 0..9,999`:

```text
subject_refs = [ actual society.information_claim[r] ]
authority_ref = NONE
content_digest = InformationClaim[r].content_digest byte-for-byte
created_step = InformationClaim[r].created_step
```

The current accepted InformationClaim fixture has `created_step = 0`, so the proposed benchmark genesis is also deterministically Step 0 without inventing a separate storage timestamp.

### 4.1 Consequences

- exactly 10,000 distinct stored-record relations;
- exactly 10,000 distinct InformationClaim subjects, each used once;
- each record has exactly one subject;
- digest is derived from already-authoritative semantic content rather than a placeholder/hash of the RecordStore identity;
- record creation time follows the subject content's accepted genesis time;
- no legal/public authority is inferred from claim authorship or storage existence.

This is benchmark fixture mapping only. It does not define that all stored records must represent InformationClaims or have one subject.

## 5. Recommended benchmark payload — review only

Proposed exact values:

```text
record_kind   = perf.information-claim-record
authority_ref = NONE
version       = 1
available     = true
supersedes_ref = NONE
```

Together with §4:

```text
subject_refs  = [InformationClaim[r]]
content_digest = InformationClaim[r].content_digest
created_step   = InformationClaim[r].created_step (= 0 in current fixture)
```

### 5.1 `record_kind = perf.information-claim-record`

This is a proposed benchmark-only StableToken. The `perf.` prefix prevents a benchmark fixture classification from silently becoming a universal document/record taxonomy.

It means only: this RecordStore fixture stores a record whose semantic subject is an accepted InformationClaim.

### 5.2 `authority_ref = NONE`

Phase 3 states that legal effect/registration authority belongs to Governance, while a record may be wrong, stale, false, lost, or damaged. The current benchmark has no separately approved rule pairing these 10,000 stored records with a legal/public authority.

Using `NONE` avoids falsely treating the InformationClaim claimant, Organization, Institution, or PublicAuthority as the record's legal authority.

### 5.3 `version = 1`, `supersedes_ref = NONE`

These 10,000 records are proposed as genesis versions. No accepted predecessor/supersession relation exists, so version 1 plus no predecessor is the minimal consistent initial version policy.

### 5.4 `available = true`

The proposed records are materialized as benchmark genesis store entries and therefore are proposed initially available. This is fixture state only; it does not assert permanent availability or suppress later loss/failure/recovery behavior.

## 6. Proposed envelope — review only

Use the existing descriptor envelope:

```text
record_id     = descriptor RecordId at global ordinal 470,100 + r
record_schema = standard information.record_store schema
revision      = 1
created_step  = 0
retired_step  = NONE
detail_level  = descriptor DetailLevel (currently D2)
lineage_ref   = NONE
```

Payload `created_step` is separately copied from the subject InformationClaim and must equal 0 for this accepted fixture.

## 7. Secondary-index proof

If approved, production proof must rebuild:

```text
information.record-by-subject
information.record-by-kind
```

Expected benchmark results:

- subject index: exactly 10,000 InformationClaim keys, each mapping to exactly one RecordStore RecordId;
- kind index: exactly one `perf.information-claim-record` key mapping to all 10,000 RecordStore RecordIds in canonical order.

## 8. Required production proof after approval

After explicit #240 approval and normative documentation integration, #265 must prove at minimum:

1. exact 10,000 record materialization;
2. descriptor identity range `470,100..480,099` with no duplicate/reused identity;
3. actual InformationClaim Ref closure for all 10,000 subjects;
4. exactly one subject per record and exact one-to-one subject mapping;
5. `authority_ref=NONE` and `supersedes_ref=NONE` throughout genesis;
6. `record_kind=perf.information-claim-record`, `version=1`, `available=true`;
7. exact byte-for-byte content digest equality to each InformationClaim;
8. payload created_step equality to each InformationClaim created_step and current fixture value 0;
9. standard payload validation;
10. both standard secondary-index rebuilds;
11. full 10,000-record production Snapshot encode / recovery / semantic rehash;
12. fail-closed tests for missing/wrong subject, duplicate subject relation, duplicate identity, placeholder/wrong digest, invalid kind, non-canonical subject list, authority/supersedes injection, version/Step/availability drift;
13. current-head full CI.

No synthetic legal/public authority or predecessor record may be created merely to make optional fields non-null.

## 9. Expected impact only after successful production proof

This proposal is independent of the separate `information.media_distribution` 5,000 proposal in the same documentation PR.

Current accepted Infrastructure/Information material is:

```text
accepted = 450,100 / 500,000
remaining = 49,900
```

If RecordStore alone is explicitly approved and fully proven:

```text
accepted 450,100 -> 460,100 / 500,000
remaining 49,900 -> 39,900
```

If both MediaDistribution 5,000 and RecordStore 10,000 are approved and fully proven, cumulative impact would be:

```text
accepted 450,100 -> 465,100 / 500,000
remaining 49,900 -> 34,900
```

Counts must be incremented only after each production proof succeeds, not at approval time.

Neither package resolves `infrastructure.facility_service`; Operation authority remains `5 / 6`. Infrastructure parent blocker remains active, and release flags remain false.

## 10. Approval checklist

An explicit #240 RecordStore decision should accept or replace:

- population = 10,000;
- descriptor identity at `470,100..480,099`;
- subject = InformationClaim `r`;
- exactly one subject per record;
- `record_kind = perf.information-claim-record`;
- `authority_ref = NONE`;
- digest = exact InformationClaim content digest;
- `version = 1`;
- payload `created_step = InformationClaim.created_step`;
- `available = true`;
- `supersedes_ref = NONE`;
- descriptor envelope;
- §8 proof obligations.

Until explicit approval, this document remains review-only and **must not be cited as normative production authority**.
