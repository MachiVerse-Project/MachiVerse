using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class ResidentSnapshotMaterialSmoke
{
    [ModuleInitializer]
    internal static void Initialize() => Run();

    internal static void Run()
    {
        var qa = Qa04ReferenceWorldMaterializerV1.MaterializeResidentIdentityLifecycle(1);
        var source = qa.Partition.RecordsCanonical.Single();
        var payload = new ResidentIdentityLifecyclePayloadV1(
            source.Payload.ResidentId,
            source.Payload.Lifecycle,
            source.Payload.BirthStep,
            source.Payload.DeathStep,
            source.Payload.ParentRefs,
            source.Payload.LineageGeneration,
            source.Payload.ProfileToken);
        var identity = StandardDomainPartitionRegistry.Get(ResidentIdentityLifecyclePayloadV1.PartitionId);
        var record = new DomainRecordEnvelopeV1<ResidentIdentityLifecyclePayloadV1>(
            source.RecordId, source.RecordSchema, source.Revision, source.CreatedStep, source.RetiredStep, source.DetailLevel, source.LineageRef, payload);
        var identityState = new DomainPartitionStateV1<ResidentIdentityLifecyclePayloadV1>(identity, [record]);
        var state = new ResidentDomainStateV1(
            identityState,
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

        var material = state.BindSnapshotMaterial(qa.WorldState);
        var providers = ResidentDomainSnapshotProviderV1.CreateAll();
        Require(material.Authorities.Count == 13 && providers.Count == 13,
            "Resident runtime state/provider set must cover all 13 partitions.");
        Require(material.Authorities.Single(x => x.PartitionId.Value == ResidentIdentityLifecyclePayloadV1.PartitionId).ActualItemCount == 1,
            "Resident identity root must use the actual QA04 record.");

        foreach (var authority in material.Authorities)
        {
            var provider = providers.Single(x => x.SectionId == authority.PartitionId.Value);
            var section = provider.Create(authority);
            var restored = provider.CreateSemanticVerifier(authority.Header).Verify(section.Fragments);
            Require(restored.LogicalContentDigest.SequenceEqual(authority.Header.CanonicalDigest),
                $"Resident recovery must rehash frozen authority: {authority.PartitionId.Value}.");
        }

        var identityProvider = providers.Single(x => x.SectionId == ResidentIdentityLifecyclePayloadV1.PartitionId);
        var identitySection = identityProvider.Create(material.Authorities.Single(x => x.PartitionId.Value == ResidentIdentityLifecyclePayloadV1.PartitionId));
        var decoded = DomainPartitionSnapshotWireCodecV1.DecodeFragment(
            ResidentIdentityLifecyclePayloadV1.PartitionId,
            identitySection.Fragments.Single().FragmentPayload,
            StandardDomainNestedSnapshotCodecRegistryV1.Default);
        var round = ResidentIdentityLifecyclePayloadV1.FromStandardPayload(decoded.Records.Single().Payload);
        Require(round.ResidentId == payload.ResidentId && round.Lifecycle == payload.Lifecycle && round.LineageGeneration == payload.LineageGeneration && round.CanonicalDigest().SequenceEqual(payload.CanonicalDigest()),
            "Resident identity P4-05 payload must round-trip losslessly.");
    }

    private static DomainPartitionStateV1<T> Empty<T>(string id)
        => new(StandardDomainPartitionRegistry.Get(id), Array.Empty<DomainRecordEnvelopeV1<T>>());
    private static void Require(bool c,string m){if(!c)throw new InvalidOperationException(m);}
}
