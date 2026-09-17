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
    Qa04ProductionDeterminismEvidenceV1 DeterminismEvidence,
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
/// durable State(1), then all 27,000 workload transitions execute consecutively through the Gate4
/// Step2 compact Operation authority, live Detail promotion/deferral authority, eight-domain runtime,
/// typed mutation path, canonical SQLite transition COMMIT and publish boundary. CrossDomainTransaction
/// turnover is applied every 300 basis Steps without reinitializing the world.
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
        Qa04CanonicalDetailTransitionBindingV1.ValidateCanonicalContract();
        Qa04DetailRegionCanonicalAuthorityV1.ValidateCanonicalContract();
        Qa04CrossDomainTransactionTurnoverMaterializerV1.ValidateCanonicalContract();
        _ = Qa04AlgorithmIterationBudgetGuardV1.ValidateCanonicalContract();

        var assembly = Qa04ProductionReferenceWorldAssemblerV1.AssembleCanonical();
        if (assembly.Validation.CanonicalInitialRecordCount != Qa04ReferenceLoadV1.CanonicalInitialRecordCount ||
            assembly.BasisDomainAuthorities.Count != StandardDomainPartitionRegistry.StandardPartitionCount)
            throw new InvalidDataException("qa04.production-run.reference-world-incomplete");

        var turnoverAuthority = Qa04ProductionCrossDomainTurnoverAuthorityBuilderV1.CreateCanonical(
            assembly.ActiveTransactions);
        IReadOnlyList<Qa04ActiveTransactionSlotV1> currentActiveSlots = turnoverAuthority.ActiveSlots;
        IReadOnlyList<CrossDomainTransactionStateV1> currentActiveTransactions = Array.AsReadOnly(
            currentActiveSlots.Select(static slot => slot.State).ToArray());
        var currentClosedPrefix = Qa04OperationClosedPrefixV1.Empty();

        var detailMaterial = Qa04DetailRegionCanonicalAuthorityV1.MaterializeCanonical();
        var currentDetailDirectory = new DetailDirectoryV1(
            detailMaterial.RegionsByTile,
            Array.Empty<DetailTransitionCandidateV1>());
        var detailPolicy = DetailTransitionPolicyV1.FromConfig(Qa04ReferenceConfigAuthorityV1.CreateCanonical());

        var scheduler = new OperationSchedulerStateV1(
            nextSchedulableStep: Qa04ProductionReferenceWorldAssemblerV1.CanonicalBasisStep,
            freezeStep: null,
            scheduled: Array.Empty<ScheduledOperationRefV1>());
        var basisState = Qa04ProductionStep2BasisPersistenceV1.BindInitialBasis(
            assembly.PartitionAuthorityState,
            scheduler,
            currentActiveTransactions,
            currentClosedPrefix);
        Qa04ProductionStep2BasisAuthorityV1.RequireSubstateMatch(
            DetailDirectorySubstateV1.Canonicalize(currentDetailDirectory),
            basisState.DetailState,
            "qa04.production-run.initial-detail-authority-drift");

        var paths = PersistenceLayout.Resolve(persistenceRoot, Qa04ReferenceLoadV1.WorldId, 1);
        PersistenceLayout.EnsureGenerationDirectories(paths);
        await PersistenceLayout.WriteCurrentAsync(paths, 1, cancellationToken).ConfigureAwait(false);

        var collector = new Qa04BenchmarkMetricCollectorV1();
        var measurement = new Qa04BenchmarkRunMeasurementSessionV1(collector);
        var candidateIdentities = new Qa04ProductionStepCandidateIdentityRegistryV1();
        var determinismEvidence = new Qa04ProductionDeterminismEvidenceProducerV1();
        var snapshotCoordinator = new RunningSnapshotCoordinatorV1();
        RunningSnapshotCutV1? frozenSnapshot = null;
        DomainPartitionSnapshotAuthoritySetV1? frozenDomainAuthorities = null;

        await using var store = await SqlitePersistenceStore.OpenOrCreateAsync(paths, cancellationToken).ConfigureAwait(false);
        var initialContinuity = await InitializePersistenceGenesisAsync(
            store,
            basisState,
            currentActiveTransactions,
            cancellationToken).ConfigureAwait(false);
        _ = await Qa04ProductionStep2BasisPersistenceV1.PersistAsync(
            store,
            basisState,
            initialContinuity,
            currentClosedPrefix,
            currentActiveTransactions,
            cancellationToken).ConfigureAwait(false);

        var currentState = basisState;
        var currentMutationState = assembly.MutationState;
        IReadOnlyList<IDomainPartitionSnapshotAuthorityV1> currentDomainAuthorities = assembly.BasisDomainAuthorities;
        var commitMetricAttached = false;
        var snapshotCommitted = false;
        var turnoverCount = 0;
        var detailDecisionCount = 0;

        try
        {
            for (ulong injectionStep = 0; injectionStep < CanonicalTransitionCount; injectionStep++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var basisStep = checked(injectionStep + 1UL);
                var resultingStep = checked(injectionStep + 2UL);
                if (!commitMetricAttached && resultingStep == Qa04MeasurementPhaseContractV1.MeasurementFirstFinalizedStep)
                {
                    store.AttachCommitMetricSink(collector);
                    commitMetricAttached = true;
                }

                var basisTransactions = currentActiveTransactions;
                IReadOnlyList<CrossDomainTransactionStateV1> resultingTransactions = basisTransactions;
                IReadOnlyList<CrossDomainTransactionStateV1> transactionChanges = Array.Empty<CrossDomainTransactionStateV1>();
                IReadOnlyList<Qa04ActiveTransactionSlotV1>? pendingActiveSlots = null;

                if (basisStep % Qa04CrossDomainTransactionTurnoverMaterializerV1.TurnoverCadenceSteps == 0)
                {
                    var beforeBySlot = currentActiveSlots.ToDictionary(static slot => slot.SlotOrdinal);
                    var turnover = Qa04CrossDomainTransactionTurnoverMaterializerV1.Apply(
                        basisStep,
                        currentActiveSlots,
                        turnoverAuthority.CanonicalRecordPools);
                    var replacements = turnover.ActiveSlots
                        .Where(slot => slot.State.TransactionId != beforeBySlot[slot.SlotOrdinal].State.TransactionId)
                        .OrderBy(static slot => slot.SlotOrdinal)
                        .ToArray();
                    if (turnover.CommittedStates.Count != checked((int)Qa04CrossDomainTransactionTurnoverMaterializerV1.CohortSize) ||
                        replacements.Length != checked((int)Qa04CrossDomainTransactionTurnoverMaterializerV1.CohortSize) ||
                        turnover.ActiveSlots.Count != checked((int)Qa04CrossDomainTransactionGenesisMaterializerV1.CanonicalActiveCount))
                        throw new InvalidDataException("qa04.production-run.transaction-turnover-cardinality-drift");

                    transactionChanges = Array.AsReadOnly(turnover.CommittedStates
                        .Concat(replacements.Select(static slot => slot.State))
                        .OrderBy(static state => state.TransactionId)
                        .ToArray());
                    resultingTransactions = Array.AsReadOnly(
                        turnover.ActiveSlots.Select(static slot => slot.State).ToArray());
                    Qa04ProductionCrossDomainTurnoverContractV1.Validate(
                        basisStep,
                        resultingStep,
                        basisTransactions,
                        resultingTransactions,
                        transactionChanges);
                    pendingActiveSlots = turnover.ActiveSlots;
                }

                Qa04ProductionStep2AuthoritativeStepExecutionV1? executed = null;
                await measurement.ExecuteFinalizingStepAsync(
                    resultingStep,
                    async token =>
                    {
                        executed = await Qa04ProductionStep2AuthoritativeStepExecutorV1.ExecuteAsync(
                            injectionStep,
                            workerCount,
                            currentState,
                            currentMutationState,
                            currentDomainAuthorities,
                            assembly.References,
                            basisTransactions,
                            resultingTransactions,
                            transactionChanges,
                            currentClosedPrefix,
                            store,
                            scheduler,
                            candidateIdentities,
                            token,
                            currentDetailDirectory,
                            detailPolicy).ConfigureAwait(false);
                    },
                    cancellationToken).ConfigureAwait(false);

                var completed = executed
                    ?? throw new InvalidDataException("qa04.production-run.step-result-missing");
                var resultingDetailDirectory = completed.Finalization.DetailDirectory
                    ?? throw new InvalidDataException("qa04.production-run.detail-directory-missing");
                var detailDecisionAuthority = completed.Finalization.DetailDecisionAuthority
                    ?? throw new InvalidDataException("qa04.production-run.detail-decision-authority-missing");
                if (detailDecisionAuthority.BasisStep != basisStep ||
                    detailDecisionAuthority.ResultingStep != resultingStep ||
                    detailDecisionAuthority.History.NormalizedPayloadDigest.Length != 32 ||
                    !string.Equals(
                        detailDecisionAuthority.History.RecordType,
                        "qa04.detail-promotion-decision.v1",
                        StringComparison.Ordinal))
                    throw new InvalidDataException("qa04.production-run.detail-decision-authority-drift");

                determinismEvidence.Append(
                    injectionStep,
                    completed.Finalization.TransitionAuthority,
                    detailDecisionAuthority,
                    completed.ClosedPrefix);

                currentState = completed.Finalization.AuthoritativeState.State;
                currentMutationState = completed.MutationState;
                currentDomainAuthorities = completed.DomainAuthorities;
                currentClosedPrefix = completed.ClosedPrefix;
                currentDetailDirectory = resultingDetailDirectory;
                detailDecisionCount++;

                if (pendingActiveSlots is not null)
                {
                    currentActiveSlots = pendingActiveSlots;
                    currentActiveTransactions = resultingTransactions;
                    turnoverCount++;
                }

                if (resultingStep == RunningSnapshotCoordinatorV1.StandardIntervalSteps)
                {
                    var config = Qa04ReferenceConfigAuthorityV1.CreateCanonical();
                    var registry = StandardDomainRegistryAuthorityV1.Generation1;
                    frozenSnapshot = await snapshotCoordinator
                        .TryFreezeWithCoreOwnerMaterialV2IfDueMeasuredAsync(
                            currentState,
                            store,
                            new IFrozenCoreSnapshotOwnerMaterialV1[]
                            {
                                FrozenCoreConfigSnapshotOwnerV1.Freeze(currentState.Header.Step, config),
                                FrozenDetailDirectorySnapshotOwnerV1.Freeze(currentState.Header.Step, currentDetailDirectory),
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

            var expectedTurnoverCount = CanonicalTransitionCount /
                checked((int)Qa04CrossDomainTransactionTurnoverMaterializerV1.TurnoverCadenceSteps);
            if (turnoverCount != expectedTurnoverCount ||
                currentActiveTransactions.Count != checked((int)Qa04CrossDomainTransactionGenesisMaterializerV1.CanonicalActiveCount))
                throw new InvalidDataException("qa04.production-run.transaction-turnover-run-coverage-drift");
            if (detailDecisionCount != CanonicalTransitionCount)
                throw new InvalidDataException("qa04.production-run.detail-decision-run-coverage-drift");
            Qa04ProductionStep2BasisAuthorityV1.RequireSubstateMatch(
                DetailDirectorySubstateV1.Canonicalize(currentDetailDirectory),
                currentState.DetailState,
                "qa04.production-run.final-detail-authority-drift");

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

            var finalDurableOperations = await store.ListOperationStatesCanonicalAsync(cancellationToken).ConfigureAwait(false);
            if (finalDurableOperations.Count != 0)
                throw new InvalidDataException("qa04.production-run.compact-operation-row-leak");
            var finalOperationAuthority = Qa04OperationAuthorityV1.Canonicalize(
                finalDurableOperations,
                currentClosedPrefix,
                currentActiveTransactions,
                currentState.Header.Step);
            if (finalOperationAuthority.Schema != currentState.OperationState.Schema ||
                !CryptographicOperations.FixedTimeEquals(
                    finalOperationAuthority.CanonicalDigest,
                    currentState.OperationState.CanonicalDigest))
                throw new InvalidDataException("qa04.production-run.final-transaction-operation-authority-drift");

            await Qa04CrossDomainDurableAuthorityVerifierV1.RequireActiveAuthorityAsync(
                store,
                currentActiveTransactions,
                cancellationToken).ConfigureAwait(false);
            await using (var reopened = await SqlitePersistenceStore.OpenOrCreateAsync(paths, cancellationToken).ConfigureAwait(false))
            {
                await Qa04CrossDomainDurableAuthorityVerifierV1.RequireActiveAuthorityAsync(
                    reopened,
                    currentActiveTransactions,
                    cancellationToken).ConfigureAwait(false);
                var reopenedRecovery = await reopened.ReadRecoveryHeadAsync(cancellationToken).ConfigureAwait(false);
                var reopenedPrefix = await reopened.ReadQa04OperationClosedPrefixAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidDataException("qa04.production-run.reopened-prefix-missing");
                if (reopenedRecovery.FinalizedStep != currentState.Header.Step ||
                    !CryptographicOperations.FixedTimeEquals(
                        reopenedRecovery.ContinuityToken,
                        finalRecovery.ContinuityToken) ||
                    reopenedPrefix.LastClosedInjectionStep != currentClosedPrefix.LastClosedInjectionStep ||
                    reopenedPrefix.TerminalOperationCount != currentClosedPrefix.TerminalOperationCount ||
                    !CryptographicOperations.FixedTimeEquals(
                        reopenedPrefix.TerminalSemanticDigest,
                        currentClosedPrefix.TerminalSemanticDigest))
                    throw new InvalidDataException("qa04.production-run.reopened-transaction-recovery-head-drift");
            }

            var candidateDigest = HashSuite.DomainHash("mv.qa04-step-candidate-sequence.v1", writer =>
            {
                writer.WriteArrayStart((ulong)candidateSequence.Count);
                foreach (var candidate in candidateSequence)
                    writer.WriteBytes(candidate.CandidateId.ToBytes());
            });
            var completedDeterminismEvidence = determinismEvidence.Complete(currentState, currentClosedPrefix);

            return new Qa04ProductionReferenceRunResultV1(
                SchemaVersion: "1.0",
                ProfileId: Qa04ReferenceLoadV1.BenchmarkProfileId,
                WorkerCount: workerCount,
                TransitionCount: CanonicalTransitionCount,
                FinalizedStep: currentState.Header.Step,
                FinalStateDigest: Hex(currentState.Diagnostic.StateDigest),
                DeterminismEvidence: completedDeterminismEvidence,
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

        var decoders = CanonicalSnapshotProductionPhysicalDrainV1.ProductionDecoders(zstd);
        var durable = await CanonicalSnapshotDurableRecoveryV1.RecoverNewestAsync(
            store,
            world,
            decoders,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (durable.Catalog.SnapshotId != cut.SnapshotId ||
            durable.Catalog.SnapshotStep != cut.SnapshotStep ||
            durable.Sections.Count != 103 ||
            durable.ChunkCount != staged.Chunks.Count ||
            !CryptographicOperations.FixedTimeEquals(durable.Catalog.SnapshotDigest, staged.SnapshotDigest) ||
            !CryptographicOperations.FixedTimeEquals(durable.Catalog.PhysicalManifestDigest, staged.PhysicalManifestDigest))
            throw new InvalidDataException("qa04.production-run.snapshot-durable-recovery-drift");

        var semantic = await CanonicalSnapshotSemanticRecoveryV1.RecoverAndRehashAsync(
            durable,
            world,
            decoders,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (semantic.Header.WorldId != cut.FrozenState.Header.WorldId ||
            semantic.Header.Step != cut.SnapshotStep ||
            semantic.SectionCount != 103 ||
            semantic.CoreSectionCount != 6 ||
            semantic.DomainSectionCount != 97 ||
            semantic.DomainLogicalRecordCount != domainLogicalRecordCount ||
            semantic.StateDigest.Length != 32)
            throw new InvalidDataException("qa04.production-run.snapshot-semantic-recovery-drift");

        return persisted with
        {
            RecoveredStateDigest = semantic.StateDigest.ToArray(),
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
