using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.WorldState;

internal static class SocietyMarketTransactionV2Initializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        SocietyMarketTransactionRecordSchemaV2.ValidateCanonicalContract();
        VerifyPositiveArms();
        VerifyLegacyMigration();
        VerifyNegativeContracts();
    }

    private static void VerifyPositiveArms()
    {
        var scope = Ref("spatial.scope_registry", "00000000000000000000000000a10001");
        var market = Ref("society.market_transaction", "00000000000000000000000000a10002");
        var owner = Ref("resident.identity_lifecycle", "00000000000000000000000000a10003");
        var buyer = Ref("resident.identity_lifecycle", "00000000000000000000000000a10004");
        var seller = Ref("resident.identity_lifecycle", "00000000000000000000000000a10005");

        var state = new SocietyMarketStatePayloadV2(
            scope,
            new StableToken("perf.instrument"),
            new StableToken("perf.currency"),
            new StableToken("active"),
            clearingCadenceSteps: 30,
            lastClearingStep: null,
            lastClearingPriceMicrounit: null);
        Require(state.RecordKind == SocietyMarketTransactionRecordSchemaV2.MarketStateKind,
            "Market v2 state record kind drifted.");

        var order = new SocietyMarketOrderPayloadV2(
            market,
            owner,
            new StableToken("perf.instrument"),
            new StableToken("buy"),
            limitPriceMicrounit: 100_123,
            quantity: 7,
            remainingQuantity: 7,
            eligibleStep: 0,
            new StableToken("open"));
        Require(order.RecordKind == SocietyMarketTransactionRecordSchemaV2.OrderOrOfferKind,
            "Market v2 order record kind drifted.");

        var trade = new SocietyMarketFactPayloadV2(
            new StableToken(SocietyMarketTransactionRecordSchemaV2.TradeFactKind),
            market,
            new StableToken("perf.instrument"),
            new StableToken("buy"),
            limitPriceMicrounit: 100_123,
            quantity: 3,
            clearingPriceMicrounit: 100_000,
            buyer,
            seller,
            eligibleStep: 4,
            new StableToken("committed"));
        Require(trade.RecordKind == SocietyMarketTransactionRecordSchemaV2.TransactionOrPriceFactKind,
            "Market v2 trade record kind drifted.");

        var price = new SocietyMarketFactPayloadV2(
            new StableToken(SocietyMarketTransactionRecordSchemaV2.ClearingPriceFactKind),
            market,
            new StableToken("perf.instrument"),
            orderSide: null,
            limitPriceMicrounit: null,
            quantity: 0,
            clearingPriceMicrounit: 99_999,
            buyerRef: null,
            sellerRef: null,
            eligibleStep: 4,
            new StableToken("published"));
        Require(price.Quantity == 0 && price.ClearingPriceMicrounit == 99_999,
            "Market v2 clearing-price fact drifted.");
    }

    private static void VerifyLegacyMigration()
    {
        var market = Ref("society.market_transaction", "00000000000000000000000000a20001");
        var buyer = Ref("resident.identity_lifecycle", "00000000000000000000000000a20002");
        var seller = Ref("resident.identity_lifecycle", "00000000000000000000000000a20003");
        var source = new DomainRecordEnvelopeV1<SocietyMarketTransactionPayloadV1>(
            OpaqueId128.Parse("00000000000000000000000000a20004"),
            StandardDomainPartitionRegistry.Get(SocietyMarketTransactionPayloadV1.PartitionId).RecordSchema,
            revision: 9,
            createdStep: 2,
            retiredStep: 11,
            detailLevel: DetailLevelV1.D2RegionalAggregate,
            lineageRef: OpaqueId128.Parse("00000000000000000000000000a20005"),
            new SocietyMarketTransactionPayloadV1(
                market,
                new StableToken("perf.instrument"),
                new StableToken("sell"),
                LimitPrice: 123_456,
                Quantity: 5,
                ClearingPrice: 123_400,
                BuyerRef: buyer,
                SellerRef: seller,
                EligibleStep: 7,
                Status: new StableToken("settled")));

        var migrated = SocietyMarketTransactionRecordMaterialV2.MigrateLegacyFact(source);
        var payload = migrated.Payload as SocietyMarketFactPayloadV2
            ?? throw new InvalidOperationException("Market v1 migration must emit a fact payload.");
        Require(migrated.RecordId == source.RecordId &&
                migrated.Revision == source.Revision &&
                migrated.CreatedStep == source.CreatedStep &&
                migrated.RetiredStep == source.RetiredStep &&
                migrated.DetailLevel == source.DetailLevel &&
                migrated.LineageRef == source.LineageRef,
            "Market v1 migration must preserve the record envelope.");
        Require(payload.FactKind.Value == SocietyMarketTransactionRecordSchemaV2.LegacyV1FactKind &&
                payload.MarketRef == source.Payload.MarketRef &&
                payload.InstrumentToken == source.Payload.InstrumentToken &&
                payload.OrderSide == source.Payload.OrderSide &&
                payload.LimitPriceMicrounit == source.Payload.LimitPrice &&
                payload.Quantity == source.Payload.Quantity &&
                payload.ClearingPriceMicrounit == source.Payload.ClearingPrice &&
                payload.BuyerRef == source.Payload.BuyerRef &&
                payload.SellerRef == source.Payload.SellerRef &&
                payload.EligibleStep == source.Payload.EligibleStep &&
                payload.Status == source.Payload.Status,
            "Market v1 migration must copy every legacy semantic field exactly.");
    }

    private static void VerifyNegativeContracts()
    {
        var scope = Ref("spatial.scope_registry", "00000000000000000000000000a30001");
        var market = Ref("society.market_transaction", "00000000000000000000000000a30002");
        var owner = Ref("resident.identity_lifecycle", "00000000000000000000000000a30003");

        ExpectInvalid(() => _ = new SocietyMarketStatePayloadV2(
            scope, new StableToken("perf.instrument"), new StableToken("perf.currency"), new StableToken("active"),
            0, null, null));
        ExpectInvalid(() => _ = new SocietyMarketStatePayloadV2(
            scope, new StableToken("perf.instrument"), new StableToken("perf.currency"), new StableToken("active"),
            30, null, 1));
        ExpectInvalid(() => _ = new SocietyMarketOrderPayloadV2(
            market, owner, new StableToken("perf.instrument"), new StableToken("buy"),
            1, 1, 0, 0, new StableToken("open")));
        ExpectInvalid(() => _ = new SocietyMarketOrderPayloadV2(
            market, owner, new StableToken("perf.instrument"), new StableToken("sell"),
            1, 1, 1, 0, new StableToken("filled")));
        ExpectInvalid(() => _ = new SocietyMarketFactPayloadV2(
            new StableToken(SocietyMarketTransactionRecordSchemaV2.TradeFactKind),
            market, new StableToken("perf.instrument"), null, null, 0, 1, null, null, 0, new StableToken("committed")));
        ExpectInvalid(() => _ = new SocietyMarketFactPayloadV2(
            new StableToken(SocietyMarketTransactionRecordSchemaV2.ClearingPriceFactKind),
            market, new StableToken("perf.instrument"), null, null, 1, 1, null, null, 0, new StableToken("published")));
    }

    private static PartitionRecordRefV1 Ref(string partitionId, string id)
        => new(partitionId, OpaqueId128.Parse(id));

    private static void ExpectInvalid(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidDataException)
        {
            return;
        }
        throw new InvalidOperationException("Expected Market v2 contract rejection.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
