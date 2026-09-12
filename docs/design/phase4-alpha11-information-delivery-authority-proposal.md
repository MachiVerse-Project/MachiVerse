# Alpha 1.1 InformationDelivery authority proposal

Status: **Draft / Audit / normative proposal pending**

Tracking: #240

Implementation PR after approval: #265

## 1. Purpose and boundary

This document isolates the next decision-ready `perf.reference.v1` Infrastructure / Information package after the production-proven `infrastructure.dependency` 20,000 authority.

Current proven Infrastructure accounting:

```text
accepted topology v2                 120,100
accepted network services             40,000
accepted dependency                    20,000
accepted ServiceQueue                250,000
--------------------------------------------
accepted                              430,100 / 500,000
remaining                              69,900
```

`infrastructure.facility_service` 15,000 remains blocked by upstream Physical/Built facility identity authority. This proposal does **not** invent that authority and does not attempt to close the final `infrastructure-service-delivery` Operation family.

Among the remaining slices, `information.delivery` 20,000 is separately decision-ready because all required target authority already exists:

- `society.information_claim` 25,000 actual production records;
- canonical Resident identity 1,000,000 actual production records;
- `infrastructure.communication_service` 10,000 actual production records.

This document proposes benchmark-only exact values for `information.delivery` and nothing else. Until explicit adoption in #240, the values below are non-normative and MUST NOT be implemented in #265.

## 2. Existing schema and runtime constraints

Persistent payload:

```text
content_ref: Ref
sender_ref: Ref
recipient_refs: RefList
channel_ref: Ref
eligible_step: Step
delivered_step: Step?
priority: int32
status: Token
content_digest: Digest
```

Phase 4 requires recipient refs to be canonical and defines derived secondary indexes:

```text
information.delivery-by-recipient
information.delivery-by-status
```

The production runtime already distinguishes:

```text
Queued
Delivered
Failed
```

and `MarkDelivered()` permits only the `Queued -> Delivered` transition. That fixes lifecycle semantics at runtime but does not by itself choose the persistent benchmark StableToken spelling or genesis state.

The standard Operation catalog also defines `information.delivery.send`, confirming that delivery is ordinary Infrastructure/Information authority rather than Society-owned claim state.

## 3. Existing authority surfaces

### 3.1 InformationClaim

The accepted Society/Governance authority materializes 25,000 `society.information_claim` records. For local ordinal `i`:

- the record has a stable QA-04 descriptor RecordId;
- `claimant_ref` is an actual canonical Resident;
- benchmark claim Token is `perf.claim`;
- `created_step = 0`;
- `status = active`;
- `content_digest` is a real 32-byte authoritative field produced by the accepted InformationClaim genesis contract.

Therefore `information.delivery.content_digest` can copy the referenced claim's authoritative `content_digest` exactly. No placeholder digest and no new digest recipe are required.

### 3.2 Resident

Canonical Resident authority provides 1,000,000 `resident.identity_lifecycle` records with stable ordinal identity.

### 3.3 CommunicationService

The accepted Infrastructure service authority provides 10,000 `infrastructure.communication_service` records with stable local ordinals `0..9,999`, active genesis, and actual network/scope closure.

## 4. Recommended package — review only

### 4.1 Cardinality and record identity

```text
population = 20,000
local ordinal d = 0..19,999
```

Use the existing QA-04 Infrastructure descriptor identity for the `information_delivery` slice:

```text
slice start = 445,100
slice count = 20,000
partition   = information.delivery
specialized identity = false

record_id(d) = Qa04InfrastructureReferenceDecompositionV1
  .Bind(445,100 + d)
  .Descriptor.RecordId
```

Do not reuse InformationClaim, Resident, or CommunicationService RecordIds as Delivery RecordIds.

### 4.2 Content and sender mapping

For every `d = 0..19,999`:

```text
content_ref = actual InformationClaim[d]
sender_ref  = InformationClaim[d].claimant_ref
```

Consequences:

- exactly 20,000 distinct actual InformationClaim records are used;
- sender identity is not independently invented;
- the sender has semantic provenance from the referenced content authority;
- the first 20,000 accepted claims are used in canonical local-ordinal order, independent of collection/task/filesystem enumeration order.

### 4.3 Recipient mapping — proposed decision

Recommended benchmark-only mapping:

