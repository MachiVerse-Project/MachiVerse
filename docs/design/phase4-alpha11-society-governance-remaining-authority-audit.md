# Alpha 1.1 Society/Governance残authority監査

Status: **MembershipRole 80,000 production-proven; Governance Jurisdiction 10,000 recommended decision package / review only; 他673,900は未決定**

Tracking: #240, #265

## Purpose

This document records the remaining `perf.reference.v1` Society/Governance authority surface after the first benchmark-only authority package and MembershipRole were implemented and production Snapshot/recovery evidence was established.

MembershipRole 80,000 is already normative and production-proven. This revision does not change that authority. It adds a **review-only** recommended package for `governance.jurisdiction` 10,000. The Jurisdiction package MUST NOT be treated as normative, merged into production authority, or counted as accepted until an explicit #240 approval is recorded and the required production proof succeeds.

Current accepted Society/Governance material:

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

The accepted material above has actual reference closure and production Snapshot semantic recovery evidence. This audit proposal does not itself change accepted counts or release flags.

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
| `governance.jurisdiction` | 10,000 | **recommended package below; explicit #240 approval required** |
| `governance.territorial_claim` | 10,000 | claimant Polity/Scope selectors; claim kind; strength; effective Step; basis refs |
| `governance.effective_control` | 20,000 | controller/Scope target; control/security capacity; effective Step; basis refs |
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

The implementation must not satisfy required Ref fields with fabricated records or an exists-everywhere resolver. Investigation depends on SecurityIncident/PublicAuthority, JudicialCase and BorderControl depend on Jurisdiction, and many Society records still need explicit target-pool decisions.

### 3. `governance.law_rule` remains a distinct nested-payload gate

`GovernanceLawRulePayloadV1` carries canonical nested `PredicateAst` and `EffectAst`. Jurisdiction approval does not decide those AST semantics.

### 4. Jurisdiction now has real upstream Ref authority

The current production profile already has:

- `governance.polity`: exactly 1,000 accepted records using the QA-04 Society/Governance descriptor identity;
- Spatial `TileScope`: exactly 4,096 production records for the 64x64 regional tile set, each with an independently derivable `scope_ref`;
- `governance.jurisdiction`: exactly 10,000 descriptor slots in the Society/Governance decomposition;
- typed `GovernanceJurisdictionPayloadV1` and production Snapshot codec support.

Therefore Jurisdiction is no longer blocked by missing Ref identity or Snapshot schema. What remains undecided is the benchmark-specific deterministic mapping and Token/Step vocabulary.

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

Its 80,000-record production proof has completed; it contributes to the current accepted count shown above. Nothing in the Jurisdiction proposal changes MembershipRole semantics.

## Recommended Governance package — `governance.jurisdiction` 10,000

**Status: review only / approval required.**

### Exact payload surface

The production payload is:

```text
polity_ref: Ref
scope_ref: Ref
jurisdiction_kind: Token
subject_classes: TokenList
effective_from: Step
effective_until: Step?
```

The schema and generic Governance design do not themselves select the `perf.reference.v1` values below. The following values are therefore a recommended benchmark fixture, not an inferred domain rule.

### Recommended deterministic mapping

For Jurisdiction local ordinal `i = 0..9,999`:

```text
polity_ref        = Polity[i mod 1,000]
scope_ref         = TileScope[i mod 4,096]
jurisdiction_kind = perf.regional-jurisdiction
subject_classes   = [perf.subject]
effective_from    = 0
effective_until   = NONE
```

Identity/envelope:

```text
record_id    = existing QA-04 Society/Governance descriptor RecordId
revision     = 1
created_step = 0
retired_step = NONE
detail_level = D2
lineage_ref  = NONE
```

No new RecordId recipe is introduced. If current implementation reveals a descriptor/envelope gap, stop and return that gap to normative review.

### Mapping properties

This mapping is intentionally simple and benchmark-only:

- every `polity_ref` resolves to one of the 1,000 actual accepted Polity records;
- every `scope_ref` resolves to one of the 4,096 actual canonical TileScope records;
- each Polity receives exactly 10 Jurisdiction records;
- TileScope ordinals `0..1,807` receive three Jurisdiction records each;
- TileScope ordinals `1,808..4,095` receive two Jurisdiction records each;
- every `(polity_ref, scope_ref)` pair is unique for all 10,000 records because a repeated pair would require an ordinal difference divisible by both 1,000 and 4,096, whose least common multiple is 512,000, larger than the package population;
- `subject_classes` contains one token, so canonical collection ordering is trivial;
- `effective_until = NONE` avoids inventing benchmark expiry events at genesis.

### Recommended Token meaning and boundary

`perf.regional-jurisdiction` and `perf.subject` are **benchmark-only fixture Tokens**.

- `perf.regional-jurisdiction` means only “the QA-04 reference profile's minimal regional jurisdiction record”. It does not define a universal jurisdiction taxonomy.
- `perf.subject` is the reference profile's single catch-all subject-class fixture. It does not define the simulation's general legal subject ontology and does not assert that real jurisdictions have only one subject class.
- The mapping does not imply that all real polities have ten jurisdictions or that jurisdiction boundaries must coincide with regional TileScope boundaries.
- Overlap is permitted by the generic Governance design; this package deliberately exercises multiple jurisdiction records over the same TileScope without deciding TerritorialClaim or EffectiveControl semantics.

