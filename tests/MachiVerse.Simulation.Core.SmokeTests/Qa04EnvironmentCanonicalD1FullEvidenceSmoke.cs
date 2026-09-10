using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04EnvironmentCanonicalD1FullEvidenceSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        var evidence = Qa04EnvironmentCanonicalD1FullEvidenceBuilderV1.Build();

        Require(evidence.Materialization.FullCanonicalD1Materialized,
            "QA-04 Environment D1 full evidence must materialize the complete canonical D1 set.");
        Require(evidence.Materialization.MaterializedRecordCount == Qa04EnvironmentReferenceDecompositionV1.CanonicalD1Count,
            "QA-04 Environment D1 full evidence count drifted.");
        Require(evidence.ConsumedD0SourceCount == Qa04EnvironmentReferenceDecompositionV1.CanonicalD0Count,
            "QA-04 Environment D1 must consume every canonical D0 source exactly once.");
        Require(evidence.PartitionHeaders.Count == Qa04EnvironmentReferenceDecompositionV1.Partitions.Count,
            "QA-04 Environment D1 full evidence must expose all thirteen partition headers.");
        Require(evidence.PartitionHeaders.All(static header =>
                header.OwnerDomain.Value == "environment" &&
                header.Revision == 1 &&
                header.BasisStep == 0 &&
                header.DetailLevel == DetailLevelV1.D1LocalAggregate &&
                header.CanonicalDigest.Length == 32),
            "QA-04 Environment D1 canonical partition header envelope drifted.");
        Require(evidence.PartitionHeaders.Aggregate(0UL, static (sum, header) => checked(sum + header.ItemCount)) ==
                Qa04EnvironmentReferenceDecompositionV1.CanonicalD1Count,
            "QA-04 Environment D1 canonical partition header total drifted.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
