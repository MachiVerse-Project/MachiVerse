using MachiVerse.Simulation.Core.Domains.Spatial;

namespace MachiVerse.Simulation.Core.Domains.Environment;

public static class EnvironmentIntegerMathV1
{
    public static long DivideRoundToEven(Int128 numerator, Int128 denominator)
    {
        if (denominator == 0) throw new DivideByZeroException();
        if (denominator < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }

        var quotient = numerator / denominator;
        var remainder = numerator % denominator;
        var absoluteRemainder = remainder < 0 ? -remainder : remainder;
        var twiceRemainder = absoluteRemainder * 2;
        if (twiceRemainder > denominator ||
            (twiceRemainder == denominator && (quotient & (Int128)1) != 0))
        {
            quotient += numerator < 0 ? -1 : 1;
        }

        if (quotient < long.MinValue || quotient > long.MaxValue)
            throw new OverflowException("simulation.numeric-overflow");
        return (long)quotient;
    }
}

public static class ClimateRecurrenceV1
{
    public static long Update(long average, long sample, long alphaNumerator, long alphaDenominator)
    {
        if (alphaDenominator <= 0) throw new ArgumentOutOfRangeException(nameof(alphaDenominator));
        if (alphaNumerator < 0 || alphaNumerator > alphaDenominator)
            throw new ArgumentOutOfRangeException(nameof(alphaNumerator));

        var delta = (Int128)sample - average;
        var increment = EnvironmentIntegerMathV1.DivideRoundToEven(
            delta * alphaNumerator,
            alphaDenominator);
        try
        {
            return checked(average + increment);
        }
        catch (OverflowException ex)
        {
            throw new OverflowException("simulation.numeric-overflow", ex);
        }
    }
}

public readonly record struct EnvironmentFluxEdgeV1(
    SpatialCellKeyV1 Source,
    SpatialCellKeyV1 Target,
    long Amount)
{
    public void Validate()
    {
        if (Source == Target)
            throw new InvalidDataException("environment.flux-self-edge");
        if (Amount < 0)
            throw new InvalidDataException("environment.flux-negative-amount");
    }
}

public static class ConservativeFluxBatchV1
{
    public static IReadOnlyDictionary<SpatialCellKeyV1, long> Apply(
        IReadOnlyDictionary<SpatialCellKeyV1, long> frozenStocks,
        IEnumerable<EnvironmentFluxEdgeV1> fluxes)
    {
        ArgumentNullException.ThrowIfNull(frozenStocks);
        ArgumentNullException.ThrowIfNull(fluxes);

        var canonicalStocks = frozenStocks
            .OrderBy(static pair => pair.Key)
            .ToArray();
        if (canonicalStocks.Any(static pair => pair.Value < 0))
            throw new InvalidDataException("environment.stock-negative");

        var orderedFluxes = fluxes
            .OrderBy(static edge => edge.Source)
            .ThenBy(static edge => edge.Target)
            .ThenBy(static edge => edge.Amount)
            .ToArray();
        foreach (var edge in orderedFluxes) edge.Validate();

        var delta = canonicalStocks.ToDictionary(
            static pair => pair.Key,
            static _ => (Int128)0);
        foreach (var edge in orderedFluxes)
        {
            if (!delta.ContainsKey(edge.Source) || !delta.ContainsKey(edge.Target))
                throw new InvalidDataException("environment.flux-cell-missing");
            delta[edge.Source] -= edge.Amount;
            delta[edge.Target] += edge.Amount;
        }

        var result = new SortedDictionary<SpatialCellKeyV1, long>();
        foreach (var (cell, stock) in canonicalStocks)
        {
            var next = (Int128)stock + delta[cell];
            if (next < 0)
                throw new InvalidDataException("environment.flux-insufficient-source-stock");
            if (next > long.MaxValue)
                throw new OverflowException("simulation.numeric-overflow");
            result.Add(cell, (long)next);
        }

        var before = canonicalStocks.Aggregate((Int128)0, static (sum, pair) => sum + pair.Value);
        var after = result.Aggregate((Int128)0, static (sum, pair) => sum + pair.Value);
        if (before != after)
            throw new InvalidDataException("environment.flux-conservation-violation");

        return result;
    }
}

public readonly record struct HydraulicHeadCandidateV1(
    SpatialCellKeyV1 Cell,
    long HydraulicHeadMm);

public static class HydrologySelectionV1
{
    public static HydraulicHeadCandidateV1 SelectMinimumHead(
        IEnumerable<HydraulicHeadCandidateV1> neighbors)
    {
        ArgumentNullException.ThrowIfNull(neighbors);
        var ordered = neighbors
            .OrderBy(static candidate => candidate.HydraulicHeadMm)
            .ThenBy(static candidate => candidate.Cell)
            .ToArray();
        if (ordered.Length == 0)
            throw new ArgumentException("At least one hydrology neighbor is required.", nameof(neighbors));
        return ordered[0];
    }
}
