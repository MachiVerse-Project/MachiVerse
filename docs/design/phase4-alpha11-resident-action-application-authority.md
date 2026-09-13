# Alpha 1.1 Resident action application authority

Status: **Complete / normative benchmark authority**

Tracking: #240, #265

## 1. Purpose

This document fixes the benchmark-only runtime application semantics for the canonical `perf.reference.v1` `participation-control-resident-action` Operation family after the existing `resident.action.request` binding has succeeded.

It closes only the boundary between the already-bound Operation and authoritative `resident.behavior_state`. It does not define a general Resident planner, Diver-input policy, physical action execution, or cross-domain consequence model.

## 2. Existing authority reused

Implementation MUST reuse, without redefining:

- `Qa04CanonicalOperationBindingV1` for the canonical Operation and scheduling identity;
- canonical Resident identity authority for the target Resident;
- `Qa04ParticipationControlModeCanonicalAuthorityV1` for the benchmark control-mode record;
- `ResidentBehaviorStatePayloadV1` and the registered `resident.behavior_state` record schema;
- the P4-05 invariant `one active behavior/resident`;
- `StandardDomainPayloadCodecValidatorV1` for reference/schema validation;
- `DerivedIdentity.DeriveEntityId` for deterministic first-record identity.

No new PartitionId, record schema, OperationKind, or general Resident behavior vocabulary is introduced.

## 3. Canonical action input

For a source descriptor in family `participation-control-resident-action`, the existing canonical binding defines:

```text
operation_kind = resident.action.request
resident_ref   = Resident[family_ordinal]
action_token   = Qa04ReferenceLoadV1.ResidentActivity(resident_id, injection_step)
target_refs    = []
parameters     = { "perf.ordinal" -> family_ordinal }
effective_step = injection_step + 1
semantic_priority = 0
```

Runtime application MUST fail closed if the supplied binding differs from recomputing the canonical binding from its source descriptor and scheduling-policy generation.

The `perf.ordinal` parameter is immutable binding material only. `resident.behavior_state` has no generic parameter bag, therefore it is validated through canonical rebinding and is not copied into persistent Resident state.

## 4. Benchmark control-source boundary

The canonical reference world materializes exactly one `participation.control_mode` record per Resident with:

```text
binding_ref                = NONE
mode                       = autonomous
effective_from             = 0
input_authority_generation = 0
```

For this benchmark handler, `resident.action.request` is accepted only while the supplied control-mode record is exactly that canonical autonomous record for the same Resident.

The resulting BehaviorState uses:

```text
control_source = autonomous
mode           = perf.action-active
```

`perf.action-active` is a benchmark-only BehaviorState mode token meaning that the canonical requested Resident action is the currently active action represented by this record. It MUST NOT be generalized into a universal Resident mode vocabulary.

A Diver binding, non-autonomous control mode, foreign control-mode record, or generation drift MUST reject rather than be interpreted as equivalent input.

## 5. One active BehaviorState per Resident

P4-05 requires `resident.behavior_state` to maintain one active behavior per Resident. The canonical reference world starts this partition empty.

Therefore application has two cases.

### 5.1 First canonical action for the Resident

If no BehaviorState record exists for `resident_ref`, create exactly one record:

```text
resident_ref        = bound canonical resident_ref
mode                = perf.action-active
active_goal_ref     = NONE
active_action_token = bound action_token
action_target_refs  = []
action_started_step = effective_step
control_source      = autonomous
```

Envelope:

```text
revision     = 1
created_step = effective_step
retired_step = NONE
detail_level = Qa04ReferenceLoadV1.ResidentDetailLevel(family_ordinal)
lineage_ref  = NONE
```

### 5.2 Later canonical action for the same Resident

If exactly one BehaviorState record already exists for `resident_ref`, application updates that same RecordId instead of creating another record.

The update MUST:

