using Google.Protobuf;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04InfrastructureServiceReserveApplicationResultV1(
    DomainRecordEnvelopeV1<InfrastructureServiceQueuePayloadV1> CreatedRecord,
    DomainPartitionStateV1<InfrastructureServiceQueuePayloadV1> ServiceQueue);

/// <summary>
/// Applies the already-bound perf.reference.v1 infrastructure-service-delivery Operation to the
/// authoritative infrastructure.service_queue partition. This is benchmark-only application
/// authority; it does not define general reservation-window expiry or queue allocation semantics.
/// </summary>
public static class Qa04InfrastructureServiceReserveApplicationV1
{
    private const string InfrastructureFamily = "infrastructure-service-delivery";
    private const string ReserveOperationKind = "infrastructure.service.reserve";

    private static readonly StableToken InfrastructureDomain = new("infrastructure_information");
    private static readonly StableToken ResidentClass = new("resident.persistent-identity");
    private static readonly StableToken RuntimeQueueRequestKind = new("perf.service-queue-request");
    private static readonly StableToken Queued = new("queued");
    private static readonly StandardDomainPayloadCodecValidatorV1 PayloadValidator = new();

    public static Qa04InfrastructureServiceReserveApplicationResultV1 Apply(
        OpaqueId128 worldId,
        Qa04CanonicalOperationBindingResultV1 binding,
        DomainPartitionStateV1<InfrastructureServiceQueuePayloadV1> current,
        IDomainRecordSchemaResolverV1 references)
    {
        var record = CreateRecord(worldId, binding, current, references);
        var next = current.WithAdditions(
            new[] { record },
            "qa04.infrastructure.service-reserve-duplicate");
        if (next.ItemCount != checked(current.ItemCount + 1UL))
            throw new InvalidDataException("qa04.infrastructure.service-reserve-count-drift");

        return new Qa04InfrastructureServiceReserveApplicationResultV1(record, next);
    }

    internal static DomainRecordEnvelopeV1<InfrastructureServiceQueuePayloadV1> CreateRecord(
        OpaqueId128 worldId,
        Qa04CanonicalOperationBindingResultV1 binding,
        DomainPartitionStateV1<InfrastructureServiceQueuePayloadV1> current,
        IDomainRecordSchemaResolverV1 references)
    {
        if (worldId.IsZero) throw new ArgumentException("WorldId ZERO is invalid.", nameof(worldId));
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(references);

        RequireCanonicalBinding(binding);

        var identity = StandardDomainPartitionRegistry.Get(InfrastructureServiceQueuePayloadV1.PartitionId);
        if (current.Identity != identity)
            throw new InvalidDataException("qa04.infrastructure.service-reserve-partition-identity");

        var descriptor = binding.SourceDescriptor;
        var requester = Qa04ReferenceLoadV1.Record(ResidentClass, descriptor.FamilyOrdinal);
        var requesterRef = new PartitionRecordRefV1(
            ResidentIdentityLifecyclePayloadV1.PartitionId,
            requester.RecordId);
        var serviceRef = Qa04InfrastructureCanonicalServicePoolV1.Resolve(descriptor.FamilyOrdinal);
        var requestedUnits = checked(1UL + descriptor.FamilyOrdinal % 100UL);
        var eligibleFrom = checked(descriptor.InjectionStep + 1UL);
        var eligibleUntil = checked(descriptor.InjectionStep + 30UL);

        if (eligibleUntil <= eligibleFrom)
            throw new InvalidDataException("qa04.infrastructure.service-reserve-eligible-range");
        if (binding.PrimaryTarget != serviceRef || binding.ScheduledOperation.EffectiveStep != eligibleFrom)
            throw new InvalidDataException("qa04.infrastructure.service-reserve-target-step-drift");
        if (requestedUnits == 0)
            throw new InvalidDataException("qa04.infrastructure.service-reserve-units-zero");

        var payload = new InfrastructureServiceQueuePayloadV1(
            serviceRef,
            requesterRef,
            EligibleStep: eligibleFrom,
            SemanticPriority: 0,
            RequestedUnits: requestedUnits,
            AllocatedUnits: 0,
            Status: Queued);
        PayloadValidator.Validate(
            InfrastructureServiceQueuePayloadV1.PartitionId,
            payload.ToStandardPayload(),
            references);

        var recordId = DerivedIdentity.DeriveEntityId(
            worldId,
            eligibleFrom,
            InfrastructureDomain,
            binding.SourceDescriptor.OperationId,
            RuntimeQueueRequestKind,
            localOrdinal: 0);
        if (recordId.IsZero)
            throw new InvalidDataException("qa04.infrastructure.service-reserve-record-id-zero");
        if (current.TryGet(recordId, out _))
            throw new InvalidDataException("qa04.infrastructure.service-reserve-duplicate");

        return new DomainRecordEnvelopeV1<InfrastructureServiceQueuePayloadV1>(
            recordId,
            identity.RecordSchema,
            revision: 1,
            createdStep: eligibleFrom,
            retiredStep: null,
            DetailLevelV1.D2RegionalAggregate,
            lineageRef: null,
            payload);
    }

