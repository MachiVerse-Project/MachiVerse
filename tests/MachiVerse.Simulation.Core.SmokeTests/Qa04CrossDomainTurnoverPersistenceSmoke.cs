using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;

internal static class Qa04CrossDomainTurnoverPersistenceSmoke
{
    [ModuleInitializer]
    internal static void Run()
        => RunAsync().GetAwaiter().GetResult();

    private static async Task RunAsync()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "machiverse-qa04-turnover-persistence-" + Guid.NewGuid().ToString("N"));
        var worldId = OpaqueId128.Parse("00000000000000000000000000049300");
        var configDigest = SHA256.HashData("qa04-turnover-persistence-config"u8);
        try
        {
            var active = CreateState(worldId, "00000000000000000000000000049301", createdStep: 0, updatedStep: 0);
            var replacement = CreateState(worldId, "00000000000000000000000000049302", createdStep: 1, updatedStep: 1);
            var committed = active.Commit(1);

            var paths = PersistenceLayout.Resolve(rootPath, worldId, 1);
            PersistenceLayout.EnsureGenerationDirectories(paths);
            await PersistenceLayout.WriteCurrentAsync(paths, 1);

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
                    writer.WriteArrayStart(1);
                    writer.WriteBytes(worldId.ToBytes());
                });
            var genesisContinuity = HistoryIntegrity.ComputeGenesisContinuityToken(worldId, genesis.RecordDigest);

            var transition = HistoryRecordMaterial.Create(
                worldId,
                sequence: 2,
                previousRecordDigest: genesis.RecordDigest,
                recordType: "transition.committed.v1",
                payloadSchemaId: "persistence.transition-committed",
                payloadSchemaMajor: 1,
                payloadSchemaMinor: 0,
                payloadBytes: committed.CanonicalDigest(),
                writeNormalizedPayload: writer =>
                {
                    writer.WriteArrayStart(3);
                    writer.WriteUnsigned(0);
                    writer.WriteUnsigned(1);
                    writer.WriteBytes(replacement.TransactionId.ToBytes());
                });
            var resultingContinuity = HistoryIntegrity.ComputeTransitionContinuityToken(
                worldId,
                resultingStep: 1,
                genesisContinuity,
                transition.RecordDigest);

            await using (var store = await SqlitePersistenceStore.OpenOrCreateAsync(paths))
            {
                await store.InitializeWorldMetadataWithCanonicalCrossDomainTransactionsAsync(
                    new WorldPersistenceMetadataSeed(
                        worldId,
                        PersistenceGeneration: 1,
                        new WorldSeed256(new byte[32]),
                        genesisContinuity,
                        ConfigGeneration: 1,
                        configDigest,
                        MasterGeneration: 1),
                    genesis,
                    [active]);

                var durable = await store.PersistTransitionCommitWithCanonicalCrossDomainTransactionsAsync(
                    effectiveStep: 0,
                    resultingStep: 1,
                    resultingContinuity,
                    activeConfigGeneration: 1,
                    configDigest,
                    transition,
                    Array.Empty<TerminalOperationCommit>(),
                    [committed, replacement]);
                Require(durable.ResultingStep == 1 && durable.HistorySequence == 2,
                    "Turnover transition durability receipt drifted.");
                await Qa04CrossDomainDurableAuthorityVerifierV1.RequireActiveAuthorityAsync(store, [replacement]);
            }

            await using (var reopened = await SqlitePersistenceStore.OpenOrCreateAsync(paths))
            {
                await Qa04CrossDomainDurableAuthorityVerifierV1.RequireActiveAuthorityAsync(reopened, [replacement]);
                var terminal = await reopened.ReadCrossDomainTransactionStateAsync(active.TransactionId);
                Require(terminal is not null && terminal.Lifecycle == TransactionLifecycleV1.Committed && terminal.TerminalStep == 1,
                    "Reopened turnover authority lost the committed predecessor state.");
                var recovery = await reopened.ReadRecoveryHeadAsync();
                Require(recovery.FinalizedStep == 1 && recovery.ContinuityToken.AsSpan().SequenceEqual(resultingContinuity),
                    "Reopened turnover recovery head drifted.");
            }
        }
        finally
        {
            if (Directory.Exists(rootPath)) Directory.Delete(rootPath, recursive: true);
        }
    }

    private static CrossDomainTransactionStateV1 CreateState(
        OpaqueId128 worldId,
        string transactionId,
        ulong createdStep,
        ulong updatedStep)
    {
        var kind = CrossDomainTransactionKindRegistryV1.Get("transaction.birth");
        var resident = StandardDomainExecutionPlanV1.Create().Entries.Single(entry => entry.DomainToken.Value == "resident");
        var participant = new PersistentTransactionParticipantV1(
            resident.DomainToken,
            resident.OwnedPartitions[0],
            [OpaqueId128.Parse("00000000000000000000000000049303")],
            required: true,
            TransactionParticipantOutcomeV1.Ready,
            SHA256.HashData("qa04-turnover-persistence-effect"u8));
        var invariant = new InvariantResultV1(
            CrossDomainTransactionInvariantRegistryV1.GetRequiredInvariantIds(kind).Single(),
            InvariantSeverityV1.CommitBlocking,
            InvariantOutcomeV1.Pass,
            Array.Empty<CausalityRefV1>(),
            null);
        return new CrossDomainTransactionStateV1(
            OpaqueId128.Parse(transactionId),
            kind,
            TransactionLifecycleV1.Active,
            createdStep,
            updatedStep,
            terminalStep: null,
            new CausalityRefV1(CausalityRefKindV1.Transaction, worldId.ToBytes(), basisStep: createdStep == 0 ? 0 : createdStep - 1),
            [OpaqueId128.Parse("00000000000000000000000000049304")],
            [participant],
            [invariant]);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
