using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;

namespace MachiVerse.Simulation.Core.Domains.Environment;

public readonly record struct GroundwaterConductanceEdgeV1(
    SpatialCellKeyV1 A,
    SpatialCellKeyV1 B,
    uint ConductancePpm)
{
    public void Validate()
    {
        if (A == B) throw new InvalidDataException("environment.groundwater-self-edge");
        if (ConductancePpm > 1_000_000)
            throw new InvalidDataException("environment.groundwater-conductance-range");
    }
}

public static class GroundwaterJacobiV1
{
    public const int StandardIterations = 16;
    private const long Scale = 1_000_000;

    public static IReadOnlyDictionary<SpatialCellKeyV1, long> Solve(
        IReadOnlyDictionary<SpatialCellKeyV1, long> initialHeadMm,
        IEnumerable<GroundwaterConductanceEdgeV1> edges)
    {
        ArgumentNullException.ThrowIfNull(initialHeadMm);
        ArgumentNullException.ThrowIfNull(edges);
        var orderedCells = initialHeadMm.OrderBy(static pair => pair.Key).ToArray();
        if (orderedCells.Length == 0)
            throw new ArgumentException("At least one groundwater cell is required.", nameof(initialHeadMm));

        var orderedEdges = edges
            .Select(static edge => Canonicalize(edge))
            .OrderBy(static edge => edge.A)
            .ThenBy(static edge => edge.B)
            .ThenBy(static edge => edge.ConductancePpm)
            .ToArray();
        foreach (var edge in orderedEdges) edge.Validate();
        if (orderedEdges.Distinct().Count() != orderedEdges.Length)
            throw new InvalidDataException("environment.groundwater-edge-duplicate");

        var cellSet = orderedCells.Select(static pair => pair.Key).ToHashSet();
        if (orderedEdges.Any(edge => !cellSet.Contains(edge.A) || !cellSet.Contains(edge.B)))
            throw new InvalidDataException("environment.groundwater-cell-missing");

        var conductanceTotals = orderedCells.ToDictionary(static pair => pair.Key, static _ => 0UL);
        foreach (var edge in orderedEdges)
        {
            conductanceTotals[edge.A] = checked(conductanceTotals[edge.A] + edge.ConductancePpm);
            conductanceTotals[edge.B] = checked(conductanceTotals[edge.B] + edge.ConductancePpm);
        }
        if (conductanceTotals.Values.Any(static total => total > Scale))
            throw new InvalidDataException("environment.groundwater-conductance-sum-range");

        var previous = orderedCells.ToDictionary(static pair => pair.Key, static pair => pair.Value);
        for (var iteration = 0; iteration < StandardIterations; iteration++)
        {
            var deltaNumerator = orderedCells.ToDictionary(static pair => pair.Key, static _ => (Int128)0);
            foreach (var edge in orderedEdges)
            {
                var difference = (Int128)previous[edge.B] - previous[edge.A];
                var transfer = difference * edge.ConductancePpm;
                deltaNumerator[edge.A] += transfer;
                deltaNumerator[edge.B] -= transfer;
            }

            var next = new Dictionary<SpatialCellKeyV1, long>();
            foreach (var pair in orderedCells)
            {
                var delta = EnvironmentIntegerMathV1.DivideRoundToEven(deltaNumerator[pair.Key], Scale);
                try
                {
                    next.Add(pair.Key, checked(previous[pair.Key] + delta));
                }
                catch (OverflowException ex)
                {
                    throw new OverflowException("simulation.numeric-overflow", ex);
                }
            }
            previous = next;
        }

        return new SortedDictionary<SpatialCellKeyV1, long>(previous);
    }

    private static GroundwaterConductanceEdgeV1 Canonicalize(GroundwaterConductanceEdgeV1 edge)
        => edge.A.CompareTo(edge.B) <= 0 ? edge : new GroundwaterConductanceEdgeV1(edge.B, edge.A, edge.ConductancePpm);
}

public readonly record struct OceanCellStockV1(
    long VolumeMillilitre,
    long MomentumUnit,
    long SaltMassGram,
    long ThermalEnergyMillijoule)
{
    public void Validate()
    {
        if (VolumeMillilitre < 0 || SaltMassGram < 0 || ThermalEnergyMillijoule < 0)
            throw new InvalidDataException("environment.ocean-negative-stock");
    }
}

