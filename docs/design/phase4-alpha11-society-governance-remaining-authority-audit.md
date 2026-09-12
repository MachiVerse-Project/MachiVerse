# Alpha 1.1 Society/Governance残authority監査

Status: **MembershipRole 80,000 production-proven; Governance territorial foundation 40,000 normative benchmark authority / production proof pending; 他643,900は未決定**

Tracking: #240, #265

Approval: https://github.com/MachiVerse-Project/MachiVerse/issues/240#issuecomment-5646581346

## Purpose

This document records the remaining `perf.reference.v1` Society/Governance authority surface and the normative benchmark authority approved for the Governance territorial foundation.

The following three packages are now explicitly approved as `perf.reference.v1` benchmark authority:

- G1 `governance.jurisdiction`: 10,000;
- G2 `governance.territorial_claim`: 10,000;
- G3 `governance.effective_control`: 20,000.

Approval makes their deterministic benchmark semantics normative. It does **not** make them accepted production material. Accepted accounting moves only after #265 proves the full production materialization, real Ref closure, Snapshot recovery, semantic rehash, negative cases, and current-head CI for each slice.

## Current accepted material

```text
Market              1,000,100
Household               40,000
Polity                    1,000
Organization             10,000
MembershipRole           80,000
Institution               5,000
ContractClaim            60,000
InformationClaim         25,000
PublicAuthority          25,000
PermissionLicense        70,000
------------------------------
accepted              1,316,100 / 2,000,000
remaining               683,900
```

The 40,000 approved Governance records below remain outside accepted accounting until production proof succeeds.

## Remaining surface

### Society — 384,900

| Partition | Count | Initial authority still requiring explicit decision |
|---|---:|---|
| `society.employment` | 80,000 | employer/worker targets; job Token; status; started Step; wage; pay period; obligation refs |
| `society.property_right` | 50,000 | asset/holder target pools; right kind; share; effective Step; optional claim/effective-until policy |
| `society.currency_money` | 100 | currency Token; issuer target; supply; status; policy refs; unit scale |
| `society.finance_account` | 80,000 | owner target; optional institution policy; currency Token; balance/credit; status; ledger digest |
| `society.business_production` | 30,000 | organization target; recipe Token; planned/completed quantity; input/output refs; work/energy; status |
| `society.logistics_obligation` | 40,000 | shipper/consignee/cargo/origin/destination targets; quantity; due/carrier policy; status |
| `society.education` | 25,000 | provider/learner targets; program Token; status; progress; skill refs; started/ended Step policy |
| `society.culture` | 30,000 | subject target; trait Token; affiliation; adoption Step; source refs; status |
| `society.reputation` | 30,000 | subject target; audience scope policy; dimension Token; score/confidence; evidence refs; updated Step |
| `society.history_lineage` | 19,800 | subject target; history kind; parent refs; basis Step; causality digest |
| **Total** | **384,900** | |

### Governance — 299,000

| Partition | Count | Authority state |
|---|---:|---|
| `governance.law_rule` | 30,000 | undecided; canonical PredicateAst/EffectAst gate remains |
| `governance.jurisdiction` | 10,000 | **normative below; production proof pending** |
| `governance.territorial_claim` | 10,000 | **normative below; production proof pending** |
| `governance.effective_control` | 20,000 | **normative below; production proof pending** |
| `governance.tax_fiscal` | 50,000 | undecided |
| `governance.diplomacy` | 10,000 | undecided |
| `governance.security_incident` | 45,000 | undecided; world authority is not implied by workload binding |
| `governance.investigation` | 30,000 | undecided; depends on Incident/PublicAuthority |
| `governance.judicial_case` | 25,000 | undecided; depends on Jurisdiction plus own semantics |
| `governance.enforcement` | 30,000 | undecided; depends on PublicAuthority plus own semantics |
| `governance.military_authority` | 10,000 | undecided |
| `governance.border_control` | 10,000 | undecided; depends on Jurisdiction plus own semantics |
| `governance.lineage` | 19,000 | undecided |
| **Total** | **299,000** | |

## Existing production authority used by G1/G2/G3

The approved packages consume only authority already present in the production profile:

- `governance.polity`: exactly 1,000 accepted records;
- `governance.public_authority`: exactly 25,000 accepted records;
- Spatial `TileScope`: exactly 4,096 canonical records for the 64x64 regional tile set;
- QA-04 Society/Governance descriptor slots: Jurisdiction 10,000, TerritorialClaim 10,000, EffectiveControl 20,000;
- typed payloads and production Snapshot codecs for all three partitions.

No package may use fabricated records or an exists-everywhere resolver.

## Existing normative package — `society.membership_role` 80,000

MembershipRole remains governed by the already-approved and production-proven mapping:

```text
organization_ref = Organization[i mod 10,000]
member_ref       = Resident[i]
role_tokens      = [perf.member]
authority_tokens = []
joined_step      = 0
ended_step       = NONE
status           = active
```

