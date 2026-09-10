using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04EnvironmentPartitionDecompositionV1(
    StableToken PartitionId,
    ulong D0StartOrdinal,
    ulong D0Count,
    ulong D1StartOrdinal,
    ulong D1Count)
{
    public ulong D0EndExclusive => checked(D0StartOrdinal + D0Count);
    public ulong D1EndExclusive => checked(D1StartOrdinal + D1Count);
}

public sealed record Qa04EnvironmentD0BindingV1(
    ulong GlobalOrdinal,
    StableToken PartitionId,
    ulong PartitionLocalOrdinal,
    Qa04ReferenceRecordV1 Descriptor);

public sealed record Qa04EnvironmentD1BindingV1(
    ulong GlobalOrdinal,
    StableToken PartitionId,
    ulong PartitionLocalOrdinal,
    Qa04ReferenceRecordV1 Descriptor,
    IReadOnlyList<ulong> SourceD0GlobalOrdinals);

/// <summary>
/// Exact Alpha 1.1 partition decomposition for the two Environment benchmark classes. This contract
/// owns ordinal-to-partition/source mapping and the normative same-partition D0 topology used by
/// groundwater, surface-water, and ocean genesis. Payload genesis values and cross-partition Ref
/// material remain the responsibility of the Environment materializer.
/// </summary>
public static class Qa04EnvironmentReferenceDecompositionV1
{
    public const ulong CanonicalD0Count = 1_000_000;
    public const ulong CanonicalD1Count = 250_000;
    public const int D0SourcesPerD1 = 4;

    private static readonly StableToken D0ReferenceClass = new("environment.d0-cell-cohort");
    private static readonly StableToken D1ReferenceClass = new("environment.d1-aggregate");

    private static readonly IReadOnlyList<Qa04EnvironmentPartitionDecompositionV1> PartitionsValue =
        Array.AsReadOnly(new[]
        {
            Slice("environment.geology",             0,      40_000,       0,      10_000),
            Slice("environment.soil",               40_000, 60_000,       10_000, 15_000),
            Slice("environment.resource_deposit",   100_000,20_000,       25_000, 5_000),
            Slice("environment.groundwater",        120_000,80_000,       30_000, 20_000),
            Slice("environment.atmosphere",         200_000,180_000,      50_000, 45_000),
            Slice("environment.climate",            380_000,30_000,       95_000, 7_500),
            Slice("environment.weather",            410_000,180_000,      102_500,45_000),
            Slice("environment.surface_water",      590_000,120_000,      147_500,30_000),
            Slice("environment.ocean",              710_000,10_000,       177_500,2_500),
            Slice("environment.ecosystem",          720_000,160_000,      180_000,40_000),
            Slice("environment.contaminant",        880_000,100_000,      220_000,25_000),
            Slice("environment.hazard",             980_000,10_000,       245_000,2_500),
            Slice("environment.environment_lineage",990_000,10_000,       247_500,2_500),
        });

    private static readonly IReadOnlySet<string> NextRecordTopologyPartitions = new HashSet<string>(StringComparer.Ordinal)
    {
        "environment.groundwater",
        "environment.surface_water",
        "environment.ocean",
    };

    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyDictionary<OpaqueId128, PartitionRecordRefV1>>>
        NextRecordTopology = new(BuildNextRecordTopology, LazyThreadSafetyMode.ExecutionAndPublication);

    public static IReadOnlyList<Qa04EnvironmentPartitionDecompositionV1> Partitions => PartitionsValue;

    public static void ValidateCanonicalContract()
    {
        Qa04ReferenceLoadV1.ValidateCanonicalContract();
        if (PartitionsValue.Count != 13)
            throw new InvalidDataException("qa04.environment.decomposition.partition-count");
        if (Qa04ReferenceLoadV1.RecordClasses.Single(x => x.ClassToken == D0ReferenceClass).Count != CanonicalD0Count ||
            Qa04ReferenceLoadV1.RecordClasses.Single(x => x.ClassToken == D1ReferenceClass).Count != CanonicalD1Count)
            throw new InvalidDataException("qa04.environment.decomposition.reference-count-drift");

        ulong nextD0 = 0;
        ulong nextD1 = 0;
        foreach (var slice in PartitionsValue)
        {
            var identity = StandardDomainPartitionRegistry.Get(slice.PartitionId.Value);
            if (identity.OwnerDomain.Value != "environment")
                throw new InvalidDataException($"qa04.environment.decomposition.foreign-owner:{slice.PartitionId.Value}");
            if (slice.D0StartOrdinal != nextD0 || slice.D1StartOrdinal != nextD1)
                throw new InvalidDataException($"qa04.environment.decomposition.range-gap:{slice.PartitionId.Value}");
            if (slice.D0Count == 0 || slice.D1Count == 0 ||
                slice.D0Count != checked(slice.D1Count * D0SourcesPerD1))
                throw new InvalidDataException($"qa04.environment.decomposition.four-to-one:{slice.PartitionId.Value}");
            nextD0 = slice.D0EndExclusive;
            nextD1 = slice.D1EndExclusive;
        }
        if (nextD0 != CanonicalD0Count || nextD1 != CanonicalD1Count)
            throw new InvalidDataException("qa04.environment.decomposition.total-count");
        if (PartitionsValue.Select(static x => x.PartitionId).Distinct().Count() != PartitionsValue.Count)
            throw new InvalidDataException("qa04.environment.decomposition.partition-duplicate");

        foreach (var partitionId in NextRecordTopologyPartitions)
        {
            var slice = Get(partitionId);
            if (slice.D0Count <= 1)
                throw new InvalidDataException($"qa04.environment.decomposition.topology-requires-multiple-records:{partitionId}");
        }
    }

