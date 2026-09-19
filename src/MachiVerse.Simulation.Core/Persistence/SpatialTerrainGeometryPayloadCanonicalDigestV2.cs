using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

/// <summary>
/// Semantic payload digest for spatial.terrain_geometry record schema 2.0.
/// It intentionally reuses the existing mv.domain-payload.v1 digest algorithm domain:
/// schema id + explicit major/minor are part of the hashed material, so v1/v2 payloads
/// cannot collide merely because the partition id is shared.
/// </summary>
public static class SpatialTerrainGeometryPayloadCanonicalDigestV2
{
    public static byte[] Compute(
        SpatialTerrainGeometryPayloadV2 payload,
        IDomainRecordReferenceResolverV1? references = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        SpatialTerrainGeometryRecordSchemaV2.ValidateCanonicalContract();

        return HashSuite.DomainHash(StandardDomainPayloadCanonicalDigestV1.HashDomain, writer =>
        {
            writer.WriteMapStart(5);
            writer.WriteUnsigned(0); writer.WriteAsciiText(SpatialTerrainGeometryRecordSchemaV2.PartitionId);
            writer.WriteUnsigned(1); writer.WriteAsciiText(SpatialTerrainGeometryRecordSchemaV2.RecordSchema.SchemaId.Value);
            writer.WriteUnsigned(2); writer.WriteUnsigned(SpatialTerrainGeometryRecordSchemaV2.RecordSchema.Version.Major);
            writer.WriteUnsigned(3); writer.WriteUnsigned(SpatialTerrainGeometryRecordSchemaV2.RecordSchema.Version.Minor);
            writer.WriteUnsigned(4);
            switch (payload)
            {
                case SpatialTerrainRootPayloadV2 root:
                    WriteRoot(writer, root, references);
                    break;
                case SpatialTerrainBrickPayloadV2 brick:
                    WriteBrick(writer, brick);
                    break;
                default:
                    throw new InvalidDataException("domain.payload.digest-terrain-v2-kind");
            }
        });
    }

    private static void WriteRoot(
        MvDcborWriter writer,
        SpatialTerrainRootPayloadV2 root,
        IDomainRecordReferenceResolverV1? references)
    {
        var fieldCount = root.ArchiveAnchor is null ? 6UL : 7UL;
        writer.WriteArrayStart(fieldCount);
        WriteField(writer, 1, static w => w.WriteAsciiText(SpatialTerrainGeometryRecordSchemaV2.TerrainRootKind));
        WriteField(writer, 2, w => WriteReference(w, root.ScopeRef, references, "scope_ref"));
        WriteField(writer, 3, w => WriteReference(w, root.RootBrickRef, references, "root_brick_ref"));
        if (root.GeometryRevision == 0) throw Range("geometry_revision");
        WriteField(writer, 4, w => w.WriteUnsigned(root.GeometryRevision));
        WriteField(writer, 5, w => WriteTokenList(w, root.SurfaceClasses, "surface_classes"));
        WriteField(writer, 6, w => WriteReferenceList(w, root.ConnectivityRefs, references, "connectivity_refs"));
        if (root.ArchiveAnchor is { } archive)
        {
            if (archive.Length != 32) throw Range("archive_anchor");
            WriteField(writer, 7, w => w.WriteBytes(archive));
        }
    }

    private static void WriteBrick(MvDcborWriter writer, SpatialTerrainBrickPayloadV2 brick)
    {
        if (brick.SampleSpacingMm == 0) throw Range("sample_spacing_mm");
        if (brick.SdfMm.Count != TerrainBrickV1.SdfSampleCount) throw Range("sdf_mm");
        if (brick.SurfaceMaterialIds.Count != TerrainBrickV1.SurfaceMaterialCount) throw Range("surface_material_id");

        writer.WriteArrayStart(6);
        WriteField(writer, 1, static w => w.WriteAsciiText(SpatialTerrainGeometryRecordSchemaV2.TerrainBrickKind));
        WriteField(writer, 2, w => w.WriteUnsigned(brick.Level));
        WriteField(writer, 3, w =>
        {
            w.WriteArrayStart(4);
            w.WriteUnsigned(brick.CellOrigin.Level);
            w.WriteInt64(brick.CellOrigin.X);
            w.WriteInt64(brick.CellOrigin.Y);
            w.WriteInt64(brick.CellOrigin.Z);
        });
        WriteField(writer, 4, w => w.WriteUnsigned(brick.SampleSpacingMm));
        WriteField(writer, 5, w =>
        {
            w.WriteArrayStart(TerrainBrickV1.SdfSampleCount);
            foreach (var value in brick.SdfMm) w.WriteInt64(value);
        });
        WriteField(writer, 6, w =>
        {
            w.WriteArrayStart(TerrainBrickV1.SurfaceMaterialCount);
            foreach (var value in brick.SurfaceMaterialIds) w.WriteUnsigned(value);
        });
    }

    private static void WriteTokenList(
        MvDcborWriter writer,
        IReadOnlyList<StableToken> values,
        string field)
    {
        writer.WriteArrayStart(checked((ulong)values.Count));
        string? previous = null;
        foreach (var value in values)
        {
            var token = value.Value;
            if (previous is not null && string.CompareOrdinal(previous, token) >= 0)
                throw new InvalidDataException($"domain.payload.digest-terrain-v2-token-order:{field}");
            previous = token;
            writer.WriteAsciiText(token);
        }
    }

    private static void WriteReferenceList(
        MvDcborWriter writer,
        IReadOnlyList<PartitionRecordRefV1> values,
        IDomainRecordReferenceResolverV1? references,
        string field)
    {
        writer.WriteArrayStart(checked((ulong)values.Count));
        PartitionRecordRefV1? previous = null;
        foreach (var reference in values)
        {
            if (previous is { } prior && CompareReference(prior, reference) >= 0)
                throw new InvalidDataException($"domain.payload.digest-terrain-v2-ref-order:{field}");
            previous = reference;
            WriteReference(writer, reference, references, field);
        }
    }

    private static void WriteReference(
        MvDcborWriter writer,
        PartitionRecordRefV1 reference,
        IDomainRecordReferenceResolverV1? references,
        string field)
    {
        if (reference.RecordId.IsZero) throw Range(field);
        _ = StandardDomainPartitionRegistry.Get(reference.PartitionId.Value);
        if (references is not null && !references.Exists(reference))
            throw new InvalidDataException($"domain.payload.digest-reference-missing:{SpatialTerrainGeometryRecordSchemaV2.PartitionId}:{field}");
        writer.WriteArrayStart(2);
        writer.WriteAsciiText(reference.PartitionId.Value);
        writer.WriteBytes(reference.RecordId.ToBytes());
    }

    private static int CompareReference(PartitionRecordRefV1 left, PartitionRecordRefV1 right)
    {
        var partition = string.CompareOrdinal(left.PartitionId.Value, right.PartitionId.Value);
        return partition != 0 ? partition : left.RecordId.CompareTo(right.RecordId);
    }

    private static void WriteField(MvDcborWriter writer, ulong ordinal, Action<MvDcborWriter> writeValue)
    {
        writer.WriteArrayStart(2);
        writer.WriteUnsigned(ordinal);
        writeValue(writer);
    }

    private static InvalidDataException Range(string field)
        => new($"domain.payload.digest-terrain-v2-range:{field}");
}
