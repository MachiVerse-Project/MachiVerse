using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04GovernancePublicAuthorityResolvedAuthorityV1(
    PartitionRecordRefV1 InstitutionRef,
    IReadOnlyList<StableToken> AuthorityTokens,
    IReadOnlyList<PartitionRecordRefV1> ScopeRefs,
    ulong? EffectiveFrom);

/// <summary>
/// Mechanical materialization boundary for canonical perf.reference.v1 governance.public_authority
/// records after the unresolved Institution authority, authority-token vocabulary, scope mapping,
/// and payload effective_from have been supplied explicitly.
///
/// Alpha 1.1 already fixes the holder target pool to Resident using canonical modulo mapping,
/// status=active, effective_until=NONE, descriptor identity, and the D2 genesis envelope. This type
/// deliberately provides no parameterless canonical materializer and does not choose any of the
/// remaining four authorities. Supplied refs must close against actual records through the provided
/// production reference resolver. Fixture authorities prove mechanics only and do not release the
/// PublicAuthority blockers.
/// </summary>
public static class Qa04GovernancePublicAuthorityResolvedMaterializerV1
{
    public const ulong CanonicalCount = Qa04GovernancePublicAuthorityDependencyContractV1.CanonicalCount;
    public const ulong InitialRecordRevision = 1;
    public const ulong InitialEnvelopeCreatedStep = 0;

    public static void ValidateCanonicalContract()
    {
        Qa04GovernancePublicAuthorityDependencyContractV1.ValidateCanonicalContract();
        Qa04ReferenceWorldMaterializerV1.ValidateCanonicalContract();

        var blockers = Qa04GovernancePublicAuthorityDependencyContractV1.Blockers;
        var expectedFailureCodes = new HashSet<string>(StringComparer.Ordinal)
        {
            "qa04.material.public-authority-institution-undefined",
            "qa04.material.public-authority-token-vocabulary-undefined",
            "qa04.material.public-authority-scope-mapping-undefined",
            "qa04.material.public-authority-effective-from-undefined",
        };
        if (blockers.Count != 4 ||
            !blockers.Select(static blocker => blocker.FailureCode.Value).ToHashSet(StringComparer.Ordinal)
                .SetEquals(expectedFailureCodes))
            throw new InvalidDataException("qa04.governance.public-authority-resolved-boundary-stale");

        if (CanonicalCount != 25_000 ||
            Qa04GovernancePublicAuthorityDependencyContractV1.CanonicalStartOrdinal != 1_676_000 ||
            Qa04ReferenceWorldMaterializerV1.CanonicalResidentCount != 1_000_000 ||
            Qa04GovernancePublicAuthorityDependencyContractV1.CanonicalStatus.Value != "active" ||
            InitialRecordRevision != 1 || InitialEnvelopeCreatedStep != 0)
            throw new InvalidDataException("qa04.governance.public-authority-resolved-genesis-drift");
    }

