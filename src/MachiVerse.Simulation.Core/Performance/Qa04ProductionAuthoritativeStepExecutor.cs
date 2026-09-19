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

public sealed record Qa04ProductionAuthoritativeStepExecutionV1(
    ulong InjectionStep,
    ulong BasisStep,
    ulong ResultingStep,
    OpaqueId128 CandidateId,
    int OperationCount,
    Qa04CanonicalOperationMutationStateV1 MutationState,
    IReadOnlyList<IDomainPartitionSnapshotAuthorityV1> DomainAuthorities,
    Qa04CanonicalOperationStepFinalizationResultV1 Finalization);

/// <summary>
/// Executes exactly one ordinary perf.reference.v1 production workload transition while preserving
/// the complete authoritative State / SQLite Operation catalog / scheduler / typed mutation material
/// from the previous transition. CandidateId is always derived through the canonical QA-04 identity
/// contract and registered in the caller-owned run-local collision guard.
/// </summary>
public static class Qa04ProductionAuthoritativeStepExecutorV1
{
    public static Task<Qa04ProductionAuthoritativeStepExecutionV1> ExecuteAsync(
        ulong injectionStep,
        int workerCount,
        WorldStateV1 partitionAuthorityState,
        Qa04CanonicalOperationMutationStateV1 mutationState,
        IReadOnlyList<IDomainPartitionSnapshotAuthorityV1> domainAuthorities,
        IDomainRecordSchemaResolverV1 references,
        IReadOnlyCollection<CrossDomainTransactionStateV1> crossDomainTransactions,
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
            store,
            scheduler,
            candidateIdentities,
            cancellationToken);

