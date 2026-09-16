using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04ProductionReferenceRunResultV1(
    string SchemaVersion,
    string ProfileId,
    int WorkerCount,
    int TransitionCount,
    ulong FinalizedStep,
    string FinalStateDigest,
    ulong FinalHistorySequence,
    string FinalHistoryDigest,
    string FinalContinuityToken,
    string CandidateIdSequenceDigest,
    bool SnapshotCowFrozen,
    ulong SnapshotStep,
    bool SnapshotDrainCompleted,
    Qa04PerformanceMeasurementSnapshotV1 Measurement,
    bool PerformanceThresholdsPassed,
    int AcceptedOperationLoss,
    bool HiddenSolverIterationReduction,
    long PersistenceMetricObserverFailureCount,
    bool Passed,
    IReadOnlyList<string> FailureCodes);

/// <summary>
/// Canonical perf.reference.v1 production process run. The reference world is assembled once at
/// durable State(1), then all 27,000 workload transitions execute consecutively through the ordinary
/// production scheduler, durable Operation lifecycle, eight-domain runtime, typed mutation path,
/// SQLite COMMIT and publish boundary. No Step reinitializes the world.
///
/// The standard State(18000) running-Snapshot COW cut is frozen through the v2 production
/// coordinator and measured. Physical drain/commit remains a separate Gate-4 benchmark closure and
/// is reported fail-closed until it is connected; the cut is abandoned only after all transitions
/// finish so the production process never fabricates a completed Snapshot.
/// </summary>
public static class Qa04ProductionReferenceRunV1
{
    public const int CanonicalTransitionCount = 27_000;
    private const string SnapshotDrainIncompleteCode = "qa04.measurement.snapshot-drain-not-completed";

