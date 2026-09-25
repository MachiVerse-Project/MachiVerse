# Alpha 1.1 — `society.market_transaction` record schema v2

Status: Decided normative design / implementation pending  
Tracking: #240  
Parent: `phase4-alpha11-normative-closure.md`

## 1. Schema identity

```text
schema_id = domain.society.market_transaction.record
version   = 2.0
partition = society.market_transaction
```

required `record_kind`:

```text
market_state
order_or_offer
transaction_or_price_fact
```

## 2. `market_state`

Exact fields/order:

```text
1 scope_ref: Ref
2 instrument_token: Token
3 currency_token: Token
4 status: Token
5 clearing_cadence_steps: uint64
6 last_clearing_step: Step?
7 last_clearing_price_microunit: int64?
```

Rules:

- scope target = `spatial.scope_registry`
- cadence >0
- clearing price >=0 when present
- `last_clearing_price` present requires `last_clearing_step` present
- status v2 initial registry: `active`, `halted`, `retired`

## 3. `order_or_offer`

```text
1 market_ref: Ref
2 owner_ref: Ref
3 instrument_token: Token
4 side: Token
5 limit_price_microunit: int64
6 quantity: int64
7 remaining_quantity: int64
8 eligible_step: Step
9 status: Token
```

Rules:

```text
market_ref target = same partition /2.0 / market_state
side = buy | sell
limit_price >= 0
quantity > 0
0 <= remaining_quantity <= quantity
status = open | filled | cancelled | expired
filled -> remaining_quantity=0
open -> remaining_quantity>0
```

owner_refはactual authoritative actor/organization record。benchmarkではResident identityを使用する。

Canonical call-auction input order is existing `MarketOrderV1` order:

```text
buy:  price DESC, eligible_step ASC, record_id ASC
sell: price ASC,  eligible_step ASC, record_id ASC
```

## 4. `transaction_or_price_fact`

```text
1 fact_kind: Token
2 market_ref: Ref
3 instrument_token: Token
4 order_side: Token?
5 limit_price_microunit: int64?
6 quantity: int64
7 clearing_price_microunit: int64?
8 buyer_ref: Ref?
9 seller_ref: Ref?
10 eligible_step: Step
11 status: Token
```

This arm is the v2 successor of P4-05 v1 `society.market_transaction` semantics and can represent durable trade/price facts without mutating old v1 meaning.

Rules:

- market_ref -> same partition /2.0 / market_state
- optional monetary values >=0
- quantity may be zero only for price-only facts; trade facts require >0
- buyer/seller presence follows `fact_kind` contract

v2 initial fact kinds:

```text
market.trade
market.clearing-price
market.legacy-v1
```

## 5. v1 -> v2 migration

Every v1 record migrates to exactly one `transaction_or_price_fact`:

```text
fact_kind = market.legacy-v1
```

All v1 fields are copied without semantic reinterpretation. Migration must not synthesize a `market_state` or an `order_or_offer`; missing target market_state makes Ref closure fail until an explicit owner materialization/migration plan supplies it.

For a world that contains legacy v1 market refs with no materialized v2 market state, production migration remains fail-closed rather than guessing market identity.

## 6. `perf.reference.v1` market material

Exactly:

```text
market_state = 100
open orders  = 1,000,000
trade/price facts at genesis = 0
```

IDs:

```text
market_state.record_id = Qa04ReferenceScenariosV1.MarketScopeId(scope)
order.record_id = Qa04ReferenceScenariosV1.MarketOrderId(scope, order)
```

Market genesis:

```text
scope_ref = TileScope(floor(scope*4096/100))
instrument_token = perf.instrument
currency_token = perf.currency
status = active
clearing_cadence_steps = 30
last_clearing_step = NONE
last_clearing_price = NONE
```

Order global ordinal `g=scope*10000+order`:

```text
market_ref = MarketScopeId(scope)
owner_ref = Resident(g)
instrument_token = perf.instrument
side = order%2==0 ? buy : sell
buy price  = 100000 + order%1000
sell price =  99500 + order%1000
quantity = 1 + order%20
remaining_quantity = quantity
eligible_step = 0
status = open
```

Existing `MarketOrderChanges(scope,order)` identifies exactly 5% mutable orders. On a change cadence:

- RecordId is unchanged.
- revision increments exactly one.
- new price delta is `((revision + orderOrdinal) % 5) - 2` microunit, checked and clamped only at zero lower bound.
- quantity/owner/market/side remain unchanged.

A clearing result that executes quantity produces `transaction_or_price_fact` records and updates `remaining_quantity` in the same authoritative Step candidate/commit.

## 7. Reference closure

Required closure:

```text
order.market_ref -> market_state
fact.market_ref -> market_state
market_state.scope_ref -> spatial.scope_registry
order.owner_ref -> resident.identity_lifecycle (benchmark)
```

No `market_ref` may point to a transaction/order record.

## 8. Blocker removal gate

`qa04.material.market-ref-authority-undefined` remains until v2 record/state/wire, migration, provider selection, target-kind Ref resolution, recovery and the 100+1,000,000 canonical materialization all pass production-path validation.
