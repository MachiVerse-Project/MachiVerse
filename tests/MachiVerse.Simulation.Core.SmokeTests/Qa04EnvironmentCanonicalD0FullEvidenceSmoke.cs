using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Performance;

internal static class Qa04EnvironmentCanonicalD0FullEvidenceSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        var evidence = Qa04EnvironmentCanonicalD0FullEvidenceBuilderV1.Build();

        Require(evidence.Materialization.FullCanonicalD0Materialized,
            "Canonical Environment D0 full evidence must materialize the complete reference set.");
        Require(evidence.Materialization.MaterializedRecordCount == Qa04EnvironmentReferenceDecompositionV1.CanonicalD0Count,
            "Canonical Environment D0 full evidence record count drifted.");
        Require(evidence.PartitionHeaders.Count == Qa04EnvironmentReferenceDecompositionV1.Partitions.Count,
            "Canonical Environment D0 full evidence must emit one semantic header per Environment partition.");

        ulong total = 0;
        foreach (var header in evidence.PartitionHeaders)
        {
            var slice = Qa04EnvironmentReferenceDecompositionV1.Get(header.PartitionId.Value);
            Require(header.ItemCount == slice.D0Count,
                $"Canonical Environment D0 partition count drifted: {header.PartitionId.Value}.");
            Require(header.CanonicalDigest.Length == 32 && header.CanonicalDigest.Any(static value => value != 0),
                $"Canonical Environment D0 partition digest is invalid: {header.PartitionId.Value}.");
            total = checked(total + header.ItemCount);
        }

        Require(total == 1_000_000UL,
            "Canonical Environment D0 full evidence must cover exactly 1,000,000 records.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
