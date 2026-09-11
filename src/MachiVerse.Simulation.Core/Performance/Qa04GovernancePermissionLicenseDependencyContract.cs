using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public enum Qa04GovernancePermissionLicenseDependencyKindV1 : byte
{
    PermissionKindVocabulary = 1,
    PublicAuthorityTarget = 2,
    ScopeAuthorityMapping = 3,
    GenesisEffectiveFrom = 4,
}

public sealed record Qa04GovernancePermissionLicenseDependencyV1(
    StableToken DependencyId,
    Qa04GovernancePermissionLicenseDependencyKindV1 Kind,
    StableToken FailureCode);

/// <summary>
/// Exact fail-closed boundary for the canonical 70,000 governance.permission_license records.
///
/// Alpha 1.1 already fixes the descriptor range, non-specialized authoritative RecordId mapping,
/// D2 genesis envelope, subject selector (Resident), status=active, optional effective_until=NONE,
/// and the deterministic conditions_digest scalar source. The remaining semantic gaps are the
/// canonical permission_kind vocabulary, actual PublicAuthority target authority, the meaning and
/// target pool for required scope_refs, and the canonical genesis effective_from Step. None of
/// those are synthesized by the generic scalar source.
/// </summary>
public static class Qa04GovernancePermissionLicenseDependencyContractV1
{
    public const string PartitionId = "governance.permission_license";
    public const string AuthorityPartitionId = "governance.public_authority";
    public const string SubjectPartitionId = "resident.identity_lifecycle";
    public const ulong CanonicalStartOrdinal = 1_751_000;
    public const ulong CanonicalCount = 70_000;
    public const string ConditionsDigestFieldTag = "governance.permission_license.conditions_digest";

    public static readonly StableToken CanonicalStatus = new("active");

    private static readonly IReadOnlyList<Qa04GovernancePermissionLicenseDependencyV1> BlockersValue =
        Array.AsReadOnly(new[]
        {
            Blocker(
                "governance.permission-license.permission-kind-vocabulary",
                Qa04GovernancePermissionLicenseDependencyKindV1.PermissionKindVocabulary,
                "qa04.material.permission-kind-vocabulary-undefined"),
            Blocker(
                "governance.permission-license.public-authority",
                Qa04GovernancePermissionLicenseDependencyKindV1.PublicAuthorityTarget,
                "qa04.material.permission-public-authority-undefined"),
            Blocker(
                "governance.permission-license.scope-ref-mapping",
                Qa04GovernancePermissionLicenseDependencyKindV1.ScopeAuthorityMapping,
                "qa04.material.permission-scope-mapping-undefined"),
            Blocker(
                "governance.permission-license.effective-from-genesis",
                Qa04GovernancePermissionLicenseDependencyKindV1.GenesisEffectiveFrom,
                "qa04.material.permission-effective-from-undefined"),
        }
        .OrderBy(static blocker => blocker.DependencyId.Value, StringComparer.Ordinal)
        .ToArray());

    public static IReadOnlyList<Qa04GovernancePermissionLicenseDependencyV1> Blockers => BlockersValue;

    public static byte[] ConditionsDigest(OpaqueId128 recordId)
        => Qa04ReferenceGenesisValueSourceV1.Hash(recordId, ConditionsDigestFieldTag);

    public static void ValidateCanonicalContract()
    {
        Qa04SocietyGovernanceReferenceDecompositionV1.ValidateCanonicalContract();

        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(PartitionId);
        if (slice.StartOrdinal != CanonicalStartOrdinal || slice.Count != CanonicalCount || slice.UsesSpecializedIdentity)
            throw new InvalidDataException("qa04.governance.permission-license-decomposition-drift");

        var identity = StandardDomainPartitionRegistry.Get(PartitionId);
        if (identity.OwnerDomain.Value != "governance_security")
            throw new InvalidDataException("qa04.governance.permission-license-owner-drift");
        if (StandardDomainPartitionRegistry.Get(AuthorityPartitionId).OwnerDomain.Value != "governance_security" ||
            StandardDomainPartitionRegistry.Get(SubjectPartitionId).OwnerDomain.Value != "resident")
            throw new InvalidDataException("qa04.governance.permission-license-ref-owner-drift");

        var schema = StandardDomainPayloadSchemaRegistry.Get(PartitionId);
        RequireField(schema, "subject_ref", DomainPayloadFieldKindV1.Ref, optional: false);
        RequireField(schema, "authority_ref", DomainPayloadFieldKindV1.Ref, optional: false);
        RequireField(schema, "permission_kind", DomainPayloadFieldKindV1.Token, optional: false);
        RequireField(schema, "scope_refs", DomainPayloadFieldKindV1.RefList, optional: false);
        RequireField(schema, "effective_from", DomainPayloadFieldKindV1.Step, optional: false);
        RequireField(schema, "effective_until", DomainPayloadFieldKindV1.Step, optional: true);
        RequireField(schema, "status", DomainPayloadFieldKindV1.Token, optional: false);
        RequireField(schema, "conditions_digest", DomainPayloadFieldKindV1.Digest, optional: false);

        var probe = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(CanonicalStartOrdinal);
        if (probe.PartitionId.Value != PartitionId || probe.Descriptor.RecordId.IsZero ||
            ConditionsDigest(probe.Descriptor.RecordId).Length != 32 || CanonicalStatus.Value != "active")
            throw new InvalidDataException("qa04.governance.permission-license-known-genesis-drift");

        if (BlockersValue.Count != 4 ||
            BlockersValue.Select(static blocker => blocker.Kind).Distinct().Count() != BlockersValue.Count ||
            BlockersValue.Select(static blocker => blocker.DependencyId).Distinct().Count() != BlockersValue.Count ||
            BlockersValue.Select(static blocker => blocker.FailureCode).Distinct().Count() != BlockersValue.Count)
            throw new InvalidDataException("qa04.governance.permission-license-dependency-drift");
    }

    private static Qa04GovernancePermissionLicenseDependencyV1 Blocker(
        string dependencyId,
        Qa04GovernancePermissionLicenseDependencyKindV1 kind,
        string failureCode)
        => new(new StableToken(dependencyId), kind, new StableToken(failureCode));

    private static void RequireField(
        DomainPayloadSchemaDescriptorV1 schema,
        string fieldName,
        DomainPayloadFieldKindV1 kind,
        bool optional)
    {
        var field = schema.Fields.SingleOrDefault(candidate => string.Equals(candidate.Name, fieldName, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"qa04.governance.permission-license-field-missing:{fieldName}");
        if (field.Kind != kind || field.Optional != optional)
            throw new InvalidDataException($"qa04.governance.permission-license-field-drift:{fieldName}");
    }
}
