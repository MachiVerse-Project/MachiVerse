using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed record PortableWorldImportPlan(
    string ExportDirectory,
    PersistenceMigrationPaths Migration);

/// <summary>
/// Format-neutral import staging boundary. The concrete export bundle schema and decoder are
/// supplied by the schema-owned caller; this type guarantees that a verified import is loaded
/// into a new persistence generation and CURRENT is switched only after target validation.
/// </summary>
public static class PortableWorldImport
{
    public static PortableWorldImportPlan PrepareForExistingWorld(
        string exportDirectory,
        string persistenceRoot,
        OpaqueId128 worldId,
        ulong activeGeneration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exportDirectory);
        var export = Path.GetFullPath(exportDirectory);
        if (!Directory.Exists(export))
            throw new InvalidDataException("persistence.export-missing");
        var migration = PersistenceGenerationMigration.Prepare(persistenceRoot, worldId, activeGeneration);
        return new PortableWorldImportPlan(export, migration);
    }

    public static async Task LoadAndActivateAsync(
        PortableWorldImportPlan plan,
        Func<string, OpaqueId128, CancellationToken, Task> verifyExport,
        Func<string, WorldPersistencePaths, CancellationToken, Task> loadIntoStaging,
        Func<WorldPersistencePaths, CancellationToken, Task> verifyImportedGeneration,
        OpaqueId128 expectedWorldId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(verifyExport);
        ArgumentNullException.ThrowIfNull(loadIntoStaging);
        ArgumentNullException.ThrowIfNull(verifyImportedGeneration);
        if (expectedWorldId.IsZero)
            throw new ArgumentException("WorldId ZERO is invalid for import.", nameof(expectedWorldId));
        if (!Directory.Exists(plan.ExportDirectory))
            throw new InvalidDataException("persistence.export-missing");

        await verifyExport(plan.ExportDirectory, expectedWorldId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // Only the new staging generation is exposed as a write target. The current source
        // generation stays authoritative until the loaded generation passes full validation.
        await loadIntoStaging(plan.ExportDirectory, plan.Migration.Staging, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        await PersistenceGenerationMigration.FinalizeValidatedAsync(
            plan.Migration,
            verifyImportedGeneration,
            cancellationToken);
    }
}
