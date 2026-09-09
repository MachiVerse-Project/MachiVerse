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
        IdentityLifecycle=R(identityLifecycle,ResidentIdentityLifecyclePayloadV1.PartitionId); BodyHealth=R(bodyHealth,ResidentBodyHealthPayloadV1.PartitionId);
        Physiology=R(physiology,ResidentPhysiologyPayloadV1.PartitionId); Perception=R(perception,ResidentPerceptionPayloadV1.PartitionId);
        KnowledgeBelief=R(knowledgeBelief,ResidentKnowledgeBeliefPayloadV1.PartitionId); Memory=R(memory,ResidentMemoryPayloadV1.PartitionId);
        Psychology=R(psychology,ResidentPsychologyPayloadV1.PartitionId); GoalPlan=R(goalPlan,ResidentGoalPlanPayloadV1.PartitionId);
        SkillAptitude=R(skillAptitude,ResidentSkillAptitudePayloadV1.PartitionId); Relationship=R(relationship,ResidentRelationshipPayloadV1.PartitionId);
        FamilyLineage=R(familyLineage,ResidentFamilyLineagePayloadV1.PartitionId); BehaviorState=R(behaviorState,ResidentBehaviorStatePayloadV1.PartitionId);
        Lineage=R(lineage,ResidentLineagePayloadV1.PartitionId);
    }
    public DomainPartitionStateV1<ResidentIdentityLifecyclePayloadV1> IdentityLifecycle{get;}
    public DomainPartitionStateV1<ResidentBodyHealthPayloadV1> BodyHealth{get;}
    public DomainPartitionStateV1<ResidentPhysiologyPayloadV1> Physiology{get;}
    public DomainPartitionStateV1<ResidentPerceptionPayloadV1> Perception{get;}
    public DomainPartitionStateV1<ResidentKnowledgeBeliefPayloadV1> KnowledgeBelief{get;}
    public DomainPartitionStateV1<ResidentMemoryPayloadV1> Memory{get;}
    public DomainPartitionStateV1<ResidentPsychologyPayloadV1> Psychology{get;}
    public DomainPartitionStateV1<ResidentGoalPlanPayloadV1> GoalPlan{get;}
    public DomainPartitionStateV1<ResidentSkillAptitudePayloadV1> SkillAptitude{get;}
    public DomainPartitionStateV1<ResidentRelationshipPayloadV1> Relationship{get;}
    public DomainPartitionStateV1<ResidentFamilyLineagePayloadV1> FamilyLineage{get;}
    public DomainPartitionStateV1<ResidentBehaviorStatePayloadV1> BehaviorState{get;}
    public DomainPartitionStateV1<ResidentLineagePayloadV1> Lineage{get;}

    public static ResidentDomainStateV1 CreateEmpty()=>new(
        E<ResidentIdentityLifecyclePayloadV1>(ResidentIdentityLifecyclePayloadV1.PartitionId),E<ResidentBodyHealthPayloadV1>(ResidentBodyHealthPayloadV1.PartitionId),
        E<ResidentPhysiologyPayloadV1>(ResidentPhysiologyPayloadV1.PartitionId),E<ResidentPerceptionPayloadV1>(ResidentPerceptionPayloadV1.PartitionId),
        E<ResidentKnowledgeBeliefPayloadV1>(ResidentKnowledgeBeliefPayloadV1.PartitionId),E<ResidentMemoryPayloadV1>(ResidentMemoryPayloadV1.PartitionId),
        E<ResidentPsychologyPayloadV1>(ResidentPsychologyPayloadV1.PartitionId),E<ResidentGoalPlanPayloadV1>(ResidentGoalPlanPayloadV1.PartitionId),
        E<ResidentSkillAptitudePayloadV1>(ResidentSkillAptitudePayloadV1.PartitionId),E<ResidentRelationshipPayloadV1>(ResidentRelationshipPayloadV1.PartitionId),
        E<ResidentFamilyLineagePayloadV1>(ResidentFamilyLineagePayloadV1.PartitionId),E<ResidentBehaviorStatePayloadV1>(ResidentBehaviorStatePayloadV1.PartitionId),
        E<ResidentLineagePayloadV1>(ResidentLineagePayloadV1.PartitionId));
    public ResidentDomainSnapshotMaterialV1 BindSnapshotMaterial(WorldStateV1 frozen)=>ResidentDomainSnapshotMaterialV1.Bind(frozen,this);
    private static DomainPartitionStateV1<T> E<T>(string id)=>new(StandardDomainPartitionRegistry.Get(id),Array.Empty<DomainRecordEnvelopeV1<T>>());
    private static DomainPartitionStateV1<T> R<T>(DomainPartitionStateV1<T> p,string id){ArgumentNullException.ThrowIfNull(p);if(p.Identity!=StandardDomainPartitionRegistry.Get(id))throw new InvalidDataException($"resident.runtime-state.partition-identity:{id}");return p;}
}

