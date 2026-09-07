using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

internal static class Sim06DurableFinalizeSmoke
{
    internal static async Task RunAsync()
    {
        const ulong basisStep = 20;
        var worldId = OpaqueId128.Parse("00000000000000000000000000000800");
        var state = CreateWorldState(worldId, basisStep);
        var originalStateDigest = state.Diagnostic.StateDigest.ToArray();

        var operationNow = OpaqueId128.Parse("00000000000000000000000000000801");
        var operationFuture = OpaqueId128.Parse("00000000000000000000000000000802");
        var orderNow = new SameStepOrderKey(
            1,
            50,
            SHA256.HashData("sim06-finalize-now"u8),
            0,
            OpaqueId128.Parse("00000000000000000000000000000811"));
        var orderFuture = new SameStepOrderKey(
            1,
            50,
            SHA256.HashData("sim06-finalize-future"u8),
            0,
            OpaqueId128.Parse("00000000000000000000000000000812"));

        var scheduler = new OperationSchedulerStateV1(
            basisStep,
            null,
            [
                new ScheduledOperationRefV1(operationFuture, basisStep + 1, orderFuture),
                new ScheduledOperationRefV1(operationNow, basisStep, orderNow),
            ]);
        var frozen = StepInputFreezerV1.Freeze(state, scheduler);
        var candidate = BuildCandidate(
            OpaqueId128.Parse("00000000000000000000000000000820"),
            state,
            frozen,
            invariantResults:
            [
                new InvariantResultV1(
                    new StableToken("sim06.finalize-pass"),
                    InvariantSeverityV1.CommitBlocking,
                    InvariantOutcomeV1.Pass),
            ]);

        var transitionHistory = HistoryRecordMaterial.Create(
            worldId,
            sequence: 77,
            previousRecordDigest: new byte[32],
            recordType: "transition.committed.v1",
            payloadSchemaId: "persistence.transition-committed",
            payloadSchemaMajor: 1,
            payloadSchemaMinor: 0,
            payloadBytes: [0x20, 0x21],
            writeNormalizedPayload: writer =>
            {
                writer.WriteMapStart(3);
                writer.WriteUnsigned(0); writer.WriteUnsigned(basisStep);
                writer.WriteUnsigned(1); writer.WriteUnsigned(basisStep + 1);
                writer.WriteUnsigned(2); writer.WriteBytes(operationNow.ToBytes());
            });
        var terminalOperations = new[]
        {
            new TerminalOperationCommit(operationNow, 1, "operation.succeeded"),
        };
        var continuity = SHA256.HashData("sim06-resulting-continuity"u8);
        var configDigest = state.Diagnostic.ConfigDigest.ToArray();

        var blockedStore = new RecordingTransitionStore(failBeforeCommit: false);
        var blockedCandidate = BuildCandidate(
            OpaqueId128.Parse("00000000000000000000000000000821"),
            state,
            frozen,
            invariantResults:
            [
                new InvariantResultV1(
                    new StableToken("sim06.blocking-failure"),
                    InvariantSeverityV1.CommitBlocking,
                    InvariantOutcomeV1.Fail),
            ]);
        await RequireRejectAsync(
            () => new DurableStepCoordinatorV1(blockedStore).FinalizeAsync(
                state,
                blockedCandidate,
                scheduler,
                continuity,
                configDigest,
                transitionHistory,
                terminalOperations),
            "step-finalize.commit-blocked-by-invariant");
        Require(blockedStore.Calls == 0,
            "Commit-blocked candidate must not reach persistence.");

        var crashingStore = new RecordingTransitionStore(failBeforeCommit: true);
        await RequireRejectAsync(
            () => new DurableStepCoordinatorV1(crashingStore).FinalizeAsync(
                state,
                candidate,
                scheduler,
                continuity,
                configDigest,
                transitionHistory,
                terminalOperations),
            "fixture.crash-before-commit");

        Require(crashingStore.Calls == 1,
            "Crash fixture must reach the durable transition boundary exactly once.");
        Require(state.Header.Step == basisStep && state.Diagnostic.StateDigest.SequenceEqual(originalStateDigest),
            "A failed pre-commit finalize must leave State(S) authoritative and unchanged.");
        Require(scheduler.FreezeStep == basisStep && scheduler.NextSchedulableStep == basisStep + 1,
            "A failed pre-commit finalize must retain the frozen input barrier for retry/recovery.");
        Require(scheduler.ForEffectiveStep(basisStep).Count == 1 &&
                scheduler.ForEffectiveStep(basisStep)[0].OperationId == operationNow,
            "A failed pre-commit finalize must not retire the current scheduled Operation.");
        Require(scheduler.ForEffectiveStep(basisStep + 1).Count == 1 &&
                scheduler.ForEffectiveStep(basisStep + 1)[0].OperationId == operationFuture,
            "A failed pre-commit finalize must preserve future scheduled Operations.");
        Require(!candidate.IsPublishable,
            "Pre-commit StepCandidate must never become confirmed-publishable after a failed commit.");

        var durableStore = new RecordingTransitionStore(failBeforeCommit: false);
        var result = await new DurableStepCoordinatorV1(durableStore).FinalizeAsync(
            state,
            candidate,
            scheduler,
            continuity,
            configDigest,
            transitionHistory,
            terminalOperations);

        Require(durableStore.Calls == 1 && result.ResultingStep == basisStep + 1 && result.HistorySequence == 77,
            "Successful durable finalize must return the committed transition identity.");
        Require(result.CanPublishConfirmed,
            "Only a post-commit finalization result may authorize confirmed publication.");
        Require(result.CandidateDiagnosticDigest.SequenceEqual(candidate.DiagnosticDigest),
            "Durable finalization must retain candidate diagnostic provenance.");
        Require(scheduler.FreezeStep is null && scheduler.NextSchedulableStep == basisStep + 1,
            "Successful durable finalize must retire the frozen barrier only after persistence commit.");
        Require(scheduler.ForEffectiveStep(basisStep).Count == 0,
            "Successful durable finalize must retire the finalized scheduler bucket.");
        Require(scheduler.ForEffectiveStep(basisStep + 1).Count == 1 &&
                scheduler.ForEffectiveStep(basisStep + 1)[0].OperationId == operationFuture,
            "Successful durable finalize must preserve future scheduled Operations.");
        Require(state.Header.Step == basisStep && state.Diagnostic.StateDigest.SequenceEqual(originalStateDigest),
            "StepCoordinator finalization bridge must not mutate the immutable basis WorldState in place.");
    }