### Dependency order

Production materialization for this package must consume only already-existing authority:

1. Society/Governance descriptor decomposition;
2. accepted `governance.polity` 1,000 authority;
3. canonical Spatial TileScope 4,096 authority;
4. Jurisdiction 10,000 materialization;
5. Jurisdiction Snapshot/recovery semantic proof.

This package must not synthesize TerritorialClaim, EffectiveControl, LawRule, JudicialCase, or BorderControl records merely to satisfy downstream relationships.

### Required production proof after approval

Before the 10,000 records can contribute to accepted accounting, #265 must prove at current head:

1. exact 10,000 Jurisdiction records;
2. exact production descriptor RecordId/envelope binding;
3. actual Polity Ref closure against all 1,000 accepted Polity records;
4. actual TileScope Ref closure against all 4,096 canonical TileScope records;
5. exact 10 Jurisdictions per Polity;
6. exact TileScope distribution: 1,808 scopes with 3 and 2,288 scopes with 2;
7. unique `(polity_ref, scope_ref)` pairs;
8. exact `perf.regional-jurisdiction`, `[perf.subject]`, Step 0, and `effective_until = NONE` semantics;
9. full production payload validation;
10. expected secondary-index coverage for `governance.jurisdiction-by-scope` and `governance.jurisdiction-by-polity`;
11. full production Snapshot encode / recovery / semantic rehash;
12. negative proof for missing/wrong Polity Ref, missing/wrong Scope Ref, wrong Token, duplicate relation/identity, effective-period drift, and collection-order drift;
13. current-head full CI.

Only after explicit normative approval, documentation integration, develop sync, and successful production proof may accepted accounting move by 10,000:

```text
Society/Governance accepted = 1,326,100 / 2,000,000
Society/Governance remaining =   673,900
```

The Society/Governance parent reference-world blocker remains active at that intermediate point.

### Downstream effect if accepted

A proven Jurisdiction authority removes the missing-Jurisdiction Ref prerequisite for later `governance.judicial_case` and `governance.border_control` work. It does **not** decide those partitions' own Tokens, mappings, statuses, Steps, nested values, or other dependencies. `governance.law_rule` also remains separately blocked by its canonical PredicateAst/EffectAst authority.

## Governance foundation priority after Jurisdiction

After Jurisdiction, the next foundation candidates remain SecurityIncident and the control/territorial surfaces needed by downstream Investigation, JudicialCase, Enforcement, and BorderControl. They require separate explicit authority decisions.

## Proposed work decomposition — process only

### Package B1 — Jurisdiction decision

Review the 10,000-record recommendation above. If #240 explicitly approves it, convert only this section to normative authority, merge documentation, sync documentation to develop, and implement/prove it in existing #265.

### Package A2 — remaining Society

Resolve Employment, PropertyRight, CurrencyMoney, FinanceAccount, BusinessProduction, LogisticsObligation, Education, Culture, Reputation, and Society lineage according to their actual dependency surfaces.

### Package B2 — remaining Governance foundation

Resolve SecurityIncident, TerritorialClaim, EffectiveControl, and other required foundations before dependent Investigation, JudicialCase, Enforcement, BorderControl, or lineage materialization.

### Package C — LawRule nested authority

Resolve canonical benchmark RuleAst representation independently. Do not accept LawRule until its nested semantic Snapshot path is production-proven.

### Package D — downstream Governance materialization

After foundation authority exists, materialize dependent Governance partitions using only actual registered records and run production Snapshot/recovery semantic proof.

## Acceptance rules

For a partition to contribute to the Society/Governance accepted count, all of the following must be true:

- its benchmark authority is normatively decided;
- its deterministic descriptor-to-payload mapping is implemented;
- every required Ref resolves to an actual record with the expected schema;
- missing authority and unresolved Ref cases fail closed;
- the full canonical record count is materialized through the production payload codec;
- the full partition passes production Snapshot encode and semantic recovery rehash against its canonical header;
- current-head CI is green.

Documentation proposal or approval alone does not increase accepted counts.

## Non-decisions

This audit intentionally does **not** decide general-world employment/job taxonomy, general ownership/property taxonomy, general currency/finance policy, production recipe ontology, education/culture/reputation ontology, tax/diplomacy/security/legal/military taxonomy, realistic initial distributions/economic quantities, universal status transitions, TerritorialClaim/EffectiveControl semantics, or RuleAst business semantics.

The Jurisdiction recommendation is limited to the QA-04 `perf.reference.v1` benchmark fixture and does not define general simulation law/governance behavior.

## Current release boundary

Until the Jurisdiction recommendation is explicitly approved, implemented, and production-proven:

```text
Society/Governance accepted = 1,316,100 / 2,000,000
Society/Governance remaining =   683,900
reference-world parent blockers = 2
referenceWorldMaterialized = false
```
