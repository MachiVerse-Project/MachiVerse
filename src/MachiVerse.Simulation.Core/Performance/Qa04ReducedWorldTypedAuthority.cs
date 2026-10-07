using MachiVerse.Simulation.Core.Domains.Environment;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Domains.Participation;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Binds the reduced QA-04 Resident material slice to the real typed runtime roots for all 97
/// standard partitions. The underlying reduced WorldState may derive unchanged empty partition
/// headers generically, but this boundary proves those headers are exactly the canonical headers of
/// the owner-specific typed empty roots before the reduced world is accepted as a truthful
/// infrastructure fixture. It does not claim perf.reference.v1 materialization.
/// </summary>
public static class Qa04ReducedWorldTypedAuthorityV1
{
    public static DomainPartitionSnapshotAuthoritySetV1 BindAll97(
        Qa04ResidentIdentityMaterializationV1 materialized)
    {
        ArgumentNullException.ThrowIfNull(materialized);
        var frozenState = materialized.WorldState;

        var resident = CreateResidentState(materialized).BindSnapshotMaterial(frozenState);
        var participation = ParticipationDomainStateV1.CreateEmpty().BindSnapshotMaterial(frozenState);
        var physicalBuilt = PhysicalBuiltDomainStateV1.CreateEmpty().BindSnapshotMaterial(frozenState);
        var spatial = SpatialDomainStateV1.CreateEmpty().BindSnapshotMaterial(frozenState);
        var environment = EnvironmentDomainStateV1.CreateEmpty().BindSnapshotMaterial(frozenState);
        var societyEconomy = SocietyEconomyDomainStateV1.CreateEmpty().BindSnapshotMaterial(frozenState);
        var infrastructureInformation = InfrastructureInformationDomainStateV1.CreateEmpty().BindSnapshotMaterial(frozenState);
        var governanceSecurity = GovernanceSecurityDomainStateV1.CreateEmpty().BindSnapshotMaterial(frozenState);

        var authorities = StandardDomainSnapshotOwnerCompositionV1.CreateAuthoritySet(
            frozenState,
            resident,
            participation,
            physicalBuilt,
            spatial,
            environment,
            societyEconomy,
            infrastructureInformation,
            governanceSecurity);

        if (authorities.CanonicalAuthorities.Count != StandardDomainPartitionRegistry.StandardPartitionCount)
            throw new InvalidDataException("qa04.reduced-world.typed-authority-count-not-97");
        if (authorities.CanonicalAuthorities.Sum(static value => checked((long)value.ActualItemCount)) !=
            checked((long)materialized.MaterializedRecordCount))
            throw new InvalidDataException("qa04.reduced-world.typed-authority-record-count-mismatch");

        foreach (var authority in authorities.CanonicalAuthorities)
            authority.VerifyBoundAuthority();

        return authorities;
    }

    private static ResidentDomainStateV1 CreateResidentState(Qa04ResidentIdentityMaterializationV1 materialized)
        => new(
            materialized.Partition,
            Empty<ResidentBodyHealthPayloadV1>(ResidentBodyHealthPayloadV1.PartitionId),
            Empty<ResidentPhysiologyPayloadV1>(ResidentPhysiologyPayloadV1.PartitionId),
            Empty<ResidentPerceptionPayloadV1>(ResidentPerceptionPayloadV1.PartitionId),
            Empty<ResidentKnowledgeBeliefPayloadV1>(ResidentKnowledgeBeliefPayloadV1.PartitionId),
            Empty<ResidentMemoryPayloadV1>(ResidentMemoryPayloadV1.PartitionId),
            Empty<ResidentPsychologyPayloadV1>(ResidentPsychologyPayloadV1.PartitionId),
            Empty<ResidentGoalPlanPayloadV1>(ResidentGoalPlanPayloadV1.PartitionId),
            Empty<ResidentSkillAptitudePayloadV1>(ResidentSkillAptitudePayloadV1.PartitionId),
            Empty<ResidentRelationshipPayloadV1>(ResidentRelationshipPayloadV1.PartitionId),
            Empty<ResidentFamilyLineagePayloadV1>(ResidentFamilyLineagePayloadV1.PartitionId),
            Empty<ResidentBehaviorStatePayloadV1>(ResidentBehaviorStatePayloadV1.PartitionId),
            Empty<ResidentLineagePayloadV1>(ResidentLineagePayloadV1.PartitionId));

    private static DomainPartitionStateV1<TPayload> Empty<TPayload>(string partitionId)
        => new(
            StandardDomainPartitionRegistry.Get(partitionId),
            Array.Empty<DomainRecordEnvelopeV1<TPayload>>());
}
