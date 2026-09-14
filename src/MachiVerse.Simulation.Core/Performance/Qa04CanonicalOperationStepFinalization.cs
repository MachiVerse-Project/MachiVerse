using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04CanonicalOperationStepFinalizationResultV1(
    Qa04CanonicalOperationStepPreparationResultV1 Preparation,
    StepFinalizeMaterialV1 FinalizeMaterial,
    DurableStepReceiptV1 DurableReceipt,
    AuthoritativeStepWorldStateV1 AuthoritativeState,
    IReadOnlyList<DurableOperationStateV1> TerminalOperations,
    byte[] ResultingContinuityToken)
{
    public Qa04CanonicalOperationPostCommitVerificationV1? PostCommitVerification { get; init; }
}

/// <summary>
/// Gate-2 Steps 6-12 bridge. It requires the actual frozen canonical Operation set to become
/// terminal in the same SQLite transition transaction, binds the projected Scheduler/Operation
/// core substates into the ordinary StepCandidate before COMMIT, keeps the scheduler frozen until
/// that COMMIT succeeds, publishes only after the durable boundary, and then re-verifies the
/// published State(S+1) against the complete committed Operation catalog and all 97 partitions.
/// Terminal result semantics are supplied by the caller; this bridge does not invent result codes.
/// When persistent CrossDomainTransaction authority is supplied, core.operation-state /2.0 is
/// preserved across the transition instead of collapsing to the operation-only v1 authority.
/// </summary>
public static class Qa04CanonicalOperationStepFinalizationV1
{
    public static async Task<Qa04CanonicalOperationStepFinalizationResultV1> CommitAndPublishAsync(
        SqlitePersistenceStore store,
        OperationSchedulerStateV1 scheduler,
        Qa04CanonicalOperationStepPreparationResultV1 preparation,
        IReadOnlyCollection<TerminalOperationCommit> terminalOperations,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<CrossDomainTransactionStateV1>? crossDomainTransactions = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(terminalOperations);

        var step5Candidate = preparation.Candidate;
        var step5Prepared = preparation.PreparedState;
        var basisState = preparation.BasisState
            ?? throw new InvalidDataException("qa04.full-step.finalization-basis-state-missing");
        if (step5Candidate.WorldId != Qa04ReferenceLoadV1.WorldId)
            throw new InvalidDataException("qa04.full-step.finalization-world-id-drift");
        if (!step5Candidate.CommitDecision.CanCommit)
            throw new InvalidDataException("qa04.full-step.finalization-candidate-not-commit-eligible");
        if (step5Candidate.IsPublishable || step5Prepared.IsPublishable)
            throw new InvalidDataException("qa04.full-step.finalization-premature-authority");
        if (basisState.Header.WorldId != step5Candidate.WorldId ||
            basisState.Header.Step != step5Candidate.BasisStep ||
            !CryptographicOperations.FixedTimeEquals(
                basisState.Diagnostic.StateDigest,
                step5Prepared.BasisStateDigest))
            throw new InvalidDataException("qa04.full-step.finalization-basis-state-drift");
        if (step5Prepared.CandidateId != step5Candidate.CandidateId ||
            step5Prepared.BasisStep != step5Candidate.BasisStep ||
            step5Prepared.TargetStep != step5Candidate.TargetStep)
            throw new InvalidDataException("qa04.full-step.finalization-prepared-candidate-drift");
        if (step5Candidate.CoreSubstateCandidates.Count != 0 ||
            step5Candidate.TransactionCandidates.Count != 0)
            throw new InvalidDataException("qa04.full-step.finalization-step5-authority-surface-drift");

        var orderedTerminal = ValidateTerminalCoverage(step5Candidate, terminalOperations);
        var before = await store.ReadRecoveryHeadAsync(cancellationToken).ConfigureAwait(false);
        if (before.FinalizedStep != step5Candidate.BasisStep)
            throw new InvalidDataException("qa04.full-step.finalization-persistence-basis-drift");
        if (before.ConfigGeneration != step5Candidate.ConfigGeneration ||
            !CryptographicOperations.FixedTimeEquals(before.ConfigDigest, step5Candidate.ConfigDigest))
            throw new InvalidDataException("qa04.full-step.finalization-persistence-config-drift");

        var anchor = await store.ReadHistoryAnchorAsync(cancellationToken).ConfigureAwait(false);
        if (anchor.Sequence == ulong.MaxValue)
            throw new InvalidDataException("qa04.full-step.finalization-history-sequence-overflow");
        var transitionSequence = checked(anchor.Sequence + 1UL);

        // Scheduler and Operation authority for State(S+1) is not inferred from the Step-5 domain
        // outputs. Project it through the standard core-substate contracts from the actual frozen
        // scheduler and the complete committed SQLite Operation catalog immediately before COMMIT.
        var durableBefore = await store.ListOperationStatesCanonicalAsync(cancellationToken).ConfigureAwait(false);
        var schedulerCore = OperationSchedulerSubstateV1.CreatePostFinalizationCandidate(
            basisState,
            scheduler,
            step5Candidate.FrozenInput);
        StepCoreSubstateCandidateV1 operationCore;
        if (crossDomainTransactions is null)
        {
            operationCore = DurableOperationSubstateV1.CreatePostTransitionCandidate(
                basisState,
                durableBefore,
                orderedTerminal,
                step5Candidate.BasisStep,
                transitionSequence);
        }
        else
        {
            var expectedBasisOperation = CoreOperationStateSubstateV2.Canonicalize(
                durableBefore,
                crossDomainTransactions,
                step5Candidate.BasisStep);
            if (basisState.OperationState.Schema != expectedBasisOperation.Schema ||
                !CryptographicOperations.FixedTimeEquals(
                    basisState.OperationState.CanonicalDigest,
                    expectedBasisOperation.CanonicalDigest))
                throw new InvalidDataException("qa04.full-step.finalization-operation-v2-basis-drift");

            operationCore = CoreOperationStateSubstateV2.CreatePostTransitionCandidate(
                basisState,
                durableBefore,
                crossDomainTransactions,
                orderedTerminal,
                step5Candidate.BasisStep,
                transitionSequence);
        }
        var coreCandidates = new[] { schedulerCore, operationCore }
            .OrderBy(static candidate => candidate.Kind)
            .ToArray();

        var candidate = StepCandidateV1.Build(
            step5Candidate.CandidateId,
            basisState,
            step5Candidate.FrozenInput,
            preparation.DomainOutputs.Outputs,
            step5Candidate.ConflictResolutions,
            invariantResults: step5Candidate.InvariantResults,
            coreSubstateCandidates: coreCandidates);
        RequirePartitionCandidateStability(step5Candidate, candidate);
        if (!candidate.CommitDecision.CanCommit ||
            candidate.CoreSubstateCandidates.Count != 2 ||
            candidate.CoreSubstateCandidates.Select(static core => core.Kind).ToHashSet().SetEquals(
                new[] { StepCoreSubstateKindV1.Scheduler, StepCoreSubstateKindV1.Operation }) is false)
            throw new InvalidDataException("qa04.full-step.finalization-core-candidate-drift");

        var partitionMaterials = candidate.PartitionCandidates
            .Select(partition => new StepPartitionStateMaterialV1(
                step5Prepared.ResultingState.Partitions.Get(partition.PartitionId.Value).Header))
            .ToArray();
        var coreMaterials = coreCandidates
            .Select(static core => new StepCoreSubstateStateMaterialV1(core.Kind, core.ResultingState))
            .ToArray();
        var prepared = StepStateApplicationV1.Prepare(
            basisState,
            candidate,
            partitionMaterials,
            coreMaterials);
        if (prepared.IsPublishable ||
            prepared.CandidateId != candidate.CandidateId ||
            prepared.BasisStep != candidate.BasisStep ||
            prepared.TargetStep != candidate.TargetStep)
            throw new InvalidDataException("qa04.full-step.finalization-core-preparation-drift");

        var transition = CreateTransitionHistory(
            candidate,
            prepared,
            transitionSequence,
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

        // Steps 9-12: verify semantic/digest authority after COMMIT against all 97 partitions, the
        // complete durable Operation catalog, optional persistent transaction authority, and the
        // reopened scheduler. No result is returned if any one of these authorities disagrees.
        var durableCatalog = await store.ListOperationStatesCanonicalAsync(cancellationToken).ConfigureAwait(false);
        var verification = Qa04CanonicalOperationPostCommitVerifierV1.Verify(
            preparation,
            prepared,
            receipt,
            authoritative.State,
            scheduler,
            durableCatalog,
            crossDomainTransactions);

        return new Qa04CanonicalOperationStepFinalizationResultV1(
            preparation,
            material,
            receipt,
            authoritative,
            Array.AsReadOnly(durableOperations.ToArray()),
            resultingContinuity.ToArray())
        {
            PostCommitVerification = verification,
        };
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

    private static void RequirePartitionCandidateStability(
        StepCandidateV1 step5Candidate,
        StepCandidateV1 finalCandidate)
    {
        if (step5Candidate.PartitionCandidates.Count != finalCandidate.PartitionCandidates.Count)
            throw new InvalidDataException("qa04.full-step.finalization-partition-candidate-count-drift");
        var expected = step5Candidate.PartitionCandidates.ToDictionary(static item => item.PartitionId);
        foreach (var actual in finalCandidate.PartitionCandidates)
        {
            if (!expected.TryGetValue(actual.PartitionId, out var original) ||
                actual.OwnerDomain != original.OwnerDomain ||
                actual.BasisRevision != original.BasisRevision ||
                actual.CandidateRevision != original.CandidateRevision ||
                actual.BasisStep != original.BasisStep ||
                actual.TargetStep != original.TargetStep ||
                !CryptographicOperations.FixedTimeEquals(actual.ChangeSetDigest, original.ChangeSetDigest) ||
                !CryptographicOperations.FixedTimeEquals(actual.CandidateDigest, original.CandidateDigest))
                throw new InvalidDataException($"qa04.full-step.finalization-partition-candidate-drift:{actual.PartitionId.Value}");
        }
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
