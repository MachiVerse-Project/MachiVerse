using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains.Resident;

public sealed class ResidentDomainStateV1
{
    public ResidentDomainStateV1(
        DomainPartitionStateV1<ResidentIdentityLifecyclePayloadV1> identityLifecycle,
        DomainPartitionStateV1<ResidentBodyHealthPayloadV1> bodyHealth,
        DomainPartitionStateV1<ResidentPhysiologyPayloadV1> physiology,
        DomainPartitionStateV1<ResidentPerceptionPayloadV1> perception,
        DomainPartitionStateV1<ResidentKnowledgeBeliefPayloadV1> knowledgeBelief,
        DomainPartitionStateV1<ResidentMemoryPayloadV1> memory,
        DomainPartitionStateV1<ResidentPsychologyPayloadV1> psychology,
        DomainPartitionStateV1<ResidentGoalPlanPayloadV1> goalPlan,
        DomainPartitionStateV1<ResidentSkillAptitudePayloadV1> skillAptitude,
        DomainPartitionStateV1<ResidentRelationshipPayloadV1> relationship,
        DomainPartitionStateV1<ResidentFamilyLineagePayloadV1> familyLineage,
        DomainPartitionStateV1<ResidentBehaviorStatePayloadV1> behaviorState,
        DomainPartitionStateV1<ResidentLineagePayloadV1> lineage)
    {
        IdentityLifecycle = RequireIdentity(identityLifecycle, ResidentIdentityLifecyclePayloadV1.PartitionId);
        BodyHealth = RequireIdentity(bodyHealth, ResidentBodyHealthPayloadV1.PartitionId);
        Physiology = RequireIdentity(physiology, ResidentPhysiologyPayloadV1.PartitionId);
        Perception = RequireIdentity(perception, ResidentPerceptionPayloadV1.PartitionId);
        KnowledgeBelief = RequireIdentity(knowledgeBelief, ResidentKnowledgeBeliefPayloadV1.PartitionId);
        Memory = RequireIdentity(memory, ResidentMemoryPayloadV1.PartitionId);
        Psychology = RequireIdentity(psychology, ResidentPsychologyPayloadV1.PartitionId);
        GoalPlan = RequireIdentity(goalPlan, ResidentGoalPlanPayloadV1.PartitionId);
        SkillAptitude = RequireIdentity(skillAptitude, ResidentSkillAptitudePayloadV1.PartitionId);
        Relationship = RequireIdentity(relationship, ResidentRelationshipPayloadV1.PartitionId);
        FamilyLineage = RequireIdentity(familyLineage, ResidentFamilyLineagePayloadV1.PartitionId);
        BehaviorState = RequireIdentity(behaviorState, ResidentBehaviorStatePayloadV1.PartitionId);
        Lineage = RequireIdentity(lineage, ResidentLineagePayloadV1.PartitionId);
    }

    public DomainPartitionStateV1<ResidentIdentityLifecyclePayloadV1> IdentityLifecycle { get; }
    public DomainPartitionStateV1<ResidentBodyHealthPayloadV1> BodyHealth { get; }
    public DomainPartitionStateV1<ResidentPhysiologyPayloadV1> Physiology { get; }
    public DomainPartitionStateV1<ResidentPerceptionPayloadV1> Perception { get; }
    public DomainPartitionStateV1<ResidentKnowledgeBeliefPayloadV1> KnowledgeBelief { get; }
    public DomainPartitionStateV1<ResidentMemoryPayloadV1> Memory { get; }
    public DomainPartitionStateV1<ResidentPsychologyPayloadV1> Psychology { get; }
    public DomainPartitionStateV1<ResidentGoalPlanPayloadV1> GoalPlan { get; }
    public DomainPartitionStateV1<ResidentSkillAptitudePayloadV1> SkillAptitude { get; }
    public DomainPartitionStateV1<ResidentRelationshipPayloadV1> Relationship { get; }
    public DomainPartitionStateV1<ResidentFamilyLineagePayloadV1> FamilyLineage { get; }
    public DomainPartitionStateV1<ResidentBehaviorStatePayloadV1> BehaviorState { get; }
    public DomainPartitionStateV1<ResidentLineagePayloadV1> Lineage { get; }