Nothing in G1/G2/G3 changes MembershipRole semantics.

# Normative Governance territorial foundation

The values in this section are **benchmark-only authority for `perf.reference.v1`**. They do not define general-world governance, legal-subject, territorial-claim, or control taxonomies.

## Common identity / envelope boundary

For G1/G2/G3, use the existing QA-04 Society/Governance descriptor identity for the partition record.

```text
record_id    = existing descriptor RecordId
revision     = 1
created_step = 0
retired_step = NONE
detail_level = D2
lineage_ref  = NONE
```

Do not introduce a new RecordId recipe. If implementation discovers a missing descriptor/envelope invariant, stop that slice and return the gap to normative review instead of synthesizing identity semantics in #265.

## G1 — `governance.jurisdiction` 10,000

Exact payload:

```text
polity_ref: Ref
scope_ref: Ref
jurisdiction_kind: Token
subject_classes: TokenList
effective_from: Step
effective_until: Step?
```

For local ordinal `i = 0..9,999`:

```text
polity_ref        = Polity[i mod 1,000]
scope_ref         = TileScope[i mod 4,096]
jurisdiction_kind = perf.regional-jurisdiction
subject_classes   = [perf.subject]
effective_from    = 0
effective_until   = NONE
```

Normative properties:

- every `polity_ref` resolves to one of the 1,000 accepted Polity records;
- every `scope_ref` resolves to one of the 4,096 canonical TileScope records;
- each Polity receives exactly 10 Jurisdiction records;
- TileScope ordinals `0..1,807` receive exactly 3 records each;
- TileScope ordinals `1,808..4,095` receive exactly 2 records each;
- all 10,000 `(polity_ref, scope_ref)` pairs are unique because `lcm(1,000, 4,096) = 512,000 > 10,000`;
- `subject_classes` has one Token, so canonical list ordering is trivial;
- `effective_until = NONE` introduces no synthetic expiry event.

Token boundary:

- `perf.regional-jurisdiction` means only the QA-04 benchmark regional-jurisdiction fixture;
- `perf.subject` means only the QA-04 catch-all subject-class fixture;
- neither Token defines a universal legal taxonomy;
- the fixture does not imply that real polities have ten jurisdictions or that jurisdiction boundaries must coincide with TileScope boundaries.

Required production proof:

1. exact 10,000 materialized records;
2. exact descriptor/envelope identity binding;
3. actual Polity and TileScope Ref closure;
4. exact 10-per-Polity and 3/2-per-TileScope distribution;
5. unique relation pairs;
6. exact Token / Step / NONE semantics;
7. production payload validation;
8. secondary-index coverage for `governance.jurisdiction-by-scope` and `governance.jurisdiction-by-polity`;
9. Snapshot encode / recovery / semantic rehash;
10. negative proof for missing/wrong Ref, wrong Token, duplicate relation/identity, effective-period drift, and collection-order drift;
11. current-head full CI.

After G1 proof succeeds, accepted accounting may move by 10,000 only.

## G2 — `governance.territorial_claim` 10,000

Exact payload:

```text
claimant_polity_ref: Ref
scope_ref: Ref
claim_kind: Token
strength_ppm: Ratio
effective_from: Step
effective_until: Step?
basis_refs: RefList
```

For local ordinal `i = 0..9,999`:

```text
claimant_polity_ref = Polity[i mod 1,000]
scope_ref           = TileScope[i mod 4,096]
claim_kind           = perf.territorial-claim
strength_ppm         = 1,000,000
effective_from       = 0
effective_until      = NONE
basis_refs           = []
```

Normative properties:

- every claimant and scope Ref resolves to actual production authority;
- each Polity receives exactly 10 claim records;
- TileScope distribution is the same 3/2 split as G1;
- all 10,000 `(claimant_polity_ref, scope_ref)` pairs are unique;
- `strength_ppm = 1,000,000` is a full-strength benchmark **claim**, not recognized sovereignty and not effective control;
- `basis_refs = []` deliberately does not fabricate evidence or legal-instrument records at genesis;
- overlapping claims remain permitted by the generic Governance model.

`perf.territorial-claim` is a benchmark-only Token and does not define a general territorial-claim taxonomy.

Required production proof:

1. exact 10,000 materialized records;
2. actual Polity / TileScope closure;
3. exact 10-per-Polity, 3/2-per-TileScope distribution, and unique relation pairs;
4. exact Token / strength / Step / NONE / empty-basis semantics;
5. ratio-bound and production payload validation;
6. secondary-index coverage for claim-by-scope and claim-by-polity indexes;
7. Snapshot encode / recovery / semantic rehash;
8. negative proof for missing/wrong Ref, out-of-range strength, wrong Token, duplicate relation/identity, unexpected basis refs, and effective-period drift;
9. current-head full CI.

