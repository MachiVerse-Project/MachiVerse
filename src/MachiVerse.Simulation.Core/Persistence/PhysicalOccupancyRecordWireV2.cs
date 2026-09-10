using System.Text;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

/// <summary>
/// physical.occupancy record schema 2.0 の strict standalone protobuf wire。
/// payload field 1 は record_kind、shape のみ field 2 が shape_kind、
/// arm 固有 field は設計書の記載順に 10 から連番で割り当てる。
/// </summary>
public static class PhysicalOccupancyRecordWireCodecV2
{
    public static byte[] Encode(PhysicalOccupancyRecordMaterialV2 record)
    {
        ArgumentNullException.ThrowIfNull(record);
        PhysicalOccupancyRecordSchemaV2.ValidateCanonicalContract();
        return Proto.Encode(stream =>
        {
            Proto.WriteBytes(stream, 1, record.RecordId.ToBytes());
            Proto.WriteString(stream, 2, record.RecordSchema.SchemaId.Value);
            Proto.WriteMessage(stream, 3, EncodeSchemaVersion(record.RecordSchema.Version));
            Proto.WriteUInt64(stream, 4, record.Revision);
            if (record.CreatedStep != 0) Proto.WriteUInt64(stream, 5, record.CreatedStep);
            if (record.RetiredStep is { } retired) Proto.WriteUInt64(stream, 6, retired);
            if ((byte)record.DetailLevel != 0) Proto.WriteUInt32(stream, 7, (byte)record.DetailLevel);
            if (record.LineageRef is { } lineage) Proto.WriteBytes(stream, 8, lineage.ToBytes());
            Proto.WriteMessage(stream, 9, EncodePayload(record.Payload));
        });
    }

    public static PhysicalOccupancyRecordMaterialV2 Decode(ReadOnlySpan<byte> encoded)
    {
        var reader = new Proto.Reader(encoded);
        byte[]? recordIdBytes = null;
        string? schemaId = null;
        SchemaVersionV1? version = null;
        ulong revision = 0;
        ulong createdStep = 0;
        ulong? retiredStep = null;
        uint detail = 0;
        byte[]? lineageBytes = null;
        byte[]? payloadBytes = null;
        var previous = 0;

        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (field <= previous) throw WireError("record-field-order");
            previous = field;
            switch (field)
            {
                case 1: reader.RequireWire(wire, 2); recordIdBytes = reader.ReadBytes(); break;
                case 2: reader.RequireWire(wire, 2); schemaId = reader.ReadString(); break;
                case 3: reader.RequireWire(wire, 2); version = DecodeSchemaVersion(reader.ReadBytes()); break;
                case 4: reader.RequireWire(wire, 0); revision = reader.ReadVarUInt64(); break;
                case 5: reader.RequireWire(wire, 0); createdStep = reader.ReadVarUInt64(); break;
                case 6: reader.RequireWire(wire, 0); retiredStep = reader.ReadVarUInt64(); break;
                case 7: reader.RequireWire(wire, 0); detail = reader.ReadUInt32(); break;
                case 8: reader.RequireWire(wire, 2); lineageBytes = reader.ReadBytes(); break;
                case 9: reader.RequireWire(wire, 2); payloadBytes = reader.ReadBytes(); break;
                default: throw WireError("record-field-unknown");
            }
        }

        if (recordIdBytes is null || recordIdBytes.Length != 16 || schemaId is null || version is null ||
            revision == 0 || payloadBytes is null)
            throw WireError("record-required-field");
        if (detail > 3) throw WireError("record-detail-level");
        if (!string.Equals(schemaId, PhysicalOccupancyRecordSchemaV2.RecordSchema.SchemaId.Value, StringComparison.Ordinal) ||
            version.Value != PhysicalOccupancyRecordSchemaV2.RecordSchema.Version)
            throw WireError("record-schema");

        var recordId = OpaqueId128.FromBytes(recordIdBytes);
        if (recordId.IsZero) throw WireError("record-id-zero");
        if (retiredStep is { } retired && retired < createdStep) throw WireError("record-retired-before-created");

