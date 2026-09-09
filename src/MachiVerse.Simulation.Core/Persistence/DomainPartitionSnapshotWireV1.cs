using System.Collections.ObjectModel;
using System.Text;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed record DomainRecordSnapshotMaterialV1(
    OpaqueId128 RecordId,
    SchemaRefV1 RecordSchema,
    ulong Revision,
    ulong CreatedStep,
    ulong? RetiredStep,
    DetailLevelV1 DetailLevel,
    OpaqueId128? LineageRef,
    IReadOnlyDictionary<string, object?> Payload);

public sealed record DomainPartitionSnapshotFragmentDecodedV1(
    PartitionStateHeaderV1 Header,
    IReadOnlyList<DomainRecordSnapshotMaterialV1> Records);

/// <summary>
/// Exact P4-04/INT-03 protobuf wire codec for standard Domain partition fragments.
/// Top-level payload field numbers are the 1-based P4-05 descriptor ordinal.
/// Unknown fields, duplicate singular fields, type substitutions, and non-canonical
/// top-level payload field order fail closed. Nested values remain unavailable until
/// their explicit owner codec is registered.
/// </summary>
public static class DomainPartitionSnapshotWireCodecV1
{
    public static byte[] EncodeHeader(PartitionStateHeaderV1 header)
    {
        ArgumentNullException.ThrowIfNull(header);
        var identity = StandardDomainPartitionRegistry.Get(header.PartitionId.Value);
        if (header.OwnerDomain != identity.OwnerDomain || header.Schema != identity.PartitionSchema)
            throw WireError($"partition-header-identity-mismatch:{header.PartitionId.Value}");
        if (header.CanonicalDigest.Length != 32)
            throw WireError("partition-header-digest-length");

        return Proto.Encode(stream =>
        {
            Proto.WriteString(stream, 1, header.PartitionId.Value);
            Proto.WriteString(stream, 2, header.OwnerDomain.Value);
            Proto.WriteString(stream, 3, header.Schema.SchemaId.Value);
            Proto.WriteMessage(stream, 4, EncodeSchemaVersion(header.Schema.Version));
            Proto.WriteUInt64(stream, 5, header.Revision);
            if (header.BasisStep != 0) Proto.WriteUInt64(stream, 6, header.BasisStep);
            if ((byte)header.DetailLevel != 0) Proto.WriteUInt32(stream, 7, (byte)header.DetailLevel);
            if (header.ItemCount != 0) Proto.WriteUInt64(stream, 8, header.ItemCount);
            Proto.WriteBytes(stream, 9, header.CanonicalDigest);
        });
    }

    public static PartitionStateHeaderV1 DecodeHeader(ReadOnlySpan<byte> encoded)
    {
        var reader = new Proto.Reader(encoded);
        string? partitionId = null;
        string? owner = null;
        string? schemaId = null;
        SchemaVersionV1? schemaVersion = null;
        ulong revision = 0;
        ulong basisStep = 0;
        uint detail = 0;
        ulong itemCount = 0;
        byte[]? digest = null;
        var seen = new HashSet<int>();

        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (!seen.Add(field)) throw WireError("partition-header-duplicate-field");
            switch (field)
            {
                case 1: reader.RequireWire(wire, 2); partitionId = reader.ReadString(); break;
                case 2: reader.RequireWire(wire, 2); owner = reader.ReadString(); break;
                case 3: reader.RequireWire(wire, 2); schemaId = reader.ReadString(); break;
                case 4: reader.RequireWire(wire, 2); schemaVersion = DecodeSchemaVersion(reader.ReadBytes()); break;
                case 5: reader.RequireWire(wire, 0); revision = reader.ReadVarUInt64(); break;
                case 6: reader.RequireWire(wire, 0); basisStep = reader.ReadVarUInt64(); break;
                case 7: reader.RequireWire(wire, 0); detail = reader.ReadUInt32(); break;
                case 8: reader.RequireWire(wire, 0); itemCount = reader.ReadVarUInt64(); break;
                case 9: reader.RequireWire(wire, 2); digest = reader.ReadBytes(); break;
                default: throw WireError("partition-header-unknown-field");
            }
        }

        if (partitionId is null || owner is null || schemaId is null || schemaVersion is null || revision == 0 || digest is null)
            throw WireError("partition-header-required-field");
        if (detail > 3) throw WireError("partition-header-detail-level");
        if (digest.Length != 32) throw WireError("partition-header-digest-length");

        var identity = StandardDomainPartitionRegistry.Get(new StableToken(partitionId).Value);
        if (new StableToken(owner) != identity.OwnerDomain ||
            new StableToken(schemaId) != identity.PartitionSchema.SchemaId ||
            schemaVersion.Value != identity.PartitionSchema.Version)
            throw WireError("partition-header-identity-mismatch");

