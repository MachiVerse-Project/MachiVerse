using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04ProductionStep2DeterminismRunResultV1(
    string SchemaVersion,
    string ProfileId,
    int WorkerCount,
    int TransitionCount,
    ulong TerminalOperationCount,
    ulong FinalizedStep,
    int TurnoverCount,
    int DetailDecisionCount,
    int BurstStepCount,
    string FinalStateDigest,
    Qa04ProductionDeterminismEvidenceV1 DeterminismEvidence,
    ulong FinalHistorySequence,
    string FinalHistoryDigest,
    string FinalContinuityToken,
    string CandidateIdSequenceDigest,
    int AcceptedOperationLoss,
    bool HiddenSolverIterationReduction,
    long PersistenceMetricObserverFailureCount,
    bool Passed,
    IReadOnlyList<string> FailureCodes);

/// <summary>
/// Bounded Gate4 Step2 determinism proof. This uses the same canonical reference world,
/// operation generator, domain executor, typed mutation path, SQLite transition commit,
/// Detail authority and CrossDomainTransaction turnover as the full perf.reference.v1 run,
/// but stops at the transition count supplied by the external Step2 evidence plan.
/// Snapshot/recovery is not repeated here because Gate3 owns that production proof.
/// </summary>
public static class Qa04ProductionStep2DeterminismRunV1
{
    public const string ProfileId = "gate4.step2.determinism.v1";

    public static async Task<Qa04ProductionStep2DeterminismRunResultV1> RunAsync(
        int workerCount,
        int transitionCount,
        string persistenceRoot,
        CancellationToken cancellationToken = default)
    {
        if (!Qa04DomainExecutionTargetV1.CanonicalWorkerCounts.Contains(workerCount))
            throw new InvalidDataException("qa04.step2-determinism.worker-count-not-canonical");
        if (transitionCount <= 0 || transitionCount > Qa04ProductionReferenceRunV1.CanonicalTransitionCount)
            throw new ArgumentOutOfRangeException(nameof(transitionCount));
        if (string.IsNullOrWhiteSpace(persistenceRoot))
            throw new ArgumentException("persistenceRoot is required.", nameof(persistenceRoot));

        Qa04ReferenceLoadV1.ValidateCanonicalContract();
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
            throw new InvalidDataException("qa04.step2-determinism.reference-world-incomplete");

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
            "qa04.step2-determinism.initial-detail-authority-drift");

        var paths = PersistenceLayout.Resolve(persistenceRoot, Qa04ReferenceLoadV1.WorldId, 1);
        PersistenceLayout.EnsureGenerationDirectories(paths);
        await PersistenceLayout.WriteCurrentAsync(paths, 1, cancellationToken).ConfigureAwait(false);

        var candidateIdentities = new Qa04ProductionStepCandidateIdentityRegistryV1();
        var determinismEvidence = new Qa04ProductionDeterminismEvidenceProducerV1();

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
        var turnoverCount = 0;
        var detailDecisionCount = 0;
        var burstStepCount = 0;

        for (ulong injectionStep = 0; injectionStep < checked((ulong)transitionCount); injectionStep++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var basisStep = checked(injectionStep + 1UL);
            var resultingStep = checked(injectionStep + 2UL);
            if (Qa04ReferenceLoadV1.OperationCountForStep(injectionStep) > Qa04ReferenceLoadV1.SteadyOperationsPerStep)
                burstStepCount++;

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
                    throw new InvalidDataException("qa04.step2-determinism.transaction-turnover-cardinality-drift");

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

            var completed = await Qa04ProductionStep2AuthoritativeStepExecutorV1.ExecuteAsync(
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
                cancellationToken,
                currentDetailDirectory,
                detailPolicy).ConfigureAwait(false);

            var resultingDetailDirectory = completed.Finalization.DetailDirectory
                ?? throw new InvalidDataException("qa04.step2-determinism.detail-directory-missing");
            var detailDecisionAuthority = completed.Finalization.DetailDecisionAuthority
                ?? throw new InvalidDataException("qa04.step2-determinism.detail-decision-authority-missing");
            if (detailDecisionAuthority.BasisStep != basisStep ||
                detailDecisionAuthority.ResultingStep != resultingStep ||
                detailDecisionAuthority.History.NormalizedPayloadDigest.Length != 32 ||
                !string.Equals(
                    detailDecisionAuthority.History.RecordType,
                    "qa04.detail-promotion-decision.v1",
                    StringComparison.Ordinal))
                throw new InvalidDataException("qa04.step2-determinism.detail-decision-authority-drift");

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
        }

