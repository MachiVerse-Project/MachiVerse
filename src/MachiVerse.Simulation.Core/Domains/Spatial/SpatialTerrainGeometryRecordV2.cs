using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains.Spatial;

public enum SpatialTerrainGeometryFieldKindV2 : byte
{
    Token = 1,
    Ref = 2,
    UInt64 = 3,
    TokenList = 4,
    RefList = 5,
    Digest = 6,
    UInt8 = 7,
    SpatialCellKey = 8,
    UInt32 = 9,
    FixedInt32List729 = 10,
    FixedUInt16List512 = 11,
}

public sealed record SpatialTerrainGeometryFieldDescriptorV2(
    ushort Ordinal,
    string Name,
    SpatialTerrainGeometryFieldKindV2 Kind,
    bool Optional);

/// <summary>
/// Normative v2 record contract for spatial.terrain_geometry.
/// This catalog does not mutate StandardDomainPartitionRegistry: production remains on v1
/// until the mixed-record state/snapshot migration is integrated end-to-end.
/// </summary>
public static class SpatialTerrainGeometryRecordSchemaV2
{
    public const string PartitionId = SpatialTerrainGeometryPayloadV1.PartitionId;
    public const string TerrainRootKind = "terrain_root";
    public const string TerrainBrickKind = "terrain_brick";

    public static SchemaRefV1 RecordSchema { get; } =
        new("domain.spatial.terrain_geometry.record", 2, 0);

    public static IReadOnlyList<string> RecordKinds { get; } =
        Array.AsReadOnly(new[] { TerrainBrickKind, TerrainRootKind });

    public static IReadOnlyList<SpatialTerrainGeometryFieldDescriptorV2> TerrainRootFields { get; } =
        Array.AsReadOnly(new[]
        {
            Field(1, "record_kind", SpatialTerrainGeometryFieldKindV2.Token),
            Field(2, "scope_ref", SpatialTerrainGeometryFieldKindV2.Ref),
            Field(3, "root_brick_ref", SpatialTerrainGeometryFieldKindV2.Ref),
            Field(4, "geometry_revision", SpatialTerrainGeometryFieldKindV2.UInt64),
            Field(5, "surface_classes", SpatialTerrainGeometryFieldKindV2.TokenList),
            Field(6, "connectivity_refs", SpatialTerrainGeometryFieldKindV2.RefList),
            Field(7, "archive_anchor", SpatialTerrainGeometryFieldKindV2.Digest, optional: true),
        });

    public static IReadOnlyList<SpatialTerrainGeometryFieldDescriptorV2> TerrainBrickFields { get; } =
        Array.AsReadOnly(new[]
        {
            Field(1, "record_kind", SpatialTerrainGeometryFieldKindV2.Token),
            Field(2, "level", SpatialTerrainGeometryFieldKindV2.UInt8),
            Field(3, "cell_origin", SpatialTerrainGeometryFieldKindV2.SpatialCellKey),
            Field(4, "sample_spacing_mm", SpatialTerrainGeometryFieldKindV2.UInt32),
            Field(5, "sdf_mm", SpatialTerrainGeometryFieldKindV2.FixedInt32List729),
            Field(6, "surface_material_id", SpatialTerrainGeometryFieldKindV2.FixedUInt16List512),
        });

    public static void ValidateCanonicalContract()
    {
        var current = StandardDomainPartitionRegistry.Get(PartitionId);
        if (current.RecordSchema.SchemaId != RecordSchema.SchemaId ||
            current.RecordSchema.Version != new SchemaVersionV1(1, 0) ||
            RecordSchema.Version != new SchemaVersionV1(2, 0))
            throw new InvalidDataException("spatial.terrain-v2.schema-version-contract");

        if (!RecordKinds.SequenceEqual(RecordKinds.OrderBy(static value => value, StringComparer.Ordinal)))
            throw new InvalidDataException("spatial.terrain-v2.record-kind-order");
        if (RecordKinds.Distinct(StringComparer.Ordinal).Count() != 2)
            throw new InvalidDataException("spatial.terrain-v2.record-kind-count");

        ValidateFields(TerrainRootFields, 7, TerrainRootKind);
        ValidateFields(TerrainBrickFields, 6, TerrainBrickKind);

        if (TerrainBrickV1.SdfSampleCount != 729 || TerrainBrickV1.SurfaceMaterialCount != 512)
            throw new InvalidDataException("spatial.terrain-v2.brick-cardinality-drift");
    }

    private static SpatialTerrainGeometryFieldDescriptorV2 Field(
        ushort ordinal,
        string name,
        SpatialTerrainGeometryFieldKindV2 kind,
        bool optional = false)
        => new(ordinal, name, kind, optional);

