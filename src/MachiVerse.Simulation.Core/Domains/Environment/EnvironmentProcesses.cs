using MachiVerse.Simulation.Core.Domains.Spatial;

namespace MachiVerse.Simulation.Core.Domains.Environment;

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
