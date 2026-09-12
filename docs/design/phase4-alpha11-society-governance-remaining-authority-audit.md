# Alpha 1.1 Society/Governance残authority監査

Status: **MembershipRole 80,000 production-proven; Governance territorial foundation 40,000 recommended decision packages / review only; 他643,900は未決定**

Tracking: #240, #265

## Purpose

This document records the remaining `perf.reference.v1` Society/Governance authority surface after the already-approved benchmark packages were implemented and production Snapshot/recovery evidence was established.

MembershipRole 80,000 is already normative and production-proven. This revision does not change that authority. It adds three **review-only** Governance foundation recommendations:

- `governance.jurisdiction`: 10,000;
- `governance.territorial_claim`: 10,000;
- `governance.effective_control`: 20,000.

Each recommendation is independently approvable. None is normative, mergeable into production authority, or countable as accepted until #240 records explicit approval for that slice and the required production proof succeeds.

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

The accepted material above has actual reference closure and production Snapshot semantic recovery evidence. This proposal itself does not change accepted counts or release flags.

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

| Partition | Count | Initial authority still requiring explicit decision |
|---|---:|---|
| `governance.law_rule` | 30,000 | jurisdiction target; priority/specificity; effective Step; status; canonical `PredicateAst` and `EffectAst` |
| `governance.jurisdiction` | 10,000 | **recommended below; explicit #240 approval required** |
| `governance.territorial_claim` | 10,000 | **recommended below; explicit #240 approval required** |
| `governance.effective_control` | 20,000 | **recommended below; explicit #240 approval required** |
| `governance.tax_fiscal` | 50,000 | Polity selector; tax kind/base Token; rate; optional claim/debtor/due policy; status |
| `governance.diplomacy` | 10,000 | party target pools/cardinality; relation kind; status; effective Step; instrument refs; terms digest |
| `governance.security_incident` | 45,000 | incident kind; subject refs; Scope selector; occurred Step; fact refs; status; severity |
| `governance.investigation` | 30,000 | Incident/PublicAuthority dependency; investigator/evidence/suspect refs; status; opened/closed Step policy |
| `governance.judicial_case` | 25,000 | case kind; Jurisdiction dependency; party/evidence/charge refs; status; opened Step; decision policy |
| `governance.enforcement` | 30,000 | PublicAuthority dependency; order kind; subject/target refs; status; issued/effective Step; outcome refs |
| `governance.military_authority` | 10,000 | Polity/unit-or-org targets; command policy; mission Token; objective/scope refs; status; issued Step |
| `governance.border_control` | 10,000 | Jurisdiction/boundary targets; checkpoint/rule refs; status; capacity |
| `governance.lineage` | 19,000 | subject target; predecessor refs; succession kind; effective Step; causality digest |
| **Total** | **299,000** | |

## Findings

### 1. Remaining material is not mechanically implied by payload schemas

Payload contracts define field types, required/optional shape, canonical serialization, and Snapshot codecs. They do not choose benchmark initial-world semantic values. Required numeric zero, empty collections, Token strings, Ref selectors, and optional NONE/present policy require explicit benchmark authority unless already fixed elsewhere.

### 2. Actual references require production authority

The implementation must not satisfy required Ref fields with fabricated records or an exists-everywhere resolver. Investigation depends on SecurityIncident/PublicAuthority; JudicialCase and BorderControl depend on Jurisdiction; many Society records still need explicit target-pool decisions.

### 3. `governance.law_rule` remains a distinct nested-payload gate

`GovernanceLawRulePayloadV1` carries canonical nested `PredicateAst` and `EffectAst`. None of the foundation recommendations below decides those AST semantics.

### 4. The territorial foundation now has real upstream Ref authority

The current production profile already has:

