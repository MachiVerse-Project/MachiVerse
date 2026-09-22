using System.Diagnostics;
using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains;
using MachiVerse.Simulation.Core.Domains.Environment;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Domains.Participation;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04ProductionStep2FinalizationResultV1(
    Qa04CanonicalOperationStepPreparationResultV1 Preparation,
    StepFinalizeMaterialV1 FinalizeMaterial,
    DurableStepReceiptV1 DurableReceipt,
    AuthoritativeStepWorldStateV1 AuthoritativeState,
    Qa04OperationClosedPrefixV1 ClosedPrefix,
    byte[] ResultingContinuityToken,
    Qa04TransitionCommittedAuthorityV1 TransitionAuthority,
    DetailDirectoryV1? DetailDirectory,
    Qa04DetailDecisionAuthorityV1? DetailDecisionAuthority,
    Qa04CanonicalOperationPostCommitVerificationV1 PostCommitVerification);

public sealed record Qa04ProductionStep2AuthoritativeStepExecutionV1(
    ulong InjectionStep,
    ulong BasisStep,
    ulong ResultingStep,
    OpaqueId128 CandidateId,
    int OperationCount,
    DeterministicCpuBatchObservationV1 OperationBindingParallelism,
    DeterministicCpuBatchObservationV1 TypedMutationParallelism,
    DeterministicCpuBatchObservationV1 PreparationParallelism,
    Qa04CanonicalOperationMutationStateV1 MutationState,
    IReadOnlyList<IDomainPartitionSnapshotAuthorityV1> DomainAuthorities,
    Qa04OperationClosedPrefixV1 ClosedPrefix,
    Qa04ProductionStep2FinalizationResultV1 Finalization);

public static class Qa04ProductionStep2BasisAuthorityV1
{
    public static WorldStateV1 Bind(
        WorldStateV1 partitionAuthorityState,
        OperationSchedulerStateV1 scheduler,
        IReadOnlyCollection<DurableOperationStateV1> mutableOperations,
        Qa04OperationClosedPrefixV1 closedPrefix,
        IReadOnlyCollection<CrossDomainTransactionStateV1> activeTransactions)
    {
        ArgumentNullException.ThrowIfNull(partitionAuthorityState);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(mutableOperations);
        ArgumentNullException.ThrowIfNull(closedPrefix);
        ArgumentNullException.ThrowIfNull(activeTransactions);

        ValidateMutableShape(partitionAuthorityState.Header.Step, scheduler, mutableOperations);
        closedPrefix.Validate(partitionAuthorityState.Header.Step);

        var schedulerState = OperationSchedulerSubstateV1.Canonicalize(
            scheduler,
            partitionAuthorityState.Header.Step);
        var operationState = Qa04OperationAuthorityV1.Canonicalize(
            mutableOperations,
            closedPrefix,
            activeTransactions,
            partitionAuthorityState.Header.Step);
        var state = new WorldStateV1(
            partitionAuthorityState.Header,
            partitionAuthorityState.Partitions,
            schedulerState,
            operationState,
            partitionAuthorityState.DetailState,
            partitionAuthorityState.DomainRegistryState,
            partitionAuthorityState.Diagnostic.ConfigDigest);

        RequireSubstateMatch(
            schedulerState,
            state.SchedulerState,
            "qa04.step2.scheduler-substate-drift");
        RequireSubstateMatch(
            operationState,
            state.OperationState,
            "qa04.step2.operation-substate-drift");
        return state;
    }

    internal static WorldStateV1 BindValidatedCanonicalCurrentStep(
        WorldStateV1 partitionAuthorityState,
        OperationSchedulerStateV1 scheduler,
        IReadOnlyList<DurableOperationStateV1> mutableOperations,
        Qa04OperationClosedPrefixV1 closedPrefix,
        IReadOnlyCollection<CrossDomainTransactionStateV1> activeTransactions,
        Qa04ProductionStep2CanonicalDigestCacheV1? digestCache = null)
    {
        ArgumentNullException.ThrowIfNull(partitionAuthorityState);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(mutableOperations);
        ArgumentNullException.ThrowIfNull(closedPrefix);
        ArgumentNullException.ThrowIfNull(activeTransactions);

        ValidateCanonicalCurrentStepShape(
            partitionAuthorityState.Header.Step,
            scheduler,
            mutableOperations);
        closedPrefix.Validate(partitionAuthorityState.Header.Step);

        var schedulerState = OperationSchedulerSubstateV1.Canonicalize(
            scheduler,
            partitionAuthorityState.Header.Step);
        var operationState = digestCache is null
            ? Qa04OperationAuthorityV1.Canonicalize(
                mutableOperations,
                closedPrefix,
                activeTransactions,
                partitionAuthorityState.Header.Step)
            : Qa04OperationAuthorityV1.Canonicalize(
                mutableOperations,
                closedPrefix,
                activeTransactions,
                partitionAuthorityState.Header.Step,
                digestCache);
        var state = new WorldStateV1(
            partitionAuthorityState.Header,
            partitionAuthorityState.Partitions,
            schedulerState,
            operationState,
            partitionAuthorityState.DetailState,
            partitionAuthorityState.DomainRegistryState,
            partitionAuthorityState.Diagnostic.ConfigDigest);

        RequireSubstateMatch(
            schedulerState,
            state.SchedulerState,
            "qa04.step2.scheduler-substate-drift");
        RequireSubstateMatch(
            operationState,
            state.OperationState,
            "qa04.step2.operation-substate-drift");
        return state;
    }

    public static void Validate(
        WorldStateV1 state,
        OperationSchedulerStateV1 scheduler,
        IReadOnlyCollection<DurableOperationStateV1> mutableOperations,
        Qa04OperationClosedPrefixV1 closedPrefix,
        IReadOnlyCollection<CrossDomainTransactionStateV1> activeTransactions)
    {
        ValidateMutableShape(state.Header.Step, scheduler, mutableOperations);
        var expectedScheduler = OperationSchedulerSubstateV1.Canonicalize(scheduler, state.Header.Step);
        var expectedOperation = Qa04OperationAuthorityV1.Canonicalize(
            mutableOperations,
            closedPrefix,
            activeTransactions,
            state.Header.Step);
        RequireSubstateMatch(expectedScheduler, state.SchedulerState, "qa04.step2.scheduler-substate-drift");
        RequireSubstateMatch(expectedOperation, state.OperationState, "qa04.step2.operation-substate-drift");
    }

    private static void ValidateCanonicalCurrentStepShape(
        ulong basisStep,
        OperationSchedulerStateV1 scheduler,
        IReadOnlyList<DurableOperationStateV1> mutableOperations)
    {
        if (scheduler.FreezeStep is not null || scheduler.NextSchedulableStep != basisStep)
            throw new InvalidDataException("qa04.step2.scheduler-basis-drift");

        var scheduled = scheduler.ForEffectiveStep(basisStep);
        if (scheduled.Count != mutableOperations.Count)
            throw new InvalidDataException("qa04.step2.mutable-operation-count-drift");

        foreach (var bucket in scheduler.CanonicalBuckets)
        {
            if (bucket.Key != basisStep)
                throw new InvalidDataException("qa04.step2.scheduler-unexpected-bucket");
        }

        for (var index = 0; index < mutableOperations.Count; index++)
        {
            var durable = mutableOperations[index]
                ?? throw new InvalidDataException("qa04.step2.mutable-operation-null");
            var scheduledOperation = scheduled[index];
            if (durable.OperationId != scheduledOperation.OperationId ||
                durable.Lifecycle != DurableOperationLifecycleV1.ScheduledDurable ||
                durable.EffectiveStep != basisStep ||
                scheduledOperation.EffectiveStep != basisStep)
            {
                throw new InvalidDataException("qa04.step2.mutable-operation-not-scheduled");
            }
        }
    }

