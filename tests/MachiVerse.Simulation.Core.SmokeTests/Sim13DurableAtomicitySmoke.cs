using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

internal static class Sim13DurableAtomicitySmoke
{
    internal static void Run()
    {
        const ulong basisStep = 140;
        var worldId = OpaqueId128.Parse("00000000000000000000000000013600");
        var state = CreateWorldState(worldId, basisStep);
        var scheduler = new OperationSchedulerStateV1(basisStep, null);
        var frozen = StepInputFreezerV1.Freeze(state, scheduler);
        var outputs = StandardDomainExecutionPlanV1.Create().Entries
            .Select(entry => new DomainCandidateOutputV1(entry.DomainToken, basisStep))
            .ToArray();

        var kind = CrossDomainTransactionKindRegistryV1.Get("transaction.birth");
        var rootId = OpaqueId128.Parse("00000000000000000000000000013601");
        var root = new CausalityRefV1(CausalityRefKindV1.Operation, rootId.ToBytes(), basisStep);
        var subject = OpaqueId128.Parse("00000000000000000000000000013602");
        var invalidTransaction = CrossDomainTransactionAssemblerV1.AssembleAndValidate(
            worldId,
            kind,
            basisStep,
            root,
            [subject],
            stableLocalOrdinal: 0,
            participants: [],
            invariantResults:
            [
                new InvariantResultV1(
                    new StableToken("sim13.birth.atomic"),
                    InvariantSeverityV1.CommitBlocking,
                    InvariantOutcomeV1.Pass),
            ]);

        var candidate = StepCandidateV1.Build(
            OpaqueId128.Parse("00000000000000000000000000013610"),
            state,
            frozen,
            outputs,
            [],
            transactionCandidates: [invalidTransaction]);
        Require(!candidate.CommitDecision.CanCommit,
            "transaction.birth.required-participant-failure: invalid transaction must block StepCandidate commit.");

        var history = HistoryRecordMaterial.Create(
            worldId,
            sequence: 200,
            previousRecordDigest: new byte[32],
            recordType: "transition.committed.v1",
            payloadSchemaId: "persistence.transition-committed",
            payloadSchemaMajor: 1,
            payloadSchemaMinor: 0,
            payloadBytes: [0x13],
            writeNormalizedPayload: writer =>
            {
                writer.WriteMapStart(2);
                writer.WriteUnsigned(0); writer.WriteUnsigned(basisStep);
                writer.WriteUnsigned(1); writer.WriteUnsigned(basisStep + 1);
            });
        var material = new StepFinalizeMaterialV1(
            candidate.ConfigGeneration,
            candidate.ConfigDigest,
            SHA256.HashData("sim13-blocked-continuity"u8),
            history,
            []);
        var durability = new RecordingDurability();

        try
        {
            new StepFinalizationCoordinatorV1(durability)
                .FinalizeAsync(candidate, scheduler, material)
                .GetAwaiter()
                .GetResult();
        }
        catch (InvalidDataException ex) when (ex.Message == "step-finalize.commit-blocked-by-invariant")
        {
            Require(durability.Calls == 0,
                "transaction.birth.crash-before-commit: invalid transaction must never reach durable CommitAsync.");
            Require(scheduler.FreezeStep == basisStep && scheduler.NextSchedulableStep == basisStep + 1,
                "transaction.birth.crash-before-commit: blocked finalize must preserve frozen State(S) retry boundary.");
            Require(!candidate.IsPublishable,
                "transaction.birth.crash-before-commit: blocked candidate must remain non-authoritative/non-publishable.");
            return;
        }

        throw new InvalidOperationException(
            "transaction.birth.crash-before-commit: expected Step finalization to reject before durable commit.");
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

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class RecordingDurability : IStepTransitionDurabilityV1
    {
        public int Calls { get; private set; }

        public Task<DurableTransitionResult> CommitAsync(
            StepCandidateV1 candidate,
            StepFinalizeMaterialV1 material,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(new DurableTransitionResult(
                candidate.TargetStep,
                material.TransitionHistory.Sequence));
        }
    }
}
