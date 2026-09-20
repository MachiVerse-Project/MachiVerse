using Google.Protobuf;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04PhysicalMoveApplicationResultV1(
    DomainRecordEnvelopeV1<PhysicalPresencePayloadV1> AppliedRecord,
    DomainPartitionStateV1<PhysicalPresencePayloadV1> PresenceState);

/// <summary>
/// Applies the already-bound perf.reference.v1 physical-item-movement-work Operation to the
/// authoritative physical.presence partition. This benchmark-only handler applies the bound
/// desired velocity directly to linear_velocity; position/occupancy/collision integration remains
/// outside this focused application boundary.
/// </summary>
public static class Qa04PhysicalMoveApplicationV1
{
    private const string PhysicalFamily = "physical-item-movement-work";
    private const string MoveOperationKind = "physical.move.request";

    private static readonly StableToken PhysicalDomain = new("physical_built");
    private static readonly StableToken PhysicalClass = new("physical.d0-presence");

    public static Qa04PhysicalMoveApplicationResultV1 Apply(
        Qa04CanonicalOperationBindingResultV1 binding,
        DomainPartitionStateV1<PhysicalPresencePayloadV1> current,
        IDomainRecordSchemaResolverV1 references)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(references);

        RequireCanonicalBinding(binding);

        var identity = StandardDomainPartitionRegistry.Get(PhysicalPresencePayloadV1.PartitionId);
        if (current.Identity != identity)
            throw new InvalidDataException("qa04.physical.move-partition-identity");

        var target = RequireCanonicalTargetBinding(binding);
        if (!current.TryGet(target.RecordId, out var existing) || existing is null)
            throw new InvalidDataException("qa04.physical.move-target-missing");

        var revised = ApplyRecordCore(binding, existing, identity, target, references);
        var next = current.WithReplacements(
            new[] { revised },
            "qa04.physical.move-target-missing");
        if (next.ItemCount != current.ItemCount)
            throw new InvalidDataException("qa04.physical.move-count-drift");

