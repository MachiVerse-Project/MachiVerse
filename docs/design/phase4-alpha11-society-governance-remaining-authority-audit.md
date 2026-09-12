# Alpha 1.1 Society/Governance remaining authority audit

Status: **Audit / normative proposal pending**

Tracking: #298, #240, #265

## Purpose

This document records the remaining `perf.reference.v1` Society/Governance authority surface after the first benchmark-only authority package was implemented and production Snapshot/recovery evidence was established.

It is an audit and decision checklist, not a semantic decision. Values, mappings, Token vocabularies, numeric genesis values, nested payloads, and dependency edges that are not already fixed by an existing normative document remain undefined until explicitly approved.

Current accepted Society/Governance material:

```text
Market              1,000,100
Household               40,000
Polity                    1,000
Organization             10,000
Institution               5,000
ContractClaim            60,000
InformationClaim         25,000
PublicAuthority          25,000
PermissionLicense        70,000
------------------------------
accepted              1,236,100 / 2,000,000
remaining               763,900
```

The accepted 195,000-record package above has actual reference closure and production Snapshot semantic recovery evidence. This audit does not change accepted counts or release flags.

## Remaining surface

### Society — 464,900

| Partition | Count | Initial authority still requiring explicit decision |
|---|---:|---|
| `society.membership_role` | 80,000 | organization/member target pools; role/authority Token sets; joined Step; ended-state policy; status |
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
| **Total** | **464,900** | |

### Governance — 299,000

| Partition | Count | Initial authority still requiring explicit decision |
|---|---:|---|
| `governance.law_rule` | 30,000 | jurisdiction target; priority/specificity; effective Step; status; canonical `PredicateAst` and `EffectAst` |
| `governance.jurisdiction` | 10,000 | Polity/Scope selectors; jurisdiction kind; subject classes; effective Step |
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

### 1. The remaining material is not mechanically implied by the payload schema

The payload contracts define field types, required/optional shape, canonical serialization, and Snapshot codecs. They do not choose benchmark initial-world semantic values. A required numeric field accepting zero does not imply that zero is the benchmark genesis value. A collection type accepting an empty list does not by itself establish that empty is semantically correct for the benchmark.

### 2. Actual references need a dependency graph before materialization

Several remaining partitions can only be production-valid after upstream authority exists. Examples include Investigation -> SecurityIncident/PublicAuthority, JudicialCase -> Jurisdiction, Enforcement -> PublicAuthority, and BorderControl -> Jurisdiction. Society records likewise require choices for Organization, Resident, asset, spatial, account, cargo, provider, or other target pools.

The implementation must not satisfy these fields with fabricated records or an exists-everywhere resolver.

### 3. `governance.law_rule` is a distinct nested-payload gate

`GovernanceLawRulePayloadV1` carries canonical nested `PredicateAst` and `EffectAst` values. This is not equivalent to choosing ordinary scalar benchmark values. The benchmark needs an explicitly supported canonical nested representation before the 30,000 LawRule records can be accepted.

### 4. `governance.security_incident.incident_kind` is also a workload dependency

A decided canonical incident kind would not only enable the 45,000 initial SecurityIncident records; it is also relevant to closing the pending governance-security Operation family. The world-record decision and workload binding must nevertheless remain separate acceptance checks.

## Required decision categories

For each partition, a normative proposal must explicitly state all applicable categories below:

1. benchmark-only Token vocabulary;
2. actual Ref target partition(s);
3. deterministic selector/cardinality rule;
4. required-list empty/non-empty rule;
5. optional field NONE/present rule;
6. status/lifecycle Token;
7. Step values;
8. numeric genesis values and units;
9. canonical digest source where payload digest fields exist;
10. nested payload authority where applicable;
11. materialization dependency order;
12. production Snapshot/recovery evidence required before accepted-count increase.

## First decision-ready Society package — `society.membership_role` 80,000

This subsection is a **review-only proposal**, not normative authority.

### Why this slice is first

The exact Phase 4 payload is:

```text
organization_ref: Ref
member_ref: Ref
role_tokens: TokenList
authority_tokens: TokenList
joined_step: Step
ended_step: Step?
status: Token
```

The required target pools already exist as production authority:

- Organization: 10,000 actual records;
- Resident identity: 1,000,000 actual records.

Unlike FinanceAccount, HistoryLineage, Diplomacy, or LawRule, this slice requires no digest, no unresolved nested AST, and no still-missing upstream partition.

### Recommended deterministic mapping — review only

For local ordinal `i = 0..79,999`:

```text
organization_ref = Organization[i mod 10,000]
member_ref       = Resident[i]
role_tokens      = [perf.member]
authority_tokens = []
joined_step      = 0
ended_step       = NONE
status           = active
```

Properties:

