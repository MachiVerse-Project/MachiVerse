using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed record PersistenceMigrationPaths(
    ulong SourceGeneration,
    ulong TargetGeneration,
    WorldPersistencePaths Source,
    WorldPersistencePaths Staging,
    WorldPersistencePaths Target);

public static class PersistenceGenerationMigration
{
    public static PersistenceMigrationPaths Prepare(
        string root,
        OpaqueId128 worldId,
        ulong sourceGeneration)
    {
        if (sourceGeneration == 0)
            throw new ArgumentOutOfRangeException(nameof(sourceGeneration), "PersistenceGeneration starts at 1.");
        if (sourceGeneration == ulong.MaxValue)
            throw new OverflowException("PersistenceGeneration cannot wrap.");

        var source = PersistenceLayout.Resolve(root, worldId, sourceGeneration);
        if (!Directory.Exists(source.GenerationDirectory))
            throw new InvalidDataException("persistence.migration-source-generation-missing");
        if (PersistenceLayout.ReadCurrent(source) != sourceGeneration)
            throw new InvalidDataException("persistence.migration-source-not-current");

        var targetGeneration = checked(sourceGeneration + 1);
        var target = PersistenceLayout.Resolve(root, worldId, targetGeneration);
        if (Directory.Exists(target.GenerationDirectory))
            throw new InvalidDataException("persistence.migration-target-generation-exists");

        var generationsDirectory = Path.Combine(source.WorldDirectory, "generations");
        var stagingDirectory = Path.Combine(generationsDirectory, $".staging-{targetGeneration:x16}");
        if (Directory.Exists(stagingDirectory))
            throw new InvalidDataException("persistence.migration-staging-exists");

        var staging = new WorldPersistencePaths(
            source.Root,
            source.WorldDirectory,
            source.CurrentPath,
            stagingDirectory,
            Path.Combine(stagingDirectory, "world.sqlite3"),
            Path.Combine(stagingDirectory, "snapshots"));
        PersistenceLayout.EnsureGenerationDirectories(staging);
        return new PersistenceMigrationPaths(sourceGeneration, targetGeneration, source, staging, target);
    }

    public static async Task FinalizeValidatedAsync(
        PersistenceMigrationPaths migration,
        Func<WorldPersistencePaths, CancellationToken, Task> validateStaging,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(migration);
        ArgumentNullException.ThrowIfNull(validateStaging);
        cancellationToken.ThrowIfCancellationRequested();

        if (PersistenceLayout.ReadCurrent(migration.Source) != migration.SourceGeneration)
            throw new InvalidDataException("persistence.migration-source-no-longer-current");
        if (!Directory.Exists(migration.Staging.GenerationDirectory))
            throw new InvalidDataException("persistence.migration-staging-missing");
        if (Directory.Exists(migration.Target.GenerationDirectory))
            throw new InvalidDataException("persistence.migration-target-generation-exists");

        await validateStaging(migration.Staging, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        DurableFileSystem.AtomicMoveDirectory(
            migration.Staging.GenerationDirectory,
            migration.Target.GenerationDirectory);

        // If CURRENT replacement fails, sourceGeneration remains authoritative. The finalized
        // target directory is retained as an orphan candidate for operator inspection/retry;
        // this method never performs an implicit rollback or deletes the source generation.
        await PersistenceLayout.WriteCurrentAsync(migration.Target, migration.TargetGeneration, cancellationToken);
    }
}
