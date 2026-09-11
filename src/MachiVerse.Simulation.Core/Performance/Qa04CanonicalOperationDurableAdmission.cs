using Google.Protobuf;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04CanonicalOperationDurableAdmissionReceiptV1(
    Qa04CanonicalOperationBindingResultV1 Binding,
    OperationDurableObservationV1 Accepted,
    OperationDurableObservationV1 Scheduled)
{
    public bool Passed
        => Accepted.Lifecycle == OperationLifecycleStateV1.AcceptedDurable &&
           Scheduled.Lifecycle == OperationLifecycleStateV1.ScheduledDurable &&
           Scheduled.EffectiveStep == Binding.ScheduledOperation.EffectiveStep;
}

/// <summary>
/// Crosses the ordinary SIM-05 SQLite ACCEPTED and SCHEDULED boundaries for an authority-complete
/// perf.reference.v1 Operation binding. Domain execution/terminal mutation remains owned by the
/// normal Step loop; this component only establishes durable operation custody and scheduling.
/// </summary>
public static class Qa04CanonicalOperationDurableAdmissionV1
{
    public static OperationSchedulingPolicyV1 CreateCanonicalPolicy(ulong ownerConfigGeneration)
        => new(
            ownerConfigGeneration,
            minLeadSteps: 1,
            defaultDeadlineWindowSteps: null,
            graceSteps: 0,
            OperationLatePolicyV1.Reject);

    public static async Task<Qa04CanonicalOperationDurableAdmissionReceiptV1> AdmitAndScheduleAsync(
        SqlitePersistenceStore store,
        Qa04CanonicalOperationBindingResultV1 binding,
        OperationSchedulingPolicyV1 policy,
        ulong nextSchedulableStep,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(policy);

        var operation = binding.Operation;
        if (operation.Admission is null || operation.Candidate is null)
            throw new InvalidDataException("qa04.workload.operation-scheduling-context-missing");
        if (policy.OwnerConfigGeneration != operation.Admission.SchedulingPolicyGeneration)
            throw new InvalidDataException("qa04.workload.operation-policy-generation-mismatch");
        if (nextSchedulableStep != checked(binding.SourceDescriptor.InjectionStep + 1))
            throw new InvalidDataException("qa04.workload.operation-scheduler-basis-drift");

        var expectedDigest = Qa04CanonicalOperationBindingV1.ComputeImmutablePayloadDigest(operation);
        if (!expectedDigest.AsSpan().SequenceEqual(binding.BoundDescriptor.PayloadDigest) ||
            !expectedDigest.AsSpan().SequenceEqual(operation.ImmutablePayloadDigest.Span))
            throw new InvalidDataException("qa04.workload.operation-bound-digest-mismatch");

        var coordinator = new DurableOperationCoordinatorV1(
            store,
            new OperationSchedulingPolicyHistoryV1([
                new OperationSchedulingPolicyHistoryEntryV1(policy, 0, null),
            ]));
        var operationId = binding.SourceDescriptor.OperationId;

        var existing = await coordinator.ObserveAsync(operationId, expectedDigest, cancellationToken)
            .ConfigureAwait(false);
        if (existing?.Lifecycle is OperationLifecycleStateV1.ScheduledDurable or OperationLifecycleStateV1.TerminalDurable)
        {
            return new Qa04CanonicalOperationDurableAdmissionReceiptV1(
                binding,
                existing,
                existing);
        }

        OperationDurableObservationV1 accepted;
        if (existing is null)
        {
            var acceptedHistory = await NextHistoryAsync(
                store,
                binding,
                "operation.accepted.v1",
                "persistence.operation-accepted",
                writer =>
                {
                    writer.WriteMapStart(3);
                    writer.WriteUnsigned(0); writer.WriteBytes(operationId.ToBytes());
                    writer.WriteUnsigned(1); writer.WriteBytes(expectedDigest);
                    writer.WriteUnsigned(2); writer.WriteAsciiText(operation.OperationKind);
                },
                cancellationToken).ConfigureAwait(false);
            accepted = await coordinator.AcceptOrConvergeAsync(
                operationId,
                expectedDigest,
                acceptedHistory,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            accepted = existing;
        }

        if (accepted.Lifecycle != OperationLifecycleStateV1.AcceptedDurable)
            throw new InvalidDataException("qa04.workload.operation-accepted-boundary-invalid");

        var admission = new OperationSchedulingAdmissionV1(
            operation.Admission.AdmissionBasisStep,
            operation.Admission.SchedulingPolicyGeneration,
            operation.Admission.HasRequestedNotBeforeStep ? operation.Admission.RequestedNotBeforeStep : null,
            operation.Admission.HasRequestedDeadlineStep ? operation.Admission.RequestedDeadlineStep : null);
        var decision = coordinator.Plan(
            admission,
            new OperationSchedulingBarrierV1(nextSchedulableStep, PauseActive: false, PauseBasisStep: null),
            operation.Candidate.CandidateStep);
        if (decision.Kind != OperationSchedulingDecisionKindV1.Scheduled ||
            decision.EffectiveStep != binding.ScheduledOperation.EffectiveStep)
            throw new InvalidDataException("qa04.workload.operation-effective-step-drift");

        var scheduledHistory = await NextHistoryAsync(
            store,
            binding,
            "operation.scheduled.v1",
            "persistence.operation-scheduled",
            writer =>
            {
                writer.WriteMapStart(3);
                writer.WriteUnsigned(0); writer.WriteBytes(operationId.ToBytes());
                writer.WriteUnsigned(1); writer.WriteUnsigned(decision.EffectiveStep!.Value);
                writer.WriteUnsigned(2); writer.WriteBytes(binding.OrderKey.ToDatabaseBytes());
            },
            cancellationToken).ConfigureAwait(false);
        var scheduled = await coordinator.ScheduleOrConvergeAsync(
            operationId,
            expectedDigest,
            decision,
            binding.OrderKey,
            scheduledHistory,
            cancellationToken).ConfigureAwait(false);
        if (scheduled.Lifecycle != OperationLifecycleStateV1.ScheduledDurable ||
            scheduled.EffectiveStep != binding.ScheduledOperation.EffectiveStep)
            throw new InvalidDataException("qa04.workload.operation-scheduled-boundary-invalid");

        return new Qa04CanonicalOperationDurableAdmissionReceiptV1(binding, accepted, scheduled);
    }

    private static async Task<HistoryRecordMaterial> NextHistoryAsync(
        SqlitePersistenceStore store,
        Qa04CanonicalOperationBindingResultV1 binding,
        string recordType,
        string schemaId,
        Action<Determinism.MvDcborWriter> writeNormalized,
        CancellationToken cancellationToken)
    {
        var anchor = await store.ReadHistoryAnchorAsync(cancellationToken).ConfigureAwait(false);
        if (anchor.Sequence == ulong.MaxValue)
            throw new OverflowException("HistorySequence cannot wrap.");
        return HistoryRecordMaterial.Create(
            Qa04ReferenceLoadV1.WorldId,
            checked(anchor.Sequence + 1),
            anchor.Digest,
            recordType,
            schemaId,
            1,
            0,
            binding.Operation.ToByteArray(),
            writeNormalized);
    }
}
