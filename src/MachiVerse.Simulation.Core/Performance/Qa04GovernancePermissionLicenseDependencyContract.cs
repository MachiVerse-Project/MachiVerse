using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public enum Qa04GovernancePermissionLicenseDependencyKindV1 : byte
{
    PermissionKindVocabulary = 1,
    PublicAuthorityTarget = 2,
    GenesisEffectiveFrom = 3,
}

public sealed record Qa04GovernancePermissionLicenseDependencyV1(
    StableToken DependencyId,
    Qa04GovernancePermissionLicenseDependencyKindV1 Kind,
    StableToken FailureCode);

/// <summary>
/// Canonical 70,000 governance.permission_license dependency boundary.
/// Benchmark-only permission kind, actual PublicAuthority mapping and effective_from=0 are decided
/// by the Alpha 1.1 Society/Governance amendment and production-proven by the canonical materializer.
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
        Array.AsReadOnly(Array.Empty<Qa04GovernancePermissionLicenseDependencyV1>());

    public static IReadOnlyList<Qa04GovernancePermissionLicenseDependencyV1> Blockers => BlockersValue;

    public static byte[] ConditionsDigest(OpaqueId128 recordId)
        => Qa04ReferenceGenesisValueSourceV1.Hash(recordId, ConditionsDigestFieldTag);

    public static void ValidateCanonicalContract()
    {
        Qa04SocietyGovernanceReferenceDecompositionV1.ValidateCanonicalContract();
        Qa04GovernancePublicAuthorityDependencyContractV1.ValidateCanonicalContract();
        Qa04SpatialTileScopeAuthorityV1.ValidateCanonicalContract();

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
            ConditionsDigest(probe.Descriptor.RecordId).Length != 32 || CanonicalStatus.Value != "active" ||
            Qa04GovernancePublicAuthorityDependencyContractV1.Blockers.Count != 0 ||
            Qa04SpatialTileScopeAuthorityV1.CanonicalScopeCount != 4_096 ||
            BlockersValue.Count != 0)
            throw new InvalidDataException("qa04.governance.permission-license-known-genesis-drift");
    }

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
