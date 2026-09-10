using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class SocietyMarketTransactionV2ProductionSnapshotSmoke
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var stateId = Id("00000000000000000000000000041001");
        var orderId = Id("00000000000000000000000000041002");
        var scopeRef = new PartitionRecordRefV1("spatial.scope_registry", Id("00000000000000000000000000042001"));
        var ownerRef = new PartitionRecordRefV1("resident.identity", Id("00000000000000000000000000043001"));
        var marketRef = new PartitionRecordRefV1(SocietyMarketTransactionRecordSchemaV2.PartitionId, stateId);

        var state = new SocietyMarketTransactionRecordMaterialV2(
            stateId, 1, 0, null, DetailLevelV1.D2LocalDetailed, null,
            new SocietyMarketStatePayloadV2(
                scopeRef,
                new StableToken("reference-instrument"),
                new StableToken("reference-currency"),
                new StableToken("active"),
                1,
                null,
                null));
        var order = new SocietyMarketTransactionRecordMaterialV2(
            orderId, 1, 0, null, DetailLevelV1.D2LocalDetailed, null,
            new SocietyMarketOrderPayloadV2(
                marketRef,
                ownerRef,
                new StableToken("reference-instrument"),
                new StableToken("buy"),
                100_000,
                10,
                10,
                0,
                new StableToken("open")));

        var partition = new SocietyMarketTransactionPartitionStateV2(new[] { state, order });
        SocietyMarketTransactionReferenceClosureV2.Validate(partition.RecordSet.RecordsCanonical);
        var authority = SocietyMarketTransactionSnapshotAuthorityV2.CreateCanonical(
            partition, 1, 0, DetailLevelV1.D2LocalDetailed);
        var provider = new SocietyMarketTransactionSnapshotSectionProviderV2();
        var section = provider.Create(authority);
        Require(section.LogicalItemCount == 2, "Market v2 production section must retain both records.");
        var recovered = new SocietyMarketTransactionRecoveredReferenceSourceV2(section.Fragments);
        Require(recovered.RecordSchema == SocietyMarketTransactionRecordSchemaV2.RecordSchema,
            "Recovered Market source must preserve record schema v2.");
        Require(recovered.IsMarketState(stateId), "Recovered Market source must retain market_state target kind.");
        Require(!recovered.IsMarketState(orderId), "Recovered order must not be classified as market_state.");

        var decodedState = SocietyMarketTransactionRecordWireCodecV2.Decode(
            SocietyMarketTransactionRecordWireCodecV2.Encode(state));
        var decodedOrder = SocietyMarketTransactionRecordWireCodecV2.Decode(
            SocietyMarketTransactionRecordWireCodecV2.Encode(order));
        Require(decodedState.Payload.RecordKind == SocietyMarketTransactionRecordSchemaV2.MarketStateKind,
            "Market state wire round-trip must preserve record kind.");
        Require(decodedOrder.Payload.RecordKind == SocietyMarketTransactionRecordSchemaV2.OrderOrOfferKind,
            "Market order wire round-trip must preserve record kind.");

        var invalidOrder = new SocietyMarketTransactionRecordMaterialV2(
            Id("00000000000000000000000000041003"), 1, 0, null, DetailLevelV1.D2LocalDetailed, null,
            new SocietyMarketOrderPayloadV2(
                new PartitionRecordRefV1(SocietyMarketTransactionRecordSchemaV2.PartitionId, orderId),
                ownerRef,
                new StableToken("reference-instrument"),
                new StableToken("sell"),
                100_000,
                1,
                1,
                0,
                new StableToken("open")));
        ExpectReject(
            () => SocietyMarketTransactionReferenceClosureV2.Validate(new[] { state, order, invalidOrder }),
            "society.market-transaction-v2.reference-closure-market-target-kind");
    }

    private static OpaqueId128 Id(string value) => OpaqueId128.Parse(value);

    private static void ExpectReject(Action action, string code)
    {
        try
        {
            action();
            throw new InvalidOperationException($"Expected rejection containing '{code}'.");
        }
        catch (InvalidDataException ex) when (ex.Message.Contains(code, StringComparison.Ordinal))
        {
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