    private static StepCandidateV1 BuildCandidate(
        OpaqueId128 candidateId,
        WorldStateV1 state,
        FrozenStepInputV1 frozen,
        IEnumerable<InvariantResultV1> invariantResults)
    {
        var outputs = StandardDomainExecutionPlanV1.Create().Entries
            .Select(entry => new DomainCandidateOutputV1(entry.DomainToken, state.Header.Step))
            .ToArray();
        return StepCandidateV1.Build(
            candidateId,
            state,
            frozen,
            outputs,
            Array.Empty<ConflictGroupResolutionV1>(),
            invariantResults: invariantResults);
    }

    private static WorldStateV1 CreateWorldState(OpaqueId128 worldId, ulong step)
    {
        var zero = new byte[32];
        var partitions = StandardDomainPartitionRegistry.Entries.Select(entry => new PartitionStateRefV1(
            new PartitionStateHeaderV1(
                entry,
                revision: 1,
                basisStep: step,
                detailLevel: DetailLevelV1.D0Entity,
                itemCount: 0,
                canonicalDigest: SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(entry.PartitionId.Value)))));
        var header = new WorldStateHeaderV1(
            worldId,
            step,
            worldSeedDigest: zero,
            configGeneration: 1,
            masterGeneration: 1,
            rateGeneration: 1);
        return new WorldStateV1(
            header,
            new OrderedPartitionDirectoryV1(partitions),
            WorldStateV1.EmptySubstate("core.scheduler-state"),
            WorldStateV1.EmptySubstate("core.operation-state"),
            WorldStateV1.EmptySubstate("core.detail-state"),
            WorldStateV1.EmptySubstate("core.domain-registry-state"),
            zero);
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

        throw new InvalidOperationException($"Expected SIM-06 rejection: {expectedMessage}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class RecordingTransitionStore(bool failBeforeCommit) : IDurableStepTransitionStoreV1
    {
        public int Calls { get; private set; }

        public Task<DurableTransitionResult> PersistTransitionCommitAsync(
            ulong effectiveStep,
            ulong resultingStep,
            byte[] resultingStateContinuityToken,
            ulong activeConfigGeneration,
            byte[] activeConfigDigest,
            HistoryRecordMaterial history,
            IReadOnlyCollection<TerminalOperationCommit> terminalOperations,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            if (failBeforeCommit)
                throw new InvalidDataException("fixture.crash-before-commit");
            return Task.FromResult(new DurableTransitionResult(resultingStep, history.Sequence));
        }
    }
}
