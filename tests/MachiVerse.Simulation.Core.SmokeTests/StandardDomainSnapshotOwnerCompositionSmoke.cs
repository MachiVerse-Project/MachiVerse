using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Domains.Environment;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Domains.Participation;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class StandardDomainSnapshotOwnerCompositionSmoke
{
    [ModuleInitializer]
    internal static void Initialize() => Run();

    internal static void Run()
    {
        var qa = Qa04ReferenceWorldMaterializerV1.MaterializeResidentIdentityLifecycle(1);
        var residentState = CreateResidentState(qa);

        var resident = residentState.BindSnapshotMaterial(qa.WorldState);
        var participation = ParticipationDomainStateV1.CreateEmpty().BindSnapshotMaterial(qa.WorldState);
        var physicalBuilt = PhysicalBuiltDomainStateV1.CreateEmpty().BindSnapshotMaterial(qa.WorldState);
        var spatial = SpatialDomainStateV1.CreateEmpty().BindSnapshotMaterial(qa.WorldState);
        var environment = EnvironmentDomainStateV1.CreateEmpty().BindSnapshotMaterial(qa.WorldState);
        var societyEconomy = SocietyEconomyDomainStateV1.CreateEmpty().BindSnapshotMaterial(qa.WorldState);
        var infrastructureInformation = InfrastructureInformationDomainStateV1.CreateEmpty().BindSnapshotMaterial(qa.WorldState);
        var governanceSecurity = GovernanceSecurityDomainStateV1.CreateEmpty().BindSnapshotMaterial(qa.WorldState);

        var providers = StandardDomainSnapshotOwnerCompositionV1.CreateAllProviders();
        var expectedIds = StandardDomainPartitionRegistry.Entries
            .Select(static identity => identity.PartitionId.Value)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        Require(providers.Count == StandardDomainPartitionRegistry.StandardPartitionCount,
            "Standard Domain provider composition must contain exactly 97 providers.");
        Require(providers.Select(static provider => provider.SectionId).SequenceEqual(expectedIds),
            "Standard Domain provider composition must match the exact canonical 97 partition IDs.");

        var authoritySet = StandardDomainSnapshotOwnerCompositionV1.CreateAuthoritySet(
            qa.WorldState,
            resident,
            participation,
            physicalBuilt,
            spatial,
            environment,
            societyEconomy,
            infrastructureInformation,
            governanceSecurity);
        Require(authoritySet.CanonicalAuthorities.Count == StandardDomainPartitionRegistry.StandardPartitionCount,
            "Eight owner roots must compose to an exact-97 authority set.");
        Require(authoritySet.CanonicalAuthorities.Sum(static authority => checked((long)authority.ActualItemCount)) == 1,
            "Minimal exact-97 authority fixture must contain only the one truthful Resident identity record.");
        Require(authoritySet.Get(ResidentIdentityLifecyclePayloadV1.PartitionId).ActualItemCount == 1,
            "Minimal exact-97 authority fixture must retain the actual Resident identity root.");
        Require(authoritySet.CanonicalAuthorities
                .Where(static authority => authority.PartitionId.Value != ResidentIdentityLifecyclePayloadV1.PartitionId)
                .All(static authority => authority.ActualItemCount == 0),
            "All other minimal-world partitions must be genuinely typed empty material.");

        var sections = DomainPartitionSnapshotProductionProviderV1.CreateAll97(authoritySet, providers);
        Require(sections.Count == StandardDomainPartitionRegistry.StandardPartitionCount,
            "Production Domain serialization must emit exactly 97 logical sections.");
        Require(sections.Select(static section => section.SectionId).SequenceEqual(expectedIds),
            "Production Domain sections must remain in canonical partition order.");
        Require(sections.Sum(static section => checked((long)section.LogicalItemCount)) == 1,
            "Minimal exact-97 production sections must not fabricate records.");

        var residentSection = sections.Single(static section => section.SectionId == ResidentIdentityLifecyclePayloadV1.PartitionId);
        Require(residentSection.LogicalItemCount == 1 &&
                residentSection.LogicalContentDigest.SequenceEqual(qa.PartitionHeader.CanonicalDigest),
            "Exact-97 production serialization must preserve the real Resident frozen digest.");

        foreach (var section in sections.Where(static section => section.SectionId != ResidentIdentityLifecyclePayloadV1.PartitionId))
        {
            Require(section.LogicalItemCount == 0 && section.Fragments.Count == 1,
                $"Typed empty exact-97 section must emit exactly one empty fragment: {section.SectionId}.");
            Require(section.Fragments[0].ItemCount == 0 &&
                    section.Fragments[0].FirstRecordId is null &&
                    section.Fragments[0].LastRecordId is null,
                $"Typed empty exact-97 section must not fabricate a record range: {section.SectionId}.");
        }
    }

    private static ResidentDomainStateV1 CreateResidentState(Qa04ResidentIdentityMaterializationV1 qa)
    {
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
            source.RecordId,
            source.RecordSchema,
            source.Revision,
            source.CreatedStep,
            source.RetiredStep,
            source.DetailLevel,
            source.LineageRef,
            payload);

        return new ResidentDomainStateV1(
            new DomainPartitionStateV1<ResidentIdentityLifecyclePayloadV1>(identity, [record]),
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
    }

    private static DomainPartitionStateV1<TPayload> Empty<TPayload>(string partitionId)
        => new(StandardDomainPartitionRegistry.Get(partitionId), Array.Empty<DomainRecordEnvelopeV1<TPayload>>());

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
