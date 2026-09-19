# Alpha 1.1 / INT-03 — Remaining Society benchmark authority

Status: **Normative benchmark authority**  
Tracking: #240  
Implementation: Draft PR #265

## 1. Scope

This document closes the final **89,800** Society records of the `perf.reference.v1` Society/Governance 2,000,000-record decomposition:

- `society.business_production` 30,000
- `society.logistics_obligation` 40,000
- `society.history_lineage` 19,800

These rules are benchmark-only. They do not define universal production recipes, real-world logistics economics, or general historical lineage semantics.

## 2. Upstream authority

This package depends only on already-approved production authority:

- actual `society.organization` 10,000 records;
- actual first-50,000 Physical D0 records from the PropertyRight support authority;
- actual `built.structure` 15,000 benchmark facility identities from the FacilityService authority;
- existing Society/Governance reference decomposition descriptor identities.

No descriptor-only or permissive synthetic reference resolver may substitute for those actual records in the production proof.

## 3. `society.business_production` 30,000

Use the existing slice at global Society/Governance ordinals `1,400,200..1,430,199`. RecordId is the existing descriptor RecordId.

For local ordinal `b = 0..29,999`:

```text
organization_ref    = Organization[b % 10,000]
recipe_token        = perf.reference-production-plan
planned_quantity    = 1
completed_quantity  = 0
input_refs          = []
output_refs         = []
work_required       = 1
energy_required_mj  = 0
status              = planned
```

The empty input/output lists mean that the benchmark genesis plan has not yet reserved concrete material inputs or created output assets. They do not mean that production generally requires no inputs or produces no outputs.

Envelope:

```text
revision      = 1
created_step  = 0
retired_step  = NONE
detail_level  = descriptor D2
lineage_ref   = NONE
```

## 4. `society.logistics_obligation` 40,000

Use the existing slice at global ordinals `1,430,200..1,470,199`. RecordId is the existing descriptor RecordId.

For local ordinal `l = 0..39,999`:

```text
shipper_ref      = Organization[l % 10,000]
consignee_ref    = Organization[(l + 1) % 10,000]
cargo_refs       = [PhysicalPresence[l]]
quantity         = 1
origin_ref       = BuiltStructure[l % 15,000]
destination_ref  = BuiltStructure[(l + 1) % 15,000]
due_step         = 30
status           = planned
carrier_ref      = Organization[(l + 2) % 10,000]
```

The selected PhysicalPresence is an actual first-50,000 benchmark Physical record. The origin and destination are actual distinct benchmark facilities. The mapping is a deterministic benchmark workload fixture and does not establish a general rule that every physical presence is shippable or that every organization is a logistics carrier.

Envelope is revision 1 / created Step 0 / descriptor D2 / no retirement / no lineage ref.

## 5. `society.history_lineage` 19,800

Use the existing slice at global ordinals `1,580,200..1,599,999`. RecordId is the existing descriptor RecordId.

For local ordinal `h = 0..19,799`:

```text
subject_ref      = BusinessProduction[h]
history_kind     = perf.genesis-production
parent_refs      = []
basis_step       = 0
causality_digest = BusinessProduction[h].CanonicalDigest()
```

The empty parent list is the explicit benchmark genesis boundary. `causality_digest` MUST be the actual canonical payload digest of the referenced BusinessProduction record and MUST fail closed if that subject payload drifts.

Envelope is revision 1 / created Step 0 / descriptor D2 / no retirement / no lineage ref.

## 6. Required production proof

Before this package counts as accepted:

1. materialize exact counts 30,000 / 40,000 / 19,800 using existing descriptor RecordIds;
2. validate every BusinessProduction organization relation against actual Organization records;
3. validate every Logistics shipper/consignee/carrier against actual Organization records;
4. validate every Logistics cargo against the actual first-50,000 Physical authority;
5. validate every Logistics origin/destination against actual BuiltStructure facility authority and prove they differ;
6. validate exact Token/scalar/optional/list/envelope values;
7. validate every HistoryLineage subject against actual BusinessProduction and exact canonical payload digest;
8. prove RecordId uniqueness and the benchmark one-record-per-cargo / one-lineage-per-subject relations where specified;
9. run production Snapshot encode / recovery / semantic rehash for all three partitions;
10. fail closed on missing/wrong partition refs, ordinal relation drift, status/scalar/list drift, source-digest drift, duplicate identity/relation, and population drift;
11. keep unrelated Governance and release gates unchanged;
12. run current-head CI.

## 7. Release boundary

After full proof:

```text
Society accepted             1,510,200 -> 1,600,000 / 1,600,000
Society remaining               89,800 ->         0
Society/Governance accepted  1,651,200 -> 1,741,000 / 2,000,000
Society/Governance remaining   348,800 ->   259,000
```

This closes the Society portion of the 2,000,000-record decomposition. Governance remains 259,000 records. The Society/Governance reference-world parent blocker remains active until the Governance portion and its independent nested/schema gates are closed.

`referenceWorldMaterialized` and `authoritativeStepLoopAvailable` remain false until their independent gates are satisfied.
