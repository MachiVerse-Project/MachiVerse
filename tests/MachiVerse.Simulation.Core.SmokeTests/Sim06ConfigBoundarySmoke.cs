using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Configuration;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

internal static class Sim06ConfigBoundarySmoke
{
    internal static async Task RunAsync()
    {
        const ulong basisStep = 12;
        var coordinator = new CoreConfigCoordinator();
        var initial = coordinator.LoadStartup("""
[meta]
format = "machiverse-config"
schema_version = "1.0"
component = "simulation-core"
""");

        var validated = coordinator.ValidateRuntimeChange(
            new ConfigChangeSet(
                initial.Generation,
                [new ConfigChange("scheduling.min-lead-steps", 3L)],
                basisStep),
            minimumNextApplicableStep: basisStep);
        Require(validated.ContainsSimulationImpact && validated.EffectiveStep == basisStep,
            "Simulation Config validation must preserve the exact effective Step.");

        RequireReject(
            () => coordinator.ApplyAtBoundary(validated),
            "config.simulation-boundary-step-required");
        RequireReject(
            () => coordinator.ApplyAtBoundary(validated, basisStep + 1),
            "config.effective-step-boundary-mismatch");
        Require(coordinator.Current.Generation == initial.Generation,
            "Rejected Config boundary attempts must not advance ConfigGeneration.");

        var active = coordinator.ApplyAtBoundary(validated, basisStep);
        Require(active.Generation == initial.Generation + 1 &&
                !active.Digest.SequenceEqual(initial.Digest),
            "Effective-Step apply must atomically activate the validated Config generation.");

        var worldId = OpaqueId128.Parse("00000000000000000000000000000900");
        var state = CreateWorldState(worldId, basisStep, initial.Generation, initial.Digest);
        var scheduler = new OperationSchedulerStateV1(basisStep, null);
        var frozen = StepInputFreezerV1.Freeze(state, scheduler, active);
        Require(frozen.ConfigGeneration == active.Generation && frozen.ConfigDigest.SequenceEqual(active.Digest),
            "Frozen Step input must bind the Config generation/digest active before transition start.");

        var outputs = StandardDomainExecutionPlanV1.Create().Entries
            .Select(entry => new DomainCandidateOutputV1(entry.DomainToken, basisStep))
            .ToArray();
        var candidate = StepCandidateV1.Build(
            OpaqueId128.Parse("00000000000000000000000000000901"),
            state,
            frozen,
            outputs,
            Array.Empty<ConflictGroupResolutionV1>());
        Require(candidate.ConfigGeneration == active.Generation && candidate.ConfigDigest.SequenceEqual(active.Digest),
            "StepCandidate must use the frozen active Config rather than the basis State header generation.");

        var transition = HistoryRecordMaterial.Create(
            worldId,
            sequence: 1,
            previousRecordDigest: new byte[32],
            recordType: "transition.committed.v1",
            payloadSchemaId: "persistence.transition-committed",
            payloadSchemaMajor: 1,
            payloadSchemaMinor: 0,
            payloadBytes: [0x09, 0x01],
            writeNormalizedPayload: writer =>
            {
                writer.WriteMapStart(2);
                writer.WriteUnsigned(0); writer.WriteUnsigned(basisStep);
                writer.WriteUnsigned(1); writer.WriteUnsigned(basisStep + 1);
            });
        var durability = new RecordingDurability();
        var finalizer = new StepFinalizationCoordinatorV1(durability);
        var wrongDigest = SHA256.HashData("wrong-config-digest"u8);
        var material = new StepFinalizeMaterialV1(
            active.Generation,
            wrongDigest,
            SHA256.HashData("candidate-continuity"u8),
            transition,
            Array.Empty<TerminalOperationCommit>());

        await RequireRejectAsync(
            () => finalizer.FinalizeAsync(candidate, scheduler, material),
            "step-finalize.config-digest-mismatch");
        Require(durability.CallCount == 0,
            "Config snapshot mismatch must fail before entering transition persistence.");
        Require(scheduler.FreezeStep == basisStep && scheduler.NextSchedulableStep == basisStep + 1,
            "Config mismatch must leave the frozen Step retry/recovery boundary intact.");
    }

    private static WorldStateV1 CreateWorldState(
        OpaqueId128 worldId,
        ulong step,
        ulong configGeneration,
        byte[] configDigest)
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
                configGeneration,
                masterGeneration: 1,
                rateGeneration: 1),
            new OrderedPartitionDirectoryV1(partitions),
            WorldStateV1.EmptySubstate("core.scheduler-state"),
            WorldStateV1.EmptySubstate("core.operation-state"),
            WorldStateV1.EmptySubstate("core.detail-state"),
            WorldStateV1.EmptySubstate("core.domain-registry-state"),
            configDigest);
    }

    private static void RequireReject(Func<EffectiveCoreConfig> action, string expectedMessage)
    {
        try
        {
            _ = action();
        }
        catch (InvalidDataException ex) when (ex.Message == expectedMessage)
        {
            return;
        }
        throw new InvalidOperationException($"Expected Config rejection: {expectedMessage}");
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

    private sealed class RecordingDurability : IStepTransitionDurabilityV1
    {
        public int CallCount { get; private set; }

        public Task<DurableTransitionResult> CommitAsync(
            StepCandidateV1 candidate,
            StepFinalizeMaterialV1 material,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new DurableTransitionResult(candidate.TargetStep, material.TransitionHistory.Sequence));
        }
    }
}
