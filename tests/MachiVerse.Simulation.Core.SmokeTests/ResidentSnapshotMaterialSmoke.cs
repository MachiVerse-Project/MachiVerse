using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
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
        VerifyTypedOwnerRootsAndQa04SemanticIdentity();
        VerifyNestedSchemaBoundary();
    }

    private static void VerifyTypedOwnerRootsAndQa04SemanticIdentity()
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

        Require(payload.CanonicalDigest().SequenceEqual(source.Payload.CanonicalDigest()),
            "Resident domain-owned payload and QA04 payload must have identical type-independent semantic digest.");

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
        var identityState = new DomainPartitionStateV1<ResidentIdentityLifecyclePayloadV1>(identity, [record]);
        var recomputedHeader = PartitionStateHeaderV1.CreateCanonical(
            identityState,
            qa.PartitionHeader.Revision,
            qa.PartitionHeader.BasisStep,
            qa.PartitionHeader.DetailLevel,
            static value => value.CanonicalDigest());
        Require(recomputedHeader.CanonicalDigest.SequenceEqual(qa.PartitionHeader.CanonicalDigest) &&
                recomputedHeader.ItemCount == qa.PartitionHeader.ItemCount,
            "Resident domain-owned actual record must reproduce the frozen QA04 partition authority.");

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
        Require(providers.Select(static provider => provider.SectionId).SequenceEqual(
                material.Authorities.Select(static authority => authority.PartitionId.Value)),
            "Resident provider/root sets must match in canonical order.");
        Require(material.IdentityLifecycle.ActualItemCount == 1,
            "Resident identity root must use the actual QA04 record.");

        foreach (var authority in material.Authorities)
        {
            var provider = providers.Single(x => x.SectionId == authority.PartitionId.Value);
            var section = provider.Create(authority);
            if (authority.ActualItemCount == 0)
            {
                Require(section.Fragments.Count == 1 && section.Fragments[0].ItemCount == 0 &&
                        section.Fragments[0].FirstRecordId is null && section.Fragments[0].LastRecordId is null,
                    $"Resident typed empty root must not fabricate record ranges: {authority.PartitionId.Value}.");
            }
            var restored = provider.CreateSemanticVerifier(authority.Header).Verify(section.Fragments);
            Require(restored.LogicalContentDigest.SequenceEqual(authority.Header.CanonicalDigest),
                $"Resident recovery must rehash frozen authority: {authority.PartitionId.Value}.");
        }

        var identityProvider = providers.Single(x => x.SectionId == ResidentIdentityLifecyclePayloadV1.PartitionId);
        var identitySection = identityProvider.Create(material.IdentityLifecycle);
        var decoded = DomainPartitionSnapshotWireCodecV1.DecodeFragment(
            ResidentIdentityLifecyclePayloadV1.PartitionId,
            identitySection.Fragments.Single().FragmentPayload,
            StandardDomainNestedSnapshotCodecRegistryV1.Default);
        var round = ResidentIdentityLifecyclePayloadV1.FromStandardPayload(decoded.Records.Single().Payload);
        Require(round.ResidentId == payload.ResidentId &&
                round.Lifecycle == payload.Lifecycle &&
                round.LineageGeneration == payload.LineageGeneration &&
                round.CanonicalDigest().SequenceEqual(payload.CanonicalDigest()),
            "Resident identity P4-05 payload must round-trip losslessly.");
    }

    private static void VerifyNestedSchemaBoundary()
    {
        var residentRef = new PartitionRecordRefV1(
            ResidentIdentityLifecyclePayloadV1.PartitionId,
            OpaqueId128.Parse("000000000000000000000000000aa001"));
        var bodyHealth = new ResidentBodyHealthPayloadV1(
            residentRef,
            DevelopmentPpm: 500_000,
            HealthCapacityPpm: 900_000,
            BodyRegionStates: Array.Empty<ICanonicalDomainNestedValueV1>(),
            InjuryRefs: Array.Empty<PartitionRecordRefV1>(),
            DiseaseRefs: Array.Empty<PartitionRecordRefV1>(),
            RecoveryPpm: 750_000);
        var perception = new ResidentPerceptionPayloadV1(
            residentRef,
            AttentionTargetRefs: Array.Empty<PartitionRecordRefV1>(),
            PerceivedFacts: Array.Empty<ICanonicalDomainNestedValueV1>(),
            SensoryCapacityPpm: 1_000_000,
            BasisStep: 10);

        ExpectInvalid(
            "resident body-region nested schema must remain unavailable until normatively defined",
            () => _ = DomainPartitionSnapshotWireCodecV1.EncodePayload(
                ResidentBodyHealthPayloadV1.PartitionId,
                bodyHealth.ToStandardPayload(),
                StandardDomainNestedSnapshotCodecRegistryV1.Default));
        ExpectInvalid(
            "resident body-region semantic digest must not guess an undefined nested schema",
            () => _ = bodyHealth.CanonicalDigest());

        var perceptionWire = DomainPartitionSnapshotWireCodecV1.EncodePayload(
            ResidentPerceptionPayloadV1.PartitionId,
            perception.ToStandardPayload(),
            StandardDomainNestedSnapshotCodecRegistryV1.Default);
        var perceptionRound = ResidentPerceptionPayloadV1.FromStandardPayload(
            DomainPartitionSnapshotWireCodecV1.DecodePayload(
                ResidentPerceptionPayloadV1.PartitionId,
                perceptionWire,
                StandardDomainNestedSnapshotCodecRegistryV1.Default));
        Require(perceptionRound.PerceivedFacts.Count == 0 && perception.CanonicalDigest().Length == 32,
            "Resolved perceived-fact nested schema must allow canonical empty-list payload wire/digest.");
    }

    private static DomainPartitionStateV1<T> Empty<T>(string id)
        => new(StandardDomainPartitionRegistry.Get(id), Array.Empty<DomainRecordEnvelopeV1<T>>());

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void ExpectInvalid(string name, Action action)
    {
        try
        {
            action();
        }
        catch (InvalidDataException)
        {
            return;
        }
        throw new InvalidOperationException($"Expected rejection: {name}.");
    }
}