- `governance.polity`: exactly 1,000 accepted records using QA-04 Society/Governance descriptor identity;
- `governance.public_authority`: exactly 25,000 accepted records with actual Institution closure;
- Spatial `TileScope`: exactly 4,096 production records for the 64x64 regional tile set with independently derivable `scope_ref`;
- decomposition slots for Jurisdiction 10,000, TerritorialClaim 10,000, and EffectiveControl 20,000;
- typed payloads and Snapshot codecs for all three partitions.

Therefore these three slices are not blocked by missing Ref identity or Snapshot schema. Their remaining gap is benchmark-specific deterministic mapping, Token/Step values, list policy, and numeric genesis values.

### 5. SecurityIncident remains separate

`governance.security_incident` has `incident_kind`, `subject_refs`, `scope_ref`, `occurred_step`, `fact_event_refs`, `status`, and `severity_ppm`. The existing governance-security workload binding does not define the reference-world target pools or initial-world incident semantics, so this audit does not infer them.

## Required decision categories

For each partition, a normative proposal must explicitly state all applicable categories below:

1. benchmark-only Token vocabulary;
2. actual Ref target partition(s);
3. deterministic selector/cardinality rule;
4. required-list empty/non-empty rule;
5. optional field NONE/present rule;
6. status/lifecycle Token where applicable;
7. Step values;
8. numeric genesis values and units where applicable;
9. canonical digest source where applicable;
10. nested payload authority where applicable;
11. materialization dependency order;
12. production Snapshot/recovery evidence required before accepted-count increase.

## Existing normative package — `society.membership_role` 80,000

MembershipRole remains governed by the already-approved mapping:

```text
organization_ref = Organization[i mod 10,000]
member_ref       = Resident[i]
role_tokens      = [perf.member]
authority_tokens = []
joined_step      = 0
ended_step       = NONE
status           = active
```

Its 80,000-record production proof has completed and contributes to the current accepted count.

# Recommended Governance territorial foundation

All sections below are **review only / approval required**. The schema and generic Governance design do not select these `perf.reference.v1` values; they are explicit benchmark-fixture recommendations, not inferred domain rules.

## Package G1 — `governance.jurisdiction` 10,000

Exact payload:

```text
polity_ref: Ref
scope_ref: Ref
jurisdiction_kind: Token
subject_classes: TokenList
effective_from: Step
effective_until: Step?
```

Recommended local ordinal mapping for `i = 0..9,999`:

```text
polity_ref        = Polity[i mod 1,000]
scope_ref         = TileScope[i mod 4,096]
jurisdiction_kind = perf.regional-jurisdiction
subject_classes   = [perf.subject]
effective_from    = 0
effective_until   = NONE
```

Envelope uses the existing QA-04 descriptor RecordId, revision 1, created Step 0, retired NONE, D2, lineage NONE. No new identity recipe is introduced.

Properties:

- every Ref resolves to actual accepted/canonical production authority;
- each Polity receives exactly 10 Jurisdiction records;
- TileScope ordinals `0..1,807` receive 3 records and `1,808..4,095` receive 2;
- all 10,000 `(polity_ref, scope_ref)` pairs are unique because `lcm(1,000, 4,096) = 512,000 > 10,000`;
- the one-token `subject_classes` list has trivial canonical ordering;
- `effective_until = NONE` introduces no invented expiry event.

Token boundary:

- `perf.regional-jurisdiction` is only the QA-04 minimal regional jurisdiction fixture;
- `perf.subject` is only the QA-04 catch-all subject-class fixture;
- neither Token defines a universal jurisdiction or legal-subject taxonomy;
- the mapping does not imply that real polities have ten jurisdictions or that jurisdiction boundaries must coincide with TileScope boundaries.

Required proof after approval:

1. full 10,000 materialization;
2. descriptor/envelope identity proof;
3. actual Polity and TileScope Ref closure;
4. exact 10-per-Polity and 3/2-per-TileScope distribution;
5. unique relation pairs;
6. exact Token/Step/NONE semantics;
7. production payload validation;
8. `governance.jurisdiction-by-scope` and `governance.jurisdiction-by-polity` index proof;
9. Snapshot encode/recovery/semantic rehash;
10. negative proof for missing/wrong Ref, wrong Token, duplicate relation/identity, effective-period drift, collection-order drift;
11. current-head full CI.

