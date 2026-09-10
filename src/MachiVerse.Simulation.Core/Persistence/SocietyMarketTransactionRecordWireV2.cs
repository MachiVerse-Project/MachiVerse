using System.Text;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

/// <summary>Strict standalone protobuf wire for society.market_transaction record schema 2.0.</summary>
public static class SocietyMarketTransactionRecordWireCodecV2
{
    public static byte[] Encode(SocietyMarketTransactionRecordMaterialV2 record)
    {
        ArgumentNullException.ThrowIfNull(record);
        SocietyMarketTransactionRecordSchemaV2.ValidateCanonicalContract();
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

    public static SocietyMarketTransactionRecordMaterialV2 Decode(ReadOnlySpan<byte> encoded)
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

        if (recordIdBytes is null || recordIdBytes.Length != 16 || schemaId is null || version is null || revision == 0 || payloadBytes is null)
            throw Error("record-required-field");
        if (detail > 3) throw Error("record-detail-level");
        if (!string.Equals(schemaId, SocietyMarketTransactionRecordSchemaV2.RecordSchema.SchemaId.Value, StringComparison.Ordinal) ||
            version.Value != SocietyMarketTransactionRecordSchemaV2.RecordSchema.Version)
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

        return new SocietyMarketTransactionRecordMaterialV2(
            recordId,
            revision,
            createdStep,
            retiredStep,
            (DetailLevelV1)detail,
            lineage,
            DecodePayload(payloadBytes));
    }

    private static byte[] EncodePayload(SocietyMarketTransactionRecordPayloadV2 payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return Proto.Encode(stream =>
        {
            Proto.WriteString(stream, 1, payload.RecordKind);
            switch (payload)
            {
                case SocietyMarketStatePayloadV2 state:
                    Proto.WriteMessage(stream, 10, EncodeRef(state.ScopeRef));
                    Proto.WriteString(stream, 11, state.InstrumentToken.Value);
                    Proto.WriteString(stream, 12, state.CurrencyToken.Value);
                    Proto.WriteString(stream, 13, state.Status.Value);
                    Proto.WriteUInt64(stream, 14, state.ClearingCadenceSteps);
                    if (state.LastClearingStep is { } step) Proto.WriteUInt64(stream, 15, step);
                    if (state.LastClearingPriceMicrounit is { } price) Proto.WriteSInt64(stream, 16, price);
                    break;
                case SocietyMarketOrderPayloadV2 order:
                    Proto.WriteMessage(stream, 10, EncodeRef(order.MarketRef));
                    Proto.WriteMessage(stream, 11, EncodeRef(order.OwnerRef));
                    Proto.WriteString(stream, 12, order.InstrumentToken.Value);
                    Proto.WriteString(stream, 13, order.Side.Value);
                    Proto.WriteSInt64(stream, 14, order.LimitPriceMicrounit);
                    Proto.WriteSInt64(stream, 15, order.Quantity);
                    Proto.WriteSInt64(stream, 16, order.RemainingQuantity);
                    Proto.WriteUInt64(stream, 17, order.EligibleStep);
                    Proto.WriteString(stream, 18, order.Status.Value);
                    break;
                case SocietyMarketFactPayloadV2 fact:
                    Proto.WriteString(stream, 10, fact.FactKind.Value);
                    Proto.WriteMessage(stream, 11, EncodeRef(fact.MarketRef));
                    Proto.WriteString(stream, 12, fact.InstrumentToken.Value);
                    if (fact.OrderSide is { } side) Proto.WriteString(stream, 13, side.Value);
                    if (fact.LimitPriceMicrounit is { } limit) Proto.WriteSInt64(stream, 14, limit);
                    Proto.WriteSInt64(stream, 15, fact.Quantity);
                    if (fact.ClearingPriceMicrounit is { } clearing) Proto.WriteSInt64(stream, 16, clearing);
                    if (fact.BuyerRef is { } buyer) Proto.WriteMessage(stream, 17, EncodeRef(buyer));
                    if (fact.SellerRef is { } seller) Proto.WriteMessage(stream, 18, EncodeRef(seller));
                    Proto.WriteUInt64(stream, 19, fact.EligibleStep);
                    Proto.WriteString(stream, 20, fact.Status.Value);
                    break;
                default:
                    throw Error("payload-type");
            }
        });
    }

