using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

/// <summary>
/// Normative semantic digest for the 97 standard P4-05 Domain record payload schemas.
/// The digest is independent of protobuf bytes and CLR object layout.
/// </summary>
public static class StandardDomainPayloadCanonicalDigestV1
{
    public const string HashDomain = "mv.domain-payload.v1";

    public static byte[] Compute(
        string partitionId,
        IReadOnlyDictionary<string, object?> payload,
        DomainNestedSnapshotCodecRegistryV1? nestedCodecs = null,
        IDomainRecordReferenceResolverV1? references = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var descriptor = StandardDomainPayloadSchemaRegistry.Get(partitionId);
        var identity = StandardDomainPartitionRegistry.Get(partitionId);
        if (descriptor.RecordSchema != identity.RecordSchema)
            throw new InvalidDataException($"domain.payload.digest-schema-mismatch:{partitionId}");

        new StandardDomainPayloadValidatorV1().Validate(partitionId, payload, references);
        var registry = nestedCodecs ?? StandardDomainNestedSnapshotCodecRegistryV1.Default;

        return HashSuite.DomainHash(HashDomain, writer =>
        {
            writer.WriteMapStart(5);
            writer.WriteUnsigned(0); writer.WriteAsciiText(identity.PartitionId.Value);
            writer.WriteUnsigned(1); writer.WriteAsciiText(identity.RecordSchema.SchemaId.Value);
            writer.WriteUnsigned(2); writer.WriteUnsigned(identity.RecordSchema.Version.Major);
            writer.WriteUnsigned(3); writer.WriteUnsigned(identity.RecordSchema.Version.Minor);
            writer.WriteUnsigned(4);
            WriteTopLevelFields(writer, identity.PartitionId.Value, descriptor.Fields, payload, registry, references);
        });
    }

    private static void WriteTopLevelFields(
        MvDcborWriter writer,
        string partitionId,
        IReadOnlyList<DomainPayloadFieldRuleV1> descriptorFields,
        IReadOnlyDictionary<string, object?> values,
        DomainNestedSnapshotCodecRegistryV1 nestedCodecs,
        IDomainRecordReferenceResolverV1? references)
    {
        var present = PresentFields(descriptorFields, values, partitionId);
        writer.WriteArrayStart(checked((ulong)present.Count));
        foreach (var entry in present)
        {
            writer.WriteArrayStart(2);
            writer.WriteUnsigned(entry.FieldNumber);
            WriteValue(writer, partitionId, entry.Rule, entry.Value, nestedCodecs, references);
        }
    }

    private static void WriteNestedFields(
        MvDcborWriter writer,
        string parentPartitionId,
        string parentFieldName,
        DomainNestedSnapshotSchemaDescriptorV1 descriptor,
        IReadOnlyDictionary<string, object?> values,
        DomainNestedSnapshotCodecRegistryV1 nestedCodecs,
        IDomainRecordReferenceResolverV1? references)
    {
        var present = PresentFields(descriptor.Fields, values, $"{parentPartitionId}:{parentFieldName}:{descriptor.Schema.SchemaId.Value}");
        writer.WriteArrayStart(checked((ulong)present.Count));
        foreach (var entry in present)
        {
            ValidateNestedField(parentPartitionId, parentFieldName, entry.Rule, entry.Value, references);
            writer.WriteArrayStart(2);
            writer.WriteUnsigned(entry.FieldNumber);
            WriteValue(writer, parentPartitionId, entry.Rule, entry.Value, nestedCodecs, references);
        }
    }

    private static IReadOnlyList<PresentField> PresentFields(
        IReadOnlyList<DomainPayloadFieldRuleV1> descriptorFields,
        IReadOnlyDictionary<string, object?> values,
        string context)
    {
        var known = descriptorFields.Select(static field => field.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var name in values.Keys)
        {
            if (!known.Contains(name))
                throw new InvalidDataException($"domain.payload.digest-unknown-field:{context}:{name}");
        }

        var present = new List<PresentField>(descriptorFields.Count);
        for (var index = 0; index < descriptorFields.Count; index++)
        {
            var rule = descriptorFields[index];
            if (!values.TryGetValue(rule.Name, out var value) || value is null)
            {
                if (!rule.Optional)
                    throw new InvalidDataException($"domain.payload.digest-required-field:{context}:{rule.Name}");
                continue;
            }
            present.Add(new PresentField(checked((ulong)index + 1UL), rule, value));
        }
        return present;
    }

