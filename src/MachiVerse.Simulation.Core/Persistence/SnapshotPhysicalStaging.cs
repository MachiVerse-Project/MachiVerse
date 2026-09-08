using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed record SnapshotPhysicalPaths(
    OpaqueId128 SnapshotId,
    string StagingDirectory,
    string FinalDirectory,
    string StagingManifestPath,
    string FinalManifestPath,
    string StagingChunksDirectory,
    string FinalChunksDirectory);

public static class SnapshotPhysicalStaging
{
    public static SnapshotPhysicalPaths Prepare(WorldPersistencePaths world, OpaqueId128 snapshotId)
    {
        if (snapshotId.IsZero) throw new ArgumentException("SnapshotId ZERO is invalid.", nameof(snapshotId));
        Directory.CreateDirectory(world.SnapshotsDirectory);
        var id = snapshotId.ToString();
        var staging = Path.Combine(world.SnapshotsDirectory, $".staging-{id}");
        var final = Path.Combine(world.SnapshotsDirectory, id);
        if (Directory.Exists(staging)) throw new InvalidDataException("persistence.snapshot-staging-exists");
        if (Directory.Exists(final)) throw new InvalidDataException("persistence.snapshot-final-exists");

        var chunks = Path.Combine(staging, "chunks");
        Directory.CreateDirectory(chunks);
        return new SnapshotPhysicalPaths(
            snapshotId,
            staging,
            final,
            Path.Combine(staging, "manifest.pb"),
            Path.Combine(final, "manifest.pb"),
            chunks,
            Path.Combine(final, "chunks"));
    }

    public static async Task WriteManifestDurablyAsync(
        SnapshotPhysicalPaths snapshot,
        ReadOnlyMemory<byte> manifestBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (manifestBytes.IsEmpty) throw new ArgumentException("Snapshot manifest cannot be empty.", nameof(manifestBytes));
        if (!Directory.Exists(snapshot.StagingDirectory))
            throw new InvalidDataException("persistence.snapshot-staging-missing");

        await using var stream = new FileStream(
            snapshot.StagingManifestPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(manifestBytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    public static async Task FinalizeValidatedAsync(
        WorldPersistencePaths world,
        SnapshotPhysicalPaths snapshot,
        Func<SnapshotPhysicalPaths, CancellationToken, Task> validateStaging,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(validateStaging);
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(snapshot.StagingManifestPath))
            throw new InvalidDataException("persistence.snapshot-manifest-missing");
        if (!Directory.Exists(snapshot.StagingChunksDirectory))
            throw new InvalidDataException("persistence.snapshot-chunks-missing");
        if (Directory.Exists(snapshot.FinalDirectory))
            throw new InvalidDataException("persistence.snapshot-final-exists");

        await validateStaging(snapshot, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // Chunk writers and manifest writer flush each file. Persist directory entries before
        // exposing the directory by its final name on POSIX; Windows final rename is write-through.
        if (!OperatingSystem.IsWindows())
        {
            DurableFileSystem.FlushDirectory(snapshot.StagingChunksDirectory);
            DurableFileSystem.FlushDirectory(snapshot.StagingDirectory);
        }

        DurableFileSystem.AtomicMoveDirectory(snapshot.StagingDirectory, snapshot.FinalDirectory);
        if (!OperatingSystem.IsWindows())
            DurableFileSystem.FlushDirectory(world.SnapshotsDirectory);
    }
}