    private static void ValidateMutableShape(
        ulong basisStep,
        OperationSchedulerStateV1 scheduler,
        IReadOnlyCollection<DurableOperationStateV1> mutableOperations)
    {
        if (scheduler.FreezeStep is not null || scheduler.NextSchedulableStep != basisStep)
            throw new InvalidDataException("qa04.step2.scheduler-basis-drift");
        var mutableById = mutableOperations.ToDictionary(static value => value.OperationId);
        if (mutableById.Count != mutableOperations.Count)
            throw new InvalidDataException("qa04.step2.mutable-operation-duplicate");

        var scheduledById = new Dictionary<OpaqueId128, ScheduledOperationRefV1>();
        foreach (var bucket in scheduler.CanonicalBuckets)
        {
            if (bucket.Key < basisStep)
                throw new InvalidDataException("qa04.step2.scheduler-past-bucket-retained");
            foreach (var scheduled in bucket.Value)
            {
                if (!scheduledById.TryAdd(scheduled.OperationId, scheduled))
                    throw new InvalidDataException("qa04.step2.scheduler-operation-duplicate");
                if (!mutableById.TryGetValue(scheduled.OperationId, out var durable) ||
                    durable.Lifecycle != DurableOperationLifecycleV1.ScheduledDurable ||
                    durable.EffectiveStep != scheduled.EffectiveStep)
                    throw new InvalidDataException("qa04.step2.scheduler-operation-not-durable");
            }
        }

        foreach (var durable in mutableOperations)
        {
            if (durable.Lifecycle != DurableOperationLifecycleV1.ScheduledDurable ||
                durable.EffectiveStep is null || durable.EffectiveStep < basisStep ||
                !scheduledById.TryGetValue(durable.OperationId, out var scheduled) ||
                scheduled.EffectiveStep != durable.EffectiveStep)
                throw new InvalidDataException("qa04.step2.mutable-operation-not-scheduled");
        }
    }

    internal static void RequireSubstateMatch(WorldSubstateRefV1 expected, WorldSubstateRefV1 actual, string error)
    {
        if (expected.Schema != actual.Schema ||
            !CryptographicOperations.FixedTimeEquals(expected.CanonicalDigest, actual.CanonicalDigest))
            throw new InvalidDataException(error);
    }
}