        return new Qa04PhysicalMoveApplicationResultV1(revised, next);
    }

    internal static DomainRecordEnvelopeV1<PhysicalPresencePayloadV1> ApplyRecord(
        Qa04CanonicalOperationBindingResultV1 binding,
        DomainRecordEnvelopeV1<PhysicalPresencePayloadV1> existing,
        IDomainRecordSchemaResolverV1 references)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(references);

        RequireCanonicalBinding(binding);
        var identity = StandardDomainPartitionRegistry.Get(PhysicalPresencePayloadV1.PartitionId);
        var target = RequireCanonicalTargetBinding(binding);
        return ApplyRecordCore(binding, existing, identity, target, references);
    }

    private static PartitionRecordRefV1 RequireCanonicalTargetBinding(
        Qa04CanonicalOperationBindingResultV1 binding)
    {
        var descriptor = binding.SourceDescriptor;
        var canonicalPresence = Qa04ReferenceLoadV1.Record(PhysicalClass, descriptor.FamilyOrdinal);
        var presenceRef = new PartitionRecordRefV1(
            PhysicalPresencePayloadV1.PartitionId,
            canonicalPresence.RecordId);
        var effectiveStep = checked(descriptor.InjectionStep + 1UL);
        if (binding.PrimaryTarget != presenceRef || binding.ScheduledOperation.EffectiveStep != effectiveStep)
            throw new InvalidDataException("qa04.physical.move-target-step-drift");
        return presenceRef;
    }

    private static DomainRecordEnvelopeV1<PhysicalPresencePayloadV1> ApplyRecordCore(
        Qa04CanonicalOperationBindingResultV1 binding,
        DomainRecordEnvelopeV1<PhysicalPresencePayloadV1> existing,
        DomainPartitionIdentityV1 identity,
        PartitionRecordRefV1 presenceRef,
        IDomainRecordSchemaResolverV1 references)
    {
        RequireCanonicalTarget(existing, identity, presenceRef, references);

        var descriptor = binding.SourceDescriptor;
        var velocity = new Vec3Int64V1(
            Qa04ReferenceGenesisValueSourceV1.SmallSignedValue(descriptor.OperationId, "vx"),
            Qa04ReferenceGenesisValueSourceV1.SmallSignedValue(descriptor.OperationId, "vy"),
            0);
        var payload = existing.Payload with { LinearVelocity = velocity };

        new StandardDomainPayloadCodecValidatorV1().Validate(
            PhysicalPresencePayloadV1.PartitionId,
            payload.ToStandardPayload(),
            references);

        var revised = existing.Revise(payload);
        var expectedRevision = checked(existing.Revision + 1UL);
        if (revised.RecordId != existing.RecordId ||
            revised.RecordSchema != existing.RecordSchema ||
            revised.Revision != expectedRevision ||
            revised.CreatedStep != 0 ||
            revised.RetiredStep is not null ||
            revised.DetailLevel != DetailLevelV1.D0Entity ||
            revised.LineageRef is not null)
        {
            throw new InvalidDataException("qa04.physical.move-revision-envelope-drift");
        }

        if (revised.Payload.SubjectRef != existing.Payload.SubjectRef ||
            revised.Payload.FrameRef != existing.Payload.FrameRef ||
            revised.Payload.Position != existing.Payload.Position ||
            revised.Payload.Orientation != existing.Payload.Orientation ||
            revised.Payload.AngularRateUradPerSecond != existing.Payload.AngularRateUradPerSecond ||
            revised.Payload.ShapeRef != existing.Payload.ShapeRef ||
            revised.Payload.ContainmentRef != existing.Payload.ContainmentRef ||
            revised.Payload.PresenceMode != existing.Payload.PresenceMode ||
            revised.Payload.LinearVelocity != velocity)
        {
            throw new InvalidDataException("qa04.physical.move-payload-drift");
        }

        return revised;
    }

    private static void RequireCanonicalTarget(
        DomainRecordEnvelopeV1<PhysicalPresencePayloadV1> record,
        DomainPartitionIdentityV1 identity,
        PartitionRecordRefV1 presenceRef,
        IDomainRecordSchemaResolverV1 references)
    {
        if (record.RecordId != presenceRef.RecordId ||
            record.RecordSchema != identity.RecordSchema ||
            record.Revision == 0 ||
            record.CreatedStep != 0 ||
            record.IsRetired ||
            record.DetailLevel != DetailLevelV1.D0Entity ||
            record.LineageRef is not null)
        {
            throw new InvalidDataException("qa04.physical.move-target-drift");
        }

        new StandardDomainPayloadCodecValidatorV1().Validate(
            PhysicalPresencePayloadV1.PartitionId,
            record.Payload.ToStandardPayload(),
            references);
    }

    private static void RequireCanonicalBinding(Qa04CanonicalOperationBindingResultV1 binding)
    {
        if (binding.SourceDescriptor is null || binding.BoundDescriptor is null ||
            binding.Operation is null || binding.OrderKey is null || binding.ScheduledOperation is null)
            throw new InvalidDataException("qa04.physical.move-binding-null");
        if (binding.SourceDescriptor.FamilyToken.Value != PhysicalFamily)
            throw new InvalidDataException("qa04.physical.move-family");
        if (binding.Operation.OperationKind != MoveOperationKind)
            throw new InvalidDataException("qa04.physical.move-operation-kind");
        if (binding.OwnerDomain != PhysicalDomain)
            throw new InvalidDataException("qa04.physical.move-owner-domain");
        if (binding.Operation.Admission is null || binding.Operation.Admission.SchedulingPolicyGeneration == 0)
            throw new InvalidDataException("qa04.physical.move-admission");

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
            throw new InvalidDataException("qa04.physical.move-binding-drift");
        }
    }
}
