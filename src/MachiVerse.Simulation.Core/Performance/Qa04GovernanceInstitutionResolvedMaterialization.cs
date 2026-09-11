using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04GovernanceInstitutionResolvedAuthorityV1(
    StableToken InstitutionKind,
    IReadOnlyList<PartitionRecordRefV1> OfficeRefs,
    StableToken DecisionMethod);

/// <summary>
/// Mechanical materialization boundary for canonical perf.reference.v1 governance.institution
/// records after the unresolved Institution authorities have been supplied explicitly.
///
/// The canonical Polity target is not caller-selectable: Alpha 1.1 fixes required relation refs to
/// their canonical target pool using modulo mapping, and governance.polity already has actual
/// authority. The institution_kind / decision_method vocabularies and office_refs semantics remain
/// unresolved, so this type deliberately provides no parameterless canonical materializer and does
/// not remove Qa04GovernanceInstitutionDependencyContractV1 blockers.
/// </summary>
public static class Qa04GovernanceInstitutionResolvedMaterializerV1
{
    public const ulong CanonicalCount = Qa04GovernanceInstitutionDependencyContractV1.CanonicalCount;
    public const ulong InitialRecordRevision = 1;
    public const ulong InitialCreatedStep = 0;

    public static void ValidateCanonicalContract()
    {
        Qa04GovernanceInstitutionDependencyContractV1.ValidateCanonicalContract();
        Qa04GovernancePolityMaterializerV1.ValidateCanonicalContract();

        var blockers = Qa04GovernanceInstitutionDependencyContractV1.Blockers;
        var expectedFailureCodes = new HashSet<string>(StringComparer.Ordinal)
        {
            "qa04.material.institution-kind-vocabulary-undefined",
            "qa04.material.decision-method-vocabulary-undefined",
            "qa04.material.institution-office-mapping-undefined",
        };
        if (blockers.Count != 3 ||
            !blockers.Select(static blocker => blocker.FailureCode.Value).ToHashSet(StringComparer.Ordinal)
                .SetEquals(expectedFailureCodes))
            throw new InvalidDataException("qa04.governance.institution-resolved-boundary-stale");

        if (CanonicalCount != 5_000 ||
            Qa04GovernanceInstitutionDependencyContractV1.CanonicalStartOrdinal != 1_601_000 ||
            Qa04GovernancePolityMaterializerV1.CanonicalCount != 1_000 ||
            Qa04GovernanceInstitutionDependencyContractV1.CanonicalLifecycle.Value != "active" ||
            InitialRecordRevision != 1 || InitialCreatedStep != 0)
            throw new InvalidDataException("qa04.governance.institution-resolved-genesis-drift");
    }