public static class Qa04ProductionStep2AuthoritativeStepExecutorV1
{
    public static Task<Qa04ProductionStep2AuthoritativeStepExecutionV1> ExecuteAsync(
        ulong injectionStep,
        int workerCount,
        WorldStateV1 partitionAuthorityState,
        Qa04CanonicalOperationMutationStateV1 mutationState,
        IReadOnlyList<IDomainPartitionSnapshotAuthorityV1> domainAuthorities,
        IDomainRecordSchemaResolverV1 references,
        IReadOnlyCollection<CrossDomainTransactionStateV1> crossDomainTransactions,
        Qa04OperationClosedPrefixV1 closedPrefix,
        SqlitePersistenceStore store,
        OperationSchedulerStateV1 scheduler,
        Qa04ProductionStepCandidateIdentityRegistryV1 candidateIdentities,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(
            injectionStep,
            workerCount,
            partitionAuthorityState,
            mutationState,
            domainAuthorities,
            references,
            crossDomainTransactions,
            crossDomainTransactions,
            Array.Empty<CrossDomainTransactionStateV1>(),
            closedPrefix,
            store,
            scheduler,
            candidateIdentities,
            cancellationToken);

    public static async Task<Qa04ProductionStep2AuthoritativeStepExecutionV1> ExecuteAsync(
        ulong injectionStep,
        int workerCount,
        WorldStateV1 partitionAuthorityState,
        Qa04CanonicalOperationMutationStateV1 mutationState,
        IReadOnlyList<IDomainPartitionSnapshotAuthorityV1> domainAuthorities,
        IDomainRecordSchemaResolverV1 references,
        IReadOnlyCollection<CrossDomainTransactionStateV1> basisCrossDomainTransactions,
        IReadOnlyCollection<CrossDomainTransactionStateV1> resultingCrossDomainTransactions,
        IReadOnlyCollection<CrossDomainTransactionStateV1> crossDomainTransactionStateChanges,
        Qa04OperationClosedPrefixV1 closedPrefix,
        SqlitePersistenceStore store,
        OperationSchedulerStateV1 scheduler,
        Qa04ProductionStepCandidateIdentityRegistryV1 candidateIdentities,
        CancellationToken cancellationToken = default,
        DetailDirectoryV1? basisDetailDirectory = null,
        DetailTransitionPolicyV1? detailPolicy = null,
        int? persistenceInsertBatchSize = null,
        Qa04ProductionStep2CanonicalDigestCacheV1? digestCache = null,
        int phaseLogIntervalTransitions = 1,
        bool detailedPhaseDiagnostics = false)
    {
        if (!Qa04DomainExecutionTargetV1.CanonicalWorkerCounts.Contains(workerCount))
            throw new InvalidDataException("qa04.step2.production-loop.worker-count-not-canonical");
        ArgumentNullException.ThrowIfNull(partitionAuthorityState);
        ArgumentNullException.ThrowIfNull(mutationState);
        ArgumentNullException.ThrowIfNull(domainAuthorities);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(basisCrossDomainTransactions);
        ArgumentNullException.ThrowIfNull(resultingCrossDomainTransactions);
        ArgumentNullException.ThrowIfNull(crossDomainTransactionStateChanges);
        ArgumentNullException.ThrowIfNull(closedPrefix);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(candidateIdentities);
        if ((basisDetailDirectory is null) != (detailPolicy is null))
            throw new InvalidDataException("qa04.step2.production-loop.detail-input-pair-drift");
        if (phaseLogIntervalTransitions <= 0)
            throw new ArgumentOutOfRangeException(nameof(phaseLogIntervalTransitions));

        var preStepPhaseStarted = Stopwatch.GetTimestamp();
        var basisStep = checked(injectionStep + 1UL);
        var resultingStep = checked(basisStep + 1UL);
        if (partitionAuthorityState.Header.WorldId != Qa04ReferenceLoadV1.WorldId ||
            partitionAuthorityState.Header.Step != basisStep)
            throw new InvalidDataException("qa04.step2.production-loop.basis-state-drift");
        closedPrefix.Validate(basisStep);
        Qa04ProductionCrossDomainTurnoverContractV1.ValidateProductionStep(
            basisStep,
            resultingStep,
            basisCrossDomainTransactions,
            resultingCrossDomainTransactions,
            crossDomainTransactionStateChanges,
            digestCache);
        if (scheduler.FreezeStep is not null || scheduler.NextSchedulableStep != basisStep ||
            scheduler.CanonicalBuckets.Any())
            throw new InvalidDataException("qa04.step2.production-loop.scheduler-not-closed-at-basis");
        if (domainAuthorities.Count != StandardDomainPartitionRegistry.StandardPartitionCount)
            throw new InvalidDataException("qa04.step2.production-loop.domain-authority-count-not-97");

        var expectedClosedOperation = digestCache is null
            ? Qa04OperationAuthorityV1.Canonicalize(
                Array.Empty<DurableOperationStateV1>(),
                closedPrefix,
                basisCrossDomainTransactions,
                basisStep)
            : Qa04OperationAuthorityV1.Canonicalize(
                Array.Empty<DurableOperationStateV1>(),
                closedPrefix,
                basisCrossDomainTransactions,
                basisStep,
                digestCache);
        Qa04ProductionStep2BasisAuthorityV1.RequireSubstateMatch(
            expectedClosedOperation,
            partitionAuthorityState.OperationState,
            "qa04.step2.production-loop.closed-operation-authority-drift");

        var candidateIdentity = candidateIdentities.DeriveAndRegister(
            partitionAuthorityState.Header.WorldId,
            basisStep);
        if (candidateIdentity.TargetStep != resultingStep)
            throw new InvalidDataException("qa04.step2.production-loop.candidate-target-step-drift");
        if (detailedPhaseDiagnostics)
            EmitPhase(
                injectionStep,
                workerCount,
                phaseLogIntervalTransitions,
                "pre-step-authority",
                preStepPhaseStarted);

        var phaseStarted = Stopwatch.GetTimestamp();
        var descriptors = Qa04ReferenceLoadV1.OperationsForStep(injectionStep).ToArray();
        var expectedOperationCount = checked((int)Qa04ReferenceLoadV1.OperationCountForStep(injectionStep));
        if (descriptors.Length != expectedOperationCount)
            throw new InvalidDataException("qa04.step2.production-loop.operation-count-drift");

        var bindingBatch = await DeterministicBatchExecutor.RunCpuBoundAsync(
            descriptors,
            workerCount,
            (descriptor, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Qa04CanonicalOperationBindingV1.Bind(
                    descriptor,
                    partitionAuthorityState.Header.ConfigGeneration);
            },
            cancellationToken).ConfigureAwait(false);
        var bindingParallelism = bindingBatch.Observation;
        var expectedEffectiveWorkers = Math.Min(workerCount, descriptors.Length);
        if (bindingParallelism.RequestedWorkerCount != workerCount ||
            bindingParallelism.EffectiveWorkerCount != expectedEffectiveWorkers ||
            bindingParallelism.MaxObservedConcurrency < 1 ||
            bindingParallelism.MaxObservedConcurrency > expectedEffectiveWorkers)
        {
            throw new InvalidDataException("qa04.step2.production-loop.operation-binding-worker-budget-drift");
        }

        var bindings = bindingBatch.Outputs
            .OrderBy(static binding => binding.OrderKey)
            .ThenBy(static binding => binding.SourceDescriptor.OperationId)
            .ToArray();
        if (bindings.Length != expectedOperationCount)
            throw new InvalidDataException("qa04.step2.production-loop.operation-count-drift");
        EmitPhase(injectionStep, workerCount, phaseLogIntervalTransitions, "bind-operations", phaseStarted);
        phaseStarted = Stopwatch.GetTimestamp();

        var anchor = await store.ReadHistoryAnchorAsync(cancellationToken).ConfigureAwait(false);
        var batchAuthority = Qa04ScheduledOperationBatchAuthorityBuilderV1.Create(
            Qa04ReferenceLoadV1.WorldId,
            anchor,
            injectionStep,
            basisStep,
            bindings);
        var batch = await store.PersistQa04ValidatedScheduledOperationBatchAsync(
            batchAuthority,
            bindings,
            persistenceInsertBatchSize,
            cancellationToken).ConfigureAwait(false);
        if (batch.ScheduledOperations.Count != bindings.Length || batch.EffectiveStep != basisStep)
            throw new InvalidDataException("qa04.step2.production-loop.batch-durability-drift");
        foreach (var binding in bindings)
            scheduler.AddDurable(binding.ScheduledOperation);
        EmitPhase(injectionStep, workerCount, phaseLogIntervalTransitions, "persistence-schedule", phaseStarted);
        phaseStarted = Stopwatch.GetTimestamp();

        var freezeSubphaseStarted = Stopwatch.GetTimestamp();
        var basisState = Qa04ProductionStep2BasisAuthorityV1.BindValidatedCanonicalCurrentStep(
            partitionAuthorityState,
            scheduler,
            batch.ScheduledOperations,
            closedPrefix,
            basisCrossDomainTransactions,
            digestCache);
        if (detailedPhaseDiagnostics)
            EmitPhase(injectionStep, workerCount, phaseLogIntervalTransitions, "freeze-basis-bind", freezeSubphaseStarted);
        freezeSubphaseStarted = Stopwatch.GetTimestamp();

        var frozen = StepInputFreezerV1.Freeze(basisState, scheduler);
        if (frozen.ScheduledOperations.Count != bindings.Length)
            throw new InvalidDataException("qa04.step2.production-loop.frozen-operation-count-drift");
        if (detailedPhaseDiagnostics)
            EmitPhase(injectionStep, workerCount, phaseLogIntervalTransitions, "freeze-input", freezeSubphaseStarted);
        freezeSubphaseStarted = Stopwatch.GetTimestamp();

        Qa04ProductionDetailTransitionStepV1? detailTransition = null;
        if (basisDetailDirectory is not null)
        {
            detailTransition = Qa04ProductionDetailTransitionV1.Prepare(
                basisState,
                frozen,
                basisDetailDirectory,
                detailPolicy!);
        }
        if (detailedPhaseDiagnostics)
            EmitPhase(injectionStep, workerCount, phaseLogIntervalTransitions, "detail-prepare", freezeSubphaseStarted);
        EmitPhase(injectionStep, workerCount, phaseLogIntervalTransitions, "freeze-detail", phaseStarted);
        phaseStarted = Stopwatch.GetTimestamp();

        var runtimeOutputs = await DomainRuntimeExecutorV1.ExecuteAsync(
                StandardDomainExecutionPlanV1.Create(),
                basisState,
                frozen,
                CreateProductionRuntimes(bindings.Length),
                workerCount,
                cancellationToken)
            .ConfigureAwait(false);
        if (runtimeOutputs.Count != 8 || runtimeOutputs.Any(output => output.BasisStep != basisStep))
            throw new InvalidDataException("qa04.step2.production-loop.domain-runtime-output-drift");
        EmitPhase(injectionStep, workerCount, phaseLogIntervalTransitions, "domain-execution", phaseStarted);
        phaseStarted = Stopwatch.GetTimestamp();

        var mutation = await Qa04CanonicalOperationMutationBatchV1.ApplyParallelAsync(
            Qa04ReferenceLoadV1.WorldId,
            basisStep,
            bindings,
            mutationState,
            references,
            workerCount,
            cancellationToken).ConfigureAwait(false);
        if (mutation.AppliedOperationIds.Count != bindings.Length || mutation.Changes.Count != bindings.Length)
            throw new InvalidDataException("qa04.step2.production-loop.typed-mutation-coverage-drift");
        var mutationParallelism = mutation.CpuParallelism
            ?? throw new InvalidDataException("qa04.step2.production-loop.typed-mutation-parallelism-missing");
        var expectedMutationWorkers = Math.Min(workerCount, 6);
        if (mutationParallelism.RequestedWorkerCount != workerCount ||
            mutationParallelism.EffectiveWorkerCount != expectedMutationWorkers ||
            mutationParallelism.MaxObservedConcurrency < 1 ||
            mutationParallelism.MaxObservedConcurrency > expectedMutationWorkers)
            throw new InvalidDataException("qa04.step2.production-loop.typed-mutation-worker-budget-drift");
        EmitPhase(injectionStep, workerCount, phaseLogIntervalTransitions, "typed-mutation", phaseStarted);
        phaseStarted = Stopwatch.GetTimestamp();

        var preparation = await Qa04ProductionAuthoritativeStepPreparationV1.PrepareParallelWithExpectedDescriptorsAsync(
            candidateIdentity.CandidateId,
            basisState,
            frozen,
            bindings,
            mutation,
            references,
            runtimeOutputs,
            descriptors,
            workerCount,
            digestCache,
            cancellationToken).ConfigureAwait(false);
        var preparationParallelism = preparation.PartitionBatch?.CpuParallelism
            ?? throw new InvalidDataException("qa04.step2.production-loop.preparation-parallelism-missing");
        var expectedPreparationWorkers = Math.Min(workerCount, 6);
        if (preparationParallelism.RequestedWorkerCount != workerCount ||
            preparationParallelism.EffectiveWorkerCount != expectedPreparationWorkers ||
            preparationParallelism.MaxObservedConcurrency < 1 ||
            preparationParallelism.MaxObservedConcurrency > expectedPreparationWorkers)
            throw new InvalidDataException("qa04.step2.production-loop.preparation-worker-budget-drift");
        var terminals = bindings.Select(static binding => new TerminalOperationCommit(
            binding.SourceDescriptor.OperationId,
            (int)CoreOperationResultStatusV1.Success,
            "operation.succeeded")).ToArray();
        EmitPhase(injectionStep, workerCount, phaseLogIntervalTransitions, "step-preparation", phaseStarted);
        phaseStarted = Stopwatch.GetTimestamp();

        var finalized = await Qa04ProductionStep2OperationFinalizationV1.CommitAndPublishValidatedBasisAsync(
            store,
            scheduler,
            preparation,
            bindings,
            batch.ScheduledOperations,
            terminals,
            closedPrefix,
            basisCrossDomainTransactions,
            resultingCrossDomainTransactions,
            crossDomainTransactionStateChanges,
            cancellationToken,
            detailTransition,
            scheduledBatchDigest: batchAuthority.ScheduledBatchDigest,
            terminalOperationsAreCanonical: true,
            diagnosticPhaseObserver: detailedPhaseDiagnostics
                ? CreateDiagnosticPhaseObserver(
                    injectionStep,
                    workerCount,
                    phaseLogIntervalTransitions)
                : null,
            digestCache: digestCache).ConfigureAwait(false);
        var verification = finalized.PostCommitVerification;
        var nextPrefix = finalized.ClosedPrefix;
        if (verification.ResultingStep != resultingStep ||
            finalized.AuthoritativeState.State.Header.Step != resultingStep ||
            verification.TerminalOperationCount != bindings.Length ||
            scheduler.FreezeStep is not null || scheduler.NextSchedulableStep != resultingStep)
            throw new InvalidDataException("qa04.step2.production-loop.post-commit-authority-drift");
        if (detailTransition is not null &&
            (finalized.DetailDirectory is null || finalized.DetailDecisionAuthority is null))
            throw new InvalidDataException("qa04.step2.production-loop.detail-result-missing");
        EmitPhase(injectionStep, workerCount, phaseLogIntervalTransitions, "commit-finalization", phaseStarted);
        phaseStarted = Stopwatch.GetTimestamp();

        var resultingAuthorities = Qa04ProductionDomainSnapshotAuthorityBuilderV1.CreateResultingState(
            finalized.AuthoritativeState.State,
            domainAuthorities,
            mutation.State,
            preparation.PartitionBatch
                ?? throw new InvalidDataException("qa04.step2.production-loop.prepared-partition-batch-missing"));
        if (resultingAuthorities.CanonicalAuthorities.Count != StandardDomainPartitionRegistry.StandardPartitionCount)
            throw new InvalidDataException("qa04.step2.production-loop.resulting-domain-authority-count-not-97");
        EmitPhase(injectionStep, workerCount, phaseLogIntervalTransitions, "snapshot-authority", phaseStarted);

        return new Qa04ProductionStep2AuthoritativeStepExecutionV1(
            injectionStep,
            basisStep,
            resultingStep,
            candidateIdentity.CandidateId,
            bindings.Length,
            bindingParallelism,
            mutationParallelism,
            preparationParallelism,
            mutation.State,
            Array.AsReadOnly(resultingAuthorities.CanonicalAuthorities.ToArray()),
            nextPrefix,
            finalized);
    }

    private static void EmitPhase(
        ulong injectionStep,
        int workerCount,
        int phaseLogIntervalTransitions,
        string phase,
        long startedTimestamp)
    {
        if (injectionStep % checked((ulong)phaseLogIntervalTransitions) != 0)
            return;

        Console.Error.WriteLine(
            $"QA04_PHASE workers={workerCount} injection_step={injectionStep} phase={phase} " +
            $"elapsed_ms={Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds:F1}");
    }

    private static Action<string, double>? CreateDiagnosticPhaseObserver(
        ulong injectionStep,
        int workerCount,
        int phaseLogIntervalTransitions)
    {
        if (injectionStep % checked((ulong)phaseLogIntervalTransitions) != 0)
            return null;

        return (phase, elapsedMs) =>
            Console.Error.WriteLine(
                $"QA04_PHASE workers={workerCount} injection_step={injectionStep} phase={phase} elapsed_ms={elapsedMs:F1}");
    }

    private static IReadOnlyCollection<IDomainRuntimeV1> CreateProductionRuntimes(int expectedOperationCount)
    {
        ValueTask<IReadOnlyList<MutationIntentCandidateV1>> NoIntents(
            DomainRuntimeContextV1 context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (context.FrozenInput.ScheduledOperations.Count != expectedOperationCount)
                throw new InvalidDataException("qa04.step2.production-loop.runtime-workload-count-drift");
            return ValueTask.FromResult<IReadOnlyList<MutationIntentCandidateV1>>(Array.Empty<MutationIntentCandidateV1>());
        }

        static ValueTask<IReadOnlyList<PartitionCandidateV1>> NoResidentPartitionCandidates(
            DomainRuntimeContextV1 context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IReadOnlyList<PartitionCandidateV1>>(Array.Empty<PartitionCandidateV1>());
        }

        return
        [
            new SpatialDomainRuntimeV1(NoIntents),
            new EnvironmentDomainRuntimeV1(NoIntents),
            new PhysicalBuiltDomainRuntimeV1(NoIntents),
            new ParticipationDomainRuntimeV1(NoIntents),
            new ResidentDomainRuntimeV1(NoIntents, NoResidentPartitionCandidates),
            new SocietyEconomyDomainRuntimeV1(NoIntents),
            new GovernanceSecurityDomainRuntimeV1(NoIntents),
            new InfrastructureInformationDomainRuntimeV1(NoIntents),
        ];
    }
}

public static class Qa04ProductionStep2OperationFinalizationV1
{
    public static Task<Qa04ProductionStep2FinalizationResultV1> CommitAndPublishAsync(
        SqlitePersistenceStore store,
        OperationSchedulerStateV1 scheduler,
        Qa04CanonicalOperationStepPreparationResultV1 preparation,
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> bindings,
        IReadOnlyCollection<DurableOperationStateV1> mutableOperations,
        IReadOnlyCollection<TerminalOperationCommit> terminalOperations,
        Qa04OperationClosedPrefixV1 basisPrefix,
        IReadOnlyCollection<CrossDomainTransactionStateV1> activeTransactions,
        CancellationToken cancellationToken = default)
        => CommitAndPublishAsync(
            store,
            scheduler,
            preparation,
            bindings,
            mutableOperations,
            terminalOperations,
            basisPrefix,
            activeTransactions,
            activeTransactions,
            Array.Empty<CrossDomainTransactionStateV1>(),
            cancellationToken);

