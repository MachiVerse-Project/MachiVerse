# Alpha 1.1 Market order application authority

Status: **Complete / normative benchmark authority**

Tracking: #240, #265

## 1. Purpose

This document fixes the benchmark-only runtime application semantics for the canonical `perf.reference.v1` `society-market-payment-contract` Operation family after the existing `society.market.order-place` binding has succeeded.

It closes only the focused QA-04 boundary between the already-bound Operation and authoritative `society.market_transaction` v2 state. It does not define a general exchange API, order amendment/cancellation lifecycle, matching engine, clearing cycle, settlement, payment transfer, contract execution, or price-discovery policy.

## 2. Existing authority reused

Implementation MUST reuse, without redefining:

- `Qa04CanonicalOperationBindingV1` for canonical Operation payload, market target, owner, scheduling identity, and owner-domain binding;
- `Qa04ReferenceLoadV1` and `Qa04ReferenceScenariosV1` for canonical workload/world identities;
- `Qa04MarketMaterializerV1` for canonical market-state material and benchmark market vocabulary;
- `SocietyMarketTransactionRecordSchemaV2`, `SocietyMarketTransactionPartitionIdentityV2`, and `SocietyMarketTransactionPartitionStateV2` for the heterogeneous v2 partition;
- `DerivedIdentity.DeriveEntityId` for deterministic creation identity;
- the ordinary reference/schema validation authorities used by the v2 market partition.

No new PartitionId, record schema, general market side/status vocabulary, matching algorithm, or settlement rule is introduced.

The only new identity-purpose token fixed by this document is the benchmark-scoped creation kind `perf.market-order-operation`. It exists solely to derive a collision-resistant deterministic RecordId for the order created by this focused QA-04 Operation application. It is not a new public OperationKind or general domain contract.

## 3. Canonical bound input

For a source descriptor in family `society-market-payment-contract`, existing binding authority defines:

```text
operation_kind = society.market.order-place
owner_domain   = society_economy
scope_ordinal  = family_ordinal % 100
market_ref     = canonical MarketScopeId(scope_ordinal)
owner_ref      = resident.identity_lifecycle[family_ordinal]
side           = family_ordinal even ? buy : sell
limit_price    = side == buy
                 ? 100000 + (family_ordinal % 1000)
                 :  99500 + (family_ordinal % 1000)
quantity       = 1 + (family_ordinal % 20)
effective_step = injection_step + 1
semantic_priority = 0
```

The immutable Operation payload encodes exactly `market_ref`, `owner_ref`, `side`, `limit_price`, and `quantity`.

Runtime application MUST fail closed if the supplied binding differs from recomputing `Qa04CanonicalOperationBindingV1.Bind(source_descriptor, scheduling_policy_generation)`.

## 4. Canonical target market authority

`market_ref` is not an order RecordId. It identifies the exact canonical v2 `market_state` record for `scope_ordinal`.

Before creating an order, application MUST verify that this target is the canonical QA-04 market state and remains active. Its authoritative material is the result equivalent to `Qa04MarketMaterializerV1.CreateMarketState(scope_ordinal, Qa04SpatialTileScopeAuthorityV1.ScopeRef, ...)` and therefore has:

```text
record_id      = market_ref.record_id
record_schema  = society.market_transaction v2
revision       = 1
created_step   = 0
retired_step   = NONE
detail_level   = D2
lineage_ref    = NONE
record_kind    = market_state
instrument     = perf.instrument
currency       = perf.currency
status         = active
clearing cadence = 30
last clearing step  = NONE
last clearing price = NONE
```

The target market-state record is read-only at this focused boundary. Placing the order MUST NOT revise the market-state record.

## 5. Benchmark-only created-order identity

The Operation creates exactly one v2 `order_or_offer` record.

Its RecordId is derived deterministically as:

```text
DerivedIdentity.DeriveEntityId(
    world_id      = Qa04ReferenceLoadV1.WorldId,
    creation_step = effective_step,
    domain         = society_economy,
    creator_id     = operation_id,
    creation_kind  = perf.market-order-operation,
    local_ordinal  = 0)
```

This rule is benchmark-only. It is deliberately based on the immutable Operation identity and effective Step so replay from the same pre-state derives the same RecordId and a repeated application to the already-mutated state collides and rejects rather than creating a second order.

The implementation MUST reject if the derived RecordId is ZERO or is already present in the supplied v2 partition state.

## 6. Created order payload

The created order payload is exactly:

```text
market_ref            = bound market_ref
owner_ref             = bound owner_ref
instrument_token      = canonical target market_state.instrument_token
side                  = bound side
limit_price_microunit = bound limit_price
quantity              = bound quantity
remaining_quantity    = bound quantity
eligible_step         = effective_step
status                = open
```

