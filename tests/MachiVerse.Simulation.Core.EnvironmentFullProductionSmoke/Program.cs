using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void ValidateD0()
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

    Require(total == Qa04EnvironmentReferenceDecompositionV1.CanonicalD0Count,
        "Canonical Environment D0 full evidence must cover the complete canonical record set.");
}

static void ValidateD1()
{
    var evidence = Qa04EnvironmentCanonicalD1FullEvidenceBuilderV1.Build();

    Require(evidence.Materialization.FullCanonicalD1Materialized,
        "QA-04 Environment D1 full evidence must materialize the complete canonical D1 set.");
    Require(evidence.Materialization.MaterializedRecordCount == Qa04EnvironmentReferenceDecompositionV1.CanonicalD1Count,
        "QA-04 Environment D1 full evidence count drifted.");
    Require(evidence.ConsumedD0SourceCount == Qa04EnvironmentReferenceDecompositionV1.CanonicalD0Count,
        "QA-04 Environment D1 must consume every canonical D0 source exactly once.");
    Require(evidence.PartitionHeaders.Count == Qa04EnvironmentReferenceDecompositionV1.Partitions.Count,
        "QA-04 Environment D1 full evidence must expose all canonical Environment partition headers.");
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

Console.WriteLine("Validating full canonical Environment D0 evidence...");
ValidateD0();
Console.WriteLine("Environment D0 full evidence: PASS");

Console.WriteLine("Validating full canonical Environment D1 evidence...");
ValidateD1();
Console.WriteLine("Environment D1 full evidence: PASS");