    public static DomainRecordEnvelopeV1<GovernancePublicAuthorityPayloadV1> CreateResolved(
        ulong localOrdinal,
        Qa04GovernancePublicAuthorityResolvedAuthorityV1 authority,
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

    public static IEnumerable<DomainRecordEnvelopeV1<GovernancePublicAuthorityPayloadV1>> MaterializeResolved(
        Func<ulong, Qa04GovernancePublicAuthorityResolvedAuthorityV1> authorityForLocalOrdinal,
        IDomainRecordSchemaResolverV1 referenceResolver)
    {
        ArgumentNullException.ThrowIfNull(authorityForLocalOrdinal);
        ArgumentNullException.ThrowIfNull(referenceResolver);
        ValidateCanonicalContract();

        for (ulong localOrdinal = 0; localOrdinal < CanonicalCount; localOrdinal++)
        {
            var authority = authorityForLocalOrdinal(localOrdinal)
                ?? throw new InvalidDataException("qa04.governance.public-authority-authority-required");
            yield return CreateResolvedValidated(
                localOrdinal,
                RequireAuthority(authority),
                referenceResolver,
                out _);
        }
    }

    public static DomainPartitionStateV1<GovernancePublicAuthorityPayloadV1> MaterializeResolvedPartition(
        Func<ulong, Qa04GovernancePublicAuthorityResolvedAuthorityV1> authorityForLocalOrdinal,
        IDomainRecordSchemaResolverV1 referenceResolver)
        => new(
            StandardDomainPartitionRegistry.Get(GovernancePublicAuthorityPayloadV1.PartitionId),
            MaterializeResolved(authorityForLocalOrdinal, referenceResolver));

    public static PartitionRecordRefV1 ResolveCanonicalHolderRef(ulong publicAuthorityLocalOrdinal)
    {
        if (publicAuthorityLocalOrdinal >= CanonicalCount)
            throw new ArgumentOutOfRangeException(nameof(publicAuthorityLocalOrdinal));

        var residentOrdinal = publicAuthorityLocalOrdinal % Qa04ReferenceWorldMaterializerV1.CanonicalResidentCount;
        var residentRecord = Qa04ReferenceWorldMaterializerV1.CreateResidentRecord(residentOrdinal);
        return new PartitionRecordRefV1(
            ResidentIdentityLifecyclePayloadV1.PartitionId,
            residentRecord.RecordId);
    }

    private static DomainRecordEnvelopeV1<GovernancePublicAuthorityPayloadV1> CreateResolvedValidated(
        ulong localOrdinal,
        Qa04GovernancePublicAuthorityResolvedAuthorityV1 authority,
        IDomainRecordSchemaResolverV1 referenceResolver,
        out Qa04SocietyGovernanceBindingV1 descriptorBinding)
    {
        if (localOrdinal >= CanonicalCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));

        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(GovernancePublicAuthorityPayloadV1.PartitionId);
        descriptorBinding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(
            checked(slice.StartOrdinal + localOrdinal));
        if (descriptorBinding.PartitionId.Value != GovernancePublicAuthorityPayloadV1.PartitionId ||
            descriptorBinding.PartitionLocalOrdinal != localOrdinal || descriptorBinding.UsesSpecializedIdentity)
            throw new InvalidDataException("qa04.governance.public-authority-resolved-descriptor-binding-drift");

        var payload = new GovernancePublicAuthorityPayloadV1(
            authority.InstitutionRef,
            ResolveCanonicalHolderRef(localOrdinal),
            authority.AuthorityTokens.ToArray(),
            authority.ScopeRefs.ToArray(),
            authority.EffectiveFrom!.Value,
            EffectiveUntil: null,
            Qa04GovernancePublicAuthorityDependencyContractV1.CanonicalStatus);

        new StandardDomainPayloadCodecValidatorV1().Validate(
            GovernancePublicAuthorityPayloadV1.PartitionId,
            payload.ToStandardPayload(),
            referenceResolver);

        var identity = StandardDomainPartitionRegistry.Get(GovernancePublicAuthorityPayloadV1.PartitionId);
        return new DomainRecordEnvelopeV1<GovernancePublicAuthorityPayloadV1>(
            descriptorBinding.Descriptor.RecordId,
            identity.RecordSchema,
            revision: InitialRecordRevision,
            createdStep: InitialEnvelopeCreatedStep,
            retiredStep: null,
            detailLevel: DetailLevelV1.D2RegionalAggregate,
            lineageRef: null,
            payload);
    }

    private static Qa04GovernancePublicAuthorityResolvedAuthorityV1 RequireAuthority(
        Qa04GovernancePublicAuthorityResolvedAuthorityV1 authority)
    {
        if (authority.InstitutionRef.RecordId.IsZero)
            throw new InvalidDataException("qa04.governance.public-authority-institution-authority-required");
        if (authority.AuthorityTokens is null || authority.AuthorityTokens.Count == 0)
            throw new InvalidDataException("qa04.governance.public-authority-token-authority-required");
        if (authority.ScopeRefs is null || authority.ScopeRefs.Count == 0)
            throw new InvalidDataException("qa04.governance.public-authority-scope-authority-required");
        if (authority.EffectiveFrom is null)
            throw new InvalidDataException("qa04.governance.public-authority-effective-from-authority-required");
        return authority;
    }
}