        OpaqueId128? lineage = null;
        if (lineageBytes is not null)
        {
            if (lineageBytes.Length != 16) throw WireError("record-lineage-length");
            lineage = OpaqueId128.FromBytes(lineageBytes);
            if (lineage.Value.IsZero) throw WireError("record-lineage-zero");
        }

        return new PhysicalOccupancyRecordMaterialV2(
            recordId,
            revision,
            createdStep,
            retiredStep,
            (DetailLevelV1)detail,
            lineage,
            DecodePayload(payloadBytes));
    }

    private static byte[] EncodePayload(PhysicalOccupancyRecordPayloadV2 payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return Proto.Encode(stream =>
        {
            Proto.WriteString(stream, 1, payload.RecordKind);
            switch (payload)
            {
                case PhysicalOccupancyStatePayloadV2 occupancy:
                    Proto.WriteMessage(stream, 10, EncodeRef(occupancy.PresenceRef));
                    Proto.WriteMessage(stream, 11, EncodeVec3(occupancy.AabbMin));
                    Proto.WriteMessage(stream, 12, EncodeVec3(occupancy.AabbMax));
                    Proto.WriteMessage(stream, 13, EncodeRefList(occupancy.ContactRefs));
                    Proto.WriteUInt32(stream, 14, occupancy.OccupancyFlags);
                    Proto.WriteUInt32(stream, 15, occupancy.CollisionLayer);
                    break;
                case PhysicalCollisionShapePayloadV2 shape:
                    Proto.WriteString(stream, 2, shape.ShapeKind);
                    EncodeShapeArm(stream, shape);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown physical occupancy v2 payload type: {payload.GetType().FullName}");
            }
        });
    }

    private static void EncodeShapeArm(Stream stream, PhysicalCollisionShapePayloadV2 shape)
    {
        switch (shape)
        {
            case PhysicalSphereShapePayloadV2 sphere:
                Proto.WriteMessage(stream, 10, EncodeVec3(sphere.CenterMm));
                Proto.WriteSInt64(stream, 11, sphere.RadiusMm);
                break;
            case PhysicalCapsuleShapePayloadV2 capsule:
                Proto.WriteMessage(stream, 10, EncodeVec3(capsule.SegmentStartMm));
                Proto.WriteMessage(stream, 11, EncodeVec3(capsule.SegmentEndMm));
                Proto.WriteSInt64(stream, 12, capsule.RadiusMm);
                break;
            case PhysicalOrientedBoxShapePayloadV2 box:
                Proto.WriteMessage(stream, 10, EncodeVec3(box.CenterMm));
                Proto.WriteMessage(stream, 11, EncodeVec3(box.HalfExtentsMm));
                Proto.WriteMessage(stream, 12, EncodeQuaternion(box.Orientation));
                break;
            case PhysicalConvexPolytopeShapePayloadV2 convex:
                Proto.WriteMessage(stream, 10, EncodeVertices(convex.VerticesMm));
                break;
            case PhysicalTriangleMeshStaticShapePayloadV2 mesh:
                Proto.WriteMessage(stream, 10, EncodeTriangles(mesh.Triangles));
                break;
            case PhysicalTerrainSdfRefShapePayloadV2 terrain:
                Proto.WriteMessage(stream, 10, EncodeRef(terrain.TerrainRootRef));
                break;
            default:
                throw new InvalidOperationException($"Unknown physical shape v2 payload: {shape.GetType().FullName}");
        }
    }

    private static PhysicalOccupancyRecordPayloadV2 DecodePayload(ReadOnlySpan<byte> encoded)
    {
        var reader = new Proto.Reader(encoded);
        if (reader.End) throw WireError("payload-empty");
        var (kindField, kindWire) = reader.ReadTag();
        if (kindField != 1) throw WireError("payload-record-kind-first");
        reader.RequireWire(kindWire, 2);
        var recordKind = new StableToken(reader.ReadString()).Value;
        return recordKind switch
        {
            PhysicalOccupancyRecordSchemaV2.OccupancyKind => DecodeOccupancy(ref reader),
            PhysicalOccupancyRecordSchemaV2.CollisionShapeKind => DecodeShape(ref reader),
            _ => throw WireError("payload-record-kind-unknown"),
        };
    }

    private static PhysicalOccupancyStatePayloadV2 DecodeOccupancy(ref Proto.Reader reader)
    {
        PartitionRecordRefV1? presence = null;
        Vec3Int64V1? min = null;
        Vec3Int64V1? max = null;
        IReadOnlyList<PartitionRecordRefV1>? contacts = null;
        uint? flags = null;
        uint? layer = null;
        var previous = 1;

        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (field <= previous) throw WireError("payload-occupancy-field-order");
            previous = field;
            switch (field)
            {
                case 10: reader.RequireWire(wire, 2); presence = DecodeRef(reader.ReadBytes()); break;
                case 11: reader.RequireWire(wire, 2); min = DecodeVec3(reader.ReadBytes()); break;
                case 12: reader.RequireWire(wire, 2); max = DecodeVec3(reader.ReadBytes()); break;
                case 13: reader.RequireWire(wire, 2); contacts = DecodeRefList(reader.ReadBytes()); break;
                case 14: reader.RequireWire(wire, 0); flags = reader.ReadUInt32(); break;
                case 15: reader.RequireWire(wire, 0); layer = reader.ReadUInt32(); break;
                default: throw WireError("payload-occupancy-field-unknown");
            }
        }
        if (presence is null || min is null || max is null || contacts is null || flags is null || layer is null)
            throw WireError("payload-occupancy-required-field");
        return new PhysicalOccupancyStatePayloadV2(presence.Value, min.Value, max.Value, contacts, flags.Value, layer.Value);
    }

    private static PhysicalCollisionShapePayloadV2 DecodeShape(ref Proto.Reader reader)
    {
        if (reader.End) throw WireError("payload-shape-kind-missing");
        var (kindField, kindWire) = reader.ReadTag();
        if (kindField != 2) throw WireError("payload-shape-kind-second");
        reader.RequireWire(kindWire, 2);
        var shapeKind = new StableToken(reader.ReadString()).Value;
        return shapeKind switch
        {
            PhysicalOccupancyRecordSchemaV2.SphereShapeKind => DecodeSphere(ref reader),
            PhysicalOccupancyRecordSchemaV2.CapsuleShapeKind => DecodeCapsule(ref reader),
            PhysicalOccupancyRecordSchemaV2.OrientedBoxShapeKind => DecodeOrientedBox(ref reader),
            PhysicalOccupancyRecordSchemaV2.ConvexPolytopeShapeKind => DecodeConvex(ref reader),
            PhysicalOccupancyRecordSchemaV2.TriangleMeshStaticShapeKind => DecodeMesh(ref reader),
            PhysicalOccupancyRecordSchemaV2.TerrainSdfRefShapeKind => DecodeTerrainSdfRef(ref reader),
            _ => throw WireError("payload-shape-kind-unknown"),
        };
    }

    private static PhysicalSphereShapePayloadV2 DecodeSphere(ref Proto.Reader reader)
    {
        Vec3MmV1? center = null;
        long? radius = null;
        ReadShapeFields(ref reader, (field, wire, bytes, number) =>
        {
            if (field == 10) { RequireLengthDelimited(wire); center = ToMm(DecodeVec3(bytes!)); }
            else if (field == 11) { RequireVarint(wire); radius = number!.Value; }
            else throw WireError("payload-sphere-field-unknown");
        });
        if (center is null || radius is null) throw WireError("payload-sphere-required-field");
        return new PhysicalSphereShapePayloadV2(center.Value, radius.Value);
    }

    private static PhysicalCapsuleShapePayloadV2 DecodeCapsule(ref Proto.Reader reader)
    {
        Vec3MmV1? start = null;
        Vec3MmV1? end = null;
        long? radius = null;
        ReadShapeFields(ref reader, (field, wire, bytes, number) =>
        {
            if (field == 10) { RequireLengthDelimited(wire); start = ToMm(DecodeVec3(bytes!)); }
            else if (field == 11) { RequireLengthDelimited(wire); end = ToMm(DecodeVec3(bytes!)); }
            else if (field == 12) { RequireVarint(wire); radius = number!.Value; }
            else throw WireError("payload-capsule-field-unknown");
        });
        if (start is null || end is null || radius is null) throw WireError("payload-capsule-required-field");
        return new PhysicalCapsuleShapePayloadV2(start.Value, end.Value, radius.Value);
    }

    private static PhysicalOrientedBoxShapePayloadV2 DecodeOrientedBox(ref Proto.Reader reader)
    {
        Vec3MmV1? center = null;
        Vec3MmV1? half = null;
        QuaternionQ30V1? orientation = null;
        ReadShapeFields(ref reader, (field, wire, bytes, number) =>
        {
            if (field == 10) { RequireLengthDelimited(wire); center = ToMm(DecodeVec3(bytes!)); }
            else if (field == 11) { RequireLengthDelimited(wire); half = ToMm(DecodeVec3(bytes!)); }
            else if (field == 12) { RequireLengthDelimited(wire); orientation = DecodeQuaternion(bytes!); }
            else throw WireError("payload-obb-field-unknown");
        });
        if (center is null || half is null || orientation is null) throw WireError("payload-obb-required-field");
        return new PhysicalOrientedBoxShapePayloadV2(center.Value, half.Value, orientation.Value);
    }

    private static PhysicalConvexPolytopeShapePayloadV2 DecodeConvex(ref Proto.Reader reader)
    {
        IReadOnlyList<Vec3MmV1>? vertices = null;
        ReadShapeFields(ref reader, (field, wire, bytes, number) =>
        {
            if (field != 10) throw WireError("payload-convex-field-unknown");
            RequireLengthDelimited(wire);
            vertices = DecodeVertices(bytes!);
        });
        if (vertices is null) throw WireError("payload-convex-required-field");
        return new PhysicalConvexPolytopeShapePayloadV2(vertices);
    }

    private static PhysicalTriangleMeshStaticShapePayloadV2 DecodeMesh(ref Proto.Reader reader)
    {
        IReadOnlyList<StaticTriangleV1>? triangles = null;
        ReadShapeFields(ref reader, (field, wire, bytes, number) =>
        {
            if (field != 10) throw WireError("payload-mesh-field-unknown");
            RequireLengthDelimited(wire);
            triangles = DecodeTriangles(bytes!);
        });
        if (triangles is null) throw WireError("payload-mesh-required-field");
        return new PhysicalTriangleMeshStaticShapePayloadV2(triangles);
    }

    private static PhysicalTerrainSdfRefShapePayloadV2 DecodeTerrainSdfRef(ref Proto.Reader reader)
    {
        PartitionRecordRefV1? terrain = null;
        ReadShapeFields(ref reader, (field, wire, bytes, number) =>
        {
            if (field != 10) throw WireError("payload-terrain-sdf-field-unknown");
            RequireLengthDelimited(wire);
            terrain = DecodeRef(bytes!);
        });
        if (terrain is null) throw WireError("payload-terrain-sdf-required-field");
        return new PhysicalTerrainSdfRefShapePayloadV2(terrain.Value);
    }

    private delegate void ShapeFieldReader(int field, int wire, byte[]? bytes, long? number);

    private static void ReadShapeFields(ref Proto.Reader reader, ShapeFieldReader consume)
    {
        var previous = 2;
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (field <= previous) throw WireError("payload-shape-field-order");
            previous = field;
            if (wire == 2) consume(field, wire, reader.ReadBytes(), null);
            else if (wire == 0) consume(field, wire, null, reader.ReadSInt64());
            else throw WireError("payload-shape-wire-type");
        }
    }

    private static void RequireLengthDelimited(int wire)
    {
        if (wire != 2) throw WireError("payload-shape-wire-type");
    }

    private static void RequireVarint(int wire)
    {
        if (wire != 0) throw WireError("payload-shape-wire-type");
    }

    private static byte[] EncodeSchemaVersion(SchemaVersionV1 version) => Proto.Encode(stream =>
    {
        Proto.WriteUInt32(stream, 1, version.Major);
        Proto.WriteUInt32(stream, 2, version.Minor);
    });

    private static SchemaVersionV1 DecodeSchemaVersion(ReadOnlySpan<byte> encoded)
    {
        var reader = new Proto.Reader(encoded);
        uint? major = null;
        uint? minor = null;
        var previous = 0;
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (field <= previous) throw WireError("schema-version-field-order");
            previous = field;
            reader.RequireWire(wire, 0);
            if (field == 1) major = reader.ReadUInt32();
            else if (field == 2) minor = reader.ReadUInt32();
            else throw WireError("schema-version-field-unknown");
        }
        if (major is null || minor is null || major > ushort.MaxValue || minor > ushort.MaxValue)
            throw WireError("schema-version-required-field");
        return new SchemaVersionV1((ushort)major.Value, (ushort)minor.Value);
    }

    private static byte[] EncodeRef(PartitionRecordRefV1 value) => Proto.Encode(stream =>
    {
        Proto.WriteString(stream, 1, value.PartitionId.Value);
        Proto.WriteBytes(stream, 2, value.RecordId.ToBytes());
    });

    private static PartitionRecordRefV1 DecodeRef(ReadOnlySpan<byte> encoded)
    {
        var reader = new Proto.Reader(encoded);
        string? partition = null;
        byte[]? idBytes = null;
        var previous = 0;
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (field <= previous) throw WireError("ref-field-order");
            previous = field;
            if (field == 1) { reader.RequireWire(wire, 2); partition = reader.ReadString(); }
            else if (field == 2) { reader.RequireWire(wire, 2); idBytes = reader.ReadBytes(); }
            else throw WireError("ref-field-unknown");
        }
        if (partition is null || idBytes is null || idBytes.Length != 16) throw WireError("ref-required-field");
        var id = OpaqueId128.FromBytes(idBytes);
        if (id.IsZero) throw WireError("ref-id-zero");
        return new PartitionRecordRefV1(partition, id);
    }

    private static byte[] EncodeRefList(IReadOnlyList<PartitionRecordRefV1> values) => Proto.Encode(stream =>
    {
        foreach (var value in values) Proto.WriteMessage(stream, 1, EncodeRef(value));
    });

    private static IReadOnlyList<PartitionRecordRefV1> DecodeRefList(ReadOnlySpan<byte> encoded)
    {
        var reader = new Proto.Reader(encoded);
        var result = new List<PartitionRecordRefV1>();
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (field != 1) throw WireError("ref-list-field-unknown");
            reader.RequireWire(wire, 2);
            result.Add(DecodeRef(reader.ReadBytes()));
        }
        return Array.AsReadOnly(result.ToArray());
    }

    private static byte[] EncodeVec3(Vec3Int64V1 value) => Proto.Encode(stream =>
    {
        Proto.WriteSInt64(stream, 1, value.X);
        Proto.WriteSInt64(stream, 2, value.Y);
        Proto.WriteSInt64(stream, 3, value.Z);
    });

    private static byte[] EncodeVec3(Vec3MmV1 value) => EncodeVec3(new Vec3Int64V1(value.X, value.Y, value.Z));

    private static Vec3Int64V1 DecodeVec3(ReadOnlySpan<byte> encoded)
    {
        var reader = new Proto.Reader(encoded);
        long? x = null;
        long? y = null;
        long? z = null;
        var previous = 0;
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (field <= previous) throw WireError("vec3-field-order");
            previous = field;
            reader.RequireWire(wire, 0);
            if (field == 1) x = reader.ReadSInt64();
            else if (field == 2) y = reader.ReadSInt64();
            else if (field == 3) z = reader.ReadSInt64();
            else throw WireError("vec3-field-unknown");
        }
        if (x is null || y is null || z is null) throw WireError("vec3-required-field");
        return new Vec3Int64V1(x.Value, y.Value, z.Value);
    }

    private static Vec3MmV1 ToMm(Vec3Int64V1 value) => new(value.X, value.Y, value.Z);

    private static byte[] EncodeQuaternion(QuaternionQ30V1 value) => Proto.Encode(stream =>
    {
        Proto.WriteSInt32(stream, 1, value.X);
        Proto.WriteSInt32(stream, 2, value.Y);
        Proto.WriteSInt32(stream, 3, value.Z);
        Proto.WriteSInt32(stream, 4, value.W);
    });

    private static QuaternionQ30V1 DecodeQuaternion(ReadOnlySpan<byte> encoded)
    {
        var reader = new Proto.Reader(encoded);
        int? x = null;
        int? y = null;
        int? z = null;
        int? w = null;
        var previous = 0;
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (field <= previous) throw WireError("quat-field-order");
            previous = field;
            reader.RequireWire(wire, 0);
            if (field == 1) x = reader.ReadSInt32();
            else if (field == 2) y = reader.ReadSInt32();
            else if (field == 3) z = reader.ReadSInt32();
            else if (field == 4) w = reader.ReadSInt32();
            else throw WireError("quat-field-unknown");
        }
        if (x is null || y is null || z is null || w is null) throw WireError("quat-required-field");
        var result = new QuaternionQ30V1(x.Value, y.Value, z.Value, w.Value);
        result.ValidateCanonical();
        return result;
    }

    private static byte[] EncodeVertices(IReadOnlyList<Vec3MmV1> vertices) => Proto.Encode(stream =>
    {
        foreach (var vertex in vertices) Proto.WriteMessage(stream, 1, EncodeVec3(vertex));
    });

    private static IReadOnlyList<Vec3MmV1> DecodeVertices(ReadOnlySpan<byte> encoded)
    {
        var reader = new Proto.Reader(encoded);
        var result = new List<Vec3MmV1>();
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (field != 1) throw WireError("vertex-list-field-unknown");
            reader.RequireWire(wire, 2);
            result.Add(ToMm(DecodeVec3(reader.ReadBytes())));
        }
        return Array.AsReadOnly(result.ToArray());
    }

    private static byte[] EncodeTriangles(IReadOnlyList<StaticTriangleV1> triangles) => Proto.Encode(stream =>
    {
        foreach (var triangle in triangles) Proto.WriteMessage(stream, 1, EncodeTriangle(triangle));
    });

    private static IReadOnlyList<StaticTriangleV1> DecodeTriangles(ReadOnlySpan<byte> encoded)
    {
        var reader = new Proto.Reader(encoded);
        var result = new List<StaticTriangleV1>();
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (field != 1) throw WireError("triangle-list-field-unknown");
            reader.RequireWire(wire, 2);
            result.Add(DecodeTriangle(reader.ReadBytes()));
        }
        return Array.AsReadOnly(result.ToArray());
    }

    private static byte[] EncodeTriangle(StaticTriangleV1 triangle) => Proto.Encode(stream =>
    {
        Proto.WriteUInt32(stream, 1, triangle.CanonicalIndex);
        Proto.WriteMessage(stream, 2, EncodeVec3(triangle.A));
        Proto.WriteMessage(stream, 3, EncodeVec3(triangle.B));
        Proto.WriteMessage(stream, 4, EncodeVec3(triangle.C));
    });

    private static StaticTriangleV1 DecodeTriangle(ReadOnlySpan<byte> encoded)
    {
        var reader = new Proto.Reader(encoded);
        uint? index = null;
        Vec3MmV1? a = null;
        Vec3MmV1? b = null;
        Vec3MmV1? c = null;
        var previous = 0;
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (field <= previous) throw WireError("triangle-field-order");
            previous = field;
            if (field == 1) { reader.RequireWire(wire, 0); index = reader.ReadUInt32(); }
            else if (field == 2) { reader.RequireWire(wire, 2); a = ToMm(DecodeVec3(reader.ReadBytes())); }
            else if (field == 3) { reader.RequireWire(wire, 2); b = ToMm(DecodeVec3(reader.ReadBytes())); }
            else if (field == 4) { reader.RequireWire(wire, 2); c = ToMm(DecodeVec3(reader.ReadBytes())); }
            else throw WireError("triangle-field-unknown");
        }
        if (index is null || a is null || b is null || c is null) throw WireError("triangle-required-field");
        return new StaticTriangleV1(index.Value, a.Value, b.Value, c.Value);
    }

    private static InvalidDataException WireError(string suffix) => new($"persistence.snapshot.physical-occupancy-v2:{suffix}");

    private static class Proto
    {
        public static byte[] Encode(Action<Stream> write)
        {
            using var stream = new MemoryStream();
            write(stream);
            return stream.ToArray();
        }

        public static void WriteMessage(Stream stream, int field, ReadOnlySpan<byte> value) => WriteBytes(stream, field, value);
        public static void WriteString(Stream stream, int field, string value) => WriteBytes(stream, field, Encoding.UTF8.GetBytes(value));

        public static void WriteBytes(Stream stream, int field, ReadOnlySpan<byte> value)
        {
            WriteTag(stream, field, 2);
            WriteVarUInt64(stream, checked((ulong)value.Length));
            stream.Write(value);
        }

        public static void WriteUInt32(Stream stream, int field, uint value)
        {
            WriteTag(stream, field, 0);
            WriteVarUInt64(stream, value);
        }

        public static void WriteUInt64(Stream stream, int field, ulong value)
        {
            WriteTag(stream, field, 0);
            WriteVarUInt64(stream, value);
        }

        public static void WriteSInt32(Stream stream, int field, int value)
        {
            WriteTag(stream, field, 0);
            WriteVarUInt64(stream, ZigZag32(value));
        }

        public static void WriteSInt64(Stream stream, int field, long value)
        {
            WriteTag(stream, field, 0);
            WriteVarUInt64(stream, ZigZag64(value));
        }

        private static void WriteTag(Stream stream, int field, int wire)
            => WriteVarUInt64(stream, ((ulong)field << 3) | (uint)wire);

        private static void WriteVarUInt64(Stream stream, ulong value)
        {
            while (value >= 0x80)
            {
                stream.WriteByte((byte)((value & 0x7f) | 0x80));
                value >>= 7;
            }
            stream.WriteByte((byte)value);
        }

        private static uint ZigZag32(int value) => unchecked((uint)((value << 1) ^ (value >> 31)));
        private static ulong ZigZag64(long value) => unchecked((ulong)((value << 1) ^ (value >> 63)));

        public ref struct Reader
        {
            private ReadOnlySpan<byte> _remaining;

            public Reader(ReadOnlySpan<byte> encoded) => _remaining = encoded;
            public bool End => _remaining.IsEmpty;

            public (int Field, int Wire) ReadTag()
            {
                var tag = ReadVarUInt64();
                if (tag == 0) throw WireError("tag-zero");
                return (checked((int)(tag >> 3)), checked((int)(tag & 7)));
            }

            public void RequireWire(int actual, int expected)
            {
                if (actual != expected) throw WireError("wire-type");
            }

            public ulong ReadVarUInt64()
            {
                ulong value = 0;
                var shift = 0;
                for (var index = 0; index < 10; index++)
                {
                    if (_remaining.IsEmpty) throw WireError("varint-truncated");
                    var current = _remaining[0];
                    _remaining = _remaining[1..];
                    if (index == 9 && current > 1) throw WireError("varint-overflow");
                    value |= (ulong)(current & 0x7f) << shift;
                    if ((current & 0x80) == 0) return value;
                    shift += 7;
                }
                throw WireError("varint-overflow");
            }

            public uint ReadUInt32()
            {
                var value = ReadVarUInt64();
                if (value > uint.MaxValue) throw WireError("uint32-range");
                return (uint)value;
            }

            public int ReadSInt32()
            {
                var value = ReadUInt32();
                return unchecked((int)((value >> 1) ^ (uint)-(int)(value & 1)));
            }

            public long ReadSInt64()
            {
                var value = ReadVarUInt64();
                return unchecked((long)((value >> 1) ^ (ulong)-(long)(value & 1)));
            }

            public byte[] ReadBytes()
            {
                var length = ReadVarUInt64();
                if (length > int.MaxValue || (ulong)_remaining.Length < length) throw WireError("bytes-length");
                var count = checked((int)length);
                var value = _remaining[..count].ToArray();
                _remaining = _remaining[count..];
                return value;
            }

            public string ReadString() => Encoding.UTF8.GetString(ReadBytes());
        }
    }
}
