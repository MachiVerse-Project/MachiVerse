using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed record InfrastructureNetworkTopologySnapshotFragmentDecodedV2(
    PartitionStateHeaderV1 Header,
    IReadOnlyList<InfrastructureNetworkTopologyRecordMaterialV2> Records);

/// <summary>infrastructure.network_topology v2 section fragment envelope.</summary>
public static class InfrastructureNetworkTopologySnapshotFragmentWireV2
{
    public static byte[] Encode(
        PartitionStateHeaderV1 header,
        IReadOnlyList<InfrastructureNetworkTopologyRecordMaterialV2> records)
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
            if (record.RecordSchema != InfrastructureNetworkTopologyRecordSchemaV2.RecordSchema)
                throw Error("record-schema");
            if (record.CreatedStep > header.BasisStep || record.RetiredStep is { } retired && retired > header.BasisStep)
                throw Error("record-step-after-basis");
            if (previous is { } prior && prior.CompareTo(record.RecordId) >= 0)
                throw Error("record-order");
            previous = record.RecordId;
            WriteMessage(stream, 2, InfrastructureNetworkTopologyRecordWireCodecV2.Encode(record));
        }
        return stream.ToArray();
    }

    public static InfrastructureNetworkTopologySnapshotFragmentDecodedV2 Decode(ReadOnlySpan<byte> encoded)
    {
        if (encoded.IsEmpty) throw Error("empty");
        var reader = new Reader(encoded);
        PartitionStateHeaderV1? header = null;
        var records = new List<InfrastructureNetworkTopologyRecordMaterialV2>();
        var headerSeen = false;
        var recordSeen = false;
        OpaqueId128? previous = null;

        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            switch (field)
            {
                case 1:
                    if (headerSeen || recordSeen) throw Error("header-order");
                    headerSeen = true;
                    reader.RequireWire(wire, 2);
                    header = DomainPartitionSnapshotWireCodecV1.DecodeHeader(reader.ReadBytes());
                    RequireHeader(header);
                    break;
                case 2:
                    if (!headerSeen || header is null) throw Error("record-before-header");
                    recordSeen = true;
                    reader.RequireWire(wire, 2);
                    var record = InfrastructureNetworkTopologyRecordWireCodecV2.Decode(reader.ReadBytes());
                    if (record.CreatedStep > header.BasisStep || record.RetiredStep is { } retired && retired > header.BasisStep)
                        throw Error("record-step-after-basis");
                    if (previous is { } prior && prior.CompareTo(record.RecordId) >= 0)
                        throw Error("record-order");
                    previous = record.RecordId;
                    records.Add(record);
                    break;
                default:
                    throw Error("field-unknown");
            }
        }

        if (header is null) throw Error("header-missing");
        return new InfrastructureNetworkTopologySnapshotFragmentDecodedV2(
            header,
            Array.AsReadOnly(records.ToArray()));
    }

    private static void RequireHeader(PartitionStateHeaderV1 header)
    {
        var identity = StandardDomainPartitionRegistry.Get(InfrastructureNetworkTopologyRecordSchemaV2.PartitionId);
        if (header.PartitionId != identity.PartitionId ||
            header.OwnerDomain != identity.OwnerDomain ||
            header.Schema != identity.PartitionSchema)
            throw Error("header-identity");
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

    private static InvalidDataException Error(string suffix)
        => new($"persistence.snapshot.infrastructure-network-v2-fragment:{suffix}");

    private ref struct Reader
    {
        private ReadOnlySpan<byte> _remaining;
        public Reader(ReadOnlySpan<byte> encoded) => _remaining = encoded;
        public bool End => _remaining.IsEmpty;
        public (int Field, int Wire) ReadTag()
        {
            var tag = ReadVarUInt64();
            if (tag == 0) throw Error("tag-zero");
            return (checked((int)(tag >> 3)), checked((int)(tag & 7)));
        }
        public void RequireWire(int actual, int expected)
        {
            if (actual != expected) throw Error("wire-type");
        }
        private ulong ReadVarUInt64()
        {
            ulong value = 0;
            var shift = 0;
            for (var i = 0; i < 10; i++)
            {
                if (_remaining.IsEmpty) throw Error("varint-truncated");
                var current = _remaining[0];
                _remaining = _remaining[1..];
                if (i == 9 && current > 1) throw Error("varint-overflow");
                value |= (ulong)(current & 0x7f) << shift;
                if ((current & 0x80) == 0) return value;
                shift += 7;
            }
            throw Error("varint-overflow");
        }
        public byte[] ReadBytes()
        {
            var length = ReadVarUInt64();
            if (length > int.MaxValue || (ulong)_remaining.Length < length) throw Error("bytes-length");
            var count = checked((int)length);
            var bytes = _remaining[..count].ToArray();
            _remaining = _remaining[count..];
            return bytes;
        }
    }
}
