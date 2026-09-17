# Alpha 1.1 Society CurrencyMoney authority

Status: **Approved / normative for `perf.reference.v1`**

Project-owner approval: **2026-09-13**
Tracking: #240
Documentation integration: #324
Production implementation/proof: #265

## Purpose

This document fixes the canonical `perf.reference.v1` authority for the remaining `society.currency_money` 100-record slice.

The exact mapping below is normative for Alpha 1.1 / INT-03 production materialization and proof. It does not define general-world monetary semantics beyond this benchmark fixture.

`finance_account` remains explicitly excluded. Its `ledger_head_digest` and account authority require separate explicit decisions and must not be inferred from CurrencyMoney availability.

## Existing authority consumed

The package closes all required references using authority already accepted in production:

- Organization: 10,000 accepted records;
- existing QA-04 `society.currency_money` descriptor slice: 100 records at Society/Governance local ordinals `320,000..320,099`;
- standard CurrencyMoney payload/schema and Snapshot codecs;
- required derived secondary index `society.currency-by-token`.

No new issuer identity, monetary-policy record, account, balance, exchange-rate record, or RecordId recipe is introduced.

## Canonical benchmark authority — `society.currency_money` 100

For local ordinal `c = 0..99`:

```text
currency_token   = perf.currency-{c:000}
issuer_ref       = Organization[c]
supply_microunit = 0
status           = active
policy_refs      = []
unit_scale       = 6
```

The exact token vocabulary is therefore:

```text
perf.currency-000
...
perf.currency-099
```

### Normative meaning and boundary

- `issuer_ref = Organization[c]` uses 100 distinct already-accepted Society actors. It does not assert that those organizations are central banks, governments, or financial institutions.
- `supply_microunit = 0` means each benchmark currency identity exists at genesis with no issued supply. No initial holdings or monetary distribution are fabricated.
- `status = active` means the currency definition is available for benchmark references; it does not imply circulating supply or acceptance.
- `policy_refs = []` means no fiscal, legal, monetary-policy, or Governance authority is synthesized for this package.
- `unit_scale = 6` is the explicitly approved Alpha 1.1 benchmark representation for these 100 CurrencyMoney records. It is not a universal rule for every possible MachiVerse currency.
- the 100 currency Tokens are opaque benchmark identities. They do not define real-world currency names, exchange rates, convertibility, or a universal monetary taxonomy.

Each CurrencyMoney record has one distinct `currency_token` and one distinct `issuer_ref`. No additional semantic relation between currency count and Organization count is implied.

## Canonical envelope

Use only the existing QA-04 descriptor identity:

```text
record_id    = existing descriptor RecordId for CurrencyMoney local ordinal c
revision     = 1
created_step = 0
retired_step = NONE
detail_level = D2
lineage_ref  = NONE
```

No new RecordId derivation is allowed.

## Required production proof

Before accepted accounting moves, #265 must prove all of the following on the current implementation head:

1. exactly 100 CurrencyMoney records materialized through the production payload validator;
2. exact existing descriptor/envelope binding for all 100 records;
3. `issuer_ref` resolves to actual accepted Organization ordinal `c` for every record;
4. all 100 issuer Refs are distinct;
5. exact token vocabulary `perf.currency-000..099`, with all 100 tokens distinct;
6. exact `supply_microunit=0`, `status=active`, `policy_refs=[]`, `unit_scale=6` semantics;
7. rebuild `society.currency-by-token` from production records and prove exactly 100 keys / 100 records / one record per token;
8. full 100-record Snapshot encode / recovery / semantic rehash;
9. fail-closed negative proof for missing/wrong issuer, wrong/duplicate token, supply drift, status drift, policy injection, unit-scale drift, and duplicate RecordId;
10. current-head full CI.

No FinanceAccount, balance, credit, exchange-rate, transaction, settlement, fiscal-policy, or monetary-policy authority may be synthesized as part of this proof.

## Acceptance accounting after successful production proof

Only after every proof gate succeeds:

```text
Society/Governance accepted = 1,521,200 / 2,000,000
Society/Governance remaining =   478,800
Society remaining            =   219,800
Governance remaining         =   259,000
```

Infrastructure accounting is unchanged. The Society/Governance parent blocker remains active. `referenceWorldMaterialized=false` and `authoritativeStepLoopAvailable=false` remain unchanged.

## Explicit non-decisions

This authority does not decide:

- general-world currency taxonomy;
- realistic monetary supply, issuance, seigniorage, inflation, exchange rates, convertibility, or acceptance;
- central-bank or government assumptions;
- FinanceAccount owner/institution mappings;
- FinanceAccount balance, credit-limit, status, or `ledger_head_digest` authority;
- payment/settlement semantics;
- fiscal policy or Governance TaxFiscal authority;
- any other remaining Society/Governance slice.
