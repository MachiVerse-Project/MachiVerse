using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;

internal static class Sim05CoordinatorSmoke
{
    internal static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "machiverse-sim05-coordinator-" + Guid.NewGuid().ToString("N"));
        var worldId = OpaqueId128.Parse("00000000000000000000000000000520");
        var paths = PersistenceLayout.Resolve(root, worldId, 1);
        try
        {
            PersistenceLayout.EnsureGenerationDirectories(paths);
            await PersistenceLayout.WriteCurrentAsync(paths, 1);

            var seed = new WorldSeed256(new byte[32]);
            var configDigest = SHA256.HashData("sim05-coordinator-config"u8);
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
            var continuity = HistoryIntegrity.ComputeGenesisContinuityToken(worldId, genesis.RecordDigest);

            var policyGeneration1 = new OperationSchedulingPolicyV1(
                ownerConfigGeneration: 1,
                minLeadSteps: 2,
                defaultDeadlineWindowSteps: 20,
                graceSteps: 3,
                OperationLatePolicyV1.DeferWithinGrace);
            var policyGeneration2 = new OperationSchedulingPolicyV1(
                ownerConfigGeneration: 2,
                minLeadSteps: 9,
                defaultDeadlineWindowSteps: 20,
                graceSteps: 3,
                OperationLatePolicyV1.DeferWithinGrace);
            var policyHistory = new OperationSchedulingPolicyHistoryV1(
            [
                new OperationSchedulingPolicyHistoryEntryV1(policyGeneration1, 0, 100),
                new OperationSchedulingPolicyHistoryEntryV1(policyGeneration2, 100, null),
            ]);

            await using (var store = await SqlitePersistenceStore.OpenOrCreateAsync(paths))
            {
                await store.InitializeWorldMetadataAsync(
                    new WorldPersistenceMetadataSeed(
                        worldId,
                        PersistenceGeneration: 1,
                        seed,
                        continuity,
                        ConfigGeneration: 1,
                        configDigest,
                        MasterGeneration: 1),
                    genesis);

                var coordinator = new DurableOperationCoordinatorV1(store, policyHistory);
                var admission = new OperationSchedulingAdmissionV1(
                    admissionBasisStep: 50,
                    schedulingPolicyGeneration: 1,
                    requestedNotBeforeStep: null,
                    requestedDeadlineStep: null);
                var plan = coordinator.Plan(
                    admission,
                    new OperationSchedulingBarrierV1(NextSchedulableStep: 50, PauseActive: false, PauseBasisStep: null));
                Require(plan.CanonicalCandidateStep == 52,
                    "Coordinator must resolve the historical admission policy rather than a newer current policy.");

                RequireReject(
                    () => coordinator.Plan(
                        new OperationSchedulingAdmissionV1(150, 1, null, null),
                        new OperationSchedulingBarrierV1(150, false, null)),
                    "operation.scheduling-policy-not-effective-at-admission");
                var later = coordinator.Plan(
                    new OperationSchedulingAdmissionV1(150, 2, null, null),
                    new OperationSchedulingBarrierV1(150, false, null));
                Require(later.CanonicalCandidateStep == 159,
                    "Historical resolver must select generation 2 after its effective boundary.");

                var operationId = OpaqueId128.Parse("00000000000000000000000000000521");
                var digest = SHA256.HashData("sim05-coordinator-operation"u8);
                var acceptedHistory = Record(
                    worldId,
                    2,
                    genesis.RecordDigest,
                    "operation.accepted.v1",
                    "persistence.operation-accepted",
                    writer =>
                    {
                        writer.WriteMapStart(2);
                        writer.WriteUnsigned(0); writer.WriteBytes(operationId.ToBytes());
                        writer.WriteUnsigned(1); writer.WriteBytes(digest);
                    });

                var accepted = await coordinator.AcceptOrConvergeAsync(operationId, digest, acceptedHistory);
                Require(!accepted.Duplicate && accepted.Lifecycle == OperationLifecycleStateV1.AcceptedDurable && accepted.AcceptedSequence == 2,
                    "Coordinator must expose ACCEPTED only after the durable acceptance transaction.");
                var acceptedRetry = await coordinator.AcceptOrConvergeAsync(operationId, digest, acceptedHistory);
                Require(acceptedRetry.Duplicate && acceptedRetry.Lifecycle == OperationLifecycleStateV1.AcceptedDurable && acceptedRetry.AcceptedSequence == 2,
                    "Same-id/same-digest accepted retry must converge without appending history.");

                var mismatched = SHA256.HashData("sim05-coordinator-mismatch"u8);
                await RequireRejectAsync(
                    () => coordinator.ObserveAsync(operationId, mismatched),
                    "protocol.operation-payload-mismatch");

                var orderKey = new SameStepOrderKey(
                    phase: 1,
                    domainRank: 50,
                    conflictScopeDigest: SHA256.HashData("sim05-coordinator-scope"u8),
                    semanticPriority: 0,
                    intentId: operationId);
                var scheduledHistory = Record(
                    worldId,
                    3,
                    acceptedHistory.RecordDigest,
                    "operation.scheduled.v1",
                    "persistence.operation-scheduled",
                    writer =>
                    {
                        writer.WriteMapStart(3);
                        writer.WriteUnsigned(0); writer.WriteBytes(operationId.ToBytes());
                        writer.WriteUnsigned(1); writer.WriteUnsigned(plan.EffectiveStep!.Value);
                        writer.WriteUnsigned(2); writer.WriteBytes(orderKey.ToDatabaseBytes());
                    });
                var scheduled = await coordinator.ScheduleOrConvergeAsync(
                    operationId,
                    digest,
                    plan,
                    orderKey,
                    scheduledHistory);
                Require(!scheduled.Duplicate && scheduled.Lifecycle == OperationLifecycleStateV1.ScheduledDurable &&
                        scheduled.EffectiveStep == plan.EffectiveStep,
                    "Coordinator must expose the effective Step only after durable scheduling.");
                var scheduledRetry = await coordinator.ScheduleOrConvergeAsync(
                    operationId,
                    digest,
                    plan,
                    orderKey,
                    scheduledHistory);
                Require(scheduledRetry.Duplicate && scheduledRetry.EffectiveStep == scheduled.EffectiveStep,
                    "Scheduled retry must converge to the same durable effective Step.");

                var rejectedId = OpaqueId128.Parse("00000000000000000000000000000522");
                var rejectedDigest = SHA256.HashData("sim05-coordinator-rejected"u8);
                var terminalHistory = Record(
                    worldId,
                    4,
                    scheduledHistory.RecordDigest,
                    "operation.terminal.v1",
                    "persistence.operation-terminal",
                    writer =>
                    {
                        writer.WriteMapStart(3);
                        writer.WriteUnsigned(0); writer.WriteBytes(rejectedId.ToBytes());
                        writer.WriteUnsigned(1); writer.WriteBytes(rejectedDigest);
                        writer.WriteUnsigned(2); writer.WriteAsciiText("world.deadline-exceeded");
                    });
                var terminal = await coordinator.RejectUnseenOrConvergeAsync(
                    rejectedId,
                    rejectedDigest,
                    new StableToken("world.deadline-exceeded"),
                    terminalHistory);
                Require(!terminal.Duplicate && terminal.Lifecycle == OperationLifecycleStateV1.TerminalDurable &&
                        terminal.TerminalStatus == CoreOperationResultStatusV1.Rejected && terminal.EffectiveStep is null,
                    "Direct non-mutating rejection must become a durable terminal tombstone.");
                var terminalRetry = await coordinator.RejectUnseenOrConvergeAsync(
                    rejectedId,
                    rejectedDigest,
                    new StableToken("world.deadline-exceeded"),
                    terminalHistory);
                Require(terminalRetry.Duplicate && terminalRetry.TerminalSequence == terminal.TerminalSequence,
                    "Terminal retry must converge to the existing tombstone without re-execution.");
            }

            await using (var recovered = await SqlitePersistenceStore.OpenOrCreateAsync(paths))
            {
                var coordinator = new DurableOperationCoordinatorV1(recovered, policyHistory);
                var operationId = OpaqueId128.Parse("00000000000000000000000000000521");
                var digest = SHA256.HashData("sim05-coordinator-operation"u8);
                var scheduled = await coordinator.ObserveAsync(operationId, digest)
                    ?? throw new InvalidOperationException("Scheduled Operation dedup state disappeared after restart.");
                Require(scheduled.Lifecycle == OperationLifecycleStateV1.ScheduledDurable && scheduled.EffectiveStep == 52,
                    "Recovery must retain the original durable effective Step without rescheduling.");

                var rejectedId = OpaqueId128.Parse("00000000000000000000000000000522");
                var rejectedDigest = SHA256.HashData("sim05-coordinator-rejected"u8);
                var rejected = await coordinator.ObserveAsync(rejectedId, rejectedDigest)
                    ?? throw new InvalidOperationException("Terminal tombstone disappeared after restart.");
                Require(rejected.Lifecycle == OperationLifecycleStateV1.TerminalDurable &&
                        rejected.TerminalStatus == CoreOperationResultStatusV1.Rejected,
                    "Recovery must retain direct terminal dedup authority.");
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

    private static void RequireReject(Action action, string expectedMessage)
    {
        try
        {
            action();
        }
        catch (InvalidDataException ex) when (ex.Message == expectedMessage)
        {
            return;
        }
        throw new InvalidOperationException($"Expected SIM-05 rejection: {expectedMessage}");
    }

    private static async Task RequireRejectAsync(Func<Task> action, string expectedMessage)
    {
        try
        {
            await action();
        }
        catch (InvalidDataException ex) when (ex.Message == expectedMessage)
        {
            return;
        }
        throw new InvalidOperationException($"Expected SIM-05 rejection: {expectedMessage}");
    }
}
