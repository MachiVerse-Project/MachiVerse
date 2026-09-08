using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains.Spatial;

public readonly record struct Vec3MmV1(long X, long Y, long Z);

public enum SignedDistanceClassV1 : byte
{
    Solid = 0,
    Boundary = 1,
    Void = 2,
}

public readonly record struct SpatialCellKeyV1(byte Level, int X, int Y, int Z) : IComparable<SpatialCellKeyV1>
{
    public int CompareTo(SpatialCellKeyV1 other)
    {
        var comparison = Level.CompareTo(other.Level);
        if (comparison != 0) return comparison;
        comparison = SignedBias(X).CompareTo(SignedBias(other.X));
        if (comparison != 0) return comparison;
        comparison = SignedBias(Y).CompareTo(SignedBias(other.Y));
        if (comparison != 0) return comparison;
        return SignedBias(Z).CompareTo(SignedBias(other.Z));
    }

    public static uint SignedBias(int value) => unchecked((uint)value) ^ 0x80000000u;
}

public static class TerrainOctreeV1
{
    public const int ChildCount = 8;

    public static int ChildIndex(bool highX, bool highY, bool highZ)
        => (highX ? 1 : 0) | (highY ? 2 : 0) | (highZ ? 4 : 0);

    public static IReadOnlyList<int> CanonicalChildTraversal { get; } =
        Array.AsReadOnly(Enumerable.Range(0, ChildCount).ToArray());
}

public sealed class TerrainBrickV1
{
    public const int CellsPerAxis = 8;
    public const int SamplesPerAxis = CellsPerAxis + 1;
    public const int SdfSampleCount = SamplesPerAxis * SamplesPerAxis * SamplesPerAxis;
    public const int SurfaceMaterialCount = CellsPerAxis * CellsPerAxis * CellsPerAxis;

    private readonly int[] _sdfMm;
    private readonly ushort[] _surfaceMaterialIds;

    public TerrainBrickV1(
        OpaqueId128 brickId,
        byte level,
        SpatialCellKeyV1 cellOrigin,
        uint sampleSpacingMm,
        IEnumerable<int> sdfMm,
        IEnumerable<ushort> surfaceMaterialIds,
        ulong revision)
    {
        if (brickId.IsZero) throw new ArgumentException("Terrain brick id ZERO is invalid.", nameof(brickId));
        if (sampleSpacingMm == 0) throw new ArgumentOutOfRangeException(nameof(sampleSpacingMm));
        if (revision == 0) throw new ArgumentOutOfRangeException(nameof(revision));
        ArgumentNullException.ThrowIfNull(sdfMm);
        ArgumentNullException.ThrowIfNull(surfaceMaterialIds);

        var samples = sdfMm.ToArray();
        var materials = surfaceMaterialIds.ToArray();
        if (samples.Length != SdfSampleCount)
            throw new ArgumentException($"SBO-SDF brick requires exactly {SdfSampleCount} SDF samples.", nameof(sdfMm));
        if (materials.Length != SurfaceMaterialCount)
            throw new ArgumentException($"SBO-SDF brick requires exactly {SurfaceMaterialCount} material cells.", nameof(surfaceMaterialIds));

        BrickId = brickId;
        Level = level;
        CellOrigin = cellOrigin;
        SampleSpacingMm = sampleSpacingMm;
        Revision = revision;
        _sdfMm = samples;
        _surfaceMaterialIds = materials;
    }

    public OpaqueId128 BrickId { get; }
    public byte Level { get; }
    public SpatialCellKeyV1 CellOrigin { get; }
    public uint SampleSpacingMm { get; }
    public ulong Revision { get; }
    public IReadOnlyList<int> SdfMm => Array.AsReadOnly(_sdfMm);
    public IReadOnlyList<ushort> SurfaceMaterialIds => Array.AsReadOnly(_surfaceMaterialIds);

    public int GetSdfSample(int x, int y, int z)
        => _sdfMm[SampleIndex(x, y, z)];

    public ushort GetSurfaceMaterial(int x, int y, int z)
        => _surfaceMaterialIds[CellIndex(x, y, z)];

    public SignedDistanceClassV1 ClassifySample(int x, int y, int z)
        => Classify(GetSdfSample(x, y, z));

    public static SignedDistanceClassV1 Classify(int sdfMm)
        => sdfMm < 0
            ? SignedDistanceClassV1.Solid
            : sdfMm > 0
                ? SignedDistanceClassV1.Void
                : SignedDistanceClassV1.Boundary;

