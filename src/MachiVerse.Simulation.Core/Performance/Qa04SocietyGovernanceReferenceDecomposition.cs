using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04SocietyGovernancePartitionDecompositionV1(
    StableToken PartitionId,
    ulong StartOrdinal,
    ulong Count,
    bool UsesSpecializedIdentity)
{
    public ulong EndExclusive => checked(StartOrdinal + Count);
}

public sealed record Qa04SocietyGovernanceBindingV1(
    ulong GlobalOrdinal,
    StableToken PartitionId,
    ulong PartitionLocalOrdinal,
    Qa04ReferenceRecordV1 Descriptor,
    bool UsesSpecializedIdentity);

public readonly record struct Qa04MarketSliceBindingV1(
    ulong GlobalDescriptorOrdinal,
    ulong MarketLocalOrdinal,
    bool IsMarketState,
    uint ScopeOrdinal,
    uint? OrderOrdinalWithinScope);

/// <summary>
/// Exact perf.reference.v1 Society/Governance 2,000,000-record partition decomposition. This
/// contract fixes descriptor ordinal ownership only. Required relation Ref targets and specialized
/// market record identities are applied by their owning materializers.
/// </summary>
public static class Qa04SocietyGovernanceReferenceDecompositionV1
{
    public const ulong CanonicalCount = 2_000_000;
    public const ulong SocietyCount = 1_600_000;
    public const ulong GovernanceCount = 400_000;
    public const ulong MarketSliceStartOrdinal = 400_100;
    public const ulong MarketSliceCount = 1_000_100;
    public const uint MarketScopeCount = 100;
    public const uint MarketOrdersPerScope = 10_000;
    public const ulong MarketStateCount = 100;
    public const ulong MarketOrderCount = 1_000_000;

    private static readonly StableToken ReferenceClass = new("society-governance.active-record");

    private static readonly IReadOnlyList<Qa04SocietyGovernancePartitionDecompositionV1> PartitionsValue =
        Array.AsReadOnly(new[]
        {
            Slice("society.organization", 0, 10_000),
            Slice("society.membership_role", 10_000, 80_000),
            Slice("society.employment", 90_000, 80_000),
            Slice("society.household", 170_000, 40_000),
            Slice("society.contract_claim", 210_000, 60_000),
            Slice("society.property_right", 270_000, 50_000),
            Slice("society.currency_money", 320_000, 100),
            Slice("society.finance_account", 320_100, 80_000),
            Slice("society.market_transaction", MarketSliceStartOrdinal, MarketSliceCount, usesSpecializedIdentity: true),
            Slice("society.business_production", 1_400_200, 30_000),
            Slice("society.logistics_obligation", 1_430_200, 40_000),
            Slice("society.education", 1_470_200, 25_000),
            Slice("society.culture", 1_495_200, 30_000),
            Slice("society.reputation", 1_525_200, 30_000),
            Slice("society.information_claim", 1_555_200, 25_000),
            Slice("society.history_lineage", 1_580_200, 19_800),

            Slice("governance.polity", 1_600_000, 1_000),
            Slice("governance.institution", 1_601_000, 5_000),
            Slice("governance.law_rule", 1_606_000, 30_000),
            Slice("governance.jurisdiction", 1_636_000, 10_000),
            Slice("governance.territorial_claim", 1_646_000, 10_000),
            Slice("governance.effective_control", 1_656_000, 20_000),
            Slice("governance.public_authority", 1_676_000, 25_000),
            Slice("governance.tax_fiscal", 1_701_000, 50_000),
            Slice("governance.permission_license", 1_751_000, 70_000),
            Slice("governance.diplomacy", 1_821_000, 10_000),
            Slice("governance.security_incident", 1_831_000, 45_000),
            Slice("governance.investigation", 1_876_000, 30_000),
            Slice("governance.judicial_case", 1_906_000, 25_000),
            Slice("governance.enforcement", 1_931_000, 30_000),
            Slice("governance.military_authority", 1_961_000, 10_000),
            Slice("governance.border_control", 1_971_000, 10_000),
            Slice("governance.lineage", 1_981_000, 19_000),
        });

    public static IReadOnlyList<Qa04SocietyGovernancePartitionDecompositionV1> Partitions => PartitionsValue;

