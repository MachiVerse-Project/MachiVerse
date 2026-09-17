using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class RunningSnapshotCoordinatorSmoke
{
    internal static async Task RunAsync()
    {
        var standard = new RunningSnapshotCoordinatorV1();
        Require(!standard.IsDue(0), "Snapshot must not trigger at genesis Step 0.");
        Require(!standard.IsDue(17_999), "Standard snapshot trigger fired before 18,000 Steps.");
        Require(standard.IsDue(18_000), "Standard snapshot trigger must fire at Step 18,000.");
        Require(!standard.IsDue(18_000, newestCommittedSnapshotStep: 18_000),
            "Already committed snapshot Step must not retrigger.");

        var root = Path.Combine(Path.GetTempPath(), "machiverse-running-snapshot-" + Guid.NewGuid().ToString("N"));
        try
        {
            var worldId = OpaqueId128.Parse("00000000000000000000000000000071");
            var configDigest = SHA256.HashData("running-snapshot-config"u8);
            var worldSeed = new WorldSeed256(SHA256.HashData("running-snapshot-seed"u8));
            var state = CreateState(worldId, worldSeed, configDigest, step: 0, previousStateDigest: null);
            var paths = PersistenceLayout.Resolve(root, worldId, 1);
            PersistenceLayout.EnsureGenerationDirectories(paths);
            await PersistenceLayout.WriteCurrentAsync(paths, 1);

            var genesis = CreateGenesisHistory(state, worldSeed);
            var continuity = HistoryIntegrity.ComputeGenesisContinuityToken(worldId, genesis.RecordDigest);
            await using var store = await SqlitePersistenceStore.OpenOrCreateAsync(paths);
            await store.InitializeWorldMetadataAsync(
                new WorldPersistenceMetadataSeed(
                    worldId,
                    PersistenceGeneration: 1,
                    worldSeed,
                    continuity,
                    ConfigGeneration: 1,
                    configDigest,
                    MasterGeneration: 1),
                genesis);

            for (ulong step = 0; step < 30; step++)
            {
                (state, continuity) = await CommitTransitionAsync(store, state, continuity, configDigest);
            }
            Require(state.Header.Step == 30, "Running snapshot fixture must reach finalized State(30).");

            var coordinator = new RunningSnapshotCoordinatorV1(intervalSteps: 30);
            var cut = await coordinator.TryFreezeIfDueAsync(state, store)
                ?? throw new InvalidOperationException("Running snapshot trigger did not freeze the due Step boundary.");
            var cutHistorySequence = cut.HistoryAnchor.Sequence;
            var cutStateDigest = cut.FrozenState.Diagnostic.StateDigest.ToArray();
            Require(cut.SnapshotStep == 30, "Frozen snapshot cut Step mismatch.");
            Require(cut.SnapshotId == RunningSnapshotCoordinatorV1.DeriveSnapshotId(
                    worldId,
                    cut.SnapshotStep,
                    cut.HistoryAnchor.Sequence,
                    cut.HistoryAnchor.Digest,
                    cut.StateContinuityToken),
                "SnapshotId derivation must be deterministic from the frozen cut.");
            Require(await coordinator.TryFreezeIfDueAsync(state, store) is null,
                "Only one running snapshot cut may be in flight.");

            // Release the consistency barrier and let the authoritative world advance while the
            // frozen State(30) cut is conceptually being serialized in the background.
            (state, continuity) = await CommitTransitionAsync(store, state, continuity, configDigest);
            Require(state.Header.Step == 31, "Simulation did not advance after snapshot barrier release.");
            var recoveryBeforeDrain = await store.ReadRecoveryHeadAsync();
            Require(recoveryBeforeDrain.FinalizedStep == 31,
                "Persistence head did not advance while the frozen snapshot was draining.");

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
                SnapshotCompression.None);

            var manifestBytes = cut.SnapshotId.ToBytes()
                .Concat(U64Be.Encode(cut.SnapshotStep))
                .Concat(cut.HistoryAnchor.Digest)
                .Concat(cut.StateContinuityToken)
                .Concat(cutStateDigest)
                .ToArray();
            await SnapshotPhysicalStaging.WriteManifestDurablyAsync(physical, manifestBytes);
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
                async (candidate, cancellationToken) =>
                {
                    var actualManifest = await File.ReadAllBytesAsync(candidate.StagingManifestPath, cancellationToken);
                    if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(actualManifest), physicalManifestDigest))
                        throw new InvalidDataException("running-snapshot.manifest-digest-mismatch");
                    await SnapshotChunkFile.ValidateAsync(
                        Path.Combine(candidate.StagingChunksDirectory, "00000000.mvchunk"),
                        cancellationToken);
                });

            Require(committed.SnapshotStep == 30,
                "Running snapshot must commit the frozen Step, not the later current Step.");
            Require(await store.HistoryAnchorExistsAsync(cutHistorySequence, cut.HistoryAnchor.Digest),
                "Frozen snapshot history anchor must remain present in the durable chain.");
            var candidates = await store.ListSnapshotCandidatesNewestFirstAsync();
            Require(candidates.Count == 1 &&
                    candidates[0].SnapshotId == cut.SnapshotId &&
                    candidates[0].SnapshotStep == 30 &&
                    candidates[0].HistoryAnchorSequence == cutHistorySequence,
                "Cataloged running snapshot did not preserve its frozen consistent cut.");
            var recoveryAfterDrain = await store.ReadRecoveryHeadAsync();
            Require(recoveryAfterDrain.FinalizedStep == 31 &&
                    CryptographicOperations.FixedTimeEquals(recoveryAfterDrain.ContinuityToken, continuity),
                "Background snapshot commit must not roll back or rewrite the later authoritative Step.");
            Require((await store.ReadHistoryAnchorAsync()).Sequence == committed.HistorySequence,
                "snapshot.committed history must append at drain completion head.");
            Require(Directory.Exists(physical.FinalDirectory) && !Directory.Exists(physical.StagingDirectory),
                "Running snapshot staging directory was not atomically finalized.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<(WorldStateV1 State, byte[] Continuity)> CommitTransitionAsync(
        SqlitePersistenceStore store,
        WorldStateV1 state,
        byte[] previousContinuity,
        byte[] configDigest)
    {
        var anchor = await store.ReadHistoryAnchorAsync();
        var targetStep = checked(state.Header.Step + 1);
        var nextState = CreateState(
            state.Header.WorldId,
            new WorldSeed256(SHA256.HashData("running-snapshot-seed"u8)),
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
            Array.Empty<TerminalOperationCommit>());
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

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
