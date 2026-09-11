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

    /// <summary>
    /// QA-04 の全 hot-terrain-brick descriptor に対応する v2 brick record が存在する場合のみ true。
    /// これは descriptor/count の充足だけを表し、SDF/material 値が canonical benchmark Terrain であること、
    /// または terrain_root/scope closure が成立したことを示さない。
    /// </summary>
    public bool FullDescriptorCountMaterialized
        => MaterializedBrickCount == Qa04TerrainBrickDescriptorMaterializerV1.CanonicalTerrainBrickCount;
}

/// <summary>
/// 既に canonical な QA-04 Terrain descriptor identity を、exact TerrainBrickV1/v2 record material へ結び付ける。
/// 未定義の Terrain 生成 semantics は補完しない。cell origin、SDF sample、surface material id は
/// 明示的な content source からのみ受け取る。common Domain record contract が固定する genesis revision=1 は
/// descriptor binding で強制する。
/// </summary>
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

        var records = CreateRecords(contentSource, recordCount).ToArray();
        var partition = new SpatialTerrainGeometryPartitionStateV2(records);
        if (partition.State.ItemCount != recordCount)
            throw new InvalidDataException("qa04.materialization.terrain-partition-count-mismatch");

        return new Qa04TerrainBrickDescriptorMaterializationV1(partition, recordCount);
    }

    /// <summary>
    /// Materializes exactly one canonical hot-terrain-brick descriptor without building a partition.
    /// This is the production boundary used by bounded-memory Terrain enumeration: descriptor identity
    /// and payload are validated with the same rules as the existing bulk materializer.
    /// </summary>
    public static SpatialTerrainGeometryRecordMaterialV2 MaterializeRecord(
        IQa04TerrainBrickContentSourceV1 contentSource,
        ulong ordinal)
    {
        ArgumentNullException.ThrowIfNull(contentSource);
        ValidateCanonicalContract();
        if (ordinal >= CanonicalTerrainBrickCount)
            throw new ArgumentOutOfRangeException(nameof(ordinal));

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

    private static IEnumerable<SpatialTerrainGeometryRecordMaterialV2> CreateRecords(
        IQa04TerrainBrickContentSourceV1 contentSource,
        ulong count)
    {
        for (ulong ordinal = 0; ordinal < count; ordinal++)
            yield return MaterializeRecord(contentSource, ordinal);
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
