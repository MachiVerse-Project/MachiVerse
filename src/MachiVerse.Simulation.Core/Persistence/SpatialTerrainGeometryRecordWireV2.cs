using System.Text;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

/// <summary>
/// Strict standalone protobuf wire for the decided spatial.terrain_geometry record schema v2.
/// It is intentionally not wired into StandardDomainPartitionRegistry or the production v1
/// DomainPartitionSnapshotWireCodecV1 yet; doing so requires the partition-wide v1->v2 migration.
/// </summary>
public static class SpatialTerrainGeometryRecordWireCodecV2
{
    public static byte[] Encode(SpatialTerrainGeometryRecordMaterialV2 record)
    {
        ArgumentNullException.ThrowIfNull(record);
        SpatialTerrainGeometryRecordSchemaV2.ValidateCanonicalContract();

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

    public static SpatialTerrainGeometryRecordMaterialV2 Decode(ReadOnlySpan<byte> encoded)
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
        var seen = new HashSet<int>();
        var previous = 0;

        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (field <= previous) throw WireError("record-field-order");
            previous = field;
            if (!seen.Add(field)) throw WireError("record-field-duplicate");
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
        if (!string.Equals(schemaId, SpatialTerrainGeometryRecordSchemaV2.RecordSchema.SchemaId.Value, StringComparison.Ordinal) ||
            version.Value != SpatialTerrainGeometryRecordSchemaV2.RecordSchema.Version)
            throw WireError("record-schema");

        var recordId = OpaqueId128.FromBytes(recordIdBytes);
        if (recordId.IsZero) throw WireError("record-id-zero");
        if (retiredStep is { } retired && retired < createdStep) throw WireError("record-retired-before-created");

        OpaqueId128? lineage = null;
        if (lineageBytes is not null)
        {
            if (lineageBytes.Length != 16) throw WireError("record-lineage-length");
            var parsed = OpaqueId128.FromBytes(lineageBytes);
            if (parsed.IsZero) throw WireError("record-lineage-zero");
            lineage = parsed;
        }

        return new SpatialTerrainGeometryRecordMaterialV2(
            recordId,
            revision,
            createdStep,
            retiredStep,
            (DetailLevelV1)detail,
            lineage,
            DecodePayload(payloadBytes));
    }

    private static byte[] EncodePayload(SpatialTerrainGeometryPayloadV2 payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return Proto.Encode(stream =>
        {
            Proto.WriteString(stream, 1, payload.RecordKind);
            switch (payload)
            {
                case SpatialTerrainRootPayloadV2 root:
                    Proto.WriteMessage(stream, 2, EncodeRef(root.ScopeRef));
                    Proto.WriteMessage(stream, 3, EncodeRef(root.RootBrickRef));
                    Proto.WriteUInt64(stream, 4, root.GeometryRevision);
                    Proto.WriteMessage(stream, 5, EncodeTokenList(root.SurfaceClasses));
                    Proto.WriteMessage(stream, 6, EncodeRefList(root.ConnectivityRefs));
                    if (root.ArchiveAnchor is { } archive) Proto.WriteBytes(stream, 7, archive);
                    break;
                case SpatialTerrainBrickPayloadV2 brick:
                    Proto.WriteUInt32(stream, 2, brick.Level);
                    Proto.WriteMessage(stream, 3, EncodeCellKey(brick.CellOrigin));
                    Proto.WriteUInt32(stream, 4, brick.SampleSpacingMm);
                    Proto.WriteMessage(stream, 5, EncodeInt32List(brick.SdfMm));
                    Proto.WriteMessage(stream, 6, EncodeUInt16List(brick.SurfaceMaterialIds));
                    break;
                default:
                    throw new InvalidOperationException($"Unknown terrain v2 payload type: {payload.GetType().FullName}");
            }
        });
    }

    private static SpatialTerrainGeometryPayloadV2 DecodePayload(ReadOnlySpan<byte> encoded)
    {
        var reader = new Proto.Reader(encoded);
        if (reader.End) throw WireError("payload-empty");
        var (kindField, kindWire) = reader.ReadTag();
        if (kindField != 1) throw WireError("payload-record-kind-first");
        reader.RequireWire(kindWire, 2);
        var kind = new StableToken(reader.ReadString()).Value;
        return kind switch
        {
            SpatialTerrainGeometryRecordSchemaV2.TerrainRootKind => DecodeRootPayload(ref reader),
            SpatialTerrainGeometryRecordSchemaV2.TerrainBrickKind => DecodeBrickPayload(ref reader),
            _ => throw WireError("payload-record-kind-unknown"),
        };
    }

