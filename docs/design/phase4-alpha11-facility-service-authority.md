# Alpha 1.1 / INT-03 — FacilityService benchmark authority

Status: **Normative benchmark authority**  
Tracking: #240  
Implementation: Draft PR #265

## 1. Scope

This document closes the benchmark-only upstream authority required by `infrastructure.facility_service` and the canonical `infrastructure-service-delivery` Operation family.

It does not define a universal building taxonomy, real-world facility capacity model, or all Physical/Built records. The authority is limited to the `perf.reference.v1` QA-04 benchmark.

## 2. Facility identity owner

Benchmark facility identity is owned by `built.structure`. `physical.presence`, occupancy/collision-shape records, TileScope, or Infrastructure service records MUST NOT be reused as facility identity.

Exactly **15,000** benchmark BuiltStructure records are materialized, local ordinal `f = 0..14,999`.

Record identity:

```text
DerivedIdentity.DeriveEntityId(
  world_id       = perf.reference.v1 WorldId,
  creation_step  = 0,
  creator_domain = physical_built,
  creator_entity = PhysicalPresence[f].record_id,
  creation_kind  = perf.service-facility-structure,
  local_ordinal  = 0)
```

Each facility is grounded in the already approved first-50,000 Physical D0 support authority used by PropertyRight. For facility ordinal `f`, use actual `PhysicalPresence[f]`, its canonical collision-shape record, and its canonical regional TileScope.

BuiltStructure payload:

```text
spatial_scope  = TileScope(PhysicalPresence[f].regional_tile_index)
structure_class = perf.service-facility
geometry_parts = [PhysicalPresence[f].shape_ref]
material_refs  = []
integrity_ppm  = 1,000,000
support_refs   = []
lifecycle      = active
```

Envelope:

```text
revision      = 1
created_step  = 0
retired_step  = NONE
detail_level  = D0
lineage_ref   = NONE
```

The empty material/support lists are explicit benchmark genesis authority. They do not assert that real facilities have no materials or structural supports; no canonical material/support record pool is currently required for this benchmark closure.

## 3. `infrastructure.facility_service` 15,000

Use the existing Infrastructure reference decomposition slice at global ordinals `180,100..195,099`. FacilityService RecordId is the existing descriptor RecordId; no additional identity recipe is introduced for this slice.

For local ordinal `f = 0..14,999`:

```text
facility_ref           = BuiltStructure[f]
service_kind           = perf.facility-service
capacity_per_step      = 1
active_load            = 0
required_resource_refs = []
availability_ppm       = 1,000,000
status                 = active
```

Envelope uses the existing Infrastructure descriptor DetailLevel and standard genesis fields:

```text
revision      = 1
created_step  = 0
retired_step  = NONE
lineage_ref   = NONE
```

`capacity_per_step = 1` is a deterministic benchmark unit, not a general facility-capacity statement. Runtime/profile-specific capacity scaling remains outside this benchmark fixture.

## 4. Canonical service pool

After FacilityService production proof, the workload service pool is complete and is exactly the actual records from:

```text
infrastructure.transport_service     10,000
infrastructure.water_service         10,000
infrastructure.power_service         10,000
infrastructure.communication_service 10,000
infrastructure.facility_service      15,000
```

The pool MUST be sorted by `(partitionId ASCII, RecordId)` exactly as specified by `phase4-alpha11-canonical-workload-v1.md`.

## 5. Infrastructure Operation binding

The previously specified `infrastructure-service-delivery` family becomes implementation-eligible only after the full FacilityService proof succeeds.

For descriptor ordinal `o`:

```text
requester      = Resident(o)
service_ref    = servicePool[o % servicePool.Count]
units          = 1 + o % 100
eligible_from  = injection_step + 1
eligible_until = injection_step + 30
```

Production Operation binding remains:

```text
OperationKind = infrastructure.service.reserve
owner domain  = infrastructure_information
primary target = service_ref
```

Common admission, immutable payload digest, conflict-scope digest, SameStepOrderKey and effective-Step rules remain those of `phase4-alpha11-canonical-workload-v1.md`.

## 6. Required production proof

Before counting the package as accepted:

1. materialize exactly 15,000 BuiltStructure support records;
2. prove every BuiltStructure uses one actual first-15,000 Physical presence shape and actual TileScope;
3. prove unique BuiltStructure identity and no Physical/shape/TileScope identity reuse;
4. materialize exactly 15,000 FacilityService records using the existing descriptor RecordIds;
5. prove one-to-one `FacilityService[f] -> BuiltStructure[f]` closure;
6. validate exact Token/scalar/list/status/envelope values;
7. production Snapshot encode/recovery/semantic rehash for BuiltStructure and FacilityService material;
8. fail closed on missing/wrong Physical presence, shape, TileScope, BuiltStructure, wrong partition, reversed/mismatched ordinal, duplicate identity, payload drift, and population drift;
9. construct the exact 55,000-record sorted service pool;
10. bind `infrastructure-service-delivery` to `infrastructure.service.reserve` and prove immutable payload/scheduling identity;
11. remove the workload Operation blocker only after the above proofs pass;
12. run current-head CI.

## 7. Release boundary

After FacilityService production proof:

```text
Infrastructure accepted 465,100 -> 480,100 / 500,000
Infrastructure remaining  34,900 ->  19,900
Operation authority            5/6 -> 6/6
workload Operation blocker       1 -> 0
```

Infrastructure reference-world completion still remains blocked by the other Infrastructure slices and any parent topology/schema blocker. `referenceWorldMaterialized` and `authoritativeStepLoopAvailable` MUST NOT be flipped solely by this package.
