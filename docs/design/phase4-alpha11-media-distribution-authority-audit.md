# Alpha 1.1 `information.media_distribution` authority proposal

Status: **Review-only / decision-ready proposal — NOT normative until explicit #240 approval**  
Tracking: #240, #265  
Parent design: `phase3-infrastructure-information-domain-design.md`  
Schema: `phase4-domain-payload-schema.md`

## 1. Purpose and approval boundary

This document makes the remaining `information.media_distribution` 5,000-record QA-04 benchmark slice decision-ready without treating any proposed mapping, Token, scalar, or genesis value as approved authority.

The proposal intentionally reuses only already-materialized production authority:

- `society.information_claim`: 25,000 actual records;
- `society.organization`: 10,000 actual records;
- `infrastructure.communication_service`: 10,000 actual records;
- the actual `service_scope_ref` already owned by each CommunicationService.

Phase 3 assigns editorial/social information claims and media organization semantics to SocietyEconomy, while InfrastructureInformation owns publication channel, distribution, audience reach, capacity and delay. Resident owns actual receipt/belief. This proposal preserves that ownership split.

**Nothing in §4–§9 may be implemented as canonical benchmark authority before explicit #240 approval.**

## 2. Existing schema and hard constraints

Canonical payload schema:

```text
information.media_distribution {
  claim_ref: Ref
  publisher_ref: Ref
  channel_refs: RefList
  audience_scope_refs: RefList
  published_step: Step
  reach_count: uint64
  status: Token
}
```

Phase 4 requires that authoritative delivered/reach count be exact; a derived/non-authoritative reach estimate must not be written as authoritative state.

Phase 3 additionally requires:

- publishing is distinct from every Resident receiving the content;
- claim/content social provenance remains SocietyEconomy authority;
- publication channel/distribution/reach remains InfrastructureInformation authority;
- actual Resident receipt/belief is not owned by this partition.

Therefore a benchmark genesis fixture must not invent successful recipients merely to produce a non-zero reach value.

## 3. Existing QA-04 identity slice

`Qa04InfrastructureReferenceDecompositionV1` already fixes:

```text
material_class = media_distribution
partition      = information.media_distribution
start_ordinal  = 465,100
count          = 5,000
specialized_identity = false
```

For local ordinal `m = 0..4,999`, the proposed record identity is therefore the existing descriptor identity:

```text
Qa04InfrastructureReferenceDecompositionV1
  .Bind(465_100 + m)
  .Descriptor.RecordId
```

No InformationClaim, Organization, CommunicationService, or scope RecordId is reused as the MediaDistribution RecordId.

This identity choice is already implied by the accepted 500,000-record decomposition; the proposal does not add a new identity algorithm.

## 4. Recommended exact mapping — review only

For each local ordinal `m = 0..4,999`:

```text
claim_ref     = actual society.information_claim[m]
publisher_ref = actual society.organization[m]
channel_refs  = [ actual infrastructure.communication_service[m] ]

audience_scope_refs = [ CommunicationService[m].service_scope_ref ]
```

### 4.1 Consequences

The proposed mapping gives:

- exactly 5,000 distinct MediaDistribution records;
- exactly 5,000 distinct InformationClaim refs, each used once;
- exactly 5,000 distinct Organization publisher refs, each used once;
- exactly one CommunicationService channel per publication;
- exactly 5,000 distinct CommunicationService channels, each used once;
- exactly one audience scope per publication;
- the audience scope is not independently guessed: it is the already-authoritative scope of the selected channel;
- RefList ordering is trivial because both lists have one item;
- no dictionary/filesystem/task enumeration order participates in the mapping.

This is a **benchmark fixture mapping**, not a general rule that one claim has one publisher/channel or that all media publications use an Organization publisher.

### 4.2 Why Organization is recommended as publisher

Phase 3 explicitly places media organization semantics in SocietyEconomy. Actual `society.organization` authority already exists and therefore supplies a semantically appropriate publisher pool without inventing a new media-organization identity partition.

The referenced benchmark InformationClaim claimant is not required to equal the publisher. In the current accepted InformationClaim fixture, claimants are actual Residents; this proposal therefore models an Organization publishing an existing social/editorial claim rather than rewriting claim provenance.

No ownership, employment, editorial-control, or authorship relation between the publisher Organization and claimant Resident is implied beyond this benchmark publication relation.

### 4.3 Why CommunicationService scope is recommended as audience scope

The selected CommunicationService already has an actual `service_scope_ref`. Reusing that exact scope as the single intended audience scope:

- avoids inventing an unrelated TileScope selection algorithm;
- keeps channel reach and intended audience spatial authority aligned;
- does not claim every Resident in the scope actually received the claim.

## 5. Recommended benchmark genesis — review only

Proposed exact values:

```text
published_step = 0
reach_count    = 0
status         = published
```

### 5.1 `published_step = 0`

All referenced benchmark InformationClaims, Organizations, CommunicationServices and their scope authority exist at benchmark genesis. Step 0 is therefore a deterministic proposed publication step and introduces no future timing assumption.

