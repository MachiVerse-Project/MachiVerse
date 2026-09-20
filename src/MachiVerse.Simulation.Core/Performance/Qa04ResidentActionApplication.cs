using Google.Protobuf;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Participation;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04ResidentActionApplicationResultV1(
    DomainRecordEnvelopeV1<ResidentBehaviorStatePayloadV1> AppliedRecord,
    DomainPartitionStateV1<ResidentBehaviorStatePayloadV1> BehaviorState,
    bool Created);

internal sealed record Qa04ResidentActionRecordResultV1(
    DomainRecordEnvelopeV1<ResidentBehaviorStatePayloadV1> AppliedRecord,
    bool Created);

/// <summary>
/// Applies the already-bound perf.reference.v1 participation-control-resident-action Operation to
/// authoritative resident.behavior_state. This benchmark-only handler establishes the Resident's
/// active action state; it does not claim any cross-domain action consequence.
/// </summary>
public static class Qa04ResidentActionApplicationV1
{
    private const string ResidentFamily = "participation-control-resident-action";
    private const string ResidentActionOperationKind = "resident.action.request";

    private static readonly StableToken ResidentDomain = new("resident");
    private static readonly StableToken ResidentClass = new("resident.persistent-identity");
    private static readonly StableToken RuntimeBehaviorKind = new("perf.behavior-state");
    private static readonly StableToken ActiveActionMode = new("perf.action-active");
    private static readonly StableToken Autonomous = new("autonomous");

    public static Qa04ResidentActionApplicationResultV1 Apply(
        OpaqueId128 worldId,
        Qa04CanonicalOperationBindingResultV1 binding,
        DomainPartitionStateV1<ResidentBehaviorStatePayloadV1> current,
        DomainRecordEnvelopeV1<ParticipationControlModePayloadV1> controlMode,
        IDomainRecordSchemaResolverV1 references)
    {
        if (worldId.IsZero) throw new ArgumentException("WorldId ZERO is invalid.", nameof(worldId));
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(controlMode);
        ArgumentNullException.ThrowIfNull(references);

        RequireCanonicalBinding(binding);

        var identity = StandardDomainPartitionRegistry.Get(ResidentBehaviorStatePayloadV1.PartitionId);
        if (current.Identity != identity)
            throw new InvalidDataException("qa04.resident.action-partition-identity");

        var residentRef = RequireResidentTarget(binding);
        var matches = current.RecordsCanonical
            .Where(record => record.Payload.ResidentRef == residentRef)
            .Take(2)
            .ToArray();
        if (matches.Length > 1)
            throw new InvalidDataException("qa04.resident.action-duplicate-resident-behavior");

        var applied = ApplyRecordCore(
            worldId,
            binding,
            matches.Length == 0 ? null : matches[0],
            current,
            controlMode,
            references,
            identity,
            residentRef);

        var next = applied.Created
            ? current.WithAdditions(new[] { applied.AppliedRecord }, "qa04.resident.action-record-id-collision")
            : current.WithReplacements(new[] { applied.AppliedRecord }, "qa04.full-step.mutation-resident-result-drift");

        var expectedCount = applied.Created ? checked(current.ItemCount + 1UL) : current.ItemCount;
        if (next.ItemCount != expectedCount)
            throw new InvalidDataException(applied.Created
                ? "qa04.resident.action-create-count-drift"
                : "qa04.resident.action-update-count-drift");

        return new Qa04ResidentActionApplicationResultV1(
            applied.AppliedRecord,
            next,
            applied.Created);
    }

    internal static Qa04ResidentActionRecordResultV1 ApplyRecord(
        OpaqueId128 worldId,
        Qa04CanonicalOperationBindingResultV1 binding,
        DomainRecordEnvelopeV1<ResidentBehaviorStatePayloadV1>? existing,
        DomainPartitionStateV1<ResidentBehaviorStatePayloadV1> collisionState,
        DomainRecordEnvelopeV1<ParticipationControlModePayloadV1> controlMode,
        IDomainRecordSchemaResolverV1 references)
    {
        if (worldId.IsZero) throw new ArgumentException("WorldId ZERO is invalid.", nameof(worldId));
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(collisionState);
        ArgumentNullException.ThrowIfNull(controlMode);
        ArgumentNullException.ThrowIfNull(references);

        RequireCanonicalBinding(binding);
        var identity = StandardDomainPartitionRegistry.Get(ResidentBehaviorStatePayloadV1.PartitionId);
        if (collisionState.Identity != identity)
            throw new InvalidDataException("qa04.resident.action-partition-identity");
        var residentRef = RequireResidentTarget(binding);
        return ApplyRecordCore(
            worldId,
            binding,
            existing,
            collisionState,
            controlMode,
            references,
            identity,
            residentRef);
    }

