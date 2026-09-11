using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public enum Qa04GovernancePublicAuthorityDependencyKindV1 : byte
{
    InstitutionAuthority = 1,
    AuthorityTokenVocabulary = 2,
    GenesisEffectiveFrom = 3,
}

public sealed record Qa04GovernancePublicAuthorityDependencyV1(
    StableToken DependencyId,
    Qa04GovernancePublicAuthorityDependencyKindV1 Kind,
    StableToken FailureCode);

/// <summary>
/// Exact fail-closed boundary for the canonical 25,000 governance.public_authority records.
///
/// Alpha 1.1 fixes the descriptor range, non-specialized authoritative RecordId mapping, holder
/// selector (Resident), required spatial scope selector (canonical TileScope modulo mapping),
/// status=active, optional effective_until=NONE, and the D2 genesis envelope. Actual Institution
/// authority is not yet available, the canonical authority_tokens vocabulary is undefined, and
/// effective_from has no profile-specific genesis rule. PublicAuthority materialization must remain
/// fail-closed until those three dependencies are resolved.
/// </summary>
public static class Qa04GovernancePublicAuthorityDependencyContractV1
{
    public const string PartitionId = "governance.public_authority";
    public const string InstitutionPartitionId = "governance.institution";
    public const string HolderPartitionId = "resident.identity_lifecycle";
    public const ulong CanonicalStartOrdinal = 1_676_000;
    public const ulong CanonicalCount = 25_000;

    public static readonly StableToken CanonicalStatus = new("active");

    private static readonly IReadOnlyList<Qa04GovernancePublicAuthorityDependencyV1> BlockersValue =
        Array.AsReadOnly(new[]
        {
            Blocker(
                "governance.public-authority.institution-authority",
                Qa04GovernancePublicAuthorityDependencyKindV1.InstitutionAuthority,
                "qa04.material.public-authority-institution-undefined"),
            Blocker(
                "governance.public-authority.authority-token-vocabulary",
                Qa04GovernancePublicAuthorityDependencyKindV1.AuthorityTokenVocabulary,
                "qa04.material.public-authority-token-vocabulary-undefined"),
            Blocker(
                "governance.public-authority.effective-from-genesis",
                Qa04GovernancePublicAuthorityDependencyKindV1.GenesisEffectiveFrom,
                "qa04.material.public-authority-effective-from-undefined"),
        }
        .OrderBy(static blocker => blocker.DependencyId.Value, StringComparer.Ordinal)
        .ToArray());

    public static IReadOnlyList<Qa04GovernancePublicAuthorityDependencyV1> Blockers => BlockersValue;

    public static void ValidateCanonicalContract()
    {
        Qa04SocietyGovernanceReferenceDecompositionV1.ValidateCanonicalContract();
        Qa04GovernanceInstitutionDependencyContractV1.ValidateCanonicalContract();
        Qa04SpatialTileScopeAuthorityV1.ValidateCanonicalContract();

        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(PartitionId);
        if (slice.StartOrdinal != CanonicalStartOrdinal || slice.Count != CanonicalCount || slice.UsesSpecializedIdentity)
            throw new InvalidDataException("qa04.governance.public-authority-decomposition-drift");

        var identity = StandardDomainPartitionRegistry.Get(PartitionId);
        if (identity.OwnerDomain.Value != "governance_security" ||
            StandardDomainPartitionRegistry.Get(InstitutionPartitionId).OwnerDomain.Value != "governance_security" ||
            StandardDomainPartitionRegistry.Get(HolderPartitionId).OwnerDomain.Value != "resident")
            throw new InvalidDataException("qa04.governance.public-authority-ref-owner-drift");

        var schema = StandardDomainPayloadSchemaRegistry.Get(PartitionId);
        RequireField(schema, "institution_ref", DomainPayloadFieldKindV1.Ref, optional: false);
        RequireField(schema, "holder_ref", DomainPayloadFieldKindV1.Ref, optional: false);
        RequireField(schema, "authority_tokens", DomainPayloadFieldKindV1.TokenList, optional: false);
        RequireField(schema, "scope_refs", DomainPayloadFieldKindV1.RefList, optional: false);
        RequireField(schema, "effective_from", DomainPayloadFieldKindV1.Step, optional: false);
        RequireField(schema, "effective_until", DomainPayloadFieldKindV1.Step, optional: true);
        RequireField(schema, "status", DomainPayloadFieldKindV1.Token, optional: false);

        if (CanonicalStatus.Value != "active" ||
            Qa04GovernanceInstitutionDependencyContractV1.Blockers.Count != 3 ||
            Qa04SpatialTileScopeAuthorityV1.CanonicalScopeCount != 4_096)
            throw new InvalidDataException("qa04.governance.public-authority-known-genesis-drift");

        if (BlockersValue.Count != 3 ||
            BlockersValue.Select(static blocker => blocker.Kind).Distinct().Count() != BlockersValue.Count ||
            BlockersValue.Select(static blocker => blocker.DependencyId).Distinct().Count() != BlockersValue.Count ||
            BlockersValue.Select(static blocker => blocker.FailureCode).Distinct().Count() != BlockersValue.Count)
            throw new InvalidDataException("qa04.governance.public-authority-dependency-drift");
    }

    private static Qa04GovernancePublicAuthorityDependencyV1 Blocker(
        string dependencyId,
        Qa04GovernancePublicAuthorityDependencyKindV1 kind,
        string failureCode)
        => new(new StableToken(dependencyId), kind, new StableToken(failureCode));

    private static void RequireField(
        DomainPayloadSchemaDescriptorV1 schema,
        string fieldName,
        DomainPayloadFieldKindV1 kind,
        bool optional)
    {
        var field = schema.Fields.SingleOrDefault(candidate => string.Equals(candidate.Name, fieldName, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"qa04.governance.public-authority-field-missing:{fieldName}");
        if (field.Kind != kind || field.Optional != optional)
            throw new InvalidDataException($"qa04.governance.public-authority-field-drift:{fieldName}");
    }
}
