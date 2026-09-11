using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04GovernancePermissionLicenseResolvedAuthorityV1(
    PartitionRecordRefV1 AuthorityRef,
    StableToken PermissionKind,
    IReadOnlyList<PartitionRecordRefV1> ScopeRefs,
    ulong? EffectiveFrom);

/// <summary>
/// Mechanical materialization boundary for canonical perf.reference.v1 governance.permission_license
/// records after the unresolved PublicAuthority target, permission_kind vocabulary, scope mapping,
/// and payload effective_from have been supplied explicitly.
///
/// Alpha 1.1 already fixes the subject target pool to Resident using canonical modulo mapping,
/// status=active, effective_until=NONE, deterministic conditions_digest, descriptor identity, and
/// the D2 genesis envelope. This type deliberately does not choose any of the remaining four
/// authorities. Supplied refs must close against actual records through the production reference
/// resolver. Fixture authorities prove mechanics only and do not release PermissionLicense blockers.
/// </summary>
public static class Qa04GovernancePermissionLicenseResolvedMaterializerV1
{
    public const ulong CanonicalCount = Qa04GovernancePermissionLicenseDependencyContractV1.CanonicalCount;
    public const ulong InitialRecordRevision = 1;
    public const ulong InitialEnvelopeCreatedStep = 0;

    public static void ValidateCanonicalContract()
    {
        Qa04GovernancePermissionLicenseDependencyContractV1.ValidateCanonicalContract();
        Qa04ReferenceWorldMaterializerV1.ValidateCanonicalContract();

        var blockers = Qa04GovernancePermissionLicenseDependencyContractV1.Blockers;
        var expectedFailureCodes = new HashSet<string>(StringComparer.Ordinal)
        {
            "qa04.material.permission-kind-vocabulary-undefined",
            "qa04.material.permission-public-authority-undefined",
            "qa04.material.permission-scope-mapping-undefined",
            "qa04.material.permission-effective-from-undefined",
        };
        if (blockers.Count != 4 ||
            !blockers.Select(static blocker => blocker.FailureCode.Value).ToHashSet(StringComparer.Ordinal)
                .SetEquals(expectedFailureCodes))
            throw new InvalidDataException("qa04.governance.permission-license-resolved-boundary-stale");

        if (CanonicalCount != 70_000 ||
            Qa04GovernancePermissionLicenseDependencyContractV1.CanonicalStartOrdinal != 1_751_000 ||
            Qa04ReferenceWorldMaterializerV1.CanonicalResidentCount != 1_000_000 ||
            Qa04GovernancePermissionLicenseDependencyContractV1.CanonicalStatus.Value != "active" ||
            InitialRecordRevision != 1 || InitialEnvelopeCreatedStep != 0)
            throw new InvalidDataException("qa04.governance.permission-license-resolved-genesis-drift");
    }

