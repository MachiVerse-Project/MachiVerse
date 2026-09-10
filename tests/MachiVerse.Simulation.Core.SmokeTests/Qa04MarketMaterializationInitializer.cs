using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04MarketMaterializationInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        Qa04MarketMaterializerV1.ValidateCanonicalContract();
        VerifyStateBoundaries();
        VerifyOrderBoundaries();
        VerifyDeterminism();
        VerifyCompleteCanonicalStream();
    }

    private static void VerifyStateBoundaries()
    {
        ushort? requestedFirstTile = null;
        var first = Qa04MarketMaterializerV1.CreateMarketState(0, tile =>
        {
            requestedFirstTile = tile;
            return Scope(tile);
        }, out var firstBinding);
        Require(requestedFirstTile == 0 && firstBinding.ScopeOrdinal == 0 && firstBinding.OrderOrdinalWithinScope is null,
            "QA-04 first market-state tile/identity binding drifted.");
        Require(first.RecordId == Qa04ReferenceScenariosV1.MarketScopeId(0) &&
                first.Payload is SocietyMarketStatePayloadV2 state0 &&
                state0.ScopeRef == Scope(0) && state0.ClearingCadenceSteps == 30,
            "QA-04 first market-state material drifted.");

        ushort? requestedLastTile = null;
        var last = Qa04MarketMaterializerV1.CreateMarketState(99, tile =>
        {
            requestedLastTile = tile;
            return Scope(tile);
        }, out var lastBinding);
        var expectedLastTile = checked((ushort)((99UL * Qa04ReferenceLoadV1.RegionalTileCount) / 100UL));
        Require(requestedLastTile == expectedLastTile && lastBinding.ScopeOrdinal == 99,
            "QA-04 final market-state tile mapping drifted.");
        Require(last.RecordId == Qa04ReferenceScenariosV1.MarketScopeId(99),
            "QA-04 final market-state identity drifted.");
    }

    private static void VerifyOrderBoundaries()
    {
        VerifyOrder(0, 0, 0, "buy", 100_000, 1);
        VerifyOrder(1, 0, 1, "sell", 99_501, 2);
        VerifyOrder(9_999, 0, 9_999, "sell", 100_499, 20);
        VerifyOrder(10_000, 1, 0, "buy", 100_000, 1);
        VerifyOrder(999_999, 99, 9_999, "sell", 100_499, 20);
    }

    private static void VerifyOrder(
        ulong global,
        uint expectedScope,
        uint expectedOrder,
        string expectedSide,
        long expectedPrice,
        long expectedQuantity)
    {
        var record = Qa04MarketMaterializerV1.CreateOrder(global, out var binding);
        var order = record.Payload as SocietyMarketOrderPayloadV2
            ?? throw new InvalidOperationException("QA-04 market order materializer emitted non-order payload.");
        Require(binding.ScopeOrdinal == expectedScope && binding.OrderOrdinalWithinScope == expectedOrder,
            $"QA-04 market order descriptor binding drifted: {global}");
        Require(record.RecordId == Qa04ReferenceScenariosV1.MarketOrderId((int)expectedScope, (int)expectedOrder),
            $"QA-04 market order identity drifted: {global}");
        Require(order.MarketRef.RecordId == Qa04ReferenceScenariosV1.MarketScopeId((int)expectedScope) &&
                order.OwnerRef.PartitionId.Value == "resident.identity_lifecycle" &&
                order.Side.Value == expectedSide &&
                order.LimitPriceMicrounit == expectedPrice &&
                order.Quantity == expectedQuantity && order.RemainingQuantity == expectedQuantity &&
                order.EligibleStep == 0 && order.Status.Value == "open",
            $"QA-04 market order payload drifted: {global}");
        var resident = Qa04ReferenceLoadV1.Record(new StableToken("resident.persistent-identity"), global);
        Require(order.OwnerRef.RecordId == resident.RecordId,
            $"QA-04 market order owner must bind Resident(globalOrderOrdinal): {global}");
        Require(record.Revision == 1 && record.CreatedStep == 0 && record.DetailLevel == DetailLevelV1.D2RegionalAggregate,
            $"QA-04 market order genesis envelope drifted: {global}");
    }

    private static void VerifyDeterminism()
    {
        var first = Qa04MarketMaterializerV1.CreateOrder(123_456, out var firstBinding);
        var second = Qa04MarketMaterializerV1.CreateOrder(123_456, out var secondBinding);
        Require(first.RecordId == second.RecordId && firstBinding == secondBinding,
            "QA-04 market materialization must be deterministic.");
        Require(firstBinding.DescriptorId != firstBinding.AuthoritativeRecordId,
            "QA-04 specialized market identity evidence must retain descriptor and authoritative ids distinctly.");
    }

    private static void VerifyCompleteCanonicalStream()
    {
        var marketIds = new OpaqueId128[checked((int)Qa04MarketMaterializerV1.CanonicalMarketStateCount)];
        var seenIds = new HashSet<OpaqueId128>();
        ulong stateCount = 0;
        ulong orderCount = 0;
        ulong totalCount = 0;

        foreach (var record in Qa04MarketMaterializerV1.MaterializeCanonical(Scope))
        {
            Require(record.RecordSchema == SocietyMarketTransactionRecordSchemaV2.RecordSchema &&
                    record.Revision == 1 && record.CreatedStep == 0 && record.RetiredStep is null &&
                    record.DetailLevel == DetailLevelV1.D2RegionalAggregate && record.LineageRef is null,
                $"QA-04 Market canonical stream envelope drifted at ordinal {totalCount}.");
            Require(seenIds.Add(record.RecordId),
                $"QA-04 Market canonical stream emitted duplicate RecordId at ordinal {totalCount}.");

            if (record.Payload is SocietyMarketStatePayloadV2 state)
            {
                Require(totalCount < Qa04MarketMaterializerV1.CanonicalMarketStateCount,
                    "QA-04 Market canonical stream emitted market_state after the state prefix.");
                var scope = checked((int)stateCount);
                marketIds[scope] = record.RecordId;
                Require(state.ScopeRef.PartitionId.Value == "spatial.scope_registry" &&
                        !state.ScopeRef.RecordId.IsZero && state.Status.Value == "active" &&
                        state.ClearingCadenceSteps == 30,
                    $"QA-04 Market canonical market_state payload drifted at scope {scope}.");
                stateCount++;
            }
            else if (record.Payload is SocietyMarketOrderPayloadV2 order)
            {
                Require(totalCount >= Qa04MarketMaterializerV1.CanonicalMarketStateCount,
                    "QA-04 Market canonical stream emitted order before all market_state records.");
                var scope = checked((int)(orderCount / 10_000UL));
                Require(scope < marketIds.Length && marketIds[scope] != default,
                    $"QA-04 Market order targets an unmaterialized market_state at order {orderCount}.");
                Require(order.MarketRef.PartitionId.Value == SocietyMarketTransactionRecordSchemaV2.PartitionId &&
                        order.MarketRef.RecordId == marketIds[scope],
                    $"QA-04 Market order market_ref target-kind closure drifted at order {orderCount}.");
                Require(order.OwnerRef.PartitionId.Value == "resident.identity_lifecycle" &&
                        !order.OwnerRef.RecordId.IsZero && order.Status.Value == "open" &&
                        order.Quantity > 0 && order.RemainingQuantity == order.Quantity,
                    $"QA-04 Market canonical order payload drifted at order {orderCount}.");
                orderCount++;
            }
            else
            {
                throw new InvalidOperationException(
                    $"QA-04 Market canonical stream emitted unsupported payload at ordinal {totalCount}.");
            }

            totalCount++;
        }

        Require(stateCount == Qa04MarketMaterializerV1.CanonicalMarketStateCount,
            $"QA-04 Market canonical stream market_state count drifted: {stateCount}.");
        Require(orderCount == Qa04MarketMaterializerV1.CanonicalOrderCount,
            $"QA-04 Market canonical stream order count drifted: {orderCount}.");
        Require(totalCount == Qa04MarketMaterializerV1.CanonicalRecordCount &&
                (ulong)seenIds.Count == Qa04MarketMaterializerV1.CanonicalRecordCount,
            $"QA-04 Market canonical stream must contain exactly {Qa04MarketMaterializerV1.CanonicalRecordCount} unique records; actual={totalCount}, unique={seenIds.Count}.");
    }

    private static PartitionRecordRefV1 Scope(ushort tile)
    {
        var id = HashSuite.Trunc128(HashSuite.DomainHash("mv.qa04-market-smoke-tile-scope.v1", writer =>
        {
            writer.WriteMapStart(1);
            writer.WriteUnsigned(0); writer.WriteUnsigned(tile);
        }));
        if (id.IsZero) throw new InvalidOperationException("Market smoke scope id unexpectedly became ZERO.");
        return new PartitionRecordRefV1("spatial.scope_registry", id);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
