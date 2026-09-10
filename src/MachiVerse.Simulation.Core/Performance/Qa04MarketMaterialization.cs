using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04MarketDescriptorIdentityBindingV1(
    OpaqueId128 DescriptorId,
    OpaqueId128 AuthoritativeRecordId,
    uint ScopeOrdinal,
    uint? OrderOrdinalWithinScope);

/// <summary>
/// Canonical perf.reference.v1 market_state + open-order materialization. TileScope RecordId remains
/// an explicit Spatial authority input; every other benchmark identity/value is fixed by the market
/// v2 design and existing QA-04 scenario identities.
/// </summary>
public static class Qa04MarketMaterializerV1
{
    public const ulong CanonicalMarketStateCount = 100;
    public const ulong CanonicalOrderCount = 1_000_000;
    public const ulong CanonicalRecordCount = CanonicalMarketStateCount + CanonicalOrderCount;

    private static readonly StableToken ResidentClass = new("resident.persistent-identity");
    private static readonly StableToken Instrument = new("perf.instrument");
    private static readonly StableToken Currency = new("perf.currency");
    private static readonly StableToken Active = new("active");
    private static readonly StableToken Open = new("open");
    private static readonly StableToken Buy = new("buy");
    private static readonly StableToken Sell = new("sell");

    public static void ValidateCanonicalContract()
    {
        Qa04ReferenceLoadV1.ValidateCanonicalContract();
        Qa04ReferenceScenariosV1.ValidateCanonicalContract();
        Qa04SocietyGovernanceReferenceDecompositionV1.ValidateCanonicalContract();
        SocietyMarketTransactionRecordSchemaV2.ValidateCanonicalContract();
        if (CanonicalMarketStateCount != Qa04SocietyGovernanceReferenceDecompositionV1.MarketStateCount ||
            CanonicalOrderCount != Qa04SocietyGovernanceReferenceDecompositionV1.MarketOrderCount ||
            CanonicalRecordCount != Qa04SocietyGovernanceReferenceDecompositionV1.MarketSliceCount)
            throw new InvalidDataException("qa04.market.canonical-count-drift");
        if (Qa04ReferenceScenariosV1.MarketScopeCount != checked((int)CanonicalMarketStateCount) ||
            Qa04ReferenceScenariosV1.ActiveOrdersPerMarketScope != 10_000)
            throw new InvalidDataException("qa04.market.scenario-count-drift");
    }

    public static SocietyMarketTransactionRecordMaterialV2 CreateMarketState(
        uint scopeOrdinal,
        Func<ushort, PartitionRecordRefV1> tileScopeForTile,
        out Qa04MarketDescriptorIdentityBindingV1 identityBinding)
    {
        ArgumentNullException.ThrowIfNull(tileScopeForTile);
        ValidateCanonicalContract();
        if (scopeOrdinal >= CanonicalMarketStateCount)
            throw new ArgumentOutOfRangeException(nameof(scopeOrdinal));

        var globalDescriptorOrdinal = checked(
            Qa04SocietyGovernanceReferenceDecompositionV1.MarketSliceStartOrdinal + scopeOrdinal);
        var descriptorBinding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(globalDescriptorOrdinal);
        var marketBinding = Qa04SocietyGovernanceReferenceDecompositionV1.BindMarket(globalDescriptorOrdinal);
        if (!marketBinding.IsMarketState || marketBinding.ScopeOrdinal != scopeOrdinal)
            throw new InvalidDataException("qa04.market.state-descriptor-binding-drift");

        var recordId = Qa04ReferenceScenariosV1.MarketScopeId(checked((int)scopeOrdinal));
        var tile = checked((ushort)(((ulong)scopeOrdinal * Qa04ReferenceLoadV1.RegionalTileCount) / CanonicalMarketStateCount));
        var scopeRef = tileScopeForTile(tile);
        if (scopeRef.PartitionId.Value != "spatial.scope_registry" || scopeRef.RecordId.IsZero)
            throw new InvalidDataException("qa04.market.state-tile-scope-ref-invalid");

        identityBinding = new Qa04MarketDescriptorIdentityBindingV1(
            descriptorBinding.Descriptor.RecordId,
            recordId,
            scopeOrdinal,
            null);
        return new SocietyMarketTransactionRecordMaterialV2(
            recordId,
            revision: 1,
            createdStep: 0,
            retiredStep: null,
            detailLevel: DetailLevelV1.D2RegionalAggregate,
            lineageRef: null,
            new SocietyMarketStatePayloadV2(
                scopeRef,
                Instrument,
                Currency,
                Active,
                clearingCadenceSteps: 30,
                lastClearingStep: null,
                lastClearingPriceMicrounit: null));
    }