    private static void WriteValue(
        MvDcborWriter writer,
        string partitionId,
        DomainPayloadFieldRuleV1 rule,
        object value,
        DomainNestedSnapshotCodecRegistryV1 nestedCodecs,
        IDomainRecordReferenceResolverV1? references)
    {
        switch (rule.Kind)
        {
            case DomainPayloadFieldKindV1.Ref:
                WriteReference(writer, Require<PartitionRecordRefV1>(value, partitionId, rule.Name), references, partitionId, rule.Name);
                return;
            case DomainPayloadFieldKindV1.RefList:
            {
                var refs = Require<IReadOnlyList<PartitionRecordRefV1>>(value, partitionId, rule.Name);
                writer.WriteArrayStart(checked((ulong)refs.Count));
                foreach (var reference in refs)
                    WriteReference(writer, reference, references, partitionId, rule.Name);
                return;
            }
            case DomainPayloadFieldKindV1.Id128:
            {
                var id = Require<OpaqueId128>(value, partitionId, rule.Name);
                if (id.IsZero) ThrowRange(partitionId, rule.Name);
                writer.WriteBytes(id.ToBytes());
                return;
            }
            case DomainPayloadFieldKindV1.Token:
                writer.WriteAsciiText(new StableToken(Require<string>(value, partitionId, rule.Name)).Value);
                return;
            case DomainPayloadFieldKindV1.TokenList:
            case DomainPayloadFieldKindV1.OrderedTokenList:
            {
                var tokens = Require<IReadOnlyList<string>>(value, partitionId, rule.Name);
                writer.WriteArrayStart(checked((ulong)tokens.Count));
                foreach (var token in tokens) writer.WriteAsciiText(new StableToken(token).Value);
                return;
            }
            case DomainPayloadFieldKindV1.Ratio:
            case DomainPayloadFieldKindV1.UInt32:
                writer.WriteUnsigned(Require<uint>(value, partitionId, rule.Name));
                return;
            case DomainPayloadFieldKindV1.UInt8:
                writer.WriteUnsigned(Require<byte>(value, partitionId, rule.Name));
                return;
            case DomainPayloadFieldKindV1.UInt16:
                writer.WriteUnsigned(Require<ushort>(value, partitionId, rule.Name));
                return;
            case DomainPayloadFieldKindV1.Step:
            case DomainPayloadFieldKindV1.UInt64:
                writer.WriteUnsigned(Require<ulong>(value, partitionId, rule.Name));
                return;
            case DomainPayloadFieldKindV1.Int32:
            case DomainPayloadFieldKindV1.Temperature:
            case DomainPayloadFieldKindV1.Pressure:
                writer.WriteInt64(Require<int>(value, partitionId, rule.Name));
                return;
            case DomainPayloadFieldKindV1.Int64:
            case DomainPayloadFieldKindV1.Length:
            case DomainPayloadFieldKindV1.Mass:
            case DomainPayloadFieldKindV1.Volume:
            case DomainPayloadFieldKindV1.Power:
            case DomainPayloadFieldKindV1.Energy:
            case DomainPayloadFieldKindV1.Money:
                writer.WriteInt64(Require<long>(value, partitionId, rule.Name));
                return;
            case DomainPayloadFieldKindV1.Bool:
                writer.WriteBoolean(Require<bool>(value, partitionId, rule.Name));
                return;
            case DomainPayloadFieldKindV1.Digest:
            {
                var digest = Require<byte[]>(value, partitionId, rule.Name);
                if (digest.Length != 32) ThrowRange(partitionId, rule.Name);
                writer.WriteBytes(digest);
                return;
            }
            case DomainPayloadFieldKindV1.Vec3:
            {
                var vector = Require<Vec3Int64V1>(value, partitionId, rule.Name);
                writer.WriteArrayStart(3);
                writer.WriteInt64(vector.X);
                writer.WriteInt64(vector.Y);
                writer.WriteInt64(vector.Z);
                return;
            }
            case DomainPayloadFieldKindV1.Quat:
            {
                var quaternion = Require<QuaternionQ30V1>(value, partitionId, rule.Name);
                if (quaternion.X == 0 && quaternion.Y == 0 && quaternion.Z == 0 && quaternion.W == 0)
                    ThrowRange(partitionId, rule.Name);
                writer.WriteArrayStart(4);
                writer.WriteInt64(quaternion.X);
                writer.WriteInt64(quaternion.Y);
                writer.WriteInt64(quaternion.Z);
                writer.WriteInt64(quaternion.W);
                return;
            }
            case DomainPayloadFieldKindV1.OrderedTokenUInt8Map:
                WriteOrderedMap(writer, Require<IReadOnlyList<KeyValuePair<string, byte>>>(value, partitionId, rule.Name), static (w, item) => w.WriteUnsigned(item));
                return;
            case DomainPayloadFieldKindV1.OrderedTokenUInt32Map:
                WriteOrderedMap(writer, Require<IReadOnlyList<KeyValuePair<string, uint>>>(value, partitionId, rule.Name), static (w, item) => w.WriteUnsigned(item));
                return;
            case DomainPayloadFieldKindV1.OrderedTokenInt32Map:
                WriteOrderedMap(writer, Require<IReadOnlyList<KeyValuePair<string, int>>>(value, partitionId, rule.Name), static (w, item) => w.WriteInt64(item));
                return;
            case DomainPayloadFieldKindV1.OrderedNestedList:
            {
                var nested = Require<IReadOnlyList<ICanonicalDomainNestedValueV1>>(value, partitionId, rule.Name);
                nestedCodecs.ValidateOrderedList(partitionId, rule.Name, nested);
                writer.WriteArrayStart(checked((ulong)nested.Count));
                foreach (var item in nested)
                    WriteNestedValue(writer, partitionId, rule.Name, item, nestedCodecs, references);
                return;
            }
            case DomainPayloadFieldKindV1.RuleAst:
                WriteNestedValue(
                    writer,
                    partitionId,
                    rule.Name,
                    Require<ICanonicalDomainNestedValueV1>(value, partitionId, rule.Name),
                    nestedCodecs,
                    references);
                return;
            default:
                throw new InvalidOperationException($"Unhandled standard domain payload kind: {rule.Kind}");
        }
    }

