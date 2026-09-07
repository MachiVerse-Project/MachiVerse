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
            Directory.CreateDirectory(Path.Combine(sourceSnapshot, "chunks"));
            await File.WriteAllBytesAsync(Path.Combine(sourceSnapshot, "manifest.pb"), new byte[] { 1, 2, 3 });
            await File.WriteAllBytesAsync(Path.Combine(sourceSnapshot, "chunks", "00000000.mvchunk"), new byte[] { 4, 5, 6 });

            var exportFinal = Path.Combine(root, "portable-export");
            var export = PortableWorldExport.Prepare(exportFinal);
            await PortableWorldBundleV1.CopyCommittedSnapshotAsync(export, sourceSnapshot);
            await PortableWorldBundleV1.WriteManifestAsync(export, new byte[] { 20, 21, 22 });

            var segmentDigest = Enumerable.Repeat((byte)0x5a, 32).ToArray();
            await PortableWorldBundleV1.WriteHistorySegmentAsync(
                export,
                segmentIndex: 0,
                firstSequence: 6,
                serializedRecords: new ReadOnlyMemory<byte>[]
                {
                    new byte[] { 10, 11, 12 },
                    new byte[] { 13, 14 }
                },
                segmentLogicalDigest: segmentDigest);

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
                async (stagingDirectory, cancellationToken) =>
                {
                    PortableWorldBundleV1.ValidateBundleStructure(stagingDirectory);
                    var header = await PortableWorldBundleV1.ValidateHistorySegmentAsync(
                        Path.Combine(stagingDirectory, "history", "00000000.mvlog"),
                        cancellationToken);
                    if (header.FirstSequence != 6 || header.LastSequence != 7 || header.RecordCount != 2)
                        throw new InvalidDataException("fixture.export-history-range-invalid");
                    if (!header.LogicalDigest.SequenceEqual(segmentDigest))
                        throw new InvalidDataException("fixture.export-history-digest-invalid");
                });

            if (!Directory.Exists(exportFinal) || Directory.Exists(export.StagingDirectory))
                throw new InvalidOperationException("Validated portable export must publish atomically.");
            if (!File.Exists(Path.Combine(exportFinal, "export-manifest.pb")))
                throw new InvalidOperationException("Standard portable export manifest is missing.");
            if (!File.Exists(Path.Combine(exportFinal, "history", "00000000.mvlog")))
                throw new InvalidOperationException("Standard MVLOG001 history segment is missing.");
            PortableWorldBundleV1.ValidateBundleStructure(exportFinal);

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
                static async (exportDirectory, expectedWorldId, cancellationToken) =>
                {
                    PortableWorldBundleV1.ValidateBundleStructure(exportDirectory);
                    if (expectedWorldId.IsZero)
                        throw new InvalidDataException("fixture.export-world-id-invalid");
                    _ = await PortableWorldBundleV1.ValidateHistorySegmentAsync(
                        Path.Combine(exportDirectory, "history", "00000000.mvlog"),
                        cancellationToken);
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
