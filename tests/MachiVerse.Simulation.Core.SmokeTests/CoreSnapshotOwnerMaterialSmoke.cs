using System.Security.Cryptography;
using System.Text;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

internal static class CoreSnapshotOwnerMaterialSmoke
{
    internal static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "machiverse-core-snapshot-owner-" + Guid.NewGuid().ToString("N"));
        try
        {
            var worldId = OpaqueId128.Parse("000000000000000000000000000000b1");
            var worldSeed = new WorldSeed256(SHA256.HashData("core-snapshot-owner-seed"u8));
            var detailBytes = Encoding.ASCII.GetBytes("detail-owner-material");
            var registryBytes = Encoding.ASCII.GetBytes("domain-registry-owner-material");
            var configBytes = Encoding.ASCII.GetBytes("config-owner-material");
            var detailAuthority = TestOwnerMaterial.Compute("core.detail-state", detailBytes);
            var registryAuthority = TestOwnerMaterial.Compute("core.domain-registry-state", registryBytes);
            var configAuthority = TestOwnerMaterial.Compute("config.simulation-core", configBytes);
            var state = CreateState(
                worldId,
                worldSeed,
                configAuthority.CanonicalDigest,
                detailAuthority,
                registryAuthority,
                step: 0,
                previousStateDigest: null);

            var paths = PersistenceLayout.Resolve(root, worldId, 1);
            PersistenceLayout.EnsureGenerationDirectories(paths);
            await PersistenceLayout.WriteCurrentAsync(paths, 1);
            var genesis = CreateGenesisHistory(state, worldSeed);
            var continuity = HistoryIntegrity.ComputeGenesisContinuityToken(worldId, genesis.RecordDigest);
            await using var store = await SqlitePersistenceStore.OpenOrCreateAsync(paths);
            await store.InitializeWorldMetadataAsync(
                new WorldPersistenceMetadataSeed(
                    worldId,
                    1,
                    worldSeed,
                    continuity,
                    1,
                    configAuthority.CanonicalDigest,
                    1),
                genesis);

            for (ulong step = 0; step < 30; step++)
            {
                (state, continuity) = await CommitTransitionAsync(
                    store,
                    state,
                    continuity,
                    worldSeed,
                    configAuthority.CanonicalDigest,
                    detailAuthority,
                    registryAuthority);
            }
            Require(state.Header.Step == 30, "Strict owner freeze fixture must reach State(30).");

            var sourceDetail = detailBytes.ToArray();
            var sourceRegistry = registryBytes.ToArray();
            var sourceConfig = configBytes.ToArray();
            var owners = new IFrozenCoreSnapshotOwnerMaterialV1[]
            {
                new TestOwnerMaterial(CoreSnapshotOwnerSectionRegistryV1.DetailDirectory, 30, "core.detail-state", sourceDetail),
                new TestOwnerMaterial(CoreSnapshotOwnerSectionRegistryV1.DomainRegistry, 30, "core.domain-registry-state", sourceRegistry),
                new TestOwnerMaterial(CoreSnapshotOwnerSectionRegistryV1.ConfigState, 30, "config.simulation-core", sourceConfig),
            };
            Array.Fill(sourceDetail, (byte)0xff);
            Array.Fill(sourceRegistry, (byte)0xff);
            Array.Fill(sourceConfig, (byte)0xff);

            var coordinator = new RunningSnapshotCoordinatorV1(intervalSteps: 30);
            var strictCut = await coordinator.TryFreezeWithCoreOwnerMaterialIfDueAsync(state, store, owners)
                ?? throw new InvalidOperationException("Strict Core owner material freeze did not produce a due cut.");
            var ownerCut = strictCut.CoreOwnerMaterial
                ?? throw new InvalidOperationException("Strict Core owner material cut was not attached.");
            Require(ownerCut.BasisStep == 30 && ownerCut.Header.Step == 30,
                "Core owner material cut basis Step mismatch.");
            Require(ownerCut.SupplementalSectionIds.Count == 3,
                "Strict Core owner material cut must contain all three supplemental owners.");
            Require(Same(ownerCut.RecomputeSchedulerAuthority(), state.SchedulerState),
                "Frozen scheduler material did not recompute authoritative scheduler digest.");
            Require(Same(ownerCut.RecomputeOperationAuthority(), state.OperationState),
                "Frozen Operation material did not recompute authoritative Operation digest.");
            coordinator.Abandon(strictCut);

            var ordinary = await coordinator.TryFreezeIfDueAsync(state, store)
                ?? throw new InvalidOperationException("Legacy running snapshot freeze unexpectedly failed.");
            Require(ordinary.CoreOwnerMaterial is null,
                "Legacy running snapshot API must not fabricate Core owner material.");
            coordinator.Abandon(ordinary);

            await RequireRejectedAsync(
                () => coordinator.TryFreezeWithCoreOwnerMaterialIfDueAsync(state, store, owners.Take(2)),
                "snapshot-running.core-owner-material-count-mismatch",
                "Missing supplemental Core owner material must fail closed.");

            var stale = owners.Select(owner => owner.SectionId == CoreSnapshotOwnerSectionRegistryV1.ConfigState
                ? new TestOwnerMaterial(CoreSnapshotOwnerSectionRegistryV1.ConfigState, 29, "config.simulation-core", configBytes)
                : owner).ToArray();
            await RequireRejectedAsync(
                () => coordinator.TryFreezeWithCoreOwnerMaterialIfDueAsync(state, store, stale),
                "snapshot-running.core-owner-material-step-mismatch:core.config-state",
                "Stale supplemental owner material must fail closed.");

            var wrongConfig = owners.Select(owner => owner.SectionId == CoreSnapshotOwnerSectionRegistryV1.ConfigState
                ? new TestOwnerMaterial(CoreSnapshotOwnerSectionRegistryV1.ConfigState, 30, "config.simulation-core", "wrong-config"u8.ToArray())
                : owner).ToArray();
            await RequireRejectedAsync(
                () => coordinator.TryFreezeWithCoreOwnerMaterialIfDueAsync(state, store, wrongConfig),
                "snapshot-running.core-owner-material-authority-mismatch:core.config-state",
                "Owner material whose semantic authority differs from State(30) must fail closed.");
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
        WorldSeed256 worldSeed,
        byte[] configDigest,
        CoreSnapshotOwnerAuthorityV1 detailAuthority,
        CoreSnapshotOwnerAuthorityV1 registryAuthority)
    {
        var anchor = await store.ReadHistoryAnchorAsync();
        var targetStep = checked(state.Header.Step + 1);
        var next = CreateState(
            state.Header.WorldId,
            worldSeed,
            configDigest,
            detailAuthority,
            registryAuthority,
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
            next.Diagnostic.StateDigest,
            writer =>
            {
                writer.WriteMapStart(3);
                writer.WriteUnsigned(0); writer.WriteUnsigned(state.Header.Step);
                writer.WriteUnsigned(1); writer.WriteUnsigned(targetStep);
                writer.WriteUnsigned(2); writer.WriteBytes(next.Diagnostic.StateDigest);
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
        return (next, continuity);
    }

    private static WorldStateV1 CreateState(
        OpaqueId128 worldId,
        WorldSeed256 worldSeed,
        byte[] configDigest,
        CoreSnapshotOwnerAuthorityV1 detailAuthority,
        CoreSnapshotOwnerAuthorityV1 registryAuthority,
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
            new WorldStateHeaderV1(
                worldId,
                step,
                SHA256.HashData(worldSeed.ToBytes()),
                1,
                1,
                1,
                previousStateDigest),
            new OrderedPartitionDirectoryV1(partitions),
            WorldStateV1.EmptySubstate("core.scheduler-state"),
            WorldStateV1.EmptySubstate("core.operation-state"),
            new WorldSubstateRefV1(detailAuthority.Schema, detailAuthority.CanonicalDigest),
            new WorldSubstateRefV1(registryAuthority.Schema, registryAuthority.CanonicalDigest),
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

    private static bool Same(WorldSubstateRefV1 left, WorldSubstateRefV1 right)
        => left.Schema == right.Schema && CryptographicOperations.FixedTimeEquals(left.CanonicalDigest, right.CanonicalDigest);

    private static async Task RequireRejectedAsync(
        Func<Task<RunningSnapshotCutV1?>> action,
        string expectedMessage,
        string failureMessage)
    {
        var rejected = false;
        try { _ = await action(); }
        catch (InvalidDataException ex) when (ex.Message == expectedMessage) { rejected = true; }
        Require(rejected, failureMessage);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class TestOwnerMaterial : IFrozenCoreSnapshotOwnerMaterialV1
    {
        private readonly string _schemaId;
        private readonly byte[] _material;

        public TestOwnerMaterial(string sectionId, ulong basisStep, string schemaId, byte[] material)
        {
            SectionId = sectionId;
            BasisStep = basisStep;
            _schemaId = schemaId;
            _material = material.ToArray();
        }

        public string SectionId { get; }
        public ulong BasisStep { get; }

        public CoreSnapshotOwnerAuthorityV1 RecomputeAuthority() => Compute(_schemaId, _material);

        public static CoreSnapshotOwnerAuthorityV1 Compute(string schemaId, ReadOnlySpan<byte> material)
        {
            var frozen = material.ToArray();
            var digest = HashSuite.DomainHash("mv.test-core-snapshot-owner.v1", writer =>
            {
                writer.WriteMapStart(2);
                writer.WriteUnsigned(0); writer.WriteAsciiText(schemaId);
                writer.WriteUnsigned(1); writer.WriteBytes(frozen);
            });
            return new CoreSnapshotOwnerAuthorityV1(new SchemaRefV1(schemaId), digest);
        }
    }
}