After G1 proof only:

```text
accepted  = 1,326,100 / 2,000,000
remaining =   673,900
```

## Package G2 — `governance.territorial_claim` 10,000

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

Recommended local ordinal mapping for `i = 0..9,999`:

```text
claimant_polity_ref = Polity[i mod 1,000]
scope_ref           = TileScope[i mod 4,096]
claim_kind           = perf.territorial-claim
strength_ppm         = 1,000,000
effective_from       = 0
effective_until      = NONE
basis_refs           = []
```

Envelope uses the existing QA-04 descriptor RecordId, revision 1, created Step 0, retired NONE, D2, lineage NONE.

Properties:

- all claimant and scope Refs resolve to actual production authority;
- each Polity receives exactly 10 claim records;
- TileScope distribution is the same deterministic 3/2 split as G1;
- all 10,000 `(claimant_polity_ref, scope_ref)` pairs are unique;
- `strength_ppm = 1,000,000` represents a full-strength benchmark claim, not recognized sovereignty or effective control;
- `basis_refs = []` explicitly means the genesis benchmark does not fabricate evidence/instrument records;
- overlapping claims remain allowed by the generic Governance model and do not imply control.

`perf.territorial-claim` is benchmark-only and does not define a general claim taxonomy.

Required proof after approval:

1. full 10,000 materialization;
2. actual Polity/TileScope closure;
3. exact 10-per-Polity, 3/2-per-TileScope distribution, unique pairs;
4. exact Token/strength/Step/NONE/empty-basis semantics;
5. ratio bounds and payload validation;
6. expected claim-by-scope and claim-by-polity index proof;
7. Snapshot encode/recovery/semantic rehash;
8. negative proof for missing/wrong Ref, out-of-range strength, wrong Token, duplicate relation/identity, unexpected basis/effective-until drift;
9. current-head full CI.

After G1+G2 proof:

```text
accepted  = 1,336,100 / 2,000,000
remaining =   663,900
```

G2 may also be approved/proven independently; accepted accounting must move only for actually proven slices.

## Package G3 — `governance.effective_control` 20,000

Exact payload:

```text
controller_ref: Ref
scope_ref: Ref
control_ppm: Ratio
security_capacity_ppm: Ratio
effective_from: Step
basis_refs: RefList
```

The generic payload contract requires scope aggregate control to remain bounded. With 20,000 records distributed over 4,096 TileScopes, each scope receives 4 or 5 records. Therefore a constant 1,000,000 control value per record is invalid as a benchmark recommendation.

Recommended local ordinal mapping for `i = 0..19,999`:

```text
controller_ref = PublicAuthority[i]
scope_ordinal  = i mod 4,096
scope_ref      = TileScope[scope_ordinal]
records_on_scope = (scope_ordinal < 3,616) ? 5 : 4
control_ppm             = 1,000,000 / records_on_scope
security_capacity_ppm   = 1,000,000 / records_on_scope
effective_from           = 0
basis_refs               = []
```

Therefore:

```text
TileScope 0..3,615   => 5 records each, control/security = 200,000 ppm each
TileScope 3,616..4,095 => 4 records each, control/security = 250,000 ppm each
```

Envelope uses the existing QA-04 descriptor RecordId, revision 1, created Step 0, retired NONE, D2, lineage NONE.

Properties:

- controller Refs use the first 20,000 of the 25,000 actual accepted PublicAuthority records, each exactly once;
- all scope Refs resolve to canonical TileScope authority;
- every TileScope has aggregate `control_ppm = 1,000,000` exactly;
- this proposal also keeps aggregate benchmark `security_capacity_ppm = 1,000,000` exactly for symmetry;
- all `(controller_ref, scope_ref)` pairs are unique because controller_ref is unique in the 20,000-record package;
- `basis_refs = []` does not fabricate claim/evidence dependencies;
- this does not assert that a territorial claim implies effective control or vice versa.

