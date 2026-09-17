# Alpha 1.1 Infrastructure service reserve application authority

Status: **Complete / normative benchmark authority**

Tracking: #240, #265

## 1. Purpose

This document fixes the benchmark-only runtime application semantics for the canonical `perf.reference.v1` `infrastructure-service-delivery` Operation family after the existing `infrastructure.service.reserve` binding has succeeded.

It closes only the representational boundary between the already-bound Operation and authoritative `infrastructure.service_queue` state. It does not define a general queue allocator, cancellation algorithm, service-completion policy, or universal reservation model.

## 2. Existing authority reused

Implementation MUST reuse, without redefining:

- `Qa04CanonicalOperationBindingV1` for the canonical Operation and scheduling identity;
- `Qa04InfrastructureCanonicalServicePoolV1` for the actual service target;
- canonical Resident identity authority for the requester;
- `InfrastructureServiceQueuePayloadV1` and the registered `infrastructure.service_queue` record schema;
- `StandardDomainPayloadCodecValidatorV1` for reference/schema validation;
- `DerivedIdentity.DeriveEntityId` for deterministic runtime-created record identity.

No new PartitionId, record schema, OperationKind, or service-capacity authority is introduced.

## 3. Canonical reserve input

For a source descriptor in family `infrastructure-service-delivery`, the existing canonical binding defines:

```text
operation_kind = infrastructure.service.reserve
requester_ref  = Resident[family_ordinal]
service_ref    = CanonicalInfrastructureServicePool[family_ordinal]
units          = 1 + (family_ordinal mod 100)
eligible_from  = injection_step + 1
eligible_until = injection_step + 30
effective_step = injection_step + 1
semantic_priority = 0
```

Runtime application MUST fail closed if the supplied binding differs from recomputing the canonical binding from its source descriptor and scheduling-policy generation.

## 4. Eligible-range persistence boundary

The standard Operation catalog requires `infrastructure.service.reserve` to carry an eligible range, while the standard `infrastructure.service_queue` record schema stores only `eligible_step` and has no `eligible_until` field.

For `perf.reference.v1` only, the upper endpoint has the following normative meaning:

- `eligible_from` is the first Step at which the request may be accepted into authoritative queue state;
- the canonical Operation executes exactly at `eligible_from`;
- `eligible_until` is an immutable pre-enqueue validity boundary carried by the durable Operation payload;
- successful enqueue consumes that eligibility decision;
- after enqueue, the request does **not** implicitly expire at `eligible_until`;
- subsequent lifetime is governed only by ordinary queue allocation/cancellation/service lifecycle authority.

Therefore the authoritative queue record persists:

```text
eligible_step = eligible_from
```

and does not duplicate `eligible_until` into a field that does not exist.

This is a benchmark-only semantic decision. It does not establish general production meaning for reservation-window expiry. Implementations MUST NOT silently apply this rule to non-`perf.reference.v1` Operations.

## 5. Runtime-created ServiceQueue record

Applying one canonical reserve Operation creates exactly one new `infrastructure.service_queue` record:

```text
service_ref       = bound canonical service_ref
requester_ref     = canonical requester_ref
eligible_step     = eligible_from
semantic_priority = 0
requested_units   = units
allocated_units   = 0
status            = queued
```

The record envelope is:

```text
revision      = 1
created_step  = effective_step
retired_step  = NONE
detail_level  = D2RegionalAggregate
lineage_ref   = NONE
```

`queued` means accepted and waiting for later allocation. Applying the reserve Operation MUST NOT directly mutate service capacity/load and MUST NOT emit a completed service result.

## 6. Runtime record identity

The runtime-created queue request identity is:

```text
DerivedIdentity.DeriveEntityId(
  world_id,
  creation_step = effective_step,
  creator_domain = infrastructure_information,
  creator_entity_id = operation_id,
  creation_kind = perf.service-queue-request,
  local_ordinal = 0)
```

Consequences:

- identical canonical input produces the same RecordId;
- one Operation can create only one benchmark queue request;
- reapplying the same Operation to a state already containing that RecordId MUST fail closed as a duplicate;
- identity does not depend on worker completion order, dictionary order, or runtime enumeration order.

This runtime identity is separate from the specialized genesis identity `Qa04ReferenceScenariosV1.InfrastructureServiceRequestId(q)` used by the initial 250,000 queue records.

## 7. Validation and fail-closed rules

Before constructing the next partition state, implementation MUST verify:

- family is exactly `infrastructure-service-delivery`;
- OperationKind is exactly `infrastructure.service.reserve`;
- owner domain is exactly `infrastructure_information`;
- effective Step equals `eligible_from`;
- the supplied binding is byte-for-byte equivalent to canonical rebinding from the source descriptor;
- current partition identity is exactly `infrastructure.service_queue`;
- `service_ref` and `requester_ref` resolve through the ordinary record-schema resolver and their schemas are allowed by the standard migration-aware validator;
- requested units are non-zero;
- generated RecordId is non-zero and not already present.

Any mismatch MUST reject rather than infer or repair input.

## 8. Acceptance boundary

Focused proof for this application authority MUST cover at least:

1. one canonical reserve creates exactly one queue record with the payload/envelope above;
2. deterministic replay from the same source derives the same RecordId and payload;
3. duplicate application rejects;
4. wrong family or tampered binding rejects;
5. unresolved/wrong-schema requester or service references reject.

Completing this authority does **not** by itself set:

```text
authoritativeStepLoopAvailable = true
releaseEvidenceCapable         = true
```

Those flags remain gated on the full authoritative Step loop and subsequent release evidence.

## 9. Explicit non-decisions

This document does not define:

- general `eligible_until` persistence for arbitrary Operations;
- queue expiry/abandonment policy;
- `infrastructure.service.cancel` application;
- service allocation, reservation assignment, or completion;
- service capacity/load mutation;
- other canonical Operation-family application handlers;
- cross-domain service-result intents;
- release PASS criteria beyond the existing #240 boundary.

## 10. Normative boundary

This document is approved benchmark-only authority under the #240 deterministic-authority policy. After integration through `documentation` and minimal synchronization into `develop`, PR #265 may implement exactly this `infrastructure.service.reserve` application behavior while remaining Draft.