```text
recipient_refs = [ Resident[d + 1] ]
```

for `d = 0..19,999`.

Because the sender of InformationClaim[d] is canonical Resident[d], this yields:

- exactly one recipient per delivery;
- no benchmark self-delivery;
- 20,000 distinct recipient Residents (`1..20,000`);
- canonical RefList ordering is trivial because the list contains one element;
- no random/hash selector and no iteration-order dependence.

This `+1 Resident` rule is only a deterministic benchmark fixture. It does not define general message-recipient semantics.

### 4.4 Channel mapping

Recommended mapping:

```text
channel_ref = actual CommunicationService[d mod 10,000]
```

Consequences:

- every delivery has an actual channel authority;
- each canonical CommunicationService is used by exactly two delivery records;
- mapping is deterministic and does not imply exclusive ownership of a channel by a sender/recipient.

### 4.5 Recommended genesis payload

Recommended exact values:

```text
eligible_step   = 0
delivered_step  = NONE
priority        = 0
status          = queued
content_digest  = exact InformationClaim[d].content_digest bytes
```

Rationale:

- `eligible_step = 0` matches benchmark genesis availability;
- `delivered_step = NONE` is required for a not-yet-delivered genesis record;
- `priority = 0` selects the neutral deterministic baseline and adds no unapproved priority taxonomy;
- `queued` matches the production runtime's `Queued` state and the existing benchmark queue vocabulary style;
- copying the InformationClaim content digest preserves immutable content identity instead of inventing or rehashing substitute data.

The proposal does not claim that real-world deliveries universally begin queued at Step 0 or use priority zero.

### 4.6 Envelope

Use the existing non-specialized QA-04 descriptor envelope:

```text
revision      = 1
created_step  = 0
retired_step  = NONE
detail_level  = descriptor DetailLevel (currently D2 for infrastructure.active-record)
lineage_ref   = NONE
```

No new identity or detail distribution is proposed.

## 5. Required production proof after approval

If and only if #240 explicitly adopts this package, #265 must prove at least:

1. exact 20,000 `information.delivery` record materialization;
2. actual InformationClaim Ref closure;
3. exact `sender_ref == referenced claim claimant_ref` for all records;
4. actual Resident recipient Ref closure;
5. exact one-recipient mapping and no self-delivery;
6. actual CommunicationService Ref closure;
7. exact two deliveries per CommunicationService;
8. exact `eligible_step=0 / delivered_step=NONE / priority=0 / queued` genesis;
9. byte-for-byte equality between Delivery `content_digest` and referenced InformationClaim `content_digest`;
10. canonical recipient list ordering;
11. production payload validation and secondary-index rebuild compatibility;
12. full production Snapshot encode / recovery / semantic rehash for all 20,000 records;
13. runtime `Queued -> Delivered` transition compatibility without mutating content identity;
14. negative tests for missing/wrong content, sender mismatch, missing/wrong recipient, missing/wrong channel, self-delivery, duplicate Delivery identity, wrong digest, invalid status, delivered-step/status mismatch, priority/eligible-step genesis drift, and non-canonical recipient list;
15. current-head full CI.

Only after successful production proof may accounting move:

```text
Infrastructure accepted = 450,100 / 500,000
Infrastructure remaining =  49,900
```

The Infrastructure reference-world parent blocker remains active. Operation authority remains `5 / 6` because `facility_service` authority is still absent.

## 6. Explicit non-decisions

This proposal does not decide:

- `infrastructure.facility_service` or upstream Built facility authority;
- `information.media_distribution`;
- `information.record_store`;
- `information.address_place_index`;
- `infrastructure.failure_recovery`;
- `infrastructure.lineage`;
- a general messaging/channel routing model;
- general StableToken vocabulary beyond the benchmark genesis `queued` proposal;
- general priority semantics;
- message fan-out cardinality beyond this benchmark fixture.

## 7. Approval boundary

**Pending explicit #240 decision.**

Before approval:

- keep this PR Draft;
- do not merge this proposal as normative authority;
- do not implement the candidate values in #265;
- do not increase Infrastructure accepted accounting;
- do not change `referenceWorldMaterialized`, `authoritativeStepLoopAvailable`, or Operation authority.

If approved, update this document from proposal language to normative benchmark authority, merge through `documentation`, sync only the approved diff to `develop`, then implement/prove it in #265.