    public static Task<Qa04ProductionStep2FinalizationResultV1> CommitAndPublishAsync(
        SqlitePersistenceStore store,
        OperationSchedulerStateV1 scheduler,
        Qa04CanonicalOperationStepPreparationResultV1 preparation,
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> bindings,
        IReadOnlyCollection<DurableOperationStateV1> mutableOperations,
        IReadOnlyCollection<TerminalOperationCommit> terminalOperations,
        Qa04OperationClosedPrefixV1 basisPrefix,
        IReadOnlyCollection<CrossDomainTransactionStateV1> basisActiveTransactions,
        IReadOnlyCollection<CrossDomainTransactionStateV1> resultingActiveTransactions,
        IReadOnlyCollection<CrossDomainTransactionStateV1> crossDomainTransactionStateChanges,
        CancellationToken cancellationToken = default,
        Qa04ProductionDetailTransitionStepV1? detailTransition = null,
        byte[]? scheduledBatchDigest = null,
        bool terminalOperationsAreCanonical = false,
        Action<string, double>? diagnosticPhaseObserver = null)
        => CommitAndPublishCoreAsync(
            store,
            scheduler,
            preparation,
            bindings,
            mutableOperations,
            terminalOperations,
            basisPrefix,
            basisActiveTransactions,
            resultingActiveTransactions,
            crossDomainTransactionStateChanges,
            cancellationToken,
            detailTransition,
            scheduledBatchDigest,
            terminalOperationsAreCanonical,
            diagnosticPhaseObserver,
            digestCache: null,
            basisOperationAuthorityAlreadyValidated: false);

