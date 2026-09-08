using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Domains.Spatial;

public readonly record struct AabbMmV1(Vec3MmV1 Min, Vec3MmV1 Max)
{
    public void Validate()
    {
        if (Min.X > Max.X || Min.Y > Max.Y || Min.Z > Max.Z)
            throw new InvalidDataException("spatial.aabb-invalid");
    }
}

public sealed record SpatialIndexEntryV1(OpaqueId128 RecordId, AabbMmV1 Bounds)
{
    public void Validate()
    {
        if (RecordId.IsZero) throw new InvalidDataException("spatial.index-record-id-zero");
        Bounds.Validate();
    }
}

public sealed class HierarchicalAabbGridV1
{
    private static readonly long[] CellEdgeMmByLevel = [1_000L, 8_000L, 64_000L, 512_000L, 4_096_000L];
    private readonly SortedDictionary<SpatialCellKeyV1, SortedSet<OpaqueId128>> _cells = new();

    private HierarchicalAabbGridV1() { }

    public static IReadOnlyList<long> StandardCellEdgesMm => Array.AsReadOnly(CellEdgeMmByLevel);

    public static HierarchicalAabbGridV1 Rebuild(IEnumerable<SpatialIndexEntryV1> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var materialized = entries.ToArray();
        foreach (var entry in materialized) entry.Validate();
        if (materialized.Select(static entry => entry.RecordId).Distinct().Count() != materialized.Length)
            throw new InvalidDataException("spatial.index-record-duplicate");

        var grid = new HierarchicalAabbGridV1();
        foreach (var entry in materialized.OrderBy(static entry => entry.RecordId))
            grid.Register(entry);
        return grid;
    }

    public IReadOnlyList<OpaqueId128> QueryCandidates(AabbMmV1 query)
    {
        query.Validate();
        var candidates = new SortedSet<OpaqueId128>();
        for (byte level = 0; level < CellEdgeMmByLevel.Length; level++)
        {
            foreach (var key in EnumerateCoveredCells(query, level))
            {
                if (_cells.TryGetValue(key, out var ids))
                    candidates.UnionWith(ids);
            }
        }
        return candidates.ToArray();
    }

    public IReadOnlyList<KeyValuePair<SpatialCellKeyV1, IReadOnlyList<OpaqueId128>>> CanonicalCells
        => _cells.Select(static pair =>
            new KeyValuePair<SpatialCellKeyV1, IReadOnlyList<OpaqueId128>>(pair.Key, pair.Value.ToArray())).ToArray();

    private void Register(SpatialIndexEntryV1 entry)
    {
        var level = SelectRegistrationLevel(entry.Bounds);
        foreach (var key in EnumerateCoveredCells(entry.Bounds, level))
        {
            if (!_cells.TryGetValue(key, out var ids))
            {
                ids = new SortedSet<OpaqueId128>();
                _cells.Add(key, ids);
            }
            ids.Add(entry.RecordId);
        }
    }

    private static byte SelectRegistrationLevel(AabbMmV1 bounds)
    {
        for (byte level = 0; level < CellEdgeMmByLevel.Length; level++)
        {
            var cells = CoveredCellCount(bounds, level);
            if (cells <= 8) return level;
        }
        throw new InvalidDataException("spatial.index-aabb-too-large");
    }

    private static ulong CoveredCellCount(AabbMmV1 bounds, byte level)
    {
        var edge = CellEdgeMmByLevel[level];
        var minX = FloorDiv(bounds.Min.X, edge);
        var maxX = FloorDiv(bounds.Max.X, edge);
        var minY = FloorDiv(bounds.Min.Y, edge);
        var maxY = FloorDiv(bounds.Max.Y, edge);
        var minZ = FloorDiv(bounds.Min.Z, edge);
        var maxZ = FloorDiv(bounds.Max.Z, edge);
        var xCount = checked((ulong)(maxX - minX + 1));
        var yCount = checked((ulong)(maxY - minY + 1));
        var zCount = checked((ulong)(maxZ - minZ + 1));
        return checked(xCount * yCount * zCount);
    }

    private static IEnumerable<SpatialCellKeyV1> EnumerateCoveredCells(AabbMmV1 bounds, byte level)
    {
        var edge = CellEdgeMmByLevel[level];
        var minX = RequireIntCell(FloorDiv(bounds.Min.X, edge));
        var maxX = RequireIntCell(FloorDiv(bounds.Max.X, edge));
        var minY = RequireIntCell(FloorDiv(bounds.Min.Y, edge));
        var maxY = RequireIntCell(FloorDiv(bounds.Max.Y, edge));
        var minZ = RequireIntCell(FloorDiv(bounds.Min.Z, edge));
        var maxZ = RequireIntCell(FloorDiv(bounds.Max.Z, edge));

        var keys = new List<SpatialCellKeyV1>();
        for (long x = minX; x <= maxX; x++)
        for (long y = minY; y <= maxY; y++)
        for (long z = minZ; z <= maxZ; z++)
            keys.Add(new SpatialCellKeyV1(level, (int)x, (int)y, (int)z));
        keys.Sort();
        return keys;
    }

    private static long FloorDiv(long value, long divisor)
    {
        var quotient = value / divisor;
        var remainder = value % divisor;
        return remainder < 0 ? quotient - 1 : quotient;
    }

    private static int RequireIntCell(long value)
    {
        if (value < int.MinValue || value > int.MaxValue)
            throw new InvalidDataException("spatial.index-cell-coordinate-overflow");
        return (int)value;
    }
}
