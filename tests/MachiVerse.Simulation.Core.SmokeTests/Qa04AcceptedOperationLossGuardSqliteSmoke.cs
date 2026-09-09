using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;

internal static class Qa04AcceptedOperationLossGuardSqliteSmoke
{
    [ModuleInitializer]
    internal static void Run()
        => RunAsync().GetAwaiter().GetResult();

    private static async Task RunAsync()
    {
        var worldId = OpaqueId128.Parse("0000000000000000000000000000ac01");
        var operationId = OpaqueId128.Parse("0000000000000000000000000000ac02");
        var seed = new WorldSeed256(new byte[32]);
        var configDigest = SHA256.HashData("qa04-operation-loss-sqlite-config"u8);
        var operationDigest = SHA256.HashData("qa04-operation-loss-sqlite-payload"u8);
        var root = Path.Combine(Path.GetTempPath(), "machiverse-qa04-operation-loss-" + Guid.NewGuid().ToString("N"));

        try
        {
            var paths = PersistenceLayout.Resolve(root, worldId, 1);
            PersistenceLayout.EnsureGenerationDirectories(paths);
            await PersistenceLayout.WriteCurrentAsync(paths, 1).ConfigureAwait(false);

            var genesis = HistoryRecordMaterial.Create(
                worldId,
                sequence: 1,
                previousRecordDigest: new byte[32],
                recordType: "world.genesis.v1",
                payloadSchemaId: "core.world-genesis.v1",
                payloadSchemaMajor: 1,
                payloadSchemaMinor: 0,
                payloadBytes: worldId.ToBytes(),
                writeNormalizedPayload: writer =>
                {
                    writer.WriteMapStart(2);
                    writer.WriteUnsigned(0); writer.WriteBytes(worldId.ToBytes());
                    writer.WriteUnsigned(1); writer.WriteBytes(configDigest);
                });
            var continuity = HistoryIntegrity.ComputeGenesisContinuityToken(worldId, genesis.RecordDigest);

            await using var store = await SqlitePersistenceStore.OpenOrCreateAsync(paths).ConfigureAwait(false);
            await store.InitializeWorldMetadataAsync(
                new WorldPersistenceMetadataSeed(
                    worldId,
                    PersistenceGeneration: 1,
                    seed,
                    continuity,
                    ConfigGeneration: 1,
                    configDigest,
                    MasterGeneration: 1),
                genesis).ConfigureAwait(false);

            var acceptedHistory = HistoryRecordMaterial.Create(
                worldId,
                sequence: 2,
                previousRecordDigest: genesis.RecordDigest,
                recordType: "operation.accepted.v1",
                payloadSchemaId: "core.operation-accepted.v1",
                payloadSchemaMajor: 1,
                payloadSchemaMinor: 0,
                payloadBytes: operationId.ToBytes().Concat(operationDigest).ToArray(),
                writeNormalizedPayload: writer =>
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
                "QA-04 SQLite accepted Operation fixture must cross the durable ACCEPTED boundary.");

            var receipt = await Qa04AcceptedOperationLossGuardV1.ValidateStoreAsync(
                [new Qa04AcceptedOperationExpectationV1(operationId, operationDigest)],
                store).ConfigureAwait(false);
            Require(receipt.Passed &&
                    receipt.ExpectedAcceptedCount == 1 &&
                    receipt.AcceptedDurableCount == 1 &&
                    receipt.ScheduledDurableCount == 0 &&
                    receipt.TerminalDurableCount == 0,
                "QA-04 accepted Operation loss guard must observe the real SQLite durable ACCEPTED row.");

            var missingId = OpaqueId128.Parse("0000000000000000000000000000ac03");
            await ExpectFailureAsync(
                () => Qa04AcceptedOperationLossGuardV1.ValidateStoreAsync(
                    [new Qa04AcceptedOperationExpectationV1(missingId, operationDigest)],
                    store),
                "qa04.operation-loss.accepted-operation-missing").ConfigureAwait(false);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
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
