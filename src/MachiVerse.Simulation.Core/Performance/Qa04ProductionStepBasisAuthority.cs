using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Binds the ordinary scheduler and durable-operation core substates into a QA-04 production
/// State(S). Partition authority is supplied by the caller and is preserved byte-for-byte; this
/// component does not invent or synthesize reference-world partition material.
/// </summary>
public static class Qa04ProductionStepBasisAuthorityV1
{
    public static WorldStateV1 BindCoreAuthority(
        WorldStateV1 partitionAuthorityState,
        OperationSchedulerStateV1 scheduler,
        IReadOnlyCollection<DurableOperationStateV1> durableOperations)
    {
        ArgumentNullException.ThrowIfNull(partitionAuthorityState);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(durableOperations);

        ValidateSchedulerAndDurableShape(partitionAuthorityState.Header.Step, scheduler, durableOperations);
        var operationState = DurableOperationSubstateV1.Canonicalize(durableOperations);
        var state = BindCoreAuthorityCore(partitionAuthorityState, scheduler, operationState);
        ValidateBoundCoreAuthority(state, scheduler, durableOperations);
        return state;
    }

    public static WorldStateV1 BindCoreAuthorityV2(
        WorldStateV1 partitionAuthorityState,
        OperationSchedulerStateV1 scheduler,
        IReadOnlyCollection<DurableOperationStateV1> durableOperations,
        IReadOnlyCollection<CrossDomainTransactionStateV1> crossDomainTransactions)
    {
        ArgumentNullException.ThrowIfNull(partitionAuthorityState);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(durableOperations);
        ArgumentNullException.ThrowIfNull(crossDomainTransactions);

        ValidateSchedulerAndDurableShape(partitionAuthorityState.Header.Step, scheduler, durableOperations);
        var operationState = CoreOperationStateSubstateV2.Canonicalize(
            durableOperations,
            crossDomainTransactions,
            partitionAuthorityState.Header.Step);
        var state = BindCoreAuthorityCore(partitionAuthorityState, scheduler, operationState);
        ValidateBoundCoreAuthorityV2(state, scheduler, durableOperations, crossDomainTransactions);
        return state;
    }

    public static void ValidateBoundCoreAuthority(
        WorldStateV1 state,
        OperationSchedulerStateV1 scheduler,
        IReadOnlyCollection<DurableOperationStateV1> durableOperations)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(durableOperations);

        ValidateSchedulerAndDurableShape(state.Header.Step, scheduler, durableOperations);
        var expectedScheduler = OperationSchedulerSubstateV1.Canonicalize(scheduler, state.Header.Step);
        var expectedOperations = DurableOperationSubstateV1.Canonicalize(durableOperations);
        ValidateBoundCoreAuthorityCore(state, expectedScheduler, expectedOperations);
    }

    public static void ValidateBoundCoreAuthorityV2(
        WorldStateV1 state,
        OperationSchedulerStateV1 scheduler,
        IReadOnlyCollection<DurableOperationStateV1> durableOperations,
        IReadOnlyCollection<CrossDomainTransactionStateV1> crossDomainTransactions)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(durableOperations);
        ArgumentNullException.ThrowIfNull(crossDomainTransactions);

        ValidateSchedulerAndDurableShape(state.Header.Step, scheduler, durableOperations);
        var expectedScheduler = OperationSchedulerSubstateV1.Canonicalize(scheduler, state.Header.Step);
        var expectedOperations = CoreOperationStateSubstateV2.Canonicalize(
            durableOperations,
            crossDomainTransactions,
            state.Header.Step);
        ValidateBoundCoreAuthorityCore(state, expectedScheduler, expectedOperations);
    }

    private static WorldStateV1 BindCoreAuthorityCore(
        WorldStateV1 partitionAuthorityState,
        OperationSchedulerStateV1 scheduler,
        WorldSubstateRefV1 operationState)
        => new(
            partitionAuthorityState.Header,
            partitionAuthorityState.Partitions,
            OperationSchedulerSubstateV1.Canonicalize(scheduler, partitionAuthorityState.Header.Step),
            operationState,
            partitionAuthorityState.DetailState,
            partitionAuthorityState.DomainRegistryState,
            partitionAuthorityState.Diagnostic.ConfigDigest);

    private static void ValidateBoundCoreAuthorityCore(
        WorldStateV1 state,
        WorldSubstateRefV1 expectedScheduler,
        WorldSubstateRefV1 expectedOperations)
    {
        if (state.SchedulerState.Schema != expectedScheduler.Schema ||
            !CryptographicOperations.FixedTimeEquals(
                state.SchedulerState.CanonicalDigest,
                expectedScheduler.CanonicalDigest))
            throw new InvalidDataException("qa04.production-step.scheduler-substate-mismatch");
        if (state.OperationState.Schema != expectedOperations.Schema ||
            !CryptographicOperations.FixedTimeEquals(
                state.OperationState.CanonicalDigest,
                expectedOperations.CanonicalDigest))
            throw new InvalidDataException("qa04.production-step.operation-substate-mismatch");
    }

    private static void ValidateSchedulerAndDurableShape(
        ulong basisStep,
        OperationSchedulerStateV1 scheduler,
        IReadOnlyCollection<DurableOperationStateV1> durableOperations)
    {
        if (scheduler.FreezeStep is not null)
            throw new InvalidDataException("qa04.production-step.scheduler-already-frozen");
        if (scheduler.NextSchedulableStep != basisStep)
            throw new InvalidDataException("qa04.production-step.scheduler-basis-mismatch");

        var scheduled = scheduler.ForEffectiveStep(basisStep);
        if (scheduled.Count != durableOperations.Count)
            throw new InvalidDataException("qa04.production-step.scheduler-durable-count-mismatch");

        var durableById = durableOperations.ToDictionary(static state => state.OperationId);
        if (durableById.Count != durableOperations.Count)
            throw new InvalidDataException("qa04.production-step.durable-operation-duplicate");

        foreach (var operation in scheduled)
        {
            if (!durableById.TryGetValue(operation.OperationId, out var durable))
                throw new InvalidDataException("qa04.production-step.scheduled-operation-not-durable");
            if (durable.Lifecycle != DurableOperationLifecycleV1.ScheduledDurable ||
                durable.EffectiveStep != basisStep)
                throw new InvalidDataException("qa04.production-step.durable-operation-not-scheduled-for-basis");
        }

        if (durableById.Values.Any(state =>
                state.Lifecycle != DurableOperationLifecycleV1.ScheduledDurable ||
                state.EffectiveStep != basisStep))
            throw new InvalidDataException("qa04.production-step.unexpected-durable-operation-state");
    }
}
