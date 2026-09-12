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

## Proposed work decomposition — process only

The following is a process decomposition, not a semantic decision:

### Package A — Society remaining audit

Resolve the 11 remaining Society partitions, 464,900 records. Start by identifying which references can target already-authoritative Resident, Organization, Household, Market, ContractClaim, InformationClaim, PublicAuthority, PermissionLicense, Spatial, Physical, and Infrastructure pools, and which require still-unmaterialized Society records.

### Package B — Governance foundation audit

Resolve the records that establish authority used by other Governance records, especially Jurisdiction, SecurityIncident, and any required controller/authority relation. Dependency ordering must be explicit before downstream Investigation, JudicialCase, Enforcement, BorderControl, or lineage materialization.

### Package C — LawRule nested authority

Resolve the canonical benchmark RuleAst representation independently. Do not block unrelated scalar/reference partitions on an invented placeholder AST, and do not accept LawRule until its nested semantic Snapshot path is production-proven.

### Package D — downstream Governance materialization

After the required foundation records are authoritative, materialize dependent Governance partitions using only actual registered records and then run production Snapshot/recovery semantic proof.

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

This audit intentionally does **not** decide:

- general-world employment/job taxonomy;
- general ownership/property taxonomy;
- general currency/finance policy;
- production recipe ontology;
- education/culture/reputation ontology;
- tax/diplomacy/security/legal/military taxonomy;
- realistic initial distributions or economic quantities;
- universal status transitions;
- RuleAst business semantics;
- any Participation, Infrastructure ServiceQueue, or DetailRegion authority.

## Current release boundary

Until additional packages are approved, implemented, and production-proven:

```text
Society/Governance accepted = 1,236,100 / 2,000,000
Society/Governance remaining =   763,900
reference-world parent blockers = 2
referenceWorldMaterialized = false
```

No implementation in PR #265 should infer the missing values from this audit.