public readonly record struct OceanFluxEdgeV1(
    SpatialCellKeyV1 Source,
    SpatialCellKeyV1 Target,
    long VolumeMillilitre,
    long MomentumUnit,
    long SaltMassGram,
    long ThermalEnergyMillijoule)
{
    public void Validate()
    {
        if (Source == Target) throw new InvalidDataException("environment.ocean-self-edge");
        if (VolumeMillilitre < 0 || SaltMassGram < 0 || ThermalEnergyMillijoule < 0)
            throw new InvalidDataException("environment.ocean-negative-flux");
    }
}

public static class OceanConservativeFluxV1
{
    public static IReadOnlyDictionary<SpatialCellKeyV1, OceanCellStockV1> Apply(
        IReadOnlyDictionary<SpatialCellKeyV1, OceanCellStockV1> frozen,
        IEnumerable<OceanFluxEdgeV1> fluxes)
    {
        ArgumentNullException.ThrowIfNull(frozen);
        ArgumentNullException.ThrowIfNull(fluxes);
        var cells = frozen.OrderBy(static pair => pair.Key).ToArray();
        foreach (var pair in cells) pair.Value.Validate();

        var orderedFluxes = fluxes
            .OrderBy(static edge => edge.Source)
            .ThenBy(static edge => edge.Target)
            .ThenBy(static edge => edge.VolumeMillilitre)
            .ThenBy(static edge => edge.SaltMassGram)
            .ThenBy(static edge => edge.ThermalEnergyMillijoule)
            .ThenBy(static edge => edge.MomentumUnit)
            .ToArray();
        foreach (var edge in orderedFluxes) edge.Validate();

        var delta = cells.ToDictionary(
            static pair => pair.Key,
            static _ => new OceanDelta());
        foreach (var edge in orderedFluxes)
        {
            if (!delta.ContainsKey(edge.Source) || !delta.ContainsKey(edge.Target))
                throw new InvalidDataException("environment.ocean-cell-missing");
            delta[edge.Source].Volume -= edge.VolumeMillilitre;
            delta[edge.Target].Volume += edge.VolumeMillilitre;
            delta[edge.Source].Momentum -= edge.MomentumUnit;
            delta[edge.Target].Momentum += edge.MomentumUnit;
            delta[edge.Source].Salt -= edge.SaltMassGram;
            delta[edge.Target].Salt += edge.SaltMassGram;
            delta[edge.Source].Thermal -= edge.ThermalEnergyMillijoule;
            delta[edge.Target].Thermal += edge.ThermalEnergyMillijoule;
        }

        var result = new SortedDictionary<SpatialCellKeyV1, OceanCellStockV1>();
        foreach (var pair in cells)
        {
            var current = pair.Value;
            var d = delta[pair.Key];
            var volume = (Int128)current.VolumeMillilitre + d.Volume;
            var momentum = (Int128)current.MomentumUnit + d.Momentum;
            var salt = (Int128)current.SaltMassGram + d.Salt;
            var thermal = (Int128)current.ThermalEnergyMillijoule + d.Thermal;
            if (volume < 0 || salt < 0 || thermal < 0)
                throw new InvalidDataException("environment.ocean-insufficient-source-stock");
            result.Add(pair.Key, new OceanCellStockV1(
                ToLong(volume), ToLong(momentum), ToLong(salt), ToLong(thermal)));
        }

        RequireConserved(cells, result);
        return result;
    }

    private static void RequireConserved(
        IReadOnlyList<KeyValuePair<SpatialCellKeyV1, OceanCellStockV1>> before,
        IReadOnlyDictionary<SpatialCellKeyV1, OceanCellStockV1> after)
    {
        static (Int128 Volume, Int128 Momentum, Int128 Salt, Int128 Thermal) Sum(
            IEnumerable<OceanCellStockV1> values)
        {
            Int128 volume = 0, momentum = 0, salt = 0, thermal = 0;
            foreach (var value in values)
            {
                volume += value.VolumeMillilitre;
                momentum += value.MomentumUnit;
                salt += value.SaltMassGram;
                thermal += value.ThermalEnergyMillijoule;
            }
            return (volume, momentum, salt, thermal);
        }

        if (Sum(before.Select(static pair => pair.Value)) != Sum(after.Values))
            throw new InvalidDataException("environment.ocean-conservation-violation");
    }

    private static long ToLong(Int128 value)
    {
        if (value < long.MinValue || value > long.MaxValue)
            throw new OverflowException("simulation.numeric-overflow");
        return (long)value;
    }

    private sealed class OceanDelta
    {
        public Int128 Volume;
        public Int128 Momentum;
        public Int128 Salt;
        public Int128 Thermal;
    }
}