After G2 proof succeeds, accepted accounting may move by 10,000 only.

## G3 — `governance.effective_control` 20,000

Exact payload:

```text
controller_ref: Ref
scope_ref: Ref
control_ppm: Ratio
security_capacity_ppm: Ratio
effective_from: Step
basis_refs: RefList
```

For local ordinal `i = 0..19,999`:

```text
controller_ref   = PublicAuthority[i]
scope_ordinal    = i mod 4,096
scope_ref         = TileScope[scope_ordinal]
records_on_scope = (scope_ordinal < 3,616) ? 5 : 4
control_ppm           = 1,000,000 / records_on_scope
security_capacity_ppm = 1,000,000 / records_on_scope
effective_from        = 0
basis_refs             = []
```

Therefore:

```text
TileScope 0..3,615     => 5 records each; 200,000 ppm each
TileScope 3,616..4,095 => 4 records each; 250,000 ppm each
```

Normative properties:

- `controller_ref` uses PublicAuthority ordinals `0..19,999`, each exactly once;
- all scope Refs resolve to canonical TileScope authority;
- 3,616 TileScopes receive exactly 5 records and 480 receive exactly 4;
- every TileScope has aggregate `control_ppm = 1,000,000` exactly;
- the benchmark also uses aggregate `security_capacity_ppm = 1,000,000` exactly;
- all `(controller_ref, scope_ref)` pairs are unique because every controller is unique in this package;
- `basis_refs = []` deliberately does not fabricate claim/evidence dependencies;
- TerritorialClaim and EffectiveControl remain distinct concepts. Neither implies the other.

Required production proof:

1. exact 20,000 materialized records;
2. actual PublicAuthority / TileScope closure;
3. exact 20,000 unique PublicAuthority controllers from ordinals `0..19,999`;
4. exact 3,616 scopes with 5 records and 480 scopes with 4;
5. exact 200,000 / 250,000 ppm rule;
6. exact aggregate 1,000,000 `control_ppm` per TileScope;
7. exact benchmark aggregate 1,000,000 `security_capacity_ppm` per TileScope;
8. production payload validation and ratio bounds;
9. secondary-index coverage for control-by-scope and control-by-controller indexes;
10. Snapshot encode / recovery / semantic rehash;
11. negative proof for missing/wrong controller/scope Ref, aggregate overflow, ppm drift, duplicate relation/identity, and unexpected basis refs;
12. current-head full CI.

After G3 proof succeeds, accepted accounting may move by 20,000 only.

## Production integration order

For each approved slice:

1. this normative documentation is merged into `documentation`;
2. `documentation` is synchronized to `develop` through the permanent-branch integration path;
3. existing #265 consumes the synchronized normative authority;
4. production materializer and real reference resolver paths are implemented;
5. the complete partition is materialized through the production codec;
6. Snapshot encode / recovery / semantic rehash and negative proof are executed;
7. current-head full CI must be green;
8. only then may #240 accepted accounting move for that proven slice.

The implementation must not synthesize LawRule, JudicialCase, BorderControl, SecurityIncident, Investigation, Enforcement, or other missing records merely to satisfy downstream relationships.

## Downstream effect

A proven G1 removes the missing-Jurisdiction Ref prerequisite for later `governance.judicial_case` and `governance.border_control` work. It does not decide those partitions' own Tokens, statuses, mappings, Steps, or dependencies.

G2 and G3 establish benchmark claim/control foundations but do not equate territorial claim with effective control.

`governance.law_rule` remains separately blocked by canonical PredicateAst/EffectAst authority.

`governance.security_incident` remains undecided because `incident_kind`, `subject_refs`, `fact_event_refs`, severity, status, and initial-world event semantics are not implied by the already-completed governance-security workload binding.

## Acceptance accounting after all G1/G2/G3 proofs

Only if all three production proofs succeed:

```text
Society/Governance accepted = 1,356,100 / 2,000,000
Society/Governance remaining =   643,900
```

The Society/Governance parent reference-world blocker remains active. `referenceWorldMaterialized` remains false until all required reference-world conditions are satisfied.

## Non-decisions

This authority does **not** decide general-world employment/job taxonomy, ownership/property taxonomy, currency/finance policy, production recipe ontology, education/culture/reputation ontology, tax/diplomacy/security/legal/military taxonomy, realistic initial distributions/economic quantities, universal status transitions, SecurityIncident semantics, JudicialCase semantics, BorderControl semantics, or RuleAst business semantics.

G1/G2/G3 are strictly `perf.reference.v1` benchmark fixtures.

## Current release boundary

Until G1/G2/G3 are production-proven:

```text
Society/Governance accepted = 1,316,100 / 2,000,000
Society/Governance remaining =   683,900
reference-world parent blockers = 2
referenceWorldMaterialized = false
```