### 5.2 `reach_count = 0`

This is the most conservative exact genesis value.

Phase 3 separates publication from actual receipt, and Phase 4 requires authoritative reach/delivered count to be exact. The current benchmark authority does not yet provide a canonical 5,000-publication recipient/delivery result set that would justify a positive authoritative reach count. Therefore the proposal records no completed audience reach at genesis rather than fabricating one.

`0` does **not** mean the publication has no possible audience. The intended audience is represented by `audience_scope_refs`; later authoritative delivery/receipt consequences may advance reach only when exact evidence exists.

### 5.3 `status = published`

Proposed benchmark-only StableToken:

```text
published
```

It means the publication relation has been established on the selected channel at `published_step`; it does not mean delivery to every intended recipient succeeded.

This Token is proposed here because generic `active` would blur publication occurrence with ongoing service availability, while `delivered` would violate the publication-versus-receipt boundary. The proposal does not create a universal media lifecycle vocabulary beyond this benchmark genesis state.

## 6. Proposed envelope — review only

Use the existing QA-04 Infrastructure descriptor envelope authority:

```text
record_id     = descriptor RecordId at global ordinal 465,100 + m
record_schema = standard information.media_distribution schema
revision      = 1
created_step  = 0
retired_step  = NONE
detail_level  = descriptor DetailLevel (currently D2)
lineage_ref   = NONE
```

No publisher/claim/channel identity is reused for the envelope identity.

## 7. Secondary indexes

The standard schema already defines:

```text
information.media-by-claim
information.media-by-publisher
```

If approved, production proof must rebuild both indexes from the actual 5,000-record partition and verify:

- 5,000 claim keys, each mapping to exactly one MediaDistribution RecordId;
- 5,000 publisher keys, each mapping to exactly one MediaDistribution RecordId;
- canonical ordering independent of runtime enumeration order.

## 8. Required production proof after approval

Only after explicit #240 approval and normative documentation integration may #265 implement this package. At minimum the production proof must establish:

1. exact 5,000 record materialization;
2. descriptor identity range `465,100..470,099` and no duplicate/reused MediaDistribution RecordId;
3. actual InformationClaim / Organization / CommunicationService / scope Ref closure;
4. exact one-to-one claim mapping for `0..4,999`;
5. exact one-to-one publisher mapping for Organization `0..4,999`;
6. exactly one channel and one matching channel service-scope per record;
7. exact `published_step=0`, `reach_count=0`, `status=published` genesis;
8. standard payload validation;
9. both standard secondary-index rebuilds;
10. full 5,000-record production Snapshot encode / recovery / semantic rehash;
11. fail-closed proof for missing/wrong claim, publisher, channel or scope; channel/scope mismatch; duplicate identity; duplicate relation; non-canonical list cardinality/order; Step/reach/status drift;
12. current-head full CI.

There is currently no separate specialized MediaDistribution runtime lifecycle whose semantics should be invented merely for this package. Production proof must use existing generic partition/Snapshot/index authority unless a separately approved runtime contract exists.

## 9. Expected impact only after successful production proof

Current accepted Infrastructure/Information material after `information.delivery` proof:

```text
accepted = 450,100 / 500,000
remaining = 49,900
```

If and only if this exact package is explicitly approved, normatively integrated, implemented in production, and passes the proof in §8:

```text
accepted 450,100 -> 455,100 / 500,000
remaining 49,900 -> 44,900
```

The Infrastructure parent blocker would remain active.

This package does not resolve `infrastructure.facility_service`, so Operation authority remains `5 / 6` and `infrastructure-service-delivery` remains fail-closed.

It also does not justify changing:

```text
referenceWorldMaterialized=false
authoritativeStepLoopAvailable=false
```

## 10. Remaining Infrastructure/Information slices if approved and proven

```text
infrastructure.facility_service        15,000
information.record_store               10,000
information.address_place_index         5,000
infrastructure.failure_recovery        10,000
infrastructure.lineage                  4,900
---------------------------------------------
remaining                              44,900
```

`facility_service` still requires canonical Physical/Built facility identity first. `address_place_index` still requires semantic place ownership. `record_store`, `failure_recovery`, and `lineage` retain unresolved Token/digest/lifecycle or predecessor semantics and must not be inferred from type-compatible refs.

## 11. Approval checklist

An explicit #240 decision should accept or replace all of the following as one coherent benchmark package:

- population = 5,000;
- descriptor identity at Infrastructure ordinals `465,100..470,099`;
- InformationClaim `m` mapping;
- Organization publisher `m` mapping;
- one CommunicationService `m` channel;
- audience scope = selected channel's actual `service_scope_ref`;
- `published_step=0`;
- `reach_count=0`;
- StableToken `published`;
- descriptor DetailLevel envelope;
- §8 production proof obligations.

Until that decision exists, this document remains review-only and **must not be cited as normative authority for production materialization**.