    private static void ValidateFields(
        IReadOnlyList<SpatialTerrainGeometryFieldDescriptorV2> fields,
        int expectedCount,
        string kind)
    {
        if (fields.Count != expectedCount)
            throw new InvalidDataException($"spatial.terrain-v2.field-count:{kind}");
        for (var index = 0; index < fields.Count; index++)
        {
            if (fields[index].Ordinal != index + 1)
                throw new InvalidDataException($"spatial.terrain-v2.field-order:{kind}");
            if (string.IsNullOrWhiteSpace(fields[index].Name) || !Enum.IsDefined(fields[index].Kind))
                throw new InvalidDataException($"spatial.terrain-v2.field-invalid:{kind}");
        }
        if (fields.Select(static field => field.Name).Distinct(StringComparer.Ordinal).Count() != fields.Count)
            throw new InvalidDataException($"spatial.terrain-v2.field-duplicate:{kind}");
        if (fields[0].Name != "record_kind" || fields[0].Kind != SpatialTerrainGeometryFieldKindV2.Token)
            throw new InvalidDataException($"spatial.terrain-v2.record-kind-discriminator:{kind}");
    }
}

public abstract class SpatialTerrainGeometryPayloadV2
{
    public abstract string RecordKind { get; }
}

public sealed class SpatialTerrainRootPayloadV2 : SpatialTerrainGeometryPayloadV2
{
    public SpatialTerrainRootPayloadV2(
        PartitionRecordRefV1 scopeRef,
        PartitionRecordRefV1 rootBrickRef,
        ulong geometryRevision,
        IEnumerable<StableToken> surfaceClasses,
        IEnumerable<PartitionRecordRefV1> connectivityRefs,
        byte[]? archiveAnchor)
    {
        if (rootBrickRef.PartitionId.Value != SpatialTerrainGeometryRecordSchemaV2.PartitionId)
            throw new InvalidDataException("spatial.terrain-v2.root-brick-owner");
        if (geometryRevision == 0)
            throw new ArgumentOutOfRangeException(nameof(geometryRevision));
        ArgumentNullException.ThrowIfNull(surfaceClasses);
        ArgumentNullException.ThrowIfNull(connectivityRefs);
        if (archiveAnchor is not null && archiveAnchor.Length != 32)
            throw new ArgumentException("archive_anchor must be a 32-byte digest.", nameof(archiveAnchor));

        ScopeRef = scopeRef;
        RootBrickRef = rootBrickRef;
        GeometryRevision = geometryRevision;
        SurfaceClasses = Array.AsReadOnly(surfaceClasses.ToArray());
        ConnectivityRefs = Array.AsReadOnly(connectivityRefs.ToArray());
        ArchiveAnchor = archiveAnchor?.ToArray();
    }

    public override string RecordKind => SpatialTerrainGeometryRecordSchemaV2.TerrainRootKind;
    public PartitionRecordRefV1 ScopeRef { get; }
    public PartitionRecordRefV1 RootBrickRef { get; }
    public ulong GeometryRevision { get; }
    public IReadOnlyList<StableToken> SurfaceClasses { get; }
    public IReadOnlyList<PartitionRecordRefV1> ConnectivityRefs { get; }
    public byte[]? ArchiveAnchor { get; }
}

public sealed class SpatialTerrainBrickPayloadV2 : SpatialTerrainGeometryPayloadV2
{
    public SpatialTerrainBrickPayloadV2(
        byte level,
        SpatialCellKeyV1 cellOrigin,
        uint sampleSpacingMm,
        IEnumerable<int> sdfMm,
        IEnumerable<ushort> surfaceMaterialIds)
    {
        if (sampleSpacingMm == 0) throw new ArgumentOutOfRangeException(nameof(sampleSpacingMm));
        ArgumentNullException.ThrowIfNull(sdfMm);
        ArgumentNullException.ThrowIfNull(surfaceMaterialIds);

        var samples = sdfMm.ToArray();
        var materials = surfaceMaterialIds.ToArray();
        if (samples.Length != TerrainBrickV1.SdfSampleCount)
            throw new ArgumentException($"terrain_brick requires exactly {TerrainBrickV1.SdfSampleCount} SDF samples.", nameof(sdfMm));
        if (materials.Length != TerrainBrickV1.SurfaceMaterialCount)
            throw new ArgumentException($"terrain_brick requires exactly {TerrainBrickV1.SurfaceMaterialCount} material cells.", nameof(surfaceMaterialIds));

        Level = level;
        CellOrigin = cellOrigin;
        SampleSpacingMm = sampleSpacingMm;
        SdfMm = Array.AsReadOnly(samples);
        SurfaceMaterialIds = Array.AsReadOnly(materials);
    }

