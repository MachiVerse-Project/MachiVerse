# Alpha 1.1 governance incident application authority

Status: **Complete / normative benchmark authority**

Tracking: #240, #265

## 1. Scope

This document fixes the benchmark-only QA-04 application semantics for canonical `perf.reference.v1` family `governance-security-incident-enforcement` after `Qa04CanonicalOperationBindingV1` has bound `governance.incident.register`.

It defines only creation of the authoritative `governance.security_incident` record. It does not define a general incident lifecycle, investigation, adjudication, enforcement, emergency response, or severity model.

## 2. Existing authority reused

Implementation MUST reuse:

- `Qa04CanonicalOperationBindingV1` for immutable Operation payload, primary target, owner domain, scheduling identity, and effective Step;
- `Qa04ReferenceLoadV1` / canonical Resident and Information reference identities;
- `GovernanceSecurityIncidentPayloadV1` and the registered `governance.security_incident` partition/schema;
- `DerivedIdentity.DeriveEntityId` for deterministic benchmark creation identity;
- ordinary registered schema/reference validation authorities.

No existing production vocabulary or lifecycle semantics may be generalized from this benchmark rule.

## 3. Canonical bound input

For family `governance-security-incident-enforcement`, canonical binding supplies exactly:

```text
operation_kind = governance.incident.register
owner_domain   = governance_institution
scope_ref      = society.governance_legal_process[family_ordinal % 10000]
subject_refs   = [resident.identity_lifecycle[family_ordinal]]
fact_event_refs= [information.fact_event[family_ordinal]]
incident_kind  = security-incident
effective_step = injection_step + 1
```

The primary target is the bound legal-process `scope_ref`. Runtime MUST fail closed unless recomputing `Qa04CanonicalOperationBindingV1.Bind(source_descriptor, scheduling_policy_generation)` is byte/semantic equivalent to the supplied binding.

## 4. Benchmark-only missing fields

The canonical Operation does not define incident RecordId, status, or severity. For this QA-04 workload only, they are fixed as follows:

```text
created_record_id = DerivedIdentity.DeriveEntityId(
    world_id      = Qa04ReferenceLoadV1.WorldId,
    creation_step = effective_step,
    domain         = governance_institution,
    creator_id     = operation_id,
    creation_kind  = perf.governance-incident-operation,
    local_ordinal  = 0)

occurred_step = effective_step
status        = active
severity_ppm  = 500000 + (family_ordinal % 500001)
```

Thus `severity_ppm` is deterministic and always within `[500000, 1000000]`. These values are benchmark fixtures, not a general governance severity/status policy.

Retry after successful application MUST derive the same RecordId and reject on collision rather than creating another incident.

## 5. Created incident

The created `GovernanceSecurityIncidentPayloadV1` is exactly:

```text
incident_kind   = bound incident_kind
subject_refs    = bound subject_refs, in canonical order
scope_ref       = bound scope_ref
occurred_step   = effective_step
fact_event_refs = bound fact_event_refs, in canonical order
status          = active
severity_ppm    = benchmark formula above
```

Envelope metadata is exactly:

```text
RecordSchema = StandardDomainPartitionRegistry.Get(governance.security_incident).RecordSchema
Revision     = 1
CreatedStep  = effective_step
RetiredStep  = NONE
DetailLevel  = D0Entity
LineageRef   = NONE
```

Application adds exactly one record to the supplied authoritative `governance.security_incident` state. Every pre-existing incident record remains unchanged and item count increases by exactly one.

## 6. Reference and target validation

Before mutation implementation MUST verify:

- family, OperationKind, owner domain, scheduling identity, effective Step, primary target, Operation bytes/digest, order keys, and bound descriptor equal canonical rebinding;
- partition identity is exactly `governance.security_incident`;
- bound `scope_ref` is exactly the canonical `society.governance_legal_process` target for `family_ordinal % 10000` and resolves to its registered schema;
- the single bound subject is exactly canonical `resident.identity_lifecycle[family_ordinal]` and resolves to that registered schema;
- the single bound fact event is exactly canonical `information.fact_event[family_ordinal]` and resolves to that registered schema;
- created RecordId is non-ZERO and absent from current state;
- created payload and envelope exactly match this document;
- item count increases by one and all pre-existing records remain unchanged.

Any mismatch MUST reject. The handler must not infer substitute subjects, scopes, facts, status, severity, or identity.

## 7. Required proof

Positive proof MUST bind a real canonical governance descriptor, apply it to a canonical-identity incident partition, verify deterministic identity/payload/envelope, count +1, unchanged pre-existing records, and identical replay result from the same pre-state.

Negative proof MUST at minimum reject wrong family, tampered binding, wrong partition identity, missing/wrong-schema scope/subject/fact reference, created-id collision/retry, and canonical target/reference drift.

## 8. Release boundary

Completion changes only:

```text
actual Operation mutation application: 4 / 6 -> 5 / 6
```

It MUST NOT by itself set `authoritativeStepLoopAvailable=true` or `releaseEvidenceCapable=true` and does not prove investigation/enforcement semantics, full authoritative Step, exact-103 replay, determinism matrix, benchmark evidence, or soak.

## 9. Non-generalization

`perf.governance-incident-operation`, `status=active`, and the severity formula are QA-04 benchmark-only authorities introduced because the canonical Operation does not carry those required incident fields. They MUST NOT be reused as general production incident defaults without separate normative authority.
