namespace MachiVerse.Simulation.Core.Persistence;

public static class SnapshotCommitCoordinator
{
    public static async Task<DurableSnapshotCommitResult> CommitAsync(
        SqlitePersistenceStore store,
        WorldPersistencePaths world,
        SnapshotPhysicalPaths physical,
        SnapshotCommitMaterial snapshot,
        HistoryRecordMaterial history,
        Func<SnapshotPhysicalPaths, CancellationToken, Task> validateStaging,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(physical);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(validateStaging);

        if (physical.SnapshotId != snapshot.SnapshotId)
            throw new InvalidDataException("persistence.snapshot-physical-logical-id-mismatch");

        var expectedRelativeDirectory = Path.GetRelativePath(world.GenerationDirectory, physical.FinalDirectory)
            .Replace('\\', '/');
        if (!string.Equals(snapshot.RelativeDirectory, expectedRelativeDirectory, StringComparison.Ordinal))
            throw new InvalidDataException("persistence.snapshot-relative-directory-mismatch");

        // Phase 4 ordering is deliberate: all snapshot files become durable and the staging
        // directory is atomically published before SQLite records the snapshot as a recovery
        // candidate. If the database transaction then fails, the final directory is merely an
        // orphan and recovery ignores it because snapshot_catalog has no row for it.
        await SnapshotPhysicalStaging.FinalizeValidatedAsync(
            world,
            physical,
            validateStaging,
            cancellationToken);

        return await store.PersistSnapshotCommitAsync(snapshot, history, cancellationToken);
    }
}
