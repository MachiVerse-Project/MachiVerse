using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;

internal static class Qa04CanonicalOperationDurableAdmissionSmoke
{
    internal static async Task RunAsync()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "machiverse-qa04-operation-admission-" + Guid.NewGuid().ToString("N"));
        var configDigest = SHA256.HashData("qa04-canonical-operation-admission-config"u8);
        var paths = PersistenceLayout.Resolve(root, Qa04ReferenceLoadV1.WorldId, 1);

        try
        {
            PersistenceLayout.EnsureGenerationDirectories(paths);
            await PersistenceLayout.WriteCurrentAsync(paths, 1).ConfigureAwait(false);
            var genesis = CreateGenesis();
            var continuity = HistoryIntegrity.ComputeGenesisContinuityToken(
                Qa04ReferenceLoadV1.WorldId,
                genesis.RecordDigest);

            var descriptors = Qa04ReferenceLoadV1.OperationsForStep(0).ToArray();
            var supported = Qa04CanonicalOperationBindingV1.BoundFamilies
                .Select(family => descriptors.First(descriptor => descriptor.FamilyToken == family))
                .ToArray();
            var expectations = new List<Qa04AcceptedOperationExpectationV1>(supported.Length);

            await using (var store = await SqlitePersistenceStore.OpenOrCreateAsync(paths).ConfigureAwait(false))
            {
                await store.InitializeWorldMetadataAsync(
                    new WorldPersistenceMetadataSeed(
                        Qa04ReferenceLoadV1.WorldId,
                        PersistenceGeneration: 1,
                        Qa04ReferenceLoadV1.WorldSeed,
                        continuity,
                        ConfigGeneration: 1,
                        configDigest,
                        MasterGeneration: 1),
                    genesis).ConfigureAwait(false);

                var policy = Qa04CanonicalOperationDurableAdmissionV1.CreateCanonicalPolicy(1);
                foreach (var descriptor in supported)
                {
                    var binding = Qa04CanonicalOperationBindingV1.Bind(descriptor, schedulingPolicyGeneration: 1);
                    var receipt = await Qa04CanonicalOperationDurableAdmissionV1.AdmitAndScheduleAsync(
                        store,
                        binding,
                        policy,
                        nextSchedulableStep: 1).ConfigureAwait(false);
                    Require(receipt.Passed,
                        $"QA-04 canonical Operation did not cross durable custody: {descriptor.FamilyToken.Value}");
                    Require(receipt.Accepted.AcceptedSequence is not null &&
                            receipt.Scheduled.AcceptedSequence == receipt.Accepted.AcceptedSequence &&
                            receipt.Scheduled.ScheduledSequence is not null &&
                            receipt.Scheduled.EffectiveStep == 1,
                        "QA-04 durable Operation sequence/effective-Step custody mismatch.");

                    var state = await store.ReadOperationStateAsync(descriptor.OperationId).ConfigureAwait(false)
                        ?? throw new InvalidOperationException("QA-04 scheduled Operation state disappeared after commit.");
                    Require(state.Lifecycle == DurableOperationLifecycleV1.ScheduledDurable &&
                            state.EffectiveStep == 1 &&
                            state.OperationPayloadDigest.SequenceEqual(binding.BoundDescriptor.PayloadDigest),
                        "QA-04 SQLite operation_state did not retain the canonical bound digest/effective Step.");
                    expectations.Add(new Qa04AcceptedOperationExpectationV1(
                        descriptor.OperationId,
                        binding.BoundDescriptor.PayloadDigest));

                    var duplicate = await Qa04CanonicalOperationDurableAdmissionV1.AdmitAndScheduleAsync(
                        store,
                        binding,
                        policy,
                        nextSchedulableStep: 1).ConfigureAwait(false);
                    Require(duplicate.Passed && duplicate.Scheduled.Duplicate,
                        "QA-04 duplicate durable admission must converge on the existing scheduled custody.");
                }

                var lossGuard = await Qa04AcceptedOperationLossGuardV1.ValidateStoreAsync(
                    expectations,
                    store).ConfigureAwait(false);
                Require(lossGuard.Passed &&
                        lossGuard.ExpectedAcceptedCount == supported.Length &&
                        lossGuard.AcceptedDurableCount == 0 &&
                        lossGuard.ScheduledDurableCount == supported.Length &&
                        lossGuard.TerminalDurableCount == 0,
                    "QA-04 accepted-operation loss guard must observe all four bound families as durable scheduled custody.");
            }

            await using (var recovered = await SqlitePersistenceStore.OpenOrCreateAsync(paths).ConfigureAwait(false))
            {
                foreach (var expectation in expectations)
                {
                    var state = await recovered.ReadOperationStateAsync(expectation.OperationId).ConfigureAwait(false)
                        ?? throw new InvalidOperationException("QA-04 canonical Operation custody disappeared after restart.");
                    Require(state.Lifecycle == DurableOperationLifecycleV1.ScheduledDurable &&
                            state.EffectiveStep == 1 &&
                            state.OperationPayloadDigest.SequenceEqual(expectation.PayloadDigest),
                        "QA-04 canonical Operation custody changed across SQLite restart.");
                }
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static HistoryRecordMaterial CreateGenesis()
        => HistoryRecordMaterial.Create(
            Qa04ReferenceLoadV1.WorldId,
            sequence: 1,
            previousRecordDigest: new byte[32],
            recordType: "world.genesis.v1",
            payloadSchemaId: "core.world-genesis.v1",
            payloadSchemaMajor: 1,
            payloadSchemaMinor: 0,
            payloadBytes: Qa04ReferenceLoadV1.WorldId.ToBytes(),
            writeNormalizedPayload: writer =>
            {
                writer.WriteMapStart(3);
                writer.WriteUnsigned(0); writer.WriteBytes(Qa04ReferenceLoadV1.WorldId.ToBytes());
                writer.WriteUnsigned(1); writer.WriteBytes(Qa04ReferenceLoadV1.WorldSeed.ToBytes());
                writer.WriteUnsigned(2); writer.WriteAsciiText(Qa04ReferenceLoadV1.BenchmarkProfileId);
            });

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
