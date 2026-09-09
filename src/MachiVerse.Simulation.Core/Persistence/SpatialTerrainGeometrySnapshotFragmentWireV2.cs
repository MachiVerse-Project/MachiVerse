using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed record SpatialTerrainGeometrySnapshotFragmentDecodedV2(
    PartitionStateHeaderV1 Header,
    IReadOnlyList<SpatialTerrainGeometryRecordMaterialV2> Records);

/// <summary>
/// Terrain-v2 variant of the existing Domain fragment envelope: field 1 is the unchanged
/// PartitionStateHeaderV1 wire and repeated field 2 contains record-schema-2.0 records.
/// </summary>
public static class SpatialTerrainGeometrySnapshotFragmentWireV2
{
    public static byte[] Encode(
        PartitionStateHeaderV1 header,
        IReadOnlyList<SpatialTerrainGeometryRecordMaterialV2> records)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(records);
        RequireHeader(header);

        using var stream = new MemoryStream();
        WriteMessage(stream, 1, DomainPartitionSnapshotWireCodecV1.EncodeHeader(header));
        OpaqueId128? previous = null;
        foreach (var record in records)
        {
            ArgumentNullException.ThrowIfNull(record);
            if (record.RecordSchema != SpatialTerrainGeometryRecordSchemaV2.RecordSchema)
                throw WireError("record-schema");
            if (record.CreatedStep > header.BasisStep || record.RetiredStep is { } retired && retired > header.BasisStep)
                throw WireError("record-step-after-basis");
            if (previous is { } prior && prior.CompareTo(record.RecordId) >= 0)
                throw WireError("record-order");
            previous = record.RecordId;
            WriteMessage(stream, 2, SpatialTerrainGeometryRecordWireCodecV2.Encode(record));
        }
        return stream.ToArray();
    }

    public static SpatialTerrainGeometrySnapshotFragmentDecodedV2 Decode(ReadOnlySpan<byte> encoded)
    {
        if (encoded.IsEmpty) throw WireError("empty");
        var reader = new Reader(encoded);
        PartitionStateHeaderV1? header = null;
        var records = new List<SpatialTerrainGeometryRecordMaterialV2>();
        var headerSeen = false;
        var recordSeen = false;
        OpaqueId128? previous = null;

        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            switch (field)
            {
                case 1:
                    if (headerSeen || recordSeen) throw WireError("header-order");
                    headerSeen = true;
                    reader.RequireWire(wire, 2);
                    header = DomainPartitionSnapshotWireCodecV1.DecodeHeader(reader.ReadBytes());
                    RequireHeader(header);
                    break;
                case 2:
                {
                    if (!headerSeen || header is null) throw WireError("record-before-header");
                    recordSeen = true;
                    reader.RequireWire(wire, 2);
                    var record = SpatialTerrainGeometryRecordWireCodecV2.Decode(reader.ReadBytes());
                    if (record.CreatedStep > header.BasisStep || record.RetiredStep is { } retired && retired > header.BasisStep)
                        throw WireError("record-step-after-basis");
                    if (previous is { } prior && prior.CompareTo(record.RecordId) >= 0)
                        throw WireError("record-order");
                    previous = record.RecordId;
                    records.Add(record);
                    break;
                }
                default:
                    throw WireError("field-unknown");
            }
        }

        if (header is null) throw WireError("header-missing");
        return new SpatialTerrainGeometrySnapshotFragmentDecodedV2(
            header,
            Array.AsReadOnly(records.ToArray()));
    }

    private static void RequireHeader(PartitionStateHeaderV1 header)
    {
        var identity = StandardDomainPartitionRegistry.Get(SpatialTerrainGeometryRecordSchemaV2.PartitionId);
        if (header.PartitionId != identity.PartitionId ||
            header.OwnerDomain != identity.OwnerDomain ||
            header.Schema != identity.PartitionSchema)
            throw WireError("header-identity");
    }

    private static void WriteMessage(Stream stream, int field, ReadOnlySpan<byte> value)
    {
        WriteVarUInt64(stream, ((ulong)field << 3) | 2UL);
        WriteVarUInt64(stream, checked((ulong)value.Length));
        stream.Write(value);
    }

    private static void WriteVarUInt64(Stream stream, ulong value)
    {
        while (value >= 0x80)
        {
            stream.WriteByte((byte)((value & 0x7f) | 0x80));
            value >>= 7;
        }
        stream.WriteByte((byte)value);
    }

    private static InvalidDataException WireError(string suffix)
        => new($"persistence.snapshot.terrain-v2-fragment:{suffix}");

    private ref struct Reader
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
            for (var i = 0; i < 10; i++)
            {
                if (_remaining.IsEmpty) throw WireError("varint-truncated");
                var current = _remaining[0];
                _remaining = _remaining[1..];
                if (i == 9 && current > 1) throw WireError("varint-overflow");
                value |= (ulong)(current & 0x7f) << shift;
                if ((current & 0x80) == 0) return value;
                shift += 7;
            }
            throw WireError("varint-overflow");
        }

        public byte[] ReadBytes()
        {
            var length = ReadVarUInt64();
            if (length > int.MaxValue || (ulong)_remaining.Length < length) throw WireError("bytes-length");
            var count = checked((int)length);
            var bytes = _remaining[..count].ToArray();
            _remaining = _remaining[count..];
            return bytes;
        }
    }
}
