using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed class Qa04RunningSnapshotProbeResultV1
{
    public string SchemaVersion { get; init; } = "1.0";
    public ulong StandardIntervalSteps { get; init; }
    public bool StandardTriggerAt18000 { get; init; }
    public ulong FrozenSnapshotStep { get; init; }
    public ulong LaterFinalizedStepBeforeDrain { get; init; }
    public ulong FinalizedStepAfterDrain { get; init; }
    public ulong FrozenHistoryAnchorSequence { get; init; }
    public ulong SnapshotCommitHistorySequence { get; init; }
    public int FrozenOperationCount { get; init; }
    public int FrozenScheduledOperationCount { get; init; }
    public bool SnapshotIdDeterministic { get; init; }
    public bool SingleInFlightEnforced { get; init; }
    public bool HistoricalCutCommittedAfterLaterStep { get; init; }
    public bool FrozenHistoryAnchorStillDurable { get; init; }
    public bool SnapshotCatalogPreservedFrozenCut { get; init; }
    public bool LaterAuthorityPreservedAfterDrain { get; init; }
    public bool SnapshotHistoryAppendedAtDrainHead { get; init; }
    public bool PhysicalSnapshotAtomicallyFinalized { get; init; }
    public bool ReducedRunningSnapshotAuthorityAvailable { get; init; }
    public bool ReferenceWorldMaterialized { get; init; }
    public bool ReleaseEvidenceCapable { get; init; }
    public string[] BlockingFailureCodes { get; init; } = [];
}

public static class Qa04RunningSnapshotBridgeV1
{
    public static async Task<Qa04RunningSnapshotProbeResultV1> RunReducedAsync(
        string persistenceRoot,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(persistenceRoot))
            throw new ArgumentException("persistenceRoot is required.", nameof(persistenceRoot));

        var standard = new RunningSnapshotCoordinatorV1();
        var standardTrigger = !standard.IsDue(0) &&
                              !standard.IsDue(17_999) &&
                              standard.IsDue(18_000) &&
                              !standard.IsDue(18_000, newestCommittedSnapshotStep: 18_000);
        if (!standardTrigger)
            throw new InvalidDataException("qa04.snapshot.standard-trigger-contract-mismatch");

        var worldId = OpaqueId128.Parse("00000000000000000000000000000072");
        var configDigest = SHA256.HashData("qa04-running-snapshot-config"u8);
        var worldSeed = new WorldSeed256(SHA256.HashData("qa04-running-snapshot-seed"u8));
        var state = CreateState(worldId, worldSeed, configDigest, step: 0, previousStateDigest: null);
        var paths = PersistenceLayout.Resolve(persistenceRoot, worldId, 1);
        PersistenceLayout.EnsureGenerationDirectories(paths);
        await PersistenceLayout.WriteCurrentAsync(paths, 1, cancellationToken).ConfigureAwait(false);

        var genesis = CreateGenesisHistory(state, worldSeed);
        var continuity = HistoryIntegrity.ComputeGenesisContinuityToken(worldId, genesis.RecordDigest);
        await using var store = await SqlitePersistenceStore.OpenOrCreateAsync(paths, cancellationToken).ConfigureAwait(false);
        await store.InitializeWorldMetadataAsync(
            new WorldPersistenceMetadataSeed(
                worldId,
                PersistenceGeneration: 1,
                worldSeed,
                continuity,
                ConfigGeneration: 1,
                configDigest,
                MasterGeneration: 1),
            genesis,
            cancellationToken).ConfigureAwait(false);

        for (ulong step = 0; step < 30; step++)
            (state, continuity) = await CommitTransitionAsync(store, state, continuity, configDigest, worldSeed, cancellationToken).ConfigureAwait(false);

