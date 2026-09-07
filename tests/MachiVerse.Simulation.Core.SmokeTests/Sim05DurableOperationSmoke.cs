using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;

internal static class Sim05DurableOperationSmoke
{
    internal static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "machiverse-sim05-" + Guid.NewGuid().ToString("N"));
        var worldId = OpaqueId128.Parse("00000000000000000000000000000510");
        var paths = PersistenceLayout.Resolve(root, worldId, 1);
        try
        {
            PersistenceLayout.EnsureGenerationDirectories(paths);
            await PersistenceLayout.WriteCurrentAsync(paths, 1);

            var seed = new WorldSeed256(new byte[32]);
            var configDigest = SHA256.HashData("sim05-config"u8);
            var genesis = HistoryRecordMaterial.Create(
                worldId,
                1,
                new byte[32],
                "world.genesis.v1",
                "core.world-genesis.v1",
                1,
                0,
                [0x01],
                writer =>
                {
                    writer.WriteMapStart(2);
                    writer.WriteUnsigned(0); writer.WriteBytes(worldId.ToBytes());
                    writer.WriteUnsigned(1); writer.WriteBytes(seed.ToBytes());
                });
            var initialContinuity = HistoryIntegrity.ComputeGenesisContinuityToken(worldId, genesis.RecordDigest);

            await using (var store = await SqlitePersistenceStore.OpenOrCreateAsync(paths))
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
                    genesis);

                var operationId = OpaqueId128.Parse("00000000000000000000000000000511");
                var operationDigest = SHA256.HashData("sim05-operation"u8);
                var acceptedHistory = Record(
                    worldId, 2, genesis.RecordDigest,
                    "operation.accepted.v1", "persistence.operation-accepted",
                    writer =>
                    {
                        writer.WriteMapStart(2);
                        writer.WriteUnsigned(0); writer.WriteBytes(operationId.ToBytes());
                        writer.WriteUnsigned(1); writer.WriteBytes(operationDigest);
                    });

                var accepted = await store.PersistAcceptedOperationAsync(operationId, operationDigest, acceptedHistory);
                Require(accepted.Status == DurableAcceptanceStatus.Accepted && accepted.AcceptedSequence == 2,
                    "ACCEPTED must only become observable after the durable acceptance record commits.");
                var duplicateAccepted = await store.PersistAcceptedOperationAsync(operationId, operationDigest, acceptedHistory);
                Require(duplicateAccepted.Status == DurableAcceptanceStatus.Duplicate && duplicateAccepted.AcceptedSequence == 2,
                    "Same-id/same-digest retry must converge to the original durable acceptance.");

                var mismatchRejected = false;
                try
                {
                    _ = await store.PersistAcceptedOperationAsync(
                        operationId,
                        SHA256.HashData("sim05-operation-mismatch"u8),
                        acceptedHistory);
                }
                catch (InvalidDataException ex) when (ex.Message == "protocol.operation-payload-mismatch")
                {
                    mismatchRejected = true;
                }
                Require(mismatchRejected, "Same OperationId with a different immutable digest must reject.");

                var schedulingPolicy = new OperationSchedulingPolicyV1(1, 0, null, 0, OperationLatePolicyV1.Reject);
                var admission = new OperationSchedulingAdmissionV1(0, 1, null, null);
                var decision = OperationSchedulingPlannerV1.Plan(
                    schedulingPolicy,
                    admission,
                    new OperationSchedulingBarrierV1(0, false, null));
                Require(decision.Kind == OperationSchedulingDecisionKindV1.Scheduled && decision.EffectiveStep == 0,
                    "Fixture Operation must schedule for transition 0.");

                var orderKey = new SameStepOrderKey(
                    phase: 1,
                    domainRank: 50,
                    conflictScopeDigest: SHA256.HashData("sim05-scope"u8),
                    semanticPriority: 0,
                    intentId: operationId);
                var scheduledHistory = Record(
                    worldId, 3, acceptedHistory.RecordDigest,
                    "operation.scheduled.v1", "persistence.operation-scheduled",
                    writer =>
                    {
                        writer.WriteMapStart(3);
                        writer.WriteUnsigned(0); writer.WriteBytes(operationId.ToBytes());
                        writer.WriteUnsigned(1); writer.WriteUnsigned(0);
                        writer.WriteUnsigned(2); writer.WriteBytes(orderKey.ToDatabaseBytes());
                    });
                var scheduled = await store.PersistScheduledOperationAsync(
                    operationId, 0, orderKey, scheduledHistory);
                Require(scheduled.Status == DurableSchedulingStatus.Scheduled && scheduled.EffectiveStep == 0,
                    "Effective Step must become authoritative only after scheduled durability.");
                var duplicateScheduled = await store.PersistScheduledOperationAsync(
                    operationId, 0, orderKey, scheduledHistory);
                Require(duplicateScheduled.Status == DurableSchedulingStatus.Duplicate && duplicateScheduled.EffectiveStep == 0,
                    "Scheduled retry must converge without changing effective Step.");

                var transitionHistory = Record(
                    worldId, 4, scheduledHistory.RecordDigest,
                    "transition.committed.v1", "persistence.transition-committed",
                    writer =>
                    {
                        writer.WriteMapStart(2);
                        writer.WriteUnsigned(0); writer.WriteUnsigned(0);
                        writer.WriteUnsigned(1); writer.WriteBytes(operationId.ToBytes());
                    });
                var resultingContinuity = HistoryIntegrity.ComputeTransitionContinuityToken(
                    worldId, 1, initialContinuity, transitionHistory.RecordDigest);
                await store.PersistTransitionCommitAsync(
                    effectiveStep: 0,
                    resultingStep: 1,
                    resultingContinuity,
                    activeConfigGeneration: 1,
                    configDigest,
                    transitionHistory,
                    [new TerminalOperationCommit(operationId, 1, "operation.succeeded", [0xaa, 0xbb])]);

                var terminal = await store.ReadOperationStateAsync(operationId)
                    ?? throw new InvalidOperationException("Terminal durable Operation disappeared.");
                Require(terminal.Lifecycle == DurableOperationLifecycleV1.TerminalDurable &&
                        terminal.EffectiveStep == 0 && terminal.TerminalSequence == 4 &&
                        terminal.ResultCode == "operation.succeeded",
                    "Transition terminal tombstone must preserve effective Step and result identity.");

                var rejectedId = OpaqueId128.Parse("00000000000000000000000000000512");
                var rejectedDigest = SHA256.HashData("sim05-rejected"u8);
                var terminalHistory = Record(
                    worldId, 5, transitionHistory.RecordDigest,
                    "operation.terminal.v1", "persistence.operation-terminal",
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
                    terminalHistory,
                    richResultPayload: [0x10]);
                Require(direct.Status == DirectTerminalPersistenceStatusV1.Terminalized &&
                        direct.State.AcceptedSequence is null && direct.State.EffectiveStep is null,
                    "Non-applied reject must support UNSEEN -> TERMINAL_DURABLE without scheduling.");
                var duplicateDirect = await store.PersistRejectedUnseenOperationAsync(
                    rejectedId,
                    rejectedDigest,
                    terminalStatus: 6,
                    resultCode: "world.deadline-exceeded",
                    terminalHistory,
                    richResultPayload: [0x10]);
                Require(duplicateDirect.Status == DirectTerminalPersistenceStatusV1.Duplicate &&
                        duplicateDirect.State.TerminalSequence == 5,
                    "Terminal retry must converge to the existing tombstone rather than append history.");
            }

            await using (var recovered = await SqlitePersistenceStore.OpenOrCreateAsync(paths))
            {
                var operationId = OpaqueId128.Parse("00000000000000000000000000000511");
                var restored = await recovered.ReadOperationStateAsync(operationId)
                    ?? throw new InvalidOperationException("Operation dedup authority was not recovered.");
                Require(restored.Lifecycle == DurableOperationLifecycleV1.TerminalDurable && restored.EffectiveStep == 0,
                    "Recovery must not silently reassign a scheduled/terminal Operation effective Step.");

                var rejectedId = OpaqueId128.Parse("00000000000000000000000000000512");
                var rejected = await recovered.ReadOperationStateAsync(rejectedId)
                    ?? throw new InvalidOperationException("Direct terminal tombstone was not recovered.");
                Require(rejected.Lifecycle == DurableOperationLifecycleV1.TerminalDurable &&
                        rejected.AcceptedSequence is null && rejected.ResultCode == "world.deadline-exceeded",
                    "Direct reject tombstone must survive restart as dedup authority.");
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

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