    private static SpatialTerrainRootPayloadV2 DecodeRootPayload(ref Proto.Reader reader)
    {
        PartitionRecordRefV1? scope = null;
        PartitionRecordRefV1? rootBrick = null;
        ulong geometryRevision = 0;
        IReadOnlyList<StableToken>? surfaceClasses = null;
        IReadOnlyList<PartitionRecordRefV1>? connectivity = null;
        byte[]? archive = null;
        var previous = 1;

        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (field <= previous) throw WireError("payload-root-field-order");
            previous = field;
            switch (field)
            {
                case 2: reader.RequireWire(wire, 2); scope = DecodeRef(reader.ReadBytes()); break;
                case 3: reader.RequireWire(wire, 2); rootBrick = DecodeRef(reader.ReadBytes()); break;
                case 4: reader.RequireWire(wire, 0); geometryRevision = reader.ReadVarUInt64(); break;
                case 5: reader.RequireWire(wire, 2); surfaceClasses = DecodeTokenList(reader.ReadBytes()); break;
                case 6: reader.RequireWire(wire, 2); connectivity = DecodeRefList(reader.ReadBytes()); break;
                case 7:
                    reader.RequireWire(wire, 2);
                    archive = reader.ReadBytes();
                    if (archive.Length != 32) throw WireError("payload-root-archive-length");
                    break;
                default: throw WireError("payload-root-field-unknown");
            }
        }