    internal static Task<Qa04ProductionStep2FinalizationResultV1> CommitAndPublishValidatedBasisAsync(
        SqlitePersistenceStore store,
        OperationSchedulerStateV1 scheduler,
        Qa04CanonicalOperationStepPreparationResultV1 preparation,
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> bindings,
        IReadOnlyCollection<DurableOperationStateV1> mutableOperations,
        IReadOnlyCollection<TerminalOperationCommit> terminalOperations,
        Qa04OperationClosedPrefixV1 basisPrefix,
        IReadOnlyCollection<CrossDomainTransactionStateV1> basisActiveTransactions,
        IReadOnlyCollection<CrossDomainTransactionStateV1> resultingActiveTransactions,
        IReadOnlyCollection<CrossDomainTransactionStateV1> crossDomainTransactionStateChanges,
        CancellationToken cancellationToken = default,
        Qa04ProductionDetailTransitionStepV1? detailTransition = null,
        byte[]? scheduledBatchDigest = null,
        bool terminalOperationsAreCanonical = false,
        Action<string, double>? diagnosticPhaseObserver = null,
        Qa04ProductionStep2CanonicalDigestCacheV1? digestCache = null)
        => CommitAndPublishCoreAsync(
            store,
            scheduler,
            preparation,
            bindings,
            mutableOperations,
            terminalOperations,
            basisPrefix,
            basisActiveTransactions,
            resultingActiveTransactions,
            crossDomainTransactionStateChanges,
            cancellationToken,
            detailTransition,
            scheduledBatchDigest,
            terminalOperationsAreCanonical,
            diagnosticPhaseObserver,
            digestCache,
            basisOperationAuthorityAlreadyValidated: true);

    private static async Task<Qa04ProductionStep2FinalizationResultV1> CommitAndPublishCoreAsync(
        SqlitePersistenceStore store,
        OperationSchedulerStateV1 scheduler,
        Qa04CanonicalOperationStepPreparationResultV1 preparation,
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> bindings,
        IReadOnlyCollection<DurableOperationStateV1> mutableOperations,
        IReadOnlyCollection<TerminalOperationCommit> terminalOperations,
        Qa04OperationClosedPrefixV1 basisPrefix,
        IReadOnlyCollection<CrossDomainTransactionStateV1> basisActiveTransactions,
        IReadOnlyCollection<CrossDomainTransactionStateV1> resultingActiveTransactions,
        IReadOnlyCollection<CrossDomainTransactionStateV1> crossDomainTransactionStateChanges,
        CancellationToken cancellationToken,
        Qa04ProductionDetailTransitionStepV1? detailTransition,
        byte[]? scheduledBatchDigest,
        bool terminalOperationsAreCanonical,
        Action<string, double>? diagnosticPhaseObserver,
        Qa04ProductionStep2CanonicalDigestCacheV1? digestCache,
        bool basisOperationAuthorityAlreadyValidated)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(mutableOperations);
        ArgumentNullException.ThrowIfNull(terminalOperations);
        ArgumentNullException.ThrowIfNull(basisPrefix);
        ArgumentNullException.ThrowIfNull(basisActiveTransactions);
        ArgumentNullException.ThrowIfNull(resultingActiveTransactions);
        ArgumentNullException.ThrowIfNull(crossDomainTransactionStateChanges);
        if (scheduledBatchDigest is not null && scheduledBatchDigest.Length != 32)
            throw new InvalidDataException("qa04.step2.finalization.scheduled-batch-digest-invalid");

        var diagnosticPhaseStarted = Stopwatch.GetTimestamp();
        var step5Candidate = preparation.Candidate;
        var step5Prepared = preparation.PreparedState;
        var basisState = preparation.BasisState
            ?? throw new InvalidDataException("qa04.step2.finalization-basis-state-missing");
        if (step5Candidate.WorldId != Qa04ReferenceLoadV1.WorldId ||
            !step5Candidate.CommitDecision.CanCommit || step5Candidate.IsPublishable || step5Prepared.IsPublishable)
            throw new InvalidDataException("qa04.step2.finalization-candidate-invalid");
        if (step5Candidate.CoreSubstateCandidates.Count != 0 || step5Candidate.TransactionCandidates.Count != 0)
            throw new InvalidDataException("qa04.step2.finalization-step5-authority-surface-drift");
        if (detailTransition is not null &&
            (detailTransition.Plan.BasisStep != step5Candidate.BasisStep ||
             detailTransition.Projection.Candidate.Kind != StepCoreSubstateKindV1.Detail ||
             detailTransition.Projection.Candidate.BasisStep != step5Candidate.BasisStep ||
             detailTransition.Projection.Candidate.TargetStep != step5Candidate.TargetStep))
            throw new InvalidDataException("qa04.step2.finalization-detail-transition-drift");

