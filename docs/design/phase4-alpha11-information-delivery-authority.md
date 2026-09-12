# Alpha 1.1 InformationDelivery canonical authority

Status: **Approved / normative benchmark authority**

Tracking: #240

Review history: #313 / `phase4-alpha11-information-delivery-authority-proposal.md`

Implementation target: #265

## 1. Adopted scope

#240 approval adopts the exact `perf.reference.v1` `information.delivery` package described below. This authority is benchmark-only and MUST NOT be generalized into real-world messaging semantics.

Current production-proven Infrastructure baseline before this package is implemented:

```text
Infrastructure accepted  = 430,100 / 500,000
Infrastructure remaining =  69,900
```

This decision covers exactly 20,000 `information.delivery` records. It does not adopt `infrastructure.facility_service`, `information.media_distribution`, `information.record_store`, `information.address_place_index`, `infrastructure.failure_recovery`, or `infrastructure.lineage`.

## 2. Canonical population and identity

```text
population = 20,000
local ordinal d = 0..19,999
slice start = 445,100
slice count = 20,000
partition = information.delivery
specialized identity = false
```

For each local ordinal `d`:

```text
record_id(d) = Qa04InfrastructureReferenceDecompositionV1
  .Bind(445,100 + d)
  .Descriptor.RecordId
```

Delivery identity MUST remain distinct from InformationClaim, Resident, and CommunicationService identities.

Envelope authority:

```text
revision      = 1
created_step  = 0
retired_step  = NONE
detail_level  = descriptor DetailLevel
lineage_ref   = NONE
```

## 3. Canonical reference mapping

For every `d = 0..19,999`:

```text
content_ref = actual society.information_claim[d]
sender_ref  = content_ref.claimant_ref
recipient_refs = [ actual resident.identity_lifecycle[d + 1] ]
channel_ref = actual infrastructure.communication_service[d mod 10,000]
```

Required invariants:

- exactly 20,000 distinct InformationClaim records are used;
- `sender_ref` MUST equal the referenced InformationClaim's authoritative `claimant_ref`;
- exactly one recipient exists per Delivery;
- recipient MUST be canonical Resident ordinal `d + 1`;
- sender and recipient MUST differ for this benchmark fixture;
- recipient list canonical ordering is therefore trivial and exact;
- each of the 10,000 CommunicationService records is referenced by exactly two Delivery records;
- all refs MUST resolve to actual production authority, not synthetic placeholders.

The `d + 1` recipient rule is only the approved deterministic benchmark fixture. It does not define general recipient selection semantics.

## 4. Canonical genesis payload

Exact approved values:

```text
eligible_step   = 0
delivered_step  = NONE
priority        = 0
status          = queued
content_digest  = exact referenced InformationClaim.content_digest bytes
```

`content_digest` MUST be copied byte-for-byte from the referenced InformationClaim authority. It MUST NOT be replaced with a newly invented digest recipe, placeholder digest, record-id digest, or rehash of substitute material.

`queued` is the exact approved persistent StableToken for benchmark genesis. It corresponds to the existing runtime queued lifecycle state. This approval does not define a general delivery-status taxonomy beyond the existing schema/runtime contract.

## 5. Runtime compatibility

Production runtime already defines the delivery lifecycle states `Queued`, `Delivered`, and `Failed`, with `MarkDelivered()` allowing only `Queued -> Delivered`.

The production implementation MUST demonstrate that the approved persistent genesis record can bind to that runtime lifecycle without changing content identity. A successful delivery transition may update delivery lifecycle fields according to runtime authority, but MUST NOT mutate the referenced claim identity or `content_digest` meaning.

## 6. Required production proof

Before this package may be counted as accepted, #265 MUST prove all of the following on the production path:

1. exact 20,000 `information.delivery` record materialization;
2. actual InformationClaim Ref closure;
3. exact `sender_ref == referenced claim claimant_ref` for all 20,000 records;
4. actual Resident recipient Ref closure;
5. exact one-recipient mapping and no benchmark self-delivery;
6. actual CommunicationService Ref closure;
7. exact two deliveries per CommunicationService;
8. exact `eligible_step=0`, `delivered_step=NONE`, `priority=0`, `status=queued` genesis;
9. byte-for-byte equality between Delivery `content_digest` and referenced InformationClaim `content_digest`;
10. canonical recipient list ordering;
11. production payload validation and secondary-index rebuild compatibility;
12. full 20,000-record production Snapshot encode / recovery / semantic rehash;
13. runtime `Queued -> Delivered` compatibility without content-identity mutation;
14. fail-closed negative proof for missing/wrong content, sender mismatch, missing/wrong recipient, missing/wrong channel, self-delivery, duplicate Delivery identity, wrong digest, invalid status, delivered-step/status mismatch, priority/eligible-step genesis drift, and non-canonical recipient list;
15. current-head full CI.

Only after all production proof succeeds may accounting move to:

```text
Infrastructure accepted  = 450,100 / 500,000
Infrastructure remaining =  49,900
```

The Infrastructure reference-world parent blocker remains active at that point.

## 7. Explicitly unchanged gates

This approval does not alter the following until separately proven:

- `infrastructure.facility_service` remains blocked by missing canonical Physical/Built facility identity authority;
- Operation authority remains `5 / 6`;
- the `infrastructure-service-delivery` Operation family remains fail-closed;
- `referenceWorldMaterialized` remains `false`;
- `authoritativeStepLoopAvailable` remains `false`;
- no remaining Infrastructure or Society/Governance semantics are inferred from type compatibility.

## 8. Integration order

The required integration order is:

```text
#313 normative documentation
  -> documentation
  -> minimal documentation-to-develop sync
  -> #265 production implementation
  -> full production proof
  -> accounting update
```

Do not count the 20,000 records as accepted merely because this normative document is merged. Acceptance requires the production proof in §6.