using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Participation;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class CanonicalNestedSnapshotNegativeInitializer
{
    private const string PartitionId = "participation.absence_policy";
    private const string FieldName = "priority_rules";

    [ModuleInitializer]
    internal static void Initialize()
    {
        var standardRegistry = StandardDomainNestedSnapshotCodecRegistryV1.Create();
        var value = new ParticipationPolicyRuleV1(-10, new StableToken("safety"));
        var valueWire = DomainNestedSnapshotWireCodecV1.EncodeValue(
            PartitionId,
            FieldName,
            value,
            standardRegistry);
        var listWire = DomainNestedSnapshotWireCodecV1.EncodeList(
            PartitionId,
            FieldName,
            new ICanonicalDomainNestedValueV1[] { value },
            standardRegistry);

        ExpectInvalid(
            "nested value unknown protobuf field",
            () => _ = DomainNestedSnapshotWireCodecV1.DecodeValue(
                PartitionId,
                FieldName,
                valueWire.Concat(new byte[] { 0x20, 0x01 }).ToArray(),
                standardRegistry),
            "persistence.snapshot.nested-value-unknown-field:");

        ExpectInvalid(
            "nested list unknown protobuf field",
            () => _ = DomainNestedSnapshotWireCodecV1.DecodeList(
                PartitionId,
                FieldName,
                listWire.Concat(new byte[] { 0x12, 0x00 }).ToArray(),
                standardRegistry),
            "persistence.snapshot.nested-list-unknown-field:");

        var missingRequiredRegistry = new DomainNestedSnapshotCodecRegistryV1(new[]
        {
            CreateCodec(
                StandardDomainNestedSnapshotSchemaV1.ParticipationPolicyRule,
                static rule => new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["priority"] = rule.Priority,
                }),
        });
        ExpectInvalid(
            "nested required field omission",
            () => _ = DomainNestedSnapshotWireCodecV1.EncodeValue(
                PartitionId,
                FieldName,
                value,
                missingRequiredRegistry),
            "persistence.snapshot.nested-required-field:");

        var version2Descriptor = new DomainNestedSnapshotSchemaDescriptorV1(
            new SchemaRefV1("domain.nested.participation-policy-rule", 2, 0),
            StandardDomainNestedSnapshotSchemaV1.ParticipationPolicyRule.Fields);
        var version2Registry = new DomainNestedSnapshotCodecRegistryV1(new[]
        {
            CreateCodec(version2Descriptor, StandardFields),
        });
        ExpectInvalid(
            "nested schema version mismatch",
            () => _ = DomainNestedSnapshotWireCodecV1.DecodeValue(
                PartitionId,
                FieldName,
                valueWire,
                version2Registry),
            "persistence.snapshot.nested-schema-unavailable:");

        var alternateSchema = new DomainNestedSnapshotSchemaDescriptorV1(
            new SchemaRefV1("domain.nested.participation-policy-rule-alt"),
            StandardDomainNestedSnapshotSchemaV1.ParticipationPolicyRule.Fields);
        ExpectInvalid(
            "nested codec duplicate parent binding",
            () => _ = new DomainNestedSnapshotCodecRegistryV1(new IDomainNestedSnapshotCodecV1[]
            {
                CreateCodec(StandardDomainNestedSnapshotSchemaV1.ParticipationPolicyRule, StandardFields),
                CreateCodec(alternateSchema, StandardFields),
            }),
            "persistence.snapshot.nested-codec-binding-duplicate:");

        var foreignBinding = new DomainNestedSnapshotCodecV1<ParticipationPolicyRuleV1>(
            "resident.body_health",
            "body_region_states",
            StandardDomainNestedSnapshotSchemaV1.ParticipationPolicyRule,
            StandardFields,
            FromStandardFields,
            Compare);
        ExpectInvalid(
            "nested codec duplicate schema authority",
            () => _ = new DomainNestedSnapshotCodecRegistryV1(new IDomainNestedSnapshotCodecV1[]
            {
                CreateCodec(StandardDomainNestedSnapshotSchemaV1.ParticipationPolicyRule, StandardFields),
                foreignBinding,
            }),
            "persistence.snapshot.nested-codec-schema-duplicate:");
    }

    private static DomainNestedSnapshotCodecV1<ParticipationPolicyRuleV1> CreateCodec(
        DomainNestedSnapshotSchemaDescriptorV1 descriptor,
        Func<ParticipationPolicyRuleV1, IReadOnlyDictionary<string, object?>> toFields)
        => new(
            PartitionId,
            FieldName,
            descriptor,
            toFields,
            FromStandardFields,
            Compare);

    private static IReadOnlyDictionary<string, object?> StandardFields(ParticipationPolicyRuleV1 rule)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["priority"] = rule.Priority,
            ["rule_id"] = rule.RuleId.Value,
        };

    private static ParticipationPolicyRuleV1 FromStandardFields(IReadOnlyDictionary<string, object?> fields)
        => new(
            (int)fields["priority"]!,
            new StableToken((string)fields["rule_id"]!));

    private static int Compare(ParticipationPolicyRuleV1 left, ParticipationPolicyRuleV1 right)
    {
        var priority = left.Priority.CompareTo(right.Priority);
        return priority != 0 ? priority : string.CompareOrdinal(left.RuleId.Value, right.RuleId.Value);
    }

    private static void ExpectInvalid(string name, Action action, string expectedPrefix)
    {
        try
        {
            action();
        }
        catch (InvalidDataException ex) when (ex.Message.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            return;
        }

        throw new InvalidOperationException($"Expected InvalidDataException for {name} with prefix {expectedPrefix}.");
    }
}
