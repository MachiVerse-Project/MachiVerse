# Alpha 1.1 Society CurrencyMoney authority proposal

Status: **Review only / approval pending**

Tracking: #240
Implementation after approval: #265

## Purpose

This document isolates the remaining `society.currency_money` 100-record slice as a low-dependency `perf.reference.v1` decision package.

It is **not normative authority yet**. No implementation or accepted-accounting change may consume this proposal before explicit #240 project-owner approval and the normal documentation -> develop integration flow.

`finance_account` is intentionally excluded. Its `ledger_head_digest` requires a separate explicit authority decision and must not be inferred merely because CurrencyMoney becomes available.

## Existing authority available

The package can close all required references using authority already accepted in production:

- Organization: 10,000 accepted records;
- existing QA-04 `society.currency_money` descriptor slice: 100 records at Society/Governance local ordinals `320,000..320,099`;
- standard CurrencyMoney payload/schema and Snapshot codecs;
- required derived secondary index `society.currency-by-token`.

The generic payload validator requires a valid existing issuer Ref, a StableToken currency token, Money scalar, status Token, canonical policy Ref list, and UInt32 unit scale. It does not itself choose benchmark monetary semantics.

## Recommended benchmark authority — `society.currency_money` 100

For local ordinal `c = 0..99`:

```text
currency_token   = perf.currency-{c:000}
issuer_ref       = Organization[c]
supply_microunit = 0
status           = active
policy_refs      = []
unit_scale       = 6
```

Thus the exact approved-token candidate vocabulary would be:

```text
perf.currency-000
...
perf.currency-099
```

### Rationale and boundary

- `issuer_ref = Organization[c]` uses 100 distinct already-accepted Society actors and does not fabricate central banks, states, or financial institutions.
- `supply_microunit = 0` means the benchmark currency identity exists at genesis but no units have yet been issued. It deliberately avoids inventing initial holdings or monetary distribution.
- `status = active` means the currency definition is available for benchmark references; it does not imply circulating supply.
- `policy_refs = []` deliberately avoids synthesizing fiscal, legal, monetary-policy, or governance records.
- `unit_scale = 6` is proposed because monetary scalar fields in the standard payload surface are expressed in microunits. This is a benchmark representation choice and requires explicit approval; it is not inferred as a universal currency rule.
- the 100 currency Tokens are opaque benchmark identities and do not define real-world currency names, exchange rates, or a universal monetary taxonomy.

Each CurrencyMoney record has a distinct `currency_token` and distinct `issuer_ref`. No realistic relationship between currency count and Organization count is asserted.

## Common envelope proposal

Use the existing QA-04 descriptor identity only:

```text
record_id    = existing descriptor RecordId for CurrencyMoney local ordinal c
revision     = 1
created_step = 0
retired_step = NONE
detail_level = D2
lineage_ref  = NONE
```

No new RecordId recipe is proposed.

## Required production proof after approval

If approved, #265 must prove all of the following before accepted accounting moves:

1. exactly 100 CurrencyMoney records materialized through the production payload validator;
2. exact existing descriptor/envelope binding for all 100 records;
3. `issuer_ref` resolves to actual accepted Organization ordinal `c` for every record;
4. all 100 issuer Refs are distinct;
5. exact token vocabulary `perf.currency-000..099`, with all 100 tokens distinct;
6. exact `supply_microunit=0`, `status=active`, `policy_refs=[]`, `unit_scale=6` semantics;
7. rebuild `society.currency-by-token` from production records and prove exactly 100 keys / 100 records / one record per token;
8. full 100-record Snapshot encode / recovery / semantic rehash;
9. fail-closed negative proof for missing/wrong issuer, wrong/duplicate token, supply drift, status drift, policy injection, unit-scale drift, duplicate RecordId;
10. current-head full CI.

No finance account, balance, credit, exchange-rate, transaction, or monetary-policy record may be synthesized as part of this proof.

## Acceptance accounting if production proof succeeds

Only after all proof gates pass:

```text
Society/Governance accepted = 1,521,200 / 2,000,000
Society/Governance remaining =   478,800
Society remaining            =   219,800
Governance remaining         =   259,000
```

Infrastructure accounting is unchanged. The Society/Governance parent blocker remains active. `referenceWorldMaterialized=false` and `authoritativeStepLoopAvailable=false` remain unchanged.

## Explicit non-decisions

This proposal does not decide:

- general-world currency taxonomy;
- realistic monetary supply, issuance, seigniorage, inflation, exchange rates, or acceptance;
- central-bank or government assumptions;
- FinanceAccount owner/institution mappings;
- FinanceAccount balance, credit-limit, status, or `ledger_head_digest` authority;
- payment/settlement semantics;
- fiscal policy or Governance TaxFiscal authority;
- any other remaining Society/Governance slice.