    public FixedQ32_32 InterpolateCellMm(
        int cellX,
        int cellY,
        int cellZ,
        FixedQ32_32 fractionX,
        FixedQ32_32 fractionY,
        FixedQ32_32 fractionZ)
    {
        RequireCellCoordinate(cellX, nameof(cellX));
        RequireCellCoordinate(cellY, nameof(cellY));
        RequireCellCoordinate(cellZ, nameof(cellZ));
        RequireUnitFraction(fractionX, nameof(fractionX));
        RequireUnitFraction(fractionY, nameof(fractionY));
        RequireUnitFraction(fractionZ, nameof(fractionZ));

        var c000 = FixedQ32_32.FromInteger(GetSdfSample(cellX, cellY, cellZ));
        var c100 = FixedQ32_32.FromInteger(GetSdfSample(cellX + 1, cellY, cellZ));
        var c010 = FixedQ32_32.FromInteger(GetSdfSample(cellX, cellY + 1, cellZ));
        var c110 = FixedQ32_32.FromInteger(GetSdfSample(cellX + 1, cellY + 1, cellZ));
        var c001 = FixedQ32_32.FromInteger(GetSdfSample(cellX, cellY, cellZ + 1));
        var c101 = FixedQ32_32.FromInteger(GetSdfSample(cellX + 1, cellY, cellZ + 1));
        var c011 = FixedQ32_32.FromInteger(GetSdfSample(cellX, cellY + 1, cellZ + 1));
        var c111 = FixedQ32_32.FromInteger(GetSdfSample(cellX + 1, cellY + 1, cellZ + 1));

        var x00 = Lerp(c000, c100, fractionX);
        var x10 = Lerp(c010, c110, fractionX);
        var x01 = Lerp(c001, c101, fractionX);
        var x11 = Lerp(c011, c111, fractionX);
        var y0 = Lerp(x00, x10, fractionY);
        var y1 = Lerp(x01, x11, fractionY);
        return Lerp(y0, y1, fractionZ);
    }

    public IReadOnlyList<SignedDistanceClassV1> VerticalSampleClasses(int sampleX, int sampleY)
    {
        RequireSampleCoordinate(sampleX, nameof(sampleX));
        RequireSampleCoordinate(sampleY, nameof(sampleY));
        var result = new SignedDistanceClassV1[SamplesPerAxis];
        for (var z = 0; z < SamplesPerAxis; z++)
            result[z] = ClassifySample(sampleX, sampleY, z);
        return Array.AsReadOnly(result);
    }

    private static FixedQ32_32 Lerp(FixedQ32_32 from, FixedQ32_32 to, FixedQ32_32 fraction)
        => from + ((to - from) * fraction);

    private static void RequireUnitFraction(FixedQ32_32 fraction, string name)
    {
        if (fraction.Raw < FixedQ32_32.Zero.Raw || fraction.Raw > FixedQ32_32.One.Raw)
            throw new ArgumentOutOfRangeException(name, "Interpolation fraction must be in Q32.32 [0,1].");
    }

    private static int SampleIndex(int x, int y, int z)
    {
        RequireSampleCoordinate(x, nameof(x));
        RequireSampleCoordinate(y, nameof(y));
        RequireSampleCoordinate(z, nameof(z));
        return checked(((z * SamplesPerAxis) + y) * SamplesPerAxis + x);
    }

    private static int CellIndex(int x, int y, int z)
    {
        RequireCellCoordinate(x, nameof(x));
        RequireCellCoordinate(y, nameof(y));
        RequireCellCoordinate(z, nameof(z));
        return checked(((z * CellsPerAxis) + y) * CellsPerAxis + x);
    }

    private static void RequireSampleCoordinate(int value, string name)
    {
        if ((uint)value >= SamplesPerAxis) throw new ArgumentOutOfRangeException(name);
    }

    private static void RequireCellCoordinate(int value, string name)
    {
        if ((uint)value >= CellsPerAxis) throw new ArgumentOutOfRangeException(name);
    }
}

public sealed record SpatialContainmentRelationV1(
    PartitionRecordRefV1 SubjectRef,
    PartitionRecordRefV1 ContainerScope,
    StableToken RelationClass,
    ulong BasisGeometryRevision);

public sealed class SpatialContainmentIndexV1
{
    private readonly IReadOnlyList<SpatialContainmentRelationV1> _canonical;

    public SpatialContainmentIndexV1(IEnumerable<SpatialContainmentRelationV1> relations)
    {
        ArgumentNullException.ThrowIfNull(relations);
        var ordered = relations
            .OrderBy(static relation => relation.SubjectRef, PartitionRecordRefComparerV1.Instance)
            .ThenBy(static relation => relation.ContainerScope, PartitionRecordRefComparerV1.Instance)
            .ThenBy(static relation => relation.RelationClass.Value, StringComparer.Ordinal)
            .ThenBy(static relation => relation.BasisGeometryRevision)
            .ToArray();
        if (ordered.Distinct().Count() != ordered.Length)
            throw new InvalidDataException("spatial.containment-relation-duplicate");
        _canonical = Array.AsReadOnly(ordered);
    }

    public IReadOnlyList<SpatialContainmentRelationV1> CanonicalRelations => _canonical;

    public IReadOnlyList<SpatialContainmentRelationV1> ForSubject(PartitionRecordRefV1 subject)
        => _canonical.Where(relation => relation.SubjectRef == subject).ToArray();
}

public static class SpatialGeometryRevisionV1
{
    public static void RequireCurrent(ulong expectedRevision, ulong currentRevision)
    {
        if (expectedRevision == 0 || currentRevision == 0)
            throw new InvalidDataException("spatial.geometry-revision-zero");
        if (expectedRevision != currentRevision)
            throw new InvalidDataException("spatial.geometry-stale-revision");
    }
}

internal sealed class PartitionRecordRefComparerV1 : IComparer<PartitionRecordRefV1>
{
    public static PartitionRecordRefComparerV1 Instance { get; } = new();

    public int Compare(PartitionRecordRefV1 left, PartitionRecordRefV1 right)
    {
        var partition = string.CompareOrdinal(left.PartitionId.Value, right.PartitionId.Value);
        return partition != 0 ? partition : left.RecordId.CompareTo(right.RecordId);
    }
}