`instrument_token` is copied from the validated canonical target market state. The application MUST NOT infer a different instrument from the owner, side, price, or arbitrary configuration.

The created envelope is:

```text
RecordSchema = SocietyMarketTransactionRecordSchemaV2.RecordSchema
Revision     = 1
CreatedStep  = effective_step
RetiredStep  = NONE
DetailLevel  = D2RegionalAggregate
LineageRef   = NONE
```

The v2 payload constructor/validator and ordinary reference/schema authority MUST accept the result before the next partition state is constructed.

## 7. Partition transition

Application adds exactly one order to the supplied authoritative `society.market_transaction` v2 state.

The transition MUST satisfy:

```text
next_item_count = current_item_count + 1
```

Every pre-existing record, including the target market-state record and all canonical genesis/open-order records, MUST remain byte/semantic equivalent to its pre-state value.

The application does not match, fill, cancel, expire, clear, settle, or otherwise transform any order at this boundary. `remaining_quantity` begins equal to `quantity` and `status` begins as `open`.

## 8. Validation and fail-closed rules

Before constructing the next partition state, implementation MUST verify:

- family is exactly `society-market-payment-contract`;
- OperationKind is exactly `society.market.order-place`;
- owner domain is exactly `society_economy`;
- primary target is exactly the canonical market-state ref for `family_ordinal % 100`;
- effective Step is exactly `injection_step + 1`;
- supplied Operation, admission, scheduling identity, order keys, target, and bound descriptor are byte-for-byte equivalent to canonical rebinding;
- current partition identity is exactly the v2 `society.market_transaction` partition identity;
- the canonical target market-state record exists exactly once and matches the canonical revision-1 Step-0 D2 material described above;
- target market status is `active`;
- bound owner ref resolves to the registered resident identity/lifecycle schema;
- bound market ref resolves to the v2 market-state target just validated;
- bound side, price, and quantity equal the values fixed by canonical rebinding;
- the derived created-order RecordId is non-ZERO and absent from the current state;
- the created order uses target-market instrument, `remaining_quantity == quantity`, `eligible_step == effective_step`, and `status == open`;
- created envelope metadata is exactly revision 1 / CreatedStep effective Step / non-retired / D2 / no lineage;
- next item count increases by exactly one;
- every pre-existing record remains unchanged.

Any mismatch MUST reject rather than infer, normalize, repair, match the order, or silently skip input.

## 9. Required proof

The implementation proof MUST cover at least:

### Positive proof

- bind an actual canonical `society-market-payment-contract` descriptor;
- load its actual canonical market-state target plus representative non-target v2 material;
- apply `society.market.order-place`;
- observe exactly one new deterministic RecordId at revision 1;
- verify the created payload contains the exact bound market/owner/side/price/quantity;
- verify instrument is copied from the canonical target market state;
- verify `remaining_quantity == quantity`, `eligible_step == effective_step`, and `status == open`;
- verify target market state and every pre-existing record are unchanged;
- verify item count increments by exactly one;
- replay from the same pre-state and verify the same created identity/payload are derived.

### Negative proof

At minimum reject:

- wrong family;
- wrong OperationKind;
- wrong owner domain;
- wrong primary target;
- wrong effective Step;
- tampered Operation payload or payload digest;
- wrong scheduling-policy generation/order identity;
- wrong partition identity/schema version;
- missing target market-state record;
- target market-state identity, metadata, payload, or status drift;
- missing or wrong-schema owner reference;
- derived RecordId collision, including retry after successful application;
- post-mutation item-count drift or pre-existing-record mutation.

## 10. Release boundary

Completion of this focused handler changes only the Operation-family actual-mutation count:

```text
actual Operation mutation application: 3 / 6 -> 4 / 6
```

It MUST NOT by itself set:

```text
authoritativeStepLoopAvailable = true
releaseEvidenceCapable = true
```

It does not prove matching/clearing/settlement, the full authoritative Step, exact-103 recovery/replay after full mutation, determinism matrix, benchmark release evidence, or 24h soak.

## 11. Non-generalization statement

This authority is intentionally restricted to the Alpha 1.1 QA-04 `perf.reference.v1` workload.

In particular, it does not define:

- a universal `society.market.order-place` creation-identity contract;
- arbitrary instruments, currencies, market scopes, or owner types;
- order amendment, cancellation, expiration, or replacement;
- price/time priority or any matching-engine behavior;
- partial/full fills, trades, clearing prices, settlement, payments, or contracts;
- repeated creation after a successful application of the same Operation;
- acceptance rules for non-canonical market-state records.

Any such generalization requires separate normative authority.
