using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04PersistenceHistoryTailResultV1(
    string SchemaVersion,
    int EquivalentMinutes,
    int TransitionCount,
    ulong FinalizedStep,
    ulong FinalHistorySequence,
    string FinalHistoryDigest,
    string FinalContinuityToken,
    bool HistoryChainValid,
    bool ReopenRecoveryMatched);

public static class Qa04PersistenceHistoryTailV1
{
    private const int EquivalentMinutes = 10;
    private const int StepsPerSecond = 30;
    private const int TransitionCount = EquivalentMinutes * 60 * StepsPerSecond;
    private static readonly OpaqueId128 WorldId =
        OpaqueId128.Parse("0000000000000000000000000000a411");
    private static readonly WorldSeed256 Seed =
        new(SHA256.HashData("qa04.persistence.history-tail.seed"u8));
    private static readonly byte[] ConfigDigest =
        SHA256.HashData("qa04.persistence.history-tail.config"u8);

    public static async Task<Qa04PersistenceHistoryTailResultV1> RunAsync(
        string persistenceRoot,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(persistenceRoot))
            throw new ArgumentException("persistenceRoot is required.", nameof(persistenceRoot));

        var paths = PersistenceLayout.Resolve(persistenceRoot, WorldId, 1);
        PersistenceLayout.EnsureGenerationDirectories(paths);
        await PersistenceLayout.WriteCurrentAsync(paths, 1, cancellationToken).ConfigureAwait(false);

        var genesis = HistoryRecordMaterial.Create(
            WorldId,
            sequence: 1,
            previousRecordDigest: new byte[32],
            recordType: "world.genesis.v1",
            payloadSchemaId: "core.world-genesis.v1",
            payloadSchemaMajor: 1,
            payloadSchemaMinor: 0,
            payloadBytes: WorldId.ToBytes(),
            writeNormalizedPayload: writer =>
            {
                writer.WriteMapStart(2);
                writer.WriteUnsigned(0); writer.WriteBytes(WorldId.ToBytes());
                writer.WriteUnsigned(1); writer.WriteBytes(Seed.ToBytes());
            });
        var continuity = HistoryIntegrity.ComputeGenesisContinuityToken(WorldId, genesis.RecordDigest);

        ulong finalSequence;
        byte[] finalDigest;
        byte[] finalContinuity;
        await using (var store = await SqlitePersistenceStore.OpenOrCreateAsync(paths, cancellationToken).ConfigureAwait(false))
        {
            await store.InitializeWorldMetadataAsync(
                new WorldPersistenceMetadataSeed(
                    WorldId,
                    PersistenceGeneration: 1,
                    Seed,
                    continuity,
                    ConfigGeneration: 1,
                    ConfigDigest,
                    MasterGeneration: 1),
                genesis,
                cancellationToken).ConfigureAwait(false);

            var previousDigest = genesis.RecordDigest;
            var currentContinuity = continuity;
            for (var index = 0; index < TransitionCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var effectiveStep = checked((ulong)index);
                var resultingStep = checked(effectiveStep + 1UL);
                var sequence = checked((ulong)index + 2UL);
                var payload = BitConverter.GetBytes(resultingStep);
                var history = HistoryRecordMaterial.Create(
                    WorldId,
                    sequence,
                    previousDigest,
                    recordType: "transition.committed.v1",
                    payloadSchemaId: "persistence.transition-committed",
                    payloadSchemaMajor: 1,
                    payloadSchemaMinor: 0,
                    payloadBytes: payload,
                    writeNormalizedPayload: writer =>
                    {
                        writer.WriteMapStart(2);
                        writer.WriteUnsigned(0); writer.WriteUnsigned(effectiveStep);
                        writer.WriteUnsigned(1); writer.WriteUnsigned(resultingStep);
                    });
                var nextContinuity = HistoryIntegrity.ComputeTransitionContinuityToken(
                    WorldId,
                    resultingStep,
                    currentContinuity,
                    history.RecordDigest);
                _ = await store.PersistTransitionCommitAsync(
                    effectiveStep,
                    resultingStep,
                    nextContinuity,
                    activeConfigGeneration: 1,
                    ConfigDigest,
                    history,
                    Array.Empty<TerminalOperationCommit>(),
                    cancellationToken).ConfigureAwait(false);
                previousDigest = history.RecordDigest;
                currentContinuity = nextContinuity;
            }

            var registered = new HashSet<string>(StringComparer.Ordinal)
            {
                "transition.committed.v1",
                "world.genesis.v1",
            };
            var chain = await store.ValidateHistoryLinkChainAsync(registered, cancellationToken).ConfigureAwait(false);
            var anchor = await store.ReadHistoryAnchorAsync(cancellationToken).ConfigureAwait(false);
            var recovery = await store.ReadRecoveryHeadAsync(cancellationToken).ConfigureAwait(false);
            if (chain.RecordCount != TransitionCount + 1 ||
                chain.LastSequence != anchor.Sequence ||
                recovery.FinalizedStep != TransitionCount ||
                !CryptographicOperations.FixedTimeEquals(recovery.ContinuityToken, currentContinuity))
                throw new InvalidDataException("qa04.persistence.history-tail-live-validation-drift");

            finalSequence = anchor.Sequence;
            finalDigest = anchor.Digest;
            finalContinuity = recovery.ContinuityToken;
        }

        var reopenMatched = false;
        await using (var reopened = await SqlitePersistenceStore.OpenOrCreateAsync(paths, cancellationToken).ConfigureAwait(false))
        {
            await reopened.ValidateQuickCheckAsync(cancellationToken).ConfigureAwait(false);
            var anchor = await reopened.ReadHistoryAnchorAsync(cancellationToken).ConfigureAwait(false);
            var recovery = await reopened.ReadRecoveryHeadAsync(cancellationToken).ConfigureAwait(false);
            reopenMatched =
                anchor.Sequence == finalSequence &&
                CryptographicOperations.FixedTimeEquals(anchor.Digest, finalDigest) &&
                recovery.FinalizedStep == TransitionCount &&
                CryptographicOperations.FixedTimeEquals(recovery.ContinuityToken, finalContinuity);
            if (!reopenMatched)
                throw new InvalidDataException("qa04.persistence.history-tail-reopen-drift");
        }

        return new Qa04PersistenceHistoryTailResultV1(
            "1.0",
            EquivalentMinutes,
            TransitionCount,
            FinalizedStep: TransitionCount,
            FinalHistorySequence: finalSequence,
            FinalHistoryDigest: Convert.ToHexStringLower(finalDigest),
            FinalContinuityToken: Convert.ToHexStringLower(finalContinuity),
            HistoryChainValid: true,
            ReopenRecoveryMatched: reopenMatched);
    }
}
