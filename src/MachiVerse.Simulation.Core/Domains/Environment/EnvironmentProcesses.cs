using System.Collections.ObjectModel;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;

namespace MachiVerse.Simulation.Core.Domains.Environment;

public static class GroundwaterJacobiV1
{
    public const int IterationCount = 16;

    public static async Task<IReadOnlyDictionary<SpatialCellKeyV1, long>> RunAsync(
        IReadOnlyDictionary<SpatialCellKeyV1, long> initialHeadMm,
        Func<SpatialCellKeyV1, IReadOnlyDictionary<SpatialCellKeyV1, long>, long> update,
        int workerCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(initialHeadMm);
        ArgumentNullException.ThrowIfNull(update);
        if (initialHeadMm.Count == 0)
            throw new ArgumentException("Groundwater Jacobi requires at least one cell.", nameof(initialHeadMm));

        var canonicalCells = initialHeadMm.Keys.OrderBy(static cell => cell).ToArray();
        IReadOnlyDictionary<SpatialCellKeyV1, long> previous = new ReadOnlyDictionary<SpatialCellKeyV1, long>(
            new SortedDictionary<SpatialCellKeyV1, long>(initialHeadMm));

        for (var iteration = 0; iteration < IterationCount; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frozenPrevious = previous;
            var nextValues = await DeterministicBatchExecutor.RunAsync(
                canonicalCells,
                workerCount,
                (cell, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    return ValueTask.FromResult(update(cell, frozenPrevious));
                },
                cancellationToken);

            var next = new SortedDictionary<SpatialCellKeyV1, long>();
            for (var index = 0; index < canonicalCells.Length; index++)
                next.Add(canonicalCells[index], nextValues[index]);
            previous = new ReadOnlyDictionary<SpatialCellKeyV1, long>(next);
        }

        return previous;
    }
}

public readonly record struct SurfaceWaterCapacityV1(
    SpatialCellKeyV1 Cell,
    long CurrentVolumeMl,
    long CapacityVolumeMl)
{
    public long AvailableVolumeMl
    {
        get
        {
            if (CurrentVolumeMl < 0 || CapacityVolumeMl < 0 || CurrentVolumeMl > CapacityVolumeMl)
                throw new InvalidDataException("environment.surface-water-invalid-capacity");
            return CapacityVolumeMl - CurrentVolumeMl;
        }
    }
}

public readonly record struct SurfaceWaterAllocationV1(
    SpatialCellKeyV1 Cell,
    long VolumeMl);

public sealed record SurfaceWaterAllocationResultV1(
    IReadOnlyList<SurfaceWaterAllocationV1> Allocations,
    long UnallocatedVolumeMl);

public static class SurfaceWaterOverflowAllocatorV1
{
    public static SurfaceWaterAllocationResultV1 Allocate(
        long overflowVolumeMl,
        IEnumerable<SurfaceWaterCapacityV1> adjacentCells)
    {
        if (overflowVolumeMl < 0) throw new ArgumentOutOfRangeException(nameof(overflowVolumeMl));
        ArgumentNullException.ThrowIfNull(adjacentCells);

        var canonical = adjacentCells.OrderBy(static item => item.Cell).ToArray();
        if (canonical.Select(static item => item.Cell).Distinct().Count() != canonical.Length)
            throw new InvalidDataException("environment.surface-water-duplicate-adjacent-cell");

        var remaining = overflowVolumeMl;
        var allocations = new List<SurfaceWaterAllocationV1>(canonical.Length);
        foreach (var cell in canonical)
        {
            var available = cell.AvailableVolumeMl;
            var allocated = Math.Min(remaining, available);
            if (allocated > 0)
                allocations.Add(new SurfaceWaterAllocationV1(cell.Cell, allocated));
            remaining -= allocated;
            if (remaining == 0) break;
        }

        return new SurfaceWaterAllocationResultV1(
            Array.AsReadOnly(allocations.ToArray()),
            remaining);
    }
}

