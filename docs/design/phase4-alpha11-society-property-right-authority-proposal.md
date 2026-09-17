# Alpha 1.1 Society PropertyRight benchmark authority

Status: **Approved normative authority for perf.reference.v1 Alpha 1.1**

Tracking: #240
Approval record: #240 comment 5649496851
Implementation: #265

## Purpose

This document fixes the exact benchmark-only authority for the remaining `society.property_right` 50,000-record slice.

The generic payload accepts an `asset_ref`, but the pre-existing contracts did not choose a particular production partition as an economic property asset. The project owner explicitly approved the target choice below on 2026-09-13. It is therefore normative for this benchmark and must not be generalized merely from Ref/schema compatibility.

## Existing authority and implementation facts

The following are established inputs to this authority:

- `Qa04ReferenceWorldMaterialContractV1` marks `physical.d0-presence` as `ProductionMaterializerAvailable` with canonical count 500,000 and primary partition `physical.presence`;
- `Qa04ReferenceWorldDependencyContractV1` has no remaining Physical Presence materialization blocker;
- `Qa04PhysicalD0MaterializerV1` creates actual `physical.presence` records using the existing QA-04 Physical descriptor RecordId and explicit canonical genesis bindings; descriptor-only refs are not sufficient proof;
- canonical Resident identity authority is available for at least ordinals `0..49,999`;
- the standard PropertyRight payload is `asset_ref`, `holder_ref`, `right_kind`, `share_ppm`, `effective_from`, optional `effective_until`, optional `claim_ref`;
- standard derived indexes are `society.property-by-asset` and `society.property-by-holder`;
- the generic payload validator checks referenced records for existence and an allowed standard record schema, but does not itself choose the economic meaning of `asset_ref`.

## Normative benchmark mapping

For local ordinal `p = 0..49,999`:

```text
asset_ref       = PhysicalPresence[p]
holder_ref      = Resident[p]
right_kind      = perf.ownership
share_ppm       = 1,000,000
effective_from  = 0
effective_until = NONE
claim_ref       = NONE
```

Envelope:

```text
record_id    = existing QA-04 society.property_right descriptor RecordId for p
revision     = 1
created_step = 0
retired_step = NONE
detail_level = D2
lineage_ref  = NONE
```

No new identity recipe is introduced.

## Exact normative semantics

For `perf.reference.v1` Alpha 1.1:

- the first 50,000 actual canonical `physical.presence` records are the benchmark economic asset targets for this PropertyRight slice;
- the first 50,000 canonical Residents are the corresponding benchmark holders, one-to-one by ordinal;
- each selected asset has exactly one PropertyRight record and a full `1,000,000 ppm` share in this slice;
- `perf.ownership` is a benchmark fixture Token, not a complete legal-right ontology;
- `effective_from=0` and `effective_until=NONE` define the benchmark genesis period only;
- `claim_ref=NONE` avoids fabricating a ContractClaim, legal instrument, court judgment, registry entry, or other supporting claim.

## Important boundaries

This authority deliberately does **not** mean:

- that every `physical.presence` record is generally ownable;
- that the Presence record is the universal long-term identity of a legal asset;
- that ownership of Presence implies ownership of its underlying subject, land, structure, container, material, or geometry in the general model;
- that a `physical.presence` is a `built.structure`, facility, building, service endpoint, or Infrastructure `facility_ref`;
- that this decision resolves the `infrastructure.facility_service` blocker;
- that possession, occupancy, effective control, territorial claim, and property ownership are equivalent;
- that transfers, inheritance, liens, leases, layered rights, fractional co-ownership, registration, taxation, or enforcement are defined;
- that the benchmark token `perf.ownership` constrains a future general right-kind ontology.

The mapping is a narrow benchmark fixture selected under explicit project-owner approval because actual canonical Physical Presence authority already exists and the PropertyRight contract intentionally accepts a cross-domain asset Ref.

## Cardinality and uniqueness

The mapping has these exact properties:

- 50,000 PropertyRight records;
- 50,000 distinct actual PhysicalPresence assets;
- 50,000 distinct canonical Resident holders;
- one PropertyRight record per selected asset;
- one selected asset per holder in this benchmark slice;
- every record has `share_ppm=1,000,000`;
- no overlapping share aggregate exists for the selected assets within this slice;
- all `(asset_ref, holder_ref, right_kind)` tuples are unique.

## Mandatory production proof before accepted accounting changes

Accepted accounting must not change until #265 proves all of the following on one current head:

1. exactly 50,000 PropertyRight records through the production payload validator;
2. exact existing QA-04 PropertyRight descriptor/envelope binding for every ordinal;
3. `asset_ref` closure to **actual records produced through the canonical Physical D0 production materialization path** for Physical ordinals `0..49,999`; descriptor-only existence, fabricated Presence records, or an exists-everywhere resolver are forbidden;
4. `holder_ref` closure to actual canonical Resident ordinals `0..49,999`;
5. exactly 50,000 distinct assets and 50,000 distinct holders;
6. exact one-to-one ordinal mapping and unique `(asset, holder, right_kind)` relation keys;
7. exact `perf.ownership`, `share_ppm=1,000,000`, Step 0, effective-until NONE, and claim NONE values;
8. full population share aggregation proving exactly 1,000,000 ppm on every selected asset in this slice;
9. both required secondary indexes rebuilt from production records with exactly 50,000 asset keys x1 and 50,000 holder keys x1;
10. full 50,000-record Snapshot encode / recovery / semantic rehash;
11. fail-closed negative proof for missing/wrong PhysicalPresence, wrong asset partition, missing/wrong Resident, wrong holder partition, wrong Token, share drift/out-of-range, period drift, claim injection, duplicate asset/right relation, duplicate RecordId, population drift, and index cardinality drift;
12. generic Simulation Core smoke plus full current-head CI.

If the implementation cannot source the first 50,000 Presence records through the already-declared production materializer without introducing any additional semantic/genesis decision, the proof must fail closed rather than substituting descriptor refs.

## Acceptance accounting gate

Until all mandatory production proof succeeds:

```text
Society/Governance accepted = 1,601,200 / 2,000,000
Society/Governance remaining =   398,800
Society remaining            =   139,800
Governance remaining         =   259,000
```

Only after all mandatory production proof succeeds on the current #265 head:

```text
Society/Governance accepted = 1,651,200 / 2,000,000
Society/Governance remaining =   348,800
Society remaining            =    89,800
Governance remaining         =   259,000
```

Other reference-world, Infrastructure, workload, Operation, and release flags remain unchanged.

## Explicit non-decisions

This authority does not decide the remaining Society slices:

- `society.business_production` 30,000;
- `society.logistics_obligation` 40,000;
- `society.history_lineage` 19,800.

It also does not alter Governance or Infrastructure authority.