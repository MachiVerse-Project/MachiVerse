using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04SocietyGovernanceReferenceDecompositionSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04SocietyGovernanceReferenceDecompositionV1.ValidateCanonicalContract();
        VerifyCountsAndBoundaries();
        VerifyMarketSlice();
        VerifyOutOfRange();
    }

    private static void VerifyCountsAndBoundaries()
    {
        Require(Qa04SocietyGovernanceReferenceDecompositionV1.Partitions.Count == 33,
            "QA-04 Society/Governance partition count drifted.");
        Require(Qa04SocietyGovernanceReferenceDecompositionV1.Partitions.Take(16).Sum(static slice => checked((long)slice.Count)) == 1_600_000,
            "QA-04 Society total must remain exactly 1,600,000.");
        Require(Qa04SocietyGovernanceReferenceDecompositionV1.Partitions.Skip(16).Sum(static slice => checked((long)slice.Count)) == 400_000,
            "QA-04 Governance total must remain exactly 400,000.");

        foreach (var slice in Qa04SocietyGovernanceReferenceDecompositionV1.Partitions)
        {
            var first = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(slice.StartOrdinal);
            var last = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(slice.EndExclusive - 1);
            Require(first.PartitionId == slice.PartitionId && first.PartitionLocalOrdinal == 0,
                $"Society/Governance first-boundary mapping drifted: {slice.PartitionId.Value}");
            Require(last.PartitionId == slice.PartitionId && last.PartitionLocalOrdinal == slice.Count - 1,
                $"Society/Governance last-boundary mapping drifted: {slice.PartitionId.Value}");
            Require(first.Descriptor.DetailLevel == DetailLevelV1.D2RegionalAggregate &&
                    last.Descriptor.DetailLevel == DetailLevelV1.D2RegionalAggregate,
                $"Society/Governance canonical descriptor detail drifted: {slice.PartitionId.Value}");
            Require(first.UsesSpecializedIdentity == slice.UsesSpecializedIdentity &&
                    last.UsesSpecializedIdentity == slice.UsesSpecializedIdentity,
                $"Society/Governance specialized identity flag drifted: {slice.PartitionId.Value}");
        }
    }

    private static void VerifyMarketSlice()
    {
        var start = Qa04SocietyGovernanceReferenceDecompositionV1.MarketSliceStartOrdinal;
        var state0 = Qa04SocietyGovernanceReferenceDecompositionV1.BindMarket(start);
        var state99 = Qa04SocietyGovernanceReferenceDecompositionV1.BindMarket(start + 99);
        var order0 = Qa04SocietyGovernanceReferenceDecompositionV1.BindMarket(start + 100);
        var order9999 = Qa04SocietyGovernanceReferenceDecompositionV1.BindMarket(start + 10_099);
        var orderNextScope = Qa04SocietyGovernanceReferenceDecompositionV1.BindMarket(start + 10_100);
        var finalOrder = Qa04SocietyGovernanceReferenceDecompositionV1.BindMarket(
            start + Qa04SocietyGovernanceReferenceDecompositionV1.MarketSliceCount - 1);

        Require(state0.IsMarketState && state0.ScopeOrdinal == 0 && state0.OrderOrdinalWithinScope is null,
            "Market state scope 0 mapping drifted.");
        Require(state99.IsMarketState && state99.ScopeOrdinal == 99 && state99.OrderOrdinalWithinScope is null,
            "Market state scope 99 mapping drifted.");
        Require(!order0.IsMarketState && order0.ScopeOrdinal == 0 && order0.OrderOrdinalWithinScope == 0,
            "Market first order mapping drifted.");
        Require(!order9999.IsMarketState && order9999.ScopeOrdinal == 0 && order9999.OrderOrdinalWithinScope == 9_999,
            "Market scope-0 final order mapping drifted.");
        Require(!orderNextScope.IsMarketState && orderNextScope.ScopeOrdinal == 1 && orderNextScope.OrderOrdinalWithinScope == 0,
            "Market scope transition mapping drifted.");
        Require(!finalOrder.IsMarketState && finalOrder.ScopeOrdinal == 99 && finalOrder.OrderOrdinalWithinScope == 9_999,
            "Market final order mapping drifted.");

        var marketSlice = Qa04SocietyGovernanceReferenceDecompositionV1.Get("society.market_transaction");
        Require(marketSlice.UsesSpecializedIdentity && marketSlice.Count == 1_000_100,
            "Market slice must retain specialized identity mapping requirement.");
    }

    private static void VerifyOutOfRange()
    {
        ExpectRange(() => Qa04SocietyGovernanceReferenceDecompositionV1.Bind(
            Qa04SocietyGovernanceReferenceDecompositionV1.CanonicalCount));
        ExpectRange(() => Qa04SocietyGovernanceReferenceDecompositionV1.BindMarket(
            Qa04SocietyGovernanceReferenceDecompositionV1.MarketSliceStartOrdinal - 1));
        ExpectRange(() => Qa04SocietyGovernanceReferenceDecompositionV1.BindMarket(
            Qa04SocietyGovernanceReferenceDecompositionV1.MarketSliceStartOrdinal +
            Qa04SocietyGovernanceReferenceDecompositionV1.MarketSliceCount));
    }

    private static void ExpectRange(Action action)
    {
        try
        {
            action();
        }
        catch (ArgumentOutOfRangeException)
        {
            return;
        }
        throw new InvalidOperationException("Expected QA-04 Society/Governance ordinal rejection.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
