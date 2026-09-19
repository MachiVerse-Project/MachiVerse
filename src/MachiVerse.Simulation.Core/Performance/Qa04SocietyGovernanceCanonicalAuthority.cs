using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Decided perf.reference.v1 benchmark-only Society/Governance authority.
///
/// The explicit perf.* tokens are profile fixtures, not exhaustive gameplay/domain enums. The
/// authority was approved through documentation #295/#296 and synchronized to develop in #297.
/// This class converts that normative decision into the already-proven resolved materializer inputs;
/// dependency blockers are removed only after production-path proof is green.
/// </summary>
public static class Qa04SocietyGovernanceCanonicalAuthorityV1
{
    public static readonly StableToken OrganizationClass = new("perf.organization");
    public static readonly StableToken InstitutionKind = new("perf.institution");
    public static readonly StableToken DecisionMethod = new("perf.decision");
    public static readonly StableToken ContractKind = new("perf.contract");
    public static readonly StableToken ClaimToken = new("perf.claim");
    public static readonly StableToken AuthorityToken = new("perf.authority");
    public static readonly StableToken PermissionKind = new("perf.permission");

    public const ulong GenesisStep = 0;

    public static void ValidateCanonicalContract()
    {
        Qa04SocietyOrganizationResolvedMaterializerV1.ValidateCanonicalContract();
        Qa04GovernanceInstitutionResolvedMaterializerV1.ValidateCanonicalContract();
        Qa04SocietyContractClaimResolvedMaterializerV1.ValidateCanonicalContract();
        Qa04SocietyInformationClaimResolvedMaterializerV1.ValidateCanonicalContract();
        Qa04GovernancePublicAuthorityResolvedMaterializerV1.ValidateCanonicalContract();
        Qa04GovernancePermissionLicenseResolvedMaterializerV1.ValidateCanonicalContract();

        if (OrganizationClass.Value != "perf.organization" ||
            InstitutionKind.Value != "perf.institution" ||
            DecisionMethod.Value != "perf.decision" ||
            ContractKind.Value != "perf.contract" ||
            ClaimToken.Value != "perf.claim" ||
            AuthorityToken.Value != "perf.authority" ||
            PermissionKind.Value != "perf.permission" ||
            GenesisStep != 0)
            throw new InvalidDataException("qa04.society-governance.canonical-authority-drift");
    }

    public static DomainRecordEnvelopeV1<SocietyOrganizationPayloadV1> CreateOrganization(
        ulong localOrdinal,
        out Qa04SocietyGovernanceBindingV1 descriptorBinding)
    {
        ValidateCanonicalContract();
        return Qa04SocietyOrganizationResolvedMaterializerV1.CreateResolved(
            localOrdinal,
            OrganizationClass,
            out descriptorBinding);
    }

    public static DomainRecordEnvelopeV1<GovernanceInstitutionPayloadV1> CreateInstitution(
        ulong localOrdinal,
        IDomainRecordSchemaResolverV1 referenceResolver,
        out Qa04SocietyGovernanceBindingV1 descriptorBinding)
    {
        ValidateCanonicalContract();
        return Qa04GovernanceInstitutionResolvedMaterializerV1.CreateResolved(
            localOrdinal,
            new Qa04GovernanceInstitutionResolvedAuthorityV1(
                InstitutionKind,
                Array.Empty<PartitionRecordRefV1>(),
                DecisionMethod),
            referenceResolver,
            out descriptorBinding);
    }

    public static DomainRecordEnvelopeV1<SocietyContractClaimPayloadV1> CreateContractClaim(
        ulong localOrdinal,
        IDomainRecordSchemaResolverV1 referenceResolver,
        out Qa04SocietyGovernanceBindingV1 descriptorBinding)
    {
        ValidateCanonicalContract();
        return Qa04SocietyContractClaimResolvedMaterializerV1.CreateResolved(
            localOrdinal,
            new Qa04SocietyContractClaimResolvedAuthorityV1(
                ContractKind,
                new[] { ResolveResidentRef(localOrdinal) }),
            referenceResolver,
            out descriptorBinding);
    }

