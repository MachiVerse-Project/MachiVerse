using System.Security.Cryptography;
using System.Text;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;

internal static class PersistenceSnapshotSmoke
{
    internal static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "machiverse-sim03-snapshot-" + Guid.NewGuid().ToString("N"));
        try
        {
            var worldId = OpaqueId128.Parse("00000000000000000000000000000031");
            var paths = PersistenceLayout.Resolve(root, worldId, 1);
            await using var store = await SqlitePersistenceStore.OpenOrCreateAsync(paths);

            if (!await store.HasTableAsync("snapshot_catalog"))
                throw new InvalidOperationException("SIM-03 snapshot_catalog table is missing.");

            await store.ValidateQuickCheckAsync();
            var candidates = await store.ListSnapshotCandidatesNewestFirstAsync();
            if (candidates.Count != 0)
                throw new InvalidOperationException("Uncataloged snapshot directories must not become recovery candidates.");

            var relativePath = SnapshotChunkFile.RelativePath(0);
            SnapshotChunkFile.ValidateRelativePath(relativePath, 0);
            var traversalRejected = false;
            try
            {
                SnapshotChunkFile.ValidateRelativePath("../chunks/00000000.mvchunk", 0);
            }
            catch (InvalidDataException)
            {
                traversalRejected = true;
            }
            if (!traversalRejected)
                throw new InvalidOperationException("Snapshot chunk traversal path must be rejected.");

            var snapshotDirectory = Path.Combine(paths.SnapshotsDirectory, "fixture-snapshot");
            var chunkPath = Path.Combine(snapshotDirectory, "chunks", "00000000.mvchunk");
            var payload = Encoding.UTF8.GetBytes("fixture-snapshot-payload");
            var logicalDigest = SHA256.HashData(payload);

            var written = await SnapshotChunkFile.WriteAsync(
                chunkPath,
                payload,
                (ulong)payload.Length,
                logicalDigest,
                SnapshotCompression.None);
            if (written.StoredLength != (ulong)payload.Length || written.Compression != SnapshotCompression.None)
                throw new InvalidOperationException("Snapshot chunk write header mismatch.");
            if (new FileInfo(chunkPath).Length != SnapshotChunkFile.HeaderLength + payload.Length)
                throw new InvalidOperationException("Snapshot chunk physical length mismatch.");

            var validated = await SnapshotChunkFile.ValidateAsync(chunkPath);
            if (validated.FormatMajor != 1 || validated.FormatMinor != 0)
                throw new InvalidOperationException("Snapshot chunk version mismatch.");
            if (!validated.LogicalPayloadDigest.SequenceEqual(logicalDigest))
                throw new InvalidOperationException("Snapshot logical payload digest was not preserved in framing.");
            if (!validated.StoredPayloadDigest.SequenceEqual(SHA256.HashData(payload)))
                throw new InvalidOperationException("Snapshot stored payload digest mismatch.");

            await using (var tamper = new FileStream(chunkPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                tamper.Position = SnapshotChunkFile.HeaderLength;
                var original = tamper.ReadByte();
                if (original < 0) throw new InvalidOperationException("Snapshot fixture payload is unexpectedly empty.");
                tamper.Position = SnapshotChunkFile.HeaderLength;
                tamper.WriteByte((byte)(original ^ 0x01));
                tamper.Flush(flushToDisk: true);
            }

            var tamperRejected = false;
            try
            {
                await SnapshotChunkFile.ValidateAsync(chunkPath);
            }
            catch (InvalidDataException ex) when (ex.Message == "persistence.snapshot.stored-digest-mismatch")
            {
                tamperRejected = true;
            }
            if (!tamperRejected)
                throw new InvalidOperationException("Tampered snapshot stored payload must be rejected.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
