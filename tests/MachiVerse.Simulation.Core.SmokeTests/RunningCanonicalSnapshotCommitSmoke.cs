using System.Security.Cryptography;
using System.Text;
using MachiVerse.Simulation.Core.Configuration;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class RunningCanonicalSnapshotCommitSmoke
{
    internal static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "machiverse-running-canonical-snapshot-" + Guid.NewGuid().ToString("N"));
        try
        {
            var worldId = OpaqueId128.Parse("000000000000000000000000000000a1");
            var config = new CoreConfigCoordinator().LoadStartup(
                """
                [meta]
                format = "machiverse-config"
                schema_version = "1.0"
                component = "simulation-core"
                """);
            var configDigest = config.Digest;
            var worldSeed = new WorldSeed256(SHA256.HashData("running-canonical-snapshot-seed"u8));
            var state = CreateState(worldId, worldSeed, configDigest, 0, null);
            var paths = PersistenceLayout.Resolve(root, worldId, 1);
            PersistenceLayout.EnsureGenerationDirectories(paths);
            await PersistenceLayout.WriteCurrentAsync(paths, 1);

            var genesis = CreateGenesisHistory(state, worldSeed);
            var continuity = HistoryIntegrity.ComputeGenesisContinuityToken(worldId, genesis.RecordDigest);
            await using var store = await SqlitePersistenceStore.OpenOrCreateAsync(paths);
            await store.InitializeWorldMetadataAsync(
                new WorldPersistenceMetadataSeed(worldId, 1, worldSeed, continuity, config.Generation, configDigest, 1),
                genesis);

            for (ulong step = 0; step < 30; step++)
                (state, continuity) = await CommitTransitionAsync(store, state, continuity, configDigest, worldSeed);

            var coordinator = new RunningSnapshotCoordinatorV1(30);
            var cut = await coordinator.TryFreezeIfDueAsync(state, store)
                ?? throw new InvalidOperationException("Canonical running snapshot did not freeze State(30).");
            var fixture = BuildOwnerMaterials(cut.FrozenState);

            // Prove canonical physical drain/verification is compatible with the running-cut model:
            // State(31) becomes authority before the frozen State(30) snapshot is cataloged.
            (state, continuity) = await CommitTransitionAsync(store, state, continuity, configDigest, worldSeed);
            Require(state.Header.Step == 31, "Canonical running snapshot fixture did not advance to State(31).");

            var physical = SnapshotPhysicalStaging.Prepare(paths, cut.SnapshotId);
            var zstd = new ZstdSnapshotChunkCompressionCodecV1();
            var staged = await CanonicalSnapshotProductionManifestDrainV1.StageRunningCutAsync(
                cut,
                physical,
                fixture.Materials,
                config,
                worldSeed,
                zstdCodec: zstd);
            Require(staged.Chunks.Count > 0, "Canonical running snapshot produced no physical chunks.");
            Require(File.Exists(physical.StagingManifestPath), "Canonical production drain did not write manifest.pb.");

            var committed = await coordinator.CommitCanonicalDrainedAsync(
                cut,
                store,
                paths,
                physical,
                staged.SnapshotDigest,
                staged.PhysicalManifestDigest,
                fixture.Materials,
                fixture.Verifiers,
                CanonicalSnapshotProductionPhysicalDrainV1.ProductionDecoders(zstd));

            Require(committed.SnapshotStep == 30, "Canonical running snapshot committed the wrong frozen Step.");
            var candidates = await store.ListSnapshotCandidatesNewestFirstAsync();
            Require(candidates.Count == 1 && candidates[0].SnapshotId == cut.SnapshotId && candidates[0].SnapshotStep == 30,
                "Canonical running snapshot was not cataloged as the frozen recovery candidate.");
            Require(candidates[0].SnapshotDigest.SequenceEqual(staged.SnapshotDigest) &&
                    candidates[0].PhysicalManifestDigest.SequenceEqual(staged.PhysicalManifestDigest),
                "Snapshot catalog must persist exactly the validated manifest authorities.");
            var recovery = await store.ReadRecoveryHeadAsync();
            Require(recovery.FinalizedStep == 31 && CryptographicOperations.FixedTimeEquals(recovery.ContinuityToken, continuity),
                "Canonical snapshot commit moved the later recovery head backward.");
            Require(Directory.Exists(physical.FinalDirectory) && !Directory.Exists(physical.StagingDirectory),
                "Canonical running snapshot staging was not atomically finalized.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static OwnerFixture BuildOwnerMaterials(WorldStateV1 state)
    {
        var materials = new List<CanonicalSnapshotSectionMaterialV1>(103);
        var verifiers = new List<SnapshotSectionSemanticVerifierV1>(103);
        foreach (var sectionId in StandardSnapshotSectionSetV1.SectionIds)
        {
            var payload = Encoding.ASCII.GetBytes(sectionId);
            SchemaRefV1 schema;
            ulong itemCount;
            byte[] digest;
            if (StandardDomainPartitionRegistry.TryGet(sectionId, out var identity) && identity is not null)
            {
                var header = state.Partitions.Get(sectionId).Header;
                schema = identity.PartitionSchema;
                itemCount = header.ItemCount;
                digest = header.CanonicalDigest.ToArray();
                Require(CryptographicOperations.FixedTimeEquals(digest, SHA256.HashData(payload)),
                    "Running snapshot domain fixture digest must bind its owner payload.");
            }
            else
            {
                schema = new SchemaRefV1("fixture.snapshot-core-section");
                itemCount = 1;
                digest = SHA256.HashData(payload);
            }

            var fragment = new SnapshotSectionFragmentMaterialV1(
                sectionId, 0, 1, null, null, itemCount, payload);
            materials.Add(new CanonicalSnapshotSectionMaterialV1(
                sectionId, schema, itemCount, digest, new[] { fragment }));
            verifiers.Add(new SnapshotSectionSemanticVerifierV1(
                sectionId,
                schema,
                fragments => new SnapshotSectionSemanticVerificationV1(
                    fragments.Aggregate(0UL, static (sum, value) => checked(sum + value.ItemCount)),
                    SHA256.HashData(fragments.SelectMany(static value => value.FragmentPayload).ToArray()))));
        }
        return new OwnerFixture(
            Array.AsReadOnly(materials.ToArray()),
            new CanonicalSnapshotSemanticVerifierRegistryV1(verifiers));
    }

    private static async Task<(WorldStateV1 State, byte[] Continuity)> CommitTransitionAsync(
        SqlitePersistenceStore store,
        WorldStateV1 state,
        byte[] previousContinuity,
        byte[] configDigest,
        WorldSeed256 worldSeed)
    {
        var anchor = await store.ReadHistoryAnchorAsync();
        var targetStep = checked(state.Header.Step + 1);
        var nextState = CreateState(state.Header.WorldId, worldSeed, configDigest, targetStep, state.Diagnostic.StateDigest);
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
            1,
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
                1,
                0,
                DetailLevelV1.D0Entity,
                0,
                SHA256.HashData(Encoding.ASCII.GetBytes(identity.PartitionId.Value)))));
        return new WorldStateV1(
            new WorldStateHeaderV1(worldId, step, SHA256.HashData(worldSeed.ToBytes()), 1, 1, 1, previousStateDigest),
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
            1,
            new byte[32],
            "world.genesis.v1",
            "core.world-genesis.v1",
            1,
            0,
            state.Header.WorldId.ToBytes().Concat(worldSeed.ToBytes()).ToArray(),
            writer =>
            {
                writer.WriteMapStart(4);
                writer.WriteUnsigned(0); writer.WriteBytes(state.Header.WorldId.ToBytes());
                writer.WriteUnsigned(1); writer.WriteBytes(worldSeed.ToBytes());
                writer.WriteUnsigned(2); writer.WriteUnsigned(0);
                writer.WriteUnsigned(3); writer.WriteBytes(state.Diagnostic.StateDigest);
            });

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed record OwnerFixture(
        IReadOnlyList<CanonicalSnapshotSectionMaterialV1> Materials,
        CanonicalSnapshotSemanticVerifierRegistryV1 Verifiers);
}
