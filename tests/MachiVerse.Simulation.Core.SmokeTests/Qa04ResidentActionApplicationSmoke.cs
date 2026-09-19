using Google.Protobuf;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Participation;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04ResidentActionApplicationSmoke
{
    public static void Run()
    {
        var descriptor1 = Qa04ReferenceLoadV1.OperationsForStep(1)
            .First(static value => value.FamilyToken.Value == "participation-control-resident-action");
        var binding1 = Qa04CanonicalOperationBindingV1.Bind(descriptor1, schedulingPolicyGeneration: 1);
        var behaviorIdentity = StandardDomainPartitionRegistry.Get(ResidentBehaviorStatePayloadV1.PartitionId);
        var empty = new DomainPartitionStateV1<ResidentBehaviorStatePayloadV1>(
            behaviorIdentity,
            Array.Empty<DomainRecordEnvelopeV1<ResidentBehaviorStatePayloadV1>>());
        var resident = Qa04ReferenceLoadV1.Record(
            new StableToken("resident.persistent-identity"),
            descriptor1.FamilyOrdinal);
        var residentRef = new PartitionRecordRefV1(
            ResidentIdentityLifecyclePayloadV1.PartitionId,
            resident.RecordId);
        var references = Resolver.ForResident(residentRef);
        var controlMode = CanonicalControlMode(descriptor1.FamilyOrdinal, residentRef);

        var first = Qa04ResidentActionApplicationV1.Apply(
            Qa04ReferenceLoadV1.WorldId,
            binding1,
            empty,
            controlMode,
            references);

        var expectedStep1 = checked(descriptor1.InjectionStep + 1UL);
        var expectedAction1 = Qa04ReferenceLoadV1.ResidentActivity(resident.RecordId, descriptor1.InjectionStep);
        Require(first.Created, "first resident action must create BehaviorState");
        Require(first.BehaviorState.ItemCount == 1, "first resident action must create exactly one record");
        Require(first.AppliedRecord.Revision == 1, "first behavior revision drift");
        Require(first.AppliedRecord.CreatedStep == expectedStep1, "first behavior created_step drift");
        Require(first.AppliedRecord.RetiredStep is null, "first behavior must be active");
        Require(first.AppliedRecord.DetailLevel == Qa04ReferenceLoadV1.ResidentDetailLevel(descriptor1.FamilyOrdinal),
            "first behavior detail level drift");
        Require(first.AppliedRecord.LineageRef is null, "first behavior lineage must be NONE");
        Require(first.AppliedRecord.Payload.ResidentRef == residentRef, "behavior resident_ref drift");
        Require(first.AppliedRecord.Payload.Mode == new StableToken("perf.action-active"), "behavior mode drift");
        Require(first.AppliedRecord.Payload.ActiveGoalRef is null, "behavior active_goal_ref must be NONE");
        Require(first.AppliedRecord.Payload.ActiveActionToken == expectedAction1, "behavior action token drift");
        Require(first.AppliedRecord.Payload.ActionTargetRefs.Count == 0, "benchmark behavior target refs must be empty");
        Require(first.AppliedRecord.Payload.ActionStartedStep == expectedStep1, "behavior action_started_step drift");
        Require(first.AppliedRecord.Payload.ControlSource == new StableToken("autonomous"), "behavior control source drift");

        var replay = Qa04ResidentActionApplicationV1.Apply(
            Qa04ReferenceLoadV1.WorldId,
            binding1,
            empty,
            controlMode,
            references);
        Require(replay.AppliedRecord.RecordId == first.AppliedRecord.RecordId,
            "resident action replay from same pre-state must derive the same RecordId");
        Require(replay.AppliedRecord.Payload == first.AppliedRecord.Payload,
            "resident action replay from same pre-state must derive the same payload");

        ExpectInvalid(
            () => Qa04ResidentActionApplicationV1.Apply(
                Qa04ReferenceLoadV1.WorldId,
                binding1,
                first.BehaviorState,
                controlMode,
                references),
            "qa04.resident.action-non-monotonic-step");

        var descriptor2 = Qa04ReferenceLoadV1.OperationsForStep(2)
            .First(value => value.FamilyToken.Value == "participation-control-resident-action" &&
                            value.FamilyOrdinal == descriptor1.FamilyOrdinal);
        var binding2 = Qa04CanonicalOperationBindingV1.Bind(descriptor2, schedulingPolicyGeneration: 1);
        var second = Qa04ResidentActionApplicationV1.Apply(
            Qa04ReferenceLoadV1.WorldId,
            binding2,
            first.BehaviorState,
            controlMode,
            references);
        var expectedStep2 = checked(descriptor2.InjectionStep + 1UL);
        var expectedAction2 = Qa04ReferenceLoadV1.ResidentActivity(resident.RecordId, descriptor2.InjectionStep);

        Require(!second.Created, "later resident action must revise existing BehaviorState");
        Require(second.BehaviorState.ItemCount == 1, "later resident action must preserve one behavior/resident");
        Require(second.AppliedRecord.RecordId == first.AppliedRecord.RecordId,
            "later resident action must preserve BehaviorState RecordId");
        Require(second.AppliedRecord.Revision == 2, "later resident action must increment revision exactly once");
        Require(second.AppliedRecord.CreatedStep == first.AppliedRecord.CreatedStep,
            "later resident action must preserve created_step");
        Require(second.AppliedRecord.Payload.ActiveActionToken == expectedAction2,
            "later resident action token drift");
        Require(second.AppliedRecord.Payload.ActionStartedStep == expectedStep2,
            "later resident action step drift");

        ExpectInvalid(
            () => Qa04ResidentActionApplicationV1.Apply(
                Qa04ReferenceLoadV1.WorldId,
                binding2,
                second.BehaviorState,
                controlMode,
                references),
            "qa04.resident.action-non-monotonic-step");

        var otherDescriptor = Qa04ReferenceLoadV1.OperationsForStep(1)
            .First(static value => value.FamilyToken.Value == "physical-item-movement-work");
        var otherBinding = Qa04CanonicalOperationBindingV1.Bind(otherDescriptor, schedulingPolicyGeneration: 1);
        ExpectInvalid(
            () => Qa04ResidentActionApplicationV1.Apply(
                Qa04ReferenceLoadV1.WorldId,
                otherBinding,
                empty,
                controlMode,
                references),
            "qa04.resident.action-family");

        var tamperedOperation = binding1.Operation.Clone();
        var tamperedPayload = tamperedOperation.OperationPayload.ToByteArray();
        tamperedPayload[^1] ^= 0x01;
        tamperedOperation.OperationPayload = ByteString.CopyFrom(tamperedPayload);
        var tamperedBinding = binding1 with { Operation = tamperedOperation };
        ExpectInvalid(
            () => Qa04ResidentActionApplicationV1.Apply(
                Qa04ReferenceLoadV1.WorldId,
                tamperedBinding,
                empty,
                controlMode,
                references),
            "qa04.resident.action-binding-drift");

        var nonAutonomousPayload = controlMode.Payload with { Mode = new StableToken("diver-control-available") };
        var nonAutonomous = new DomainRecordEnvelopeV1<ParticipationControlModePayloadV1>(
            controlMode.RecordId,
            controlMode.RecordSchema,
            controlMode.Revision,
            controlMode.CreatedStep,
            controlMode.RetiredStep,
            controlMode.DetailLevel,
            controlMode.LineageRef,
            nonAutonomousPayload);
        ExpectInvalid(
            () => Qa04ResidentActionApplicationV1.Apply(
                Qa04ReferenceLoadV1.WorldId,
                binding1,
                empty,
                nonAutonomous,
                references),
            "qa04.resident.action-control-mode-drift");

        ExpectInvalid(
            () => Qa04ResidentActionApplicationV1.Apply(
                Qa04ReferenceLoadV1.WorldId,
                binding1,
                empty,
                controlMode,
                new Resolver()),
            expectedMessage: null);

        var wrongSchema = Resolver.ForResident(residentRef);
        wrongSchema.Set(
            residentRef,
            StandardDomainPartitionRegistry.Get(ParticipationControlModePayloadV1.PartitionId).RecordSchema);
        ExpectInvalid(
            () => Qa04ResidentActionApplicationV1.Apply(
                Qa04ReferenceLoadV1.WorldId,
                binding1,
                empty,
                controlMode,
                wrongSchema),
            expectedMessage: null);

        var duplicateRecord = new DomainRecordEnvelopeV1<ResidentBehaviorStatePayloadV1>(
            OpaqueId128.Parse("0000000000000000000000000000b001"),
            behaviorIdentity.RecordSchema,
            revision: 1,
            createdStep: first.AppliedRecord.CreatedStep,
            retiredStep: null,
            first.AppliedRecord.DetailLevel,
            lineageRef: null,
            first.AppliedRecord.Payload);
        var duplicateResidentState = new DomainPartitionStateV1<ResidentBehaviorStatePayloadV1>(
            behaviorIdentity,
            new[] { first.AppliedRecord, duplicateRecord });
        ExpectInvalid(
            () => Qa04ResidentActionApplicationV1.Apply(
                Qa04ReferenceLoadV1.WorldId,
                binding2,
                duplicateResidentState,
                controlMode,
                references),
            "qa04.resident.action-duplicate-resident-behavior");
    }

    private static DomainRecordEnvelopeV1<ParticipationControlModePayloadV1> CanonicalControlMode(
        ulong residentOrdinal,
        PartitionRecordRefV1 residentRef)
    {
        var identity = StandardDomainPartitionRegistry.Get(ParticipationControlModePayloadV1.PartitionId);
        var payload = new ParticipationControlModePayloadV1(
            residentRef,
            BindingRef: null,
            Qa04ParticipationControlModeCanonicalAuthorityV1.Autonomous,
            Qa04ParticipationControlModeCanonicalAuthorityV1.InitialEffectiveFrom,
            Qa04ParticipationControlModeCanonicalAuthorityV1.InitialInputAuthorityGeneration);
        return new DomainRecordEnvelopeV1<ParticipationControlModePayloadV1>(
            Qa04ParticipationControlModeCanonicalAuthorityV1.RecordId(residentOrdinal),
            identity.RecordSchema,
            revision: 1,
            createdStep: 0,
            retiredStep: null,
            Qa04ReferenceLoadV1.ResidentDetailLevel(residentOrdinal),
            lineageRef: null,
            payload);
    }

    private static void ExpectInvalid(Action action, string? expectedMessage)
    {
        try
        {
            action();
            throw new InvalidOperationException("Expected resident action application to reject invalid input.");
        }
        catch (InvalidDataException ex)
        {
            if (expectedMessage is not null && ex.Message != expectedMessage)
                throw new InvalidOperationException($"Unexpected rejection code: {ex.Message}");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Resolver : IDomainRecordSchemaResolverV1
    {
        private readonly Dictionary<PartitionRecordRefV1, SchemaRefV1> _records = new();

        public static Resolver ForResident(PartitionRecordRefV1 residentRef)
        {
            var resolver = new Resolver();
            resolver.Set(
                residentRef,
                StandardDomainPartitionRegistry.Get(residentRef.PartitionId.Value).RecordSchema);
            return resolver;
        }

        public void Set(PartitionRecordRefV1 reference, SchemaRefV1 schema)
            => _records[reference] = schema;

        public bool Exists(PartitionRecordRefV1 reference)
            => _records.ContainsKey(reference);

        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
            => _records.TryGetValue(reference, out schema);
    }
}
