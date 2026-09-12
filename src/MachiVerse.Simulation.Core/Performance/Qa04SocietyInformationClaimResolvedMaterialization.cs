using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04SocietyInformationClaimResolvedAuthorityV1(
    PartitionRecordRefV1 ClaimantRef,
    StableToken ClaimToken,
    ulong? CreatedStep);

/// <summary>
/// Mechanical materialization boundary for canonical perf.reference.v1 society.information_claim.
/// Production canonical authority is supplied by Qa04SocietyGovernanceCanonicalAuthorityV1; this
/// explicit-authority surface remains for mechanics and fail-closed Ref validation tests.
/// </summary>
public static class Qa04SocietyInformationClaimResolvedMaterializerV1
{
    public const ulong CanonicalCount = Qa04SocietyInformationClaimDependencyContractV1.CanonicalCount;
    public const ulong InitialRecordRevision = 1;
    public const ulong InitialEnvelopeCreatedStep = 0;

    public static void ValidateCanonicalContract()
    {
        Qa04SocietyInformationClaimDependencyContractV1.ValidateCanonicalContract();

        if (Qa04SocietyInformationClaimDependencyContractV1.Blockers.Count != 0)
            throw new InvalidDataException("qa04.society.info-claim-resolved-boundary-stale");

        if (CanonicalCount != 25_000 ||
            Qa04SocietyInformationClaimDependencyContractV1.CanonicalStartOrdinal != 1_555_200 ||
            Qa04SocietyInformationClaimDependencyContractV1.CanonicalStatus.Value != "active" ||
            InitialRecordRevision != 1 || InitialEnvelopeCreatedStep != 0)
            throw new InvalidDataException("qa04.society.info-claim-resolved-genesis-drift");
    }

    public static DomainRecordEnvelopeV1<SocietyInformationClaimPayloadV1> CreateResolved(
        ulong localOrdinal,
        Qa04SocietyInformationClaimResolvedAuthorityV1 authority,
        IDomainRecordSchemaResolverV1 referenceResolver,
        out Qa04SocietyGovernanceBindingV1 descriptorBinding)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(referenceResolver);
        ValidateCanonicalContract();
        return CreateResolvedValidated(localOrdinal, RequireAuthority(authority), referenceResolver, out descriptorBinding);
    }

    public static IEnumerable<DomainRecordEnvelopeV1<SocietyInformationClaimPayloadV1>> MaterializeResolved(
        Func<ulong, Qa04SocietyInformationClaimResolvedAuthorityV1> authorityForLocalOrdinal,
        IDomainRecordSchemaResolverV1 referenceResolver)
    {
        ArgumentNullException.ThrowIfNull(authorityForLocalOrdinal);
        ArgumentNullException.ThrowIfNull(referenceResolver);
        ValidateCanonicalContract();

        for (ulong localOrdinal = 0; localOrdinal < CanonicalCount; localOrdinal++)
        {
            var authority = authorityForLocalOrdinal(localOrdinal)
                ?? throw new InvalidDataException("qa04.society.info-claim-authority-required");
            yield return CreateResolvedValidated(localOrdinal, RequireAuthority(authority), referenceResolver, out _);
        }
    }

    public static DomainPartitionStateV1<SocietyInformationClaimPayloadV1> MaterializeResolvedPartition(
        Func<ulong, Qa04SocietyInformationClaimResolvedAuthorityV1> authorityForLocalOrdinal,
        IDomainRecordSchemaResolverV1 referenceResolver)
        => new(
            StandardDomainPartitionRegistry.Get(SocietyInformationClaimPayloadV1.PartitionId),
            MaterializeResolved(authorityForLocalOrdinal, referenceResolver));

    private static DomainRecordEnvelopeV1<SocietyInformationClaimPayloadV1> CreateResolvedValidated(
        ulong localOrdinal,
        Qa04SocietyInformationClaimResolvedAuthorityV1 authority,
        IDomainRecordSchemaResolverV1 referenceResolver,
        out Qa04SocietyGovernanceBindingV1 descriptorBinding)
    {
        if (localOrdinal >= CanonicalCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));

        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(SocietyInformationClaimPayloadV1.PartitionId);
        descriptorBinding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(checked(slice.StartOrdinal + localOrdinal));
        if (descriptorBinding.PartitionId.Value != SocietyInformationClaimPayloadV1.PartitionId ||
            descriptorBinding.PartitionLocalOrdinal != localOrdinal || descriptorBinding.UsesSpecializedIdentity)
            throw new InvalidDataException("qa04.society.info-claim-resolved-descriptor-binding-drift");

        var payload = new SocietyInformationClaimPayloadV1(
            authority.ClaimantRef,
            Array.Empty<PartitionRecordRefV1>(),
            authority.ClaimToken,
            Qa04SocietyInformationClaimDependencyContractV1.ContentDigest(descriptorBinding.Descriptor.RecordId),
            Array.Empty<PartitionRecordRefV1>(),
            authority.CreatedStep!.Value,
            Qa04SocietyInformationClaimDependencyContractV1.CanonicalStatus);

        new StandardDomainPayloadCodecValidatorV1().Validate(
            SocietyInformationClaimPayloadV1.PartitionId,
            payload.ToStandardPayload(),
            referenceResolver);

        var identity = StandardDomainPartitionRegistry.Get(SocietyInformationClaimPayloadV1.PartitionId);
        return new DomainRecordEnvelopeV1<SocietyInformationClaimPayloadV1>(
            descriptorBinding.Descriptor.RecordId,
            identity.RecordSchema,
            revision: InitialRecordRevision,
            createdStep: InitialEnvelopeCreatedStep,
            retiredStep: null,
            detailLevel: DetailLevelV1.D2RegionalAggregate,
            lineageRef: null,
            payload);
    }

    private static Qa04SocietyInformationClaimResolvedAuthorityV1 RequireAuthority(Qa04SocietyInformationClaimResolvedAuthorityV1 authority)
    {
        if (authority.ClaimantRef.RecordId.IsZero)
            throw new InvalidDataException("qa04.society.info-claim-claimant-authority-required");
        if (string.IsNullOrWhiteSpace(authority.ClaimToken.Value))
            throw new InvalidDataException("qa04.society.info-claim-token-authority-required");
        if (authority.CreatedStep is null)
            throw new InvalidDataException("qa04.society.info-claim-created-step-authority-required");
        return authority;
    }
}
