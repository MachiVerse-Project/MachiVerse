using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

internal static class Sim06DurableFinalizationSmoke
{
    internal static async Task RunAsync()
    {
        await VerifySqliteCommitBoundaryAsync();
        await VerifyCrashBeforeCommitBoundaryAsync();
    }

    private static async Task VerifySqliteCommitBoundaryAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "machiverse-sim06-finalize-" + Guid.NewGuid().ToString("N"));
        var worldId = OpaqueId128.Parse("000000000000000000000000000006a0");
        var operationId = OpaqueId128.Parse("000000000000000000000000000006a1");
        var paths = PersistenceLayout.Resolve(root, worldId, 1);
        try
        {
            PersistenceLayout.EnsureGenerationDirectories(paths);
            await PersistenceLayout.WriteCurrentAsync(paths, 1);

            var seed = new WorldSeed256(new byte[32]);
            var configDigest = SHA256.HashData("sim06-config"u8);
            var genesis = Record(
                worldId,
                1,
                new byte[32],
                "world.genesis.v1",
                "core.world-genesis.v1",
                writer =>
                {
                    writer.WriteMapStart(2);
                    writer.WriteUnsigned(0); writer.WriteBytes(worldId.ToBytes());
                    writer.WriteUnsigned(1); writer.WriteBytes(seed.ToBytes());
                });
            var initialContinuity = HistoryIntegrity.ComputeGenesisContinuityToken(worldId, genesis.RecordDigest);

            await using var store = await SqlitePersistenceStore.OpenOrCreateAsync(paths);
            await store.InitializeWorldMetadataAsync(
                new WorldPersistenceMetadataSeed(
                    worldId,
                    PersistenceGeneration: 1,
                    seed,
                    initialContinuity,
                    ConfigGeneration: 1,
                    configDigest,
                    MasterGeneration: 1),
                genesis);

            var operationDigest = SHA256.HashData("sim06-operation"u8);
            var accepted = Record(
                worldId,
                2,
                genesis.RecordDigest,
                "operation.accepted.v1",
                "persistence.operation-accepted",
                writer =>
                {
                    writer.WriteMapStart(2);
                    writer.WriteUnsigned(0); writer.WriteBytes(operationId.ToBytes());
                    writer.WriteUnsigned(1); writer.WriteBytes(operationDigest);
                });
            await store.PersistAcceptedOperationAsync(operationId, operationDigest, accepted);

            var orderKey = new SameStepOrderKey(
                phase: 1,
                domainRank: 50,
                conflictScopeDigest: SHA256.HashData("sim06-order-scope"u8),
                semanticPriority: 0,
                intentId: operationId);
            var scheduled = Record(
                worldId,
                3,
                accepted.RecordDigest,
                "operation.scheduled.v1",
                "persistence.operation-scheduled",
                writer =>
                {
                    writer.WriteMapStart(3);
                    writer.WriteUnsigned(0); writer.WriteBytes(operationId.ToBytes());
                    writer.WriteUnsigned(1); writer.WriteUnsigned(0);
                    writer.WriteUnsigned(2); writer.WriteBytes(orderKey.ToDatabaseBytes());
                });
            await store.PersistScheduledOperationAsync(operationId, 0, orderKey, scheduled);

            var state = CreateWorldState(worldId, 0, configDigest);
            var scheduler = new OperationSchedulerStateV1(
                nextSchedulableStep: 0,
                freezeStep: null,
                [new ScheduledOperationRefV1(operationId, 0, orderKey)]);
            var frozen = StepInputFreezerV1.Freeze(state, scheduler);
            var candidate = EmptyCandidate(
                OpaqueId128.Parse("000000000000000000000000000006a2"),
                state,
                frozen);
            Require(!candidate.IsPublishable && scheduler.FreezeStep == 0,
                "Candidate must remain non-authoritative while transition durability is pending.");

            var transition = Record(
                worldId,
                4,
                scheduled.RecordDigest,
                "transition.committed.v1",
                "persistence.transition-committed",
                writer =>
                {
                    writer.WriteMapStart(2);
                    writer.WriteUnsigned(0); writer.WriteUnsigned(0);
                    writer.WriteUnsigned(1); writer.WriteBytes(operationId.ToBytes());
                });
            var resultingContinuity = HistoryIntegrity.ComputeTransitionContinuityToken(
                worldId,
                resultingStep: 1,
                initialContinuity,
                transition.RecordDigest);
            var material = new StepFinalizeMaterialV1(
                activeConfigGeneration: 1,
                configDigest,
                resultingContinuity,
                transition,
                [new TerminalOperationCommit(operationId, 1, "operation.succeeded")]);

            var finalizer = new StepFinalizationCoordinatorV1(new SqliteStepTransitionDurabilityV1(store));
            var receipt = await finalizer.FinalizeAsync(candidate, scheduler, material);
            Require(receipt.IsPublishable && receipt.ResultingStep == 1 && receipt.HistorySequence == 4,
                "Only the durable finalization receipt may be published as confirmed State(S+1).");
            Require(scheduler.FreezeStep is null && scheduler.NextSchedulableStep >= 1 && scheduler.ForEffectiveStep(0).Count == 0,
                "Scheduler must retire finalized Step only after durable transition commit.");

            var recovery = await store.ReadRecoveryHeadAsync();
            Require(recovery.FinalizedStep == 1 && recovery.ContinuityToken.SequenceEqual(resultingContinuity),
                "SQLite authoritative recovery head did not advance atomically with finalization.");
            var terminal = await store.ReadOperationStateAsync(operationId)
                ?? throw new InvalidOperationException("Finalized Operation disappeared from durable dedup authority.");
            Require(terminal.Lifecycle == DurableOperationLifecycleV1.TerminalDurable && terminal.TerminalSequence == 4,
                "Terminal Operation result must become durable in the same transition commit.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task VerifyCrashBeforeCommitBoundaryAsync()
    {
        var worldId = OpaqueId128.Parse("000000000000000000000000000006b0");
        var operationId = OpaqueId128.Parse("000000000000000000000000000006b1");
        var configDigest = SHA256.HashData("sim06-crash-config"u8);
        var orderKey = new SameStepOrderKey(
            phase: 1,
            domainRank: 50,
            conflictScopeDigest: SHA256.HashData("sim06-crash-scope"u8),
            semanticPriority: 0,
            intentId: operationId);
        var state = CreateWorldState(worldId, 7, configDigest);
        var scheduler = new OperationSchedulerStateV1(
            nextSchedulableStep: 7,
            freezeStep: null,
            [new ScheduledOperationRefV1(operationId, 7, orderKey)]);
        var frozen = StepInputFreezerV1.Freeze(state, scheduler);
        var candidate = EmptyCandidate(
            OpaqueId128.Parse("000000000000000000000000000006b2"),
            state,
            frozen);

        var transition = Record(
            worldId,
            99,
            new byte[32],
            "transition.committed.v1",
            "persistence.transition-committed",
            writer =>
            {
                writer.WriteMapStart(1);
                writer.WriteUnsigned(0); writer.WriteUnsigned(7);
            });
        var material = new StepFinalizeMaterialV1(
            activeConfigGeneration: 1,
            configDigest,
            SHA256.HashData("sim06-uncommitted-continuity"u8),
            transition,
            [new TerminalOperationCommit(operationId, 1, "operation.succeeded")]);
        var throwing = new ThrowBeforeCommitDurability();
        var finalizer = new StepFinalizationCoordinatorV1(throwing);

        var crashed = false;
        try
        {
            _ = await finalizer.FinalizeAsync(candidate, scheduler, material);
        }
        catch (IOException ex) when (ex.Message == "fixture.crash-before-commit")
        {
            crashed = true;
        }
        Require(crashed && throwing.CallCount == 1,
            "Crash-before-commit fixture did not interrupt the durable boundary.");
        Require(!candidate.IsPublishable,
            "A candidate must not become publishable when persistence never commits.");
        Require(scheduler.FreezeStep == 7 && scheduler.NextSchedulableStep == 8 && scheduler.ForEffectiveStep(7).Count == 1,
            "Crash-before-commit must keep State(S) scheduler authority frozen without retiring the Step bucket.");

        var missingCoverageRejected = false;
        try
        {
            var missing = new StepFinalizeMaterialV1(
                1,
                configDigest,
                SHA256.HashData("sim06-missing-coverage"u8),
                transition,
                Array.Empty<TerminalOperationCommit>());
            _ = await finalizer.FinalizeAsync(candidate, scheduler, missing);
        }
        catch (InvalidDataException ex) when (ex.Message == "step-finalize.terminal-operation-coverage-mismatch")
        {
            missingCoverageRejected = true;
        }
        Require(missingCoverageRejected && throwing.CallCount == 1,
            "Incomplete terminal coverage must fail before entering persistence.");
    }

    private static StepCandidateV1 EmptyCandidate(
        OpaqueId128 candidateId,
        WorldStateV1 state,
        FrozenStepInputV1 frozen)
    {
        var outputs = StandardDomainExecutionPlanV1.Create().Entries
            .Select(entry => new DomainCandidateOutputV1(entry.DomainToken, state.Header.Step))
            .ToArray();
        return StepCandidateV1.Build(
            candidateId,
            state,
            frozen,
            outputs,
            Array.Empty<ConflictGroupResolutionV1>());
    }

    private static WorldStateV1 CreateWorldState(OpaqueId128 worldId, ulong step, byte[] configDigest)
    {
        var partitions = StandardDomainPartitionRegistry.Entries.Select(entry => new PartitionStateRefV1(
            new PartitionStateHeaderV1(
                entry,
                revision: 1,
                basisStep: step,
                detailLevel: DetailLevelV1.D0Entity,
                itemCount: 0,
                canonicalDigest: SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(entry.PartitionId.Value)))));
        return new WorldStateV1(
            new WorldStateHeaderV1(
                worldId,
                step,
                worldSeedDigest: new byte[32],
                configGeneration: 1,
                masterGeneration: 1,
                rateGeneration: 1),
            new OrderedPartitionDirectoryV1(partitions),
            WorldStateV1.EmptySubstate("core.scheduler-state"),
            WorldStateV1.EmptySubstate("core.operation-state"),
            WorldStateV1.EmptySubstate("core.detail-state"),
            WorldStateV1.EmptySubstate("core.domain-registry-state"),
            configDigest);
    }

    private static HistoryRecordMaterial Record(
        OpaqueId128 worldId,
        ulong sequence,
        byte[] previousDigest,
        string recordType,
        string schemaId,
        Action<MvDcborWriter> normalized)
        => HistoryRecordMaterial.Create(
            worldId,
            sequence,
            previousDigest,
            recordType,
            schemaId,
            1,
            0,
            [(byte)(sequence & 0xff)],
            normalized);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class ThrowBeforeCommitDurability : IStepTransitionDurabilityV1
    {
        public int CallCount { get; private set; }

        public Task<DurableTransitionResult> CommitAsync(
            StepCandidateV1 candidate,
            StepFinalizeMaterialV1 material,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            throw new IOException("fixture.crash-before-commit");
        }
    }
}
