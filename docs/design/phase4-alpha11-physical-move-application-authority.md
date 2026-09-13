# Alpha 1.1 Physical move application authority

Status: **Complete / normative benchmark authority**

Tracking: #240, #265

## 1. Purpose

This document fixes the benchmark-only runtime application semantics for the canonical `perf.reference.v1` `physical-item-movement-work` Operation family after the existing `physical.move.request` binding has succeeded.

It closes only the focused QA-04 boundary between the already-bound Operation and authoritative `physical.presence`. It does not define a general locomotion controller, path planner, force/acceleration model, collision response policy, or cross-domain movement consequence.

## 2. Existing authority reused

Implementation MUST reuse, without redefining:

- `Qa04CanonicalOperationBindingV1` for canonical Operation payload, target, scheduling identity, and owner-domain binding;
- `Qa04ReferenceLoadV1` physical D0 descriptor authority for `family_ordinal`;
- `Qa04ReferenceGenesisValueSourceV1.SmallSignedValue` for the canonical `vx` / `vy` values already used by the binding;
- `PhysicalPresencePayloadV1` and the registered `physical.presence` record schema;
- the canonical QA-04 Physical D0 materialization, where the Presence record is revision 1, created at Step 0, D0, non-retired, and has no lineage ref;
- `StandardDomainPayloadCodecValidatorV1` for payload/reference validation;
- `DomainRecordEnvelopeV1<T>.Revise` for the mutation envelope transition.

No new PartitionId, record schema, OperationKind, general Physical motion vocabulary, or physical integration algorithm is introduced.

## 3. Canonical bound input

For a source descriptor in family `physical-item-movement-work`, existing binding authority defines:

```text
operation_kind = physical.move.request
owner_domain   = physical_built
presence_ref   = PhysicalPresence[family_ordinal]
velocity_x     = SmallSignedValue(operation_id, "vx")
velocity_y     = SmallSignedValue(operation_id, "vy")
velocity_z     = 0
constraints    = []
effective_step = injection_step + 1
semantic_priority = 0
```

The immutable Operation payload encodes the target Presence, the exact three-axis desired velocity vector, and an empty constraint list.

Runtime application MUST fail closed if the supplied binding differs from recomputing `Qa04CanonicalOperationBindingV1.Bind(source_descriptor, scheduling_policy_generation)`.

## 4. Benchmark-only application semantics

For this focused QA-04 application boundary, the bound desired velocity vector becomes the next authoritative `physical.presence.linear_velocity` value for the targeted Presence.

The next payload is exactly the current payload except:

```text
linear_velocity.x = SmallSignedValue(operation_id, "vx")
linear_velocity.y = SmallSignedValue(operation_id, "vy")
linear_velocity.z = 0
```

The application MUST preserve without reinterpretation:

- `subject_ref`;
- `frame_ref`;
- `position`;
- `orientation`;
- `angular_rate_urad_s`;
- `shape_ref`;
- `containment_ref`;
- `presence_mode`.

This is a benchmark-specific direct velocity-command application rule. It MUST NOT be generalized into a universal statement that every future `physical.move.request` immediately replaces actual velocity. A future general controller may introduce acceleration, constraints, path following, collision response, or policy-mediated motion and requires its own normative authority.

## 5. Position and occupancy boundary

This application does not integrate position.

`physical.presence.position` remains unchanged in the focused mutation. Position integration remains the responsibility of the authoritative Physical runtime Step path, including the already-defined deterministic integer integration rules.

Because position, orientation, shape, and containment are unchanged by this focused mutation, the application MUST NOT fabricate an occupancy AABB change or collision/contact result. `physical.occupancy` and collision-shape material remain unchanged at this boundary.

Therefore successful application proves only that the canonical movement Operation produces an actual typed Physical-owned state mutation before subsequent Physical runtime integration.

## 6. Target record and one-shot QA-04 boundary

The target MUST be exactly the canonical `physical.presence` record selected by the binding for `family_ordinal`.

The focused QA-04 application starts from the canonical reference-world Presence state and therefore requires the target record to be:

```text
record_id     = bound presence_ref.record_id
record_schema = registered physical.presence schema
revision      = 1
created_step  = 0
retired_step  = NONE
detail_level  = D0
lineage_ref   = NONE
```

Its payload MUST pass ordinary schema/reference validation before mutation.

Revision other than 1 rejects. This is intentional: the focused application is a one-shot proof from the canonical reference-world basis, not a general repeated-movement API. It also makes accidental retry of the same focused mutation fail closed rather than silently incrementing revision twice.

The full authoritative Step integration may later define a broader repeated-operation lifecycle only through explicit normative authority.

## 7. Envelope transition

Application MUST revise the existing target record in place rather than creating a new PhysicalPresence identity.

The result MUST:

```text
RecordId      = unchanged
RecordSchema  = unchanged
Revision      = previous Revision + 1 = 2
CreatedStep   = unchanged (= 0)
RetiredStep   = unchanged (= NONE)
DetailLevel   = unchanged (= D0)
LineageRef    = unchanged (= NONE)
```

The partition item count MUST remain unchanged.

Only the target record may change. Every non-target record in the supplied `physical.presence` partition MUST remain byte/semantic equivalent to its pre-state value.

## 8. Validation and fail-closed rules

Before constructing the next partition state, implementation MUST verify:

- family is exactly `physical-item-movement-work`;
- OperationKind is exactly `physical.move.request`;
- owner domain is exactly `physical_built`;
- primary target is exactly the canonical QA-04 Presence for `family_ordinal`;
- effective Step is exactly `injection_step + 1`;
- supplied Operation, admission, scheduling identity, order keys, target, and bound descriptor are byte-for-byte equivalent to canonical rebinding;
- current partition identity is exactly `physical.presence`;
- exactly one target RecordId exists in the current partition;
- the target is revision 1, created Step 0, non-retired, D0, and lineage NONE;
- current target payload passes `StandardDomainPayloadCodecValidatorV1`;
- the canonical requested Z velocity is zero and the constraint list is empty as fixed by the binding;
- the next target payload passes ordinary payload/reference validation;
- partition item count does not change;
- non-target records do not change.

Any mismatch MUST reject rather than infer, normalize, repair, or silently skip input.

## 9. Required proof

The implementation proof MUST cover at least:

### Positive proof

- bind an actual canonical `physical-item-movement-work` descriptor;
- load the corresponding actual canonical PhysicalPresence into an authoritative typed partition state;
- apply `physical.move.request`;
- observe the same RecordId at revision 2;
- verify `linear_velocity == [canonical vx, canonical vy, 0]`;
- verify every other payload field and envelope identity field is preserved;
- validate the next payload through the ordinary schema/reference validator;
- verify item count is unchanged;
- verify the partition canonical digest changes when the requested velocity differs from the genesis value.

### Negative proof

At minimum reject:

- wrong family;
- wrong OperationKind;
- wrong owner domain;
- wrong primary target;
- wrong effective Step;
- tampered Operation payload or payload digest;
- wrong scheduling-policy generation/order identity;
- wrong partition identity;
- missing target record;
- foreign target RecordId;
- retired target;
- target schema/detail/lineage drift;
- target revision not equal to 1, including retry after successful application;
- invalid target reference closure;
- post-mutation item-count drift.

## 10. Release boundary

Completion of this focused handler changes only the Operation-family actual-mutation count:

```text
actual Operation mutation application: 2 / 6 -> 3 / 6
```

It MUST NOT by itself set:

```text
authoritativeStepLoopAvailable = true
releaseEvidenceCapable = true
```

It does not prove the full authoritative Step, exact-103 recovery/replay after full mutation, determinism matrix, benchmark release evidence, or 24h soak.

## 11. Non-generalization statement

This authority is intentionally restricted to the Alpha 1.1 QA-04 `perf.reference.v1` workload.

In particular, it does not define:

- a universal `physical.move.request` controller contract;
- acceleration or force semantics;
- collision resolution caused by the requested velocity;
- path constraints beyond the canonical empty QA-04 constraint list;
- automatic position integration inside the application handler;
- movement authorization for arbitrary PhysicalPresence records;
- repeated mutation lifecycle after the focused canonical revision-1 basis.

Any such generalization requires separate normative authority.