        var expectedTurnoverCount = transitionCount /
            checked((int)Qa04CrossDomainTransactionTurnoverMaterializerV1.TurnoverCadenceSteps);
        if (turnoverCount != expectedTurnoverCount ||
            currentActiveTransactions.Count != checked((int)Qa04CrossDomainTransactionGenesisMaterializerV1.CanonicalActiveCount))
            throw new InvalidDataException("qa04.step2-determinism.transaction-turnover-run-coverage-drift");
        if (detailDecisionCount != transitionCount)
            throw new InvalidDataException("qa04.step2-determinism.detail-decision-run-coverage-drift");

        Qa04ProductionStep2BasisAuthorityV1.RequireSubstateMatch(
            DetailDirectorySubstateV1.Canonicalize(currentDetailDirectory),
            currentState.DetailState,
            "qa04.step2-determinism.final-detail-authority-drift");

        var candidateSequence = candidateIdentities.ValidateRun(transitionCount);
        var expectedFinalizedStep = checked((ulong)transitionCount + Qa04MeasurementPhaseContractV1.InitializationBasisStep);
        if (currentState.Header.Step != expectedFinalizedStep)
            throw new InvalidDataException("qa04.step2-determinism.final-step-drift");

        var finalHistory = await store.ReadHistoryAnchorAsync(cancellationToken).ConfigureAwait(false);
        var finalRecovery = await store.ReadRecoveryHeadAsync(cancellationToken).ConfigureAwait(false);
        if (finalRecovery.FinalizedStep != currentState.Header.Step)
            throw new InvalidDataException("qa04.step2-determinism.final-recovery-head-drift");

        var finalDurableOperations = await store.ListOperationStatesCanonicalAsync(cancellationToken).ConfigureAwait(false);
        if (finalDurableOperations.Count != 0)
            throw new InvalidDataException("qa04.step2-determinism.compact-operation-row-leak");
        var finalOperationAuthority = Qa04OperationAuthorityV1.Canonicalize(
            finalDurableOperations,
            currentClosedPrefix,
            currentActiveTransactions,
            currentState.Header.Step);
        if (finalOperationAuthority.Schema != currentState.OperationState.Schema ||
            !CryptographicOperations.FixedTimeEquals(
                finalOperationAuthority.CanonicalDigest,
                currentState.OperationState.CanonicalDigest))
            throw new InvalidDataException("qa04.step2-determinism.final-transaction-operation-authority-drift");

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
                ?? throw new InvalidDataException("qa04.step2-determinism.reopened-prefix-missing");
            if (reopenedRecovery.FinalizedStep != currentState.Header.Step ||
                !CryptographicOperations.FixedTimeEquals(
                    reopenedRecovery.ContinuityToken,
                    finalRecovery.ContinuityToken) ||
                reopenedPrefix.LastClosedInjectionStep != currentClosedPrefix.LastClosedInjectionStep ||
                reopenedPrefix.TerminalOperationCount != currentClosedPrefix.TerminalOperationCount ||
                !CryptographicOperations.FixedTimeEquals(
                    reopenedPrefix.TerminalSemanticDigest,
                    currentClosedPrefix.TerminalSemanticDigest))
                throw new InvalidDataException("qa04.step2-determinism.reopened-recovery-head-drift");
        }

        var candidateDigest = HashSuite.DomainHash("mv.qa04-step-candidate-sequence.v1", writer =>
        {
            writer.WriteArrayStart((ulong)candidateSequence.Count);
            foreach (var candidate in candidateSequence)
                writer.WriteBytes(candidate.CandidateId.ToBytes());
        });
        var completedDeterminismEvidence = determinismEvidence.Complete(
            currentState,
            currentClosedPrefix,
            checked((ulong)transitionCount));

        return new Qa04ProductionStep2DeterminismRunResultV1(
            SchemaVersion: "1.0",
            ProfileId,
            WorkerCount: workerCount,
            TransitionCount: transitionCount,
            TerminalOperationCount: determinismEvidence.TerminalOperationCount,
            FinalizedStep: currentState.Header.Step,
            TurnoverCount: turnoverCount,
            DetailDecisionCount: detailDecisionCount,
            BurstStepCount: burstStepCount,
            FinalStateDigest: Hex(currentState.Diagnostic.StateDigest),
            DeterminismEvidence: completedDeterminismEvidence,
            FinalHistorySequence: finalHistory.Sequence,
            FinalHistoryDigest: Hex(finalHistory.Digest),
            FinalContinuityToken: Hex(finalRecovery.ContinuityToken),
            CandidateIdSequenceDigest: Hex(candidateDigest),
            AcceptedOperationLoss: 0,
            HiddenSolverIterationReduction: false,
            PersistenceMetricObserverFailureCount: store.CommitMetricObserverFailureCount,
            Passed: true,
            FailureCodes: Array.Empty<string>());
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
