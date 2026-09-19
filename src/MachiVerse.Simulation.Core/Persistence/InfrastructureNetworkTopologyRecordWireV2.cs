using System.Text;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

/// <summary>Strict standalone protobuf wire for infrastructure.network_topology record schema 2.0.</summary>
public static class InfrastructureNetworkTopologyRecordWireCodecV2
{
    public static byte[] Encode(InfrastructureNetworkTopologyRecordMaterialV2 record)
    {
        ArgumentNullException.ThrowIfNull(record);
        InfrastructureNetworkTopologyRecordSchemaV2.ValidateCanonicalContract();
        _ = InfrastructureNetworkTopologyPayloadCanonicalDigestV2.Compute(record.Payload);
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

    public static InfrastructureNetworkTopologyRecordMaterialV2 Decode(ReadOnlySpan<byte> encoded)
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
            if (field <= previous) throw Error("record-field-order");
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
                default: throw Error("record-field-unknown");
            }
        }

        if (recordIdBytes is null || recordIdBytes.Length != 16 || schemaId is null || version is null ||
            revision == 0 || payloadBytes is null)
            throw Error("record-required-field");
        if (detail > 3) throw Error("record-detail-level");
        if (!string.Equals(schemaId, InfrastructureNetworkTopologyRecordSchemaV2.RecordSchema.SchemaId.Value, StringComparison.Ordinal) ||
            version.Value != InfrastructureNetworkTopologyRecordSchemaV2.RecordSchema.Version)
            throw Error("record-schema");

        var recordId = OpaqueId128.FromBytes(recordIdBytes);
        if (recordId.IsZero) throw Error("record-id-zero");
        if (retiredStep is { } retired && retired < createdStep) throw Error("record-retired-before-created");

        OpaqueId128? lineage = null;
        if (lineageBytes is not null)
        {
            if (lineageBytes.Length != 16) throw Error("record-lineage-length");
            lineage = OpaqueId128.FromBytes(lineageBytes);
            if (lineage.Value.IsZero) throw Error("record-lineage-zero");
        }

        var material = new InfrastructureNetworkTopologyRecordMaterialV2(
            recordId,
            revision,
            createdStep,
            retiredStep,
            (DetailLevelV1)detail,
            lineage,
            DecodePayload(payloadBytes));
        _ = InfrastructureNetworkTopologyPayloadCanonicalDigestV2.Compute(material.Payload);
        return material;
    }

    private static byte[] EncodePayload(InfrastructureNetworkTopologyRecordPayloadV2 payload)
        => Proto.Encode(stream =>
        {
            Proto.WriteString(stream, 1, payload.RecordKind);
            switch (payload)
            {
                case InfrastructureNetworkPayloadV2 network:
                    Proto.WriteString(stream, 2, network.NetworkKind.Value);
                    Proto.WriteMessage(stream, 3, EncodeRefList(network.NodeRefs));
                    Proto.WriteMessage(stream, 4, EncodeRefList(network.EdgeRefs));
                    Proto.WriteMessage(stream, 5, EncodeRefList(network.OperatorRefs));
                    Proto.WriteMessage(stream, 6, EncodeRefList(network.ScopeRefs));
                    Proto.WriteString(stream, 7, network.Status.Value);
                    Proto.WriteUInt64(stream, 8, network.TopologyRevision);
                    break;
                case InfrastructureNetworkNodePayloadV2 node:
                    Proto.WriteMessage(stream, 2, EncodeRef(node.NetworkRef));
                    Proto.WriteString(stream, 3, node.NodeKind.Value);
                    Proto.WriteMessage(stream, 4, EncodeRef(node.ScopeRef));
                    Proto.WriteUInt64(stream, 5, node.CapacityUnits);
                    Proto.WriteUInt32(stream, 6, node.AvailabilityPpm);
                    Proto.WriteString(stream, 7, node.Status.Value);
                    break;
                case InfrastructureNetworkEdgePayloadV2 edge:
                    Proto.WriteMessage(stream, 2, EncodeRef(edge.NetworkRef));
                    Proto.WriteMessage(stream, 3, EncodeRef(edge.FromNodeRef));
                    Proto.WriteMessage(stream, 4, EncodeRef(edge.ToNodeRef));
                    Proto.WriteString(stream, 5, edge.EdgeKind.Value);
                    Proto.WriteUInt64(stream, 6, edge.Cost);
                    Proto.WriteUInt64(stream, 7, edge.CapacityUnits);
                    Proto.WriteUInt32(stream, 8, edge.AvailabilityPpm);
                    Proto.WriteString(stream, 9, edge.Status.Value);
                    break;
                default:
                    throw Error("payload-type");
            }
        });

    private static InfrastructureNetworkTopologyRecordPayloadV2 DecodePayload(ReadOnlySpan<byte> encoded)
    {
        var reader = new Proto.Reader(encoded);
        if (reader.End) throw Error("payload-empty");
        var (field, wire) = reader.ReadTag();
        if (field != 1) throw Error("payload-record-kind-first");
        reader.RequireWire(wire, 2);
        return new StableToken(reader.ReadString()).Value switch
        {
            InfrastructureNetworkTopologyRecordSchemaV2.NetworkKind => DecodeNetwork(ref reader),
            InfrastructureNetworkTopologyRecordSchemaV2.NodeKind => DecodeNode(ref reader),
            InfrastructureNetworkTopologyRecordSchemaV2.EdgeKind => DecodeEdge(ref reader),
            _ => throw Error("payload-record-kind-unknown"),
        };
    }

    private static InfrastructureNetworkPayloadV2 DecodeNetwork(ref Proto.Reader reader)
    {
        string? networkKind = null;
        IReadOnlyList<PartitionRecordRefV1>? nodeRefs = null;
        IReadOnlyList<PartitionRecordRefV1>? edgeRefs = null;
        IReadOnlyList<PartitionRecordRefV1>? operatorRefs = null;
        IReadOnlyList<PartitionRecordRefV1>? scopeRefs = null;
        string? status = null;
        ulong topologyRevision = 0;
        var previous = 1;
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (field <= previous) throw Error("network-field-order");
            previous = field;
            switch (field)
            {
                case 2: reader.RequireWire(wire, 2); networkKind = reader.ReadString(); break;
                case 3: reader.RequireWire(wire, 2); nodeRefs = DecodeRefList(reader.ReadBytes()); break;
                case 4: reader.RequireWire(wire, 2); edgeRefs = DecodeRefList(reader.ReadBytes()); break;
                case 5: reader.RequireWire(wire, 2); operatorRefs = DecodeRefList(reader.ReadBytes()); break;
                case 6: reader.RequireWire(wire, 2); scopeRefs = DecodeRefList(reader.ReadBytes()); break;
                case 7: reader.RequireWire(wire, 2); status = reader.ReadString(); break;
                case 8: reader.RequireWire(wire, 0); topologyRevision = reader.ReadVarUInt64(); break;
                default: throw Error("network-field-unknown");
            }
        }
        if (networkKind is null || nodeRefs is null || edgeRefs is null || operatorRefs is null || scopeRefs is null ||
            status is null || topologyRevision == 0)
            throw Error("network-required-field");
        return new InfrastructureNetworkPayloadV2(
            new StableToken(networkKind), nodeRefs, edgeRefs, operatorRefs, scopeRefs,
            new StableToken(status), topologyRevision);
    }

    private static InfrastructureNetworkNodePayloadV2 DecodeNode(ref Proto.Reader reader)
    {
        PartitionRecordRefV1? networkRef = null;
        string? nodeKind = null;
        PartitionRecordRefV1? scopeRef = null;
        ulong? capacityUnits = null;
        uint? availabilityPpm = null;
        string? status = null;
        var previous = 1;
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (field <= previous) throw Error("node-field-order");
            previous = field;
            switch (field)
            {
                case 2: reader.RequireWire(wire, 2); networkRef = DecodeRef(reader.ReadBytes()); break;
                case 3: reader.RequireWire(wire, 2); nodeKind = reader.ReadString(); break;
                case 4: reader.RequireWire(wire, 2); scopeRef = DecodeRef(reader.ReadBytes()); break;
                case 5: reader.RequireWire(wire, 0); capacityUnits = reader.ReadVarUInt64(); break;
                case 6: reader.RequireWire(wire, 0); availabilityPpm = reader.ReadUInt32(); break;
                case 7: reader.RequireWire(wire, 2); status = reader.ReadString(); break;
                default: throw Error("node-field-unknown");
            }
        }
        if (networkRef is null || nodeKind is null || scopeRef is null || capacityUnits is null || availabilityPpm is null || status is null)
            throw Error("node-required-field");
        return new InfrastructureNetworkNodePayloadV2(
            networkRef.Value,
            new StableToken(nodeKind),
            scopeRef.Value,
            capacityUnits.Value,
            availabilityPpm.Value,
            new StableToken(status));
    }

    private static InfrastructureNetworkEdgePayloadV2 DecodeEdge(ref Proto.Reader reader)
    {
        PartitionRecordRefV1? networkRef = null;
        PartitionRecordRefV1? fromNodeRef = null;
        PartitionRecordRefV1? toNodeRef = null;
        string? edgeKind = null;
        ulong? cost = null;
        ulong? capacityUnits = null;
        uint? availabilityPpm = null;
        string? status = null;
        var previous = 1;
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (field <= previous) throw Error("edge-field-order");
            previous = field;
            switch (field)
            {
                case 2: reader.RequireWire(wire, 2); networkRef = DecodeRef(reader.ReadBytes()); break;
                case 3: reader.RequireWire(wire, 2); fromNodeRef = DecodeRef(reader.ReadBytes()); break;
                case 4: reader.RequireWire(wire, 2); toNodeRef = DecodeRef(reader.ReadBytes()); break;
                case 5: reader.RequireWire(wire, 2); edgeKind = reader.ReadString(); break;
                case 6: reader.RequireWire(wire, 0); cost = reader.ReadVarUInt64(); break;
                case 7: reader.RequireWire(wire, 0); capacityUnits = reader.ReadVarUInt64(); break;
                case 8: reader.RequireWire(wire, 0); availabilityPpm = reader.ReadUInt32(); break;
                case 9: reader.RequireWire(wire, 2); status = reader.ReadString(); break;
                default: throw Error("edge-field-unknown");
            }
        }
        if (networkRef is null || fromNodeRef is null || toNodeRef is null || edgeKind is null || cost is null ||
            capacityUnits is null || availabilityPpm is null || status is null)
            throw Error("edge-required-field");
        return new InfrastructureNetworkEdgePayloadV2(
            networkRef.Value,
            fromNodeRef.Value,
            toNodeRef.Value,
            new StableToken(edgeKind),
            cost.Value,
            capacityUnits.Value,
            availabilityPpm.Value,
            new StableToken(status));
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
            if (field <= previous) throw Error("schema-version-field-order");
            previous = field;
            reader.RequireWire(wire, 0);
            if (field == 1) major = reader.ReadUInt32();
            else if (field == 2) minor = reader.ReadUInt32();
            else throw Error("schema-version-field-unknown");
        }
        if (major > ushort.MaxValue || minor > ushort.MaxValue) throw Error("schema-version-range");
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
            if (field <= previous) throw Error("ref-field-order");
            previous = field;
            if (field == 1) { reader.RequireWire(wire, 2); partitionId = reader.ReadString(); }
            else if (field == 2) { reader.RequireWire(wire, 2); idBytes = reader.ReadBytes(); }
            else throw Error("ref-field-unknown");
        }
        if (partitionId is null || idBytes is null || idBytes.Length != 16) throw Error("ref-required-field");
        var id = OpaqueId128.FromBytes(idBytes);
        if (id.IsZero) throw Error("ref-id-zero");
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
            if (field != 1) throw Error("ref-list-field-unknown");
            reader.RequireWire(wire, 2);
            values.Add(DecodeRef(reader.ReadBytes()));
        }
        return Array.AsReadOnly(values.ToArray());
    }

    private static InvalidDataException Error(string suffix)
        => new($"persistence.snapshot.infrastructure-network-v2:{suffix}");

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
        public static void WriteUInt32(Stream stream, int field, uint value) { WriteTag(stream, field, 0); WriteVarUInt64(stream, value); }
        public static void WriteUInt64(Stream stream, int field, ulong value) { WriteTag(stream, field, 0); WriteVarUInt64(stream, value); }
        private static void WriteTag(Stream stream, int field, int wire) => WriteVarUInt64(stream, ((ulong)field << 3) | (uint)wire);
        private static void WriteVarUInt64(Stream stream, ulong value)
        {
            while (value >= 0x80)
            {
                stream.WriteByte((byte)((value & 0x7f) | 0x80));
                value >>= 7;
            }
            stream.WriteByte((byte)value);
        }

        public ref struct Reader
        {
            private ReadOnlySpan<byte> _remaining;
            public Reader(ReadOnlySpan<byte> encoded) => _remaining = encoded;
            public bool End => _remaining.IsEmpty;

            public (int Field, int Wire) ReadTag()
            {
                var tag = ReadVarUInt64();
                if (tag == 0) throw Error("tag-zero");
                return ((int)(tag >> 3), (int)(tag & 7));
            }

            public void RequireWire(int actual, int expected)
            {
                if (actual != expected) throw Error("wire-type");
            }

            public ulong ReadVarUInt64()
            {
                ulong value = 0;
                var shift = 0;
                for (var index = 0; index < 10; index++)
                {
                    if (_remaining.IsEmpty) throw Error("varint-truncated");
                    var current = _remaining[0];
                    _remaining = _remaining[1..];
                    if (index == 9 && current > 1) throw Error("varint-overflow");
                    value |= (ulong)(current & 0x7f) << shift;
                    if ((current & 0x80) == 0) return value;
                    shift += 7;
                }
                throw Error("varint-overflow");
            }

            public uint ReadUInt32()
            {
                var value = ReadVarUInt64();
                if (value > uint.MaxValue) throw Error("uint32-overflow");
                return (uint)value;
            }

            public byte[] ReadBytes()
            {
                var length = ReadVarUInt64();
                if (length > int.MaxValue || (ulong)_remaining.Length < length) throw Error("bytes-length");
                var count = (int)length;
                var bytes = _remaining[..count].ToArray();
                _remaining = _remaining[count..];
                return bytes;
            }

            public string ReadString()
            {
                var bytes = ReadBytes();
                try { return new UTF8Encoding(false, true).GetString(bytes); }
                catch (DecoderFallbackException) { throw Error("utf8"); }
            }
        }
    }
}