public readonly record struct OceanConservedStockV1(
    long VolumeMl,
    long MomentumX,
    long MomentumY,
    long MomentumZ,
    long SalinityStock,
    long ThermalStock)
{
    public void Validate()
    {
        if (VolumeMl < 0 || SalinityStock < 0 || ThermalStock < 0)
            throw new InvalidDataException("environment.ocean-negative-conserved-stock");
    }
}

public readonly record struct OceanFluxV1(
    SpatialCellKeyV1 Source,
    SpatialCellKeyV1 Target,
    OceanConservedStockV1 Transfer)
{
    public void Validate()
    {
        if (Source == Target) throw new InvalidDataException("environment.ocean-self-flux");
        Transfer.Validate();
    }
}

public static class OceanConservativeFluxBatchV1
{
    public static IReadOnlyDictionary<SpatialCellKeyV1, OceanConservedStockV1> Apply(
        IReadOnlyDictionary<SpatialCellKeyV1, OceanConservedStockV1> frozenStocks,
        IEnumerable<OceanFluxV1> fluxes)
    {
        ArgumentNullException.ThrowIfNull(frozenStocks);
        ArgumentNullException.ThrowIfNull(fluxes);

        var stocks = frozenStocks.OrderBy(static pair => pair.Key).ToArray();
        foreach (var pair in stocks) pair.Value.Validate();
        var orderedFluxes = fluxes
            .OrderBy(static edge => edge.Source)
            .ThenBy(static edge => edge.Target)
            .ToArray();
        foreach (var edge in orderedFluxes) edge.Validate();

        var deltas = stocks.ToDictionary(
            static pair => pair.Key,
            static _ => new OceanDeltaV1());

        foreach (var edge in orderedFluxes)
        {
            if (!deltas.TryGetValue(edge.Source, out var source) || !deltas.TryGetValue(edge.Target, out var target))
                throw new InvalidDataException("environment.ocean-flux-cell-missing");
            source.Subtract(edge.Transfer);
            target.Add(edge.Transfer);
        }

        var result = new SortedDictionary<SpatialCellKeyV1, OceanConservedStockV1>();
        foreach (var (cell, stock) in stocks)
        {
            var next = deltas[cell].Apply(stock);
            next.Validate();
            result.Add(cell, next);
        }

        RequireConserved(stocks.Select(static pair => pair.Value), result.Values);
        return result;
    }

    private static void RequireConserved(
        IEnumerable<OceanConservedStockV1> before,
        IEnumerable<OceanConservedStockV1> after)
    {
        var beforeTotal = OceanDeltaV1.Sum(before);
        var afterTotal = OceanDeltaV1.Sum(after);
        if (!beforeTotal.Equals(afterTotal))
            throw new InvalidDataException("environment.ocean-conservation-violation");
    }

    private sealed class OceanDeltaV1
    {
        private Int128 _volume;
        private Int128 _momentumX;
        private Int128 _momentumY;
        private Int128 _momentumZ;
        private Int128 _salinity;
        private Int128 _thermal;

        public void Add(OceanConservedStockV1 value)
        {
            _volume += value.VolumeMl;
            _momentumX += value.MomentumX;
            _momentumY += value.MomentumY;
            _momentumZ += value.MomentumZ;
            _salinity += value.SalinityStock;
            _thermal += value.ThermalStock;
        }

        public void Subtract(OceanConservedStockV1 value)
        {
            _volume -= value.VolumeMl;
            _momentumX -= value.MomentumX;
            _momentumY -= value.MomentumY;
            _momentumZ -= value.MomentumZ;
            _salinity -= value.SalinityStock;
            _thermal -= value.ThermalStock;
        }

        public OceanConservedStockV1 Apply(OceanConservedStockV1 basis)
            => new(
                CheckedLong((Int128)basis.VolumeMl + _volume),
                CheckedLong((Int128)basis.MomentumX + _momentumX),
                CheckedLong((Int128)basis.MomentumY + _momentumY),
                CheckedLong((Int128)basis.MomentumZ + _momentumZ),
                CheckedLong((Int128)basis.SalinityStock + _salinity),
                CheckedLong((Int128)basis.ThermalStock + _thermal));

