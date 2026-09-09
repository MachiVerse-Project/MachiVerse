using System.Collections.ObjectModel;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Participation;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed record DomainNestedSnapshotSchemaDescriptorV1(
    SchemaRefV1 Schema,
    IReadOnlyList<DomainPayloadFieldRuleV1> Fields);

/// <summary>
/// Explicit owner codec for one nested semantic value binding. A codec is bound to the exact
/// parent standard partition field that owns the nested value; no runtime type-name, reflection,
/// JSON, or arbitrary object serializer can act as persistence schema authority.
/// </summary>
public interface IDomainNestedSnapshotCodecV1
{
    string ParentPartitionId { get; }
    string ParentFieldName { get; }
    DomainNestedSnapshotSchemaDescriptorV1 Descriptor { get; }
    bool CanEncode(ICanonicalDomainNestedValueV1 value);
    IReadOnlyDictionary<string, object?> ToStandardFields(ICanonicalDomainNestedValueV1 value);
    ICanonicalDomainNestedValueV1 FromStandardFields(IReadOnlyDictionary<string, object?> fields);
    int CompareCanonical(ICanonicalDomainNestedValueV1 left, ICanonicalDomainNestedValueV1 right);
}

public sealed class DomainNestedSnapshotCodecV1<TNested> : IDomainNestedSnapshotCodecV1
    where TNested : class, ICanonicalDomainNestedValueV1
{
    private readonly Func<TNested, IReadOnlyDictionary<string, object?>> _toStandardFields;
    private readonly Func<IReadOnlyDictionary<string, object?>, TNested> _fromStandardFields;
    private readonly Comparison<TNested> _compareCanonical;

    public DomainNestedSnapshotCodecV1(
        string parentPartitionId,
        string parentFieldName,
        DomainNestedSnapshotSchemaDescriptorV1 descriptor,
        Func<TNested, IReadOnlyDictionary<string, object?>> toStandardFields,
        Func<IReadOnlyDictionary<string, object?>, TNested> fromStandardFields,
        Comparison<TNested> compareCanonical)
    {
        ParentPartitionId = new StableToken(parentPartitionId).Value;
        ParentFieldName = parentFieldName ?? throw new ArgumentNullException(nameof(parentFieldName));
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _toStandardFields = toStandardFields ?? throw new ArgumentNullException(nameof(toStandardFields));
        _fromStandardFields = fromStandardFields ?? throw new ArgumentNullException(nameof(fromStandardFields));
        _compareCanonical = compareCanonical ?? throw new ArgumentNullException(nameof(compareCanonical));
    }

    public string ParentPartitionId { get; }
    public string ParentFieldName { get; }
    public DomainNestedSnapshotSchemaDescriptorV1 Descriptor { get; }

    public bool CanEncode(ICanonicalDomainNestedValueV1 value) => value is TNested;

    public IReadOnlyDictionary<string, object?> ToStandardFields(ICanonicalDomainNestedValueV1 value)
    {
        if (value is not TNested typed)
            throw new InvalidDataException($"persistence.snapshot.nested-codec-type-mismatch:{ParentPartitionId}:{ParentFieldName}");
        typed.ValidateCanonical();
        return _toStandardFields(typed)
            ?? throw new InvalidDataException($"persistence.snapshot.nested-codec-fields-null:{ParentPartitionId}:{ParentFieldName}");
    }

    public ICanonicalDomainNestedValueV1 FromStandardFields(IReadOnlyDictionary<string, object?> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var value = _fromStandardFields(fields)
            ?? throw new InvalidDataException($"persistence.snapshot.nested-codec-value-null:{ParentPartitionId}:{ParentFieldName}");
        value.ValidateCanonical();
        return value;
    }

    public int CompareCanonical(ICanonicalDomainNestedValueV1 left, ICanonicalDomainNestedValueV1 right)
    {
        if (left is not TNested leftTyped || right is not TNested rightTyped)
            throw new InvalidDataException($"persistence.snapshot.nested-codec-type-mismatch:{ParentPartitionId}:{ParentFieldName}");
        return _compareCanonical(leftTyped, rightTyped);
    }
}

public sealed class DomainNestedSnapshotCodecRegistryV1
{
    private readonly IReadOnlyDictionary<(string PartitionId, string FieldName), IDomainNestedSnapshotCodecV1> _byBinding;
    private readonly IReadOnlyDictionary<(string SchemaId, ushort Major, ushort Minor), IDomainNestedSnapshotCodecV1> _bySchema;

    public DomainNestedSnapshotCodecRegistryV1(IEnumerable<IDomainNestedSnapshotCodecV1> codecs)
    {
        ArgumentNullException.ThrowIfNull(codecs);
        var byBinding = new Dictionary<(string, string), IDomainNestedSnapshotCodecV1>();
        var bySchema = new Dictionary<(string, ushort, ushort), IDomainNestedSnapshotCodecV1>();

        foreach (var codec in codecs)
        {
            ArgumentNullException.ThrowIfNull(codec);
            ValidateCodec(codec);
            var binding = (codec.ParentPartitionId, codec.ParentFieldName);
            if (!byBinding.TryAdd(binding, codec))
                throw new InvalidDataException($"persistence.snapshot.nested-codec-binding-duplicate:{binding.ParentPartitionId}:{binding.ParentFieldName}");

            var schema = codec.Descriptor.Schema;
            var schemaKey = (schema.SchemaId.Value, schema.Version.Major, schema.Version.Minor);
            if (!bySchema.TryAdd(schemaKey, codec))
                throw new InvalidDataException($"persistence.snapshot.nested-codec-schema-duplicate:{schema.SchemaId.Value}");
        }

        _byBinding = new ReadOnlyDictionary<(string, string), IDomainNestedSnapshotCodecV1>(byBinding);
        _bySchema = new ReadOnlyDictionary<(string, ushort, ushort), IDomainNestedSnapshotCodecV1>(bySchema);
    }

