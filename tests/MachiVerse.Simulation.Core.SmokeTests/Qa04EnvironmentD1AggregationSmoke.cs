using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04EnvironmentD1AggregationSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04EnvironmentD1AggregationV1.ValidateCanonicalContract();
        VerifySumsAndMeans();
        VerifyModeAndStatus();
        VerifyReferenceUnion();
        VerifyGenerationAndStepRules();
        VerifyCardinalityFailsClosed();
    }

    private static void VerifySumsAndMeans()
    {
        Require(Qa04EnvironmentD1AggregationV1.CheckedSum(new long[] { 1, 2, 3, 4 }) == 10,
            "Environment D1 checked signed sum drifted.");
        Require(Qa04EnvironmentD1AggregationV1.CheckedSum(new ulong[] { 1, 2, 3, 4 }) == 10,
            "Environment D1 checked unsigned sum drifted.");
        Require(Qa04EnvironmentD1AggregationV1.MeanRoundToEven(new uint[] { 0, 0, 0, 2 }) == 0,
            "Environment D1 uint half-to-even must round 0.5 to zero.");
        Require(Qa04EnvironmentD1AggregationV1.MeanRoundToEven(new uint[] { 1, 1, 1, 3 }) == 2,
            "Environment D1 uint half-to-even must round 1.5 to two.");
        Require(Qa04EnvironmentD1AggregationV1.MeanRoundToEven(new int[] { -1, -1, -1, -3 }) == -2,
            "Environment D1 signed half-to-even must round -1.5 to -2.");
        Require(Qa04EnvironmentD1AggregationV1.MeanRoundToEven(new long[] { -1, -1, 0, 0 }) == 0,
            "Environment D1 signed half-to-even must round -0.5 to zero.");
        var vector = Qa04EnvironmentD1AggregationV1.MeanRoundToEven(new[]
        {
            new Vec3Int64V1(0, 1, -1),
            new Vec3Int64V1(0, 1, -1),
            new Vec3Int64V1(0, 1, -1),
            new Vec3Int64V1(2, 3, -3),
        });
        Require(vector == new Vec3Int64V1(0, 2, -2),
            "Environment D1 vector component means drifted.");
    }

    private static void VerifyModeAndStatus()
    {
        var a = new StableToken("a");
        var b = new StableToken("b");
        Require(Qa04EnvironmentD1AggregationV1.Mode(new[] { b, a, b, a }) == a,
            "Environment D1 mode tie must choose ASCII ascending token.");
        Require(Qa04EnvironmentD1AggregationV1.Status(new[] { b, b, b, b }) == b,
            "Environment D1 identical status must be preserved.");
        Require(Qa04EnvironmentD1AggregationV1.Status(new[] { b, b, b, a }).Value == "active",
            "Environment D1 mixed status must normalize to active.");
    }

    private static void VerifyReferenceUnion()
    {
        var a = Ref("environment.geology", "00000000000000000000000000000001");
        var b = Ref("environment.geology", "00000000000000000000000000000002");
        var c = Ref("environment.soil", "00000000000000000000000000000001");
        var union = Qa04EnvironmentD1AggregationV1.CanonicalSetUnion(new IReadOnlyList<PartitionRecordRefV1>[]
        {
            new[] { b },
            new[] { a, b },
            Array.Empty<PartitionRecordRefV1>(),
            new[] { c },
        });
        Require(union.SequenceEqual(new[] { a, b, c }),
            "Environment D1 RefList union must deduplicate and canonical-sort refs.");
    }

    private static void VerifyGenerationAndStepRules()
    {
        Require(Qa04EnvironmentD1AggregationV1.MaxPlusOne(new uint[] { 1, 9, 3, 4 }) == 10,
            "Environment D1 generation max+1 drifted.");
        Require(Qa04EnvironmentD1AggregationV1.MaxPlusOne(new ulong[] { 1, 9, 3, 4 }) == 10,
            "Environment D1 revision max+1 drifted.");
        Require(Qa04EnvironmentD1AggregationV1.MaxStep(new ulong[] { 1, 9, 3, 4 }) == 9,
            "Environment D1 basis step max drifted.");
        Require(Qa04EnvironmentD1AggregationV1.OptionalEndMinimum(new ulong?[] { null, null, null, null }) is null,
            "Environment D1 optional end must remain absent when all sources are absent.");
        Require(Qa04EnvironmentD1AggregationV1.OptionalEndMinimum(new ulong?[] { null, 8, 3, null }) == 3,
            "Environment D1 optional end must choose minimum present value.");
    }

    private static void VerifyCardinalityFailsClosed()
    {
        try
        {
            _ = Qa04EnvironmentD1AggregationV1.CheckedSum(new long[] { 1, 2, 3 });
        }
        catch (ArgumentException)
        {
            return;
        }
        throw new InvalidOperationException("Environment D1 aggregation must reject non-four source cardinality.");
    }

    private static PartitionRecordRefV1 Ref(string partitionId, string recordId)
        => new(partitionId, OpaqueId128.Parse(recordId));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
