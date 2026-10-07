using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Canonical perf.reference.v1 governance.polity materialization. This slice uses only genesis
/// semantics already frozen by the reference-world decomposition: descriptor RecordId identity,
/// active lifecycle, revision 1 / Step 0 / D2, and empty-permitted canonical Ref lists.
/// </summary>
public static class Qa04GovernancePolityMaterializerV1
{
    public const ulong CanonicalCount = 1_000;

    private static readonly StableToken Active = new("active");
    private static readonly EmptyReferenceResolver ReferenceResolver = new();

    public static void ValidateCanonicalContract()
    {
        Qa04SocietyGovernanceReferenceDecompositionV1.ValidateCanonicalContract();

        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(GovernancePolityPayloadV1.PartitionId);
        if (slice.StartOrdinal != 1_600_000 || slice.Count != CanonicalCount || slice.UsesSpecializedIdentity)
            throw new InvalidDataException("qa04.governance.polity-decomposition-drift");

        var identity = StandardDomainPartitionRegistry.Get(GovernancePolityPayloadV1.PartitionId);
        if (identity.OwnerDomain.Value != "governance_security")
            throw new InvalidDataException("qa04.governance.polity-owner-drift");

        // Prove the exact genesis shape against the production payload validator instead of
        // assuming that a required RefList field must contain a synthetic target.
        var sample = CreatePayload();
        new StandardDomainPayloadCodecValidatorV1().Validate(
            GovernancePolityPayloadV1.PartitionId,
            sample.ToStandardPayload(),
            ReferenceResolver);
    }

    public static DomainRecordEnvelopeV1<GovernancePolityPayloadV1> Create(
        ulong localOrdinal,
        out Qa04SocietyGovernanceBindingV1 descriptorBinding)
    {
        ValidateCanonicalContract();
        return CreateValidated(localOrdinal, out descriptorBinding);
    }

    public static IEnumerable<DomainRecordEnvelopeV1<GovernancePolityPayloadV1>> MaterializeCanonical()
    {
        ValidateCanonicalContract();
        for (ulong localOrdinal = 0; localOrdinal < CanonicalCount; localOrdinal++)
            yield return CreateValidated(localOrdinal, out _);
    }

    public static DomainPartitionStateV1<GovernancePolityPayloadV1> MaterializeCanonicalPartition()
        => new(
            StandardDomainPartitionRegistry.Get(GovernancePolityPayloadV1.PartitionId),
            MaterializeCanonical());

    private static DomainRecordEnvelopeV1<GovernancePolityPayloadV1> CreateValidated(
        ulong localOrdinal,
        out Qa04SocietyGovernanceBindingV1 descriptorBinding)
    {
        if (localOrdinal >= CanonicalCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));

        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(GovernancePolityPayloadV1.PartitionId);
        descriptorBinding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(
            checked(slice.StartOrdinal + localOrdinal));
        if (descriptorBinding.PartitionId.Value != GovernancePolityPayloadV1.PartitionId ||
            descriptorBinding.PartitionLocalOrdinal != localOrdinal || descriptorBinding.UsesSpecializedIdentity)
            throw new InvalidDataException("qa04.governance.polity-descriptor-binding-drift");

        var identity = StandardDomainPartitionRegistry.Get(GovernancePolityPayloadV1.PartitionId);
        return new DomainRecordEnvelopeV1<GovernancePolityPayloadV1>(
            descriptorBinding.Descriptor.RecordId,
            identity.RecordSchema,
            revision: 1,
            createdStep: 0,
            retiredStep: null,
            detailLevel: DetailLevelV1.D2RegionalAggregate,
            lineageRef: null,
            CreatePayload());
    }

    private static GovernancePolityPayloadV1 CreatePayload()
        => new(
            Array.Empty<PartitionRecordRefV1>(),
            Active,
            Array.Empty<PartitionRecordRefV1>(),
            Array.Empty<PartitionRecordRefV1>(),
            Array.Empty<PartitionRecordRefV1>(),
            Array.Empty<PartitionRecordRefV1>(),
            Array.Empty<PartitionRecordRefV1>(),
            Array.Empty<PartitionRecordRefV1>());

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
