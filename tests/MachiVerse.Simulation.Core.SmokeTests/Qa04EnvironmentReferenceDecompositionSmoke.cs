using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Performance;

internal static class Qa04EnvironmentReferenceDecompositionSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04EnvironmentReferenceDecompositionV1.ValidateCanonicalContract();
        VerifyExactPartitionCounts();
        VerifyBoundaryMappings();
        VerifyEveryD0SourceConsumedExactlyOnce();
        VerifyCanonicalNextRecordTopology();
        VerifyOutOfRangeFailsClosed();
    }

    private static void VerifyExactPartitionCounts()
    {
        var expected = new (string PartitionId, ulong D0, ulong D1)[]
        {
            ("environment.geology", 40_000, 10_000),
            ("environment.soil", 60_000, 15_000),
            ("environment.resource_deposit", 20_000, 5_000),
            ("environment.groundwater", 80_000, 20_000),
            ("environment.atmosphere", 180_000, 45_000),
            ("environment.climate", 30_000, 7_500),
            ("environment.weather", 180_000, 45_000),
            ("environment.surface_water", 120_000, 30_000),
            ("environment.ocean", 10_000, 2_500),
            ("environment.ecosystem", 160_000, 40_000),
            ("environment.contaminant", 100_000, 25_000),
            ("environment.hazard", 10_000, 2_500),
            ("environment.environment_lineage", 10_000, 2_500),
        };
        var actual = Qa04EnvironmentReferenceDecompositionV1.Partitions
            .Select(static slice => (slice.PartitionId.Value, slice.D0Count, slice.D1Count))
            .ToArray();
        Require(actual.SequenceEqual(expected),
            "QA-04 Environment exact D0/D1 partition decomposition drifted.");
    }

    private static void VerifyBoundaryMappings()
    {
        foreach (var slice in Qa04EnvironmentReferenceDecompositionV1.Partitions)
        {
            var firstD0 = Qa04EnvironmentReferenceDecompositionV1.BindD0(slice.D0StartOrdinal);
            var lastD0 = Qa04EnvironmentReferenceDecompositionV1.BindD0(slice.D0EndExclusive - 1);
            Require(firstD0.PartitionId == slice.PartitionId && firstD0.PartitionLocalOrdinal == 0,
                $"Environment D0 first-boundary mapping drifted: {slice.PartitionId.Value}");
            Require(lastD0.PartitionId == slice.PartitionId && lastD0.PartitionLocalOrdinal == slice.D0Count - 1,
                $"Environment D0 last-boundary mapping drifted: {slice.PartitionId.Value}");

            var firstD1 = Qa04EnvironmentReferenceDecompositionV1.BindD1(slice.D1StartOrdinal);
            var lastD1 = Qa04EnvironmentReferenceDecompositionV1.BindD1(slice.D1EndExclusive - 1);
            Require(firstD1.PartitionId == slice.PartitionId && firstD1.PartitionLocalOrdinal == 0 &&
                    firstD1.SourceD0GlobalOrdinals.SequenceEqual(new[]
                    {
                        slice.D0StartOrdinal,
                        slice.D0StartOrdinal + 1,
                        slice.D0StartOrdinal + 2,
                        slice.D0StartOrdinal + 3,
                    }),
                $"Environment D1 first-source mapping drifted: {slice.PartitionId.Value}");
            Require(lastD1.PartitionId == slice.PartitionId && lastD1.PartitionLocalOrdinal == slice.D1Count - 1 &&
                    lastD1.SourceD0GlobalOrdinals[^1] == slice.D0EndExclusive - 1,
                $"Environment D1 last-source mapping drifted: {slice.PartitionId.Value}");
        }
    }

    private static void VerifyEveryD0SourceConsumedExactlyOnce()
    {
        var consumed = new bool[checked((int)Qa04EnvironmentReferenceDecompositionV1.CanonicalD0Count)];
        for (ulong ordinal = 0; ordinal < Qa04EnvironmentReferenceDecompositionV1.CanonicalD1Count; ordinal++)
        {
            var aggregate = Qa04EnvironmentReferenceDecompositionV1.BindD1(ordinal);
            Require(aggregate.SourceD0GlobalOrdinals.Count == Qa04EnvironmentReferenceDecompositionV1.D0SourcesPerD1,
                "Every Environment D1 aggregate must consume exactly four D0 sources.");
            foreach (var sourceOrdinal in aggregate.SourceD0GlobalOrdinals)
            {
                Require(!consumed[checked((int)sourceOrdinal)],
                    $"Environment D0 source was consumed more than once: {sourceOrdinal}");
                consumed[checked((int)sourceOrdinal)] = true;
                var source = Qa04EnvironmentReferenceDecompositionV1.BindD0(sourceOrdinal);
                Require(source.PartitionId == aggregate.PartitionId,
                    "Environment D1 source must stay within the aggregate owner partition.");
            }
        }
        Require(consumed.All(static value => value),
            "Environment D1 decomposition must consume every D0 source exactly once.");
    }

    private static void VerifyCanonicalNextRecordTopology()
    {
        foreach (var partitionId in new[]
                 {
                     "environment.groundwater",
                     "environment.surface_water",
                     "environment.ocean",
                 })
        {
            var slice = Qa04EnvironmentReferenceDecompositionV1.Get(partitionId);
            var ordered = Enumerable.Range(0, checked((int)slice.D0Count))
                .Select(offset => Qa04EnvironmentReferenceDecompositionV1.BindD0(
                    checked(slice.D0StartOrdinal + (ulong)offset)).Descriptor.RecordId)
                .OrderBy(static recordId => recordId)
                .ToArray();
            Require(ordered.Length > 1,
                $"Environment topology partition must contain multiple records: {partitionId}");

            for (var index = 0; index < ordered.Length; index++)
            {
                var expectedNext = ordered[(index + 1) % ordered.Length];
                var actual = Qa04EnvironmentReferenceDecompositionV1.NextD0RecordRef(partitionId, ordered[index]);
                Require(actual.PartitionId.Value == partitionId && actual.RecordId == expectedNext,
                    $"Environment canonical next-record topology drifted: {partitionId}:{index}");
                Require(actual.RecordId != ordered[index],
                    $"Environment canonical topology must not self-reference: {partitionId}:{index}");
            }
        }
    }

    private static void VerifyOutOfRangeFailsClosed()
    {
        ExpectOutOfRange(() => Qa04EnvironmentReferenceDecompositionV1.BindD0(
            Qa04EnvironmentReferenceDecompositionV1.CanonicalD0Count));
        ExpectOutOfRange(() => Qa04EnvironmentReferenceDecompositionV1.BindD1(
            Qa04EnvironmentReferenceDecompositionV1.CanonicalD1Count));

        var firstGeology = Qa04EnvironmentReferenceDecompositionV1.BindD0(0).Descriptor.RecordId;
        ExpectArgument(() => Qa04EnvironmentReferenceDecompositionV1.NextD0RecordRef(
            "environment.geology", firstGeology));
    }

    private static void ExpectOutOfRange(Action action)
    {
        try
        {
            action();
        }
        catch (ArgumentOutOfRangeException)
        {
            return;
        }
        throw new InvalidOperationException("Expected QA-04 Environment ordinal rejection.");
    }

    private static void ExpectArgument(Action action)
    {
        try
        {
            action();
        }
        catch (ArgumentException)
        {
            return;
        }
        throw new InvalidOperationException("Expected QA-04 Environment topology rejection.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