    private static SocietyMarketTransactionRecordPayloadV2 DecodePayload(ReadOnlySpan<byte> encoded)
    {
        var reader = new Proto.Reader(encoded);
        if (reader.End) throw Error("payload-empty");
        var (field, wire) = reader.ReadTag();
        if (field != 1) throw Error("payload-record-kind-first");
        reader.RequireWire(wire, 2);
        return new StableToken(reader.ReadString()).Value switch
        {
            SocietyMarketTransactionRecordSchemaV2.MarketStateKind => DecodeState(ref reader),
            SocietyMarketTransactionRecordSchemaV2.OrderOrOfferKind => DecodeOrder(ref reader),
            SocietyMarketTransactionRecordSchemaV2.TransactionOrPriceFactKind => DecodeFact(ref reader),
            _ => throw Error("payload-record-kind-unknown"),
        };
    }

    private static SocietyMarketStatePayloadV2 DecodeState(ref Proto.Reader reader)
    {
        PartitionRecordRefV1? scope = null;
        string? instrument = null;
        string? currency = null;
        string? status = null;
        ulong cadence = 0;
        ulong? lastStep = null;
        long? lastPrice = null;
        var previous = 1;
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (field <= previous) throw Error("state-field-order");
            previous = field;
            switch (field)
            {
                case 10: reader.RequireWire(wire, 2); scope = DecodeRef(reader.ReadBytes()); break;
                case 11: reader.RequireWire(wire, 2); instrument = reader.ReadString(); break;
                case 12: reader.RequireWire(wire, 2); currency = reader.ReadString(); break;
                case 13: reader.RequireWire(wire, 2); status = reader.ReadString(); break;
                case 14: reader.RequireWire(wire, 0); cadence = reader.ReadVarUInt64(); break;
                case 15: reader.RequireWire(wire, 0); lastStep = reader.ReadVarUInt64(); break;
                case 16: reader.RequireWire(wire, 0); lastPrice = reader.ReadSInt64(); break;
                default: throw Error("state-field-unknown");
            }
        }
        if (scope is null || instrument is null || currency is null || status is null || cadence == 0)
            throw Error("state-required-field");
        return new SocietyMarketStatePayloadV2(scope.Value, new StableToken(instrument), new StableToken(currency), new StableToken(status), cadence, lastStep, lastPrice);
    }

    private static SocietyMarketOrderPayloadV2 DecodeOrder(ref Proto.Reader reader)
    {
        PartitionRecordRefV1? market = null;
        PartitionRecordRefV1? owner = null;
        string? instrument = null;
        string? side = null;
        string? status = null;
        long? limit = null;
        long? quantity = null;
        long? remaining = null;
        ulong? eligible = null;
        var previous = 1;
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (field <= previous) throw Error("order-field-order");
            previous = field;
            switch (field)
            {
                case 10: reader.RequireWire(wire, 2); market = DecodeRef(reader.ReadBytes()); break;
                case 11: reader.RequireWire(wire, 2); owner = DecodeRef(reader.ReadBytes()); break;
                case 12: reader.RequireWire(wire, 2); instrument = reader.ReadString(); break;
                case 13: reader.RequireWire(wire, 2); side = reader.ReadString(); break;
                case 14: reader.RequireWire(wire, 0); limit = reader.ReadSInt64(); break;
                case 15: reader.RequireWire(wire, 0); quantity = reader.ReadSInt64(); break;
                case 16: reader.RequireWire(wire, 0); remaining = reader.ReadSInt64(); break;
                case 17: reader.RequireWire(wire, 0); eligible = reader.ReadVarUInt64(); break;
                case 18: reader.RequireWire(wire, 2); status = reader.ReadString(); break;
                default: throw Error("order-field-unknown");
            }
        }
        if (market is null || owner is null || instrument is null || side is null || status is null || limit is null || quantity is null || remaining is null || eligible is null)
            throw Error("order-required-field");
        return new SocietyMarketOrderPayloadV2(market.Value, owner.Value, new StableToken(instrument), new StableToken(side), limit.Value, quantity.Value, remaining.Value, eligible.Value, new StableToken(status));
    }

    private static SocietyMarketFactPayloadV2 DecodeFact(ref Proto.Reader reader)
    {
        string? factKind = null;
        PartitionRecordRefV1? market = null;
        string? instrument = null;
        string? side = null;
        long? limit = null;
        long? quantity = null;
        long? clearing = null;
        PartitionRecordRefV1? buyer = null;
        PartitionRecordRefV1? seller = null;
        ulong? eligible = null;
        string? status = null;
        var previous = 1;
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (field <= previous) throw Error("fact-field-order");
            previous = field;
            switch (field)
            {
                case 10: reader.RequireWire(wire, 2); factKind = reader.ReadString(); break;
                case 11: reader.RequireWire(wire, 2); market = DecodeRef(reader.ReadBytes()); break;
                case 12: reader.RequireWire(wire, 2); instrument = reader.ReadString(); break;
                case 13: reader.RequireWire(wire, 2); side = reader.ReadString(); break;
                case 14: reader.RequireWire(wire, 0); limit = reader.ReadSInt64(); break;
                case 15: reader.RequireWire(wire, 0); quantity = reader.ReadSInt64(); break;
                case 16: reader.RequireWire(wire, 0); clearing = reader.ReadSInt64(); break;
                case 17: reader.RequireWire(wire, 2); buyer = DecodeRef(reader.ReadBytes()); break;
                case 18: reader.RequireWire(wire, 2); seller = DecodeRef(reader.ReadBytes()); break;
                case 19: reader.RequireWire(wire, 0); eligible = reader.ReadVarUInt64(); break;
                case 20: reader.RequireWire(wire, 2); status = reader.ReadString(); break;
                default: throw Error("fact-field-unknown");
            }
        }
        if (factKind is null || market is null || instrument is null || quantity is null || eligible is null || status is null)
            throw Error("fact-required-field");
        return new SocietyMarketFactPayloadV2(
            new StableToken(factKind), market.Value, new StableToken(instrument),
            side is null ? null : new StableToken(side), limit, quantity.Value, clearing, buyer, seller, eligible.Value, new StableToken(status));
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
            if (field <= previous) throw Error("schema-version-field-order");
            previous = field;
            reader.RequireWire(wire, 0);
            if (field == 1) major = reader.ReadUInt32();
            else if (field == 2) minor = reader.ReadUInt32();
            else throw Error("schema-version-field-unknown");
        }
        if (major is null || minor is null || major > ushort.MaxValue || minor > ushort.MaxValue)
            throw Error("schema-version-required-field");
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
            if (field <= previous) throw Error("ref-field-order");
            previous = field;
            if (field == 1) { reader.RequireWire(wire, 2); partition = reader.ReadString(); }
            else if (field == 2) { reader.RequireWire(wire, 2); idBytes = reader.ReadBytes(); }
            else throw Error("ref-field-unknown");
        }
        if (partition is null || idBytes is null || idBytes.Length != 16) throw Error("ref-required-field");
        var id = OpaqueId128.FromBytes(idBytes);
        if (id.IsZero) throw Error("ref-id-zero");
        return new PartitionRecordRefV1(partition, id);
    }

    private static InvalidDataException Error(string suffix)
        => new($"persistence.snapshot.society-market-v2:{suffix}");

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
        public static void WriteSInt64(Stream stream, int field, long value) { WriteTag(stream, field, 0); WriteVarUInt64(stream, unchecked((ulong)((value << 1) ^ (value >> 63)))); }
        private static void WriteTag(Stream stream, int field, int wire) => WriteVarUInt64(stream, ((ulong)field << 3) | (uint)wire);
        private static void WriteVarUInt64(Stream stream, ulong value)
        {
            while (value >= 0x80) { stream.WriteByte((byte)((value & 0x7f) | 0x80)); value >>= 7; }
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
                return (checked((int)(tag >> 3)), checked((int)(tag & 7)));
            }
            public void RequireWire(int actual, int expected) { if (actual != expected) throw Error("wire-type"); }
            public ulong ReadVarUInt64()
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
            public uint ReadUInt32()
            {
                var value = ReadVarUInt64();
                if (value > uint.MaxValue) throw Error("uint32-overflow");
                return (uint)value;
            }
            public long ReadSInt64()
            {
                var value = ReadVarUInt64();
                return (long)(value >> 1) ^ -((long)value & 1);
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
            public string ReadString()
            {
                var bytes = ReadBytes();
                try { return new UTF8Encoding(false, true).GetString(bytes); }
                catch (DecoderFallbackException) { throw Error("utf8"); }
            }
        }
    }
}