    public static Qa04EnvironmentD0BindingV1 BindD0(ulong globalOrdinal)
    {
        ValidateOrdinal(globalOrdinal, CanonicalD0Count, nameof(globalOrdinal));
        var slice = FindD0(globalOrdinal);
        return new Qa04EnvironmentD0BindingV1(
            globalOrdinal,
            slice.PartitionId,
            checked(globalOrdinal - slice.D0StartOrdinal),
            Qa04ReferenceLoadV1.Record(D0ReferenceClass, globalOrdinal));
    }

    public static Qa04EnvironmentD1BindingV1 BindD1(ulong globalOrdinal)
    {
        ValidateOrdinal(globalOrdinal, CanonicalD1Count, nameof(globalOrdinal));
        var slice = FindD1(globalOrdinal);
        var local = checked(globalOrdinal - slice.D1StartOrdinal);
        var firstSource = checked(slice.D0StartOrdinal + local * D0SourcesPerD1);
        var sources = new[] { firstSource, firstSource + 1, firstSource + 2, firstSource + 3 };
        return new Qa04EnvironmentD1BindingV1(
            globalOrdinal,
            slice.PartitionId,
            local,
            Qa04ReferenceLoadV1.Record(D1ReferenceClass, globalOrdinal),
            Array.AsReadOnly(sources));
    }

    /// <summary>
    /// Returns the canonical same-partition successor for benchmark topology fields. Ordering is by
    /// RecordId ascending, not descriptor ordinal. The final record wraps to the first record.
    /// </summary>
    public static PartitionRecordRefV1 NextD0RecordRef(string partitionId, OpaqueId128 currentRecordId)
    {
        if (!NextRecordTopologyPartitions.Contains(partitionId))
            throw new ArgumentException($"Partition has no canonical next-record topology: {partitionId}", nameof(partitionId));
        if (currentRecordId.IsZero)
            throw new ArgumentException("currentRecordId ZERO is invalid.", nameof(currentRecordId));
        if (!NextRecordTopology.Value[partitionId].TryGetValue(currentRecordId, out var next))
            throw new KeyNotFoundException($"Record is not a canonical D0 member of {partitionId}: {currentRecordId}");
        return next;
    }

    public static Qa04EnvironmentPartitionDecompositionV1 Get(string partitionId)
        => PartitionsValue.SingleOrDefault(x => x.PartitionId.Value == partitionId)
            ?? throw new KeyNotFoundException($"Unknown QA-04 Environment partition: {partitionId}");

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<OpaqueId128, PartitionRecordRefV1>> BuildNextRecordTopology()
    {
        var result = new Dictionary<string, IReadOnlyDictionary<OpaqueId128, PartitionRecordRefV1>>(StringComparer.Ordinal);
        foreach (var partitionId in NextRecordTopologyPartitions.OrderBy(static value => value, StringComparer.Ordinal))
        {
            var slice = Get(partitionId);
            var ordered = Enumerable.Range(0, checked((int)slice.D0Count))
                .Select(offset => Qa04ReferenceLoadV1.Record(D0ReferenceClass, checked(slice.D0StartOrdinal + (ulong)offset)).RecordId)
                .OrderBy(static recordId => recordId)
                .ToArray();
            if (ordered.Length != checked((int)slice.D0Count) || ordered.Distinct().Count() != ordered.Length)
                throw new InvalidDataException($"qa04.environment.decomposition.topology-record-id-drift:{partitionId}");

            var nextByRecord = new Dictionary<OpaqueId128, PartitionRecordRefV1>(ordered.Length);
            var partitionToken = new StableToken(partitionId);
            for (var index = 0; index < ordered.Length; index++)
            {
                var current = ordered[index];
                var next = ordered[(index + 1) % ordered.Length];
                nextByRecord.Add(current, new PartitionRecordRefV1(partitionToken, next));
            }
            result.Add(partitionId, nextByRecord);
        }
        return result;
    }

    private static Qa04EnvironmentPartitionDecompositionV1 FindD0(ulong ordinal)
        => PartitionsValue.First(slice => ordinal >= slice.D0StartOrdinal && ordinal < slice.D0EndExclusive);

    private static Qa04EnvironmentPartitionDecompositionV1 FindD1(ulong ordinal)
        => PartitionsValue.First(slice => ordinal >= slice.D1StartOrdinal && ordinal < slice.D1EndExclusive);

    private static Qa04EnvironmentPartitionDecompositionV1 Slice(
        string partitionId,
        ulong d0Start,
        ulong d0Count,
        ulong d1Start,
        ulong d1Count)
        => new(new StableToken(partitionId), d0Start, d0Count, d1Start, d1Count);

    private static void ValidateOrdinal(ulong ordinal, ulong count, string parameterName)
    {
        if (ordinal >= count) throw new ArgumentOutOfRangeException(parameterName);
    }
}