        public static OceanTotalsV1 Sum(IEnumerable<OceanConservedStockV1> values)
        {
            var total = new OceanTotalsV1();
            foreach (var value in values)
            {
                total.Volume += value.VolumeMl;
                total.MomentumX += value.MomentumX;
                total.MomentumY += value.MomentumY;
                total.MomentumZ += value.MomentumZ;
                total.Salinity += value.SalinityStock;
                total.Thermal += value.ThermalStock;
            }
            return total;
        }

        private static long CheckedLong(Int128 value)
        {
            if (value < long.MinValue || value > long.MaxValue)
                throw new OverflowException("simulation.numeric-overflow");
            return (long)value;
        }
    }

    private sealed class OceanTotalsV1 : IEquatable<OceanTotalsV1>
    {
        public Int128 Volume;
        public Int128 MomentumX;
        public Int128 MomentumY;
        public Int128 MomentumZ;
        public Int128 Salinity;
        public Int128 Thermal;

        public bool Equals(OceanTotalsV1? other)
            => other is not null &&
               Volume == other.Volume &&
               MomentumX == other.MomentumX &&
               MomentumY == other.MomentumY &&
               MomentumZ == other.MomentumZ &&
               Salinity == other.Salinity &&
               Thermal == other.Thermal;

        public override bool Equals(object? obj) => obj is OceanTotalsV1 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Volume, MomentumX, MomentumY, MomentumZ, Salinity, Thermal);
    }
}

public readonly record struct EcologyPopulationTransitionV1(
    ulong BasisPopulation,
    ulong Births,
    ulong Deaths,
    ulong CandidatePopulation);

public static class EcologyPopulationV1
{
    private const ulong PpmScale = 1_000_000;

    public static EcologyPopulationTransitionV1 Transition(
        WorldSeed256 worldSeed,
        OpaqueId128 worldId,
        ulong step,
        OpaqueId128 cohortId,
        ulong population,
        uint birthRatePpm,
        uint deathRatePpm)
    {
        if (worldId.IsZero) throw new ArgumentException("WorldId ZERO is invalid.", nameof(worldId));
        if (cohortId.IsZero) throw new ArgumentException("CohortId ZERO is invalid.", nameof(cohortId));
        if (birthRatePpm > PpmScale) throw new ArgumentOutOfRangeException(nameof(birthRatePpm));
        if (deathRatePpm > PpmScale) throw new ArgumentOutOfRangeException(nameof(deathRatePpm));

        var births = RealizePpmCount(worldSeed, worldId, step, cohortId, population, birthRatePpm, "environment.ecology.birth");
        var deaths = RealizePpmCount(worldSeed, worldId, step, cohortId, population, deathRatePpm, "environment.ecology.death");
        var survivors = population - deaths;
        var next = checked(survivors + births);
        return new EcologyPopulationTransitionV1(population, births, deaths, next);
    }

    private static ulong RealizePpmCount(
        WorldSeed256 worldSeed,
        OpaqueId128 worldId,
        ulong step,
        OpaqueId128 cohortId,
        ulong population,
        uint ratePpm,
        string purpose)
    {
        var product = (UInt128)population * ratePpm;
        var whole = product / PpmScale;
        var remainder = (ulong)(product % PpmScale);
        if (whole > ulong.MaxValue) throw new OverflowException("simulation.numeric-overflow");
        var realized = (ulong)whole;
        if (remainder == 0) return realized;

        var context = new RandomContextV1(
            worldId,
            step,
            new StableToken("environment"),
            new StableToken(purpose),
            cohortId,
            OpaqueId128.Zero,
            OpaqueId128.Zero,
            0);
        if (DeterministicRandom.BoundedUInt64(worldSeed, context, 0, PpmScale) < remainder)
            realized = checked(realized + 1);
        return realized;
    }
}

public static class ContaminantTransportV1
{
    public static IReadOnlyDictionary<SpatialCellKeyV1, long> ApplyMassFlux(
        IReadOnlyDictionary<SpatialCellKeyV1, long> frozenStockMassGram,
        IEnumerable<EnvironmentFluxEdgeV1> massFluxes)
        => ConservativeFluxBatchV1.Apply(frozenStockMassGram, massFluxes);
}
