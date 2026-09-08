using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;

internal static class PersistenceMigrationSmoke
{
    internal static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "machiverse-sim03-migration-" + Guid.NewGuid().ToString("N"));
        try
        {
            var worldId = OpaqueId128.Parse("00000000000000000000000000000041");
            var source = PersistenceLayout.Resolve(root, worldId, 1);
            PersistenceLayout.EnsureGenerationDirectories(source);
            await File.WriteAllTextAsync(source.DatabasePath, "source-generation");
            await PersistenceLayout.WriteCurrentAsync(source, 1);

            var migration = PersistenceGenerationMigration.Prepare(root, worldId, 1);
            if (migration.TargetGeneration != 2 || !Directory.Exists(migration.Staging.GenerationDirectory))
                throw new InvalidOperationException("Migration staging generation was not prepared canonically.");
            if (PersistenceLayout.ReadCurrent(source) != 1)
                throw new InvalidOperationException("Preparing migration must not change CURRENT.");

            await File.WriteAllTextAsync(migration.Staging.DatabasePath, "target-generation");
            await PersistenceGenerationMigration.FinalizeValidatedAsync(
                migration,
                static (staging, _) =>
                {
                    if (!File.Exists(staging.DatabasePath))
                        throw new InvalidDataException("persistence.migration-staging-database-missing");
                    return Task.CompletedTask;
                });

            if (PersistenceLayout.ReadCurrent(source) != 2)
                throw new InvalidOperationException("Validated migration must switch CURRENT to target generation.");
            if (!Directory.Exists(source.GenerationDirectory))
                throw new InvalidOperationException("Source generation must remain available after migration switch.");
            if (!Directory.Exists(migration.Target.GenerationDirectory))
                throw new InvalidOperationException("Target generation must be finalized before CURRENT switch.");
            if (Directory.Exists(migration.Staging.GenerationDirectory))
                throw new InvalidOperationException("Staging generation must be consumed by finalization.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