    public override string RecordKind => SpatialTerrainGeometryRecordSchemaV2.TerrainBrickKind;
    public byte Level { get; }
    public SpatialCellKeyV1 CellOrigin { get; }
    public uint SampleSpacingMm { get; }
    public IReadOnlyList<int> SdfMm { get; }
    public IReadOnlyList<ushort> SurfaceMaterialIds { get; }
}

/// <summary>
/// v2 terrain record material preserving the common DomainRecordEnvelope fields while allowing
/// terrain_root and terrain_brick payload arms under one record schema.
/// </summary>
public sealed class SpatialTerrainGeometryRecordMaterialV2
{
    public SpatialTerrainGeometryRecordMaterialV2(
        OpaqueId128 recordId,
        ulong revision,
        ulong createdStep,
        ulong? retiredStep,
        DetailLevelV1 detailLevel,
        OpaqueId128? lineageRef,
        SpatialTerrainGeometryPayloadV2 payload)
    {
        if (recordId.IsZero) throw new ArgumentException("Record id ZERO is invalid.", nameof(recordId));
        if (revision == 0) throw new ArgumentOutOfRangeException(nameof(revision));
        if (retiredStep is { } retired && retired < createdStep)
            throw new ArgumentOutOfRangeException(nameof(retiredStep));
        if (lineageRef is { IsZero: true })
            throw new ArgumentException("Lineage id ZERO is invalid.", nameof(lineageRef));
        if (!Enum.IsDefined(detailLevel)) throw new ArgumentOutOfRangeException(nameof(detailLevel));
        ArgumentNullException.ThrowIfNull(payload);

        RecordId = recordId;
        Revision = revision;
        CreatedStep = createdStep;
        RetiredStep = retiredStep;
        DetailLevel = detailLevel;
        LineageRef = lineageRef;
        Payload = payload;
    }

    public OpaqueId128 RecordId { get; }
    public SchemaRefV1 RecordSchema => SpatialTerrainGeometryRecordSchemaV2.RecordSchema;
    public ulong Revision { get; }
    public ulong CreatedStep { get; }
    public ulong? RetiredStep { get; }
    public DetailLevelV1 DetailLevel { get; }
    public OpaqueId128? LineageRef { get; }
    public SpatialTerrainGeometryPayloadV2 Payload { get; }

    public static SpatialTerrainGeometryRecordMaterialV2 MigrateRoot(
        DomainRecordEnvelopeV1<SpatialTerrainGeometryPayloadV1> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var v1 = StandardDomainPartitionRegistry.Get(SpatialTerrainGeometryRecordSchemaV2.PartitionId).RecordSchema;
        if (source.RecordSchema != v1)
            throw new InvalidDataException("spatial.terrain-v2.migration-source-schema");

        var payload = source.Payload;
        return new SpatialTerrainGeometryRecordMaterialV2(
            source.RecordId,
            source.Revision,
            source.CreatedStep,
            source.RetiredStep,
            source.DetailLevel,
            source.LineageRef,
            new SpatialTerrainRootPayloadV2(
                payload.ScopeRef,
                payload.RootBrickRef,
                payload.GeometryRevision,
                payload.SurfaceClasses,
                payload.ConnectivityRefs,
                payload.ArchiveAnchor));
    }

    public static SpatialTerrainGeometryRecordMaterialV2 FromTerrainBrick(
        TerrainBrickV1 brick,
        ulong createdStep,
        DetailLevelV1 detailLevel,
        OpaqueId128? lineageRef = null,
        ulong? retiredStep = null)
    {
        ArgumentNullException.ThrowIfNull(brick);
        return new SpatialTerrainGeometryRecordMaterialV2(
            brick.BrickId,
            brick.Revision,
            createdStep,
            retiredStep,
            detailLevel,
            lineageRef,
            new SpatialTerrainBrickPayloadV2(
                brick.Level,
                brick.CellOrigin,
                brick.SampleSpacingMm,
                brick.SdfMm,
                brick.SurfaceMaterialIds));
    }

    public TerrainBrickV1 ToTerrainBrick()
    {
        if (Payload is not SpatialTerrainBrickPayloadV2 brick)
            throw new InvalidOperationException("Only terrain_brick material can reconstruct TerrainBrickV1.");
        return new TerrainBrickV1(
            RecordId,
            brick.Level,
            brick.CellOrigin,
            brick.SampleSpacingMm,
            brick.SdfMm,
            brick.SurfaceMaterialIds,
            Revision);
    }
}