public sealed class ResidentDomainSnapshotMaterialV1
{
    private ResidentDomainSnapshotMaterialV1(IEnumerable<IDomainPartitionSnapshotAuthorityV1> a){var x=a?.ToArray()??throw new ArgumentNullException(nameof(a));if(x.Length!=13)throw new InvalidDataException("resident.snapshot-material.authority-count");foreach(var y in x){y.VerifyBoundAuthority();if(y.Identity.OwnerDomain.Value!="resident")throw new InvalidDataException($"resident.snapshot-material.foreign-owner:{y.PartitionId.Value}");}Authorities=Array.AsReadOnly(x.OrderBy(y=>y.PartitionId.Value,StringComparer.Ordinal).ToArray());}
    public IReadOnlyList<IDomainPartitionSnapshotAuthorityV1> Authorities{get;}
    public static ResidentDomainSnapshotMaterialV1 Bind(WorldStateV1 f,ResidentDomainStateV1 s){ArgumentNullException.ThrowIfNull(f);ArgumentNullException.ThrowIfNull(s);return new(new IDomainPartitionSnapshotAuthorityV1[]{
        B(f,s.IdentityLifecycle,ResidentIdentityLifecyclePayloadV1.PartitionId,x=>x.CanonicalDigest()),B(f,s.BodyHealth,ResidentBodyHealthPayloadV1.PartitionId,x=>x.CanonicalDigest()),
        B(f,s.Physiology,ResidentPhysiologyPayloadV1.PartitionId,x=>x.CanonicalDigest()),B(f,s.Perception,ResidentPerceptionPayloadV1.PartitionId,x=>x.CanonicalDigest()),
        B(f,s.KnowledgeBelief,ResidentKnowledgeBeliefPayloadV1.PartitionId,x=>x.CanonicalDigest()),B(f,s.Memory,ResidentMemoryPayloadV1.PartitionId,x=>x.CanonicalDigest()),
        B(f,s.Psychology,ResidentPsychologyPayloadV1.PartitionId,x=>x.CanonicalDigest()),B(f,s.GoalPlan,ResidentGoalPlanPayloadV1.PartitionId,x=>x.CanonicalDigest()),
        B(f,s.SkillAptitude,ResidentSkillAptitudePayloadV1.PartitionId,x=>x.CanonicalDigest()),B(f,s.Relationship,ResidentRelationshipPayloadV1.PartitionId,x=>x.CanonicalDigest()),
        B(f,s.FamilyLineage,ResidentFamilyLineagePayloadV1.PartitionId,x=>x.CanonicalDigest()),B(f,s.BehaviorState,ResidentBehaviorStatePayloadV1.PartitionId,x=>x.CanonicalDigest()),
        B(f,s.Lineage,ResidentLineagePayloadV1.PartitionId,x=>x.CanonicalDigest())});}
    private static DomainPartitionSnapshotAuthorityV1<T> B<T>(WorldStateV1 f,DomainPartitionStateV1<T> p,string id,Func<T,byte[]> d)=>new(p,f.Partitions.Get(id).Header,d);
}
