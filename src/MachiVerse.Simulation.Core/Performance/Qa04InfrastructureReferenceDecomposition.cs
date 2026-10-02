using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04InfrastructureMaterialSliceV1(
    StableToken MaterialClass,
    StableToken PartitionId,
    ulong StartOrdinal,
    ulong Count,
    bool UsesSpecializedIdentity)
{
    public ulong EndExclusive => checked(StartOrdinal + Count);
}

public sealed record Qa04InfrastructureBindingV1(
    ulong GlobalOrdinal,
    StableToken MaterialClass,
    StableToken PartitionId,
    ulong LocalOrdinal,
    Qa04ReferenceRecordV1 Descriptor,
    bool UsesSpecializedIdentity);

/// <summary>Exact perf.reference.v1 500,000-record Infrastructure/Information decomposition.</summary>
public static class Qa04InfrastructureReferenceDecompositionV1
{
    public const ulong CanonicalCount = 500_000;
    public const ulong NetworkCount = 100;
    public const ulong NodeCount = 20_000;
    public const ulong EdgeCount = 100_000;
    public const ulong ServiceQueueCount = 250_000;

    public const ulong NetworkStartOrdinal = 0;
    public const ulong NodeStartOrdinal = 100;
    public const ulong EdgeStartOrdinal = 20_100;
    public const ulong ServiceQueueStartOrdinal = 195_100;

    private static readonly StableToken ReferenceClass = new("infrastructure.active-record");

    private static readonly IReadOnlyList<Qa04InfrastructureMaterialSliceV1> SlicesValue = Array.AsReadOnly(new[]
    {
        Slice("network", "infrastructure.network_topology", 0, 100, true),
        Slice("node", "infrastructure.network_topology", 100, 20_000, true),
        Slice("edge", "infrastructure.network_topology", 20_100, 100_000, true),
        Slice("transport_service", "infrastructure.transport_service", 120_100, 10_000),
        Slice("water_service", "infrastructure.water_service", 130_100, 10_000),
        Slice("power_service", "infrastructure.power_service", 140_100, 10_000),
        Slice("communication_service", "infrastructure.communication_service", 150_100, 10_000),
        Slice("dependency", "infrastructure.dependency", 160_100, 20_000),
        Slice("facility_service", "infrastructure.facility_service", 180_100, 15_000),
        Slice("service_queue", "infrastructure.service_queue", 195_100, 250_000, true),
        Slice("information_delivery", "information.delivery", 445_100, 20_000),
        Slice("media_distribution", "information.media_distribution", 465_100, 5_000),
        Slice("record_store", "information.record_store", 470_100, 10_000),
        Slice("address_place_index", "information.address_place_index", 480_100, 5_000),
        Slice("failure_recovery", "infrastructure.failure_recovery", 485_100, 10_000),
        Slice("lineage", "infrastructure.lineage", 495_100, 4_900),
    });

    public static IReadOnlyList<Qa04InfrastructureMaterialSliceV1> Slices => SlicesValue;

    public static void ValidateCanonicalContract()
    {
        Qa04ReferenceLoadV1.ValidateCanonicalContract();
        Qa04ReferenceScenariosV1.ValidateCanonicalContract();
        var descriptorClass = Qa04ReferenceLoadV1.RecordClasses.Single(entry => entry.ClassToken == ReferenceClass);
        if (descriptorClass.Count != CanonicalCount)
            throw new InvalidDataException("qa04.infrastructure.reference-class-count-drift");
        if (SlicesValue.Count != 16)
            throw new InvalidDataException("qa04.infrastructure.slice-count-drift");

        ulong next = 0;
        foreach (var slice in SlicesValue)
        {
            if (slice.StartOrdinal != next || slice.Count == 0)
                throw new InvalidDataException($"qa04.infrastructure.slice-gap:{slice.MaterialClass.Value}");
            var identity = StandardDomainPartitionRegistry.Get(slice.PartitionId.Value);
            if (identity.OwnerDomain.Value != "infrastructure_information")
                throw new InvalidDataException($"qa04.infrastructure.foreign-owner:{slice.PartitionId.Value}");
            next = slice.EndExclusive;
        }
        if (next != CanonicalCount)
            throw new InvalidDataException("qa04.infrastructure.total-count-drift");
        if (SlicesValue.Select(static value => value.MaterialClass).Distinct().Count() != SlicesValue.Count)
            throw new InvalidDataException("qa04.infrastructure.material-class-duplicate");

        if (SlicesValue.Single(static value => value.MaterialClass.Value == "network").Count != NetworkCount ||
            SlicesValue.Single(static value => value.MaterialClass.Value == "node").Count != NodeCount ||
            SlicesValue.Single(static value => value.MaterialClass.Value == "edge").Count != EdgeCount ||
            SlicesValue.Single(static value => value.MaterialClass.Value == "service_queue").Count != ServiceQueueCount)
            throw new InvalidDataException("qa04.infrastructure.specialized-count-drift");
        if (NodeCount != checked((ulong)Qa04ReferenceScenariosV1.InfrastructureNetworkNodeCount) ||
            EdgeCount != checked((ulong)Qa04ReferenceScenariosV1.InfrastructureStableEdgeCount) ||
            ServiceQueueCount != checked((ulong)Qa04ReferenceScenariosV1.InfrastructureQueuedServiceRequestCount))
            throw new InvalidDataException("qa04.infrastructure.scenario-count-drift");

        foreach (var probe in SlicesValue.SelectMany(static slice => new[] { slice.StartOrdinal, slice.EndExclusive - 1 }))
        {
            if (Qa04ReferenceLoadV1.Record(ReferenceClass, probe).DetailLevel != DetailLevelV1.D2RegionalAggregate)
                throw new InvalidDataException("qa04.infrastructure.descriptor-detail-drift");
        }
    }

    public static Qa04InfrastructureBindingV1 Bind(ulong globalOrdinal)
    {
        if (globalOrdinal >= CanonicalCount)
            throw new ArgumentOutOfRangeException(nameof(globalOrdinal));
        var slice = SlicesValue.First(value => globalOrdinal >= value.StartOrdinal && globalOrdinal < value.EndExclusive);
        var descriptor = Qa04ReferenceLoadV1.Record(ReferenceClass, globalOrdinal);
        if (descriptor.DetailLevel != DetailLevelV1.D2RegionalAggregate)
            throw new InvalidDataException("qa04.infrastructure.descriptor-detail-drift");
        return new Qa04InfrastructureBindingV1(
            globalOrdinal,
            slice.MaterialClass,
            slice.PartitionId,
            checked(globalOrdinal - slice.StartOrdinal),
            descriptor,
            slice.UsesSpecializedIdentity);
    }

    public static Qa04InfrastructureMaterialSliceV1 Get(string materialClass)
        => SlicesValue.SingleOrDefault(value => value.MaterialClass.Value == materialClass)
            ?? throw new KeyNotFoundException($"Unknown QA-04 Infrastructure material class: {materialClass}");

    private static Qa04InfrastructureMaterialSliceV1 Slice(
        string materialClass,
        string partitionId,
        ulong startOrdinal,
        ulong count,
        bool specialized = false)
        => new(new StableToken(materialClass), new StableToken(partitionId), startOrdinal, count, specialized);
}