    public static ResidentDomainStateV1 CreateEmpty()
        => new(
            Empty<ResidentIdentityLifecyclePayloadV1>(ResidentIdentityLifecyclePayloadV1.PartitionId),
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

    public ResidentDomainSnapshotMaterialV1 BindSnapshotMaterial(WorldStateV1 frozenState)
        => ResidentDomainSnapshotMaterialV1.Bind(frozenState, this);

    private static DomainPartitionStateV1<TPayload> Empty<TPayload>(string partitionId)
        => new(StandardDomainPartitionRegistry.Get(partitionId), Array.Empty<DomainRecordEnvelopeV1<TPayload>>());

    private static DomainPartitionStateV1<TPayload> RequireIdentity<TPayload>(
        DomainPartitionStateV1<TPayload> partition,
        string partitionId)
    {
        ArgumentNullException.ThrowIfNull(partition);
        if (partition.Identity != StandardDomainPartitionRegistry.Get(partitionId))
            throw new InvalidDataException($"resident.runtime-state.partition-identity:{partitionId}");
        return partition;
    }
}

public sealed class ResidentDomainSnapshotMaterialV1
{
    private ResidentDomainSnapshotMaterialV1(IEnumerable<IDomainPartitionSnapshotAuthorityV1> authorities)
    {
        var materialized = authorities?.ToArray() ?? throw new ArgumentNullException(nameof(authorities));
        if (materialized.Length != 13)
            throw new InvalidDataException("resident.snapshot-material.authority-count");

        var byId = new Dictionary<string, IDomainPartitionSnapshotAuthorityV1>(StringComparer.Ordinal);
        foreach (var authority in materialized)
        {
            ArgumentNullException.ThrowIfNull(authority);
            authority.VerifyBoundAuthority();
            if (!string.Equals(authority.Identity.OwnerDomain.Value, "resident", StringComparison.Ordinal))
                throw new InvalidDataException($"resident.snapshot-material.foreign-owner:{authority.PartitionId.Value}");
            if (!byId.TryAdd(authority.PartitionId.Value, authority))
                throw new InvalidDataException($"resident.snapshot-material.duplicate:{authority.PartitionId.Value}");
        }

        IdentityLifecycle = Require<ResidentIdentityLifecyclePayloadV1>(byId, ResidentIdentityLifecyclePayloadV1.PartitionId);
        BodyHealth = Require<ResidentBodyHealthPayloadV1>(byId, ResidentBodyHealthPayloadV1.PartitionId);
        Physiology = Require<ResidentPhysiologyPayloadV1>(byId, ResidentPhysiologyPayloadV1.PartitionId);
        Perception = Require<ResidentPerceptionPayloadV1>(byId, ResidentPerceptionPayloadV1.PartitionId);
        KnowledgeBelief = Require<ResidentKnowledgeBeliefPayloadV1>(byId, ResidentKnowledgeBeliefPayloadV1.PartitionId);
        Memory = Require<ResidentMemoryPayloadV1>(byId, ResidentMemoryPayloadV1.PartitionId);
        Psychology = Require<ResidentPsychologyPayloadV1>(byId, ResidentPsychologyPayloadV1.PartitionId);
        GoalPlan = Require<ResidentGoalPlanPayloadV1>(byId, ResidentGoalPlanPayloadV1.PartitionId);
        SkillAptitude = Require<ResidentSkillAptitudePayloadV1>(byId, ResidentSkillAptitudePayloadV1.PartitionId);
        Relationship = Require<ResidentRelationshipPayloadV1>(byId, ResidentRelationshipPayloadV1.PartitionId);
        FamilyLineage = Require<ResidentFamilyLineagePayloadV1>(byId, ResidentFamilyLineagePayloadV1.PartitionId);
        BehaviorState = Require<ResidentBehaviorStatePayloadV1>(byId, ResidentBehaviorStatePayloadV1.PartitionId);
        Lineage = Require<ResidentLineagePayloadV1>(byId, ResidentLineagePayloadV1.PartitionId);
        Authorities = Array.AsReadOnly(materialized.OrderBy(static value => value.PartitionId.Value, StringComparer.Ordinal).ToArray());
    }