    public static async Task<Qa04ProductionAuthoritativeStepExecutionV1> ExecuteAsync(
        ulong injectionStep,
        int workerCount,
        WorldStateV1 partitionAuthorityState,
        Qa04CanonicalOperationMutationStateV1 mutationState,
        IReadOnlyList<IDomainPartitionSnapshotAuthorityV1> domainAuthorities,
        IDomainRecordSchemaResolverV1 references,
        IReadOnlyCollection<CrossDomainTransactionStateV1> basisCrossDomainTransactions,
        IReadOnlyCollection<CrossDomainTransactionStateV1> resultingCrossDomainTransactions,
        IReadOnlyCollection<CrossDomainTransactionStateV1> crossDomainTransactionStateChanges,
        SqlitePersistenceStore store,
        OperationSchedulerStateV1 scheduler,
        Qa04ProductionStepCandidateIdentityRegistryV1 candidateIdentities,
        CancellationToken cancellationToken = default)
    {
        if (!Qa04DomainExecutionTargetV1.CanonicalWorkerCounts.Contains(workerCount))
            throw new InvalidDataException("qa04.production-loop.worker-count-not-canonical");
        ArgumentNullException.ThrowIfNull(partitionAuthorityState);
        ArgumentNullException.ThrowIfNull(mutationState);
        ArgumentNullException.ThrowIfNull(domainAuthorities);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(basisCrossDomainTransactions);
        ArgumentNullException.ThrowIfNull(resultingCrossDomainTransactions);
        ArgumentNullException.ThrowIfNull(crossDomainTransactionStateChanges);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(candidateIdentities);

        var basisStep = checked(injectionStep + 1UL);
        if (partitionAuthorityState.Header.WorldId != Qa04ReferenceLoadV1.WorldId ||
            partitionAuthorityState.Header.Step != basisStep)
            throw new InvalidDataException("qa04.production-loop.basis-state-drift");
        if (scheduler.FreezeStep is not null || scheduler.NextSchedulableStep != basisStep)
            throw new InvalidDataException("qa04.production-loop.scheduler-basis-drift");
        if (scheduler.ForEffectiveStep(basisStep).Count != 0)
            throw new InvalidDataException("qa04.production-loop.current-step-scheduler-not-empty-before-admission");
        if (domainAuthorities.Count != StandardDomainPartitionRegistry.StandardPartitionCount)
            throw new InvalidDataException("qa04.production-loop.domain-authority-count-not-97");
        if (basisCrossDomainTransactions.Count != checked((int)Qa04CrossDomainTransactionGenesisMaterializerV1.CanonicalActiveCount) ||
            resultingCrossDomainTransactions.Count != checked((int)Qa04CrossDomainTransactionGenesisMaterializerV1.CanonicalActiveCount))
            throw new InvalidDataException("qa04.production-loop.transaction-active-count-drift");
        if (crossDomainTransactionStateChanges.Count != 0 &&
            crossDomainTransactionStateChanges.Count != checked((int)(Qa04CrossDomainTransactionTurnoverMaterializerV1.CohortSize * 2UL)))
            throw new InvalidDataException("qa04.production-loop.transaction-turnover-change-count-drift");

        var candidateIdentity = candidateIdentities.DeriveAndRegister(
            partitionAuthorityState.Header.WorldId,
            basisStep);
        if (candidateIdentity.TargetStep != checked(basisStep + 1UL))
            throw new InvalidDataException("qa04.production-loop.candidate-target-step-drift");
        var candidateId = candidateIdentity.CandidateId;

        var bindings = Qa04ReferenceLoadV1.OperationsForStep(injectionStep)
            .Select(descriptor => Qa04CanonicalOperationBindingV1.Bind(
                descriptor,
                partitionAuthorityState.Header.ConfigGeneration))
            .OrderBy(static binding => binding.OrderKey)
            .ToArray();
        var expectedOperationCount = checked((int)Qa04ReferenceLoadV1.OperationCountForStep(injectionStep));
        if (bindings.Length != expectedOperationCount)
            throw new InvalidDataException("qa04.production-loop.operation-count-drift");

        var policy = Qa04CanonicalOperationDurableAdmissionV1.CreateCanonicalPolicy(
            partitionAuthorityState.Header.ConfigGeneration);
        foreach (var binding in bindings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var admission = await Qa04CanonicalOperationDurableAdmissionV1.AdmitAndScheduleAsync(
                    store,
                    binding,
                    policy,
                    nextSchedulableStep: basisStep,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!admission.Passed ||
                admission.Scheduled.Lifecycle != OperationLifecycleStateV1.ScheduledDurable ||
                admission.Scheduled.EffectiveStep != basisStep)
                throw new InvalidDataException("qa04.production-loop.durable-admission-failed");
            scheduler.AddDurable(binding.ScheduledOperation);
        }

        var durableCatalog = await store.ListOperationStatesCanonicalAsync(cancellationToken).ConfigureAwait(false);
        var basisState = Qa04ProductionStepBasisAuthorityV1.BindCoreAuthorityV2(
            partitionAuthorityState,
            scheduler,
            durableCatalog,
            basisCrossDomainTransactions);
        Qa04ProductionStepBasisAuthorityV1.ValidateBoundCoreAuthorityV2(
            basisState,
            scheduler,
            durableCatalog,
            basisCrossDomainTransactions);

        var frozen = StepInputFreezerV1.Freeze(basisState, scheduler);
        if (frozen.ScheduledOperations.Count != bindings.Length)
            throw new InvalidDataException("qa04.production-loop.frozen-operation-count-drift");

        var runtimeOutputs = await DomainRuntimeExecutorV1.ExecuteAsync(
                StandardDomainExecutionPlanV1.Create(),
                basisState,
                frozen,
                CreateProductionRuntimes(bindings.Length),
                workerCount,
                cancellationToken)
            .ConfigureAwait(false);
        if (runtimeOutputs.Count != 8 || runtimeOutputs.Any(output => output.BasisStep != basisStep))
            throw new InvalidDataException("qa04.production-loop.domain-runtime-output-drift");

        var mutation = Qa04CanonicalOperationMutationBatchV1.Apply(
            Qa04ReferenceLoadV1.WorldId,
            basisStep,
            bindings,
            mutationState,
            references);
        if (mutation.AppliedOperationIds.Count != bindings.Length || mutation.Changes.Count != bindings.Length)
            throw new InvalidDataException("qa04.production-loop.typed-mutation-coverage-drift");

        var preparation = Qa04ProductionAuthoritativeStepPreparationV1.Prepare(
            candidateId,
            basisState,
            frozen,
            bindings,
            mutation,
            references,
            runtimeOutputs);

        var terminals = bindings
            .Select(static binding => new TerminalOperationCommit(
                binding.SourceDescriptor.OperationId,
                (int)CoreOperationResultStatusV1.Success,
                "operation.succeeded"))
            .ToArray();
        var finalized = await Qa04CanonicalOperationStepFinalizationV1.CommitAndPublishAsync(
                store,
                scheduler,
                preparation,
                terminals,
                cancellationToken,
                crossDomainTransactions: basisCrossDomainTransactions,
                resultingCrossDomainTransactions: resultingCrossDomainTransactions,
                crossDomainTransactionStateChanges: crossDomainTransactionStateChanges)
            .ConfigureAwait(false);
        var verification = finalized.PostCommitVerification
            ?? throw new InvalidDataException("qa04.production-loop.post-commit-verification-missing");
        var resultingStep = checked(basisStep + 1UL);
        if (verification.ResultingStep != resultingStep ||
            finalized.AuthoritativeState.State.Header.Step != resultingStep ||
            finalized.TerminalOperations.Count != bindings.Length ||
            scheduler.FreezeStep is not null || scheduler.NextSchedulableStep != resultingStep)
            throw new InvalidDataException("qa04.production-loop.post-commit-authority-drift");

        var resultingAuthorities = Qa04ProductionDomainSnapshotAuthorityBuilderV1.CreateResultingState(
            finalized.AuthoritativeState.State,
            domainAuthorities,
            mutation.State);
        if (resultingAuthorities.CanonicalAuthorities.Count != StandardDomainPartitionRegistry.StandardPartitionCount)
            throw new InvalidDataException("qa04.production-loop.resulting-domain-authority-count-not-97");

        return new Qa04ProductionAuthoritativeStepExecutionV1(
            injectionStep,
            basisStep,
            resultingStep,
            candidateId,
            bindings.Length,
            mutation.State,
            Array.AsReadOnly(resultingAuthorities.CanonicalAuthorities.ToArray()),
            finalized);
    }

    private static IReadOnlyCollection<IDomainRuntimeV1> CreateProductionRuntimes(int expectedOperationCount)
    {
        if (expectedOperationCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(expectedOperationCount));

        ValueTask<IReadOnlyList<MutationIntentCandidateV1>> NoIntents(
            DomainRuntimeContextV1 context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (context.FrozenInput.ScheduledOperations.Count != expectedOperationCount)
                throw new InvalidDataException("qa04.production-loop.runtime-workload-count-drift");
            return ValueTask.FromResult<IReadOnlyList<MutationIntentCandidateV1>>(
                Array.Empty<MutationIntentCandidateV1>());
        }

        static ValueTask<IReadOnlyList<PartitionCandidateV1>> NoResidentPartitionCandidates(
            DomainRuntimeContextV1 context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IReadOnlyList<PartitionCandidateV1>>(
                Array.Empty<PartitionCandidateV1>());
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
