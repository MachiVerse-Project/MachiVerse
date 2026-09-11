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
/// Exact fail-closed boundary for the canonical 5,000 governance.institution records.
///
/// Alpha 1.1 fixes the descriptor range, non-specialized authoritative RecordId mapping, Polity
/// target pool, lifecycle=active, optional selection_rule_ref=NONE, and the D2 genesis envelope.
/// The canonical institution_kind / decision_method Token vocabularies are not defined. The
/// production schema also carries required office_refs, while the benchmark does not define their
/// target authority/mapping or state that this list is empty for canonical genesis. Those semantics
/// must be resolved before actual Institution authority is materialized.
/// </summary>
public static class Qa04GovernanceInstitutionDependencyContractV1
{
    public const string PartitionId = "governance.institution";
    public const string PolityPartitionId = "governance.polity";
    public const ulong CanonicalStartOrdinal = 1_601_000;
    public const ulong CanonicalCount = 5_000;

    public static readonly StableToken CanonicalLifecycle = new("active");

    private static readonly IReadOnlyList<Qa04GovernanceInstitutionDependencyV1> BlockersValue =
        Array.AsReadOnly(new[]
        {
            Blocker(
                "governance.institution.institution-kind-vocabulary",
                Qa04GovernanceInstitutionDependencyKindV1.InstitutionKindVocabulary,
                "qa04.material.institution-kind-vocabulary-undefined"),
            Blocker(
                "governance.institution.decision-method-vocabulary",
                Qa04GovernanceInstitutionDependencyKindV1.DecisionMethodVocabulary,
                "qa04.material.decision-method-vocabulary-undefined"),
            Blocker(
                "governance.institution.office-ref-mapping",
                Qa04GovernanceInstitutionDependencyKindV1.OfficeAuthorityMapping,
                "qa04.material.institution-office-mapping-undefined"),
        }
        .OrderBy(static blocker => blocker.DependencyId.Value, StringComparer.Ordinal)
        .ToArray());

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
            Qa04GovernancePolityMaterializerV1.CanonicalCount != 1_000)
            throw new InvalidDataException("qa04.governance.institution-known-genesis-drift");

        if (BlockersValue.Count != 3 ||
            BlockersValue.Select(static blocker => blocker.Kind).Distinct().Count() != BlockersValue.Count ||
            BlockersValue.Select(static blocker => blocker.DependencyId).Distinct().Count() != BlockersValue.Count ||
            BlockersValue.Select(static blocker => blocker.FailureCode).Distinct().Count() != BlockersValue.Count)
            throw new InvalidDataException("qa04.governance.institution-dependency-drift");
    }

    private static Qa04GovernanceInstitutionDependencyV1 Blocker(
        string dependencyId,
        Qa04GovernanceInstitutionDependencyKindV1 kind,
        string failureCode)
        => new(new StableToken(dependencyId), kind, new StableToken(failureCode));

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