    public DomainPartitionSnapshotAuthorityV1<ResidentIdentityLifecyclePayloadV1> IdentityLifecycle { get; }
    public DomainPartitionSnapshotAuthorityV1<ResidentBodyHealthPayloadV1> BodyHealth { get; }
    public DomainPartitionSnapshotAuthorityV1<ResidentPhysiologyPayloadV1> Physiology { get; }
    public DomainPartitionSnapshotAuthorityV1<ResidentPerceptionPayloadV1> Perception { get; }
    public DomainPartitionSnapshotAuthorityV1<ResidentKnowledgeBeliefPayloadV1> KnowledgeBelief { get; }
    public DomainPartitionSnapshotAuthorityV1<ResidentMemoryPayloadV1> Memory { get; }
    public DomainPartitionSnapshotAuthorityV1<ResidentPsychologyPayloadV1> Psychology { get; }
    public DomainPartitionSnapshotAuthorityV1<ResidentGoalPlanPayloadV1> GoalPlan { get; }
    public DomainPartitionSnapshotAuthorityV1<ResidentSkillAptitudePayloadV1> SkillAptitude { get; }
    public DomainPartitionSnapshotAuthorityV1<ResidentRelationshipPayloadV1> Relationship { get; }
    public DomainPartitionSnapshotAuthorityV1<ResidentFamilyLineagePayloadV1> FamilyLineage { get; }
    public DomainPartitionSnapshotAuthorityV1<ResidentBehaviorStatePayloadV1> BehaviorState { get; }
    public DomainPartitionSnapshotAuthorityV1<ResidentLineagePayloadV1> Lineage { get; }
    public IReadOnlyList<IDomainPartitionSnapshotAuthorityV1> Authorities { get; }

    public static ResidentDomainSnapshotMaterialV1 Bind(WorldStateV1 frozenState, ResidentDomainStateV1 state)
    {
        ArgumentNullException.ThrowIfNull(frozenState);
        ArgumentNullException.ThrowIfNull(state);
        return new ResidentDomainSnapshotMaterialV1(
        [
            Bind(frozenState, state.IdentityLifecycle, ResidentIdentityLifecyclePayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.BodyHealth, ResidentBodyHealthPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.Physiology, ResidentPhysiologyPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.Perception, ResidentPerceptionPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.KnowledgeBelief, ResidentKnowledgeBeliefPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.Memory, ResidentMemoryPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.Psychology, ResidentPsychologyPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.GoalPlan, ResidentGoalPlanPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.SkillAptitude, ResidentSkillAptitudePayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.Relationship, ResidentRelationshipPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.FamilyLineage, ResidentFamilyLineagePayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.BehaviorState, ResidentBehaviorStatePayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.Lineage, ResidentLineagePayloadV1.PartitionId, static value => value.CanonicalDigest()),
        ]);
    }

    private static DomainPartitionSnapshotAuthorityV1<TPayload> Bind<TPayload>(
        WorldStateV1 frozenState,
        DomainPartitionStateV1<TPayload> partition,
        string partitionId,
        Func<TPayload, byte[]> digest)
    {
        if (partition.Identity != StandardDomainPartitionRegistry.Get(partitionId))
            throw new InvalidDataException($"resident.snapshot-material.partition-identity:{partitionId}");
        return new DomainPartitionSnapshotAuthorityV1<TPayload>(
            partition,
            frozenState.Partitions.Get(partitionId).Header,
            digest);
    }

    private static DomainPartitionSnapshotAuthorityV1<TPayload> Require<TPayload>(
        IReadOnlyDictionary<string, IDomainPartitionSnapshotAuthorityV1> byId,
        string partitionId)
    {
        if (!byId.TryGetValue(partitionId, out var authority))
            throw new InvalidDataException($"resident.snapshot-material.missing:{partitionId}");
        return authority as DomainPartitionSnapshotAuthorityV1<TPayload>
            ?? throw new InvalidDataException($"resident.snapshot-material.payload-type:{partitionId}");
    }
}
