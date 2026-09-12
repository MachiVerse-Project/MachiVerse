# Alpha 1.1 `information.media_distribution` authority

Status: **Complete / normative benchmark authority**  
Tracking: #240, #265  
Parent design: `phase3-infrastructure-information-domain-design.md`  
Schema: `phase4-domain-payload-schema.md`

## 1. Normative scope

#240 owner approval on 2026-09-12 adopts the exact 5,000-record QA-04 `information.media_distribution` benchmark package defined here. This authority is benchmark-fixture-specific and does not create a universal media lifecycle or publisher/channel cardinality rule.

The package reuses only already-materialized production authority:

- `society.information_claim`: 25,000 actual records;
- `society.organization`: 10,000 actual records;
- `infrastructure.communication_service`: 10,000 actual records;
- each selected CommunicationService's actual `service_scope_ref`.

Phase 3 ownership remains unchanged: SocietyEconomy owns editorial/social claims and media organizations; InfrastructureInformation owns publication channel/distribution/reach; Resident owns actual receipt/belief.

## 2. Canonical schema

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

Publication MUST remain distinct from actual Resident receipt. Authoritative reach MUST be exact; this benchmark therefore does not fabricate positive reach at genesis.

## 3. Cardinality and identity

`Qa04InfrastructureReferenceDecompositionV1` fixes:

```text
material_class = media_distribution
partition      = information.media_distribution
start_ordinal  = 465,100
count          = 5,000
specialized_identity = false
```

For local ordinal `m = 0..4,999`:

```text
record_id = Qa04InfrastructureReferenceDecompositionV1
  .Bind(465_100 + m)
  .Descriptor.RecordId
```

No InformationClaim, Organization, CommunicationService, or scope identity is reused as the MediaDistribution identity.

## 4. Canonical mapping

For each `m = 0..4,999`:

```text
claim_ref     = actual society.information_claim[m]
publisher_ref = actual society.organization[m]
channel_refs  = [ actual infrastructure.communication_service[m] ]
audience_scope_refs = [ CommunicationService[m].service_scope_ref ]
```

Consequences:

- exactly 5,000 distinct records;
- exactly 5,000 distinct InformationClaim refs, each used once;
- exactly 5,000 distinct Organization publisher refs, each used once;
- exactly one CommunicationService channel per publication;
- exactly 5,000 distinct selected channels, each used once;
- exactly one audience scope per publication;
- audience scope is the selected channel's actual service scope;
- list ordering is canonical and independent of runtime enumeration order.

This mapping does not imply general one-claim/one-publisher/one-channel semantics outside this benchmark fixture.

## 5. Canonical genesis payload

```text
published_step = 0
reach_count    = 0
status         = published
```

`published` means the publication relation exists on the selected channel at Step 0. It does not mean every intended recipient received the claim.

`reach_count = 0` is normative for benchmark genesis because no separately accepted exact recipient/delivery result set exists at genesis. Later authoritative delivery/receipt consequences may advance reach only from exact evidence.

## 6. Canonical envelope

```text
record_id     = descriptor RecordId at global ordinal 465,100 + m
record_schema = standard information.media_distribution schema
revision      = 1
created_step  = 0
retired_step  = NONE
detail_level  = descriptor DetailLevel (D2 in the current fixture)
lineage_ref   = NONE
```

## 7. Secondary-index authority

The standard indexes are:

```text
information.media-by-claim
information.media-by-publisher
```

Production proof MUST rebuild both from the actual 5,000-record partition and verify:

- 5,000 claim keys, each mapping to exactly one MediaDistribution RecordId;
- 5,000 publisher keys, each mapping to exactly one MediaDistribution RecordId;
- canonical result ordering independent of runtime enumeration order.

## 8. Required production proof

After documentation integration to `develop`, #265 MUST establish:

1. exact 5,000 record materialization;
2. descriptor identity range `465,100..470,099` with no duplicate/reused identity;
3. actual InformationClaim / Organization / CommunicationService / scope Ref closure;
4. exact one-to-one claim mapping for `0..4,999`;
5. exact one-to-one publisher mapping for Organization `0..4,999`;
6. exactly one channel and one matching channel service-scope per record;
7. exact `published_step=0`, `reach_count=0`, `status=published` genesis;
8. standard payload validation;
9. both standard secondary-index rebuilds;
10. full 5,000-record production Snapshot encode / recovery / semantic rehash;
11. fail-closed proof for missing/wrong claim, publisher, channel or scope; channel/scope mismatch; duplicate identity/relation; non-canonical list cardinality/order; Step/reach/status drift;
12. current-head full CI.

There is no separately approved specialized MediaDistribution runtime lifecycle. Implementation MUST use existing generic partition/Snapshot/index authority unless such a runtime contract is separately approved.

## 9. Release impact

Approval alone does not change accepted counts. Only after §8 production proof succeeds may Infrastructure move:

```text
accepted 450,100 -> 455,100 / 500,000
remaining 49,900 -> 44,900
```

Infrastructure parent blocker remains active. `infrastructure.facility_service` remains unresolved, so Operation authority remains `5 / 6` and `infrastructure-service-delivery` remains fail-closed.

The following flags remain false:

```text
referenceWorldMaterialized=false
authoritativeStepLoopAvailable=false
```

No other remaining Infrastructure/Information slice is approved by this document.