using Google.Protobuf;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04InfrastructureServiceReserveApplicationSmoke
{
    public static void Run()
    {
        var descriptor = Qa04ReferenceLoadV1.OperationsForStep(1)
            .First(static value => value.FamilyToken.Value == "infrastructure-service-delivery");
        var binding = Qa04CanonicalOperationBindingV1.Bind(descriptor, schedulingPolicyGeneration: 1);
        var identity = StandardDomainPartitionRegistry.Get(InfrastructureServiceQueuePayloadV1.PartitionId);
        var empty = new DomainPartitionStateV1<InfrastructureServiceQueuePayloadV1>(
            identity,
            Array.Empty<DomainRecordEnvelopeV1<InfrastructureServiceQueuePayloadV1>>());
        var references = Resolver.ForCanonical(binding);

        var applied = Qa04InfrastructureServiceReserveApplicationV1.Apply(
            Qa04ReferenceLoadV1.WorldId,
            binding,
            empty,
            references);

        var expectedRequester = Qa04ReferenceLoadV1.Record(
            new StableToken("resident.persistent-identity"),
            descriptor.FamilyOrdinal);
        var expectedRequesterRef = new PartitionRecordRefV1(
            ResidentIdentityLifecyclePayloadV1.PartitionId,
            expectedRequester.RecordId);
        var expectedServiceRef = Qa04InfrastructureCanonicalServicePoolV1.Resolve(descriptor.FamilyOrdinal);
        var expectedEligibleStep = checked(descriptor.InjectionStep + 1UL);
        var expectedUnits = checked(1UL + descriptor.FamilyOrdinal % 100UL);

        Require(applied.ServiceQueue.ItemCount == 1, "reserve application must append exactly one queue record");
        Require(applied.CreatedRecord.Revision == 1, "runtime queue record revision drift");
        Require(applied.CreatedRecord.CreatedStep == expectedEligibleStep, "runtime queue created_step drift");
        Require(applied.CreatedRecord.RetiredStep is null, "runtime queue record must start active");
        Require(applied.CreatedRecord.DetailLevel == DetailLevelV1.D2RegionalAggregate, "runtime queue detail drift");
        Require(applied.CreatedRecord.LineageRef is null, "runtime queue lineage must be NONE");
        Require(applied.CreatedRecord.Payload.ServiceRef == expectedServiceRef, "runtime queue service_ref drift");
        Require(applied.CreatedRecord.Payload.RequesterRef == expectedRequesterRef, "runtime queue requester_ref drift");
        Require(applied.CreatedRecord.Payload.EligibleStep == expectedEligibleStep, "runtime queue eligible_step drift");
        Require(applied.CreatedRecord.Payload.SemanticPriority == 0, "runtime queue priority drift");
        Require(applied.CreatedRecord.Payload.RequestedUnits == expectedUnits, "runtime queue requested_units drift");
        Require(applied.CreatedRecord.Payload.AllocatedUnits == 0, "runtime queue allocated_units must start zero");
        Require(applied.CreatedRecord.Payload.Status == new StableToken("queued"), "runtime queue status drift");

        var replay = Qa04InfrastructureServiceReserveApplicationV1.Apply(
            Qa04ReferenceLoadV1.WorldId,
            binding,
            empty,
            references);
        Require(replay.CreatedRecord.RecordId == applied.CreatedRecord.RecordId,
            "reserve replay must derive the same RecordId");
        Require(replay.CreatedRecord.Payload == applied.CreatedRecord.Payload,
            "reserve replay must derive the same payload");

        ExpectInvalid(
            () => Qa04InfrastructureServiceReserveApplicationV1.Apply(
                Qa04ReferenceLoadV1.WorldId,
                binding,
                applied.ServiceQueue,
                references),
            "qa04.infrastructure.service-reserve-duplicate");

        var otherDescriptor = Qa04ReferenceLoadV1.OperationsForStep(1)
            .First(static value => value.FamilyToken.Value == "participation-control-resident-action");
        var otherBinding = Qa04CanonicalOperationBindingV1.Bind(otherDescriptor, schedulingPolicyGeneration: 1);
        ExpectInvalid(
            () => Qa04InfrastructureServiceReserveApplicationV1.Apply(
                Qa04ReferenceLoadV1.WorldId,
                otherBinding,
                empty,
                references),
            "qa04.infrastructure.service-reserve-family");

        var tamperedOperation = binding.Operation.Clone();
        var tamperedPayload = tamperedOperation.OperationPayload.ToByteArray();
        tamperedPayload[^1] ^= 0x01;
        tamperedOperation.OperationPayload = ByteString.CopyFrom(tamperedPayload);
        var tamperedBinding = binding with { Operation = tamperedOperation };
        ExpectInvalid(
            () => Qa04InfrastructureServiceReserveApplicationV1.Apply(
                Qa04ReferenceLoadV1.WorldId,
                tamperedBinding,
                empty,
                references),
            "qa04.infrastructure.service-reserve-binding-drift");

        var scheduledKey = binding.ScheduledOperation.OrderKey;
        var tamperedScheduledOrderKey = new SameStepOrderKey(
            scheduledKey.Phase,
            scheduledKey.DomainRank,
            scheduledKey.ConflictScopeDigest,
            checked(scheduledKey.SemanticPriority + 1),
            scheduledKey.IntentId);
        var tamperedScheduledBinding = binding with
        {
            ScheduledOperation = binding.ScheduledOperation with { OrderKey = tamperedScheduledOrderKey },
        };
        ExpectInvalid(
            () => Qa04InfrastructureServiceReserveApplicationV1.Apply(
                Qa04ReferenceLoadV1.WorldId,
                tamperedScheduledBinding,
                empty,
                references),
            "qa04.infrastructure.service-reserve-binding-drift");

        ExpectInvalid(
            () => Qa04InfrastructureServiceReserveApplicationV1.Apply(
                Qa04ReferenceLoadV1.WorldId,
                binding,
                empty,
                new Resolver()),
            expectedMessage: null);

        var wrongSchema = Resolver.ForCanonical(binding);
        wrongSchema.Set(
            expectedServiceRef,
            StandardDomainPartitionRegistry.Get(ResidentIdentityLifecyclePayloadV1.PartitionId).RecordSchema);
        ExpectInvalid(
            () => Qa04InfrastructureServiceReserveApplicationV1.Apply(
                Qa04ReferenceLoadV1.WorldId,
                binding,
                empty,
                wrongSchema),
            expectedMessage: null);
    }

    private static void ExpectInvalid(Action action, string? expectedMessage)
    {
        try
        {
            action();
            throw new InvalidOperationException("Expected reserve application to reject invalid input.");
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

        public static Resolver ForCanonical(Qa04CanonicalOperationBindingResultV1 binding)
        {
            var resolver = new Resolver();
            var requester = Qa04ReferenceLoadV1.Record(
                new StableToken("resident.persistent-identity"),
                binding.SourceDescriptor.FamilyOrdinal);
            var requesterRef = new PartitionRecordRefV1(
                ResidentIdentityLifecyclePayloadV1.PartitionId,
                requester.RecordId);
            resolver.Set(
                requesterRef,
                StandardDomainPartitionRegistry.Get(requesterRef.PartitionId.Value).RecordSchema);
            resolver.Set(
                binding.PrimaryTarget,
                StandardDomainPartitionRegistry.Get(binding.PrimaryTarget.PartitionId.Value).RecordSchema);
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