Required proof after approval:

1. full 20,000 materialization;
2. actual PublicAuthority and TileScope closure;
3. exact 20,000 unique controller refs from PublicAuthority ordinals 0..19,999;
4. exact 3,616 scopes with 5 records and 480 scopes with 4 records;
5. exact 200,000/250,000 ppm rule;
6. exact 1,000,000 aggregate control ppm per TileScope;
7. production payload validation and ratio bounds;
8. expected control-by-scope/controller index proof;
9. Snapshot encode/recovery/semantic rehash;
10. negative proof for missing/wrong controller/scope Ref, aggregate overflow, ppm drift, duplicate relation/identity, unexpected basis refs;
11. current-head full CI.

After G1+G2+G3 proof:

```text
accepted  = 1,356,100 / 2,000,000
remaining =   643,900
```

G3 may be approved/proven independently; accounting changes only after its own production proof succeeds.

## Common dependency order and non-decisions

Production materialization for these packages may consume only already-existing authority:

1. Society/Governance descriptor decomposition;
2. accepted `governance.polity` 1,000;
3. accepted `governance.public_authority` 25,000 where G3 needs it;
4. canonical Spatial TileScope 4,096;
5. approved G1/G2/G3 materializers;
6. per-partition Snapshot/recovery proof.

The implementation must not synthesize LawRule, JudicialCase, BorderControl, SecurityIncident, Investigation, Enforcement, or other missing records merely to satisfy downstream relationships.

Approval of G1 removes the missing-Jurisdiction Ref prerequisite for later JudicialCase and BorderControl work, but does not decide those partitions' own semantics. G2 and G3 establish separate claim/control surfaces; they do not imply equivalence between claim and control.

## Proposed work decomposition — process only

### Package G — territorial foundation

Review G1/G2/G3 independently. For each explicitly approved slice: convert only that slice to normative authority, merge the existing documentation PR, sync documentation to develop, then implement/prove it in existing #265. Do not increase accepted counts before production proof.

### Package A — remaining Society

Resolve Employment, PropertyRight, CurrencyMoney, FinanceAccount, BusinessProduction, LogisticsObligation, Education, Culture, Reputation, and Society lineage according to their actual dependency surfaces.

### Package B — remaining Governance foundation

Resolve SecurityIncident and other required foundations before dependent Investigation, JudicialCase, Enforcement, BorderControl, or lineage materialization.

### Package C — LawRule nested authority

Resolve canonical benchmark RuleAst representation independently. Do not accept LawRule until its nested semantic Snapshot path is production-proven.

## Acceptance rules

A partition contributes to the Society/Governance accepted count only when all are true:

- benchmark authority is normatively decided;
- deterministic descriptor-to-payload mapping is implemented;
- required Refs resolve to actual records with expected schema;
- missing authority and unresolved Refs fail closed;
- full canonical count materializes through production payload codec;
- full partition passes Snapshot encode and semantic recovery rehash;
- required negative proofs pass;
- current-head CI is green.

Documentation proposal or approval alone does not increase accepted counts.

## Non-decisions

This audit intentionally does **not** decide general-world employment/job taxonomy, ownership/property taxonomy, currency/finance policy, production recipe ontology, education/culture/reputation ontology, tax/diplomacy/security/legal/military taxonomy, realistic initial distributions/economic quantities, universal status transitions, SecurityIncident semantics, or RuleAst business semantics.

G1/G2/G3 are limited to the QA-04 `perf.reference.v1` benchmark fixture and do not define general simulation law/governance behavior.

## Current release boundary

Until any recommended package is explicitly approved, implemented, and production-proven:

```text
Society/Governance accepted = 1,316,100 / 2,000,000
Society/Governance remaining =   683,900
reference-world parent blockers = 2
referenceWorldMaterialized = false
```
