# Alpha 1.1 governance incident application authority

Status: **Complete / normative benchmark authority**

Tracking: #240, #265

## 1. Scope

This document fixes the benchmark-only QA-04 application semantics for canonical `perf.reference.v1` family `governance-security` after `Qa04CanonicalOperationBindingV1` has bound `governance.incident.register`.

It defines only creation of the authoritative `governance.security_incident` record. It does not define a general incident lifecycle, investigation, adjudication, enforcement, emergency response, or severity model.

## 2. Existing authority reused

Implementation MUST reuse `Qa04CanonicalOperationBindingV1`, canonical Resident / spatial-tile / Society InformationClaim identities, `GovernanceSecurityIncidentPayloadV1`, the registered partition/schema authorities, and `DerivedIdentity.DeriveEntityId`.

## 3. Canonical bound input

For family `governance-security`, current canonical source binding is exactly:

```text
operation_kind = governance.incident.register
owner_domain   = governance_security
subject_ref    = resident.identity_lifecycle[family_ordinal]
scope_ref      = canonical spatial tile containing that resident
claim_ref      = canonical society.information_claim decomposition entry for family_ordinal
incident_kind  = perf.incident
primary_target = subject_ref
effective_step = injection_step + 1
```

The immutable Operation payload contains `[incident_kind, [subject_ref], scope_ref, [claim_ref]]`. The domain payload field is named `FactEventRefs`; at this QA-04 boundary its single canonical member is deliberately the bound Society InformationClaim ref. Runtime MUST NOT rewrite it to a different Information record type.

Runtime MUST fail closed unless recomputing `Qa04CanonicalOperationBindingV1.Bind(source_descriptor, scheduling_policy_generation)` is byte/semantic equivalent to the supplied binding.

## 4. Benchmark-only missing fields

The canonical Operation does not define incident RecordId, status, or severity. For this QA-04 workload only:

```text
created_record_id = DerivedIdentity.DeriveEntityId(
    world_id      = Qa04ReferenceLoadV1.WorldId,
    creation_step = effective_step,
    domain         = governance_security,
    creator_id     = operation_id,
    creation_kind  = perf.governance-incident-operation,
    local_ordinal  = 0)

occurred_step = effective_step
status        = active
severity_ppm  = 500000 + (family_ordinal % 500001)
```

`severity_ppm` is deterministic and remains within `[500000, 1000000]`. These are benchmark fixtures, not a general governance status/severity policy. Retry after successful application derives the same RecordId and MUST reject on collision.

## 5. Created incident

The created `GovernanceSecurityIncidentPayloadV1` is exactly:

```text
incident_kind   = perf.incident
subject_refs    = [canonical bound resident ref]
scope_ref       = canonical bound spatial tile ref
occurred_step   = effective_step
fact_event_refs = [canonical bound society.information_claim ref]
status          = active
severity_ppm    = benchmark formula above
```

Envelope metadata is exactly revision 1, CreatedStep=`effective_step`, non-retired, D0Entity, no lineage, and the registered `governance.security_incident` record schema.

Application adds exactly one record to authoritative `governance.security_incident` state. Every pre-existing incident record remains unchanged and item count increases by one.

## 6. Validation

Implementation MUST verify:

- family=`governance-security`, OperationKind=`governance.incident.register`, owner=`governance_security`;
- canonical rebinding equivalence for Operation bytes/digest, scheduling identity/order keys, bound descriptor, effective Step and primary target;
- primary target equals canonical Resident ref, not `scope_ref`;
- partition identity is exactly `governance.security_incident`;
- bound Resident subject resolves to registered `resident.identity_lifecycle` schema;
- bound spatial scope equals `Qa04SpatialTileScopeAuthorityV1.ScopeRef(Qa04ReferenceLoadV1.RegionalTileIndex(resident.RecordId))` and resolves to its registered schema;
- bound claim equals the canonical `Qa04SocietyGovernanceReferenceDecompositionV1` entry for `society.information_claim` and resolves to its registered schema;
- created RecordId is non-ZERO and absent;
- created payload/envelope exactly match this document;
- next count is current+1 and all pre-existing records remain unchanged.

Any mismatch MUST reject rather than infer or repair values.

## 7. Proof

Positive proof MUST use a real canonical governance descriptor and verify deterministic creation, exact refs/payload/envelope, count+1, unchanged pre-existing records, and equivalent replay from the same pre-state.

Negative proof MUST reject at least wrong family, tampered binding, wrong partition identity, wrong/missing subject/scope/claim schema authority, target/reference drift, and created-id collision/retry.

## 8. Release boundary

Completion changes only `actual Operation mutation application: 4 / 6 -> 5 / 6`. It MUST NOT by itself set `authoritativeStepLoopAvailable=true` or `releaseEvidenceCapable=true`.

## 9. Non-generalization

`perf.governance-incident-operation`, `status=active`, and the severity formula are QA-04 benchmark-only authorities because the canonical Operation does not carry those required incident fields. They MUST NOT be reused as general production incident defaults without separate normative authority.