    private static void WriteNestedValue(
        MvDcborWriter writer,
        string parentPartitionId,
        string parentFieldName,
        ICanonicalDomainNestedValueV1 value,
        DomainNestedSnapshotCodecRegistryV1 nestedCodecs,
        IDomainRecordReferenceResolverV1? references)
    {
        ArgumentNullException.ThrowIfNull(value);
        var codec = nestedCodecs.GetForBinding(parentPartitionId, parentFieldName);
        if (!codec.CanEncode(value))
            throw new InvalidDataException($"domain.payload.digest-nested-type:{parentPartitionId}:{parentFieldName}");
        value.ValidateCanonical();
        var fields = codec.ToStandardFields(value)
            ?? throw new InvalidDataException($"domain.payload.digest-nested-fields-null:{parentPartitionId}:{parentFieldName}");
        var descriptor = codec.Descriptor;

        writer.WriteMapStart(4);
        writer.WriteUnsigned(0); writer.WriteAsciiText(descriptor.Schema.SchemaId.Value);
        writer.WriteUnsigned(1); writer.WriteUnsigned(descriptor.Schema.Version.Major);
        writer.WriteUnsigned(2); writer.WriteUnsigned(descriptor.Schema.Version.Minor);
        writer.WriteUnsigned(3);
        WriteNestedFields(writer, parentPartitionId, parentFieldName, descriptor, fields, nestedCodecs, references);
    }