- every `member_ref` is an actual canonical Resident;
- every `organization_ref` is an actual accepted Organization;
- every Organization receives exactly eight benchmark membership records;
- each of the first 80,000 Residents appears exactly once;
- therefore the `(organization_ref, member_ref)` pair is unique for all 80,000 records;
- list ordering is canonical trivially because `role_tokens` has one token and `authority_tokens` is empty.

`perf.member` is a new benchmark-only candidate Token. Repository search found no existing `perf.member` authority; therefore its use requires explicit normative adoption and is not inferred from existing code.

### Candidate semantic rationale

- `[perf.member]` represents only the minimum benchmark relation needed to make membership records non-empty and reproducible; it is not a universal role taxonomy.
- `authority_tokens = []` deliberately does not assign organization-level authority to every benchmark member.
- `joined_step = 0` matches benchmark genesis materialization.
- `ended_step = NONE` and `status = active` represent a currently active benchmark membership and reuse the already-established benchmark `active` vocabulary style.

The package MUST NOT imply that all real organizations have eight members, that membership is limited to Residents, or that all memberships begin at world step zero outside `perf.reference.v1`.

### Identity / envelope boundary

Use the existing QA-04 descriptor RecordId and envelope rules for this partition where already defined. Do not reuse Organization or Resident RecordIds as MembershipRole RecordIds.

If an implementation-side descriptor gap is discovered, stop and return that identity/envelope surface to normative review rather than inventing a new recipe in #265.

### Required production proof after approval

1. exact 80,000 MembershipRole records;
2. actual Organization Ref closure;
3. actual Resident Ref closure;
4. exact eight memberships per Organization;
5. exact one membership for each Resident ordinal `0..79,999`;
6. unique `(organization_ref, member_ref)` relation;
7. exact `[perf.member]`, empty authority list, step/status semantics;
8. full production payload validation;
9. full production Snapshot encode / recovery / semantic rehash;
10. negative tests for missing Organization, missing Resident, wrong target partition, duplicate relation/identity, invalid Token, ended/status drift, and collection-order drift;
11. current-head full CI.

Only after normative integration and successful production proof may accepted accounting move by 80,000 records:

```text
Society/Governance accepted = 1,316,100 / 2,000,000
Society/Governance remaining =   683,900
```

The Society/Governance parent reference-world blocker remains active at that intermediate point.

## Governance foundation priority after MembershipRole

For Governance, the most strategically valuable next decision remains `governance.security_incident`, because `incident_kind` is also an unresolved governance-security workload dependency. However, that partition still needs an explicit incident Token, subject/scope selectors, severity, fact refs, step/status semantics, and should not be bundled into MembershipRole merely to close the workload gate.

`governance.jurisdiction` is another foundation candidate because JudicialCase and BorderControl can depend on it. `governance.law_rule` remains isolated behind the canonical PredicateAst/EffectAst authority gate.

## Proposed work decomposition — process only

### Package A1 — MembershipRole decision

Review and explicitly accept/replace the 80,000-record package above.

### Package A2 — remaining Society

After MembershipRole, resolve Employment, PropertyRight, CurrencyMoney, FinanceAccount, BusinessProduction, LogisticsObligation, Education, Culture, Reputation, and Society lineage according to their actual dependency surfaces.

### Package B — Governance foundation

Resolve Jurisdiction, SecurityIncident, and required control/authority foundations before Investigation, JudicialCase, Enforcement, BorderControl, or lineage materialization.

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
- the full partition passes production Snapshot encode and semantic recovery rehash against its canonical header.

Documentation approval alone does not increase accepted counts.

## Non-decisions

This audit intentionally does **not** decide general-world employment/job taxonomy, general ownership/property taxonomy, general currency/finance policy, production recipe ontology, education/culture/reputation ontology, tax/diplomacy/security/legal/military taxonomy, realistic initial distributions/economic quantities, universal status transitions, or RuleAst business semantics.

The MembershipRole proposal does not decide those unrelated surfaces.

## Approval boundary

Explicit adoption is required for at least:

1. 80,000 MembershipRole population;
2. `Organization[i mod 10,000]` mapping;
3. `Resident[i]` mapping for `i=0..79,999`;
4. role Token `[perf.member]`;
5. `authority_tokens = []`;
6. `joined_step = 0`;
7. `ended_step = NONE`;
8. `status = active`;
9. reuse of existing descriptor/envelope authority;
10. production proof requirements.

Before explicit approval:

- do not mark this document complete normative authority;
- do not merge #299 as the MembershipRole authority decision;
- do not implement these candidate semantics in #265;
- do not increase accepted counts;
- do not change parent blockers or release flags.

## Current release boundary

Until additional packages are approved, implemented, and production-proven:

```text
Society/Governance accepted = 1,236,100 / 2,000,000
Society/Governance remaining =   763,900
reference-world parent blockers = 2
referenceWorldMaterialized = false
```