        basisPrefix.Validate(step5Candidate.BasisStep);
        Qa04ProductionCrossDomainTurnoverContractV1.ValidateProductionStep(
            step5Candidate.BasisStep,
            step5Candidate.TargetStep,
            basisActiveTransactions,
            resultingActiveTransactions,
            crossDomainTransactionStateChanges,
            digestCache);
        var alignedTerminal = AlignTerminalCoverage(
            bindings,
            terminalOperations,
            terminalOperationsAreCanonical);
        if (mutableOperations.Count != bindings.Count)
            throw new InvalidDataException("qa04.step2.finalization-mutable-operation-count-drift");
        ObserveDiagnosticPhase(diagnosticPhaseObserver, "finalize-preflight", ref diagnosticPhaseStarted);

        var before = await store.ReadRecoveryHeadAsync(cancellationToken).ConfigureAwait(false);
        if (before.FinalizedStep != step5Candidate.BasisStep ||
            before.ConfigGeneration != step5Candidate.ConfigGeneration ||
            !CryptographicOperations.FixedTimeEquals(before.ConfigDigest, step5Candidate.ConfigDigest))
            throw new InvalidDataException("qa04.step2.finalization-persistence-basis-drift");
        ObserveDiagnosticPhase(diagnosticPhaseObserver, "finalize-recovery-head", ref diagnosticPhaseStarted);

        if (basisOperationAuthorityAlreadyValidated)
        {
            if (basisState.OperationState.Schema != Qa04OperationAuthorityV1.Schema)
                throw new InvalidDataException("qa04.step2.finalization-operation-basis-drift");
        }
        else
        {
            var expectedBasisOperation = Qa04OperationAuthorityV1.Canonicalize(
                mutableOperations,
                basisPrefix,
                basisActiveTransactions,
                step5Candidate.BasisStep);
            Qa04ProductionStep2BasisAuthorityV1.RequireSubstateMatch(
                expectedBasisOperation,
                basisState.OperationState,
                "qa04.step2.finalization-operation-basis-drift");
        }

