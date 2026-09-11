# Alpha 1.1 — Society / Governance reference authority

Status: **Proposed normative design / approval pending**  
Tracking: #295  
Parent tracking: #240  
Implementation follow-up: Draft PR #265 after documentation approval/integration

## 1. Purpose

This document proposes the minimum benchmark-specific authority needed to materialize the remaining `perf.reference.v1` Society / Governance genesis slices without inventing general gameplay taxonomy.

The values in this document are **benchmark fixture semantics only**. They do not define universal Organization categories, universal Institution kinds, universal decision systems, universal contract taxonomies, or universal permission taxonomies for MachiVerse.

The proposal follows the existing Phase 3 rule that Organization and Governance structures must not be reduced to one universal fixed classification system. It also follows the Phase 4 benchmark precedent that explicit `perf.*` vocabulary may be fixed for one reproducible profile.

Until this proposal is approved and integrated into the documentation authority, Simulation code must continue to fail closed on the corresponding dependency contracts.

## 2. Scope

This proposal closes only the currently unresolved benchmark authority for these genesis slices:

| partition | count | proposed unresolved authority |
|---|---:|---|
| `society.organization` | 10,000 | `organization_class` |
| `governance.institution` | 5,000 | kind / decision / office genesis semantics |
| `society.contract_claim` | 60,000 | kind / party mapping |
| `society.information_claim` | 25,000 | claimant / token / payload creation step |
| `governance.public_authority` | 25,000 | Institution target / authority token / effective step |
| `governance.permission_license` | 70,000 | PublicAuthority target / permission kind / effective step |

Existing descriptor ranges, RecordId rules, envelope revision, D2 detail, `active` status/lifecycle, TileScope mapping, Resident mapping, deterministic digest rules, and optional-NONE rules remain unchanged.

## 3. Benchmark-only vocabulary

The following `StableToken` values are proposed for `perf.reference.v1` only:

```text
organization_class  = perf.organization
institution_kind    = perf.institution
decision_method     = perf.decision
contract_kind       = perf.contract
claim_token         = perf.claim
authority_token     = perf.authority
permission_kind     = perf.permission
```

Rules:

1. These tokens are explicit constants; they are not generated from a generic hash/value source.
2. They are not registered as exhaustive domain enums.
3. A future gameplay or benchmark profile may use different vocabularies without changing Phase 3 domain semantics.
4. Token collections remain canonical ASCII/token order according to the standard payload rules.

## 4. `society.organization` genesis authority

For local ordinal `i in 0..9,999`:

```text
organization_class = perf.organization
lifecycle          = active
founded_step       = 0
purpose_tokens     = []
parent_refs        = []
facility_refs      = []
```

`OrganizationId` remains the authoritative descriptor RecordId according to the existing implementation boundary.

The single benchmark token does **not** mean all organizations in a normal MachiVerse world are one class. It only gives the reference benchmark a reproducible genesis class value.

## 5. `governance.institution` genesis authority

For local ordinal `i in 0..4,999`:

```text
polity_ref          = Polity[i % 1,000]
institution_kind    = perf.institution
decision_method     = perf.decision
office_refs         = []
selection_rule_ref  = NONE
lifecycle           = active
```

`office_refs = []` is a benchmark-genesis decision only. It means this fixture does not materialize separate office authority records at genesis. It does not state that Governance institutions in the general simulation lack offices.

The empty list is permitted because the standard payload schema requires canonical `office_refs` but does not define it as semantic non-empty at genesis.

## 6. `society.contract_claim` genesis authority

For local ordinal `i in 0..59,999`:

```text
contract_kind = perf.contract
party_refs    = [Resident[i % 1,000,000]]
claimant_ref  = NONE
obligor_ref   = NONE
amount        = NONE
quantity      = NONE
due_step      = NONE
status        = active
```

`party_refs` contains exactly one actual canonical Resident reference and is normalized using the standard Ref ordering rule.

This is a benchmark load fixture, not a universal statement that all real contracts have one party. The standard domain model remains free to represent richer multi-party contracts.

## 7. `society.information_claim` genesis authority

For local ordinal `i in 0..24,999`:

```text
claimant_ref    = Resident[i % 1,000,000]
claim_token     = perf.claim
subject_refs    = []
provenance_refs = []
created_step    = 0
status          = active
```

The payload `created_step` is explicitly `0` and matches the genesis envelope `created_step = 0`.

The deterministic `content_digest` rule already implemented for this slice remains unchanged.

## 8. `governance.public_authority` genesis authority

For local ordinal `i in 0..24,999`:

```text
institution_ref  = Institution[i % 5,000]
holder_ref       = Resident[i % 1,000,000]
authority_tokens = [perf.authority]
scope_refs       = [TileScope[i % 4,096]]
effective_from   = 0
effective_until  = NONE
status           = active
```

The Institution, Resident, and TileScope references must resolve to actual records with the expected production schemas. Missing targets fail closed.

The existing canonical holder and TileScope selector decisions are not changed by this document; this proposal only supplies the previously unresolved Institution/token/effective-step authority.

## 9. `governance.permission_license` genesis authority

For local ordinal `i in 0..69,999`:

```text
subject_ref      = Resident[i % 1,000,000]
authority_ref    = PublicAuthority[i % 25,000]
permission_kind  = perf.permission
scope_refs       = [TileScope[i % 4,096]]
effective_from   = 0
effective_until  = NONE
status           = active
```

The existing deterministic `conditions_digest` rule remains unchanged.

PublicAuthority, Resident, and TileScope references must resolve to actual production records and schemas. Missing targets fail closed.

## 10. Dependency-chain order

After this proposal becomes normative, implementation must preserve actual authority ordering:

```text
Organization
  -> Institution
  -> PublicAuthority
  -> PermissionLicense

Resident + Organization
  -> ContractClaim

Resident
  -> InformationClaim
```

No downstream partition may satisfy reference closure using smoke-only fixture records.

## 11. Expected dependency impact

Current direct canonical sub-dependencies in #240:

```text
23
```

This proposal covers the following 15 Society/Governance dependencies:

```text
Organization      1
Institution       3
ContractClaim     2
InformationClaim  3
PublicAuthority   3
PermissionLicense 3
-------------------
total            15
```

If and only if the documentation decision is integrated **and** production implementation, negative tests, actual Ref closure, and Snapshot/runtime proof succeed, the machine-readable dependency total may move:

```text
23 -> 8
```

Documentation merge alone does not remove implementation blockers.

## 12. Explicit non-decisions

This proposal does not decide:

- general Organization taxonomy;
- general Institution taxonomy;
- general Governance decision systems;
- realistic contract-party cardinality;
- general information-claim ontology;
- general public-authority capability taxonomy;
- general permission/license taxonomy;
- Participation `control_mode` population/identity/token/genesis authority;
- Infrastructure service/requester/genesis queue authority;
- DetailRegion partition/genesis authority;
- release flags or accepted material counts.

Those remain governed by their own requirements and future normative work.

## 13. Acceptance gate

This proposal becomes implementation authority only after explicit project approval and integration through the `documentation` responsibility path.

After integration, Simulation follow-up must:

1. replace the corresponding fail-closed unresolved dependency entries with these exact benchmark rules;
2. add/adjust negative tests so missing actual Ref targets still fail closed;
3. materialize the actual authority chain using production codecs/schemas;
4. run current-head Simulation Core smoke, runtime target, multi-Gateway, and relevant full production proof;
5. update #240 counts only after production evidence succeeds.

Until then, #265 remains Draft and the unresolved dependency contracts stay active.