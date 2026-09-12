using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public enum Qa04GovernanceInstitutionDependencyKindV1 : byte
{
    InstitutionKindVocabulary = 1,
    DecisionMethodVocabulary = 2,
    OfficeAuthorityMapping = 3,
}

public sealed record Qa04GovernanceInstitutionDependencyV1(
    StableToken DependencyId,
    Qa04GovernanceInstitutionDependencyKindV1 Kind,
    StableToken FailureCode);

/// <summary>
/// Canonical 5,000 governance.institution dependency boundary.
/// Benchmark-only institution_kind, decision_method and empty office_refs semantics are decided by
/// the Alpha 1.1 Society/Governance amendment and production-proven by the canonical materializer.
/// </summary>
public static class Qa04GovernanceInstitutionDependencyContractV1
{
    public const string PartitionId = "governance.institution";
    public const string PolityPartitionId = "governance.polity";
    public const ulong CanonicalStartOrdinal = 1_601_000;
    public const ulong CanonicalCount = 5_000;

    public static readonly StableToken CanonicalLifecycle = new("active");

    private static readonly IReadOnlyList<Qa04GovernanceInstitutionDependencyV1> BlockersValue =
        Array.AsReadOnly(Array.Empty<Qa04GovernanceInstitutionDependencyV1>());

    public static IReadOnlyList<Qa04GovernanceInstitutionDependencyV1> Blockers => BlockersValue;

    public static void ValidateCanonicalContract()
    {
        Qa04SocietyGovernanceReferenceDecompositionV1.ValidateCanonicalContract();
        Qa04GovernancePolityMaterializerV1.ValidateCanonicalContract();

        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(PartitionId);
        if (slice.StartOrdinal != CanonicalStartOrdinal || slice.Count != CanonicalCount || slice.UsesSpecializedIdentity)
            throw new InvalidDataException("qa04.governance.institution-decomposition-drift");

        var identity = StandardDomainPartitionRegistry.Get(PartitionId);
        if (identity.OwnerDomain.Value != "governance_security" ||
            StandardDomainPartitionRegistry.Get(PolityPartitionId).OwnerDomain.Value != "governance_security")
            throw new InvalidDataException("qa04.governance.institution-owner-drift");

        var schema = StandardDomainPayloadSchemaRegistry.Get(PartitionId);
        RequireField(schema, "polity_ref", DomainPayloadFieldKindV1.Ref, optional: false);
        RequireField(schema, "institution_kind", DomainPayloadFieldKindV1.Token, optional: false);
        RequireField(schema, "office_refs", DomainPayloadFieldKindV1.RefList, optional: false);
        RequireField(schema, "decision_method", DomainPayloadFieldKindV1.Token, optional: false);
        RequireField(schema, "selection_rule_ref", DomainPayloadFieldKindV1.Ref, optional: true);
        RequireField(schema, "lifecycle", DomainPayloadFieldKindV1.Token, optional: false);

        if (CanonicalLifecycle.Value != "active" ||
            Qa04GovernancePolityMaterializerV1.CanonicalCount != 1_000 ||
            BlockersValue.Count != 0)
            throw new InvalidDataException("qa04.governance.institution-known-genesis-drift");
    }

    private static void RequireField(
        DomainPayloadSchemaDescriptorV1 schema,
        string fieldName,
        DomainPayloadFieldKindV1 kind,
        bool optional)
    {
        var field = schema.Fields.SingleOrDefault(candidate => string.Equals(candidate.Name, fieldName, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"qa04.governance.institution-field-missing:{fieldName}");
        if (field.Kind != kind || field.Optional != optional)
            throw new InvalidDataException($"qa04.governance.institution-field-drift:{fieldName}");
    }
}
