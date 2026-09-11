using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Mechanical materialization boundary for canonical perf.reference.v1 society.organization records
/// after an authoritative organization_class mapping has been supplied.
///
/// This type deliberately does not provide a parameterless/canonical materializer. Alpha 1.1 does
/// not yet define the organization_class vocabulary or its ordinal mapping, so callers must provide
/// that authority explicitly. Supplying a fixture mapping proves only the already-decided envelope,
/// identity, lifecycle, and empty-permitted list semantics; it is not release evidence and does not
/// release Qa04SocietyOrganizationDependencyContractV1's vocabulary blocker.
/// </summary>
public static class Qa04SocietyOrganizationResolvedMaterializerV1
{
    public const ulong CanonicalCount = Qa04SocietyOrganizationDependencyContractV1.CanonicalCount;
    public const ulong InitialRecordRevision = 1;
    public const ulong InitialCreatedStep = 0;

    private static readonly IReadOnlyList<StableToken> EmptyTokens = Array.Empty<StableToken>();
    private static readonly IReadOnlyList<PartitionRecordRefV1> EmptyRefs = Array.Empty<PartitionRecordRefV1>();
    private static readonly EmptyReferenceResolver ReferenceResolver = new();

    public static void ValidateCanonicalContract()
    {
        Qa04SocietyOrganizationDependencyContractV1.ValidateCanonicalContract();

        var blockers = Qa04SocietyOrganizationDependencyContractV1.Blockers;
        if (blockers.Count != 1 ||
            blockers[0].Kind != Qa04SocietyOrganizationDependencyKindV1.OrganizationClassVocabulary ||
            blockers[0].FailureCode.Value != "qa04.material.organization-class-vocabulary-undefined")
            throw new InvalidDataException("qa04.society.organization-resolved-boundary-stale");

        if (CanonicalCount != 10_000 ||
            Qa04SocietyOrganizationDependencyContractV1.CanonicalStartOrdinal != 0 ||
            Qa04SocietyOrganizationDependencyContractV1.CanonicalFoundedStep != 0 ||
            Qa04SocietyOrganizationDependencyContractV1.CanonicalLifecycle.Value != "active" ||
            InitialRecordRevision != 1 || InitialCreatedStep != 0)
            throw new InvalidDataException("qa04.society.organization-resolved-genesis-drift");
    }

    public static DomainRecordEnvelopeV1<SocietyOrganizationPayloadV1> CreateResolved(
        ulong localOrdinal,
        StableToken organizationClass,
        out Qa04SocietyGovernanceBindingV1 descriptorBinding)
    {
        ValidateCanonicalContract();
        return CreateResolvedValidated(localOrdinal, RequireOrganizationClass(organizationClass), out descriptorBinding);
    }

    public static IEnumerable<DomainRecordEnvelopeV1<SocietyOrganizationPayloadV1>> MaterializeResolved(
        Func<ulong, StableToken> organizationClassForLocalOrdinal)
    {
        ArgumentNullException.ThrowIfNull(organizationClassForLocalOrdinal);
        ValidateCanonicalContract();

        for (ulong localOrdinal = 0; localOrdinal < CanonicalCount; localOrdinal++)
        {
            var organizationClass = RequireOrganizationClass(organizationClassForLocalOrdinal(localOrdinal));
            yield return CreateResolvedValidated(localOrdinal, organizationClass, out _);
        }
    }

    public static DomainPartitionStateV1<SocietyOrganizationPayloadV1> MaterializeResolvedPartition(
        Func<ulong, StableToken> organizationClassForLocalOrdinal)
        => new(
            StandardDomainPartitionRegistry.Get(SocietyOrganizationPayloadV1.PartitionId),
            MaterializeResolved(organizationClassForLocalOrdinal));

    private static DomainRecordEnvelopeV1<SocietyOrganizationPayloadV1> CreateResolvedValidated(
        ulong localOrdinal,
        StableToken organizationClass,
        out Qa04SocietyGovernanceBindingV1 descriptorBinding)
    {
        if (localOrdinal >= CanonicalCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));

        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(SocietyOrganizationPayloadV1.PartitionId);
        descriptorBinding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(
            checked(slice.StartOrdinal + localOrdinal));
        if (descriptorBinding.PartitionId.Value != SocietyOrganizationPayloadV1.PartitionId ||
            descriptorBinding.PartitionLocalOrdinal != localOrdinal || descriptorBinding.UsesSpecializedIdentity)
            throw new InvalidDataException("qa04.society.organization-resolved-descriptor-binding-drift");

        var payload = new SocietyOrganizationPayloadV1(
            descriptorBinding.Descriptor.RecordId,
            organizationClass,
            Qa04SocietyOrganizationDependencyContractV1.CanonicalLifecycle,
            EmptyTokens,
            EmptyRefs,
            EmptyRefs,
            Qa04SocietyOrganizationDependencyContractV1.CanonicalFoundedStep);

        new StandardDomainPayloadCodecValidatorV1().Validate(
            SocietyOrganizationPayloadV1.PartitionId,
            payload.ToStandardPayload(),
            ReferenceResolver);

        var identity = StandardDomainPartitionRegistry.Get(SocietyOrganizationPayloadV1.PartitionId);
        return new DomainRecordEnvelopeV1<SocietyOrganizationPayloadV1>(
            descriptorBinding.Descriptor.RecordId,
            identity.RecordSchema,
            revision: InitialRecordRevision,
            createdStep: InitialCreatedStep,
            retiredStep: null,
            detailLevel: DetailLevelV1.D2RegionalAggregate,
            lineageRef: null,
            payload);
    }

    private static StableToken RequireOrganizationClass(StableToken organizationClass)
    {
        if (string.IsNullOrWhiteSpace(organizationClass.Value))
            throw new InvalidDataException("qa04.society.organization-class-authority-required");
        return organizationClass;
    }

    private sealed class EmptyReferenceResolver : IDomainRecordSchemaResolverV1
    {
        public bool Exists(PartitionRecordRefV1 reference) => false;

        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
        {
            schema = default;
            return false;
        }
    }
}
