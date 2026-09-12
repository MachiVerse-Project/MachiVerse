using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

/// <summary>
/// physical.occupancy record schema 2.0 の semantic payload digest。
/// protobuf byte列ではなく schema/version と意味値を MV-DCBOR へ正規化して hash する。
/// </summary>
public static class PhysicalOccupancyPayloadCanonicalDigestV2
{
    public static byte[] Compute(
        PhysicalOccupancyRecordPayloadV2 payload,
        IDomainRecordSchemaResolverV1? references = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        PhysicalOccupancyRecordSchemaV2.ValidateCanonicalContract();
        return HashSuite.DomainHash(StandardDomainPayloadCanonicalDigestV1.HashDomain, writer =>
        {
            writer.WriteMapStart(5);
            writer.WriteUnsigned(0); writer.WriteAsciiText(PhysicalOccupancyRecordSchemaV2.PartitionId);
            writer.WriteUnsigned(1); writer.WriteAsciiText(PhysicalOccupancyRecordSchemaV2.RecordSchema.SchemaId.Value);
            writer.WriteUnsigned(2); writer.WriteUnsigned(PhysicalOccupancyRecordSchemaV2.RecordSchema.Version.Major);
            writer.WriteUnsigned(3); writer.WriteUnsigned(PhysicalOccupancyRecordSchemaV2.RecordSchema.Version.Minor);
            writer.WriteUnsigned(4);
            WritePayload(writer, payload, references);
        });
    }

    private static void WritePayload(
        MvDcborWriter writer,
        PhysicalOccupancyRecordPayloadV2 payload,
        IDomainRecordSchemaResolverV1? references)
    {
        switch (payload)
        {
            case PhysicalOccupancyStatePayloadV2 occupancy:
                writer.WriteArrayStart(7);
                Field(writer, 1, w => w.WriteAsciiText(occupancy.RecordKind));
                Field(writer, 2, w => WriteReference(w, occupancy.PresenceRef, references, "presence_ref", requireSchema: StandardDomainPartitionRegistry.Get(PhysicalPresencePayloadV1.PartitionId).RecordSchema));
                Field(writer, 3, w => WriteVec3(w, occupancy.AabbMin.X, occupancy.AabbMin.Y, occupancy.AabbMin.Z));
                Field(writer, 4, w => WriteVec3(w, occupancy.AabbMax.X, occupancy.AabbMax.Y, occupancy.AabbMax.Z));
                Field(writer, 5, w => WriteReferenceList(w, occupancy.ContactRefs, references, "contact_refs"));
                Field(writer, 6, w => w.WriteUnsigned(occupancy.OccupancyFlags));
                Field(writer, 7, w => w.WriteUnsigned(occupancy.CollisionLayer));
                break;
            case PhysicalCollisionShapePayloadV2 shape:
                WriteShape(writer, shape, references);
                break;
            default:
                throw new InvalidDataException("domain.payload.digest-physical-occupancy-v2-kind");
        }
    }