    public static SocietyMarketTransactionRecordMaterialV2 CreateOrder(
        ulong globalOrderOrdinal,
        out Qa04MarketDescriptorIdentityBindingV1 identityBinding)
    {
        ValidateCanonicalContract();
        if (globalOrderOrdinal >= CanonicalOrderCount)
            throw new ArgumentOutOfRangeException(nameof(globalOrderOrdinal));

        var scope = checked((uint)(globalOrderOrdinal / 10_000UL));
        var order = checked((uint)(globalOrderOrdinal % 10_000UL));
        var globalDescriptorOrdinal = checked(
            Qa04SocietyGovernanceReferenceDecompositionV1.MarketSliceStartOrdinal +
            CanonicalMarketStateCount + globalOrderOrdinal);
        var descriptorBinding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(globalDescriptorOrdinal);
        var marketBinding = Qa04SocietyGovernanceReferenceDecompositionV1.BindMarket(globalDescriptorOrdinal);
        if (marketBinding.IsMarketState || marketBinding.ScopeOrdinal != scope || marketBinding.OrderOrdinalWithinScope != order)
            throw new InvalidDataException("qa04.market.order-descriptor-binding-drift");

        var marketId = Qa04ReferenceScenariosV1.MarketScopeId(checked((int)scope));
        var orderId = Qa04ReferenceScenariosV1.MarketOrderId(checked((int)scope), checked((int)order));
        var resident = Qa04ReferenceLoadV1.Record(ResidentClass, globalOrderOrdinal);
        var marketRef = new PartitionRecordRefV1(SocietyMarketTransactionRecordSchemaV2.PartitionId, marketId);
        var ownerRef = new PartitionRecordRefV1("resident.identity_lifecycle", resident.RecordId);
        var side = (order & 1u) == 0u ? Buy : Sell;
        var price = side == Buy
            ? checked(100_000L + order % 1_000u)
            : checked(99_500L + order % 1_000u);
        var quantity = checked(1L + order % 20u);

        identityBinding = new Qa04MarketDescriptorIdentityBindingV1(
            descriptorBinding.Descriptor.RecordId,
            orderId,
            scope,
            order);
        return new SocietyMarketTransactionRecordMaterialV2(
            orderId,
            revision: 1,
            createdStep: 0,
            retiredStep: null,
            detailLevel: DetailLevelV1.D2RegionalAggregate,
            lineageRef: null,
            new SocietyMarketOrderPayloadV2(
                marketRef,
                ownerRef,
                Instrument,
                side,
                price,
                quantity,
                quantity,
                eligibleStep: 0,
                Open));
    }

    public static IEnumerable<SocietyMarketTransactionRecordMaterialV2> MaterializeCanonical(
        Func<ushort, PartitionRecordRefV1> tileScopeForTile)
    {
        ArgumentNullException.ThrowIfNull(tileScopeForTile);
        ValidateCanonicalContract();
        for (uint scope = 0; scope < CanonicalMarketStateCount; scope++)
            yield return CreateMarketState(scope, tileScopeForTile, out _);
        for (ulong order = 0; order < CanonicalOrderCount; order++)
            yield return CreateOrder(order, out _);
    }
}