    public static DomainRecordEnvelopeV1<SocietyInformationClaimPayloadV1> CreateInformationClaim(
        ulong localOrdinal,
        IDomainRecordSchemaResolverV1 referenceResolver,
        out Qa04SocietyGovernanceBindingV1 descriptorBinding)
    {
        ValidateCanonicalContract();
        return Qa04SocietyInformationClaimResolvedMaterializerV1.CreateResolved(
            localOrdinal,
            new Qa04SocietyInformationClaimResolvedAuthorityV1(
                ResolveResidentRef(localOrdinal),
                ClaimToken,
                GenesisStep),
            referenceResolver,
            out descriptorBinding);
    }

    public static DomainRecordEnvelopeV1<GovernancePublicAuthorityPayloadV1> CreatePublicAuthority(
        ulong localOrdinal,
        IDomainRecordSchemaResolverV1 referenceResolver,
        out Qa04SocietyGovernanceBindingV1 descriptorBinding)
    {
        ValidateCanonicalContract();
        return Qa04GovernancePublicAuthorityResolvedMaterializerV1.CreateResolved(
            localOrdinal,
            new Qa04GovernancePublicAuthorityResolvedAuthorityV1(
                ResolveInstitutionRef(localOrdinal),
                new[] { AuthorityToken },
                GenesisStep),
            referenceResolver,
            out descriptorBinding);
    }

    public static DomainRecordEnvelopeV1<GovernancePermissionLicensePayloadV1> CreatePermissionLicense(
        ulong localOrdinal,
        IDomainRecordSchemaResolverV1 referenceResolver,
        out Qa04SocietyGovernanceBindingV1 descriptorBinding)
    {
        ValidateCanonicalContract();
        return Qa04GovernancePermissionLicenseResolvedMaterializerV1.CreateResolved(
            localOrdinal,
            new Qa04GovernancePermissionLicenseResolvedAuthorityV1(
                ResolvePublicAuthorityRef(localOrdinal),
                PermissionKind,
                GenesisStep),
            referenceResolver,
            out descriptorBinding);
    }

    public static PartitionRecordRefV1 ResolveResidentRef(ulong localOrdinal)
    {
        var residentOrdinal = localOrdinal % Qa04ReferenceWorldMaterializerV1.CanonicalResidentCount;
        var resident = Qa04ReferenceWorldMaterializerV1.CreateResidentRecord(residentOrdinal);
        return new PartitionRecordRefV1(ResidentIdentityLifecyclePayloadV1.PartitionId, resident.RecordId);
    }

    public static PartitionRecordRefV1 ResolveInstitutionRef(ulong localOrdinal)
        => ResolveDescriptorRef(
            GovernanceInstitutionPayloadV1.PartitionId,
            localOrdinal % Qa04GovernanceInstitutionResolvedMaterializerV1.CanonicalCount);

    public static PartitionRecordRefV1 ResolvePublicAuthorityRef(ulong localOrdinal)
        => ResolveDescriptorRef(
            GovernancePublicAuthorityPayloadV1.PartitionId,
            localOrdinal % Qa04GovernancePublicAuthorityResolvedMaterializerV1.CanonicalCount);

    private static PartitionRecordRefV1 ResolveDescriptorRef(string partitionId, ulong localOrdinal)
    {
        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(partitionId);
        if (localOrdinal >= slice.Count)
            throw new ArgumentOutOfRangeException(nameof(localOrdinal));

        var binding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(
            checked(slice.StartOrdinal + localOrdinal));
        if (binding.PartitionId.Value != partitionId ||
            binding.PartitionLocalOrdinal != localOrdinal ||
            binding.UsesSpecializedIdentity)
            throw new InvalidDataException("qa04.society-governance.canonical-ref-binding-drift");

        return new PartitionRecordRefV1(binding.PartitionId, binding.Descriptor.RecordId);
    }
}
