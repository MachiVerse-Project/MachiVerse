using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Runtime;

/// <summary>
/// Narrow persistence boundary used by StepCoordinator. The Simulation runtime depends on the
/// semantic transition-commit contract rather than SQLite implementation details, while the
/// production adapter delegates to the SIM-03 authoritative store.
/// </summary>
public interface IDurableStepTransitionStoreV1
{
    Task<DurableTransitionResult> PersistTransitionCommitAsync(
        ulong effectiveStep,
        ulong resultingStep,
        byte[] resultingStateContinuityToken,
        ulong activeConfigGeneration,
        byte[] activeConfigDigest,
        HistoryRecordMaterial history,
        IReadOnlyCollection<TerminalOperationCommit> terminalOperations,
        CancellationToken cancellationToken = default);
}

public sealed class SqliteStepTransitionStoreV1 : IDurableStepTransitionStoreV1
{
    private readonly SqlitePersistenceStore _store;

    public SqliteStepTransitionStoreV1(SqlitePersistenceStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public Task<DurableTransitionResult> PersistTransitionCommitAsync(
        ulong effectiveStep,
        ulong resultingStep,
        byte[] resultingStateContinuityToken,
        ulong activeConfigGeneration,
        byte[] activeConfigDigest,
        HistoryRecordMaterial history,
        IReadOnlyCollection<TerminalOperationCommit> terminalOperations,
        CancellationToken cancellationToken = default)
        => _store.PersistTransitionCommitAsync(
            effectiveStep,
            resultingStep,
            resultingStateContinuityToken,
            activeConfigGeneration,
            activeConfigDigest,
            history,
            terminalOperations,
            cancellationToken);
}

public sealed record DurableStepFinalizationResultV1(
    ulong BasisStep,
    ulong ResultingStep,
    ulong HistorySequence,
    byte[] CandidateDiagnosticDigest)
{
    /// <summary>
    /// This result exists only after the authoritative persistence transaction has committed.
    /// The pre-commit StepCandidate itself remains non-publishable.
    /// </summary>
    public bool CanPublishConfirmed => true;
}

public sealed class DurableStepCoordinatorV1
{
    private readonly IDurableStepTransitionStoreV1 _transitionStore;

    public DurableStepCoordinatorV1(IDurableStepTransitionStoreV1 transitionStore)
    {
        _transitionStore = transitionStore ?? throw new ArgumentNullException(nameof(transitionStore));
    }

    public async Task<DurableStepFinalizationResultV1> FinalizeAsync(
        WorldStateV1 basisState,
        StepCandidateV1 candidate,
        OperationSchedulerStateV1 scheduler,
        byte[] resultingStateContinuityToken,
        byte[] activeConfigDigest,
        HistoryRecordMaterial transitionHistory,
        IReadOnlyCollection<TerminalOperationCommit> terminalOperations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(basisState);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(resultingStateContinuityToken);
        ArgumentNullException.ThrowIfNull(activeConfigDigest);
        ArgumentNullException.ThrowIfNull(transitionHistory);
        ArgumentNullException.ThrowIfNull(terminalOperations);

        if (resultingStateContinuityToken.Length != 32)
            throw new ArgumentException("Resulting continuity token must be exactly 32 bytes.", nameof(resultingStateContinuityToken));
        if (activeConfigDigest.Length != 32)
            throw new ArgumentException("Active Config digest must be exactly 32 bytes.", nameof(activeConfigDigest));

        ValidateBasis(basisState, candidate);
        ValidateCommitBarrier(candidate);
        ValidateSchedulerBoundary(candidate, scheduler);
        ValidateTerminalCoverage(candidate, terminalOperations);

        if (transitionHistory.WorldId != candidate.WorldId)
            throw new InvalidDataException("step-finalize.history-world-mismatch");
        if (!string.Equals(transitionHistory.RecordType, "transition.committed.v1", StringComparison.Ordinal))
            throw new InvalidDataException("step-finalize.history-record-type-mismatch");

        // No authoritative in-memory state or scheduler retirement occurs before this await.
        // A thrown persistence error therefore leaves State(S) and the frozen input set intact.
        var durable = await _transitionStore.PersistTransitionCommitAsync(
            candidate.BasisStep,
            candidate.TargetStep,
            resultingStateContinuityToken,
            candidate.ConfigGeneration,
            activeConfigDigest,
            transitionHistory,
            terminalOperations,
            cancellationToken).ConfigureAwait(false);

        if (durable.ResultingStep != candidate.TargetStep)
            throw new InvalidDataException("step-finalize.persistence-result-step-mismatch");

        // This is the first runtime mutation after durable authority moved to State(S+1).
        scheduler.OpenAfterFinalization(candidate.BasisStep);

        return new DurableStepFinalizationResultV1(
            candidate.BasisStep,
            durable.ResultingStep,
            durable.HistorySequence,
            candidate.DiagnosticDigest.ToArray());
    }