    public static void ValidateCanonicalContract()
    {
        Qa04ReferenceLoadV1.ValidateCanonicalContract();
        var descriptorClass = Qa04ReferenceLoadV1.RecordClasses.Single(recordClass => recordClass.ClassToken == ReferenceClass);
        if (descriptorClass.Count != CanonicalCount || descriptorClass.DetailLevel != DetailLevelV1.D2RegionalAggregate)
            throw new InvalidDataException("qa04.society-governance.reference-class-drift");
        if (PartitionsValue.Count != 33)
            throw new InvalidDataException("qa04.society-governance.partition-count-drift");

        ulong next = 0;
        ulong society = 0;
        ulong governance = 0;
        foreach (var slice in PartitionsValue)
        {
            if (slice.StartOrdinal != next || slice.Count == 0)
                throw new InvalidDataException($"qa04.society-governance.range-gap:{slice.PartitionId.Value}");
            var identity = StandardDomainPartitionRegistry.Get(slice.PartitionId.Value);
            var owner = identity.OwnerDomain.Value;
            if (owner is not ("society_economy" or "governance_security"))
                throw new InvalidDataException($"qa04.society-governance.foreign-owner:{slice.PartitionId.Value}");
            if (owner == "society_economy") society = checked(society + slice.Count);
            else governance = checked(governance + slice.Count);
            next = slice.EndExclusive;
        }
        if (next != CanonicalCount || society != SocietyCount || governance != GovernanceCount)
            throw new InvalidDataException("qa04.society-governance.total-count-drift");
        if (PartitionsValue.Select(static slice => slice.PartitionId).Distinct().Count() != PartitionsValue.Count)
            throw new InvalidDataException("qa04.society-governance.partition-duplicate");

        var specialized = PartitionsValue.Where(static slice => slice.UsesSpecializedIdentity).ToArray();
        if (specialized.Length != 1 || specialized[0].PartitionId.Value != "society.market_transaction" ||
            specialized[0].StartOrdinal != MarketSliceStartOrdinal || specialized[0].Count != MarketSliceCount)
            throw new InvalidDataException("qa04.society-governance.specialized-identity-drift");
        if (MarketStateCount + MarketOrderCount != MarketSliceCount ||
            (ulong)MarketScopeCount * MarketOrdersPerScope != MarketOrderCount)
            throw new InvalidDataException("qa04.society-governance.market-count-drift");
    }

    public static Qa04SocietyGovernanceBindingV1 Bind(ulong globalOrdinal)
    {
        if (globalOrdinal >= CanonicalCount) throw new ArgumentOutOfRangeException(nameof(globalOrdinal));
        var slice = PartitionsValue.First(value => globalOrdinal >= value.StartOrdinal && globalOrdinal < value.EndExclusive);
        var descriptor = Qa04ReferenceLoadV1.Record(ReferenceClass, globalOrdinal);
        if (descriptor.DetailLevel != DetailLevelV1.D2RegionalAggregate)
            throw new InvalidDataException("qa04.society-governance.descriptor-detail-level-drift");
        return new Qa04SocietyGovernanceBindingV1(
            globalOrdinal,
            slice.PartitionId,
            checked(globalOrdinal - slice.StartOrdinal),
            descriptor,
            slice.UsesSpecializedIdentity);
    }

    public static Qa04MarketSliceBindingV1 BindMarket(ulong globalOrdinal)
    {
        if (globalOrdinal < MarketSliceStartOrdinal || globalOrdinal >= checked(MarketSliceStartOrdinal + MarketSliceCount))
            throw new ArgumentOutOfRangeException(nameof(globalOrdinal));
        var local = checked(globalOrdinal - MarketSliceStartOrdinal);
        if (local < MarketStateCount)
        {
            return new Qa04MarketSliceBindingV1(
                globalOrdinal,
                local,
                IsMarketState: true,
                ScopeOrdinal: checked((uint)local),
                OrderOrdinalWithinScope: null);
        }

        var globalOrder = checked(local - MarketStateCount);
        return new Qa04MarketSliceBindingV1(
            globalOrdinal,
            local,
            IsMarketState: false,
            ScopeOrdinal: checked((uint)(globalOrder / MarketOrdersPerScope)),
            OrderOrdinalWithinScope: checked((uint)(globalOrder % MarketOrdersPerScope)));
    }

    public static Qa04SocietyGovernancePartitionDecompositionV1 Get(string partitionId)
        => PartitionsValue.SingleOrDefault(value => value.PartitionId.Value == partitionId)
            ?? throw new KeyNotFoundException($"Unknown QA-04 Society/Governance partition: {partitionId}");

    private static Qa04SocietyGovernancePartitionDecompositionV1 Slice(
        string partitionId,
        ulong startOrdinal,
        ulong count,
        bool usesSpecializedIdentity = false)
        => new(new StableToken(partitionId), startOrdinal, count, usesSpecializedIdentity);
}
