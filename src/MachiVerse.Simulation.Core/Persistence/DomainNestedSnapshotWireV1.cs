using System.Collections.ObjectModel;
using System.Text;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

/// <summary>
/// Exact DomainNestedValueWireV1 / DomainNestedListWireV1 implementation for explicitly registered
/// nested schemas. Recursive nested fields remain fail-closed unless the bound owner codec explicitly
/// opts into same-schema self recursion; recursive children can never switch codec/schema implicitly.
/// </summary>
public static class DomainNestedSnapshotWireCodecV1
{
    private const int MaxSelfRecursiveDepth = 64;

    public static byte[] EncodeValue(
        string parentPartitionId,
        string parentFieldName,
        ICanonicalDomainNestedValueV1 value,
        DomainNestedSnapshotCodecRegistryV1 registry)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(registry);
        var codec = registry.GetForBinding(parentPartitionId, parentFieldName);
        return EncodeValue(codec, value, registry, 0);
    }

    public static ICanonicalDomainNestedValueV1 DecodeValue(
        string parentPartitionId,
        string parentFieldName,
        ReadOnlySpan<byte> encoded,
        DomainNestedSnapshotCodecRegistryV1 registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var bound = registry.GetForBinding(parentPartitionId, parentFieldName);
        return DecodeValue(bound, encoded, registry, 0);
    }

    public static byte[] EncodeList(
        string parentPartitionId,
        string parentFieldName,
        IReadOnlyList<ICanonicalDomainNestedValueV1> values,
        DomainNestedSnapshotCodecRegistryV1 registry)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(registry);
        registry.ValidateOrderedList(parentPartitionId, parentFieldName, values);
        var codec = registry.GetForBinding(parentPartitionId, parentFieldName);
        return Proto.Encode(stream =>
        {
            foreach (var value in values)
                Proto.WriteMessage(stream, 1, EncodeValue(codec, value, registry, 0));
        });
    }

    public static IReadOnlyList<ICanonicalDomainNestedValueV1> DecodeList(
        string parentPartitionId,
        string parentFieldName,
        ReadOnlySpan<byte> encoded,
        DomainNestedSnapshotCodecRegistryV1 registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var codec = registry.GetForBinding(parentPartitionId, parentFieldName);
        var reader = new Proto.Reader(encoded);
        var values = new List<ICanonicalDomainNestedValueV1>();
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (field != 1) throw Error($"nested-list-unknown-field:{parentPartitionId}:{parentFieldName}");
            reader.RequireWire(wire, 2);
            values.Add(DecodeValue(codec, reader.ReadBytes(), registry, 0));
        }
        registry.ValidateOrderedList(parentPartitionId, parentFieldName, values);
        return Array.AsReadOnly(values.ToArray());
    }

    private static byte[] EncodeValue(
        IDomainNestedSnapshotCodecV1 codec,
        ICanonicalDomainNestedValueV1 value,
        DomainNestedSnapshotCodecRegistryV1 registry,
        int depth)
    {
        RequireDepth(codec, depth);
        if (!codec.CanEncode(value))
            throw Error($"nested-codec-type-mismatch:{codec.ParentPartitionId}:{codec.ParentFieldName}");
        value.ValidateCanonical();
        var fields = codec.ToStandardFields(value);
        ValidateFieldSet(codec, fields);

        return Proto.Encode(stream =>
        {
            Proto.WriteString(stream, 1, codec.Descriptor.Schema.SchemaId.Value);
            Proto.WriteMessage(stream, 2, EncodeSchemaVersion(codec.Descriptor.Schema.Version));
            for (var index = 0; index < codec.Descriptor.Fields.Count; index++)
            {
                var rule = codec.Descriptor.Fields[index];
                if (!fields.TryGetValue(rule.Name, out var fieldValue) || fieldValue is null)
                {
                    if (!rule.Optional)
                        throw Error($"nested-required-field:{codec.Descriptor.Schema.SchemaId.Value}:{rule.Name}");
                    continue;
                }

                var entry = Proto.Encode(entryStream =>
                {
                    Proto.WriteUInt32(entryStream, 1, checked((uint)index + 1u));
                    Proto.WriteMessage(entryStream, 2, EncodeScalarValue(codec, rule, fieldValue, registry, depth));
                });
                Proto.WriteMessage(stream, 3, entry);
            }
        });
    }

    private static ICanonicalDomainNestedValueV1 DecodeValue(
        IDomainNestedSnapshotCodecV1 bound,
        ReadOnlySpan<byte> encoded,
        DomainNestedSnapshotCodecRegistryV1 registry,
        int depth)
    {
        RequireDepth(bound, depth);
        var reader = new Proto.Reader(encoded);
        string? schemaId = null;
        SchemaVersionV1? schemaVersion = null;
        var entries = new List<(uint Number, byte[] Value)>();
        var seenSingular = new HashSet<int>();
        uint previousNumber = 0;

        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            switch (field)
            {
                case 1:
                    if (!seenSingular.Add(field)) throw Error("nested-schema-id-duplicate");
                    reader.RequireWire(wire, 2);
                    schemaId = new StableToken(reader.ReadString()).Value;
                    break;
                case 2:
                    if (!seenSingular.Add(field)) throw Error("nested-schema-version-duplicate");
                    reader.RequireWire(wire, 2);
                    schemaVersion = DecodeSchemaVersion(reader.ReadBytes());
                    break;
                case 3:
                {
                    reader.RequireWire(wire, 2);
                    var entry = DecodeFieldEntry(reader.ReadBytes());
                    if (entry.Number <= previousNumber)
                        throw Error($"nested-field-order:{bound.ParentPartitionId}:{bound.ParentFieldName}");
                    previousNumber = entry.Number;
                    entries.Add(entry);
                    break;
                }
                default:
                    throw Error($"nested-value-unknown-field:{bound.ParentPartitionId}:{bound.ParentFieldName}");
            }
        }

        if (schemaId is null || schemaVersion is null)
            throw Error($"nested-schema-required:{bound.ParentPartitionId}:{bound.ParentFieldName}");
        var wireSchema = new SchemaRefV1(schemaId, schemaVersion.Value.Major, schemaVersion.Value.Minor);
        var schemaCodec = registry.GetForSchema(wireSchema);
        if (!ReferenceEquals(schemaCodec, bound) || wireSchema != bound.Descriptor.Schema)
            throw Error($"nested-schema-binding-mismatch:{bound.ParentPartitionId}:{bound.ParentFieldName}");

        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (entry.Number == 0 || entry.Number > bound.Descriptor.Fields.Count)
                throw Error($"nested-field-number:{schemaId}:{entry.Number}");
            var rule = bound.Descriptor.Fields[checked((int)entry.Number - 1)];
            values.Add(rule.Name, DecodeScalarValue(bound, rule, entry.Value, registry, depth));
        }
        foreach (var rule in bound.Descriptor.Fields)
        {
            if (!rule.Optional && !values.ContainsKey(rule.Name))
                throw Error($"nested-required-field:{schemaId}:{rule.Name}");
        }

        var normalized = new ReadOnlyDictionary<string, object?>(values);
        var result = bound.FromStandardFields(normalized);
        if (!bound.CanEncode(result))
            throw Error($"nested-codec-type-mismatch:{bound.ParentPartitionId}:{bound.ParentFieldName}");
        result.ValidateCanonical();
        return result;
    }

    private static void ValidateFieldSet(
        IDomainNestedSnapshotCodecV1 codec,
        IReadOnlyDictionary<string, object?> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var allowed = codec.Descriptor.Fields.Select(static field => field.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var key in fields.Keys)
        {
            if (!allowed.Contains(key))
                throw Error($"nested-field-unknown:{codec.Descriptor.Schema.SchemaId.Value}:{key}");
        }
    }

    private static byte[] EncodeScalarValue(
        IDomainNestedSnapshotCodecV1 codec,
        DomainPayloadFieldRuleV1 rule,
        object value,
        DomainNestedSnapshotCodecRegistryV1 registry,
        int depth)
    {
        var schema = codec.Descriptor.Schema.SchemaId.Value;
        return Proto.Encode(stream =>
        {
            switch (rule.Kind)
            {
                case DomainPayloadFieldKindV1.Id128:
                {
                    if (value is not OpaqueId128 id || id.IsZero) throw TypeOrRange(schema, rule);
                    Proto.WriteBytes(stream, 3, id.ToBytes());
                    break;
                }
                case DomainPayloadFieldKindV1.Token:
                    if (value is not string token) throw TypeOrRange(schema, rule);
                    Proto.WriteString(stream, 4, new StableToken(token).Value);
                    break;
                case DomainPayloadFieldKindV1.Ratio:
                    if (value is not uint ratio || ratio > 1_000_000u) throw TypeOrRange(schema, rule);
                    Proto.WriteUInt32(stream, 6, ratio);
                    break;
                case DomainPayloadFieldKindV1.UInt8:
                    if (value is not byte u8) throw TypeOrRange(schema, rule);
                    Proto.WriteUInt32(stream, 6, u8);
                    break;
                case DomainPayloadFieldKindV1.UInt16:
                    if (value is not ushort u16) throw TypeOrRange(schema, rule);
                    Proto.WriteUInt32(stream, 6, u16);
                    break;
                case DomainPayloadFieldKindV1.UInt32:
                    if (value is not uint u32) throw TypeOrRange(schema, rule);
                    Proto.WriteUInt32(stream, 6, u32);
                    break;
                case DomainPayloadFieldKindV1.Step:
                case DomainPayloadFieldKindV1.UInt64:
                    if (value is not ulong u64) throw TypeOrRange(schema, rule);
                    Proto.WriteUInt64(stream, 7, u64);
                    break;
                case DomainPayloadFieldKindV1.Int32:
                case DomainPayloadFieldKindV1.Temperature:
                case DomainPayloadFieldKindV1.Pressure:
                    if (value is not int i32) throw TypeOrRange(schema, rule);
                    Proto.WriteSInt32(stream, 8, i32);
                    break;
                case DomainPayloadFieldKindV1.Int64:
                case DomainPayloadFieldKindV1.Length:
                case DomainPayloadFieldKindV1.Mass:
                case DomainPayloadFieldKindV1.Volume:
                case DomainPayloadFieldKindV1.Power:
                case DomainPayloadFieldKindV1.Energy:
                case DomainPayloadFieldKindV1.Money:
                    if (value is not long i64) throw TypeOrRange(schema, rule);
                    Proto.WriteSInt64(stream, 9, i64);
                    break;
                case DomainPayloadFieldKindV1.Bool:
                    if (value is not bool flag) throw TypeOrRange(schema, rule);
                    Proto.WriteBool(stream, 10, flag);
                    break;
                case DomainPayloadFieldKindV1.Digest:
                    if (value is not byte[] digest || digest.Length != 32) throw TypeOrRange(schema, rule);
                    Proto.WriteBytes(stream, 11, digest);
                    break;
                case DomainPayloadFieldKindV1.OrderedNestedList:
                {
                    if (!codec.AllowsSelfRecursion)
                        throw Error($"nested-recursive-codec-unavailable:{schema}:{rule.Name}");
                    if (value is not IReadOnlyList<ICanonicalDomainNestedValueV1> nestedValues)
                        throw TypeOrRange(schema, rule);
                    var listWire = Proto.Encode(listStream =>
                    {
                        foreach (var nested in nestedValues)
                        {
                            if (nested is null || !codec.CanEncode(nested))
                                throw Error($"nested-self-recursive-type-mismatch:{schema}:{rule.Name}");
                            Proto.WriteMessage(listStream, 1, EncodeValue(codec, nested, registry, checked(depth + 1)));
                        }
                    });
                    Proto.WriteMessage(stream, 12, listWire);
                    break;
                }
                case DomainPayloadFieldKindV1.RuleAst:
                {
                    if (!codec.AllowsSelfRecursion)
                        throw Error($"nested-recursive-codec-unavailable:{schema}:{rule.Name}");
                    if (value is not ICanonicalDomainNestedValueV1 nested || !codec.CanEncode(nested))
                        throw TypeOrRange(schema, rule);
                    Proto.WriteMessage(stream, 13, EncodeValue(codec, nested, registry, checked(depth + 1)));
                    break;
                }
                default:
                    throw Error($"nested-field-kind-unsupported:{schema}:{rule.Name}:{rule.Kind}");
            }
        });
    }

    private static object DecodeScalarValue(
        IDomainNestedSnapshotCodecV1 codec,
        DomainPayloadFieldRuleV1 rule,
        ReadOnlySpan<byte> encoded,
        DomainNestedSnapshotCodecRegistryV1 registry,
        int depth)
    {
        var schema = codec.Descriptor.Schema.SchemaId.Value;
        var reader = new Proto.Reader(encoded);
        object? value = null;
        var arm = 0;
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (arm != 0) throw Error($"nested-field-multiple-arms:{schema}:{rule.Name}");
            arm = field;
            switch (field)
            {
                case 3:
                {
                    reader.RequireWire(wire, 2);
                    var bytes = reader.ReadBytes();
                    if (bytes.Length != 16) throw TypeOrRange(schema, rule);
                    var id = OpaqueId128.FromBytes(bytes);
                    if (id.IsZero) throw TypeOrRange(schema, rule);
                    value = id;
                    break;
                }
                case 4:
                    reader.RequireWire(wire, 2);
                    value = new StableToken(reader.ReadString()).Value;
                    break;
                case 6:
                    reader.RequireWire(wire, 0);
                    value = reader.ReadUInt32();
                    break;
                case 7:
                    reader.RequireWire(wire, 0);
                    value = reader.ReadVarUInt64();
                    break;
                case 8:
                    reader.RequireWire(wire, 0);
                    value = reader.ReadSInt32();
                    break;
                case 9:
                    reader.RequireWire(wire, 0);
                    value = reader.ReadSInt64();
                    break;
                case 10:
                    reader.RequireWire(wire, 0);
                    value = reader.ReadBool();
                    break;
                case 11:
                {
                    reader.RequireWire(wire, 2);
                    var digest = reader.ReadBytes();
                    if (digest.Length != 32) throw TypeOrRange(schema, rule);
                    value = digest;
                    break;
                }
                case 12:
                {
                    reader.RequireWire(wire, 2);
                    if (!codec.AllowsSelfRecursion)
                        throw Error($"nested-recursive-codec-unavailable:{schema}:{rule.Name}");
                    value = DecodeSelfRecursiveList(codec, reader.ReadBytes(), registry, checked(depth + 1));
                    break;
                }
                case 13:
                    reader.RequireWire(wire, 2);
                    if (!codec.AllowsSelfRecursion)
                        throw Error($"nested-recursive-codec-unavailable:{schema}:{rule.Name}");
                    value = DecodeValue(codec, reader.ReadBytes(), registry, checked(depth + 1));
                    break;
                default:
                    throw Error($"nested-field-value-arm-unsupported:{schema}:{rule.Name}:{field}");
            }
        }
        if (value is null) throw Error($"nested-field-value-missing:{schema}:{rule.Name}");
        return NormalizeScalar(schema, rule, arm, value);
    }

    private static IReadOnlyList<ICanonicalDomainNestedValueV1> DecodeSelfRecursiveList(
        IDomainNestedSnapshotCodecV1 codec,
        ReadOnlySpan<byte> encoded,
        DomainNestedSnapshotCodecRegistryV1 registry,
        int depth)
    {
        RequireDepth(codec, depth);
        var reader = new Proto.Reader(encoded);
        var values = new List<ICanonicalDomainNestedValueV1>();
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (field != 1)
                throw Error($"nested-self-recursive-list-unknown-field:{codec.Descriptor.Schema.SchemaId.Value}");
            reader.RequireWire(wire, 2);
            values.Add(DecodeValue(codec, reader.ReadBytes(), registry, depth));
        }
        return Array.AsReadOnly(values.ToArray());
    }

    private static object NormalizeScalar(string schema, DomainPayloadFieldRuleV1 rule, int arm, object value)
    {
        return rule.Kind switch
        {
            DomainPayloadFieldKindV1.Id128 when arm == 3 && value is OpaqueId128 => value,
            DomainPayloadFieldKindV1.Token when arm == 4 && value is string => value,
            DomainPayloadFieldKindV1.Ratio when arm == 6 && value is uint ratio && ratio <= 1_000_000u => ratio,
            DomainPayloadFieldKindV1.UInt8 when arm == 6 && value is uint u8 && u8 <= byte.MaxValue => (byte)u8,
            DomainPayloadFieldKindV1.UInt16 when arm == 6 && value is uint u16 && u16 <= ushort.MaxValue => (ushort)u16,
            DomainPayloadFieldKindV1.UInt32 when arm == 6 && value is uint => value,
            DomainPayloadFieldKindV1.Step or DomainPayloadFieldKindV1.UInt64 when arm == 7 && value is ulong => value,
            DomainPayloadFieldKindV1.Int32 or DomainPayloadFieldKindV1.Temperature or DomainPayloadFieldKindV1.Pressure when arm == 8 && value is int => value,
            DomainPayloadFieldKindV1.Int64 or DomainPayloadFieldKindV1.Length or DomainPayloadFieldKindV1.Mass or DomainPayloadFieldKindV1.Volume or DomainPayloadFieldKindV1.Power or DomainPayloadFieldKindV1.Energy or DomainPayloadFieldKindV1.Money when arm == 9 && value is long => value,
            DomainPayloadFieldKindV1.Bool when arm == 10 && value is bool => value,
            DomainPayloadFieldKindV1.Digest when arm == 11 && value is byte[] digest && digest.Length == 32 => digest,
            DomainPayloadFieldKindV1.OrderedNestedList when arm == 12 && value is IReadOnlyList<ICanonicalDomainNestedValueV1> => value,
            DomainPayloadFieldKindV1.RuleAst when arm == 13 && value is ICanonicalDomainNestedValueV1 => value,
            _ => throw Error($"nested-field-kind-mismatch:{schema}:{rule.Name}"),
        };
    }

    private static (uint Number, byte[] Value) DecodeFieldEntry(ReadOnlySpan<byte> encoded)
    {
        var reader = new Proto.Reader(encoded);
        uint number = 0;
        byte[]? value = null;
        var seen = new HashSet<int>();
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (!seen.Add(field)) throw Error("nested-field-entry-duplicate");
            switch (field)
            {
                case 1: reader.RequireWire(wire, 0); number = reader.ReadUInt32(); break;
                case 2: reader.RequireWire(wire, 2); value = reader.ReadBytes(); break;
                default: throw Error("nested-field-entry-unknown-field");
            }
        }
        if (number == 0 || value is null) throw Error("nested-field-entry-required");
        return (number, value);
    }

    private static byte[] EncodeSchemaVersion(SchemaVersionV1 version)
        => Proto.Encode(stream =>
        {
            if (version.Major != 0) Proto.WriteUInt32(stream, 1, version.Major);
            if (version.Minor != 0) Proto.WriteUInt32(stream, 2, version.Minor);
        });

    private static SchemaVersionV1 DecodeSchemaVersion(ReadOnlySpan<byte> encoded)
    {
        var reader = new Proto.Reader(encoded);
        uint major = 0;
        uint minor = 0;
        var seen = new HashSet<int>();
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (!seen.Add(field)) throw Error("nested-schema-version-duplicate-field");
            reader.RequireWire(wire, 0);
            if (field == 1) major = reader.ReadUInt32();
            else if (field == 2) minor = reader.ReadUInt32();
            else throw Error("nested-schema-version-unknown-field");
        }
        if (major == 0 || major > ushort.MaxValue || minor > ushort.MaxValue)
            throw Error("nested-schema-version-range");
        return new SchemaVersionV1((ushort)major, (ushort)minor);
    }

    private static void RequireDepth(IDomainNestedSnapshotCodecV1 codec, int depth)
    {
        if (depth > MaxSelfRecursiveDepth)
            throw Error($"nested-self-recursive-depth:{codec.Descriptor.Schema.SchemaId.Value}");
    }

    private static InvalidDataException TypeOrRange(string schema, DomainPayloadFieldRuleV1 rule)
        => Error($"nested-field-type-or-range:{schema}:{rule.Name}:{rule.Kind}");

    private static InvalidDataException Error(string suffix)
        => new($"persistence.snapshot.{suffix}");

    private static class Proto
    {
        internal static byte[] Encode(Action<Stream> write)
        {
            using var stream = new MemoryStream();
            write(stream);
            return stream.ToArray();
        }

        internal static void WriteString(Stream stream, int field, string value)
            => WriteBytes(stream, field, Encoding.UTF8.GetBytes(value));

        internal static void WriteBytes(Stream stream, int field, ReadOnlySpan<byte> value)
        {
            WriteVarUInt64(stream, ((ulong)field << 3) | 2UL);
            WriteVarUInt64(stream, checked((ulong)value.Length));
            stream.Write(value);
        }

        internal static void WriteMessage(Stream stream, int field, byte[] value) => WriteBytes(stream, field, value);
        internal static void WriteUInt32(Stream stream, int field, uint value) { WriteVarUInt64(stream, (ulong)field << 3); WriteVarUInt64(stream, value); }
        internal static void WriteUInt64(Stream stream, int field, ulong value) { WriteVarUInt64(stream, (ulong)field << 3); WriteVarUInt64(stream, value); }
        internal static void WriteSInt32(Stream stream, int field, int value) => WriteUInt32(stream, field, ZigZag(value));
        internal static void WriteSInt64(Stream stream, int field, long value) => WriteUInt64(stream, field, ZigZag(value));
        internal static void WriteBool(Stream stream, int field, bool value) => WriteUInt32(stream, field, value ? 1u : 0u);

        private static uint ZigZag(int value) => unchecked((uint)((value << 1) ^ (value >> 31)));
        private static ulong ZigZag(long value) => unchecked((ulong)((value << 1) ^ (value >> 63)));

        private static void WriteVarUInt64(Stream stream, ulong value)
        {
            while (value >= 0x80)
            {
                stream.WriteByte((byte)((value & 0x7f) | 0x80));
                value >>= 7;
            }
            stream.WriteByte((byte)value);
        }

        internal ref struct Reader
        {
            private ReadOnlySpan<byte> _span;
            private int _offset;

            internal Reader(ReadOnlySpan<byte> span) { _span = span; _offset = 0; }
            internal bool End => _offset == _span.Length;

            internal (int Field, int Wire) ReadTag()
            {
                var tag = ReadVarUInt64();
                if (tag == 0) throw Error("nested-proto-tag-zero");
                return (checked((int)(tag >> 3)), checked((int)(tag & 7)));
            }

            internal void RequireWire(int actual, int expected)
            {
                if (actual != expected) throw Error("nested-proto-wire-type");
            }

            internal uint ReadUInt32()
            {
                var value = ReadVarUInt64();
                if (value > uint.MaxValue) throw Error("nested-proto-uint32-overflow");
                return (uint)value;
            }

            internal int ReadSInt32()
            {
                var raw = ReadUInt32();
                return unchecked((int)((raw >> 1) ^ (uint)-(int)(raw & 1)));
            }

            internal long ReadSInt64()
            {
                var raw = ReadVarUInt64();
                return unchecked((long)((raw >> 1) ^ (ulong)-(long)(raw & 1)));
            }

            internal bool ReadBool()
            {
                var value = ReadVarUInt64();
                return value switch { 0 => false, 1 => true, _ => throw Error("nested-proto-bool") };
            }

            internal string ReadString()
            {
                var bytes = ReadBytes();
                try
                {
                    var text = new UTF8Encoding(false, true).GetString(bytes);
                    if (!Encoding.UTF8.GetBytes(text).AsSpan().SequenceEqual(bytes))
                        throw Error("nested-proto-string-noncanonical");
                    return text;
                }
                catch (DecoderFallbackException ex)
                {
                    throw new InvalidDataException("persistence.snapshot.nested-proto-utf8", ex);
                }
            }

            internal byte[] ReadBytes()
            {
                var length = ReadVarUInt64();
                if (length > int.MaxValue || length > (ulong)(_span.Length - _offset))
                    throw Error("nested-proto-length");
                var result = _span.Slice(_offset, (int)length).ToArray();
                _offset += (int)length;
                return result;
            }

            internal ulong ReadVarUInt64()
            {
                ulong value = 0;
                var shift = 0;
                for (var index = 0; index < 10; index++)
                {
                    if (_offset >= _span.Length) throw Error("nested-proto-truncated-varint");
                    var current = _span[_offset++];
                    if (index == 9 && current > 1) throw Error("nested-proto-varint-overflow");
                    value |= (ulong)(current & 0x7f) << shift;
                    if ((current & 0x80) == 0)
                    {
                        if (index > 0 && current == 0) throw Error("nested-proto-noncanonical-varint");
                        return value;
                    }
                    shift += 7;
                }
                throw Error("nested-proto-varint-overflow");
            }
        }
    }
}