    private static void ValidateBasis(WorldStateV1 basisState, StepCandidateV1 candidate)
    {
        if (basisState.Header.WorldId != candidate.WorldId ||
            basisState.Header.Step != candidate.BasisStep ||
            basisState.Header.ConfigGeneration != candidate.ConfigGeneration)
            throw new InvalidDataException("step-finalize.candidate-basis-mismatch");
        if (candidate.TargetStep != candidate.BasisStep + 1)
            throw new InvalidDataException("step-finalize.target-step-mismatch");
        if (candidate.FrozenInput.WorldId != candidate.WorldId ||
            candidate.FrozenInput.BasisStep != candidate.BasisStep)
            throw new InvalidDataException("step-finalize.frozen-input-basis-mismatch");
    }

    private static void ValidateCommitBarrier(StepCandidateV1 candidate)
    {
        if (candidate.CommitDecision.FatalAuthorityFailure)
            throw new InvalidDataException("step-finalize.fatal-authority-invariant");
        if (!candidate.CommitDecision.CanCommit)
            throw new InvalidDataException("step-finalize.commit-blocked-by-invariant");
    }

    private static void ValidateSchedulerBoundary(
        StepCandidateV1 candidate,
        OperationSchedulerStateV1 scheduler)
    {
        if (scheduler.FreezeStep != candidate.BasisStep || scheduler.NextSchedulableStep != candidate.TargetStep)
            throw new InvalidDataException("step-finalize.scheduler-freeze-mismatch");

        var live = scheduler.ForEffectiveStep(candidate.BasisStep);
        var frozen = candidate.FrozenInput.ScheduledOperations;
        if (live.Count != frozen.Count)
            throw new InvalidDataException("step-finalize.scheduler-frozen-set-mismatch");

        for (var index = 0; index < live.Count; index++)
        {
            if (live[index].OperationId != frozen[index].OperationId ||
                live[index].EffectiveStep != frozen[index].EffectiveStep ||
                !live[index].OrderKey.ToDatabaseBytes().AsSpan().SequenceEqual(frozen[index].OrderKey.ToDatabaseBytes()))
                throw new InvalidDataException("step-finalize.scheduler-frozen-set-mismatch");
        }
    }

    private static void ValidateTerminalCoverage(
        StepCandidateV1 candidate,
        IReadOnlyCollection<TerminalOperationCommit> terminalOperations)
    {
        var terminalIds = new HashSet<MachiVerse.Simulation.Core.Determinism.OpaqueId128>();
        foreach (var terminal in terminalOperations)
        {
            ArgumentNullException.ThrowIfNull(terminal);
            if (!terminalIds.Add(terminal.OperationId))
                throw new InvalidDataException("step-finalize.terminal-operation-duplicate");
        }

        var frozenIds = candidate.FrozenInput.ScheduledOperations
            .Select(static operation => operation.OperationId)
            .ToHashSet();
        if (!terminalIds.SetEquals(frozenIds))
            throw new InvalidDataException("step-finalize.terminal-operation-coverage-mismatch");
    }
}
