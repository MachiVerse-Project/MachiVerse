using System.Security.Cryptography;
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
    int SnapshotSectionCount,
    int SnapshotChunkCount,
    string SnapshotDigest,
    string SnapshotPhysicalManifestDigest,
    string SnapshotRecoveredStateDigest,
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
/// coordinator and measured. Its exact 6 Core + 97 Domain authority is retained while later Steps
/// continue, then drained, atomically committed, durably recovered and semantically rehashed after
/// State(27001). Snapshot drain is cooldown work and is not included in Step wall-time samples.
/// </summary>
public static class Qa04ProductionReferenceRunV1
{
    public const int CanonicalTransitionCount = 27_000;

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
        var snapshotCommitted = false;

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

            var snapshot = await DrainCommitAndRecoverSnapshotAsync(
                snapshotCoordinator,
                frozenSnapshot,
                frozenDomainAuthorities,
                store,
                paths,
                cancellationToken).ConfigureAwait(false);
            snapshotCommitted = true;

            if (!CryptographicOperations.FixedTimeEquals(
                    snapshot.RecoveredStateDigest,
                    frozenSnapshot.FrozenState.Diagnostic.StateDigest))
                throw new InvalidDataException("qa04.production-run.snapshot-semantic-rehash-drift");

            var measurementSnapshot = measurement.Snapshot();
            var performance = Qa04PerformanceThresholdsV1.EvaluateCompleteMeasurement(
                measurementSnapshot,
                acceptedOperationLossCount: 0,
                hiddenSolverIterationReductionCount: 0,
                persistenceMetricObserverFailureCount: store.CommitMetricObserverFailureCount);

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
                SnapshotDrainCompleted: true,
                SnapshotSectionCount: snapshot.SectionCount,
                SnapshotChunkCount: snapshot.ChunkCount,
                SnapshotDigest: Hex(snapshot.SnapshotDigest),
                SnapshotPhysicalManifestDigest: Hex(snapshot.PhysicalManifestDigest),
                SnapshotRecoveredStateDigest: Hex(snapshot.RecoveredStateDigest),
                Measurement: measurementSnapshot,
                PerformanceThresholdsPassed: performance.Passed,
                AcceptedOperationLoss: 0,
                HiddenSolverIterationReduction: false,
                PersistenceMetricObserverFailureCount: store.CommitMetricObserverFailureCount,
                Passed: performance.Passed,
                FailureCodes: performance.FailureCodes);
        }
        finally
        {
            if (frozenSnapshot is not null && !snapshotCommitted)
                snapshotCoordinator.Abandon(frozenSnapshot);
        }
    }

    private static async Task<Qa04ProductionExact103SnapshotPersistenceProofV1> DrainCommitAndRecoverSnapshotAsync(
        RunningSnapshotCoordinatorV1 coordinator,
        RunningSnapshotCutV1 cut,
        DomainPartitionSnapshotAuthoritySetV1 domainAuthorities,
        SqlitePersistenceStore store,
        WorldPersistencePaths world,
        CancellationToken cancellationToken)
    {
        var coreOwnerMaterial = cut.CoreOwnerMaterial
            ?? throw new InvalidDataException("qa04.production-run.snapshot-core-owner-material-missing");
        if (!coreOwnerMaterial.HasOperationAuthorityV2)
            throw new InvalidDataException("qa04.production-run.snapshot-operation-v2-missing");

        var providers = StandardDomainSnapshotOwnerCompositionV1.CreateAllProviders();
        var sections = StandardSnapshotStreamingOwnerCompositionV1.CreateAll103WithTerrainV2(
            coreOwnerMaterial,
            domainAuthorities,
            providers);
        if (sections.Count != 103 ||
            sections.Count != SnapshotManifestValidation.StandardRequiredSectionCount)
            throw new InvalidDataException("qa04.production-run.snapshot-section-count-not-103");

        var coreSectionCount = sections.Count(static section => StandardSnapshotSectionSetV1.IsCoreSection(section.SectionId));
        var domainSectionCount = sections.Count(static section => StandardDomainPartitionRegistry.TryGet(section.SectionId, out _));
        if (coreSectionCount != 6 || domainSectionCount != 97)
            throw new InvalidDataException("qa04.production-run.snapshot-section-owner-count-drift");

        ulong domainLogicalRecordCount = 0;
        foreach (var section in sections)
        {
            if (StandardDomainPartitionRegistry.TryGet(section.SectionId, out _))
                domainLogicalRecordCount = checked(domainLogicalRecordCount + section.LogicalItemCount);
        }

        var physical = SnapshotPhysicalStaging.Prepare(world, cut.SnapshotId);
        var config = Qa04ReferenceConfigAuthorityV1.CreateCanonical();
        var zstd = new ZstdSnapshotChunkCompressionCodecV1();
        var staged = await CanonicalSnapshotProductionManifestDrainV1.StageRunningCutStreamingAsync(
            cut,
            physical,
            sections,
            config,
            Qa04ReferenceLoadV1.WorldSeed,
            zstdCodec: zstd,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (staged.Manifest.Logical.Sections.Count != 103 || staged.Chunks.Count == 0)
            throw new InvalidDataException("qa04.production-run.snapshot-staged-material-incomplete");

        var committed = await coordinator.CommitDrainedAsync(
            cut,
            store,
            world,
            physical,
            staged.SnapshotDigest,
            staged.PhysicalManifestDigest,
            async (candidate, token) =>
            {
                var manifest = await SnapshotPhysicalManifestStagingValidationV1.ValidateAsync(
                    candidate,
                    cancellationToken: token).ConfigureAwait(false);
                SnapshotPhysicalManifestStreamingAuthorityValidationV1.RequireExpectedAuthority(
                    manifest,
                    sections,
                    cut.FrozenState,
                    cut,
                    staged.SnapshotDigest,
                    staged.PhysicalManifestDigest);
            },
            cancellationToken).ConfigureAwait(false);
        if (committed.SnapshotId != cut.SnapshotId || committed.SnapshotStep != cut.SnapshotStep)
            throw new InvalidDataException("qa04.production-run.snapshot-commit-receipt-drift");

        var persisted = new Qa04ProductionExact103SnapshotPersistenceProofV1(
            cut.SnapshotStep,
            sections.Count,
            coreSectionCount,
            domainSectionCount,
            staged.Chunks.Count,
            domainLogicalRecordCount,
            staged.SnapshotDigest.ToArray(),
            staged.PhysicalManifestDigest.ToArray());
        var recovered = await Qa04ProductionExact103SnapshotRecoveryProofRunnerV1.VerifyAsync(
            persisted,
            store,
            world,
            cancellationToken).ConfigureAwait(false);
        if (recovered.SnapshotStep != cut.SnapshotStep ||
            recovered.SectionCount != 103 ||
            recovered.CoreSectionCount != 6 ||
            recovered.DomainSectionCount != 97 ||
            recovered.ChunkCount != staged.Chunks.Count ||
            recovered.DomainLogicalRecordCount != domainLogicalRecordCount ||
            recovered.RecoveredStateDigest.Length != 32)
            throw new InvalidDataException("qa04.production-run.snapshot-recovery-drift");

        return persisted with
        {
            RecoveredStateDigest = recovered.RecoveredStateDigest.ToArray(),
        };
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
