using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04SocietyContractClaimResolvedAuthorityV1(
    StableToken ContractKind,
    IReadOnlyList<PartitionRecordRefV1> PartyRefs);

/// <summary>
/// Mechanical materialization boundary for canonical perf.reference.v1 society.contract_claim
/// records after the unresolved contract_kind and party_refs authorities have been supplied.
///
/// This type deliberately does not choose a canonical contract_kind or party target pool. The
/// common active status, absent optional fields, deterministic terms_digest, descriptor identity,
/// and D2 genesis envelope are already fixed and are materialized here. Party refs must be nonempty,
/// canonical, and resolvable as actual records through the supplied production reference resolver.
/// Fixture mappings prove mechanics only and do not release the two ContractClaim blockers.
/// </summary>
public static class Qa04SocietyContractClaimResolvedMaterializerV1
{
    public const ulong CanonicalCount = Qa04SocietyContractClaimDependencyContractV1.CanonicalCount;
    public const ulong InitialRecordRevision = 1;
    public const ulong InitialCreatedStep = 0;

    public static void ValidateCanonicalContract()
    {
        Qa04SocietyContractClaimDependencyContractV1.ValidateCanonicalContract();

        var blockers = Qa04SocietyContractClaimDependencyContractV1.Blockers;
        var expectedFailureCodes = new HashSet<string>(StringComparer.Ordinal)
        {
            "qa04.material.contract-kind-vocabulary-undefined",
            "qa04.material.contract-party-mapping-undefined",
        };
        if (blockers.Count != 2 ||
            !blockers.Select(static blocker => blocker.FailureCode.Value).ToHashSet(StringComparer.Ordinal)
                .SetEquals(expectedFailureCodes))
            throw new InvalidDataException("qa04.society.contract-claim-resolved-boundary-stale");

        if (CanonicalCount != 60_000 ||
            Qa04SocietyContractClaimDependencyContractV1.CanonicalStartOrdinal != 210_000 ||
            Qa04SocietyContractClaimDependencyContractV1.CanonicalStatus.Value != "active" ||
            InitialRecordRevision != 1 || InitialCreatedStep != 0)
            throw new InvalidDataException("qa04.society.contract-claim-resolved-genesis-drift");
    }

    public static DomainRecordEnvelopeV1<SocietyContractClaimPayloadV1> CreateResolved(
        ulong localOrdinal,
        Qa04SocietyContractClaimResolvedAuthorityV1 authority,
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

    public static IEnumerable<DomainRecordEnvelopeV1<SocietyContractClaimPayloadV1>> MaterializeResolved(
        Func<ulong, Qa04SocietyContractClaimResolvedAuthorityV1> authorityForLocalOrdinal,
        IDomainRecordSchemaResolverV1 referenceResolver)
    {
        ArgumentNullException.ThrowIfNull(authorityForLocalOrdinal);
        ArgumentNullException.ThrowIfNull(referenceResolver);
        ValidateCanonicalContract();

        for (ulong localOrdinal = 0; localOrdinal < CanonicalCount; localOrdinal++)
        {
            var authority = authorityForLocalOrdinal(localOrdinal)
                ?? throw new InvalidDataException("qa04.society.contract-claim-authority-required");
            yield return CreateResolvedValidated(
                localOrdinal,
                RequireAuthority(authority),
                referenceResolver,
                out _);
        }
    }

    public static DomainPartitionStateV1<SocietyContractClaimPayloadV1> MaterializeResolvedPartition(
        Func<ulong, Qa04SocietyContractClaimResolvedAuthorityV1> authorityForLocalOrdinal,
        IDomainRecordSchemaResolverV1 referenceResolver)
        => new(
            StandardDomainPartitionRegistry.Get(SocietyContractClaimPayloadV1.PartitionId),
            MaterializeResolved(authorityForLocalOrdinal, referenceResolver));

    private static DomainRecordEnvelopeV1<SocietyContractClaimPayloadV1> CreateResolvedValidated(
        ulong localOrdinal,
        Qa04SocietyContractClaimResolvedAuthorityV1 authority,
        IDomainRecordSchemaResolverV1 referenceResolver,
        out Qa04SocietyGovernanceBindingV1 descriptorBinding)
    {
        if (localOrdinal >= CanonicalCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));

        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(SocietyContractClaimPayloadV1.PartitionId);
        descriptorBinding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(
            checked(slice.StartOrdinal + localOrdinal));
        if (descriptorBinding.PartitionId.Value != SocietyContractClaimPayloadV1.PartitionId ||
            descriptorBinding.PartitionLocalOrdinal != localOrdinal || descriptorBinding.UsesSpecializedIdentity)
            throw new InvalidDataException("qa04.society.contract-claim-resolved-descriptor-binding-drift");

        var payload = new SocietyContractClaimPayloadV1(
            authority.ContractKind,
            authority.PartyRefs.ToArray(),
            ClaimantRef: null,
            ObligorRef: null,
            Amount: null,
            Quantity: null,
            DueStep: null,
            Qa04SocietyContractClaimDependencyContractV1.CanonicalStatus,
            Qa04SocietyContractClaimDependencyContractV1.TermsDigest(descriptorBinding.Descriptor.RecordId));

        new StandardDomainPayloadCodecValidatorV1().Validate(
            SocietyContractClaimPayloadV1.PartitionId,
            payload.ToStandardPayload(),
            referenceResolver);

        var identity = StandardDomainPartitionRegistry.Get(SocietyContractClaimPayloadV1.PartitionId);
        return new DomainRecordEnvelopeV1<SocietyContractClaimPayloadV1>(
            descriptorBinding.Descriptor.RecordId,
            identity.RecordSchema,
            revision: InitialRecordRevision,
            createdStep: InitialCreatedStep,
            retiredStep: null,
            detailLevel: DetailLevelV1.D2RegionalAggregate,
            lineageRef: null,
            payload);
    }

    private static Qa04SocietyContractClaimResolvedAuthorityV1 RequireAuthority(
        Qa04SocietyContractClaimResolvedAuthorityV1 authority)
    {
        if (string.IsNullOrWhiteSpace(authority.ContractKind.Value))
            throw new InvalidDataException("qa04.society.contract-kind-authority-required");
        if (authority.PartyRefs is null || authority.PartyRefs.Count == 0)
            throw new InvalidDataException("qa04.society.contract-party-authority-required");
        return authority;
    }
}