    public static DomainRecordEnvelopeV1<GovernancePermissionLicensePayloadV1> CreateResolved(
        ulong localOrdinal,
        Qa04GovernancePermissionLicenseResolvedAuthorityV1 authority,
        IDomainRecordSchemaResolverV1 referenceResolver,
        out Qa04SocietyGovernanceBindingV1 descriptorBinding)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(referenceResolver);
        ValidateCanonicalContract();
        return CreateResolvedValidated(
            localOrdinal,
            RequireAuthority(authority),
            referenceResolver,
            out descriptorBinding);
    }

    public static IEnumerable<DomainRecordEnvelopeV1<GovernancePermissionLicensePayloadV1>> MaterializeResolved(
        Func<ulong, Qa04GovernancePermissionLicenseResolvedAuthorityV1> authorityForLocalOrdinal,
        IDomainRecordSchemaResolverV1 referenceResolver)
    {
        ArgumentNullException.ThrowIfNull(authorityForLocalOrdinal);
        ArgumentNullException.ThrowIfNull(referenceResolver);
        ValidateCanonicalContract();

        for (ulong localOrdinal = 0; localOrdinal < CanonicalCount; localOrdinal++)
        {
            var authority = authorityForLocalOrdinal(localOrdinal)
                ?? throw new InvalidDataException("qa04.governance.permission-license-authority-required");
            yield return CreateResolvedValidated(
                localOrdinal,
                RequireAuthority(authority),
                referenceResolver,
                out _);
        }
    }

    public static DomainPartitionStateV1<GovernancePermissionLicensePayloadV1> MaterializeResolvedPartition(
        Func<ulong, Qa04GovernancePermissionLicenseResolvedAuthorityV1> authorityForLocalOrdinal,
        IDomainRecordSchemaResolverV1 referenceResolver)
        => new(
            StandardDomainPartitionRegistry.Get(GovernancePermissionLicensePayloadV1.PartitionId),
            MaterializeResolved(authorityForLocalOrdinal, referenceResolver));

    public static PartitionRecordRefV1 ResolveCanonicalSubjectRef(ulong permissionLocalOrdinal)
    {
        if (permissionLocalOrdinal >= CanonicalCount)
            throw new ArgumentOutOfRangeException(nameof(permissionLocalOrdinal));

        var residentOrdinal = permissionLocalOrdinal % Qa04ReferenceWorldMaterializerV1.CanonicalResidentCount;
        var residentRecord = Qa04ReferenceWorldMaterializerV1.CreateResidentRecord(residentOrdinal);
        return new PartitionRecordRefV1(
            ResidentIdentityLifecyclePayloadV1.PartitionId,
            residentRecord.RecordId);
    }

    private static DomainRecordEnvelopeV1<GovernancePermissionLicensePayloadV1> CreateResolvedValidated(
        ulong localOrdinal,
        Qa04GovernancePermissionLicenseResolvedAuthorityV1 authority,
        IDomainRecordSchemaResolverV1 referenceResolver,
        out Qa04SocietyGovernanceBindingV1 descriptorBinding)
    {
        if (localOrdinal >= CanonicalCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));

        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(GovernancePermissionLicensePayloadV1.PartitionId);
        descriptorBinding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(
            checked(slice.StartOrdinal + localOrdinal));
        if (descriptorBinding.PartitionId.Value != GovernancePermissionLicensePayloadV1.PartitionId ||
            descriptorBinding.PartitionLocalOrdinal != localOrdinal || descriptorBinding.UsesSpecializedIdentity)
            throw new InvalidDataException("qa04.governance.permission-license-resolved-descriptor-binding-drift");

        var payload = new GovernancePermissionLicensePayloadV1(
            ResolveCanonicalSubjectRef(localOrdinal),
            authority.AuthorityRef,
            authority.PermissionKind,
            authority.ScopeRefs.ToArray(),
            authority.EffectiveFrom!.Value,
            EffectiveUntil: null,
            Qa04GovernancePermissionLicenseDependencyContractV1.CanonicalStatus,
            Qa04GovernancePermissionLicenseDependencyContractV1.ConditionsDigest(descriptorBinding.Descriptor.RecordId));

        new StandardDomainPayloadCodecValidatorV1().Validate(
            GovernancePermissionLicensePayloadV1.PartitionId,
            payload.ToStandardPayload(),
            referenceResolver);

        var identity = StandardDomainPartitionRegistry.Get(GovernancePermissionLicensePayloadV1.PartitionId);
        return new DomainRecordEnvelopeV1<GovernancePermissionLicensePayloadV1>(
            descriptorBinding.Descriptor.RecordId,
            identity.RecordSchema,
            revision: InitialRecordRevision,
            createdStep: InitialEnvelopeCreatedStep,
            retiredStep: null,
            detailLevel: DetailLevelV1.D2RegionalAggregate,
            lineageRef: null,
            payload);
    }

    private static Qa04GovernancePermissionLicenseResolvedAuthorityV1 RequireAuthority(
        Qa04GovernancePermissionLicenseResolvedAuthorityV1 authority)
    {
        if (authority.AuthorityRef.RecordId.IsZero)
            throw new InvalidDataException("qa04.governance.permission-license-public-authority-required");
        if (string.IsNullOrWhiteSpace(authority.PermissionKind.Value))
            throw new InvalidDataException("qa04.governance.permission-license-kind-authority-required");
        if (authority.ScopeRefs is null || authority.ScopeRefs.Count == 0)
            throw new InvalidDataException("qa04.governance.permission-license-scope-authority-required");
        if (authority.EffectiveFrom is null)
            throw new InvalidDataException("qa04.governance.permission-license-effective-from-authority-required");
        return authority;
    }
}
