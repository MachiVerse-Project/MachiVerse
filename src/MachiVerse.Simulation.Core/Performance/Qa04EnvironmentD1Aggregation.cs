using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Exact four-source Environment D1 aggregation primitives from the Alpha 1.1 reference-world
/// decomposition. This type does not choose an aggregate spatial_scope or lineage subject and does
/// not invent a source_digest domain; those authority bindings remain outside this utility.
/// </summary>
public static class Qa04EnvironmentD1AggregationV1
{
    public const int SourceCount = Qa04EnvironmentReferenceDecompositionV1.D0SourcesPerD1;
    public static readonly StableToken MixedStatus = new("active");

    public static void ValidateCanonicalContract()
    {
        Qa04EnvironmentReferenceDecompositionV1.ValidateCanonicalContract();
        Qa04EnvironmentGenesisContractV1.ValidateCanonicalContract();
        if (SourceCount != 4)
            throw new InvalidDataException("qa04.environment.d1-source-count-drift");
        if (MixedStatus.Value != "active")
            throw new InvalidDataException("qa04.environment.d1-mixed-status-drift");
    }

    public static long CheckedSum(IReadOnlyList<long> values)
    {
        RequireFour(values);
        long total = 0;
        foreach (var value in values) total = checked(total + value);
        return total;
    }

    public static ulong CheckedSum(IReadOnlyList<ulong> values)
    {
        RequireFour(values);
        ulong total = 0;
        foreach (var value in values) total = checked(total + value);
        return total;
    }

    public static uint MeanRoundToEven(IReadOnlyList<uint> values)
    {
        RequireFour(values);
        ulong total = 0;
        foreach (var value in values) total = checked(total + value);
        return checked((uint)DivideRoundToEven(total, SourceCount));
    }

    public static int MeanRoundToEven(IReadOnlyList<int> values)
    {
        RequireFour(values);
        long total = 0;
        foreach (var value in values) total = checked(total + value);
        return checked((int)DivideRoundToEven(total, SourceCount));
    }

    public static long MeanRoundToEven(IReadOnlyList<long> values)
    {
        RequireFour(values);
        long total = 0;
        foreach (var value in values) total = checked(total + value);
        return DivideRoundToEven(total, SourceCount);
    }

    public static Vec3Int64V1 MeanRoundToEven(IReadOnlyList<Vec3Int64V1> values)
    {
        RequireFour(values);
        return new Vec3Int64V1(
            MeanRoundToEven(values.Select(static value => value.X).ToArray()),
            MeanRoundToEven(values.Select(static value => value.Y).ToArray()),
            MeanRoundToEven(values.Select(static value => value.Z).ToArray()));
    }

    public static StableToken Mode(IReadOnlyList<StableToken> values)
    {
        RequireFour(values);
        return values
            .GroupBy(static token => token.Value, StringComparer.Ordinal)
            .Select(static group => new { Token = group.Key, Count = group.Count() })
            .OrderByDescending(static value => value.Count)
            .ThenBy(static value => value.Token, StringComparer.Ordinal)
            .Select(static value => new StableToken(value.Token))
            .First();
    }

    public static IReadOnlyList<PartitionRecordRefV1> CanonicalSetUnion(
        IReadOnlyList<IReadOnlyList<PartitionRecordRefV1>> sources)
    {
        RequireFour(sources);
        return Array.AsReadOnly(sources
            .SelectMany(static value => value)
            .Distinct()
            .OrderBy(static value => value.PartitionId.Value, StringComparer.Ordinal)
            .ThenBy(static value => value.RecordId)
            .ToArray());
    }

    public static uint MaxPlusOne(IReadOnlyList<uint> values)
    {
        RequireFour(values);
        return checked(values.Max() + 1u);
    }

    public static ulong MaxPlusOne(IReadOnlyList<ulong> values)
    {
        RequireFour(values);
        return checked(values.Max() + 1UL);
    }

    public static ulong MaxStep(IReadOnlyList<ulong> values)
    {
        RequireFour(values);
        return values.Max();
    }

    public static ulong? OptionalEndMinimum(IReadOnlyList<ulong?> values)
    {
        RequireFour(values);
        var present = values.Where(static value => value.HasValue).Select(static value => value!.Value).ToArray();
        return present.Length == 0 ? null : present.Min();
    }

    public static StableToken Status(IReadOnlyList<StableToken> values)
    {
        RequireFour(values);
        var first = values[0];
        return values.All(value => value == first) ? first : MixedStatus;
    }

    private static ulong DivideRoundToEven(ulong numerator, ulong denominator)
    {
        var quotient = numerator / denominator;
        var remainder = numerator % denominator;
        var twice = checked(remainder * 2UL);
        if (twice < denominator) return quotient;
        if (twice > denominator) return checked(quotient + 1UL);
        return (quotient & 1UL) == 0 ? quotient : checked(quotient + 1UL);
    }

    private static long DivideRoundToEven(long numerator, long denominator)
    {
        if (denominator <= 0) throw new ArgumentOutOfRangeException(nameof(denominator));
        var quotient = numerator / denominator;
        var remainder = numerator % denominator;
        if (remainder == 0) return quotient;

        var absRemainder = remainder < 0 ? checked(-remainder) : remainder;
        var twice = checked(absRemainder * 2L);
        if (twice < denominator) return quotient;
        var direction = numerator < 0 ? -1L : 1L;
        if (twice > denominator) return checked(quotient + direction);
        return (quotient & 1L) == 0 ? quotient : checked(quotient + direction);
    }

    private static void RequireFour<T>(IReadOnlyList<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count != SourceCount)
            throw new ArgumentException($"Environment D1 aggregation requires exactly {SourceCount} sources.", nameof(values));
    }
}
