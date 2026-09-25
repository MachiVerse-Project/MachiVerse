using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04TransitionHistoryTailReplayProofV1(
    ulong SnapshotStep,
    ulong FinalStep,
    int TransitionCount,
    byte[] FinalContinuityToken);

/// <summary>
/// Semantic replay gate for the transition.committed.v1 history tail after a canonical Snapshot
/// anchor. Link-chain integrity and each persisted transition physical/semantic representation are
/// both validated before the tail is accepted as recovery authority.
/// </summary>
public static class Qa04TransitionHistoryTailReplayV1
{
    private static readonly HashSet<string> CanonicalHistoryTypes = new(StringComparer.Ordinal)
    {
        "world.genesis.v1",
        "qa04.operation-batch.scheduled.v1",
        "qa04.detail-promotion-decision.v1",
        "transition.committed.v1",
        "snapshot.committed.v1",
    };

    public static async Task<Qa04TransitionHistoryTailReplayProofV1> VerifyAsync(
        SqlitePersistenceStore store,
        ulong snapshotStep,
        ulong snapshotHistoryAnchorSequence,
        ReadOnlyMemory<byte> snapshotContinuityToken,
        WorldStateV1 finalState,
        ReadOnlyMemory<byte> finalContinuityToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(finalState);
        if (snapshotContinuityToken.Length != 32 || finalContinuityToken.Length != 32)
            throw new ArgumentException("Continuity tokens must be 32 bytes.");
        if (finalState.Header.Step <= snapshotStep)
            throw new InvalidDataException("qa04.transition-replay.final-step-not-after-snapshot");

        _ = await store.ValidateHistoryLinkChainAsync(CanonicalHistoryTypes, cancellationToken)
            .ConfigureAwait(false);
        var expectedTransitionCount = checked(finalState.Header.Step - snapshotStep);
        var expectedEffectiveStep = snapshotStep;
        var continuity = snapshotContinuityToken.ToArray();
        var transitionCount = 0;
        Qa04TransitionCommittedAuthorityV1? last = null;
        await foreach (var transition in store.StreamQa04CanonicalTransitionHistoryAfterAsync(
                           snapshotHistoryAnchorSequence,
                           cancellationToken)
                           .ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var expectedResultingStep = checked(expectedEffectiveStep + 1UL);
            if (transition.EffectiveStep != expectedEffectiveStep ||
                transition.ResultingStep != expectedResultingStep)
                throw new InvalidDataException("qa04.transition-replay.step-gap");
            if (!CryptographicOperations.FixedTimeEquals(
                    transition.PreviousStateContinuityToken,
                    continuity))
                throw new InvalidDataException("qa04.transition-replay.previous-continuity-drift");
            if (transition.ActiveConfigGeneration != finalState.Header.ConfigGeneration ||
                !CryptographicOperations.FixedTimeEquals(
                    transition.ActiveConfigDigest,
                    finalState.Diagnostic.ConfigDigest))
                throw new InvalidDataException("qa04.transition-replay.config-drift");

            var injectionStep = checked(transition.EffectiveStep - 1UL);
            var bindings = Qa04ReferenceLoadV1.OperationsForStep(injectionStep)
                .Select(descriptor => Qa04CanonicalOperationBindingV1.Bind(
                    descriptor,
                    transition.ActiveConfigGeneration))
                .OrderBy(static binding => binding.OrderKey)
                .ThenBy(static binding => binding.SourceDescriptor.OperationId)
                .ToArray();
            if (bindings.Length != transition.AppliedOperationIds.Count ||
                bindings.Length != transition.OperationOutcomes.Count ||
                (ulong)bindings.Length != Qa04ReferenceLoadV1.OperationCountForStep(injectionStep))
                throw new InvalidDataException("qa04.transition-replay.operation-cardinality-drift");
            for (var operationIndex = 0; operationIndex < bindings.Length; operationIndex++)
            {
                if (bindings[operationIndex].SourceDescriptor.OperationId !=
                    transition.AppliedOperationIds[operationIndex])
                    throw new InvalidDataException("qa04.transition-replay.operation-order-drift");
            }
            _ = Qa04TerminalSemanticAuthorityV1.ComputeBatchDigest(
                bindings,
                transition.OperationOutcomes);

            continuity = transition.ResultingStateContinuityToken.ToArray();
            expectedEffectiveStep = expectedResultingStep;
            transitionCount++;
            last = transition;
        }

        if ((ulong)transitionCount != expectedTransitionCount)
            throw new InvalidDataException("qa04.transition-replay.transition-count-drift");
        if (expectedEffectiveStep != finalState.Header.Step)
            throw new InvalidDataException("qa04.transition-replay.final-step-drift");
        if (!CryptographicOperations.FixedTimeEquals(continuity, finalContinuityToken.Span))
            throw new InvalidDataException("qa04.transition-replay.final-continuity-drift");
        if (last is null ||
            !CryptographicOperations.FixedTimeEquals(
                last.StateDiagnosticHash,
                finalState.Diagnostic.StateDigest))
            throw new InvalidDataException("qa04.transition-replay.final-state-diagnostic-drift");

        return new Qa04TransitionHistoryTailReplayProofV1(
            snapshotStep,
            finalState.Header.Step,
            transitionCount,
            continuity);
    }
}
