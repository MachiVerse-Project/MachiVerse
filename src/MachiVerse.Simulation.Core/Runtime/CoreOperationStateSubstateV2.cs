using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Runtime;

/// <summary>
/// Step-runtime projection of the canonical core.operation-state /2.0 authority. The semantic
/// digest is owned by <see cref="CoreOperationStateSnapshotAuthorityV2"/> so Snapshot and live
/// WorldState use the same Operation + persistent CrossDomainTransaction authority.
/// </summary>
public static class CoreOperationStateSubstateV2
{
    public static WorldSubstateRefV1 Canonicalize(
        IEnumerable<DurableOperationStateV1> operations,
        IEnumerable<CrossDomainTransactionStateV1> transactions,
        ulong basisStep)
    {
        var authority = CoreOperationStateSnapshotAuthorityV2.Create(
            operations ?? throw new ArgumentNullException(nameof(operations)),
            transactions ?? throw new ArgumentNullException(nameof(transactions)),
            basisStep);
        return new WorldSubstateRefV1(
            CoreOperationStateSnapshotAuthorityV2.Schema,
            authority.CanonicalDigest);
    }

    public static StepCoreSubstateCandidateV1 CreatePostTransitionCandidate(
        WorldStateV1 basisState,
        IEnumerable<DurableOperationStateV1> durableBeforeTransition,
        IReadOnlyCollection<CrossDomainTransactionStateV1> transactions,
        IReadOnlyCollection<TerminalOperationCommit> terminalOperations,
        ulong effectiveStep,
        ulong terminalHistorySequence)
    {
        ArgumentNullException.ThrowIfNull(basisState);
        ArgumentNullException.ThrowIfNull(durableBeforeTransition);
        ArgumentNullException.ThrowIfNull(transactions);
        ArgumentNullException.ThrowIfNull(terminalOperations);
        if (effectiveStep != basisState.Header.Step)
            throw new InvalidDataException("operation-substate-v2.effective-step-mismatch");
        if (basisState.OperationState.Schema != CoreOperationStateSnapshotAuthorityV2.Schema)
            throw new InvalidDataException("operation-substate-v2.basis-schema-mismatch");

        var projectedOperations = DurableOperationSubstateV1.ProjectTerminalCommit(
            durableBeforeTransition,
            terminalOperations,
            effectiveStep,
            terminalHistorySequence);
        var resulting = Canonicalize(
            projectedOperations,
            transactions,
            checked(effectiveStep + 1UL));

        return new StepCoreSubstateCandidateV1(
            StepCoreSubstateKindV1.Operation,
            effectiveStep,
            basisState.OperationState,
            resulting);
    }
}