    private static PartitionRecordRefV1 RequireResidentTarget(
        Qa04CanonicalOperationBindingResultV1 binding)
    {
        var descriptor = binding.SourceDescriptor;
        var resident = Qa04ReferenceLoadV1.Record(ResidentClass, descriptor.FamilyOrdinal);
        var residentRef = new PartitionRecordRefV1(
            ResidentIdentityLifecyclePayloadV1.PartitionId,
            resident.RecordId);
        var effectiveStep = checked(descriptor.InjectionStep + 1UL);
        if (binding.PrimaryTarget != residentRef || binding.ScheduledOperation.EffectiveStep != effectiveStep)
            throw new InvalidDataException("qa04.resident.action-target-step-drift");
        return residentRef;
    }

    private static Qa04ResidentActionRecordResultV1 ApplyRecordCore(
        OpaqueId128 worldId,
        Qa04CanonicalOperationBindingResultV1 binding,
        DomainRecordEnvelopeV1<ResidentBehaviorStatePayloadV1>? existing,
        DomainPartitionStateV1<ResidentBehaviorStatePayloadV1> collisionState,
        DomainRecordEnvelopeV1<ParticipationControlModePayloadV1> controlMode,
        IDomainRecordSchemaResolverV1 references,
        DomainPartitionIdentityV1 identity,
        PartitionRecordRefV1 residentRef)
    {
        var descriptor = binding.SourceDescriptor;
        var resident = Qa04ReferenceLoadV1.Record(ResidentClass, descriptor.FamilyOrdinal);
        var action = Qa04ReferenceLoadV1.ResidentActivity(resident.RecordId, descriptor.InjectionStep);
        var effectiveStep = checked(descriptor.InjectionStep + 1UL);
        var detailLevel = Qa04ReferenceLoadV1.ResidentDetailLevel(descriptor.FamilyOrdinal);

        RequireCanonicalControlMode(descriptor.FamilyOrdinal, residentRef, detailLevel, controlMode);

        var payload = new ResidentBehaviorStatePayloadV1(
            residentRef,
            ActiveActionMode,
            ActiveGoalRef: null,
            ActiveActionToken: action,
            ActionTargetRefs: Array.Empty<PartitionRecordRefV1>(),
            ActionStartedStep: effectiveStep,
            ControlSource: Autonomous);
        new StandardDomainPayloadCodecValidatorV1().Validate(
            ResidentBehaviorStatePayloadV1.PartitionId,
            payload.ToStandardPayload(),
            references);

        if (existing is null)
        {
            var recordId = DerivedIdentity.DeriveEntityId(
                worldId,
                effectiveStep,
                ResidentDomain,
                descriptor.OperationId,
                RuntimeBehaviorKind,
                localOrdinal: 0);
            if (recordId.IsZero)
                throw new InvalidDataException("qa04.resident.action-record-id-zero");
            if (collisionState.TryGet(recordId, out _))
                throw new InvalidDataException("qa04.resident.action-record-id-collision");

            var created = new DomainRecordEnvelopeV1<ResidentBehaviorStatePayloadV1>(
                recordId,
                identity.RecordSchema,
                revision: 1,
                createdStep: effectiveStep,
                retiredStep: null,
                detailLevel,
                lineageRef: null,
                payload);
            return new Qa04ResidentActionRecordResultV1(created, Created: true);
        }

        RequireQa04ExistingRecord(existing, identity, residentRef, detailLevel, references);
        if (existing.Payload.ActionStartedStep is not { } previousStep || previousStep >= effectiveStep)
            throw new InvalidDataException("qa04.resident.action-non-monotonic-step");

        var revised = existing.Revise(payload);
        if (revised.Revision != checked(existing.Revision + 1UL) ||
            revised.RecordId != existing.RecordId ||
            revised.CreatedStep != existing.CreatedStep ||
            revised.RecordSchema != existing.RecordSchema ||
            revised.DetailLevel != existing.DetailLevel ||
            revised.LineageRef != existing.LineageRef)
        {
            throw new InvalidDataException("qa04.resident.action-revision-envelope-drift");
        }

        return new Qa04ResidentActionRecordResultV1(revised, Created: false);
    }

