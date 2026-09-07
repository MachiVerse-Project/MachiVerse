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
        var outputs = StandardDomainExecutionPlanV1.Create().Entries
            .Select(entry => new DomainCandidateOutputV1(entry.DomainToken, basisStep))
            .ToArray();

        for (var index = 0; index < CrossDomainTransactionKindRegistryV1.Registrations.Count; index++)
        {
            var registration = CrossDomainTransactionKindRegistryV1.Registrations[index];
            var scheduler = new OperationSchedulerStateV1(basisStep, null);
            var frozen = StepInputFreezerV1.Freeze(state, scheduler);
            var rootId = Id(0x13610 + index);
            var root = new CausalityRefV1(CausalityRefKindV1.Operation, rootId.ToBytes(), basisStep);
            var subject = Id(0x13640 + index);
            var invalidTransaction = CrossDomainTransactionAssemblerV1.AssembleAndValidate(
                worldId,
                registration.TransactionKind,
                basisStep,
                root,
                [subject],
                stableLocalOrdinal: (ulong)index,
                participants: [],
                invariantResults:
                [
                    new InvariantResultV1(
                        new StableToken("sim13." + registration.TransactionKind.Value["transaction.".Length..].Replace('-', '.') + ".atomic"),
                        InvariantSeverityV1.CommitBlocking,
                        InvariantOutcomeV1.Pass),
                ]);

            var candidate = StepCandidateV1.Build(
                Id(0x13700 + index),
                state,
                frozen,
                outputs,
                [],
                transactionCandidates: [invalidTransaction]);
            Require(!candidate.CommitDecision.CanCommit,
                $"{registration.TransactionKind.Value}.required-participant-failure: invalid transaction must block StepCandidate commit.");

            var history = HistoryRecordMaterial.Create(
                worldId,
                sequence: (ulong)(200 + index),
                previousRecordDigest: new byte[32],
                recordType: "transition.committed.v1",
                payloadSchemaId: "persistence.transition-committed",
                payloadSchemaMajor: 1,
                payloadSchemaMinor: 0,
                payloadBytes: [(byte)(0x13 + index)],
                writeNormalizedPayload: writer =>
                {
                    writer.WriteMapStart(2);
                    writer.WriteUnsigned(0); writer.WriteUnsigned(basisStep);
                    writer.WriteUnsigned(1); writer.WriteUnsigned(basisStep + 1);
                });
            var material = new StepFinalizeMaterialV1(
                candidate.ConfigGeneration,
                candidate.ConfigDigest,
                SHA256.HashData(registration.TransactionKind.Value is { } kind
                    ? System.Text.Encoding.ASCII.GetBytes("sim13-blocked-continuity:" + kind)
                    : "sim13-blocked-continuity"u8.ToArray()),
                history,
                []);
            var durability = new RecordingDurability();

            var rejected = false;
            try
            {
                new StepFinalizationCoordinatorV1(durability)
                    .FinalizeAsync(candidate, scheduler, material)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (InvalidDataException ex) when (ex.Message == "step-finalize.commit-blocked-by-invariant")
            {
                rejected = true;
            }

            Require(rejected,
                $"{registration.TransactionKind.Value}.crash-before-commit: Step finalization must reject before durable commit.");
            Require(durability.Calls == 0,
                $"{registration.TransactionKind.Value}.crash-before-commit: invalid transaction must never reach durable CommitAsync.");
            Require(scheduler.FreezeStep == basisStep && scheduler.NextSchedulableStep == basisStep + 1,
                $"{registration.TransactionKind.Value}.crash-before-commit: blocked finalize must preserve frozen State(S) retry boundary.");
            Require(!candidate.IsPublishable,
                $"{registration.TransactionKind.Value}.crash-before-commit: blocked candidate must remain non-authoritative/non-publishable.");
        }
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

    private static OpaqueId128 Id(int value)
        => OpaqueId128.Parse(value.ToString("x32"));

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