    private static void ValidateNestedField(
        string partitionId,
        string parentFieldName,
        DomainPayloadFieldRuleV1 rule,
        object value,
        IDomainRecordReferenceResolverV1? references)
    {
        switch (rule.Kind)
        {
            case DomainPayloadFieldKindV1.Ref:
                ValidateReference(Require<PartitionRecordRefV1>(value, partitionId, rule.Name), references, partitionId, parentFieldName);
                break;
            case DomainPayloadFieldKindV1.RefList:
            {
                var refs = Require<IReadOnlyList<PartitionRecordRefV1>>(value, partitionId, rule.Name);
                PartitionRecordRefV1? previous = null;
                foreach (var reference in refs)
                {
                    ValidateReference(reference, references, partitionId, parentFieldName);
                    if (previous is { } prior && CompareReference(prior, reference) >= 0)
                        throw new InvalidDataException($"domain.payload.digest-nested-ref-order:{partitionId}:{parentFieldName}");
                    previous = reference;
                }
                break;
            }
            case DomainPayloadFieldKindV1.Id128:
                if (Require<OpaqueId128>(value, partitionId, rule.Name).IsZero) ThrowRange(partitionId, rule.Name);
                break;
            case DomainPayloadFieldKindV1.Token:
                _ = new StableToken(Require<string>(value, partitionId, rule.Name));
                break;
            case DomainPayloadFieldKindV1.TokenList:
            {
                string? previous = null;
                foreach (var token in Require<IReadOnlyList<string>>(value, partitionId, rule.Name))
                {
                    var canonical = new StableToken(token).Value;
                    if (previous is not null && string.CompareOrdinal(previous, canonical) >= 0)
                        throw new InvalidDataException($"domain.payload.digest-nested-token-order:{partitionId}:{parentFieldName}");
                    previous = canonical;
                }
                break;
            }
            case DomainPayloadFieldKindV1.OrderedTokenList:
                foreach (var token in Require<IReadOnlyList<string>>(value, partitionId, rule.Name)) _ = new StableToken(token);
                break;
            case DomainPayloadFieldKindV1.Ratio:
                if (Require<uint>(value, partitionId, rule.Name) > 1_000_000u) ThrowRange(partitionId, rule.Name);
                break;
            case DomainPayloadFieldKindV1.UInt8:
                _ = Require<byte>(value, partitionId, rule.Name);
                break;
            case DomainPayloadFieldKindV1.UInt16:
                _ = Require<ushort>(value, partitionId, rule.Name);
                break;
            case DomainPayloadFieldKindV1.UInt32:
                _ = Require<uint>(value, partitionId, rule.Name);
                break;
            case DomainPayloadFieldKindV1.Step:
            case DomainPayloadFieldKindV1.UInt64:
                _ = Require<ulong>(value, partitionId, rule.Name);
                break;
            case DomainPayloadFieldKindV1.Int32:
            case DomainPayloadFieldKindV1.Temperature:
            case DomainPayloadFieldKindV1.Pressure:
                _ = Require<int>(value, partitionId, rule.Name);
                break;
            case DomainPayloadFieldKindV1.Int64:
            case DomainPayloadFieldKindV1.Length:
            case DomainPayloadFieldKindV1.Mass:
            case DomainPayloadFieldKindV1.Volume:
            case DomainPayloadFieldKindV1.Power:
            case DomainPayloadFieldKindV1.Energy:
            case DomainPayloadFieldKindV1.Money:
                _ = Require<long>(value, partitionId, rule.Name);
                break;
            case DomainPayloadFieldKindV1.Bool:
                _ = Require<bool>(value, partitionId, rule.Name);
                break;
            case DomainPayloadFieldKindV1.Digest:
                if (Require<byte[]>(value, partitionId, rule.Name).Length != 32) ThrowRange(partitionId, rule.Name);
                break;
            case DomainPayloadFieldKindV1.Vec3:
                _ = Require<Vec3Int64V1>(value, partitionId, rule.Name);
                break;
            case DomainPayloadFieldKindV1.Quat:
            {
                var quaternion = Require<QuaternionQ30V1>(value, partitionId, rule.Name);
                if (quaternion.X == 0 && quaternion.Y == 0 && quaternion.Z == 0 && quaternion.W == 0) ThrowRange(partitionId, rule.Name);
                break;
            }
            case DomainPayloadFieldKindV1.OrderedTokenUInt8Map:
                ValidateMap(Require<IReadOnlyList<KeyValuePair<string, byte>>>(value, partitionId, rule.Name), partitionId, parentFieldName);
                break;
            case DomainPayloadFieldKindV1.OrderedTokenUInt32Map:
                ValidateMap(Require<IReadOnlyList<KeyValuePair<string, uint>>>(value, partitionId, rule.Name), partitionId, parentFieldName);
                break;
            case DomainPayloadFieldKindV1.OrderedTokenInt32Map:
                ValidateMap(Require<IReadOnlyList<KeyValuePair<string, int>>>(value, partitionId, rule.Name), partitionId, parentFieldName);
                break;
            case DomainPayloadFieldKindV1.OrderedNestedList:
            case DomainPayloadFieldKindV1.RuleAst:
                throw new InvalidDataException($"domain.payload.digest-recursive-nested-unavailable:{partitionId}:{parentFieldName}:{rule.Name}");
            default:
                throw new InvalidOperationException($"Unhandled nested domain payload kind: {rule.Kind}");
        }
    }

