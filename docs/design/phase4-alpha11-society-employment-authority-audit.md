# Alpha 1.1 Society Employment 80,000 authority audit

Status: **Recommended decision package / review only / approval required**

Tracking: #240, implementation after approval: #265

## Purpose

This document proposes the next low-dependency `perf.reference.v1` Society authority package after the production-proven Governance territorial foundation.

Current Society/Governance accepted accounting before this proposal:

```text
accepted = 1,356,100 / 2,000,000
remaining = 643,900
```

This proposal does not change accepted accounting. `society.employment` contributes only after explicit normative approval, documentation integration, develop sync, full production materialization, real Ref closure, Snapshot/recovery semantic proof, negative proof, and current-head CI.

## Existing authority

The production profile already has all required Ref identity needed for the minimum Employment relation:

- `society.organization`: 10,000 accepted Organization records;
- Resident identity: 1,000,000 canonical Resident records;
- `society.membership_role`: 80,000 production-proven relations using `Organization[i mod 10,000]` and `Resident[i]` for `i=0..79,999`;
- `society.employment`: 80,000 descriptor slots in the QA-04 Society/Governance decomposition;
- typed `SocietyEmploymentPayloadV1` and production Snapshot codec support.

Phase 3 requires Employment to be an actual worker/employer relation rather than merely an occupation label. The schema fixes the payload shape but does not choose benchmark job vocabulary, wage, or pay-period values.

## Exact payload surface

```text
employer_ref: Ref
worker_ref: Ref
job_token: Token
status: Token
started_step: Step
ended_step: Step?
wage_microunit_per_period: Money
pay_period_steps: UInt64
obligation_refs: RefList
```

Money uses canonical currency microunits (`10^-6` currency unit). That unit definition does not itself determine a benchmark wage.

## Recommended benchmark authority

For Employment local ordinal `i = 0..79,999`:

```text
employer_ref              = Organization[i mod 10,000]
worker_ref                = Resident[i]
job_token                 = perf.worker
status                    = active
started_step              = 0
ended_step                = NONE
wage_microunit_per_period = 1,000,000
pay_period_steps           = 1
obligation_refs            = []
```

Identity / envelope:

```text
record_id    = existing QA-04 Society/Governance descriptor RecordId
revision     = 1
created_step = 0
retired_step = NONE
detail_level = D2
lineage_ref  = NONE
```

No new RecordId recipe is introduced.

## Why this mapping is recommended

### Ref mapping

The mapping deliberately reuses the already-approved MembershipRole relation endpoints:

- each of the 10,000 Organizations receives exactly 8 Employment records;
- each Resident ordinal `0..79,999` appears exactly once as worker;
- each `(employer_ref, worker_ref)` pair is unique;
- every worker/employer Ref resolves to existing accepted production authority;
- every benchmark employee is also represented by the already-proven MembershipRole fixture for the same Organization/Resident pair.

This is a `perf.reference.v1` fixture only. It does not imply that all members are employees, that real organizations have eight employees, or that employment requires organization membership in every world configuration.

### `perf.worker`

`perf.worker` is proposed as a benchmark-only job Token. It exists only to give all 80,000 records a deterministic non-empty job classification for serialization/index/Snapshot proof. It does not define a general occupation taxonomy.

### Wage and pay period

`wage_microunit_per_period = 1,000,000` represents exactly one canonical currency unit in microunit encoding. `pay_period_steps = 1` makes the fixture arithmetic minimal and deterministic.

These values are **not** an economic calibration and are not intended to model realistic wages, payroll frequency, purchasing power, inflation, or a universal currency. The Employment payload itself has no currency-token field, so this package must not infer a currency system from the scalar wage.

The purpose of using a positive one-unit fixture rather than zero is to exercise the Money encoding as a real compensation value while avoiding arbitrary real-world wage assumptions.

### Lifecycle and obligation refs

- `status = active`, `started_step = 0`, `ended_step = NONE` represent an active genesis Employment fixture;
- `obligation_refs = []` deliberately avoids fabricating ContractClaim or payment obligations not required by the Employment payload contract;
- a wage scalar in Employment does not by itself create payment, account, or settlement records.

## Dependency order

Production implementation after approval must consume only existing authority:

1. QA-04 Society/Governance descriptor decomposition;
2. accepted Organization 10,000 authority;
3. canonical Resident 1,000,000 authority;
4. already-proven MembershipRole mapping may be used as a relation-coherence proof, but Employment identity remains independent;
5. Employment 80,000 materialization;
6. Employment secondary-index rebuild;
7. Snapshot/recovery semantic proof.

Do not synthesize CurrencyMoney, FinanceAccount, ContractClaim, payment, worksite, or physical work records merely to satisfy this benchmark Employment package.

## Required production proof after approval

Before Employment contributes to accepted accounting, #265 must prove at current head:

1. exact 80,000 Employment records;
2. exact QA-04 descriptor RecordId/envelope binding;
3. actual Organization Ref closure;
4. actual Resident Ref closure;
5. exactly 8 Employment records per Organization;
6. exactly one Employment for each Resident ordinal `0..79,999`;
7. unique `(employer_ref, worker_ref)` relations;
8. exact coherence with the already-proven MembershipRole Organization/Resident pair set;
9. exact `perf.worker`, `active`, Step 0, ended NONE semantics;
10. exact wage `1,000,000` microunit and pay period `1` semantics;
11. exact empty `obligation_refs`;
12. full production payload validation;
13. required secondary-index rebuild for employer and worker indexes;
14. full production Snapshot encode / recovery / semantic rehash;
15. negative proof for missing/wrong Organization or Resident Ref, wrong Token/status, lifecycle drift, wage/pay-period drift, unexpected obligation refs, duplicate relation/identity, and membership-pair coherence drift;
16. current-head full CI.

Only after all gates succeed may accepted accounting move:

```text
Society/Governance accepted = 1,436,100 / 2,000,000
Society/Governance remaining =   563,900
```

The Society/Governance parent reference-world blocker remains active at that intermediate point.

## Non-decisions

This proposal does not decide:

- general occupation/job taxonomy;
- realistic wage distributions or labor-market calibration;
- payroll schedules outside `perf.reference.v1`;
- currency issuance or exchange semantics;
- FinanceAccount or payment/settlement authority;
- labor law, tax, benefits, contracts, union rules, work schedules, workplace location, or physical work execution;
- whether organization membership is universally required for employment.

## Approval boundary

This document is review-only until #240 explicitly approves all of the following together or individually overrides them:

1. `Organization[i mod 10,000]` employer mapping;
2. `Resident[i]` worker mapping for ordinals `0..79,999`;
3. `job_token = perf.worker`;
4. `status = active`;
5. `started_step = 0`, `ended_step = NONE`;
6. `wage_microunit_per_period = 1,000,000`;
7. `pay_period_steps = 1`;
8. `obligation_refs = []`;
9. existing QA-04 descriptor/envelope identity;
10. the production proof gates above.

Before approval, #265 must not implement these benchmark semantic values as canonical Employment authority.