        if (crossDomainTransactionStateChanges.Count > 0)
        {
            await Qa04CrossDomainDurableAuthorityVerifierV1.RequireActiveAuthorityAsync(
                    store,
                    basisActiveTransactions,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        ObserveDiagnosticPhase(diagnosticPhaseObserver, "finalize-basis-operation-authority", ref diagnosticPhaseStarted);

        var terminalBatchDigest = terminalOperationsAreCanonical
            ? Qa04TerminalSemanticAuthorityV1.ComputeFinalizationBatchDigest(bindings, alignedTerminal)
            : Qa04TerminalSemanticAuthorityV1.ComputeBatchDigest(bindings, alignedTerminal);
        var terminalStepDigest = Qa04TerminalSemanticAuthorityV1.ComputeStepItemDigest(
            step5Candidate.BasisStep,
            checked((ulong)alignedTerminal.Count),
            terminalBatchDigest);
        var terminalSemanticDigest = HashSuite.DomainHash("mv.qa04-operation-terminal.v1.append", writer =>
        {
            writer.WriteArrayStart(3);
            writer.WriteBytes(basisPrefix.TerminalSemanticDigest);
            writer.WriteUnsigned(step5Candidate.BasisStep);
            writer.WriteBytes(terminalStepDigest);
        });
        var injectionStep = checked(step5Candidate.BasisStep - 1UL);
        var resultingPrefix = basisPrefix.Advance(
            injectionStep,
            checked((ulong)alignedTerminal.Count),
            terminalSemanticDigest,
            step5Candidate.TargetStep);
        ObserveDiagnosticPhase(diagnosticPhaseObserver, "finalize-terminal-digest", ref diagnosticPhaseStarted);

        var schedulerCore = OperationSchedulerSubstateV1.CreatePostFinalizationCandidate(
            basisState,
            scheduler,
            step5Candidate.FrozenInput);
        var resultingOperation = digestCache is null
            ? Qa04OperationAuthorityV1.Canonicalize(
                Array.Empty<DurableOperationStateV1>(),
                resultingPrefix,
                resultingActiveTransactions,
                step5Candidate.TargetStep)
            : Qa04OperationAuthorityV1.Canonicalize(
                Array.Empty<DurableOperationStateV1>(),
                resultingPrefix,
                resultingActiveTransactions,
                step5Candidate.TargetStep,
                digestCache);
        var operationCore = new StepCoreSubstateCandidateV1(
            StepCoreSubstateKindV1.Operation,
            step5Candidate.BasisStep,
            basisState.OperationState,
            resultingOperation);
        var coreCandidates = detailTransition is null
            ? new[] { schedulerCore, operationCore }
            : new[] { schedulerCore, operationCore, detailTransition.Projection.Candidate };
        coreCandidates = coreCandidates
            .OrderBy(static value => value.Kind)
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
        var expectedCoreCount = detailTransition is null ? 2 : 3;
        if (!candidate.CommitDecision.CanCommit || candidate.CoreSubstateCandidates.Count != expectedCoreCount)
            throw new InvalidDataException("qa04.step2.finalization-core-candidate-drift");
        ObserveDiagnosticPhase(diagnosticPhaseObserver, "finalize-core-candidate", ref diagnosticPhaseStarted);

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
        if (prepared.IsPublishable)
            throw new InvalidDataException("qa04.step2.finalization-premature-publishable-state");
        ObserveDiagnosticPhase(diagnosticPhaseObserver, "finalize-state-prepare", ref diagnosticPhaseStarted);

        var anchor = await store.ReadHistoryAnchorAsync(cancellationToken).ConfigureAwait(false);
        var requiredHistoryRecords = detailTransition is null ? 1UL : 2UL;
        if (anchor.Sequence > ulong.MaxValue - requiredHistoryRecords)
            throw new InvalidDataException("qa04.step2.finalization-history-sequence-overflow");

        Qa04DetailDecisionAuthorityV1? detailDecisionAuthority = null;
        var transitionSequence = checked(anchor.Sequence + 1UL);
        var transitionPreviousDigest = anchor.Digest;
        if (detailTransition is not null)
        {
            detailDecisionAuthority = Qa04DetailDecisionAuthorityBuilderV1.Create(
                candidate.WorldId,
                anchor,
                candidate.BasisStep,
                detailTransition.Plan);
            transitionSequence = checked(detailDecisionAuthority.History.Sequence + 1UL);
            transitionPreviousDigest = detailDecisionAuthority.History.RecordDigest;
        }

        var partitionDigests = candidate.PartitionCandidates
            .Select(partition => new Qa04TransitionPartitionDigestV1(
                partition.PartitionId.Value,
                prepared.ResultingState.Partitions.Get(partition.PartitionId.Value).Header.CanonicalDigest.ToArray()))
            .ToArray();
        var transitionAuthority = Qa04TransitionCommittedAuthorityV1.CreateFromValidatedCanonicalOutcomes(
            candidate.WorldId,
            transitionSequence,
            transitionPreviousDigest,
            candidate.BasisStep,
            candidate.TargetStep,
            candidate.ConfigGeneration,
            candidate.ConfigDigest,
            alignedTerminal,
            before.ContinuityToken,
            prepared.ResultingState.Diagnostic.StateDigest,
            partitionDigests);
        var transition = transitionAuthority.History;
        var resultingContinuity = transitionAuthority.ResultingStateContinuityToken;
        var material = StepFinalizeMaterialV1.CreateFromValidatedCanonicalTerminalOrder(
            candidate.ConfigGeneration,
            candidate.ConfigDigest,
            resultingContinuity,
            transition,
            transitionAuthority.OperationOutcomes);
        var effectiveScheduledBatchDigest = scheduledBatchDigest ??
            Qa04ScheduledOperationBatchAuthorityBuilderV1.ComputeScheduledBatchDigest(bindings);
        var durability = new Qa04ProductionStep2TransitionDurabilityV1(
            store,
            injectionStep,
            basisPrefix,
            resultingPrefix,
            transitionAuthority,
            crossDomainTransactionStateChanges,
            detailDecisionAuthority,
            effectiveScheduledBatchDigest,
            diagnosticPhaseObserver);
        ObserveDiagnosticPhase(diagnosticPhaseObserver, "finalize-transition-authority", ref diagnosticPhaseStarted);

        var receipt = await new StepFinalizationCoordinatorV1(durability)
            .FinalizeAsync(candidate, scheduler, material, cancellationToken)
            .ConfigureAwait(false);
        ObserveDiagnosticPhase(diagnosticPhaseObserver, "finalize-durable-commit", ref diagnosticPhaseStarted);

        var after = await store.ReadRecoveryHeadAsync(cancellationToken).ConfigureAwait(false);
        if (after.FinalizedStep != candidate.TargetStep ||
            !CryptographicOperations.FixedTimeEquals(after.ContinuityToken, resultingContinuity))
            throw new InvalidDataException("qa04.step2.finalization-recovery-head-drift");
        var durablePrefix = await store.ReadQa04OperationClosedPrefixAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("qa04.step2.finalization-durable-prefix-missing");
        RequirePrefixMatch(durablePrefix, resultingPrefix);
        if (scheduler.FreezeStep is not null || scheduler.NextSchedulableStep != candidate.TargetStep ||
            scheduler.ForEffectiveStep(candidate.BasisStep).Count != 0)
            throw new InvalidDataException("qa04.step2.finalization-scheduler-post-commit-drift");

        if (crossDomainTransactionStateChanges.Count > 0)
        {
            await Qa04CrossDomainDurableAuthorityVerifierV1.RequireActiveAuthorityAsync(
                    store,
                    resultingActiveTransactions,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        ObserveDiagnosticPhase(diagnosticPhaseObserver, "finalize-postcommit-read", ref diagnosticPhaseStarted);

        var authoritative = StepStateApplicationV1.Publish(prepared, receipt);
        if (!authoritative.IsPublishable || authoritative.State.Header.Step != candidate.TargetStep)
            throw new InvalidDataException("qa04.step2.finalization-published-state-drift");
        var resultingDetailDirectory = detailTransition?.Projection.ResultingDirectory;
        var verification = VerifyCompactPostCommit(
            preparation,
            prepared,
            receipt,
            authoritative.State,
            scheduler,
            resultingPrefix,
            resultingActiveTransactions,
            resultingDetailDirectory);
        ObserveDiagnosticPhase(diagnosticPhaseObserver, "finalize-postcommit-verify", ref diagnosticPhaseStarted);

        return new Qa04ProductionStep2FinalizationResultV1(
            preparation,
            material,
            receipt,
            authoritative,
            resultingPrefix,
            resultingContinuity.ToArray(),
            transitionAuthority,
            resultingDetailDirectory,
            detailDecisionAuthority,
            verification);
    }

    private static void ObserveDiagnosticPhase(
        Action<string, double>? observer,
        string phase,
        ref long startedTimestamp)
    {
        if (observer is null)
            return;

        var now = Stopwatch.GetTimestamp();
        observer(phase, Stopwatch.GetElapsedTime(startedTimestamp, now).TotalMilliseconds);
        startedTimestamp = now;
    }

    private static IReadOnlyList<TerminalOperationCommit> AlignTerminalCoverage(
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> bindings,
        IReadOnlyCollection<TerminalOperationCommit> terminalOperations,
        bool terminalOperationsAreCanonical)
    {
        if (terminalOperations.Count != bindings.Count)
            throw new InvalidDataException("qa04.step2.finalization-terminal-coverage-count-drift");

        if (terminalOperationsAreCanonical &&
            terminalOperations is IReadOnlyList<TerminalOperationCommit> ordered)
            return ordered;

        var byId = terminalOperations.ToDictionary(static value => value.OperationId);
        if (byId.Count != terminalOperations.Count)
            throw new InvalidDataException("qa04.step2.finalization-terminal-coverage-count-drift");

        var aligned = new TerminalOperationCommit[bindings.Count];
        for (var index = 0; index < bindings.Count; index++)
        {
            var operationId = bindings[index].SourceDescriptor.OperationId;
            if (!byId.TryGetValue(operationId, out var terminal))
                throw new InvalidDataException("qa04.step2.finalization-terminal-coverage-drift");
            ValidateTerminal(terminal);
            aligned[index] = terminal;
        }
        return Array.AsReadOnly(aligned);
    }

    private static void ValidateTerminal(TerminalOperationCommit terminal)
    {
        if (!Enum.IsDefined(typeof(CoreOperationResultStatusV1), terminal.TerminalStatus) ||
            !OperationLifecycleRulesV1.IsTerminalResult((CoreOperationResultStatusV1)terminal.TerminalStatus))
            throw new InvalidDataException("qa04.step2.finalization-terminal-status-invalid");
        _ = new StableToken(terminal.ResultCode);
    }

    private static Qa04CanonicalOperationPostCommitVerificationV1 VerifyCompactPostCommit(
        Qa04CanonicalOperationStepPreparationResultV1 step5Preparation,
        PreparedStepWorldStateV1 finalPrepared,
        DurableStepReceiptV1 receipt,
        WorldStateV1 publishedState,
        OperationSchedulerStateV1 scheduler,
        Qa04OperationClosedPrefixV1 closedPrefix,
        IReadOnlyCollection<CrossDomainTransactionStateV1> activeTransactions,
        DetailDirectoryV1? detailDirectory)
    {
        var step5Candidate = step5Preparation.Candidate;
        if (!receipt.IsPublishable || receipt.ResultingStep != publishedState.Header.Step ||
            receipt.BasisStep != finalPrepared.BasisStep || receipt.ResultingStep != finalPrepared.TargetStep)
            throw new InvalidDataException("qa04.step2.post-commit-step-authority-drift");
        if (!CryptographicOperations.FixedTimeEquals(
                finalPrepared.ResultingState.Diagnostic.StateDigest,
                publishedState.Diagnostic.StateDigest))
            throw new InvalidDataException("qa04.step2.post-commit-state-digest-drift");

        var partitions = publishedState.Partitions.CanonicalEntries.ToArray();
        if (partitions.Length != StandardDomainPartitionRegistry.StandardPartitionCount ||
            step5Candidate.PartitionCandidates.Count != 6)
            throw new InvalidDataException("qa04.step2.post-commit-partition-count-drift");
        var diagnosticByPartition = publishedState.Diagnostic.PartitionDigests
            .ToDictionary(static item => item.Key, static item => item.Value, StringComparer.Ordinal);
        foreach (var partition in partitions)
        {
            if (!diagnosticByPartition.TryGetValue(partition.Header.PartitionId.Value, out var digest) ||
                !CryptographicOperations.FixedTimeEquals(digest, partition.Header.CanonicalDigest))
                throw new InvalidDataException($"qa04.step2.post-commit-partition-digest-drift:{partition.Header.PartitionId.Value}");
        }
        foreach (var changed in step5Candidate.PartitionCandidates)
        {
            var typed = step5Preparation.PreparedState.ResultingState.Partitions.Get(changed.PartitionId.Value).Header;
            var published = publishedState.Partitions.Get(changed.PartitionId.Value).Header;
            if (typed.PartitionId != published.PartitionId || typed.Revision != published.Revision ||
                typed.BasisStep != published.BasisStep || typed.ItemCount != published.ItemCount ||
                !CryptographicOperations.FixedTimeEquals(typed.CanonicalDigest, published.CanonicalDigest))
                throw new InvalidDataException($"qa04.step2.post-commit-typed-partition-drift:{changed.PartitionId.Value}");
        }

        var schedulerAuthority = OperationSchedulerSubstateV1.Canonicalize(scheduler, publishedState.Header.Step);
        Qa04ProductionStep2BasisAuthorityV1.RequireSubstateMatch(
            schedulerAuthority,
            publishedState.SchedulerState,
            "qa04.step2.post-commit-scheduler-substate-drift");
        var operationAuthority = Qa04OperationAuthorityV1.Canonicalize(
            Array.Empty<DurableOperationStateV1>(),
            closedPrefix,
            activeTransactions,
            publishedState.Header.Step);
        Qa04ProductionStep2BasisAuthorityV1.RequireSubstateMatch(
            operationAuthority,
            publishedState.OperationState,
            "qa04.step2.post-commit-operation-substate-drift");
        if (detailDirectory is not null)
        {
            var detailAuthority = DetailDirectorySubstateV1.Canonicalize(detailDirectory);
            Qa04ProductionStep2BasisAuthorityV1.RequireSubstateMatch(
                detailAuthority,
                publishedState.DetailState,
                "qa04.step2.post-commit-detail-substate-drift");
        }

        var reconstructed = new WorldStateV1(
            new WorldStateHeaderV1(
                publishedState.Header.WorldId,
                publishedState.Header.Step,
                publishedState.Header.WorldSeedDigest,
                publishedState.Header.ConfigGeneration,
                publishedState.Header.MasterGeneration,
                publishedState.Header.RateGeneration,
                publishedState.Header.PreviousStateDigest),
            new OrderedPartitionDirectoryV1(partitions),
            CopySubstate(publishedState.SchedulerState),
            CopySubstate(publishedState.OperationState),
            CopySubstate(publishedState.DetailState),
            CopySubstate(publishedState.DomainRegistryState),
            publishedState.Diagnostic.ConfigDigest);
        if (!CryptographicOperations.FixedTimeEquals(
                reconstructed.Diagnostic.StateDigest,
                publishedState.Diagnostic.StateDigest))
            throw new InvalidDataException("qa04.step2.post-commit-semantic-rehash-drift");

        return new Qa04CanonicalOperationPostCommitVerificationV1(
            publishedState.Header.Step,
            partitions.Length,
            step5Candidate.PartitionCandidates.Count,
            step5Candidate.FrozenInput.ScheduledOperations.Count,
            publishedState.Diagnostic.StateDigest.ToArray());
    }

    private static void RequirePartitionCandidateStability(StepCandidateV1 expected, StepCandidateV1 actual)
    {
        if (expected.PartitionCandidates.Count != actual.PartitionCandidates.Count)
            throw new InvalidDataException("qa04.step2.finalization-partition-candidate-count-drift");
        var expectedById = expected.PartitionCandidates.ToDictionary(static value => value.PartitionId);
        foreach (var candidate in actual.PartitionCandidates)
        {
            if (!expectedById.TryGetValue(candidate.PartitionId, out var original) ||
                candidate.OwnerDomain != original.OwnerDomain ||
                candidate.BasisRevision != original.BasisRevision ||
                candidate.CandidateRevision != original.CandidateRevision ||
                candidate.BasisStep != original.BasisStep || candidate.TargetStep != original.TargetStep ||
                !CryptographicOperations.FixedTimeEquals(candidate.ChangeSetDigest, original.ChangeSetDigest) ||
                !CryptographicOperations.FixedTimeEquals(candidate.CandidateDigest, original.CandidateDigest))
                throw new InvalidDataException($"qa04.step2.finalization-partition-candidate-drift:{candidate.PartitionId.Value}");
        }
    }

    private static WorldSubstateRefV1 CopySubstate(WorldSubstateRefV1 value)
        => new(value.Schema, value.CanonicalDigest.ToArray());

    private static void RequirePrefixMatch(Qa04OperationClosedPrefixV1 actual, Qa04OperationClosedPrefixV1 expected)
    {
        if (!string.Equals(actual.ProfileId, expected.ProfileId, StringComparison.Ordinal) ||
            actual.FirstInjectionStep != expected.FirstInjectionStep ||
            actual.LastClosedInjectionStep != expected.LastClosedInjectionStep ||
            actual.TerminalOperationCount != expected.TerminalOperationCount ||
            !CryptographicOperations.FixedTimeEquals(actual.TerminalSemanticDigest, expected.TerminalSemanticDigest))
            throw new InvalidDataException("qa04.step2.finalization-durable-prefix-drift");
    }

    private sealed class Qa04ProductionStep2TransitionDurabilityV1(
        SqlitePersistenceStore store,
        ulong injectionStep,
        Qa04OperationClosedPrefixV1 basisPrefix,
        Qa04OperationClosedPrefixV1 resultingPrefix,
        Qa04TransitionCommittedAuthorityV1 transitionAuthority,
        IReadOnlyCollection<CrossDomainTransactionStateV1> crossDomainTransactionStateChanges,
        Qa04DetailDecisionAuthorityV1? detailDecisionAuthority,
        byte[] scheduledBatchDigest,
        Action<string, double>? diagnosticPhaseObserver) : IStepTransitionDurabilityV1
    {
        public Task<DurableTransitionResult> CommitAsync(
            StepCandidateV1 candidate,
            StepFinalizeMaterialV1 material,
            CancellationToken cancellationToken = default)
        {
            if (candidate.BasisStep != transitionAuthority.EffectiveStep ||
                candidate.TargetStep != transitionAuthority.ResultingStep ||
                material.ActiveConfigGeneration != transitionAuthority.ActiveConfigGeneration ||
                !CryptographicOperations.FixedTimeEquals(material.ActiveConfigDigest, transitionAuthority.ActiveConfigDigest) ||
                !ReferenceEquals(material.TransitionHistory, transitionAuthority.History) ||
                !ReferenceEquals(material.TerminalOperations, transitionAuthority.OperationOutcomes) ||
                !CryptographicOperations.FixedTimeEquals(material.ResultingStateContinuityToken, transitionAuthority.ResultingStateContinuityToken) ||
                !material.TerminalOperationsCanonicalToFrozenInput)
                throw new InvalidDataException("qa04.step2.finalization.transition-authority-material-drift");

            return store.PersistQa04ValidatedCanonicalTransitionCommitAsync(
                injectionStep,
                transitionAuthority,
                material.TerminalOperations,
                basisPrefix,
                resultingPrefix,
                crossDomainTransactionStateChanges,
                cancellationToken,
                detailDecisionAuthority,
                scheduledBatchDigest,
                diagnosticPhaseObserver);
        }
    }
}
