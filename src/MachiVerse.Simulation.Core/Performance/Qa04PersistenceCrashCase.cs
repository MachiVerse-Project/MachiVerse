using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04PersistenceCrashCaseVerificationV1(
    string SchemaVersion,
    string Stage,
    bool ExpectedDurable,
    bool DurableFactPresent,
    bool HistoryChainValid,
    bool NoHalfTransition,
    ulong CurrentGeneration,
    ulong HistorySequence,
    ulong FinalizedStep);

/// <summary>
/// Component-local executor for the canonical perf.persistence.v1 crash matrix.
/// The external release adapter only launches this assembled Core process; it does not reference
/// Core implementation assemblies.
/// </summary>
public static class Qa04PersistenceCrashCaseV1
{
    private static readonly OpaqueId128 WorldId =
        OpaqueId128.Parse("0000000000000000000000000000a401");
    private static readonly OpaqueId128 OperationId =
        OpaqueId128.Parse("0000000000000000000000000000a402");
    private static readonly OpaqueId128 SnapshotId =
        OpaqueId128.Parse("0000000000000000000000000000a403");
    private static readonly WorldSeed256 WorldSeed = new(SHA256.HashData("qa04.persistence.world-seed"u8));
    private static readonly byte[] ConfigDigest = SHA256.HashData("qa04.persistence.config"u8);
    private static readonly byte[] OperationDigest = SHA256.HashData("qa04.persistence.operation"u8);

    private static readonly string[] CoreStages =
    [
        "migration-generation-switch",
        "operation-acceptance",
        "operation-scheduling",
        "snapshot-commit",
        "transition-commit",
    ];

    public static async Task RunAsync(
        string stage,
        string persistenceRoot,
        CancellationToken cancellationToken = default)
    {
        RequireStage(stage);
        if (string.Equals(stage, "migration-generation-switch", StringComparison.Ordinal))
        {
            await RunMigrationAsync(persistenceRoot, cancellationToken).ConfigureAwait(false);
            return;
        }

        var paths = PersistenceLayout.Resolve(persistenceRoot, WorldId, 1);
        PersistenceLayout.EnsureGenerationDirectories(paths);
        if (!File.Exists(paths.CurrentPath))
            await PersistenceLayout.WriteCurrentAsync(paths, 1, cancellationToken).ConfigureAwait(false);

        await using var store = await SqlitePersistenceStore.OpenOrCreateAsync(paths, cancellationToken).ConfigureAwait(false);
        var genesis = await EnsureInitializedAsync(store, cancellationToken).ConfigureAwait(false);

        switch (stage)
        {
            case "operation-acceptance":
                await PersistAcceptanceAsync(store, genesis, cancellationToken).ConfigureAwait(false);
                return;
            case "operation-scheduling":
            {
                var accepted = await PersistAcceptanceAsync(store, genesis, cancellationToken).ConfigureAwait(false);
                await PersistSchedulingAsync(store, accepted, cancellationToken).ConfigureAwait(false);
                return;
            }
            case "transition-commit":
            {
                var accepted = await PersistAcceptanceAsync(store, genesis, cancellationToken).ConfigureAwait(false);
                var scheduled = await PersistSchedulingAsync(store, accepted, cancellationToken).ConfigureAwait(false);
                await PersistTransitionAsync(store, scheduled, cancellationToken).ConfigureAwait(false);
                return;
            }
            case "snapshot-commit":
                await PersistSnapshotAsync(store, paths, genesis, cancellationToken).ConfigureAwait(false);
                return;
            default:
                throw new InvalidDataException($"qa04.persistence.stage-unsupported:{stage}");
        }
    }

