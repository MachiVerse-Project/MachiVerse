using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04ProductionReferenceConnectionProbeResultV1(
    string SchemaVersion,
    string ProfileId,
    int WorkerCount,
    int TransitionCount,
    ulong BasisStep,
    ulong FinalizedStep,
    ulong ReferenceInitialRecordCount,
    int DomainAuthorityCount,
    int OperationCount,
    string FinalStateDigest,
    ulong FinalHistorySequence,
    string FinalHistoryDigest,
    string FinalContinuityToken,
    string CandidateIdSequenceDigest,
    bool RealSqliteCommitObserved,
    bool ProductionExecutorObserved);

/// <summary>
/// Gate-4 Step-1 connection proof. It assembles the complete canonical reference world once, opens
/// the real production SQLite store, persists durable State(1), and executes the first two canonical
/// workload transitions through the exact same production executor used by the 27,000-transition
/// release benchmark. It is deliberately bounded and cannot be reported as performance or release
/// evidence; its only purpose is to prove the external evidence adapter is wired to the real runtime
/// path before the 12 full process runs begin.
/// </summary>
public static class Qa04ProductionReferenceConnectionProbeV1
{
    public const int TransitionCount = 2;

    public static async Task<Qa04ProductionReferenceConnectionProbeResultV1> RunAsync(
        int workerCount,
        string persistenceRoot,
        CancellationToken cancellationToken = default)
    {
        if (!Qa04DomainExecutionTargetV1.CanonicalWorkerCounts.Contains(workerCount))
            throw new InvalidDataException("qa04.production-connection.worker-count-not-canonical");
        if (string.IsNullOrWhiteSpace(persistenceRoot))
            throw new ArgumentException("persistenceRoot is required.", nameof(persistenceRoot));

        Qa04ReferenceLoadV1.ValidateCanonicalContract();
        Qa04ReferenceWorldDependencyContractV1.ValidateCanonicalContract();
        Qa04ReferenceWorldMaterialContractV1.RequireAllProductionMaterializersAvailable();
        Qa04CanonicalOperationBindingV1.ValidateCanonicalContract();

        var assembly = Qa04ProductionReferenceWorldAssemblerV1.AssembleCanonical();
        if (assembly.Validation.CanonicalInitialRecordCount != Qa04ReferenceLoadV1.CanonicalInitialRecordCount ||
            assembly.BasisDomainAuthorities.Count != StandardDomainPartitionRegistry.StandardPartitionCount)
            throw new InvalidDataException("qa04.production-connection.reference-world-incomplete");

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
        var candidateIdentities = new Qa04ProductionStepCandidateIdentityRegistryV1();
        var operationCount = 0;

        for (ulong injectionStep = 0; injectionStep < TransitionCount; injectionStep++)
        {
            var completed = await Qa04ProductionAuthoritativeStepExecutorV1.ExecuteAsync(
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
                cancellationToken).ConfigureAwait(false);
            currentState = completed.Finalization.AuthoritativeState.State;
            currentMutationState = completed.MutationState;
            currentDomainAuthorities = completed.DomainAuthorities;
            operationCount = checked(operationCount + completed.OperationCount);
        }

        var expectedFinalStep = checked(Qa04ProductionReferenceWorldAssemblerV1.CanonicalBasisStep + (ulong)TransitionCount);
        if (currentState.Header.Step != expectedFinalStep || scheduler.NextSchedulableStep != expectedFinalStep)
            throw new InvalidDataException("qa04.production-connection.final-step-drift");
        if (candidateIdentities.Count != TransitionCount)
            throw new InvalidDataException("qa04.production-connection.candidate-count-drift");
        if (operationCount != checked((int)(
                Qa04ReferenceLoadV1.OperationCountForStep(0) +
                Qa04ReferenceLoadV1.OperationCountForStep(1))))
            throw new InvalidDataException("qa04.production-connection.operation-count-drift");

        var finalHistory = await store.ReadHistoryAnchorAsync(cancellationToken).ConfigureAwait(false);
        var finalRecovery = await store.ReadRecoveryHeadAsync(cancellationToken).ConfigureAwait(false);
        if (finalRecovery.FinalizedStep != currentState.Header.Step)
            throw new InvalidDataException("qa04.production-connection.recovery-head-drift");

        var candidateSequence = candidateIdentities.SnapshotCanonical();
        var candidateDigest = HashSuite.DomainHash("mv.qa04-step-candidate-sequence.v1", writer =>
        {
            writer.WriteArrayStart((ulong)candidateSequence.Count);
            foreach (var candidate in candidateSequence)
                writer.WriteBytes(candidate.CandidateId.ToBytes());
        });

        return new Qa04ProductionReferenceConnectionProbeResultV1(
            SchemaVersion: "1.0",
            ProfileId: Qa04ReferenceLoadV1.BenchmarkProfileId,
            WorkerCount: workerCount,
            TransitionCount: TransitionCount,
            BasisStep: Qa04ProductionReferenceWorldAssemblerV1.CanonicalBasisStep,
            FinalizedStep: currentState.Header.Step,
            ReferenceInitialRecordCount: assembly.Validation.CanonicalInitialRecordCount,
            DomainAuthorityCount: currentDomainAuthorities.Count,
            OperationCount: operationCount,
            FinalStateDigest: Hex(currentState.Diagnostic.StateDigest),
            FinalHistorySequence: finalHistory.Sequence,
            FinalHistoryDigest: Hex(finalHistory.Digest),
            FinalContinuityToken: Hex(finalRecovery.ContinuityToken),
            CandidateIdSequenceDigest: Hex(candidateDigest),
            RealSqliteCommitObserved: finalRecovery.FinalizedStep == expectedFinalStep,
            ProductionExecutorObserved: true);
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