    private static void RequireCanonicalBinding(Qa04CanonicalOperationBindingResultV1 binding)
    {
        if (binding.SourceDescriptor is null || binding.BoundDescriptor is null ||
            binding.Operation is null || binding.OrderKey is null || binding.ScheduledOperation is null)
            throw new InvalidDataException("qa04.infrastructure.service-reserve-binding-null");
        if (binding.SourceDescriptor.FamilyToken.Value != InfrastructureFamily)
            throw new InvalidDataException("qa04.infrastructure.service-reserve-family");
        if (binding.Operation.OperationKind != ReserveOperationKind)
            throw new InvalidDataException("qa04.infrastructure.service-reserve-operation-kind");
        if (binding.OwnerDomain != InfrastructureDomain)
            throw new InvalidDataException("qa04.infrastructure.service-reserve-owner-domain");
        if (binding.Operation.Admission is null || binding.Operation.Admission.SchedulingPolicyGeneration == 0)
            throw new InvalidDataException("qa04.infrastructure.service-reserve-admission");

        if (Qa04CanonicalOperationBindingV1.HasCanonicalAuthority(binding))
            return;

        var expected = Qa04CanonicalOperationBindingV1.Bind(
            binding.SourceDescriptor,
            binding.Operation.Admission.SchedulingPolicyGeneration);

        if (!expected.Operation.ToByteArray().AsSpan().SequenceEqual(binding.Operation.ToByteArray()) ||
            expected.OwnerDomain != binding.OwnerDomain ||
            expected.PrimaryTarget != binding.PrimaryTarget ||
            expected.ScheduledOperation.OperationId != binding.ScheduledOperation.OperationId ||
            expected.ScheduledOperation.EffectiveStep != binding.ScheduledOperation.EffectiveStep ||
            !expected.OrderKey.ToDatabaseBytes().AsSpan().SequenceEqual(binding.OrderKey.ToDatabaseBytes()) ||
            !expected.ScheduledOperation.OrderKey.ToDatabaseBytes().AsSpan().SequenceEqual(
                binding.ScheduledOperation.OrderKey.ToDatabaseBytes()) ||
            expected.BoundDescriptor.InjectionStep != binding.BoundDescriptor.InjectionStep ||
            expected.BoundDescriptor.FamilyToken != binding.BoundDescriptor.FamilyToken ||
            expected.BoundDescriptor.FamilyOrdinal != binding.BoundDescriptor.FamilyOrdinal ||
            expected.BoundDescriptor.OperationId != binding.BoundDescriptor.OperationId ||
            !expected.BoundDescriptor.PayloadDigest.AsSpan().SequenceEqual(binding.BoundDescriptor.PayloadDigest))
        {
            throw new InvalidDataException("qa04.infrastructure.service-reserve-binding-drift");
        }
    }
}
