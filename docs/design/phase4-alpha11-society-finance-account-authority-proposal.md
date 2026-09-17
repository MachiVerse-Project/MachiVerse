# Alpha 1.1 Society FinanceAccount authority

Status: **Approved normative authority**

Tracking: #240
Implementation: #265
Approval recorded: 2026-09-13

## Purpose

This document fixes the exact `perf.reference.v1` authority for the remaining `society.finance_account` 80,000-record slice.

The benchmark choices below are normative for Alpha 1.1. They are deliberately narrow and must not be generalized into broader banking, monetary, or ledger-history semantics.

## Existing authority used

This package closes its dependencies using already-proven production authority:

- canonical Resident identity, including ordinals `0..79,999`;
- approved CurrencyMoney authority: 100 records with exact Tokens `perf.currency-000..perf.currency-099`;
- existing QA-04 `society.finance_account` descriptor slice at global Society/Governance ordinals `320,100..400,099`;
- standard FinanceAccount payload/schema and Snapshot codecs;
- runtime `FinanceAccountBalanceV1` balance/credit-limit validation and balanced-ledger mechanics;
- derived indexes `society.account-by-owner` and `society.account-by-currency` from the standard index registry.

No pre-existing persistent `ledger_head_digest` generation rule exists. The explicit genesis sentinel below is therefore part of this approved authority.

## Canonical benchmark mapping

For local ordinal `a = 0..79,999`:

```text
owner_ref              = Resident[a]
institution_ref        = NONE
currency_token         = perf.currency-{(a mod 100):000}
balance_microunit      = 0
credit_limit_microunit = 0
status                  = active
ledger_head_digest      = SHA-256(empty byte sequence)
```

The exact approved empty-ledger digest is:

```text
e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855
```

## Normative meaning and boundaries

- one benchmark account is assigned to each canonical Resident ordinal `0..79,999`;
- all 80,000 owner Refs must resolve to actual canonical Resident production records;
- `institution_ref = NONE` avoids inventing a bank, government, central bank, employer, or other custody institution;
- currencies are assigned round-robin over the approved 100 CurrencyMoney Tokens, yielding exactly 800 accounts per currency;
- `balance_microunit = 0` establishes no initial holdings;
- `credit_limit_microunit = 0` establishes no synthetic credit authority;
- `status = active` means the benchmark account definition is available, not that it contains funds or has transacted;
- `ledger_head_digest = SHA-256(empty byte sequence)` is the Alpha 1.1 benchmark genesis sentinel for an account with **zero postings**;
- the shared empty digest represents the same empty posting sequence, not account identity;
- this digest rule must not be reused for a non-empty ledger and does not define a ledger-chain format, transaction hash recipe, Merkle scheme, or universal account-history representation;
- because genesis has zero postings, the double-entry invariant is vacuously satisfied at genesis; later postings remain subject to the existing balanced-ledger runtime mechanics.

This authority is a benchmark fixture only and does not constrain general MachiVerse account ownership or banking models.

## Canonical envelope

For every account use only the existing QA-04 descriptor identity:

```text
record_id    = existing descriptor RecordId for FinanceAccount local ordinal a
revision     = 1
created_step = 0
retired_step = NONE
detail_level = D2
lineage_ref  = NONE
```

No account RecordId derivation or specialized identity is introduced.

## Required derived indexes

Rebuild from production records:

```text
society.account-by-owner
society.account-by-currency
```

Required cardinality:

- account-by-owner: 80,000 keys, exactly one account per owner;
- account-by-currency: 100 keys, exactly 800 accounts per approved CurrencyMoney Token.

## Required production proof

Before accepted accounting moves, #265 must prove on its current head:

1. exactly 80,000 FinanceAccount records materialized through the production payload validator;
2. exact existing descriptor/envelope binding for every record;
3. actual canonical Resident `a` closure for all 80,000 owner Refs;
4. exactly 80,000 distinct owners and one account per owner;
5. exact round-robin closure to the approved 100 CurrencyMoney Tokens, exactly 800 accounts per token;
6. `institution_ref = NONE` on all records;
7. exact zero balance, zero credit limit, `active`, and approved empty-ledger digest on all records;
8. runtime `FinanceAccountBalanceV1` validation for all zero/zero account states;
9. both required secondary indexes with exact cardinalities above;
10. full 80,000-record Snapshot encode / recovery / semantic rehash;
11. fail-closed negative proof for missing/wrong owner, duplicate owner, wrong/non-approved currency Token, institution injection, balance drift, credit drift, status drift, digest drift, duplicate RecordId, and population/index cardinality drift;
12. full current-head CI.

The proof must not fabricate ledger entries merely to satisfy the digest.

## Acceptance accounting after production proof

Only after the production proof and full current-head CI succeed:

```text
Society/Governance accepted = 1,601,200 / 2,000,000
Society/Governance remaining =   398,800
Society remaining            =   139,800
Governance remaining         =   259,000
```

Other parent blockers and release flags remain unchanged.

## Explicit non-decisions

This authority does not decide:

- bank or financial-institution identity;
- Organization-owned accounts;
- nonzero initial balances or wealth distribution;
- credit issuance;
- interest, fees, overdraft policy, deposits, lending, reserves, or capital requirements;
- exchange rates or currency convertibility;
- ledger entry identity or a persistent ledger-chain encoding;
- digest rules for any non-empty ledger;
- payment/settlement authority;
- Governance TaxFiscal or monetary policy;
- PropertyRight, BusinessProduction, LogisticsObligation, or HistoryLineage authority.
