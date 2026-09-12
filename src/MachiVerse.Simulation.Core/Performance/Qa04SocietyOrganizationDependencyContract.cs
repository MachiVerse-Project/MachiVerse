using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public enum Qa04SocietyOrganizationDependencyKindV1 : byte
{
    OrganizationClassVocabulary = 1,
}

public sealed record Qa04SocietyOrganizationDependencyV1(
    StableToken DependencyId,
    Qa04SocietyOrganizationDependencyKindV1 Kind,
    StableToken FailureCode);

/// <summary>
/// Canonical 10,000 society.organization dependency boundary.
/// The benchmark-only organization_class authority is decided by the Alpha 1.1 Society/Governance
/// amendment and production-proven through Qa04SocietyGovernanceCanonicalMaterializerV1.
/// </summary>
public static class Qa04SocietyOrganizationDependencyContractV1
{
    public const ulong CanonicalCount = 10_000;
    public const ulong CanonicalStartOrdinal = 0;
    public const ulong CanonicalFoundedStep = 0;
    public static readonly StableToken CanonicalLifecycle = new("active");

    private static readonly IReadOnlyList<Qa04SocietyOrganizationDependencyV1> BlockersValue =
        Array.AsReadOnly(Array.Empty<Qa04SocietyOrganizationDependencyV1>());

    public static IReadOnlyList<Qa04SocietyOrganizationDependencyV1> Blockers => BlockersValue;

    public static void ValidateCanonicalContract()
    {
        Qa04SocietyGovernanceReferenceDecompositionV1.ValidateCanonicalContract();

        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get("society.organization");
        if (slice.StartOrdinal != CanonicalStartOrdinal || slice.Count != CanonicalCount || slice.UsesSpecializedIdentity)
            throw new InvalidDataException("qa04.society.organization-decomposition-drift");

        var partition = StandardDomainPartitionRegistry.Get("society.organization");
        if (partition.OwnerDomain.Value != "society_economy")
            throw new InvalidDataException("qa04.society.organization-owner-drift");

        var schema = StandardDomainPayloadSchemaRegistry.Get("society.organization");
        RequireField(schema, "organization_id", DomainPayloadFieldKindV1.Id128);
        RequireField(schema, "organization_class", DomainPayloadFieldKindV1.Token);
        RequireField(schema, "lifecycle", DomainPayloadFieldKindV1.Token);
        RequireField(schema, "purpose_tokens", DomainPayloadFieldKindV1.TokenList);
        RequireField(schema, "parent_refs", DomainPayloadFieldKindV1.RefList);
        RequireField(schema, "facility_refs", DomainPayloadFieldKindV1.RefList);
        RequireField(schema, "founded_step", DomainPayloadFieldKindV1.Step);

        if (CanonicalLifecycle.Value != "active" || CanonicalFoundedStep != 0 || BlockersValue.Count != 0)
            throw new InvalidDataException("qa04.society.organization-genesis-state-drift");
    }

    private static void RequireField(
        DomainPayloadSchemaDescriptorV1 schema,
        string fieldName,
        DomainPayloadFieldKindV1 kind)
    {
        var field = schema.Fields.SingleOrDefault(candidate => string.Equals(candidate.Name, fieldName, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"qa04.society.organization-field-missing:{fieldName}");
        if (field.Kind != kind || field.Optional)
            throw new InvalidDataException($"qa04.society.organization-field-drift:{fieldName}");
    }
}
