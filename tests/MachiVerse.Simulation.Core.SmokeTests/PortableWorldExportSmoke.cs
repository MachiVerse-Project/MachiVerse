using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;

internal static class PortableWorldExportSmoke
{
    internal static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "machiverse-sim03-export-" + Guid.NewGuid().ToString("N"));
        try
        {
            var sourceSnapshot = Path.Combine(root, "source-snapshot");
            Directory.CreateDirectory(sourceSnapshot);
            await File.WriteAllBytesAsync(Path.Combine(sourceSnapshot, "snapshot-payload.bin"), new byte[] { 1, 2, 3, 4 });

            var exportFinal = Path.Combine(root, "portable-export");
            var export = PortableWorldExport.Prepare(exportFinal);
            await PortableWorldExport.CopyDirectoryDurablyAsync(export, sourceSnapshot, "fixture/snapshot");
            await PortableWorldExport.WriteArtifactDurablyAsync(export, "fixture/history.bin", new byte[] { 10, 11, 12 });
            await PortableWorldExport.WriteArtifactDurablyAsync(export, "fixture/metadata.bin", new byte[] { 20, 21, 22 });

            var traversalRejected = false;
            try
            {
                _ = PortableWorldExport.ResolveStagingPath(export, "../escape.bin");
            }
            catch (InvalidDataException ex) when (ex.Message == "persistence.export-path-invalid")
            {
                traversalRejected = true;
            }
            if (!traversalRejected)
                throw new InvalidOperationException("Portable export staging must reject path traversal.");

            await PortableWorldExport.FinalizeValidatedAsync(
                export,
                static (stagingDirectory, _) =>
                {
                    if (!File.Exists(Path.Combine(stagingDirectory, "fixture", "metadata.bin")))
                        throw new InvalidDataException("fixture.export-metadata-missing");
                    if (!File.Exists(Path.Combine(stagingDirectory, "fixture", "snapshot", "snapshot-payload.bin")))
                        throw new InvalidDataException("fixture.export-snapshot-missing");
                    if (!File.Exists(Path.Combine(stagingDirectory, "fixture", "history.bin")))
                        throw new InvalidDataException("fixture.export-history-missing");
                    return Task.CompletedTask;
                });

            if (!Directory.Exists(exportFinal) || Directory.Exists(export.StagingDirectory))
                throw new InvalidOperationException("Validated portable export must publish atomically.");
            if (File.Exists(Path.Combine(exportFinal, "export-manifest.pb")) ||
                Directory.EnumerateFiles(exportFinal, "*.mvlog", SearchOption.AllDirectories).Any())
                throw new InvalidOperationException("SIM-03 must not invent an unresolved export bundle format.");

            var persistenceRoot = Path.Combine(root, "persistence");
            var worldId = OpaqueId128.Parse("00000000000000000000000000000051");
            var active = PersistenceLayout.Resolve(persistenceRoot, worldId, 1);
            PersistenceLayout.EnsureGenerationDirectories(active);
            await File.WriteAllTextAsync(active.DatabasePath, "active-generation");
            await PersistenceLayout.WriteCurrentAsync(active, 1);

            var import = PortableWorldImport.PrepareForExistingWorld(exportFinal, persistenceRoot, worldId, 1);
            if (PersistenceLayout.ReadCurrent(active) != 1)
                throw new InvalidOperationException("Preparing import must not alter the active generation.");

            await PortableWorldImport.LoadAndActivateAsync(
                import,
                static (exportDirectory, expectedWorldId, _) =>
                {
                    if (!Directory.Exists(exportDirectory))
                        throw new InvalidDataException("persistence.export-missing");
                    if (expectedWorldId.IsZero)
                        throw new InvalidDataException("fixture.export-world-id-invalid");
                    if (!File.Exists(Path.Combine(exportDirectory, "fixture", "metadata.bin")))
                        throw new InvalidDataException("fixture.export-metadata-missing");
                    return Task.CompletedTask;
                },
                static async (_, staging, cancellationToken) =>
                {
                    await File.WriteAllTextAsync(staging.DatabasePath, "imported-generation", cancellationToken);
                    Directory.CreateDirectory(staging.SnapshotsDirectory);
                },
                static (staging, _) =>
                {
                    if (!File.Exists(staging.DatabasePath))
                        throw new InvalidDataException("persistence.import-staging-database-missing");
                    return Task.CompletedTask;
                },
                worldId);

            if (PersistenceLayout.ReadCurrent(active) != 2)
                throw new InvalidOperationException("Validated import must activate the new persistence generation.");
            if (!Directory.Exists(active.GenerationDirectory))
                throw new InvalidOperationException("Import must not destroy the source active generation.");
            if (!Directory.Exists(import.Migration.Target.GenerationDirectory))
                throw new InvalidOperationException("Imported target generation was not finalized.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