        if (scope is null || rootBrick is null || geometryRevision == 0 || surfaceClasses is null || connectivity is null)
            throw WireError("payload-root-required-field");
        return new SpatialTerrainRootPayloadV2(
            scope.Value,
            rootBrick.Value,
            geometryRevision,
            surfaceClasses,
            connectivity,
            archive);
    }

    private static SpatialTerrainBrickPayloadV2 DecodeBrickPayload(ref Proto.Reader reader)
    {
        byte? level = null;
        SpatialCellKeyV1? cellOrigin = null;
        uint sampleSpacing = 0;
        IReadOnlyList<int>? sdf = null;
        IReadOnlyList<ushort>? materials = null;
        var previous = 1;

        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (field <= previous) throw WireError("payload-brick-field-order");
            previous = field;
            switch (field)
            {
                case 2:
                {
                    reader.RequireWire(wire, 0);
                    var value = reader.ReadUInt32();
                    if (value > byte.MaxValue) throw WireError("payload-brick-level-range");
                    level = (byte)value;
                    break;
                }
                case 3: reader.RequireWire(wire, 2); cellOrigin = DecodeCellKey(reader.ReadBytes()); break;
                case 4: reader.RequireWire(wire, 0); sampleSpacing = reader.ReadUInt32(); break;
                case 5: reader.RequireWire(wire, 2); sdf = DecodeInt32List(reader.ReadBytes()); break;
                case 6: reader.RequireWire(wire, 2); materials = DecodeUInt16List(reader.ReadBytes()); break;
                default: throw WireError("payload-brick-field-unknown");
            }
        }

        if (level is null || cellOrigin is null || sampleSpacing == 0 || sdf is null || materials is null)
            throw WireError("payload-brick-required-field");
        return new SpatialTerrainBrickPayloadV2(level.Value, cellOrigin.Value, sampleSpacing, sdf, materials);
    }

    private static byte[] EncodeSchemaVersion(SchemaVersionV1 version)
        => Proto.Encode(stream =>
        {
            Proto.WriteUInt32(stream, 1, version.Major);
            if (version.Minor != 0) Proto.WriteUInt32(stream, 2, version.Minor);
        });

    private static SchemaVersionV1 DecodeSchemaVersion(ReadOnlySpan<byte> encoded)
    {
        var reader = new Proto.Reader(encoded);
        uint major = 0;
        uint minor = 0;
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
        if (major > ushort.MaxValue || minor > ushort.MaxValue) throw WireError("schema-version-range");
        return new SchemaVersionV1((ushort)major, (ushort)minor);
    }

    private static byte[] EncodeRef(PartitionRecordRefV1 value)
        => Proto.Encode(stream =>
        {
            Proto.WriteString(stream, 1, value.PartitionId.Value);
            Proto.WriteBytes(stream, 2, value.RecordId.ToBytes());
        });

    private static PartitionRecordRefV1 DecodeRef(ReadOnlySpan<byte> encoded)
    {
        var reader = new Proto.Reader(encoded);
        string? partitionId = null;
        byte[]? idBytes = null;
        var previous = 0;
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (field <= previous) throw WireError("ref-field-order");
            previous = field;
            if (field == 1) { reader.RequireWire(wire, 2); partitionId = reader.ReadString(); }
            else if (field == 2) { reader.RequireWire(wire, 2); idBytes = reader.ReadBytes(); }
            else throw WireError("ref-field-unknown");
        }
        if (partitionId is null || idBytes is null || idBytes.Length != 16) throw WireError("ref-required-field");
        var id = OpaqueId128.FromBytes(idBytes);
        if (id.IsZero) throw WireError("ref-id-zero");
        return new PartitionRecordRefV1(new StableToken(partitionId), id);
    }

    private static byte[] EncodeRefList(IReadOnlyList<PartitionRecordRefV1> values)
        => Proto.Encode(stream =>
        {
            foreach (var value in values) Proto.WriteMessage(stream, 1, EncodeRef(value));
        });

    private static IReadOnlyList<PartitionRecordRefV1> DecodeRefList(ReadOnlySpan<byte> encoded)
    {
        var reader = new Proto.Reader(encoded);
        var values = new List<PartitionRecordRefV1>();
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (field != 1) throw WireError("ref-list-field-unknown");
            reader.RequireWire(wire, 2);
            values.Add(DecodeRef(reader.ReadBytes()));
        }
        return Array.AsReadOnly(values.ToArray());
    }

    private static byte[] EncodeTokenList(IReadOnlyList<StableToken> values)
        => Proto.Encode(stream =>
        {
            foreach (var value in values) Proto.WriteString(stream, 1, value.Value);
        });

    private static IReadOnlyList<StableToken> DecodeTokenList(ReadOnlySpan<byte> encoded)
    {
        var reader = new Proto.Reader(encoded);
        var values = new List<StableToken>();
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (field != 1) throw WireError("token-list-field-unknown");
            reader.RequireWire(wire, 2);
            values.Add(new StableToken(reader.ReadString()));
        }
        return Array.AsReadOnly(values.ToArray());
    }

    private static byte[] EncodeCellKey(SpatialCellKeyV1 value)
        => Proto.Encode(stream =>
        {
            Proto.WriteUInt32(stream, 1, value.Level);
            Proto.WriteSInt32(stream, 2, value.X);
            Proto.WriteSInt32(stream, 3, value.Y);
            Proto.WriteSInt32(stream, 4, value.Z);
        });

    private static SpatialCellKeyV1 DecodeCellKey(ReadOnlySpan<byte> encoded)
    {
        var reader = new Proto.Reader(encoded);
        byte? level = null;
        int? x = null;
        int? y = null;
        int? z = null;
        var previous = 0;
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (field <= previous) throw WireError("cell-key-field-order");
            previous = field;
            reader.RequireWire(wire, 0);
            switch (field)
            {
                case 1:
                {
                    var value = reader.ReadUInt32();
                    if (value > byte.MaxValue) throw WireError("cell-key-level-range");
                    level = (byte)value;
                    break;
                }
                case 2: x = reader.ReadSInt32(); break;
                case 3: y = reader.ReadSInt32(); break;
                case 4: z = reader.ReadSInt32(); break;
                default: throw WireError("cell-key-field-unknown");
            }
        }
        if (level is null || x is null || y is null || z is null) throw WireError("cell-key-required-field");
        return new SpatialCellKeyV1(level.Value, x.Value, y.Value, z.Value);
    }

    private static byte[] EncodeInt32List(IReadOnlyList<int> values)
        => Proto.Encode(stream =>
        {
            if (values.Count != TerrainBrickV1.SdfSampleCount) throw WireError("sdf-count");
            foreach (var value in values) Proto.WriteSInt32(stream, 1, value);
        });

    private static IReadOnlyList<int> DecodeInt32List(ReadOnlySpan<byte> encoded)
    {
        var reader = new Proto.Reader(encoded);
        var values = new List<int>(TerrainBrickV1.SdfSampleCount);
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (field != 1) throw WireError("sdf-field-unknown");
            reader.RequireWire(wire, 0);
            values.Add(reader.ReadSInt32());
        }
        if (values.Count != TerrainBrickV1.SdfSampleCount) throw WireError("sdf-count");
        return Array.AsReadOnly(values.ToArray());
    }

    private static byte[] EncodeUInt16List(IReadOnlyList<ushort> values)
        => Proto.Encode(stream =>
        {
            if (values.Count != TerrainBrickV1.SurfaceMaterialCount) throw WireError("material-count");
            foreach (var value in values) Proto.WriteUInt32(stream, 1, value);
        });

    private static IReadOnlyList<ushort> DecodeUInt16List(ReadOnlySpan<byte> encoded)
    {
        var reader = new Proto.Reader(encoded);
        var values = new List<ushort>(TerrainBrickV1.SurfaceMaterialCount);
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (field != 1) throw WireError("material-field-unknown");
            reader.RequireWire(wire, 0);
            var value = reader.ReadUInt32();
            if (value > ushort.MaxValue) throw WireError("material-range");
            values.Add((ushort)value);
        }
        if (values.Count != TerrainBrickV1.SurfaceMaterialCount) throw WireError("material-count");
        return Array.AsReadOnly(values.ToArray());
    }

    private static InvalidDataException WireError(string suffix)
        => new($"persistence.snapshot.terrain-v2:{suffix}");

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