    private static void RequireQa04ExistingRecord(
        DomainRecordEnvelopeV1<ResidentBehaviorStatePayloadV1> record,
        DomainPartitionIdentityV1 identity,
        PartitionRecordRefV1 residentRef,
        DetailLevelV1 detailLevel,
        IDomainRecordSchemaResolverV1 references)
    {
        if (record.RecordSchema != identity.RecordSchema ||
            record.IsRetired ||
            record.DetailLevel != detailLevel ||
            record.LineageRef is not null ||
            record.Payload.ResidentRef != residentRef ||
            record.Payload.Mode != ActiveActionMode ||
            record.Payload.ActiveGoalRef is not null ||
            record.Payload.ActiveActionToken is null ||
            record.Payload.ActionTargetRefs.Count != 0 ||
            record.Payload.ActionStartedStep is null ||
            record.Payload.ControlSource != Autonomous ||
            record.CreatedStep > record.Payload.ActionStartedStep.Value)
        {
            throw new InvalidDataException("qa04.resident.action-existing-record-drift");
        }

        new StandardDomainPayloadCodecValidatorV1().Validate(
            ResidentBehaviorStatePayloadV1.PartitionId,
            record.Payload.ToStandardPayload(),
            references);
    }

    private static void RequireCanonicalControlMode(
        ulong residentOrdinal,
        PartitionRecordRefV1 residentRef,
        DetailLevelV1 detailLevel,
        DomainRecordEnvelopeV1<ParticipationControlModePayloadV1> controlMode)
    {
        var identity = StandardDomainPartitionRegistry.Get(ParticipationControlModePayloadV1.PartitionId);
        if (controlMode.RecordId != Qa04ParticipationControlModeCanonicalAuthorityV1.RecordId(residentOrdinal) ||
            controlMode.RecordSchema != identity.RecordSchema ||
            controlMode.Revision != 1 ||
            controlMode.CreatedStep != 0 ||
            controlMode.RetiredStep is not null ||
            controlMode.DetailLevel != detailLevel ||
            controlMode.LineageRef is not null ||
            controlMode.Payload.ResidentRef != residentRef ||
            controlMode.Payload.BindingRef is not null ||
            controlMode.Payload.Mode != Autonomous ||
            controlMode.Payload.EffectiveFrom != Qa04ParticipationControlModeCanonicalAuthorityV1.InitialEffectiveFrom ||
            controlMode.Payload.InputAuthorityGeneration != Qa04ParticipationControlModeCanonicalAuthorityV1.InitialInputAuthorityGeneration)
        {
            throw new InvalidDataException("qa04.resident.action-control-mode-drift");
        }
    }

    private static void RequireCanonicalBinding(Qa04CanonicalOperationBindingResultV1 binding)
    {
        if (binding.SourceDescriptor is null || binding.BoundDescriptor is null ||
            binding.Operation is null || binding.OrderKey is null || binding.ScheduledOperation is null)
            throw new InvalidDataException("qa04.resident.action-binding-null");
        if (binding.SourceDescriptor.FamilyToken.Value != ResidentFamily)
            throw new InvalidDataException("qa04.resident.action-family");
        if (binding.Operation.OperationKind != ResidentActionOperationKind)
            throw new InvalidDataException("qa04.resident.action-operation-kind");
        if (binding.OwnerDomain != ResidentDomain)
            throw new InvalidDataException("qa04.resident.action-owner-domain");
        if (binding.Operation.Admission is null || binding.Operation.Admission.SchedulingPolicyGeneration == 0)
            throw new InvalidDataException("qa04.resident.action-admission");

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
            throw new InvalidDataException("qa04.resident.action-binding-drift");
        }
    }
}
