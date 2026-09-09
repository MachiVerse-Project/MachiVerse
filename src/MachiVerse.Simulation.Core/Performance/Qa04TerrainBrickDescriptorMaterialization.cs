using MachiVerse.Simulation.Core.Domains.Spatial;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Supplies the payload values for one QA-04 hot-terrain-brick descriptor.
/// Implementing this interface does not make the supplied values canonical. The release path may
/// use a source only after the canonical perf.reference.v1 terrain generation/material rule has
/// been normatively fixed.
/// </summary>
public interface IQa04TerrainBrickContentSourceV1
{
    TerrainBrickV1 CreateBrick(Qa04ReferenceRecordV1 descriptor);
}

public sealed class Qa04TerrainBrickDescriptorMaterializationV1
{
    internal Qa04TerrainBrickDescriptorMaterializationV1(
        SpatialTerrainGeometryPartitionStateV2 partition,
        ulong materializedBrickCount)
    {
        Partition = partition;
        MaterializedBrickCount = materializedBrickCount;
    }

    public SpatialTerrainGeometryPartitionStateV2 Partition { get; }
    public ulong MaterializedBrickCount { get; }

    /// <summary>
    /// True only when every QA-04 hot-terrain-brick descriptor has a corresponding v2 brick record.
    /// This is a descriptor/count statement, not proof that the supplied SDF/material values are the
    /// canonical benchmark terrain or that the required terrain_root/scope closure exists.
    /// </summary>
    public bool FullDescriptorCountMaterialized
        => MaterializedBrickCount == Qa04TerrainBrickDescriptorMaterializerV1.CanonicalTerrainBrickCount;
}

/// <summary>
/// Binds the already-canonical QA-04 terrain descriptor identities to exact TerrainBrickV1/v2 record
/// material without inventing the missing terrain-generation semantics. Cell origin, SDF samples,
/// surface material ids, and revision are deliberately supplied by an explicit content source.
/// </summary>
public static class Qa04TerrainBrickDescriptorMaterializerV1
{
    public const ulong CanonicalTerrainBrickCount = 500_000;
    public const uint D0SampleSpacingMm = 250;

    private static readonly Determinism.StableToken TerrainReferenceClass =
        new("spatial.hot-terrain-brick");

    public static void ValidateCanonicalContract()
    {
        Qa04ReferenceLoadV1.ValidateCanonicalContract();
        SpatialTerrainGeometryRecordSchemaV2.ValidateCanonicalContract();
        SpatialTerrainGeometryPartitionIdentityV2.ValidateCanonicalContract();

        var definition = Qa04ReferenceLoadV1.RecordClasses.Single(
            entry => entry.ClassToken == TerrainReferenceClass);
        if (definition.Count != CanonicalTerrainBrickCount)
            throw new InvalidDataException("qa04.materialization.terrain-count-drift");

        var first = Qa04ReferenceLoadV1.Record(TerrainReferenceClass, 0);
        var last = Qa04ReferenceLoadV1.Record(TerrainReferenceClass, CanonicalTerrainBrickCount - 1);
        if (first.DetailLevel != WorldState.DetailLevelV1.D0Entity ||
            last.DetailLevel != WorldState.DetailLevelV1.D0Entity)
            throw new InvalidDataException("qa04.materialization.terrain-detail-drift");
    }

    public static Qa04TerrainBrickDescriptorMaterializationV1 MaterializeCanonicalDescriptorCount(
        IQa04TerrainBrickContentSourceV1 contentSource)
        => Materialize(contentSource, CanonicalTerrainBrickCount);

    public static Qa04TerrainBrickDescriptorMaterializationV1 Materialize(
        IQa04TerrainBrickContentSourceV1 contentSource,
        ulong recordCount)
    {
        ArgumentNullException.ThrowIfNull(contentSource);
        ValidateCanonicalContract();
        if (recordCount is 0 or > CanonicalTerrainBrickCount)
            throw new ArgumentOutOfRangeException(nameof(recordCount));

        var records = CreateRecords(contentSource, recordCount).ToArray();
        var partition = new SpatialTerrainGeometryPartitionStateV2(records);
        if (partition.State.ItemCount != recordCount)
            throw new InvalidDataException("qa04.materialization.terrain-partition-count-mismatch");

        return new Qa04TerrainBrickDescriptorMaterializationV1(partition, recordCount);
    }

    private static IEnumerable<SpatialTerrainGeometryRecordMaterialV2> CreateRecords(
        IQa04TerrainBrickContentSourceV1 contentSource,
        ulong count)
    {
        for (ulong ordinal = 0; ordinal < count; ordinal++)
        {
            var descriptor = Qa04ReferenceLoadV1.Record(TerrainReferenceClass, ordinal);
            if (descriptor.DetailLevel != WorldState.DetailLevelV1.D0Entity)
                throw new InvalidDataException("qa04.materialization.terrain-detail-not-d0");

            var brick = contentSource.CreateBrick(descriptor)
                ?? throw new InvalidDataException("qa04.materialization.terrain-content-source-null");
            ValidateDescriptorBinding(descriptor, brick);
            yield return SpatialTerrainGeometryRecordMaterialV2.FromTerrainBrick(
                brick,
                createdStep: 0,
                detailLevel: descriptor.DetailLevel);
        }
    }

    private static void ValidateDescriptorBinding(Qa04ReferenceRecordV1 descriptor, TerrainBrickV1 brick)
    {
        if (brick.BrickId != descriptor.RecordId)
            throw new InvalidDataException("qa04.materialization.terrain-brick-id-mismatch");
        if (brick.SampleSpacingMm != D0SampleSpacingMm)
            throw new InvalidDataException("qa04.materialization.terrain-d0-spacing-mismatch");
        if (brick.SdfMm.Count != TerrainBrickV1.SdfSampleCount ||
            brick.SurfaceMaterialIds.Count != TerrainBrickV1.SurfaceMaterialCount)
            throw new InvalidDataException("qa04.materialization.terrain-brick-cardinality-mismatch");
    }
}