public sealed record ErosionMaterialTransferV1(
    SpatialCellKeyV1 Source,
    SpatialCellKeyV1 Target,
    long MassGram,
    OpaqueId128 SpatialGeometryIntentId)
{
    public void Validate()
    {
        if (Source == Target) throw new InvalidDataException("environment.erosion-self-transfer");
        if (MassGram <= 0) throw new InvalidDataException("environment.erosion-mass-range");
        if (SpatialGeometryIntentId.IsZero)
            throw new InvalidDataException("environment.erosion-spatial-intent-required");
    }
}

public static class ErosionMaterialBoundaryV1
{
    public static IReadOnlyDictionary<SpatialCellKeyV1, long> ApplyMaterialTransfer(
        IReadOnlyDictionary<SpatialCellKeyV1, long> materialMassGram,
        IEnumerable<ErosionMaterialTransferV1> transfers)
    {
        ArgumentNullException.ThrowIfNull(transfers);
        var materialized = transfers.ToArray();
        foreach (var transfer in materialized) transfer.Validate();
        return ConservativeFluxBatchV1.Apply(
            materialMassGram,
            materialized.Select(static transfer =>
                new EnvironmentFluxEdgeV1(transfer.Source, transfer.Target, transfer.MassGram)));
    }
}

public sealed record EcologyCohortStateV1(
    OpaqueId128 CohortId,
    ulong PopulationCount,
    long BiomassGram)
{
    public void Validate()
    {
        if (CohortId.IsZero) throw new InvalidDataException("environment.ecology-cohort-id-zero");
        if (BiomassGram < 0) throw new InvalidDataException("environment.ecology-biomass-negative");
    }
}

public static class EcologyCohortTransitionV1
{
    private const ulong Ppm = 1_000_000;

    public static EcologyCohortStateV1 Advance(
        EcologyCohortStateV1 state,
        uint birthRatePpm,
        uint deathRatePpm,
        uint migrationOutRatePpm,
        WorldSeed256 seed,
        OpaqueId128 worldId,
        ulong step)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.Validate();
        RequireRate(birthRatePpm);
        RequireRate(deathRatePpm);
        RequireRate(migrationOutRatePpm);
        if (worldId.IsZero) throw new ArgumentException("WorldId ZERO is invalid.", nameof(worldId));

        var births = RealizeRate(state.PopulationCount, birthRatePpm, seed, worldId, step, state.CohortId, "ecology.birth");
        var deaths = RealizeRate(state.PopulationCount, deathRatePpm, seed, worldId, step, state.CohortId, "ecology.death");
        var emigrants = RealizeRate(state.PopulationCount, migrationOutRatePpm, seed, worldId, step, state.CohortId, "ecology.migration");
        if ((UInt128)deaths + emigrants > state.PopulationCount)
            throw new InvalidDataException("environment.ecology-outflow-exceeds-population");
        var nextPopulation = checked(state.PopulationCount + births - deaths - emigrants);
        return state with { PopulationCount = nextPopulation };
    }

    private static ulong RealizeRate(
        ulong population,
        uint ratePpm,
        WorldSeed256 seed,
        OpaqueId128 worldId,
        ulong step,
        OpaqueId128 cohortId,
        string purpose)
    {
        var numerator = (UInt128)population * ratePpm;
        var whole = numerator / Ppm;
        var remainder = (ulong)(numerator % Ppm);
        if (whole > ulong.MaxValue) throw new OverflowException("simulation.numeric-overflow");
        if (remainder == 0) return (ulong)whole;
        var context = new RandomContextV1(
            worldId,
            step,
            new StableToken("environment"),
            new StableToken(purpose),
            cohortId,
            OpaqueId128.Zero,
            OpaqueId128.Zero,
            0);
        var extra = DeterministicRandom.BoundedUInt64(seed, context, 0, Ppm) < remainder ? 1UL : 0UL;
        return checked((ulong)whole + extra);
    }

    private static void RequireRate(uint rate)
    {
        if (rate > Ppm) throw new InvalidDataException("environment.ecology-rate-range");
    }
}

public static class ContaminantTransportV1
{
    public static IReadOnlyDictionary<SpatialCellKeyV1, long> ApplyMassFlux(
        IReadOnlyDictionary<SpatialCellKeyV1, long> contaminantMassGram,
        IEnumerable<EnvironmentFluxEdgeV1> fluxes)
        => ConservativeFluxBatchV1.Apply(contaminantMassGram, fluxes);
}
