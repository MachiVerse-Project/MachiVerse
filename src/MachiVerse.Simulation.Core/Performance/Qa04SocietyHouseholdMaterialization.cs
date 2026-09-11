using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Canonical perf.reference.v1 society.household materialization. This slice intentionally uses
/// only normative fields whose genesis semantics are already fixed: one actual Resident member,
/// empty optional relation lists, active status, and the descriptor RecordId as authoritative id.
/// </summary>
public static class Qa04SocietyHouseholdMaterializerV1
{
    public const ulong CanonicalCount = 40_000;

    private static readonly StableToken ResidentClass = new("resident.persistent-identity");
    private static readonly StableToken Active = new("active");

    public static void ValidateCanonicalContract()
    {
        Qa04ReferenceLoadV1.ValidateCanonicalContract();
        Qa04SocietyGovernanceReferenceDecompositionV1.ValidateCanonicalContract();

        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(SocietyHouseholdPayloadV1.PartitionId);
        if (slice.StartOrdinal != 170_000 || slice.Count != CanonicalCount || slice.UsesSpecializedIdentity)
            throw new InvalidDataException("qa04.society.household-decomposition-drift");

        var identity = StandardDomainPartitionRegistry.Get(SocietyHouseholdPayloadV1.PartitionId);
        if (identity.OwnerDomain.Value != "society_economy")
            throw new InvalidDataException("qa04.society.household-owner-drift");
    }

    public static DomainRecordEnvelopeV1<SocietyHouseholdPayloadV1> Create(
        ulong localOrdinal,
        out Qa04SocietyGovernanceBindingV1 descriptorBinding)
    {
        ValidateCanonicalContract();
        return CreateValidated(localOrdinal, out descriptorBinding);
    }

    public static IEnumerable<DomainRecordEnvelopeV1<SocietyHouseholdPayloadV1>> MaterializeCanonical()
    {
        ValidateCanonicalContract();
        for (ulong localOrdinal = 0; localOrdinal < CanonicalCount; localOrdinal++)
            yield return CreateValidated(localOrdinal, out _);
    }

    public static DomainPartitionStateV1<SocietyHouseholdPayloadV1> MaterializeCanonicalPartition()
        => new(
            StandardDomainPartitionRegistry.Get(SocietyHouseholdPayloadV1.PartitionId),
            MaterializeCanonical());

    private static DomainRecordEnvelopeV1<SocietyHouseholdPayloadV1> CreateValidated(
        ulong localOrdinal,
        out Qa04SocietyGovernanceBindingV1 descriptorBinding)
    {
        if (localOrdinal >= CanonicalCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));

        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(SocietyHouseholdPayloadV1.PartitionId);
        descriptorBinding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(
            checked(slice.StartOrdinal + localOrdinal));
        if (descriptorBinding.PartitionId.Value != SocietyHouseholdPayloadV1.PartitionId ||
            descriptorBinding.PartitionLocalOrdinal != localOrdinal || descriptorBinding.UsesSpecializedIdentity)
            throw new InvalidDataException("qa04.society.household-descriptor-binding-drift");

        var resident = Qa04ReferenceLoadV1.Record(ResidentClass, localOrdinal);
        var memberRef = new PartitionRecordRefV1("resident.identity_lifecycle", resident.RecordId);
        var payload = new SocietyHouseholdPayloadV1(
            new[] { memberRef },
            Array.Empty<PartitionRecordRefV1>(),
            Array.Empty<PartitionRecordRefV1>(),
            Array.Empty<PartitionRecordRefV1>(),
            Active);
        var identity = StandardDomainPartitionRegistry.Get(SocietyHouseholdPayloadV1.PartitionId);

        return new DomainRecordEnvelopeV1<SocietyHouseholdPayloadV1>(
            descriptorBinding.Descriptor.RecordId,
            identity.RecordSchema,
            revision: 1,
            createdStep: 0,
            retiredStep: null,
            detailLevel: DetailLevelV1.D2RegionalAggregate,
            lineageRef: null,
            payload);
    }
}
