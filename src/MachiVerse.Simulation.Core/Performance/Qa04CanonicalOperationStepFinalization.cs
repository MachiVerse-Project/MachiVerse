using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04CanonicalOperationStepFinalizationResultV1(
    Qa04CanonicalOperationStepPreparationResultV1 Preparation,
    StepFinalizeMaterialV1 FinalizeMaterial,
    DurableStepReceiptV1 DurableReceipt,
    AuthoritativeStepWorldStateV1 AuthoritativeState,
    IReadOnlyList<DurableOperationStateV1> TerminalOperations,
    byte[] ResultingContinuityToken);

/// <summary>
/// Gate-2 Steps 6-8 bridge. It requires the actual frozen canonical Operation set to become terminal
/// in the same SQLite transition transaction, keeps the scheduler frozen until that COMMIT succeeds,
/// verifies the durable recovery/Operation state after COMMIT, and only then publishes State(S+1).
/// Terminal result semantics are supplied by the caller; this bridge does not invent result codes.
/// </summary>
public static class Qa04CanonicalOperationStepFinalizationV1
{
    public static async Task<Qa04CanonicalOperationStepFinalizationResultV1> CommitAndPublishAsync(
        SqlitePersistenceStore store,
        OperationSchedulerStateV1 scheduler,
        Qa04CanonicalOperationStepPreparationResultV1 preparation,
        IReadOnlyCollection<TerminalOperationCommit> terminalOperations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(terminalOperations);

        var candidate = preparation.Candidate;
        var prepared = preparation.PreparedState;
        if (candidate.WorldId != Qa04ReferenceLoadV1.WorldId)
            throw new InvalidDataException("qa04.full-step.finalization-world-id-drift");
        if (!candidate.CommitDecision.CanCommit)
            throw new InvalidDataException("qa04.full-step.finalization-candidate-not-commit-eligible");
        if (candidate.IsPublishable || prepared.IsPublishable)
            throw new InvalidDataException("qa04.full-step.finalization-premature-authority");
        if (prepared.CandidateId != candidate.CandidateId ||
            prepared.BasisStep != candidate.BasisStep ||
            prepared.TargetStep != candidate.TargetStep)
            throw new InvalidDataException("qa04.full-step.finalization-prepared-candidate-drift");

        var orderedTerminal = ValidateTerminalCoverage(candidate, terminalOperations);
        var before = await store.ReadRecoveryHeadAsync(cancellationToken).ConfigureAwait(false);
        if (before.FinalizedStep != candidate.BasisStep)
            throw new InvalidDataException("qa04.full-step.finalization-persistence-basis-drift");
        if (before.ConfigGeneration != candidate.ConfigGeneration ||
            !CryptographicOperations.FixedTimeEquals(before.ConfigDigest, candidate.ConfigDigest))
            throw new InvalidDataException("qa04.full-step.finalization-persistence-config-drift");

        var anchor = await store.ReadHistoryAnchorAsync(cancellationToken).ConfigureAwait(false);
        if (anchor.Sequence == ulong.MaxValue)
            throw new InvalidDataException("qa04.full-step.finalization-history-sequence-overflow");
        var transition = CreateTransitionHistory(
            candidate,
            prepared,
            checked(anchor.Sequence + 1UL),
            anchor.Digest);
        var resultingContinuity = HistoryIntegrity.ComputeTransitionContinuityToken(
            candidate.WorldId,
            candidate.TargetStep,
            before.ContinuityToken,
            transition.RecordDigest);
        var material = new StepFinalizeMaterialV1(
            candidate.ConfigGeneration,
            candidate.ConfigDigest,
            resultingContinuity,
            transition,
            orderedTerminal);

        // This is the real authority boundary. StepFinalizationCoordinatorV1 keeps the scheduler
        // frozen and returns no publishable receipt if SQLite COMMIT fails.
        var receipt = await new StepFinalizationCoordinatorV1(new SqliteStepTransitionDurabilityV1(store))
            .FinalizeAsync(candidate, scheduler, material, cancellationToken)
            .ConfigureAwait(false);

        var after = await store.ReadRecoveryHeadAsync(cancellationToken).ConfigureAwait(false);
        if (after.FinalizedStep != candidate.TargetStep ||
            !CryptographicOperations.FixedTimeEquals(after.ContinuityToken, resultingContinuity) ||
            after.ConfigGeneration != candidate.ConfigGeneration ||
            !CryptographicOperations.FixedTimeEquals(after.ConfigDigest, candidate.ConfigDigest))
            throw new InvalidDataException("qa04.full-step.finalization-recovery-head-drift");
        if (!receipt.IsPublishable ||
            receipt.CandidateId != candidate.CandidateId ||
            receipt.BasisStep != candidate.BasisStep ||
            receipt.ResultingStep != candidate.TargetStep ||
            receipt.HistorySequence != transition.Sequence ||
            !CryptographicOperations.FixedTimeEquals(receipt.CandidateDiagnosticDigest, candidate.DiagnosticDigest))
            throw new InvalidDataException("qa04.full-step.finalization-durable-receipt-drift");
        if (scheduler.FreezeStep is not null ||
            scheduler.NextSchedulableStep != candidate.TargetStep ||
            scheduler.ForEffectiveStep(candidate.BasisStep).Count != 0)
            throw new InvalidDataException("qa04.full-step.finalization-scheduler-post-commit-drift");

        var durableOperations = new List<DurableOperationStateV1>(orderedTerminal.Count);
        foreach (var expected in orderedTerminal)
        {
            var durable = await store.ReadOperationStateAsync(expected.OperationId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("qa04.full-step.finalization-terminal-operation-missing");
            if (durable.Lifecycle != DurableOperationLifecycleV1.TerminalDurable ||
                durable.EffectiveStep != candidate.BasisStep ||
                durable.TerminalSequence != receipt.HistorySequence ||
                durable.TerminalStatus != expected.TerminalStatus ||
                !string.Equals(durable.ResultCode, expected.ResultCode, StringComparison.Ordinal) ||
                !PayloadEquals(durable.RichResultPayload, expected.RichResultPayload))
            {
                throw new InvalidDataException("qa04.full-step.finalization-terminal-operation-drift");
            }
            durableOperations.Add(durable);
        }

        // Publication happens only after the durable recovery head, scheduler retirement, and all
        // terminal Operation rows have been observed after the successful SQLite COMMIT.
        var authoritative = StepStateApplicationV1.Publish(prepared, receipt);
        if (!authoritative.IsPublishable ||
            authoritative.State.Header.Step != candidate.TargetStep ||
            authoritative.State.Header.PreviousStateDigest is null ||
            !CryptographicOperations.FixedTimeEquals(
                authoritative.State.Header.PreviousStateDigest,
                prepared.BasisStateDigest))
            throw new InvalidDataException("qa04.full-step.finalization-published-state-drift");

        return new Qa04CanonicalOperationStepFinalizationResultV1(
            preparation,
            material,
            receipt,
            authoritative,
            Array.AsReadOnly(durableOperations.ToArray()),
            resultingContinuity.ToArray());
    }

    private static IReadOnlyList<TerminalOperationCommit> ValidateTerminalCoverage(
        StepCandidateV1 candidate,
        IReadOnlyCollection<TerminalOperationCommit> terminalOperations)
    {
        var ordered = terminalOperations.OrderBy(static item => item.OperationId).ToArray();
        if (ordered.Select(static item => item.OperationId).Distinct().Count() != ordered.Length)
            throw new InvalidDataException("qa04.full-step.finalization-terminal-operation-duplicate");

        var expectedIds = candidate.FrozenInput.ScheduledOperations
            .Select(static operation => operation.OperationId)
            .ToHashSet();
        if (!expectedIds.SetEquals(ordered.Select(static terminal => terminal.OperationId)))
            throw new InvalidDataException("qa04.full-step.finalization-terminal-coverage-drift");

        foreach (var terminal in ordered)
        {
            if (terminal.OperationId.IsZero)
                throw new InvalidDataException("qa04.full-step.finalization-terminal-operation-id-zero");
            _ = new StableToken(terminal.ResultCode);
            if (!Enum.IsDefined(typeof(CoreOperationResultStatusV1), terminal.TerminalStatus) ||
                !OperationLifecycleRulesV1.IsTerminalResult((CoreOperationResultStatusV1)terminal.TerminalStatus))
                throw new InvalidDataException("qa04.full-step.finalization-terminal-status-invalid");
        }

        return Array.AsReadOnly(ordered);
    }

    private static HistoryRecordMaterial CreateTransitionHistory(
        StepCandidateV1 candidate,
        PreparedStepWorldStateV1 prepared,
        ulong sequence,
        ReadOnlySpan<byte> previousRecordDigest)
        => HistoryRecordMaterial.Create(
            candidate.WorldId,
            sequence,
            previousRecordDigest,
            recordType: "transition.committed.v1",
            payloadSchemaId: "persistence.transition-committed",
            payloadSchemaMajor: 1,
            payloadSchemaMinor: 0,
            payloadBytes: candidate.DiagnosticDigest
                .Concat(prepared.ResultingState.Diagnostic.StateDigest)
                .ToArray(),
            writeNormalizedPayload: writer =>
            {
                writer.WriteMapStart(7);
                writer.WriteUnsigned(0); writer.WriteUnsigned(candidate.BasisStep);
                writer.WriteUnsigned(1); writer.WriteUnsigned(candidate.TargetStep);
                writer.WriteUnsigned(2); writer.WriteBytes(candidate.CandidateId.ToBytes());
                writer.WriteUnsigned(3); writer.WriteBytes(candidate.DiagnosticDigest);
                writer.WriteUnsigned(4); writer.WriteBytes(prepared.BasisStateDigest);
                writer.WriteUnsigned(5); writer.WriteBytes(prepared.ResultingState.Diagnostic.StateDigest);
                writer.WriteUnsigned(6);
                writer.WriteArrayStart((ulong)candidate.PartitionCandidates.Count);
                foreach (var partition in candidate.PartitionCandidates)
                {
                    writer.WriteArrayStart(2);
                    writer.WriteAsciiText(partition.PartitionId.Value);
                    writer.WriteBytes(partition.CandidateDigest);
                }
            });

    private static bool PayloadEquals(byte[]? left, byte[]? right)
        => left is null
            ? right is null
            : right is not null && left.AsSpan().SequenceEqual(right);
}
