using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;

namespace MachiVerse.Simulation.Core.Runtime;

public sealed class StepFinalizeMaterialV1
{
    public StepFinalizeMaterialV1(
        ulong activeConfigGeneration,
        ReadOnlySpan<byte> activeConfigDigest,
        ReadOnlySpan<byte> resultingStateContinuityToken,
        HistoryRecordMaterial transitionHistory,
        IEnumerable<TerminalOperationCommit> terminalOperations)
    {
        if (activeConfigGeneration == 0)
            throw new ArgumentOutOfRangeException(nameof(activeConfigGeneration), "ConfigGeneration starts at 1.");
        if (activeConfigDigest.Length != 32)
            throw new ArgumentException("Active ConfigDigest must be exactly 32 bytes.", nameof(activeConfigDigest));
        if (resultingStateContinuityToken.Length != 32)
            throw new ArgumentException("StateContinuityToken must be exactly 32 bytes.", nameof(resultingStateContinuityToken));
        ArgumentNullException.ThrowIfNull(transitionHistory);
        ArgumentNullException.ThrowIfNull(terminalOperations);

        var orderedTerminal = terminalOperations
            .OrderBy(static item => item.OperationId)
            .ToArray();
        if (orderedTerminal.Select(static item => item.OperationId).Distinct().Count() != orderedTerminal.Length)
            throw new InvalidDataException("step-finalize.duplicate-terminal-operation");

        ActiveConfigGeneration = activeConfigGeneration;
        ActiveConfigDigest = activeConfigDigest.ToArray();
        ResultingStateContinuityToken = resultingStateContinuityToken.ToArray();
        TransitionHistory = transitionHistory;
        TerminalOperations = Array.AsReadOnly(orderedTerminal);
    }

    public ulong ActiveConfigGeneration { get; }
    public byte[] ActiveConfigDigest { get; }
    public byte[] ResultingStateContinuityToken { get; }
    public HistoryRecordMaterial TransitionHistory { get; }
    public IReadOnlyList<TerminalOperationCommit> TerminalOperations { get; }
}

public sealed record DurableStepReceiptV1(
    OpaqueId128 CandidateId,
    ulong BasisStep,
    ulong ResultingStep,
    ulong HistorySequence,
    byte[] CandidateDiagnosticDigest)
{
    public bool IsPublishable => true;
}

public interface IStepTransitionDurabilityV1
{
    Task<DurableTransitionResult> CommitAsync(
        StepCandidateV1 candidate,
        StepFinalizeMaterialV1 material,
        CancellationToken cancellationToken = default);
}

public sealed class SqliteStepTransitionDurabilityV1(SqlitePersistenceStore store) : IStepTransitionDurabilityV1
{
    private readonly SqlitePersistenceStore _store = store ?? throw new ArgumentNullException(nameof(store));

    public Task<DurableTransitionResult> CommitAsync(
        StepCandidateV1 candidate,
        StepFinalizeMaterialV1 material,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(material);
        return _store.PersistTransitionCommitAsync(
            candidate.BasisStep,
            candidate.TargetStep,
            material.ResultingStateContinuityToken,
            material.ActiveConfigGeneration,
            material.ActiveConfigDigest,
            material.TransitionHistory,
            material.TerminalOperations,
            cancellationToken);
    }
}

/// <summary>
/// Establishes the only SIM-06 publishable boundary: a StepCandidate remains non-authoritative
/// until the SIM-03 transition transaction has committed successfully. The in-memory scheduler
/// is advanced only after durable commit succeeds.
/// </summary>
public sealed class StepFinalizationCoordinatorV1(IStepTransitionDurabilityV1 durability)
{
    private readonly IStepTransitionDurabilityV1 _durability = durability ?? throw new ArgumentNullException(nameof(durability));

    public async Task<DurableStepReceiptV1> FinalizeAsync(
        StepCandidateV1 candidate,
        OperationSchedulerStateV1 scheduler,
        StepFinalizeMaterialV1 material,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(material);

        if (!candidate.CommitDecision.CanCommit)
            throw new InvalidDataException(candidate.CommitDecision.FatalAuthorityFailure
                ? "step-finalize.fatal-authority-invariant"
                : "step-finalize.commit-blocked-by-invariant");
        if (candidate.IsPublishable)
            throw new InvalidDataException("step-finalize.candidate-must-be-non-authoritative");
        if (scheduler.FreezeStep != candidate.BasisStep)
            throw new InvalidDataException("step-finalize.scheduler-freeze-mismatch");
        if (scheduler.NextSchedulableStep < candidate.TargetStep)
            throw new InvalidDataException("step-finalize.scheduler-barrier-mismatch");
        if (material.TransitionHistory.WorldId != candidate.WorldId)
            throw new InvalidDataException("step-finalize.history-world-mismatch");

        RequireTerminalCoverage(candidate, material.TerminalOperations);

        // No authoritative in-memory state is advanced before this await. If the persistence
        // implementation throws/crashes before COMMIT, the scheduler remains frozen at State(S)
        // and no publishable receipt is created.
        var durable = await _durability.CommitAsync(candidate, material, cancellationToken);
        if (durable.ResultingStep != candidate.TargetStep)
            throw new InvalidDataException("step-finalize.persistence-result-step-mismatch");
        if (durable.HistorySequence != material.TransitionHistory.Sequence)
            throw new InvalidDataException("step-finalize.persistence-history-sequence-mismatch");

        // Pre-validation above makes this a non-throwing semantic transition for a valid
        // scheduler. It intentionally happens after durable COMMIT.
        scheduler.OpenAfterFinalization(candidate.BasisStep);

        return new DurableStepReceiptV1(
            candidate.CandidateId,
            candidate.BasisStep,
            durable.ResultingStep,
            durable.HistorySequence,
            candidate.DiagnosticDigest.ToArray());
    }

    private static void RequireTerminalCoverage(
        StepCandidateV1 candidate,
        IReadOnlyCollection<TerminalOperationCommit> terminalOperations)
    {
        var expected = candidate.FrozenInput.ScheduledOperations
            .Select(static item => item.OperationId)
            .ToHashSet();
        var actual = terminalOperations
            .Select(static item => item.OperationId)
            .ToHashSet();
        if (!expected.SetEquals(actual))
            throw new InvalidDataException("step-finalize.terminal-operation-coverage-mismatch");
    }
}
