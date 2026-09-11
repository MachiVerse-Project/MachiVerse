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
/// Exact fail-closed boundary for the canonical 10,000 society.organization records.
///
/// Alpha 1.1 already fixes the descriptor range, authoritative non-specialized RecordId mapping,
/// genesis revision, status/lifecycle = active, required Ref selector policy, optional Ref = NONE,
/// and empty-permitted list behavior. The remaining semantic gap is the profile's canonical
/// organization_class Token vocabulary. The benchmark genesis scalar source must not be used to
/// invent arbitrary Token vocabulary.
/// </summary>
public static class Qa04SocietyOrganizationDependencyContractV1
{
    public const ulong CanonicalCount = 10_000;
    public const ulong CanonicalStartOrdinal = 0;
    public const ulong CanonicalFoundedStep = 0;
    public static readonly StableToken CanonicalLifecycle = new("active");

    private static readonly IReadOnlyList<Qa04SocietyOrganizationDependencyV1> BlockersValue =
        Array.AsReadOnly(new[]
        {
            new Qa04SocietyOrganizationDependencyV1(
                new StableToken("society.organization.organization-class-vocabulary"),
                Qa04SocietyOrganizationDependencyKindV1.OrganizationClassVocabulary,
                new StableToken("qa04.material.organization-class-vocabulary-undefined")),
        });

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

        if (CanonicalLifecycle.Value != "active" || CanonicalFoundedStep != 0)
            throw new InvalidDataException("qa04.society.organization-genesis-state-drift");
        if (BlockersValue.Count != 1 ||
            BlockersValue[0].Kind != Qa04SocietyOrganizationDependencyKindV1.OrganizationClassVocabulary)
            throw new InvalidDataException("qa04.society.organization-dependency-drift");
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