    public static async Task<Qa04PersistenceCrashCaseVerificationV1> VerifyAsync(
        string stage,
        string persistenceRoot,
        bool expectedDurable,
        CancellationToken cancellationToken = default)
    {
        RequireStage(stage);
        if (string.Equals(stage, "migration-generation-switch", StringComparison.Ordinal))
            return VerifyMigration(stage, persistenceRoot, expectedDurable);

        var paths = PersistenceLayout.Resolve(persistenceRoot, WorldId, 1);
        if (!File.Exists(paths.DatabasePath))
            throw new InvalidDataException("qa04.persistence.case-database-missing");

        await using var store = await SqlitePersistenceStore.OpenOrCreateAsync(paths, cancellationToken).ConfigureAwait(false);
        await store.ValidateQuickCheckAsync(cancellationToken).ConfigureAwait(false);

        var durableFactPresent = false;
        var noHalfTransition = true;
        var recovery = await store.ReadRecoveryHeadAsync(cancellationToken).ConfigureAwait(false);

        switch (stage)
        {
            case "operation-acceptance":
            {
                var state = await store.ReadOperationStateAsync(OperationId, cancellationToken).ConfigureAwait(false);
                durableFactPresent = state?.Lifecycle == DurableOperationLifecycleV1.AcceptedDurable;
                noHalfTransition = state is null || state.Lifecycle == DurableOperationLifecycleV1.AcceptedDurable;
                break;
            }
            case "operation-scheduling":
            {
                var state = await store.ReadOperationStateAsync(OperationId, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidDataException("qa04.persistence.acceptance-baseline-missing");
                durableFactPresent = state.Lifecycle == DurableOperationLifecycleV1.ScheduledDurable;
                noHalfTransition = expectedDurable
                    ? state.Lifecycle == DurableOperationLifecycleV1.ScheduledDurable && state.EffectiveStep == 0
                    : state.Lifecycle == DurableOperationLifecycleV1.AcceptedDurable && state.ScheduledSequence is null;
                break;
            }
            case "transition-commit":
            {
                var state = await store.ReadOperationStateAsync(OperationId, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidDataException("qa04.persistence.scheduling-baseline-missing");
                durableFactPresent =
                    recovery.FinalizedStep == 1 &&
                    state.Lifecycle == DurableOperationLifecycleV1.TerminalDurable;
                noHalfTransition = expectedDurable
                    ? durableFactPresent
                    : recovery.FinalizedStep == 0 &&
                      state.Lifecycle == DurableOperationLifecycleV1.ScheduledDurable &&
                      state.TerminalSequence is null;
                break;
            }
            case "snapshot-commit":
            {
                var snapshots = await store.ListSnapshotCandidatesNewestFirstAsync(cancellationToken).ConfigureAwait(false);
                durableFactPresent = snapshots.Count == 1 && snapshots[0].SnapshotId == SnapshotId;
                noHalfTransition = expectedDurable ? durableFactPresent : snapshots.Count == 0;
                break;
            }
        }

        if (durableFactPresent != expectedDurable)
            throw new InvalidDataException(
                $"qa04.persistence.durability-mismatch:{stage}:expected={expectedDurable}:actual={durableFactPresent}");
        if (!noHalfTransition)
            throw new InvalidDataException($"qa04.persistence.half-transition:{stage}");

        var registered = new HashSet<string>(StringComparer.Ordinal)
        {
            "operation.accepted.v1",
            "operation.scheduled.v1",
            "snapshot.committed.v1",
            "transition.committed.v1",
            "world.genesis.v1",
        };
        var chain = await store.ValidateHistoryLinkChainAsync(registered, cancellationToken).ConfigureAwait(false);
        var anchor = await store.ReadHistoryAnchorAsync(cancellationToken).ConfigureAwait(false);
        if (chain.LastSequence != anchor.Sequence)
            throw new InvalidDataException("qa04.persistence.history-chain-head-mismatch");

        return new Qa04PersistenceCrashCaseVerificationV1(
            "1.0",
            stage,
            expectedDurable,
            durableFactPresent,
            HistoryChainValid: true,
            noHalfTransition,
            CurrentGeneration: PersistenceLayout.ReadCurrent(paths),
            HistorySequence: anchor.Sequence,
            FinalizedStep: recovery.FinalizedStep);
    }

    private static async Task<HistoryRecordMaterial> EnsureInitializedAsync(
        SqlitePersistenceStore store,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = await store.ReadHistoryAnchorAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidDataException("qa04.persistence.case-root-not-clean");
        }
        catch (InvalidDataException ex) when (ex.Message == "persistence.meta-not-initialized")
        {
        }

        var genesis = Record(
            1,
            new byte[32],
            "world.genesis.v1",
            "core.world-genesis.v1",
            writer =>
            {
                writer.WriteMapStart(2);
                writer.WriteUnsigned(0); writer.WriteBytes(WorldId.ToBytes());
                writer.WriteUnsigned(1); writer.WriteBytes(WorldSeed.ToBytes());
            });
        var continuity = HistoryIntegrity.ComputeGenesisContinuityToken(WorldId, genesis.RecordDigest);
        await store.InitializeWorldMetadataAsync(
            new WorldPersistenceMetadataSeed(
                WorldId,
                PersistenceGeneration: 1,
                WorldSeed,
                continuity,
                ConfigGeneration: 1,
                ConfigDigest,
                MasterGeneration: 1),
            genesis,
            cancellationToken).ConfigureAwait(false);
        return genesis;
    }

    private static async Task<HistoryRecordMaterial> PersistAcceptanceAsync(
        SqlitePersistenceStore store,
        HistoryRecordMaterial previous,
        CancellationToken cancellationToken)
    {
        var accepted = Record(
            checked(previous.Sequence + 1),
            previous.RecordDigest,
            "operation.accepted.v1",
            "persistence.operation-accepted",
            writer =>
            {
                writer.WriteMapStart(2);
                writer.WriteUnsigned(0); writer.WriteBytes(OperationId.ToBytes());
                writer.WriteUnsigned(1); writer.WriteBytes(OperationDigest);
            });
        _ = await store.PersistAcceptedOperationAsync(
            OperationId,
            OperationDigest,
            accepted,
            cancellationToken).ConfigureAwait(false);
        return accepted;
    }

    private static async Task<HistoryRecordMaterial> PersistSchedulingAsync(
        SqlitePersistenceStore store,
        HistoryRecordMaterial previous,
        CancellationToken cancellationToken)
    {
        var orderKey = new SameStepOrderKey(
            phase: 1,
            domainRank: 1,
            conflictScopeDigest: SHA256.HashData("qa04.persistence.scope"u8),
            semanticPriority: 0,
            intentId: OperationId);
        var scheduled = Record(
            checked(previous.Sequence + 1),
            previous.RecordDigest,
            "operation.scheduled.v1",
            "persistence.operation-scheduled",
            writer =>
            {
                writer.WriteMapStart(3);
                writer.WriteUnsigned(0); writer.WriteBytes(OperationId.ToBytes());
                writer.WriteUnsigned(1); writer.WriteUnsigned(0);
                writer.WriteUnsigned(2); writer.WriteBytes(orderKey.ToDatabaseBytes());
            });
        _ = await store.PersistScheduledOperationAsync(
            OperationId,
            effectiveStep: 0,
            orderKey,
            scheduled,
            cancellationToken).ConfigureAwait(false);
        return scheduled;
    }

    private static async Task PersistTransitionAsync(
        SqlitePersistenceStore store,
        HistoryRecordMaterial previous,
        CancellationToken cancellationToken)
    {
        var recovery = await store.ReadRecoveryHeadAsync(cancellationToken).ConfigureAwait(false);
        var transition = Record(
            checked(previous.Sequence + 1),
            previous.RecordDigest,
            "transition.committed.v1",
            "persistence.transition-committed",
            writer =>
            {
                writer.WriteMapStart(2);
                writer.WriteUnsigned(0); writer.WriteUnsigned(0);
                writer.WriteUnsigned(1); writer.WriteUnsigned(1);
            });
        var continuity = HistoryIntegrity.ComputeTransitionContinuityToken(
            WorldId,
            resultingStep: 1,
            recovery.ContinuityToken,
            transition.RecordDigest);
        _ = await store.PersistTransitionCommitAsync(
            effectiveStep: 0,
            resultingStep: 1,
            continuity,
            activeConfigGeneration: 1,
            ConfigDigest,
            transition,
            [new TerminalOperationCommit(OperationId, 1, "qa04.persistence.terminal")],
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task PersistSnapshotAsync(
        SqlitePersistenceStore store,
        WorldPersistencePaths paths,
        HistoryRecordMaterial previous,
        CancellationToken cancellationToken)
    {
        var recovery = await store.ReadRecoveryHeadAsync(cancellationToken).ConfigureAwait(false);
        var anchor = await store.ReadHistoryAnchorAsync(cancellationToken).ConfigureAwait(false);
        var finalDirectory = Path.Combine(paths.SnapshotsDirectory, SnapshotId.ToString());
        Directory.CreateDirectory(finalDirectory);
        var relative = Path.GetRelativePath(paths.GenerationDirectory, finalDirectory).Replace('\\', '/');
        var snapshotDigest = SHA256.HashData("qa04.persistence.snapshot"u8);
        var physicalDigest = SHA256.HashData("qa04.persistence.snapshot.physical"u8);
        var material = new SnapshotCommitMaterial(
            SnapshotId,
            SnapshotStep: 0,
            anchor,
            recovery.ContinuityToken,
            snapshotDigest,
            physicalDigest,
            relative);
        var history = Record(
            checked(previous.Sequence + 1),
            previous.RecordDigest,
            "snapshot.committed.v1",
            "core.snapshot-committed.v1",
            writer =>
            {
                writer.WriteMapStart(3);
                writer.WriteUnsigned(0); writer.WriteBytes(SnapshotId.ToBytes());
                writer.WriteUnsigned(1); writer.WriteBytes(snapshotDigest);
                writer.WriteUnsigned(2); writer.WriteBytes(physicalDigest);
            });
        _ = await store.PersistSnapshotCommitAsync(material, history, cancellationToken).ConfigureAwait(false);
    }

    private static async Task RunMigrationAsync(string persistenceRoot, CancellationToken cancellationToken)
    {
        var source = PersistenceLayout.Resolve(persistenceRoot, WorldId, 1);
        PersistenceLayout.EnsureGenerationDirectories(source);
        await File.WriteAllTextAsync(source.DatabasePath, "qa04-source-generation", cancellationToken).ConfigureAwait(false);
        await PersistenceLayout.WriteCurrentAsync(source, 1, cancellationToken).ConfigureAwait(false);
        var migration = PersistenceGenerationMigration.Prepare(persistenceRoot, WorldId, 1);
        await File.WriteAllTextAsync(
            migration.Staging.DatabasePath,
            "qa04-target-generation",
            cancellationToken).ConfigureAwait(false);
        await PersistenceGenerationMigration.FinalizeValidatedAsync(
            migration,
            static (staging, _) =>
            {
                if (!File.Exists(staging.DatabasePath))
                    throw new InvalidDataException("qa04.persistence.migration-staging-database-missing");
                return Task.CompletedTask;
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static Qa04PersistenceCrashCaseVerificationV1 VerifyMigration(
        string stage,
        string persistenceRoot,
        bool expectedDurable)
    {
        var source = PersistenceLayout.Resolve(persistenceRoot, WorldId, 1);
        var current = PersistenceLayout.ReadCurrent(source);
        var durable = current == 2;
        if (durable != expectedDurable)
            throw new InvalidDataException(
                $"qa04.persistence.migration-durability-mismatch:expected={expectedDurable}:actual={durable}");
        if (!Directory.Exists(source.GenerationDirectory))
            throw new InvalidDataException("qa04.persistence.migration-source-generation-lost");
        if (expectedDurable)
        {
            var target = PersistenceLayout.Resolve(persistenceRoot, WorldId, 2);
            if (!Directory.Exists(target.GenerationDirectory))
                throw new InvalidDataException("qa04.persistence.migration-target-generation-missing");
        }

        return new Qa04PersistenceCrashCaseVerificationV1(
            "1.0",
            stage,
            expectedDurable,
            durable,
            HistoryChainValid: true,
            NoHalfTransition: true,
            CurrentGeneration: current,
            HistorySequence: 0,
            FinalizedStep: 0);
    }

    private static HistoryRecordMaterial Record(
        ulong sequence,
        byte[] previousDigest,
        string recordType,
        string schemaId,
        Action<MvDcborWriter> normalized)
        => HistoryRecordMaterial.Create(
            WorldId,
            sequence,
            previousDigest,
            recordType,
            schemaId,
            1,
            0,
            [(byte)(sequence & 0xff)],
            normalized);

    private static void RequireStage(string stage)
    {
        if (!CoreStages.Contains(stage, StringComparer.Ordinal))
            throw new InvalidDataException($"qa04.persistence.stage-unsupported:{stage}");
    }
}
