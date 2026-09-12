# Alpha 1.1 `information.record_store` authority

Status: **Complete / normative benchmark authority**  
Tracking: #240, #265  
Parent design: `phase3-infrastructure-information-domain-design.md`  
Schema: `phase4-domain-payload-schema.md`

## 1. Normative scope

#240 owner approval on 2026-09-12 adopts the exact 10,000-record QA-04 `information.record_store` benchmark package defined here.

Phase 3 truth separation remains mandatory: stored/public/private records are not Core truth merely because they exist. InfrastructureInformation owns storage/version/retrieval availability; semantic claim provenance remains SocietyEconomy and legal effect/registration authority remains Governance.

This package therefore materializes stored copies of already-authoritative InformationClaim content without granting those copies separate legal authority.

## 2. Canonical schema

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

Standard indexes:

```text
information.record-by-subject
information.record-by-kind
```

## 3. Cardinality and identity

`Qa04InfrastructureReferenceDecompositionV1` fixes:

```text
material_class = record_store
partition      = information.record_store
start_ordinal  = 470,100
count          = 10,000
specialized_identity = false
```

For local ordinal `r = 0..9,999`:

```text
record_id = Qa04InfrastructureReferenceDecompositionV1
  .Bind(470_100 + r)
  .Descriptor.RecordId
```

No InformationClaim RecordId is reused as a RecordStore RecordId.

## 4. Canonical mapping

For each `r = 0..9,999`:

```text
subject_refs   = [ actual society.information_claim[r] ]
authority_ref  = NONE
content_digest = InformationClaim[r].content_digest byte-for-byte
created_step   = InformationClaim[r].created_step
```

The accepted InformationClaim fixture has `created_step = 0`, so the benchmark RecordStore payload also has Step 0 without inventing a separate timestamp.

Consequences:

- exactly 10,000 distinct RecordStore relations;
- exactly 10,000 distinct InformationClaim subjects, each used once;
- exactly one subject per record;
- digest comes from actual accepted semantic content rather than placeholder material;
- no claimant, Organization, Institution, or PublicAuthority is silently promoted into `authority_ref`.

This is benchmark fixture authority only; it does not require general RecordStore records to represent InformationClaims or have exactly one subject.

## 5. Canonical genesis payload

```text
record_kind    = perf.information-claim-record
authority_ref  = NONE
subject_refs   = [InformationClaim[r]]
content_digest = InformationClaim[r].content_digest
version        = 1
created_step   = InformationClaim[r].created_step (=0 in the current fixture)
available      = true
supersedes_ref = NONE
```

`perf.information-claim-record` is benchmark-only vocabulary. It identifies this fixture as storing accepted InformationClaim material and MUST NOT be generalized into a universal record taxonomy.

`authority_ref = NONE` is normative because no approved rule pairs these records with legal/public authority. `version = 1` and `supersedes_ref = NONE` define the genesis version without inventing predecessor authority.

## 6. Canonical envelope

```text
record_id     = descriptor RecordId at global ordinal 470,100 + r
record_schema = standard information.record_store schema
revision      = 1
created_step  = 0
retired_step  = NONE
detail_level  = descriptor DetailLevel (D2 in the current fixture)
lineage_ref   = NONE
```

Payload `created_step` is separately copied from the subject InformationClaim and is Step 0 in the accepted fixture.

## 7. Secondary-index authority

Production proof MUST rebuild:

```text
information.record-by-subject
information.record-by-kind
```

Expected benchmark results:

- subject index: 10,000 InformationClaim keys, each mapping to exactly one RecordStore RecordId;
- kind index: one `perf.information-claim-record` key mapping to all 10,000 RecordStore RecordIds in canonical order.

## 8. Required production proof

After documentation integration to `develop`, #265 MUST establish:

1. exact 10,000 record materialization;
2. descriptor identity range `470,100..480,099` with no duplicate/reused identity;
3. actual InformationClaim Ref closure for all 10,000 subjects;
4. exactly one subject per record and exact one-to-one subject mapping;
5. `authority_ref=NONE` and `supersedes_ref=NONE` throughout genesis;
6. `record_kind=perf.information-claim-record`, `version=1`, `available=true`;
7. exact byte-for-byte digest equality to each InformationClaim;
8. payload `created_step` equality to each InformationClaim `created_step` and current fixture value 0;
9. standard payload validation;
10. both standard secondary-index rebuilds;
11. full 10,000-record production Snapshot encode / recovery / semantic rehash;
12. fail-closed proof for missing/wrong subject, duplicate subject relation, duplicate identity, placeholder/wrong digest, invalid kind, non-canonical subject list, authority/supersedes injection, version/Step/availability drift;
13. current-head full CI.

No synthetic legal/public authority or predecessor record may be created merely to make optional fields non-null.

## 9. Release impact

Approval alone does not change accepted counts. If this package alone passes §8 proof:

```text
accepted 450,100 -> 460,100 / 500,000
remaining 49,900 -> 39,900
```

If the separately approved MediaDistribution 5,000 package also passes production proof, cumulative Infrastructure state becomes:

```text
accepted 450,100 -> 465,100 / 500,000
remaining 49,900 -> 34,900
```

Infrastructure parent blocker remains active. `infrastructure.facility_service` remains unresolved, so Operation authority remains `5 / 6`. Release flags remain false.

No other remaining Infrastructure/Information slice is approved by this document.