    public static DomainRecordEnvelopeV1<GovernanceInstitutionPayloadV1> CreateResolved(
        ulong localOrdinal,
        Qa04GovernanceInstitutionResolvedAuthorityV1 authority,
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

    public static IEnumerable<DomainRecordEnvelopeV1<GovernanceInstitutionPayloadV1>> MaterializeResolved(
        Func<ulong, Qa04GovernanceInstitutionResolvedAuthorityV1> authorityForLocalOrdinal,
        IDomainRecordSchemaResolverV1 referenceResolver)
    {
        ArgumentNullException.ThrowIfNull(authorityForLocalOrdinal);
        ArgumentNullException.ThrowIfNull(referenceResolver);
        ValidateCanonicalContract();

        for (ulong localOrdinal = 0; localOrdinal < CanonicalCount; localOrdinal++)
        {
            var authority = authorityForLocalOrdinal(localOrdinal)
                ?? throw new InvalidDataException("qa04.governance.institution-authority-required");
            yield return CreateResolvedValidated(
                localOrdinal,
                RequireAuthority(authority),
                referenceResolver,
                out _);
        }
    }

    public static DomainPartitionStateV1<GovernanceInstitutionPayloadV1> MaterializeResolvedPartition(
        Func<ulong, Qa04GovernanceInstitutionResolvedAuthorityV1> authorityForLocalOrdinal,
        IDomainRecordSchemaResolverV1 referenceResolver)
        => new(
            StandardDomainPartitionRegistry.Get(GovernanceInstitutionPayloadV1.PartitionId),
            MaterializeResolved(authorityForLocalOrdinal, referenceResolver));

    public static PartitionRecordRefV1 ResolveCanonicalPolityRef(ulong institutionLocalOrdinal)
    {
        if (institutionLocalOrdinal >= CanonicalCount)
            throw new ArgumentOutOfRangeException(nameof(institutionLocalOrdinal));

        var politySlice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(GovernancePolityPayloadV1.PartitionId);
        var polityLocalOrdinal = institutionLocalOrdinal % Qa04GovernancePolityMaterializerV1.CanonicalCount;
        var binding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(
            checked(politySlice.StartOrdinal + polityLocalOrdinal));
        if (binding.PartitionId.Value != GovernancePolityPayloadV1.PartitionId ||
            binding.PartitionLocalOrdinal != polityLocalOrdinal || binding.UsesSpecializedIdentity)
            throw new InvalidDataException("qa04.governance.institution-polity-binding-drift");
        return new PartitionRecordRefV1(binding.PartitionId, binding.Descriptor.RecordId);
    }

    private static DomainRecordEnvelopeV1<GovernanceInstitutionPayloadV1> CreateResolvedValidated(
        ulong localOrdinal,
        Qa04GovernanceInstitutionResolvedAuthorityV1 authority,
        IDomainRecordSchemaResolverV1 referenceResolver,
        out Qa04SocietyGovernanceBindingV1 descriptorBinding)
    {
        if (localOrdinal >= CanonicalCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));

        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(GovernanceInstitutionPayloadV1.PartitionId);
        descriptorBinding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(
            checked(slice.StartOrdinal + localOrdinal));
        if (descriptorBinding.PartitionId.Value != GovernanceInstitutionPayloadV1.PartitionId ||
            descriptorBinding.PartitionLocalOrdinal != localOrdinal || descriptorBinding.UsesSpecializedIdentity)
            throw new InvalidDataException("qa04.governance.institution-resolved-descriptor-binding-drift");

        var payload = new GovernanceInstitutionPayloadV1(
            ResolveCanonicalPolityRef(localOrdinal),
            authority.InstitutionKind,
            authority.OfficeRefs.ToArray(),
            authority.DecisionMethod,
            SelectionRuleRef: null,
            Qa04GovernanceInstitutionDependencyContractV1.CanonicalLifecycle);

        new StandardDomainPayloadCodecValidatorV1().Validate(
            GovernanceInstitutionPayloadV1.PartitionId,
            payload.ToStandardPayload(),
            referenceResolver);

        var identity = StandardDomainPartitionRegistry.Get(GovernanceInstitutionPayloadV1.PartitionId);
        return new DomainRecordEnvelopeV1<GovernanceInstitutionPayloadV1>(
            descriptorBinding.Descriptor.RecordId,
            identity.RecordSchema,
            revision: InitialRecordRevision,
            createdStep: InitialCreatedStep,
            retiredStep: null,
            detailLevel: DetailLevelV1.D2RegionalAggregate,
            lineageRef: null,
            payload);
    }

    private static Qa04GovernanceInstitutionResolvedAuthorityV1 RequireAuthority(
        Qa04GovernanceInstitutionResolvedAuthorityV1 authority)
    {
        if (string.IsNullOrWhiteSpace(authority.InstitutionKind.Value))
            throw new InvalidDataException("qa04.governance.institution-kind-authority-required");
        if (string.IsNullOrWhiteSpace(authority.DecisionMethod.Value))
            throw new InvalidDataException("qa04.governance.institution-decision-method-authority-required");
        if (authority.OfficeRefs is null)
            throw new InvalidDataException("qa04.governance.institution-office-authority-required");
        return authority;
    }
}
