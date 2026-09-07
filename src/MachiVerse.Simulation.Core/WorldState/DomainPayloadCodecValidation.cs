using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.WorldState;

public interface IDomainRecordSchemaResolverV1 : IDomainRecordReferenceResolverV1
{
    bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema);
}

/// <summary>
/// Canonical SIM-04 payload validation entry point. It composes the generic field validator with
/// Phase 4 scalar-family ranges, nested canonical validation, and target record-schema checks.
/// Domain-specific algorithms/cross-field invariants remain owned by SIM-07..SIM-12.
/// </summary>
public sealed class StandardDomainPayloadCodecValidatorV1
{
    private readonly StandardDomainPayloadValidatorV1 _fields = new();

    public void Validate(
        string partitionId,
        IReadOnlyDictionary<string, object?> payload,
        IDomainRecordSchemaResolverV1 references)
    {
        ArgumentNullException.ThrowIfNull(references);
        _fields.Validate(partitionId, payload, references);

        var descriptor = StandardDomainPayloadSchemaRegistry.Get(partitionId);
        foreach (var field in descriptor.Fields)
        {
            if (!payload.TryGetValue(field.Name, out var value) || value is null) continue;

            ValidateScalarFamily(partitionId, field, value);
            ValidateReferenceSchema(partitionId, field, value, references);

            if (field.Kind is DomainPayloadFieldKindV1.OrderedNestedList or DomainPayloadFieldKindV1.RuleAst)
            {
                ((ICanonicalDomainNestedValueV1)value).ValidateCanonical();
            }
        }

        ValidateSchemaSpecificRange(partitionId, payload);
    }

    private static void ValidateScalarFamily(
        string partitionId,
        DomainPayloadFieldRuleV1 field,
        object value)
    {
        switch (field.Kind)
        {
            case DomainPayloadFieldKindV1.Mass:
            case DomainPayloadFieldKindV1.Volume:
            case DomainPayloadFieldKindV1.Energy:
            case DomainPayloadFieldKindV1.Power:
                if ((long)value < 0) ThrowRange(partitionId, field.Name);
                break;
            case DomainPayloadFieldKindV1.Temperature:
            case DomainPayloadFieldKindV1.Pressure:
                if ((int)value < 0) ThrowRange(partitionId, field.Name);
                break;
            case DomainPayloadFieldKindV1.UInt32 when field.Name.EndsWith("_ppb", StringComparison.Ordinal):
                if ((uint)value > 1_000_000_000u) ThrowRange(partitionId, field.Name);
                break;
            case DomainPayloadFieldKindV1.OrderedTokenUInt32Map when field.Name.EndsWith("_ppb", StringComparison.Ordinal):
                foreach (var pair in (IReadOnlyList<KeyValuePair<string, uint>>)value)
                {
                    if (pair.Value > 1_000_000_000u) ThrowRange(partitionId, field.Name);
                }
                break;
        }
    }

    private static void ValidateReferenceSchema(
        string partitionId,
        DomainPayloadFieldRuleV1 field,
        object value,
        IDomainRecordSchemaResolverV1 resolver)
    {
        switch (field.Kind)
        {
            case DomainPayloadFieldKindV1.Ref:
                RequireReferenceSchema(partitionId, field.Name, (PartitionRecordRefV1)value, resolver);
                break;
            case DomainPayloadFieldKindV1.RefList:
                foreach (var reference in (IReadOnlyList<PartitionRecordRefV1>)value)
                    RequireReferenceSchema(partitionId, field.Name, reference, resolver);
                break;
        }
    }

    private static void RequireReferenceSchema(
        string partitionId,
        string field,
        PartitionRecordRefV1 reference,
        IDomainRecordSchemaResolverV1 resolver)
    {
        var expected = StandardDomainPartitionRegistry.Get(reference.PartitionId.Value).RecordSchema;
        if (!resolver.TryGetRecordSchema(reference, out var actual) || actual != expected)
            throw new InvalidDataException($"domain.payload.reference-schema:{partitionId}:{field}");
    }

    private static void ValidateSchemaSpecificRange(
        string partitionId,
        IReadOnlyDictionary<string, object?> payload)
    {
        if (partitionId == "participation.detail_requirement" &&
            payload.TryGetValue("minimum_detail", out var minimumDetail) &&
            minimumDetail is byte detail && detail > 3)
        {
            ThrowRange(partitionId, "minimum_detail");
        }

        if (partitionId == "built.opening" &&
            payload.TryGetValue("space_refs", out var spaceRefs) &&
            spaceRefs is IReadOnlyList<PartitionRecordRefV1> spaces && spaces.Count > 2)
        {
            ThrowRange(partitionId, "space_refs");
        }
    }

    private static void ThrowRange(string partitionId, string field)
        => throw new InvalidDataException($"domain.payload.scalar-range:{partitionId}:{field}");
}