        return new PartitionStateHeaderV1(
            identity,
            revision,
            basisStep,
            (DetailLevelV1)detail,
            itemCount,
            digest);
    }

    public static byte[] EncodePayload(string partitionId, IReadOnlyDictionary<string, object?> payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var descriptor = StandardDomainPayloadSchemaRegistry.Get(partitionId);
        new StandardDomainPayloadValidatorV1().Validate(partitionId, payload);

        return Proto.Encode(stream =>
        {
            for (var index = 0; index < descriptor.Fields.Count; index++)
            {
                var rule = descriptor.Fields[index];
                if (!payload.TryGetValue(rule.Name, out var value) || value is null)
                {
                    if (!rule.Optional) throw WireError($"payload-required-field:{partitionId}:{rule.Name}");
                    continue;
                }

                var fieldNumber = checked((uint)index + 1u);
                var entry = Proto.Encode(entryStream =>
                {
                    Proto.WriteUInt32(entryStream, 1, fieldNumber);
                    Proto.WriteMessage(entryStream, 2, EncodeValue(partitionId, rule, value));
                });
                Proto.WriteMessage(stream, 1, entry);
            }
        });
    }

    public static IReadOnlyDictionary<string, object?> DecodePayload(string partitionId, ReadOnlySpan<byte> encoded)
    {
        var descriptor = StandardDomainPayloadSchemaRegistry.Get(partitionId);
        var reader = new Proto.Reader(encoded);
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        uint previousField = 0;

        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (field != 1) throw WireError($"payload-unknown-field:{partitionId}");
            reader.RequireWire(wire, 2);
            var entryReader = new Proto.Reader(reader.ReadBytes());
            uint fieldNumber = 0;
            byte[]? valueBytes = null;
            var seen = new HashSet<int>();

            while (!entryReader.End)
            {
                var (entryField, entryWire) = entryReader.ReadTag();
                if (!seen.Add(entryField)) throw WireError($"payload-entry-duplicate-field:{partitionId}");
                switch (entryField)
                {
                    case 1: entryReader.RequireWire(entryWire, 0); fieldNumber = entryReader.ReadUInt32(); break;
                    case 2: entryReader.RequireWire(entryWire, 2); valueBytes = entryReader.ReadBytes(); break;
                    default: throw WireError($"payload-entry-unknown-field:{partitionId}");
                }
            }

            if (fieldNumber == 0 || fieldNumber > descriptor.Fields.Count || valueBytes is null)
                throw WireError($"payload-entry-invalid:{partitionId}");
            if (fieldNumber <= previousField)
                throw WireError($"payload-field-order:{partitionId}");
            previousField = fieldNumber;

            var rule = descriptor.Fields[checked((int)fieldNumber - 1)];
            values.Add(rule.Name, DecodeValue(partitionId, rule, valueBytes));
        }

        foreach (var rule in descriptor.Fields)
        {
            if (!rule.Optional && !values.ContainsKey(rule.Name))
                throw WireError($"payload-required-field:{partitionId}:{rule.Name}");
        }

        var result = new ReadOnlyDictionary<string, object?>(values);
        new StandardDomainPayloadValidatorV1().Validate(partitionId, result);
        return result;
    }

    public static byte[] EncodeRecord<TPayload>(
        string partitionId,
        DomainRecordEnvelopeV1<TPayload> record,
        Func<TPayload, IReadOnlyDictionary<string, object?>> toStandardPayload)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(toStandardPayload);
        var identity = StandardDomainPartitionRegistry.Get(partitionId);
        if (record.RecordSchema != identity.RecordSchema)
            throw WireError($"record-schema-mismatch:{partitionId}");

        var payload = EncodePayload(partitionId, toStandardPayload(record.Payload));
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
            Proto.WriteMessage(stream, 9, payload);
        });
    }

    public static DomainRecordSnapshotMaterialV1 DecodeRecord(
        string partitionId,
        ReadOnlySpan<byte> encoded,
        ulong partitionBasisStep)
    {
        var identity = StandardDomainPartitionRegistry.Get(partitionId);
        var reader = new Proto.Reader(encoded);
        byte[]? recordIdBytes = null;
        string? schemaId = null;
        SchemaVersionV1? schemaVersion = null;
        ulong revision = 0;
        ulong createdStep = 0;
        ulong? retiredStep = null;
        uint detail = 0;
        byte[]? lineageBytes = null;
        byte[]? payloadBytes = null;
        var seen = new HashSet<int>();

        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (!seen.Add(field)) throw WireError($"record-duplicate-field:{partitionId}");
            switch (field)
            {
                case 1: reader.RequireWire(wire, 2); recordIdBytes = reader.ReadBytes(); break;
                case 2: reader.RequireWire(wire, 2); schemaId = reader.ReadString(); break;
                case 3: reader.RequireWire(wire, 2); schemaVersion = DecodeSchemaVersion(reader.ReadBytes()); break;
                case 4: reader.RequireWire(wire, 0); revision = reader.ReadVarUInt64(); break;
                case 5: reader.RequireWire(wire, 0); createdStep = reader.ReadVarUInt64(); break;
                case 6: reader.RequireWire(wire, 0); retiredStep = reader.ReadVarUInt64(); break;
                case 7: reader.RequireWire(wire, 0); detail = reader.ReadUInt32(); break;
                case 8: reader.RequireWire(wire, 2); lineageBytes = reader.ReadBytes(); break;
                case 9: reader.RequireWire(wire, 2); payloadBytes = reader.ReadBytes(); break;
                default: throw WireError($"record-unknown-field:{partitionId}");
            }
        }

        if (recordIdBytes is null || schemaId is null || schemaVersion is null || revision == 0 || payloadBytes is null)
            throw WireError($"record-required-field:{partitionId}");
        if (recordIdBytes.Length != 16 || detail > 3)
            throw WireError($"record-shape:{partitionId}");

        var recordId = OpaqueId128.FromBytes(recordIdBytes);
        if (recordId.IsZero) throw WireError($"record-id-zero:{partitionId}");
        if (new StableToken(schemaId) != identity.RecordSchema.SchemaId || schemaVersion.Value != identity.RecordSchema.Version)
            throw WireError($"record-schema-mismatch:{partitionId}");
        if (createdStep > partitionBasisStep || retiredStep is { } retired && retired > partitionBasisStep)
            throw WireError($"record-step-after-basis:{partitionId}");
        if (retiredStep is { } retiredBefore && retiredBefore < createdStep)
            throw WireError($"record-retired-before-created:{partitionId}");

        OpaqueId128? lineage = null;
        if (lineageBytes is not null)
        {
            if (lineageBytes.Length != 16) throw WireError($"record-lineage-length:{partitionId}");
            var parsed = OpaqueId128.FromBytes(lineageBytes);
            if (parsed.IsZero) throw WireError($"record-lineage-zero:{partitionId}");
            lineage = parsed;
        }

        return new DomainRecordSnapshotMaterialV1(
            recordId,
            identity.RecordSchema,
            revision,
            createdStep,
            retiredStep,
            (DetailLevelV1)detail,
            lineage,
            DecodePayload(partitionId, payloadBytes));
    }

    public static byte[] EncodeFragment<TPayload>(
        DomainPartitionSnapshotAuthorityV1<TPayload> authority,
        IReadOnlyList<DomainRecordEnvelopeV1<TPayload>> records,
        Func<TPayload, IReadOnlyDictionary<string, object?>> toStandardPayload)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(toStandardPayload);
        authority.VerifyBoundAuthority();

        OpaqueId128? previous = null;
        return Proto.Encode(stream =>
        {
            Proto.WriteMessage(stream, 1, EncodeHeader(authority.Header));
            foreach (var record in records)
            {
                if (previous is { } prior && prior.CompareTo(record.RecordId) >= 0)
                    throw WireError($"fragment-record-order:{authority.PartitionId.Value}");
                previous = record.RecordId;
                Proto.WriteMessage(stream, 2, EncodeRecord(authority.PartitionId.Value, record, toStandardPayload));
            }
        });
    }

    public static DomainPartitionSnapshotFragmentDecodedV1 DecodeFragment(
        string partitionId,
        ReadOnlySpan<byte> encoded)
    {
        var reader = new Proto.Reader(encoded);
        PartitionStateHeaderV1? header = null;
        var recordBytes = new List<byte[]>();
        var headerSeen = false;

        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            switch (field)
            {
                case 1:
                    if (headerSeen) throw WireError($"fragment-header-duplicate:{partitionId}");
                    headerSeen = true;
                    reader.RequireWire(wire, 2);
                    header = DecodeHeader(reader.ReadBytes());
                    break;
                case 2:
                    reader.RequireWire(wire, 2);
                    recordBytes.Add(reader.ReadBytes());
                    break;
                default:
                    throw WireError($"fragment-unknown-field:{partitionId}");
            }
        }

        if (header is null || !string.Equals(header.PartitionId.Value, partitionId, StringComparison.Ordinal))
            throw WireError($"fragment-header-mismatch:{partitionId}");

        var records = new List<DomainRecordSnapshotMaterialV1>(recordBytes.Count);
        OpaqueId128? previous = null;
        foreach (var bytes in recordBytes)
        {
            var record = DecodeRecord(partitionId, bytes, header.BasisStep);
            if (previous is { } prior && prior.CompareTo(record.RecordId) >= 0)
                throw WireError($"fragment-record-order:{partitionId}");
            previous = record.RecordId;
            records.Add(record);
        }

        return new DomainPartitionSnapshotFragmentDecodedV1(header, Array.AsReadOnly(records.ToArray()));
    }

    private static byte[] EncodeValue(string partitionId, DomainPayloadFieldRuleV1 rule, object value)
    {
        return Proto.Encode(stream =>
        {
            switch (rule.Kind)
            {
                case DomainPayloadFieldKindV1.Ref:
                    Proto.WriteMessage(stream, 1, EncodeRef((PartitionRecordRefV1)value));
                    break;
                case DomainPayloadFieldKindV1.RefList:
                    Proto.WriteMessage(stream, 2, EncodeRefList((IReadOnlyList<PartitionRecordRefV1>)value));
                    break;
                case DomainPayloadFieldKindV1.Id128:
                {
                    var id = (OpaqueId128)value;
                    if (id.IsZero) throw WireError($"payload-id-zero:{partitionId}:{rule.Name}");
                    Proto.WriteBytes(stream, 3, id.ToBytes());
                    break;
                }
                case DomainPayloadFieldKindV1.Token:
                    Proto.WriteString(stream, 4, new StableToken((string)value).Value);
                    break;
                case DomainPayloadFieldKindV1.TokenList:
                case DomainPayloadFieldKindV1.OrderedTokenList:
                    Proto.WriteMessage(stream, 5, EncodeTokenList((IReadOnlyList<string>)value));
                    break;
                case DomainPayloadFieldKindV1.Ratio:
                case DomainPayloadFieldKindV1.UInt32:
                    Proto.WriteUInt32(stream, 6, (uint)value);
                    break;
                case DomainPayloadFieldKindV1.UInt8:
                    Proto.WriteUInt32(stream, 6, (byte)value);
                    break;
                case DomainPayloadFieldKindV1.UInt16:
                    Proto.WriteUInt32(stream, 6, (ushort)value);
                    break;
                case DomainPayloadFieldKindV1.Step:
                case DomainPayloadFieldKindV1.UInt64:
                    Proto.WriteUInt64(stream, 7, (ulong)value);
                    break;
                case DomainPayloadFieldKindV1.Int32:
                case DomainPayloadFieldKindV1.Temperature:
                case DomainPayloadFieldKindV1.Pressure:
                    Proto.WriteSInt32(stream, 8, (int)value);
                    break;
                case DomainPayloadFieldKindV1.Int64:
                case DomainPayloadFieldKindV1.Length:
                case DomainPayloadFieldKindV1.Mass:
                case DomainPayloadFieldKindV1.Volume:
                case DomainPayloadFieldKindV1.Power:
                case DomainPayloadFieldKindV1.Energy:
                case DomainPayloadFieldKindV1.Money:
                    Proto.WriteSInt64(stream, 9, (long)value);
                    break;
                case DomainPayloadFieldKindV1.Bool:
                    Proto.WriteBool(stream, 10, (bool)value);
                    break;
                case DomainPayloadFieldKindV1.Digest:
                {
                    var digest = (byte[])value;
                    if (digest.Length != 32) throw WireError($"payload-digest-length:{partitionId}:{rule.Name}");
                    Proto.WriteBytes(stream, 11, digest);
                    break;
                }
                case DomainPayloadFieldKindV1.Vec3:
                    Proto.WriteMessage(stream, 12, EncodeVec3((Vec3Int64V1)value));
                    break;
                case DomainPayloadFieldKindV1.Quat:
                    Proto.WriteMessage(stream, 13, EncodeQuat((QuaternionQ30V1)value));
                    break;
                case DomainPayloadFieldKindV1.OrderedTokenUInt8Map:
                    Proto.WriteMessage(stream, 14, EncodeMap((IReadOnlyList<KeyValuePair<string, byte>>)value, static (s, v) => Proto.WriteUInt32(s, 2, v)));
                    break;
                case DomainPayloadFieldKindV1.OrderedTokenUInt32Map:
                    Proto.WriteMessage(stream, 15, EncodeMap((IReadOnlyList<KeyValuePair<string, uint>>)value, static (s, v) => Proto.WriteUInt32(s, 2, v)));
                    break;
                case DomainPayloadFieldKindV1.OrderedTokenInt32Map:
                    Proto.WriteMessage(stream, 16, EncodeMap((IReadOnlyList<KeyValuePair<string, int>>)value, static (s, v) => Proto.WriteSInt32(s, 2, v)));
                    break;
                case DomainPayloadFieldKindV1.OrderedNestedList:
                case DomainPayloadFieldKindV1.RuleAst:
                    throw WireError($"nested-codec-unavailable:{partitionId}:{rule.Name}");
                default:
                    throw new InvalidOperationException($"Unhandled payload kind: {rule.Kind}");
            }
        });
    }

    private static object DecodeValue(string partitionId, DomainPayloadFieldRuleV1 rule, ReadOnlySpan<byte> encoded)
    {
        var reader = new Proto.Reader(encoded);
        object? result = null;
        var arm = 0;

        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (arm != 0) throw WireError($"payload-value-multiple-arms:{partitionId}:{rule.Name}");
            arm = field;
            switch (field)
            {
                case 1:
                    reader.RequireWire(wire, 2);
                    result = DecodeRef(reader.ReadBytes());
                    break;
                case 2:
                    reader.RequireWire(wire, 2);
                    result = DecodeRefList(reader.ReadBytes());
                    break;
                case 3:
                {
                    reader.RequireWire(wire, 2);
                    var bytes = reader.ReadBytes();
                    if (bytes.Length != 16) throw WireError($"payload-id-length:{partitionId}:{rule.Name}");
                    var id = OpaqueId128.FromBytes(bytes);
                    if (id.IsZero) throw WireError($"payload-id-zero:{partitionId}:{rule.Name}");
                    result = id;
                    break;
                }
                case 4:
                    reader.RequireWire(wire, 2);
                    result = new StableToken(reader.ReadString()).Value;
                    break;
                case 5:
                    reader.RequireWire(wire, 2);
                    result = DecodeTokenList(reader.ReadBytes());
                    break;
                case 6:
                    reader.RequireWire(wire, 0);
                    result = reader.ReadUInt32();
                    break;
                case 7:
                    reader.RequireWire(wire, 0);
                    result = reader.ReadVarUInt64();
                    break;
                case 8:
                    reader.RequireWire(wire, 0);
                    result = reader.ReadSInt32();
                    break;
                case 9:
                    reader.RequireWire(wire, 0);
                    result = reader.ReadSInt64();
                    break;
                case 10:
                    reader.RequireWire(wire, 0);
                    result = reader.ReadBool();
                    break;
                case 11:
                {
                    reader.RequireWire(wire, 2);
                    var digest = reader.ReadBytes();
                    if (digest.Length != 32) throw WireError($"payload-digest-length:{partitionId}:{rule.Name}");
                    result = digest;
                    break;
                }
                case 12:
                    reader.RequireWire(wire, 2);
                    result = DecodeVec3(reader.ReadBytes());
                    break;
                case 13:
                    reader.RequireWire(wire, 2);
                    result = DecodeQuat(reader.ReadBytes());
                    break;
                case 14:
                    reader.RequireWire(wire, 2);
                    result = DecodeByteMap(reader.ReadBytes());
                    break;
                case 15:
                    reader.RequireWire(wire, 2);
                    result = DecodeUIntMap(reader.ReadBytes());
                    break;
                case 16:
                    reader.RequireWire(wire, 2);
                    result = DecodeIntMap(reader.ReadBytes());
                    break;
                case 17:
                case 18:
                    throw WireError($"nested-codec-unavailable:{partitionId}:{rule.Name}");
                default:
                    throw WireError($"payload-value-unknown-arm:{partitionId}:{rule.Name}");
            }
        }

        if (result is null) throw WireError($"payload-value-missing:{partitionId}:{rule.Name}");
        RequireArm(partitionId, rule, arm);
        return NormalizeDecodedScalar(rule.Kind, result, partitionId, rule.Name);
    }

    private static object NormalizeDecodedScalar(
        DomainPayloadFieldKindV1 kind,
        object value,
        string partitionId,
        string field)
    {
        return kind switch
        {
            DomainPayloadFieldKindV1.UInt8 when value is uint u && u <= byte.MaxValue => (byte)u,
            DomainPayloadFieldKindV1.UInt16 when value is uint u && u <= ushort.MaxValue => (ushort)u,
            DomainPayloadFieldKindV1.UInt8 => throw WireError($"payload-uint8-range:{partitionId}:{field}"),
            DomainPayloadFieldKindV1.UInt16 => throw WireError($"payload-uint16-range:{partitionId}:{field}"),
            _ => value,
        };
    }

    private static void RequireArm(string partitionId, DomainPayloadFieldRuleV1 rule, int arm)
    {
        var expected = rule.Kind switch
        {
            DomainPayloadFieldKindV1.Ref => 1,
            DomainPayloadFieldKindV1.RefList => 2,
            DomainPayloadFieldKindV1.Id128 => 3,
            DomainPayloadFieldKindV1.Token => 4,
            DomainPayloadFieldKindV1.TokenList or DomainPayloadFieldKindV1.OrderedTokenList => 5,
            DomainPayloadFieldKindV1.Ratio or DomainPayloadFieldKindV1.UInt8 or DomainPayloadFieldKindV1.UInt16 or DomainPayloadFieldKindV1.UInt32 => 6,
            DomainPayloadFieldKindV1.Step or DomainPayloadFieldKindV1.UInt64 => 7,
            DomainPayloadFieldKindV1.Int32 or DomainPayloadFieldKindV1.Temperature or DomainPayloadFieldKindV1.Pressure => 8,
            DomainPayloadFieldKindV1.Int64 or DomainPayloadFieldKindV1.Length or DomainPayloadFieldKindV1.Mass or DomainPayloadFieldKindV1.Volume or DomainPayloadFieldKindV1.Power or DomainPayloadFieldKindV1.Energy or DomainPayloadFieldKindV1.Money => 9,
            DomainPayloadFieldKindV1.Bool => 10,
            DomainPayloadFieldKindV1.Digest => 11,
            DomainPayloadFieldKindV1.Vec3 => 12,
            DomainPayloadFieldKindV1.Quat => 13,
            DomainPayloadFieldKindV1.OrderedTokenUInt8Map => 14,
            DomainPayloadFieldKindV1.OrderedTokenUInt32Map => 15,
            DomainPayloadFieldKindV1.OrderedTokenInt32Map => 16,
            DomainPayloadFieldKindV1.OrderedNestedList => 17,
            DomainPayloadFieldKindV1.RuleAst => 18,
            _ => throw new InvalidOperationException($"Unhandled payload kind: {rule.Kind}")
        };
        if (arm != expected) throw WireError($"payload-value-kind-mismatch:{partitionId}:{rule.Name}");
    }

    private static byte[] EncodeSchemaVersion(SchemaVersionV1 version)
    {
        return Proto.Encode(stream =>
        {
            if (version.Major != 0) Proto.WriteUInt32(stream, 1, version.Major);
            if (version.Minor != 0) Proto.WriteUInt32(stream, 2, version.Minor);
        });
    }

    private static SchemaVersionV1 DecodeSchemaVersion(ReadOnlySpan<byte> encoded)
    {
        var reader = new Proto.Reader(encoded);
        uint major = 0;
        uint minor = 0;
        var seen = new HashSet<int>();
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (!seen.Add(field)) throw WireError("schema-version-duplicate-field");
            reader.RequireWire(wire, 0);
            if (field == 1) major = reader.ReadUInt32();
            else if (field == 2) minor = reader.ReadUInt32();
            else throw WireError("schema-version-unknown-field");
        }
        if (major > ushort.MaxValue || minor > ushort.MaxValue) throw WireError("schema-version-range");
        return new SchemaVersionV1((ushort)major, (ushort)minor);
    }

    private static byte[] EncodeRef(PartitionRecordRefV1 value)
    {
        return Proto.Encode(stream =>
        {
            Proto.WriteString(stream, 1, value.PartitionId.Value);
            Proto.WriteBytes(stream, 2, value.RecordId.ToBytes());
        });
    }

    private static PartitionRecordRefV1 DecodeRef(ReadOnlySpan<byte> encoded)
    {
        var reader = new Proto.Reader(encoded);
        string? partition = null;
        byte[]? id = null;
        var seen = new HashSet<int>();
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (!seen.Add(field)) throw WireError("ref-duplicate-field");
            if (field == 1) { reader.RequireWire(wire, 2); partition = reader.ReadString(); }
            else if (field == 2) { reader.RequireWire(wire, 2); id = reader.ReadBytes(); }
            else throw WireError("ref-unknown-field");
        }
        if (partition is null || id is null || id.Length != 16) throw WireError("ref-required-field");
        var opaque = OpaqueId128.FromBytes(id);
        if (opaque.IsZero) throw WireError("ref-id-zero");
        return new PartitionRecordRefV1(new StableToken(partition), opaque);
    }

    private static byte[] EncodeRefList(IReadOnlyList<PartitionRecordRefV1> values)
    {
        return Proto.Encode(stream =>
        {
            foreach (var value in values) Proto.WriteMessage(stream, 1, EncodeRef(value));
        });
    }

    private static IReadOnlyList<PartitionRecordRefV1> DecodeRefList(ReadOnlySpan<byte> encoded)
    {
        var reader = new Proto.Reader(encoded);
        var values = new List<PartitionRecordRefV1>();
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (field != 1) throw WireError("ref-list-unknown-field");
            reader.RequireWire(wire, 2);
            values.Add(DecodeRef(reader.ReadBytes()));
        }
        return Array.AsReadOnly(values.ToArray());
    }

    private static byte[] EncodeTokenList(IReadOnlyList<string> values)
    {
        return Proto.Encode(stream =>
        {
            foreach (var value in values) Proto.WriteString(stream, 1, new StableToken(value).Value);
        });
    }

    private static IReadOnlyList<string> DecodeTokenList(ReadOnlySpan<byte> encoded)
    {
        var reader = new Proto.Reader(encoded);
        var values = new List<string>();
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (field != 1) throw WireError("token-list-unknown-field");
            reader.RequireWire(wire, 2);
            values.Add(new StableToken(reader.ReadString()).Value);
        }
        return Array.AsReadOnly(values.ToArray());
    }

    private static byte[] EncodeVec3(Vec3Int64V1 value)
    {
        return Proto.Encode(stream =>
        {
            if (value.X != 0) Proto.WriteSInt64(stream, 1, value.X);
            if (value.Y != 0) Proto.WriteSInt64(stream, 2, value.Y);
            if (value.Z != 0) Proto.WriteSInt64(stream, 3, value.Z);
        });
    }

    private static Vec3Int64V1 DecodeVec3(ReadOnlySpan<byte> encoded)
    {
        var reader = new Proto.Reader(encoded);
        long x = 0, y = 0, z = 0;
        var seen = new HashSet<int>();
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (!seen.Add(field)) throw WireError("vec3-duplicate-field");
            reader.RequireWire(wire, 0);
            if (field == 1) x = reader.ReadSInt64();
            else if (field == 2) y = reader.ReadSInt64();
            else if (field == 3) z = reader.ReadSInt64();
            else throw WireError("vec3-unknown-field");
        }
        return new Vec3Int64V1(x, y, z);
    }

    private static byte[] EncodeQuat(QuaternionQ30V1 value)
    {
        return Proto.Encode(stream =>
        {
            if (value.X != 0) Proto.WriteSInt32(stream, 1, value.X);
            if (value.Y != 0) Proto.WriteSInt32(stream, 2, value.Y);
            if (value.Z != 0) Proto.WriteSInt32(stream, 3, value.Z);
            if (value.W != 0) Proto.WriteSInt32(stream, 4, value.W);
        });
    }

    private static QuaternionQ30V1 DecodeQuat(ReadOnlySpan<byte> encoded)
    {
        var reader = new Proto.Reader(encoded);
        int x = 0, y = 0, z = 0, w = 0;
        var seen = new HashSet<int>();
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (!seen.Add(field)) throw WireError("quat-duplicate-field");
            reader.RequireWire(wire, 0);
            if (field == 1) x = reader.ReadSInt32();
            else if (field == 2) y = reader.ReadSInt32();
            else if (field == 3) z = reader.ReadSInt32();
            else if (field == 4) w = reader.ReadSInt32();
            else throw WireError("quat-unknown-field");
        }
        return new QuaternionQ30V1(x, y, z, w);
    }

    private static byte[] EncodeMap<T>(
        IReadOnlyList<KeyValuePair<string, T>> values,
        Action<Stream, T> writeValue)
    {
        return Proto.Encode(stream =>
        {
            string? previous = null;
            foreach (var pair in values)
            {
                var key = new StableToken(pair.Key).Value;
                if (previous is not null && string.CompareOrdinal(previous, key) >= 0)
                    throw WireError("map-key-order");
                previous = key;
                var entry = Proto.Encode(entryStream =>
                {
                    Proto.WriteString(entryStream, 1, key);
                    writeValue(entryStream, pair.Value);
                });
                Proto.WriteMessage(stream, 1, entry);
            }
        });
    }

    private static IReadOnlyList<KeyValuePair<string, byte>> DecodeByteMap(ReadOnlySpan<byte> encoded)
    {
        return DecodeMap(encoded, static reader =>
        {
            var value = reader.ReadUInt32();
            if (value > byte.MaxValue) throw WireError("map-byte-range");
            return (byte)value;
        });
    }

    private static IReadOnlyList<KeyValuePair<string, uint>> DecodeUIntMap(ReadOnlySpan<byte> encoded)
        => DecodeMap(encoded, static reader => reader.ReadUInt32());

    private static IReadOnlyList<KeyValuePair<string, int>> DecodeIntMap(ReadOnlySpan<byte> encoded)
        => DecodeMap(encoded, static reader => reader.ReadSInt32());

    private static IReadOnlyList<KeyValuePair<string, T>> DecodeMap<T>(
        ReadOnlySpan<byte> encoded,
        Func<Proto.ReaderBox, T> readValue)
    {
        var reader = new Proto.Reader(encoded);
        var values = new List<KeyValuePair<string, T>>();
        string? previous = null;

        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (field != 1) throw WireError("map-unknown-field");
            reader.RequireWire(wire, 2);
            var box = new Proto.ReaderBox(reader.ReadBytes());
            string? key = null;
            T value = default!;
            var valueSeen = false;
            var seen = new HashSet<int>();

            while (!box.End)
            {
                var (entryField, entryWire) = box.ReadTag();
                if (!seen.Add(entryField)) throw WireError("map-entry-duplicate-field");
                if (entryField == 1)
                {
                    box.RequireWire(entryWire, 2);
                    key = new StableToken(box.ReadString()).Value;
                }
                else if (entryField == 2)
                {
                    box.RequireWire(entryWire, 0);
                    value = readValue(box);
                    valueSeen = true;
                }
                else
                {
                    throw WireError("map-entry-unknown-field");
                }
            }

            if (key is null || !valueSeen) throw WireError("map-entry-required-field");
            if (previous is not null && string.CompareOrdinal(previous, key) >= 0)
                throw WireError("map-key-order");
            previous = key;
            values.Add(new KeyValuePair<string, T>(key, value));
        }

        return Array.AsReadOnly(values.ToArray());
    }

    private static InvalidDataException WireError(string suffix)
        => new($"persistence.snapshot.domain-wire:{suffix}");

    private static class Proto
    {
        public static byte[] Encode(Action<Stream> write)
        {
            using var stream = new MemoryStream();
            write(stream);
            return stream.ToArray();
        }

        public static void WriteMessage(Stream stream, int field, ReadOnlySpan<byte> value)
            => WriteBytes(stream, field, value);

        public static void WriteString(Stream stream, int field, string value)
            => WriteBytes(stream, field, Encoding.UTF8.GetBytes(value));

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

        public static void WriteBool(Stream stream, int field, bool value)
        {
            WriteTag(stream, field, 0);
            WriteVarUInt64(stream, value ? 1UL : 0UL);
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

        private static uint ZigZag32(int value)
            => unchecked((uint)((value << 1) ^ (value >> 31)));

        private static ulong ZigZag64(long value)
            => unchecked((ulong)((value << 1) ^ (value >> 63)));

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
                for (var shift = 0; shift < 64; shift += 7)
                {
                    if (_remaining.IsEmpty) throw WireError("truncated-varint");
                    var current = _remaining[0];
                    _remaining = _remaining[1..];
                    value |= (ulong)(current & 0x7f) << shift;
                    if ((current & 0x80) == 0) return value;
                }
                throw WireError("varint-overflow");
            }

            public uint ReadUInt32()
            {
                var value = ReadVarUInt64();
                if (value > uint.MaxValue) throw WireError("uint32-overflow");
                return (uint)value;
            }

            public int ReadSInt32()
            {
                var value = ReadUInt32();
                return unchecked((int)(value >> 1) ^ -((int)value & 1));
            }

            public long ReadSInt64()
            {
                var value = ReadVarUInt64();
                return unchecked((long)(value >> 1) ^ -((long)value & 1L));
            }

            public bool ReadBool()
            {
                var value = ReadVarUInt64();
                if (value > 1) throw WireError("bool-range");
                return value == 1;
            }

            public byte[] ReadBytes()
            {
                var length = ReadVarUInt64();
                if (length > int.MaxValue || (ulong)_remaining.Length < length)
                    throw WireError("truncated-bytes");
                var result = _remaining[..(int)length].ToArray();
                _remaining = _remaining[(int)length..];
                return result;
            }

            public string ReadString()
            {
                var bytes = ReadBytes();
                try
                {
                    return new UTF8Encoding(false, true).GetString(bytes);
                }
                catch (DecoderFallbackException ex)
                {
                    throw new InvalidDataException("persistence.snapshot.domain-wire:utf8", ex);
                }
            }
        }

        public sealed class ReaderBox
        {
            private readonly byte[] _bytes;
            private int _offset;

            public ReaderBox(byte[] encoded) => _bytes = encoded;
            public bool End => _offset >= _bytes.Length;

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
                for (var shift = 0; shift < 64; shift += 7)
                {
                    if (_offset >= _bytes.Length) throw WireError("truncated-varint");
                    var current = _bytes[_offset++];
                    value |= (ulong)(current & 0x7f) << shift;
                    if ((current & 0x80) == 0) return value;
                }
                throw WireError("varint-overflow");
            }

            public uint ReadUInt32()
            {
                var value = ReadVarUInt64();
                if (value > uint.MaxValue) throw WireError("uint32-overflow");
                return (uint)value;
            }

            public int ReadSInt32()
            {
                var value = ReadUInt32();
                return unchecked((int)(value >> 1) ^ -((int)value & 1));
            }

            public byte[] ReadBytes()
            {
                var length = ReadVarUInt64();
                if (length > int.MaxValue || _bytes.Length - _offset < (int)length)
                    throw WireError("truncated-bytes");
                var result = _bytes.AsSpan(_offset, (int)length).ToArray();
                _offset += (int)length;
                return result;
            }

            public string ReadString()
            {
                var bytes = ReadBytes();
                try
                {
                    return new UTF8Encoding(false, true).GetString(bytes);
                }
                catch (DecoderFallbackException ex)
                {
                    throw new InvalidDataException("persistence.snapshot.domain-wire:utf8", ex);
                }
            }
        }
    }
}