    public IDomainNestedSnapshotCodecV1 GetForBinding(string partitionId, string fieldName)
    {
        var normalizedPartition = new StableToken(partitionId).Value;
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        return _byBinding.TryGetValue((normalizedPartition, fieldName), out var codec)
            ? codec
            : throw new InvalidDataException($"persistence.snapshot.nested-codec-unavailable:{normalizedPartition}:{fieldName}");
    }

    public IDomainNestedSnapshotCodecV1 GetForSchema(SchemaRefV1 schema)
    {
        var key = (schema.SchemaId.Value, schema.Version.Major, schema.Version.Minor);
        return _bySchema.TryGetValue(key, out var codec)
            ? codec
            : throw new InvalidDataException($"persistence.snapshot.nested-schema-unavailable:{schema.SchemaId.Value}:{schema.Version.Major}.{schema.Version.Minor}");
    }

    public void ValidateOrderedList(
        string partitionId,
        string fieldName,
        IReadOnlyList<ICanonicalDomainNestedValueV1> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var codec = GetForBinding(partitionId, fieldName);
        ICanonicalDomainNestedValueV1? previous = null;
        foreach (var value in values)
        {
            if (value is null || !codec.CanEncode(value))
                throw new InvalidDataException($"persistence.snapshot.nested-codec-type-mismatch:{codec.ParentPartitionId}:{codec.ParentFieldName}");
            value.ValidateCanonical();
            if (previous is not null && codec.CompareCanonical(previous, value) >= 0)
                throw new InvalidDataException($"persistence.snapshot.nested-list-order:{codec.ParentPartitionId}:{codec.ParentFieldName}");
            previous = value;
        }
    }

    private static void ValidateCodec(IDomainNestedSnapshotCodecV1 codec)
    {
        var partitionId = new StableToken(codec.ParentPartitionId).Value;
        if (!string.Equals(partitionId, codec.ParentPartitionId, StringComparison.Ordinal))
            throw new InvalidDataException("persistence.snapshot.nested-codec-partition-noncanonical");
        ArgumentException.ThrowIfNullOrWhiteSpace(codec.ParentFieldName);

        var parent = StandardDomainPayloadSchemaRegistry.Get(partitionId);
        var parentField = parent.Fields.SingleOrDefault(field => string.Equals(field.Name, codec.ParentFieldName, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"persistence.snapshot.nested-codec-parent-field-unknown:{partitionId}:{codec.ParentFieldName}");
        if (parentField.Kind is not (DomainPayloadFieldKindV1.OrderedNestedList or DomainPayloadFieldKindV1.RuleAst))
            throw new InvalidDataException($"persistence.snapshot.nested-codec-parent-field-not-nested:{partitionId}:{codec.ParentFieldName}");

        var descriptor = codec.Descriptor ?? throw new InvalidDataException($"persistence.snapshot.nested-codec-descriptor-null:{partitionId}:{codec.ParentFieldName}");
        if (descriptor.Schema.Version.Major == 0)
            throw new InvalidDataException($"persistence.snapshot.nested-codec-schema-version-invalid:{descriptor.Schema.SchemaId.Value}");
        ArgumentNullException.ThrowIfNull(descriptor.Fields);
        if (descriptor.Fields.Count == 0)
            throw new InvalidDataException($"persistence.snapshot.nested-codec-field-set-empty:{descriptor.Schema.SchemaId.Value}");

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in descriptor.Fields)
        {
            ArgumentNullException.ThrowIfNull(field);
            if (string.IsNullOrWhiteSpace(field.Name) || !names.Add(field.Name))
                throw new InvalidDataException($"persistence.snapshot.nested-codec-field-duplicate:{descriptor.Schema.SchemaId.Value}");
        }
    }
}

public static class StandardDomainNestedSnapshotSchemaV1
{
    public static DomainNestedSnapshotSchemaDescriptorV1 ParticipationPolicyRule { get; } = new(
        new SchemaRefV1("domain.nested.participation-policy-rule"),
        Array.AsReadOnly(new[]
        {
            new DomainPayloadFieldRuleV1("priority", DomainPayloadFieldKindV1.Int32, Optional: false),
            new DomainPayloadFieldRuleV1("rule_id", DomainPayloadFieldKindV1.Token, Optional: false),
        }));
}

public static class StandardDomainNestedSnapshotCodecRegistryV1
{
    public static DomainNestedSnapshotCodecRegistryV1 Create()
        => new(new IDomainNestedSnapshotCodecV1[]
        {
            new DomainNestedSnapshotCodecV1<ParticipationPolicyRuleV1>(
                "participation.absence_policy",
                "priority_rules",
                StandardDomainNestedSnapshotSchemaV1.ParticipationPolicyRule,
                static value => new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["priority"] = value.Priority,
                    ["rule_id"] = value.RuleId.Value,
                },
                static fields => new ParticipationPolicyRuleV1(
                    (int)fields["priority"]!,
                    new StableToken((string)fields["rule_id"]!)),
                static (left, right) =>
                {
                    var priority = left.Priority.CompareTo(right.Priority);
                    return priority != 0 ? priority : string.CompareOrdinal(left.RuleId.Value, right.RuleId.Value);
                }),
        });
}
