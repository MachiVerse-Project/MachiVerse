using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;

internal static class Qa04AcceptedOperationLossGuardSqliteSmoke
{
    internal static async Task RunAsync()
    {
        var worldId = OpaqueId128.Parse("0000000000000000000000000000ac01");
        var operationId = OpaqueId128.Parse("0000000000000000000000000000ac02");
        var rejectedId = OpaqueId128.Parse("0000000000000000000000000000ac03");
        var missingId = OpaqueId128.Parse("0000000000000000000000000000ac04");
        var seed = new WorldSeed256(new byte[32]);
        var configDigest = SHA256.HashData("qa04-operation-loss-sqlite-config"u8);
        var operationDigest = SHA256.HashData("qa04-operation-loss-sqlite-payload"u8);
        var rejectedDigest = SHA256.HashData("qa04-operation-loss-sqlite-rejected"u8);
        var expectation = new Qa04AcceptedOperationExpectationV1(operationId, operationDigest);
        var root = Path.Combine(Path.GetTempPath(), "machiverse-qa04-operation-loss-" + Guid.NewGuid().ToString("N"));

        try
        {
            var paths = PersistenceLayout.Resolve(root, worldId, 1);
            PersistenceLayout.EnsureGenerationDirectories(paths);
            await PersistenceLayout.WriteCurrentAsync(paths, 1).ConfigureAwait(false);

            var genesis = Record(
                worldId,
                sequence: 1,
                previousDigest: new byte[32],
                recordType: "world.genesis.v1",
                schemaId: "core.world-genesis.v1",
                writer =>
                {
                    writer.WriteMapStart(2);
                    writer.WriteUnsigned(0); writer.WriteBytes(worldId.ToBytes());
                    writer.WriteUnsigned(1); writer.WriteBytes(seed.ToBytes());
                });
            var initialContinuity = HistoryIntegrity.ComputeGenesisContinuityToken(worldId, genesis.RecordDigest);

            await using (var store = await SqlitePersistenceStore.OpenOrCreateAsync(paths).ConfigureAwait(false))
            {
                await store.InitializeWorldMetadataAsync(
                    new WorldPersistenceMetadataSeed(
                        worldId,
                        PersistenceGeneration: 1,
                        seed,
                        initialContinuity,
                        ConfigGeneration: 1,
                        configDigest,
                        MasterGeneration: 1),
                    genesis).ConfigureAwait(false);

                var acceptedHistory = Record(
                    worldId,
                    sequence: 2,
                    previousDigest: genesis.RecordDigest,
                    recordType: "operation.accepted.v1",
                    schemaId: "persistence.operation-accepted",
                    writer =>
                    {
                        writer.WriteMapStart(2);
                        writer.WriteUnsigned(0); writer.WriteBytes(operationId.ToBytes());
                        writer.WriteUnsigned(1); writer.WriteBytes(operationDigest);
                    });
                var accepted = await store.PersistAcceptedOperationAsync(
                    operationId,
                    operationDigest,
                    acceptedHistory).ConfigureAwait(false);
                Require(accepted.Status == DurableAcceptanceStatus.Accepted && accepted.AcceptedSequence == 2,
                    "QA-04 SQLite fixture must cross the durable ACCEPTED boundary.");
                RequireReceipt(
                    await Qa04AcceptedOperationLossGuardV1.ValidateStoreAsync([expectation], store).ConfigureAwait(false),
                    accepted: 1,
                    scheduled: 0,
                    terminal: 0,
                    "ACCEPTED");

                var orderKey = new SameStepOrderKey(
                    phase: 1,
                    domainRank: 50,
                    conflictScopeDigest: SHA256.HashData("qa04-operation-loss-scope"u8),
                    semanticPriority: 0,
                    intentId: operationId);
                var scheduledHistory = Record(
                    worldId,
                    sequence: 3,
                    previousDigest: acceptedHistory.RecordDigest,
                    recordType: "operation.scheduled.v1",
                    schemaId: "persistence.operation-scheduled",
                    writer =>
                    {
                        writer.WriteMapStart(3);
                        writer.WriteUnsigned(0); writer.WriteBytes(operationId.ToBytes());
                        writer.WriteUnsigned(1); writer.WriteUnsigned(0);
                        writer.WriteUnsigned(2); writer.WriteBytes(orderKey.ToDatabaseBytes());
                    });
                var scheduled = await store.PersistScheduledOperationAsync(
                    operationId,
                    effectiveStep: 0,
                    orderKey,
                    scheduledHistory).ConfigureAwait(false);
                Require(scheduled.Status == DurableSchedulingStatus.Scheduled && scheduled.EffectiveStep == 0,
                    "QA-04 SQLite fixture must cross the durable SCHEDULED boundary.");
                RequireReceipt(
                    await Qa04AcceptedOperationLossGuardV1.ValidateStoreAsync([expectation], store).ConfigureAwait(false),
                    accepted: 0,
                    scheduled: 1,
                    terminal: 0,
                    "SCHEDULED");

                var transitionHistory = Record(
                    worldId,
                    sequence: 4,
                    previousDigest: scheduledHistory.RecordDigest,
                    recordType: "transition.committed.v1",
                    schemaId: "persistence.transition-committed",
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
                    transitionHistory.RecordDigest);
                await store.PersistTransitionCommitAsync(
                    effectiveStep: 0,
                    resultingStep: 1,
                    resultingContinuity,
                    activeConfigGeneration: 1,
                    configDigest,
                    transitionHistory,
                    [new TerminalOperationCommit(operationId, 1, "operation.succeeded", [0xaa])]).ConfigureAwait(false);
                RequireReceipt(
                    await Qa04AcceptedOperationLossGuardV1.ValidateStoreAsync([expectation], store).ConfigureAwait(false),
                    accepted: 0,
                    scheduled: 0,
                    terminal: 1,
                    "TERMINAL");

                var directTerminalHistory = Record(
                    worldId,
                    sequence: 5,
                    previousDigest: transitionHistory.RecordDigest,
                    recordType: "operation.terminal.v1",
                    schemaId: "persistence.operation-terminal",
                    writer =>
                    {
                        writer.WriteMapStart(3);
                        writer.WriteUnsigned(0); writer.WriteBytes(rejectedId.ToBytes());
                        writer.WriteUnsigned(1); writer.WriteBytes(rejectedDigest);
                        writer.WriteUnsigned(2); writer.WriteAsciiText("world.deadline-exceeded");
                    });
                var direct = await store.PersistRejectedUnseenOperationAsync(
                    rejectedId,
                    rejectedDigest,
                    terminalStatus: 6,
                    resultCode: "world.deadline-exceeded",
                    directTerminalHistory,
                    richResultPayload: [0x10]).ConfigureAwait(false);
                Require(direct.State.AcceptedSequence is null,
                    "Direct UNSEEN -> TERMINAL fixture must remain outside accepted custody.");
                RequireReceipt(
                    await Qa04AcceptedOperationLossGuardV1.ValidateStoreAsync([expectation], store).ConfigureAwait(false),
                    accepted: 0,
                    scheduled: 0,
                    terminal: 1,
                    "TERMINAL with unrelated direct reject");

                await ExpectFailureAsync(
                    () => Qa04AcceptedOperationLossGuardV1.ValidateStoreAsync(
                        [new Qa04AcceptedOperationExpectationV1(missingId, operationDigest)],
                        store),
                    "qa04.operation-loss.accepted-operation-missing").ConfigureAwait(false);
            }

            await using (var recovered = await SqlitePersistenceStore.OpenOrCreateAsync(paths).ConfigureAwait(false))
            {
                RequireReceipt(
                    await Qa04AcceptedOperationLossGuardV1.ValidateStoreAsync([expectation], recovered).ConfigureAwait(false),
                    accepted: 0,
                    scheduled: 0,
                    terminal: 1,
                    "restart/recovery");

                var rejected = await recovered.ReadOperationStateAsync(rejectedId).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Direct terminal fixture disappeared after restart.");
                Require(rejected.AcceptedSequence is null && rejected.Lifecycle == DurableOperationLifecycleV1.TerminalDurable,
                    "Direct reject must survive restart without becoming accepted custody.");
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
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

    private static void RequireReceipt(
        Qa04AcceptedOperationLossGuardReceiptV1 receipt,
        int accepted,
        int scheduled,
        int terminal,
        string phase)
    {
        Require(receipt.Passed &&
                receipt.ExpectedAcceptedCount == 1 &&
                receipt.AcceptedDurableCount == accepted &&
                receipt.ScheduledDurableCount == scheduled &&
                receipt.TerminalDurableCount == terminal,
            $"QA-04 accepted Operation loss guard SQLite receipt mismatch at {phase}.");
    }

    private static async Task ExpectFailureAsync(Func<Task> action, string expectedCode)
    {
        try
        {
            await action().ConfigureAwait(false);
            throw new InvalidOperationException($"Expected failure was not raised: {expectedCode}");
        }
        catch (InvalidDataException ex) when (ex.Message == expectedCode)
        {
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
