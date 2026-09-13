# Alpha 1.1 environment hazard application authority

Status: **Complete / normative benchmark authority**

Tracking: #240, #265

## 1. Scope

This document fixes the benchmark-only QA-04 application semantics for canonical `perf.reference.v1` family `environment-spatial-admin-synthetic` after `Qa04CanonicalOperationBindingV1` has bound `environment.hazard.inject`.

It defines only creation of one authoritative `environment.hazard` record per accepted canonical operation. It does not define a general hazard lifecycle, propagation model, driver inference model, impact model, or production default duration policy.

## 2. Existing authority reused

Implementation MUST reuse `Qa04CanonicalOperationBindingV1`, `Qa04SpatialTileScopeAuthorityV1`, `Qa04EnvironmentCanonicalD0PayloadSourceV1` genesis conventions where explicitly stated below, `EnvironmentHazardPayloadV1`, the registered partition/schema authorities, and `DerivedIdentity.DeriveEntityId`.

The canonical D0 Environment hazard source already establishes that a hazard owns one canonical `spatial_scope`, may have no driver refs, and may list that same scope as its affected scope. Operation-created hazards reuse only those collection/reference conventions; they do not reuse genesis identity or genesis Step metadata.

## 3. Canonical bound input

For family `environment-spatial-admin-synthetic`, current canonical source binding is exactly:

```text
operation_kind  = environment.hazard.inject
owner_domain    = environment
tile            = family_ordinal % Qa04ReferenceLoadV1.RegionalTileCount
scope_ref       = Qa04SpatialTileScopeAuthorityV1.ScopeRef(tile)
hazard_kind     = perf.synthetic-hazard
intensity_ppm   = 100000 + (family_ordinal % 800001)
duration_steps  = 30
primary_target  = scope_ref
effective_step  = injection_step + 1
```

The immutable Operation payload contains `[hazard_kind, scope_ref, intensity_ppm, duration_steps]`.

Runtime MUST fail closed unless recomputing `Qa04CanonicalOperationBindingV1.Bind(source_descriptor, scheduling_policy_generation)` is byte/semantic equivalent to the supplied binding.

## 4. Benchmark-only creation identity and duration mapping

The canonical Operation does not define the persistent Hazard RecordId or the exact `expected_end_step` interpretation. For this QA-04 workload only:

```text
created_record_id = DerivedIdentity.DeriveEntityId(
    world_id      = Qa04ReferenceLoadV1.WorldId,
    creation_step = effective_step,
    domain         = environment,
    creator_id     = operation_id,
    creation_kind  = perf.environment-hazard-operation,
    local_ordinal  = 0)

started_step      = effective_step
expected_end_step = effective_step + duration_steps
```

The addition MUST be checked for overflow. `expected_end_step` is a benchmark fixture boundary, not a general statement about inclusive/exclusive production hazard lifetime semantics. Retry after successful application derives the same RecordId and MUST reject on collision.

## 5. Created hazard

The created `EnvironmentHazardPayloadV1` is exactly:

```text
spatial_scope       = canonical bound scope_ref
hazard_kind         = perf.synthetic-hazard
intensity_ppm       = canonical bound intensity_ppm
started_step        = effective_step
expected_end_step   = effective_step + 30
driver_refs         = []
affected_scope_refs = [canonical bound scope_ref]
```

`driver_refs=[]` and `affected_scope_refs=[spatial_scope]` reuse the existing canonical D0 Environment hazard genesis collection conventions. They are benchmark-only for this operation application and MUST NOT be interpreted as a general rule that injected hazards have no causal drivers or affect exactly one scope.

Envelope metadata is exactly revision 1, CreatedStep=`effective_step`, non-retired, D0Entity, no lineage, and the registered `environment.hazard` record schema.

Application adds exactly one record to authoritative `environment.hazard` state. Every pre-existing hazard record remains unchanged and item count increases by one.

## 6. Validation

Implementation MUST verify:

- family=`environment-spatial-admin-synthetic`, OperationKind=`environment.hazard.inject`, owner=`environment`;
- canonical rebinding equivalence for Operation bytes/digest, scheduling identity/order keys, bound descriptor, effective Step and primary target;
- primary target equals the canonical TileScope ref for `family_ordinal`;
- partition identity is exactly `environment.hazard`;
- bound TileScope resolves to the registered `spatial.scope_registry` schema;
- `hazard_kind`, `intensity_ppm`, and `duration_steps` equal the canonical binding values;
- intensity remains within `[100000, 900000]` and fits the payload `uint32` ratio field;
- duration is exactly `30` for the current canonical workload;
- created RecordId is non-ZERO and absent;
- `expected_end_step` checked addition succeeds;
- created payload/envelope exactly match this document;
- next count is current+1 and all pre-existing records remain unchanged.

Any mismatch MUST reject rather than infer or repair values.

## 7. Proof

Positive proof MUST use a real canonical Environment descriptor and verify deterministic creation, exact scope/kind/intensity/duration mapping, exact payload/envelope, count+1, unchanged pre-existing records, and equivalent replay from the same pre-state.

Negative proof MUST reject at least wrong family, tampered binding, wrong partition identity, wrong/missing TileScope schema authority, target/scope drift, created-id collision/retry, and non-canonical duration/intensity through binding tamper.

## 8. Release boundary

Completion changes only `actual Operation mutation application: 5 / 6 -> 6 / 6` and closes Gate1 Operation-family mutation coverage.

It MUST NOT by itself set `authoritativeStepLoopAvailable=true` or `releaseEvidenceCapable=true`. Full authoritative Step integration, exact-103 recovery/replay, release evidence, worker determinism, benchmark, and soak remain separate gates.

## 9. Non-generalization

`perf.environment-hazard-operation`, the `effective_step + duration_steps` end mapping, empty drivers, and one-scope affected list are QA-04 benchmark-only authorities where the canonical Operation or general domain schema does not define a production policy. They MUST NOT be reused as general production Environment hazard defaults without separate normative authority.