    private static void WriteReference(
        MvDcborWriter writer,
        PartitionRecordRefV1 reference,
        IDomainRecordReferenceResolverV1? references,
        string partitionId,
        string fieldName)
    {
        ValidateReference(reference, references, partitionId, fieldName);
        writer.WriteArrayStart(2);
        writer.WriteAsciiText(reference.PartitionId.Value);
        writer.WriteBytes(reference.RecordId.ToBytes());
    }

    private static void ValidateReference(
        PartitionRecordRefV1 reference,
        IDomainRecordReferenceResolverV1? references,
        string partitionId,
        string fieldName)
    {
        if (reference.RecordId.IsZero) ThrowRange(partitionId, fieldName);
        _ = StandardDomainPartitionRegistry.Get(reference.PartitionId.Value);
        if (references is not null && !references.Exists(reference))
            throw new InvalidDataException($"domain.payload.digest-reference-missing:{partitionId}:{fieldName}");
    }

    private static int CompareReference(PartitionRecordRefV1 left, PartitionRecordRefV1 right)
    {
        var partition = string.CompareOrdinal(left.PartitionId.Value, right.PartitionId.Value);
        return partition != 0 ? partition : left.RecordId.CompareTo(right.RecordId);
    }

    private static void WriteOrderedMap<T>(
        MvDcborWriter writer,
        IReadOnlyList<KeyValuePair<string, T>> values,
        Action<MvDcborWriter, T> writeValue)
    {
        writer.WriteArrayStart(checked((ulong)values.Count));
        string? previous = null;
        foreach (var pair in values)
        {
            var key = new StableToken(pair.Key).Value;
            if (previous is not null && string.CompareOrdinal(previous, key) >= 0)
                throw new InvalidDataException("domain.payload.digest-map-order");
            previous = key;
            writer.WriteArrayStart(2);
            writer.WriteAsciiText(key);
            writeValue(writer, pair.Value);
        }
    }

    private static void ValidateMap<T>(IReadOnlyList<KeyValuePair<string, T>> values, string partitionId, string fieldName)
    {
        string? previous = null;
        foreach (var pair in values)
        {
            var key = new StableToken(pair.Key).Value;
            if (previous is not null && string.CompareOrdinal(previous, key) >= 0)
                throw new InvalidDataException($"domain.payload.digest-nested-map-order:{partitionId}:{fieldName}");
            previous = key;
        }
    }

    private static T Require<T>(object value, string partitionId, string fieldName)
    {
        if (value is T typed) return typed;
        throw new InvalidDataException($"domain.payload.digest-type:{partitionId}:{fieldName}:{typeof(T).Name}");
    }

    private static void ThrowRange(string partitionId, string fieldName)
        => throw new InvalidDataException($"domain.payload.digest-range:{partitionId}:{fieldName}");

    private sealed record PresentField(ulong FieldNumber, DomainPayloadFieldRuleV1 Rule, object Value);
}