    public static async Task<Qa04ProductionReferenceRunResultV1> RunCanonicalAsync(
        int workerCount,
        string persistenceRoot,
        CancellationToken cancellationToken = default)
    {
        if (!Qa04DomainExecutionTargetV1.CanonicalWorkerCounts.Contains(workerCount))
            throw new InvalidDataException("qa04.production-run.worker-count-not-canonical");
        if (string.IsNullOrWhiteSpace(persistenceRoot))
            throw new ArgumentException("persistenceRoot is required.", nameof(persistenceRoot));

        Qa04ReferenceLoadV1.ValidateCanonicalContract();
        Qa04MeasurementPhaseContractV1.ValidateCanonicalContract();
        Qa04ReferenceWorldDependencyContractV1.ValidateCanonicalContract();
        Qa04ReferenceWorldMaterialContractV1.RequireAllProductionMaterializersAvailable();
        Qa04CanonicalOperationBindingV1.ValidateCanonicalContract();
        _ = Qa04AlgorithmIterationBudgetGuardV1.ValidateCanonicalContract();

        var assembly = Qa04ProductionReferenceWorldAssemblerV1.AssembleCanonical();
        if (assembly.Validation.CanonicalInitialRecordCount != Qa04ReferenceLoadV1.CanonicalInitialRecordCount ||
            assembly.BasisDomainAuthorities.Count != StandardDomainPartitionRegistry.StandardPartitionCount)
            throw new InvalidDataException("qa04.production-run.reference-world-incomplete");

        var scheduler = new OperationSchedulerStateV1(
            nextSchedulableStep: Qa04ProductionReferenceWorldAssemblerV1.CanonicalBasisStep,
            freezeStep: null,
            scheduled: Array.Empty<ScheduledOperationRefV1>());
        var basisState = Qa04ProductionStepBasisAuthorityV1.BindCoreAuthorityV2(
            assembly.PartitionAuthorityState,
            scheduler,
            Array.Empty<DurableOperationStateV1>(),
            assembly.ActiveTransactions);

        var paths = PersistenceLayout.Resolve(persistenceRoot, Qa04ReferenceLoadV1.WorldId, 1);
        PersistenceLayout.EnsureGenerationDirectories(paths);
        await PersistenceLayout.WriteCurrentAsync(paths, 1, cancellationToken).ConfigureAwait(false);

        var collector = new Qa04BenchmarkMetricCollectorV1();
        var measurement = new Qa04BenchmarkRunMeasurementSessionV1(collector);
        var candidateIdentities = new Qa04ProductionStepCandidateIdentityRegistryV1();
        var snapshotCoordinator = new RunningSnapshotCoordinatorV1();
        RunningSnapshotCutV1? frozenSnapshot = null;
        DomainPartitionSnapshotAuthoritySetV1? frozenDomainAuthorities = null;

        await using var store = await SqlitePersistenceStore.OpenOrCreateAsync(paths, cancellationToken).ConfigureAwait(false);
        var initialContinuity = await InitializePersistenceGenesisAsync(
            store,
            basisState,
            assembly.ActiveTransactions,
            cancellationToken).ConfigureAwait(false);
        _ = await Qa04ProductionBasisPersistenceV1.PersistAsync(
            store,
            basisState,
            initialContinuity,
            assembly.ActiveTransactions,
            cancellationToken).ConfigureAwait(false);

        var currentState = basisState;
        var currentMutationState = assembly.MutationState;
        IReadOnlyList<IDomainPartitionSnapshotAuthorityV1> currentDomainAuthorities = assembly.BasisDomainAuthorities;
        var commitMetricAttached = false;

        try
        {
            for (ulong injectionStep = 0; injectionStep < CanonicalTransitionCount; injectionStep++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var resultingStep = checked(injectionStep + 2UL);
                if (!commitMetricAttached && resultingStep == Qa04MeasurementPhaseContractV1.MeasurementFirstFinalizedStep)
                {
                    store.AttachCommitMetricSink(collector);
                    commitMetricAttached = true;
                }

                Qa04ProductionAuthoritativeStepExecutionV1? executed = null;
                await measurement.ExecuteFinalizingStepAsync(
                    resultingStep,
                    async token =>
                    {
                        executed = await Qa04ProductionAuthoritativeStepExecutorV1.ExecuteAsync(
                            injectionStep,
                            workerCount,
                            currentState,
                            currentMutationState,
                            currentDomainAuthorities,
                            assembly.References,
                            assembly.ActiveTransactions,
                            store,
                            scheduler,
                            candidateIdentities,
                            token).ConfigureAwait(false);
                    },
                    cancellationToken).ConfigureAwait(false);

                var completed = executed
                    ?? throw new InvalidDataException("qa04.production-run.step-result-missing");
                currentState = completed.Finalization.AuthoritativeState.State;
                currentMutationState = completed.MutationState;
                currentDomainAuthorities = completed.DomainAuthorities;

                if (resultingStep == RunningSnapshotCoordinatorV1.StandardIntervalSteps)
                {
                    var config = Qa04ReferenceConfigAuthorityV1.CreateCanonical();
                    var detailMaterial = Qa04DetailRegionCanonicalAuthorityV1.MaterializeCanonical();
                    var detailDirectory = new DetailDirectoryV1(
                        detailMaterial.RegionsByTile,
                        Array.Empty<DetailTransitionCandidateV1>());
                    var registry = StandardDomainRegistryAuthorityV1.Generation1;
                    frozenSnapshot = await snapshotCoordinator
                        .TryFreezeWithCoreOwnerMaterialV2IfDueMeasuredAsync(
                            currentState,
                            store,
                            new IFrozenCoreSnapshotOwnerMaterialV1[]
                            {
                                FrozenCoreConfigSnapshotOwnerV1.Freeze(currentState.Header.Step, config),
                                FrozenDetailDirectorySnapshotOwnerV1.Freeze(currentState.Header.Step, detailDirectory),
                                FrozenDomainRegistrySnapshotOwnerV1.Freeze(currentState.Header.Step, registry),
                            },
                            collector,
                            cancellationToken)
                        .ConfigureAwait(false)
                        ?? throw new InvalidDataException("qa04.production-run.standard-snapshot-cut-missing");
                    frozenDomainAuthorities = new DomainPartitionSnapshotAuthoritySetV1(
                        currentState,
                        currentDomainAuthorities);
                }
            }

            var candidateSequence = candidateIdentities.ValidateCompleteCanonicalRun();
            if (currentState.Header.Step != Qa04MeasurementPhaseContractV1.MeasurementLastFinalizedStep ||
                measurement.LastFinalizedStep != Qa04MeasurementPhaseContractV1.MeasurementLastFinalizedStep)
                throw new InvalidDataException("qa04.production-run.final-step-drift");
            if (frozenSnapshot is null ||
                frozenSnapshot.SnapshotStep != RunningSnapshotCoordinatorV1.StandardIntervalSteps ||
                frozenSnapshot.CoreOwnerMaterial is not { HasOperationAuthorityV2: true } ||
                frozenDomainAuthorities?.CanonicalAuthorities.Count != StandardDomainPartitionRegistry.StandardPartitionCount)
                throw new InvalidDataException("qa04.production-run.snapshot-freeze-incomplete");

            var measurementSnapshot = measurement.Snapshot();
            var performance = Qa04PerformanceThresholdsV1.EvaluateCompleteMeasurement(
                measurementSnapshot,
                acceptedOperationLossCount: 0,
                hiddenSolverIterationReductionCount: 0,
                persistenceMetricObserverFailureCount: store.CommitMetricObserverFailureCount);
            var failures = performance.FailureCodes
                .Append(SnapshotDrainIncompleteCode)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static code => code, StringComparer.Ordinal)
                .ToArray();

            var finalHistory = await store.ReadHistoryAnchorAsync(cancellationToken).ConfigureAwait(false);
            var finalRecovery = await store.ReadRecoveryHeadAsync(cancellationToken).ConfigureAwait(false);
            if (finalRecovery.FinalizedStep != currentState.Header.Step)
                throw new InvalidDataException("qa04.production-run.final-recovery-head-drift");

            var candidateDigest = HashSuite.DomainHash("mv.qa04-step-candidate-sequence.v1", writer =>
            {
                writer.WriteArrayStart((ulong)candidateSequence.Count);
                foreach (var candidate in candidateSequence)
                    writer.WriteBytes(candidate.CandidateId.ToBytes());
            });

            return new Qa04ProductionReferenceRunResultV1(
                SchemaVersion: "1.0",
                ProfileId: Qa04ReferenceLoadV1.BenchmarkProfileId,
                WorkerCount: workerCount,
                TransitionCount: CanonicalTransitionCount,
                FinalizedStep: currentState.Header.Step,
                FinalStateDigest: Hex(currentState.Diagnostic.StateDigest),
                FinalHistorySequence: finalHistory.Sequence,
                FinalHistoryDigest: Hex(finalHistory.Digest),
                FinalContinuityToken: Hex(finalRecovery.ContinuityToken),
                CandidateIdSequenceDigest: Hex(candidateDigest),
                SnapshotCowFrozen: true,
                SnapshotStep: frozenSnapshot.SnapshotStep,
                SnapshotDrainCompleted: false,
                Measurement: measurementSnapshot,
                PerformanceThresholdsPassed: performance.Passed,
                AcceptedOperationLoss: 0,
                HiddenSolverIterationReduction: false,
                PersistenceMetricObserverFailureCount: store.CommitMetricObserverFailureCount,
                Passed: false,
                FailureCodes: Array.AsReadOnly(failures));
        }
        finally
        {
            if (frozenSnapshot is not null)
                snapshotCoordinator.Abandon(frozenSnapshot);
        }
    }

    private static async Task<byte[]> InitializePersistenceGenesisAsync(
        SqlitePersistenceStore store,
        WorldStateV1 state,
        IReadOnlyCollection<CrossDomainTransactionStateV1> activeTransactions,
        CancellationToken cancellationToken)
    {
        var genesis = HistoryRecordMaterial.Create(
            Qa04ReferenceLoadV1.WorldId,
            sequence: 1,
            previousRecordDigest: new byte[32],
            recordType: "world.genesis.v1",
            payloadSchemaId: "core.world-genesis.v1",
            payloadSchemaMajor: 1,
            payloadSchemaMinor: 0,
            payloadBytes: Qa04ReferenceLoadV1.WorldId.ToBytes(),
            writeNormalizedPayload: writer =>
            {
                writer.WriteMapStart(3);
                writer.WriteUnsigned(0); writer.WriteBytes(Qa04ReferenceLoadV1.WorldId.ToBytes());
                writer.WriteUnsigned(1); writer.WriteBytes(Qa04ReferenceLoadV1.WorldSeed.ToBytes());
                writer.WriteUnsigned(2); writer.WriteUnsigned(0);
            });
        var initialContinuity = HistoryIntegrity.ComputeGenesisContinuityToken(
            Qa04ReferenceLoadV1.WorldId,
            genesis.RecordDigest);
        await store.InitializeWorldMetadataWithCanonicalCrossDomainTransactionsAsync(
            new WorldPersistenceMetadataSeed(
                Qa04ReferenceLoadV1.WorldId,
                PersistenceGeneration: 1,
                Qa04ReferenceLoadV1.WorldSeed,
                initialContinuity,
                state.Header.ConfigGeneration,
                state.Diagnostic.ConfigDigest,
                state.Header.MasterGeneration),
            genesis,
            activeTransactions,
            cancellationToken).ConfigureAwait(false);
        return initialContinuity;
    }

    private static string Hex(ReadOnlySpan<byte> value)
        => Convert.ToHexString(value).ToLowerInvariant();
}
