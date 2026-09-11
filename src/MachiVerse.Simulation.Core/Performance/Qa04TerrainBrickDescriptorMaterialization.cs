using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// QA-04 の hot-terrain-brick descriptor 1件に対応する payload 値を供給する境界。
/// この interface を実装しただけでは値を canonical とみなさない。
/// release path で使用できるのは、perf.reference.v1 の canonical Terrain 生成・material 規則が
/// 正本仕様として確定した content source に限る。
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

    public bool FullDescriptorCountMaterialized
        => MaterializedBrickCount == Qa04TerrainBrickDescriptorMaterializerV1.CanonicalTerrainBrickCount;
}

public static class Qa04TerrainBrickDescriptorMaterializerV1
{
    public const ulong CanonicalTerrainBrickCount = 500_000;
    public const uint D0SampleSpacingMm = 250;
    public const ulong InitialRecordRevision = 1;

    private static readonly StableToken TerrainReferenceClass = new("spatial.hot-terrain-brick");

    public static void ValidateCanonicalContract()
    {
        Qa04ReferenceLoadV1.ValidateCanonicalContract();
        SpatialTerrainGeometryRecordSchemaV2.ValidateCanonicalContract();
        SpatialTerrainGeometryPartitionIdentityV2.ValidateCanonicalContract();

        var definition = Qa04ReferenceLoadV1.RecordClasses.Single(
            entry => entry.ClassToken == TerrainReferenceClass);
        if (definition.Count != CanonicalTerrainBrickCount)
            throw new InvalidDataException("qa04.materialization.terrain-count-drift");
        if (InitialRecordRevision != 1)
            throw new InvalidDataException("qa04.materialization.terrain-initial-revision-drift");

        var first = Qa04ReferenceLoadV1.Record(TerrainReferenceClass, 0);
        var last = Qa04ReferenceLoadV1.Record(TerrainReferenceClass, CanonicalTerrainBrickCount - 1);
        if (first.DetailLevel != DetailLevelV1.D0Entity ||
            last.DetailLevel != DetailLevelV1.D0Entity)
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

        var records = CreateRecordsValidated(contentSource, recordCount).ToArray();
        var partition = new SpatialTerrainGeometryPartitionStateV2(records);
        if (partition.State.ItemCount != recordCount)
            throw new InvalidDataException("qa04.materialization.terrain-partition-count-mismatch");

        return new Qa04TerrainBrickDescriptorMaterializationV1(partition, recordCount);
    }

    public static SpatialTerrainGeometryRecordMaterialV2 MaterializeRecord(
        IQa04TerrainBrickContentSourceV1 contentSource,
        ulong ordinal)
    {
        ArgumentNullException.ThrowIfNull(contentSource);
        ValidateCanonicalContract();
        if (ordinal >= CanonicalTerrainBrickCount)
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        return MaterializeRecordValidated(contentSource, ordinal);
    }

    internal static SpatialTerrainGeometryRecordMaterialV2 MaterializeRecordValidated(
        IQa04TerrainBrickContentSourceV1 contentSource,
        ulong ordinal)
    {
        var descriptor = Qa04ReferenceLoadV1.Record(TerrainReferenceClass, ordinal);
        if (descriptor.DetailLevel != DetailLevelV1.D0Entity)
            throw new InvalidDataException("qa04.materialization.terrain-detail-not-d0");

        var brick = contentSource.CreateBrick(descriptor)
            ?? throw new InvalidDataException("qa04.materialization.terrain-content-source-null");
        ValidateDescriptorBinding(descriptor, brick);
        return SpatialTerrainGeometryRecordMaterialV2.FromTerrainBrick(
            brick,
            createdStep: 0,
            detailLevel: descriptor.DetailLevel);
    }

    private static IEnumerable<SpatialTerrainGeometryRecordMaterialV2> CreateRecordsValidated(
        IQa04TerrainBrickContentSourceV1 contentSource,
        ulong count)
    {
        for (ulong ordinal = 0; ordinal < count; ordinal++)
            yield return MaterializeRecordValidated(contentSource, ordinal);
    }

    private static void ValidateDescriptorBinding(Qa04ReferenceRecordV1 descriptor, TerrainBrickV1 brick)
    {
        if (brick.BrickId != descriptor.RecordId)
            throw new InvalidDataException("qa04.materialization.terrain-brick-id-mismatch");
        if (brick.SampleSpacingMm != D0SampleSpacingMm)
            throw new InvalidDataException("qa04.materialization.terrain-d0-spacing-mismatch");
        if (brick.Revision != InitialRecordRevision)
            throw new InvalidDataException("qa04.materialization.terrain-initial-revision-mismatch");
        if (brick.SdfMm.Count != TerrainBrickV1.SdfSampleCount ||
            brick.SurfaceMaterialIds.Count != TerrainBrickV1.SurfaceMaterialCount)
            throw new InvalidDataException("qa04.materialization.terrain-brick-cardinality-mismatch");
    }
}