    private static void WriteShape(
        MvDcborWriter writer,
        PhysicalCollisionShapePayloadV2 shape,
        IDomainRecordSchemaResolverV1? references)
    {
        switch (shape)
        {
            case PhysicalSphereShapePayloadV2 sphere:
                writer.WriteArrayStart(4);
                ShapeHead(writer, sphere);
                Field(writer, 3, w => WriteVec3(w, sphere.CenterMm.X, sphere.CenterMm.Y, sphere.CenterMm.Z));
                Field(writer, 4, w => w.WriteInt64(sphere.RadiusMm));
                break;
            case PhysicalCapsuleShapePayloadV2 capsule:
                writer.WriteArrayStart(5);
                ShapeHead(writer, capsule);
                Field(writer, 3, w => WriteVec3(w, capsule.SegmentStartMm.X, capsule.SegmentStartMm.Y, capsule.SegmentStartMm.Z));
                Field(writer, 4, w => WriteVec3(w, capsule.SegmentEndMm.X, capsule.SegmentEndMm.Y, capsule.SegmentEndMm.Z));
                Field(writer, 5, w => w.WriteInt64(capsule.RadiusMm));
                break;
            case PhysicalOrientedBoxShapePayloadV2 box:
                writer.WriteArrayStart(5);
                ShapeHead(writer, box);
                Field(writer, 3, w => WriteVec3(w, box.CenterMm.X, box.CenterMm.Y, box.CenterMm.Z));
                Field(writer, 4, w => WriteVec3(w, box.HalfExtentsMm.X, box.HalfExtentsMm.Y, box.HalfExtentsMm.Z));
                Field(writer, 5, w =>
                {
                    w.WriteArrayStart(4);
                    w.WriteInt64(box.Orientation.X);
                    w.WriteInt64(box.Orientation.Y);
                    w.WriteInt64(box.Orientation.Z);
                    w.WriteInt64(box.Orientation.W);
                });
                break;
            case PhysicalConvexPolytopeShapePayloadV2 convex:
                writer.WriteArrayStart(3);
                ShapeHead(writer, convex);
                Field(writer, 3, w =>
                {
                    w.WriteArrayStart(checked((ulong)convex.VerticesMm.Count));
                    foreach (var vertex in convex.VerticesMm) WriteVec3(w, vertex.X, vertex.Y, vertex.Z);
                });
                break;
            case PhysicalTriangleMeshStaticShapePayloadV2 mesh:
                writer.WriteArrayStart(3);
                ShapeHead(writer, mesh);
                Field(writer, 3, w =>
                {
                    w.WriteArrayStart(checked((ulong)mesh.Triangles.Count));
                    foreach (var triangle in mesh.Triangles)
                    {
                        w.WriteArrayStart(4);
                        w.WriteUnsigned(triangle.CanonicalIndex);
                        WriteVec3(w, triangle.A.X, triangle.A.Y, triangle.A.Z);
                        WriteVec3(w, triangle.B.X, triangle.B.Y, triangle.B.Z);
                        WriteVec3(w, triangle.C.X, triangle.C.Y, triangle.C.Z);
                    }
                });
                break;
            case PhysicalTerrainSdfRefShapePayloadV2 terrain:
                writer.WriteArrayStart(3);
                ShapeHead(writer, terrain);
                Field(writer, 3, w => WriteReference(
                    w,
                    terrain.TerrainRootRef,
                    references,
                    "terrain_root_ref",
                    new SchemaRefV1("domain.spatial.terrain_geometry.record", 2, 0)));
                break;
            default:
                throw new InvalidDataException("domain.payload.digest-physical-shape-v2-kind");
        }
    }

    private static void ShapeHead(MvDcborWriter writer, PhysicalCollisionShapePayloadV2 shape)
    {
        Field(writer, 1, w => w.WriteAsciiText(shape.RecordKind));
        Field(writer, 2, w => w.WriteAsciiText(shape.ShapeKind));
    }

    private static void WriteReferenceList(
        MvDcborWriter writer,
        IReadOnlyList<PartitionRecordRefV1> values,
        IDomainRecordSchemaResolverV1? references,
        string field)
    {
        writer.WriteArrayStart(checked((ulong)values.Count));
        PartitionRecordRefV1? previous = null;
        foreach (var reference in values)
        {
            if (previous is { } prior && CompareReference(prior, reference) >= 0)
                throw new InvalidDataException($"domain.payload.digest-physical-v2-ref-order:{field}");
            previous = reference;
            WriteReference(writer, reference, references, field, null);
        }
    }

    private static void WriteReference(
        MvDcborWriter writer,
        PartitionRecordRefV1 reference,
        IDomainRecordSchemaResolverV1? references,
        string field,
        SchemaRefV1? requireSchema)
    {
        if (reference.RecordId.IsZero) throw Range(field);
        _ = StandardDomainPartitionRegistry.Get(reference.PartitionId.Value);
        if (references is not null)
        {
            if (!references.TryGetRecordSchema(reference, out var actual))
                throw new InvalidDataException($"domain.payload.digest-reference-missing:{PhysicalOccupancyRecordSchemaV2.PartitionId}:{field}");
            if (requireSchema is { } expected && actual != expected)
                throw new InvalidDataException($"domain.payload.digest-reference-schema:{PhysicalOccupancyRecordSchemaV2.PartitionId}:{field}");
        }
        writer.WriteArrayStart(2);
        writer.WriteAsciiText(reference.PartitionId.Value);
        writer.WriteBytes(reference.RecordId.ToBytes());
    }

    private static int CompareReference(PartitionRecordRefV1 left, PartitionRecordRefV1 right)
    {
        var partition = string.CompareOrdinal(left.PartitionId.Value, right.PartitionId.Value);
        return partition != 0 ? partition : left.RecordId.CompareTo(right.RecordId);
    }

    private static void WriteVec3(MvDcborWriter writer, long x, long y, long z)
    {
        writer.WriteArrayStart(3);
        writer.WriteInt64(x);
        writer.WriteInt64(y);
        writer.WriteInt64(z);
    }

    private static void Field(MvDcborWriter writer, ulong ordinal, Action<MvDcborWriter> value)
    {
        writer.WriteArrayStart(2);
        writer.WriteUnsigned(ordinal);
        value(writer);
    }

    private static InvalidDataException Range(string field)
        => new($"domain.payload.digest-physical-occupancy-v2-range:{field}");
}
