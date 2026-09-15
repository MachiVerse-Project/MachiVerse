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
/// Production-only material builder for Gate3 domain Snapshot authority. The initial map is backed by
/// the actual canonical Resident identity material plus typed empty runtime roots for every other
/// standard partition. Production materializers then replace those roots with their actual material.
/// Headers are never used to fabricate records.
/// </summary>
public sealed class Qa04ProductionDomainSnapshotAuthorityBuilderV1
{
    private readonly Dictionary<string, IDomainPartitionSnapshotAuthorityV1> _byPartition;

    private Qa04ProductionDomainSnapshotAuthorityBuilderV1(
        IEnumerable<IDomainPartitionSnapshotAuthorityV1> initialAuthorities)
    {
        ArgumentNullException.ThrowIfNull(initialAuthorities);
        _byPartition = initialAuthorities.ToDictionary(
            static authority => authority.PartitionId.Value,
            static authority => authority,
            StringComparer.Ordinal);
        if (_byPartition.Count != StandardDomainPartitionRegistry.StandardPartitionCount)
            throw new InvalidDataException("qa04.gate3.domain-authority.initial-count-not-97");
    }

    public static Qa04ProductionDomainSnapshotAuthorityBuilderV1 CreateInitial(
        Qa04ResidentIdentityMaterializationV1 resident)
    {
        ArgumentNullException.ThrowIfNull(resident);
        var frozenState = resident.WorldState;
        var residentState = new ResidentDomainStateV1(
            resident.Partition,
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

        var initial = StandardDomainSnapshotOwnerCompositionV1.CreateAuthoritySet(
            frozenState,
            residentState.BindSnapshotMaterial(frozenState),
            ParticipationDomainStateV1.CreateEmpty().BindSnapshotMaterial(frozenState),
            PhysicalBuiltDomainStateV1.CreateEmpty().BindSnapshotMaterial(frozenState),
            SpatialDomainStateV1.CreateEmpty().BindSnapshotMaterial(frozenState),
            EnvironmentDomainStateV1.CreateEmpty().BindSnapshotMaterial(frozenState),
            SocietyEconomyDomainStateV1.CreateEmpty().BindSnapshotMaterial(frozenState),
            InfrastructureInformationDomainStateV1.CreateEmpty().BindSnapshotMaterial(frozenState),
            GovernanceSecurityDomainStateV1.CreateEmpty().BindSnapshotMaterial(frozenState));

        return new Qa04ProductionDomainSnapshotAuthorityBuilderV1(initial.CanonicalAuthorities);
    }

    public void Replace<TPayload>(
        DomainPartitionStateV1<TPayload> partition,
        PartitionStateHeaderV1 header,
        Func<TPayload, byte[]> canonicalPayloadDigest)
        => Replace(new DomainPartitionSnapshotAuthorityV1<TPayload>(
            partition ?? throw new ArgumentNullException(nameof(partition)),
            header ?? throw new ArgumentNullException(nameof(header)),
            canonicalPayloadDigest ?? throw new ArgumentNullException(nameof(canonicalPayloadDigest))));

    public void Replace(IDomainPartitionSnapshotAuthorityV1 authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        authority.VerifyBoundAuthority();
        var partitionId = authority.PartitionId.Value;
        if (!_byPartition.ContainsKey(partitionId))
            throw new InvalidDataException($"qa04.gate3.domain-authority.unknown-partition:{partitionId}");
        _byPartition[partitionId] = authority;
    }

    public DomainPartitionSnapshotAuthoritySetV1 Build(WorldStateV1 frozenState)
        => new(
            frozenState ?? throw new ArgumentNullException(nameof(frozenState)),
            StandardDomainPartitionRegistry.Entries.Select(identity => _byPartition[identity.PartitionId.Value]));

    public static DomainPartitionSnapshotAuthoritySetV1 CreateResultingState(
        WorldStateV1 resultingState,
        IEnumerable<IDomainPartitionSnapshotAuthorityV1> basisAuthorities,
        Qa04CanonicalOperationMutationStateV1 mutationState)
    {
        ArgumentNullException.ThrowIfNull(resultingState);
        ArgumentNullException.ThrowIfNull(basisAuthorities);
        ArgumentNullException.ThrowIfNull(mutationState);

        var builder = new Qa04ProductionDomainSnapshotAuthorityBuilderV1(basisAuthorities);
        builder.Replace(
            mutationState.InfrastructureServiceQueue,
            resultingState.Partitions.Get(InfrastructureServiceQueuePayloadV1.PartitionId).Header,
            static payload => payload.CanonicalDigest());
        builder.Replace(
            mutationState.ResidentBehaviorState,
            resultingState.Partitions.Get(ResidentBehaviorStatePayloadV1.PartitionId).Header,
            static payload => payload.CanonicalDigest());
        builder.Replace(
            mutationState.PhysicalPresence,
            resultingState.Partitions.Get(PhysicalPresencePayloadV1.PartitionId).Header,
            static payload => payload.CanonicalDigest());
        builder.Replace(new SocietyMarketTransactionSnapshotAuthorityV2(
            mutationState.MarketTransaction,
            resultingState.Partitions.Get(SocietyMarketTransactionRecordSchemaV2.PartitionId).Header));
        builder.Replace(
            mutationState.GovernanceSecurityIncident,
            resultingState.Partitions.Get(GovernanceSecurityIncidentPayloadV1.PartitionId).Header,
            static payload => payload.CanonicalDigest());
        builder.Replace(
            mutationState.EnvironmentHazard,
            resultingState.Partitions.Get(EnvironmentHazardPayloadV1.PartitionId).Header,
            static payload => payload.CanonicalDigest());

        return builder.Build(resultingState);
    }

    private static DomainPartitionStateV1<TPayload> Empty<TPayload>(string partitionId)
        => new(
            StandardDomainPartitionRegistry.Get(partitionId),
            Array.Empty<DomainRecordEnvelopeV1<TPayload>>());
}