        var coordinator = new RunningSnapshotCoordinatorV1(intervalSteps: 30);
        var cut = await coordinator.TryFreezeIfDueAsync(state, store, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("qa04.snapshot.due-cut-not-frozen");
        var cutStateDigest = cut.FrozenState.Diagnostic.StateDigest.ToArray();
        var deterministicId = cut.SnapshotId == RunningSnapshotCoordinatorV1.DeriveSnapshotId(
            worldId,
            cut.SnapshotStep,
            cut.HistoryAnchor.Sequence,
            cut.HistoryAnchor.Digest,
            cut.StateContinuityToken);
        var singleInFlight = await coordinator.TryFreezeIfDueAsync(state, store, cancellationToken).ConfigureAwait(false) is null;

        (state, continuity) = await CommitTransitionAsync(store, state, continuity, configDigest, worldSeed, cancellationToken).ConfigureAwait(false);
        var recoveryBeforeDrain = await store.ReadRecoveryHeadAsync(cancellationToken).ConfigureAwait(false);

        var physical = SnapshotPhysicalStaging.Prepare(paths, cut.SnapshotId);
        var chunkPayload = cutStateDigest
            .Concat(cut.StateContinuityToken)
            .Concat(U64Be.Encode(cut.HistoryAnchor.Sequence))
            .ToArray();
        var chunkLogicalDigest = SHA256.HashData(chunkPayload);
        await SnapshotChunkFile.WriteAsync(
            Path.Combine(physical.StagingChunksDirectory, "00000000.mvchunk"),
            chunkPayload,
            (ulong)chunkPayload.Length,
            chunkLogicalDigest,
            SnapshotCompression.None,
            cancellationToken).ConfigureAwait(false);

        var manifestBytes = cut.SnapshotId.ToBytes()
            .Concat(U64Be.Encode(cut.SnapshotStep))
            .Concat(cut.HistoryAnchor.Digest)
            .Concat(cut.StateContinuityToken)
            .Concat(cutStateDigest)
            .ToArray();
        await SnapshotPhysicalStaging.WriteManifestDurablyAsync(physical, manifestBytes, cancellationToken).ConfigureAwait(false);
        var physicalManifestDigest = SHA256.HashData(manifestBytes);
        var snapshotDigest = HashSuite.DomainHash("mv.snapshot.v1", writer =>
        {
            writer.WriteMapStart(6);
            writer.WriteUnsigned(0); writer.WriteBytes(cut.SnapshotId.ToBytes());
            writer.WriteUnsigned(1); writer.WriteUnsigned(cut.SnapshotStep);
            writer.WriteUnsigned(2); writer.WriteUnsigned(cut.HistoryAnchor.Sequence);
            writer.WriteUnsigned(3); writer.WriteBytes(cut.HistoryAnchor.Digest);
            writer.WriteUnsigned(4); writer.WriteBytes(cut.StateContinuityToken);
            writer.WriteUnsigned(5); writer.WriteBytes(cutStateDigest);
        });

        var committed = await coordinator.CommitDrainedAsync(
            cut,
            store,
            paths,
            physical,
            snapshotDigest,
            physicalManifestDigest,
            async (candidate, token) =>
            {
                var actualManifest = await File.ReadAllBytesAsync(candidate.StagingManifestPath, token).ConfigureAwait(false);
                if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(actualManifest), physicalManifestDigest))
                    throw new InvalidDataException("qa04.snapshot.manifest-digest-mismatch");
                await SnapshotChunkFile.ValidateAsync(
                    Path.Combine(candidate.StagingChunksDirectory, "00000000.mvchunk"),
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        var frozenAnchorDurable = await store.HistoryAnchorExistsAsync(
            cut.HistoryAnchor.Sequence,
            cut.HistoryAnchor.Digest,
            cancellationToken).ConfigureAwait(false);
        var candidates = await store.ListSnapshotCandidatesNewestFirstAsync(cancellationToken).ConfigureAwait(false);
        var catalogPreserved = candidates.Count == 1 &&
                               candidates[0].SnapshotId == cut.SnapshotId &&
                               candidates[0].SnapshotStep == 30 &&
                               candidates[0].HistoryAnchorSequence == cut.HistoryAnchor.Sequence;
        var recoveryAfterDrain = await store.ReadRecoveryHeadAsync(cancellationToken).ConfigureAwait(false);
        var laterAuthorityPreserved = recoveryAfterDrain.FinalizedStep == 31 &&
                                      CryptographicOperations.FixedTimeEquals(recoveryAfterDrain.ContinuityToken, continuity);
        var finalHistory = await store.ReadHistoryAnchorAsync(cancellationToken).ConfigureAwait(false);
        var historyAppendedAtDrainHead = finalHistory.Sequence == committed.HistorySequence &&
                                         committed.HistorySequence > cut.HistoryAnchor.Sequence + 1;
        var physicalFinalized = Directory.Exists(physical.FinalDirectory) && !Directory.Exists(physical.StagingDirectory);
        var historicalCommit = cut.SnapshotStep == 30 &&
                               recoveryBeforeDrain.FinalizedStep == 31 &&
                               committed.SnapshotStep == 30;

        var reducedAvailable = standardTrigger && deterministicId && singleInFlight && historicalCommit &&
                               frozenAnchorDurable && catalogPreserved && laterAuthorityPreserved &&
                               historyAppendedAtDrainHead && physicalFinalized;
        if (!reducedAvailable)
            throw new InvalidDataException("qa04.snapshot.reduced-running-snapshot-proof-incomplete");

        return new Qa04RunningSnapshotProbeResultV1
        {
            StandardIntervalSteps = RunningSnapshotCoordinatorV1.StandardIntervalSteps,
            StandardTriggerAt18000 = standardTrigger,
            FrozenSnapshotStep = cut.SnapshotStep,
            LaterFinalizedStepBeforeDrain = recoveryBeforeDrain.FinalizedStep,
            FinalizedStepAfterDrain = recoveryAfterDrain.FinalizedStep,
            FrozenHistoryAnchorSequence = cut.HistoryAnchor.Sequence,
            SnapshotCommitHistorySequence = committed.HistorySequence,
            FrozenOperationCount = cut.DurableOperations.Count,
            FrozenScheduledOperationCount = cut.ScheduledOperations.Count,
            SnapshotIdDeterministic = deterministicId,
            SingleInFlightEnforced = singleInFlight,
            HistoricalCutCommittedAfterLaterStep = historicalCommit,
            FrozenHistoryAnchorStillDurable = frozenAnchorDurable,
            SnapshotCatalogPreservedFrozenCut = catalogPreserved,
            LaterAuthorityPreservedAfterDrain = laterAuthorityPreserved,
            SnapshotHistoryAppendedAtDrainHead = historyAppendedAtDrainHead,
            PhysicalSnapshotAtomicallyFinalized = physicalFinalized,
            ReducedRunningSnapshotAuthorityAvailable = reducedAvailable,
            ReferenceWorldMaterialized = false,
            ReleaseEvidenceCapable = false,
            BlockingFailureCodes =
            [
                "qa04.target.canonical-103-section-snapshot-serialization-not-assembled",
                "qa04.target.reference-world-not-materialized",
                "qa04.target.authoritative-step-loop-not-assembled",
            ],
        };
    }

    private static async Task<(WorldStateV1 State, byte[] Continuity)> CommitTransitionAsync(
        SqlitePersistenceStore store,
        WorldStateV1 state,
        byte[] previousContinuity,
        byte[] configDigest,
        WorldSeed256 worldSeed,
        CancellationToken cancellationToken)
    {
        var anchor = await store.ReadHistoryAnchorAsync(cancellationToken).ConfigureAwait(false);
        var targetStep = checked(state.Header.Step + 1);
        var nextState = CreateState(
            state.Header.WorldId,
            worldSeed,
            configDigest,
            targetStep,
            state.Diagnostic.StateDigest);
        var history = HistoryRecordMaterial.Create(
            state.Header.WorldId,
            checked(anchor.Sequence + 1),
            anchor.Digest,
            "transition.committed.v1",
            "persistence.transition-committed",
            1,
            0,
            nextState.Diagnostic.StateDigest,
            writer =>
            {
                writer.WriteMapStart(3);
                writer.WriteUnsigned(0); writer.WriteUnsigned(state.Header.Step);
                writer.WriteUnsigned(1); writer.WriteUnsigned(targetStep);
                writer.WriteUnsigned(2); writer.WriteBytes(nextState.Diagnostic.StateDigest);
            });
        var continuity = HistoryIntegrity.ComputeTransitionContinuityToken(
            state.Header.WorldId,
            targetStep,
            previousContinuity,
            history.RecordDigest);
        await store.PersistTransitionCommitAsync(
            state.Header.Step,
            targetStep,
            continuity,
            activeConfigGeneration: 1,
            configDigest,
            history,
            Array.Empty<TerminalOperationCommit>(),
            cancellationToken).ConfigureAwait(false);
        return (nextState, continuity);
    }

    private static WorldStateV1 CreateState(
        OpaqueId128 worldId,
        WorldSeed256 worldSeed,
        byte[] configDigest,
        ulong step,
        byte[]? previousStateDigest)
    {
        var partitions = StandardDomainPartitionRegistry.Entries.Select(identity => new PartitionStateRefV1(
            new PartitionStateHeaderV1(
                identity,
                revision: 1,
                basisStep: 0,
                detailLevel: DetailLevelV1.D0Entity,
                itemCount: 0,
                canonicalDigest: SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(identity.PartitionId.Value)))));
        return new WorldStateV1(
            new WorldStateHeaderV1(
                worldId,
                step,
                SHA256.HashData(worldSeed.ToBytes()),
                configGeneration: 1,
                masterGeneration: 1,
                rateGeneration: 1,
                previousStateDigest),
            new OrderedPartitionDirectoryV1(partitions),
            WorldStateV1.EmptySubstate("core.scheduler-state"),
            WorldStateV1.EmptySubstate("core.operation-state"),
            WorldStateV1.EmptySubstate("core.detail-state"),
            WorldStateV1.EmptySubstate("core.domain-registry-state"),
            configDigest);
    }

    private static HistoryRecordMaterial CreateGenesisHistory(WorldStateV1 state, WorldSeed256 worldSeed)
        => HistoryRecordMaterial.Create(
            state.Header.WorldId,
            sequence: 1,
            previousRecordDigest: new byte[32],
            recordType: "world.genesis.v1",
            payloadSchemaId: "core.world-genesis.v1",
            payloadSchemaMajor: 1,
            payloadSchemaMinor: 0,
            payloadBytes: state.Header.WorldId.ToBytes().Concat(worldSeed.ToBytes()).ToArray(),
            writeNormalizedPayload: writer =>
            {
                writer.WriteMapStart(4);
                writer.WriteUnsigned(0); writer.WriteBytes(state.Header.WorldId.ToBytes());
                writer.WriteUnsigned(1); writer.WriteBytes(worldSeed.ToBytes());
                writer.WriteUnsigned(2); writer.WriteUnsigned(0);
                writer.WriteUnsigned(3); writer.WriteBytes(state.Diagnostic.StateDigest);
            });
}
