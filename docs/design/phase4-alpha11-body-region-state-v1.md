# Alpha 1.1 — `BodyRegionStateV1` nested schema

Status: Decided normative design / implementation pending  
Tracking: #240  
Parent: `phase4-alpha11-normative-closure.md`

## 1. Schema identity

```text
schema_id = domain.resident.body-region-state
version   = 1.0
owner     = resident.body_health.body_region_states
```

This is a nested value, not an independent PartitionRecord. It has no RecordId and no independent lifecycle.

## 2. Exact fields and ordinals

```text
1 region_token: Token
2 integrity_ppm: uint32
3 function_capacity_ppm: uint32
4 pain_ppm: uint32
5 injury_load_ppm: uint32
6 disease_load_ppm: uint32
7 impairment_ppm: uint32
8 recovery_ppm: uint32
```

All fields are required. All ppm fields are closed range `0..1,000,000`.

No optional field and no nested Ref exists in schema 1.0.

## 3. Region vocabulary

Exactly seven tokens in 1.0:

```text
body.arm.left
body.arm.right
body.head
body.leg.left
body.leg.right
body.systemic
body.torso
```

Parent list canonical order is ASCII `region_token` ascending. Duplicate region token is invalid. Unknown region token is invalid in 1.0.

Adding a region token requires nested schema minor-version review; existing token meaning must not change.

## 4. Authority semantics

- `integrity_ppm`: local tissue/structure integrity representation.
- `function_capacity_ppm`: region contribution capacity after injury/disease/impairment.
- `pain_ppm`: current local pain intensity.
- `injury_load_ppm`: aggregate local manifestation of authoritative injury facts referenced by parent `injury_refs`.
- `disease_load_ppm`: aggregate local manifestation of authoritative disease facts referenced by parent `disease_refs`.
- `impairment_ppm`: residual/local functional impairment not represented solely by integrity.
- `recovery_ppm`: local recovery potential/progress capacity for deterministic health update.

`injury_load_ppm` / `disease_load_ppm` do not replace injury/disease identity. Persistent identity/evidence remains in parent refs; the nested values are local physiological state.

## 5. Canonical wire/digest

Wire uses field ordinals in §2. Decoder rejects missing/unknown fields for 1.0.

Parent `body_region_states` list is encoded in region-token ASCII order. Parent canonical digest includes each nested normalized value in this order.

MV-DCBOR normalized nested value is an 8-key map with unsigned numeric keys `0..7` corresponding to the semantic field order above. Tokens are ASCII strings; ppm are unsigned integers.

## 6. `perf.reference.v1` genesis

Every materialized `resident.body_health` record contains all seven regions.

Each region begins:

```text
integrity_ppm         = 1,000,000
function_capacity_ppm = 1,000,000
pain_ppm              = 0
injury_load_ppm       = 0
disease_load_ppm      = 0
impairment_ppm        = 0
recovery_ppm          = 1,000,000
```

Parent genesis:

```text
injury_refs = []
disease_refs = []
```

unless another explicit canonical scenario creates an actual injury/disease authority record in the same genesis cut. Benchmark materializer may not set non-zero nested injury/disease load without corresponding parent authority.

## 7. Evolution

Compatible minor evolution may add optional fields only after canonical default/absence semantics are specified. Region vocabulary extension also requires minor-version registration.

Field removal/rename, unit change, authority semantic change, list-order change or conversion to independent identity requires major evolution.

## 8. Blocker removal

`qa04.material.body-region-state-schema-undefined` remains until:

- CLR value type
- strict nested wire codec
- canonical digest integration
- body-health payload round-trip
- negative validation
- canonical Resident body-health materialization
- exact103 Snapshot/recovery semantic rehash

all pass.
