using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;

internal static class Sim03SnapshotCommitSmoke
{
    public static async Task RunAsync(
        SqlitePersistenceStore store,
        WorldPersistencePaths paths,
        OpaqueId128 worldId)
    {
        var recoveryHead = await store.ReadRecoveryHeadAsync();
        if (recoveryHead.FinalizedStep != 1)
            throw new InvalidOperationException("Snapshot commit smoke requires finalized State(1).");
        var historyAnchor = await store.ReadHistoryAnchorAsync();
        if (historyAnchor.Sequence != 4)
            throw new InvalidOperationException("Snapshot commit smoke requires history sequence 4.");

        var snapshotId = OpaqueId128.Parse("00000000000000000000000000000043");
        var physical = SnapshotPhysicalStaging.Prepare(paths, snapshotId);
        var chunkPayload = "snapshot-fixture"u8.ToArray();
        var chunkLogicalDigest = SHA256.HashData(chunkPayload);
        await SnapshotChunkFile.WriteAsync(
            Path.Combine(physical.StagingChunksDirectory, "00000000.mvchunk"),
            chunkPayload,
            (ulong)chunkPayload.Length,
            chunkLogicalDigest,
            SnapshotCompression.None);

        var manifestBytes = "physical-manifest-fixture"u8.ToArray();
        await SnapshotPhysicalStaging.WriteManifestDurablyAsync(physical, manifestBytes);
        var physicalManifestDigest = SHA256.HashData(manifestBytes);
        var logicalSnapshotDigest = SHA256.HashData("logical-snapshot-fixture"u8);

        var relativeDirectory = Path.GetRelativePath(paths.GenerationDirectory, physical.FinalDirectory)
            .Replace('\\', '/');
        var snapshot = new SnapshotCommitMaterial(
            snapshotId,
            SnapshotStep: 1,
            historyAnchor,
            recoveryHead.ContinuityToken,
            logicalSnapshotDigest,
            physicalManifestDigest,
            relativeDirectory);

        var history = HistoryRecordMaterial.Create(
            worldId,
            sequence: 5,
            previousRecordDigest: historyAnchor.Digest,
            recordType: "snapshot.committed.v1",
            payloadSchemaId: "core.snapshot-committed.v1",
            payloadSchemaMajor: 1,
            payloadSchemaMinor: 0,
            payloadBytes: manifestBytes,
            writeNormalizedPayload: writer =>
            {
                writer.WriteMapStart(4);
                writer.WriteUnsigned(0); writer.WriteBytes(snapshotId.ToBytes());
                writer.WriteUnsigned(1); writer.WriteUnsigned(1);
                writer.WriteUnsigned(2); writer.WriteBytes(logicalSnapshotDigest);
                writer.WriteUnsigned(3); writer.WriteBytes(physicalManifestDigest);
            });

        var committed = await SnapshotCommitCoordinator.CommitAsync(
            store,
            paths,
            physical,
            snapshot,
            history,
            async (candidate, cancellationToken) =>
            {
                if (!File.Exists(candidate.StagingManifestPath))
                    throw new InvalidDataException("fixture.manifest-missing");
                await SnapshotChunkFile.ValidateAsync(
                    Path.Combine(candidate.StagingChunksDirectory, "00000000.mvchunk"),
                    cancellationToken);
            });

        if (committed.SnapshotStep != 1 || committed.HistorySequence != 5)
            throw new InvalidOperationException("Snapshot catalog/history commit result mismatch.");
        if (!Directory.Exists(physical.FinalDirectory) || Directory.Exists(physical.StagingDirectory))
            throw new InvalidOperationException("Snapshot staging directory was not atomically finalized.");
        var candidates = await store.ListSnapshotCandidatesNewestFirstAsync();
        if (candidates.Count != 1 || candidates[0].SnapshotId != snapshotId)
            throw new InvalidOperationException("Only SQLite-committed snapshot may become a recovery candidate.");
        if ((await store.ReadHistoryAnchorAsync()).Sequence != 5)
            throw new InvalidOperationException("Snapshot catalog and snapshot.committed history must advance atomically.");

        var registeredHistoryTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "world.genesis.v1",
            "operation.accepted.v1",
            "operation.scheduled.v1",
            "transition.committed.v1",
            "snapshot.committed.v1",
        };
        var historyIntegrity = await store.ValidateHistoryLinkChainAsync(registeredHistoryTypes);
        if (historyIntegrity.LastSequence != 5 || historyIntegrity.RecordCount != 5)
            throw new InvalidOperationException("Recovery history sequence/link validation did not reach the durable head.");

        var selected = await store.SelectRecoverySnapshotAsync(
            paths,
            async (directory, candidate, cancellationToken) =>
            {
                var manifestPath = Path.Combine(directory, "manifest.pb");
                if (!File.Exists(manifestPath)) throw new InvalidDataException("fixture.manifest-missing");
                var actualManifestDigest = SHA256.HashData(await File.ReadAllBytesAsync(manifestPath, cancellationToken));
                if (!CryptographicOperations.FixedTimeEquals(actualManifestDigest, candidate.PhysicalManifestDigest))
                    throw new InvalidDataException("fixture.manifest-digest-mismatch");
                await SnapshotChunkFile.ValidateAsync(Path.Combine(directory, "chunks", "00000000.mvchunk"), cancellationToken);
            });
        if (selected?.SnapshotId != snapshotId)
            throw new InvalidOperationException("Recovery selection must choose the newest valid cataloged snapshot.");

        var orphanId = OpaqueId128.Parse("00000000000000000000000000000044");
        var orphan = SnapshotPhysicalStaging.Prepare(paths, orphanId);
        await SnapshotChunkFile.WriteAsync(
            Path.Combine(orphan.StagingChunksDirectory, "00000000.mvchunk"),
            chunkPayload,
            (ulong)chunkPayload.Length,
            chunkLogicalDigest,
            SnapshotCompression.None);
        await SnapshotPhysicalStaging.WriteManifestDurablyAsync(orphan, manifestBytes);
        await SnapshotPhysicalStaging.FinalizeValidatedAsync(
            paths,
            orphan,
            static (_, _) => Task.CompletedTask);

        candidates = await store.ListSnapshotCandidatesNewestFirstAsync();
        if (candidates.Count != 1 || candidates.Any(candidate => candidate.SnapshotId == orphanId))
            throw new InvalidOperationException("Finalized filesystem orphan without DB commit must not become a recovery candidate.");
    }
}