- require the existing record to be a non-retired QA-04 autonomous action record (`mode=perf.action-active`, `active_goal_ref=NONE`, `control_source=autonomous`, `action_started_step` present);
- require existing `action_started_step < effective_step`;
- preserve `RecordId`, `RecordSchema`, `CreatedStep`, `DetailLevel`, and `LineageRef`;
- increment `Revision` by exactly one using checked arithmetic;
- replace `active_action_token` with the new bound action token;
- keep `action_target_refs=[]`;
- set `action_started_step=effective_step`.

If more than one BehaviorState record targets the Resident, or if an existing record is not compatible with this benchmark-owned state, application MUST fail closed.

The strict Step monotonicity makes retry of the same canonical action reject instead of silently incrementing revision twice.

## 6. First-record identity

The first BehaviorState record created for a Resident is identified by the action that first establishes benchmark behavior state:

```text
DerivedIdentity.DeriveEntityId(
  world_id,
  creation_step = effective_step,
  creator_domain = resident,
  creator_entity_id = operation_id,
  creation_kind = perf.behavior-state,
  local_ordinal = 0)
```

Consequences:

- deterministic replay from the same pre-state and Operation derives the same RecordId;
- the identity does not depend on worker completion order or runtime enumeration order;
- later actions update that existing RecordId rather than deriving a new one;
- a derived RecordId already used by another BehaviorState record MUST reject.

## 7. Resident action consequence boundary

Applying this Operation changes only Resident-owned BehaviorState.

It MUST NOT directly:

- move a PhysicalPresence;
- mutate inventory, market, governance, environment, or infrastructure state;
- claim that the requested action succeeded in an external domain;
- fabricate a cross-domain result or completed action event.

Phase 3 remains authoritative: Resident action output and external-domain consequence are separate boundaries. This benchmark handler establishes the Resident's active action state only.

## 8. Validation and fail-closed rules

Before constructing the next partition state, implementation MUST verify:

- family is exactly `participation-control-resident-action`;
- OperationKind is exactly `resident.action.request`;
- owner domain is exactly `resident`;
- primary target is exactly the canonical Resident reference;
- effective Step is exactly `injection_step + 1`;
- supplied Operation, admission, scheduling identity, order keys, target, and bound descriptor are byte-for-byte equivalent to canonical rebinding;
- current partition identity is exactly `resident.behavior_state`;
- supplied participation control-mode record is the canonical autonomous record for the same Resident;
- the Resident reference resolves through the ordinary schema resolver with an allowed schema;
- target refs are empty as required by the benchmark binding;
- zero/duplicate RecordId and non-monotonic existing action Step reject.

Any mismatch MUST reject rather than infer, normalize, or repair input.

## 9. Acceptance boundary

Focused proof for this application authority MUST cover at least:

1. first canonical action creates exactly one BehaviorState record with the payload/envelope above;
2. deterministic replay from the same pre-state derives the same RecordId and payload;
3. a later canonical action for the same Resident updates the same RecordId and increments revision exactly once;
4. replay/duplicate or non-monotonic action rejects;
5. wrong family, tampered binding, foreign/non-autonomous control mode, unresolved/wrong-schema Resident reference, and duplicate Resident BehaviorState reject.

Completing this authority does **not** by itself set:

```text
authoritativeStepLoopAvailable = true
releaseEvidenceCapable         = true
```

Those flags remain gated on the full authoritative Step loop and subsequent release evidence.

## 10. Explicit non-decisions

This document does not define:

- general autonomous planning or GOAP selection;
- Diver action admission or precedence outside the existing Participation authority;
- action cancellation/completion lifecycle;
- physical/social/economic/institutional execution of the action token;
- non-empty Resident action target lists for this benchmark family;
- other canonical Operation-family mutation handlers;
- release PASS criteria beyond the existing #240 boundary.

## 11. Normative boundary

This document is approved benchmark-only authority under the #240 deterministic-authority policy. After integration through `documentation` and minimal synchronization into `develop`, PR #265 may implement exactly this `resident.action.request` application behavior while remaining Draft.