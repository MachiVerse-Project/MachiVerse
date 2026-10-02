using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04ProductionGate3Step7NegativeRecoveryProofV1(
    ulong SnapshotStep,
    uint TamperedChunkIndex,
    string MissingSectionId,
    ulong StaleGeneration,
    ulong CurrentGeneration,
    string WrongOwnerSectionId);

/// <summary>
/// Gate3 Step 7 negative recovery proof. The committed production Snapshot is never mutated.
/// Negative cases are derived from its durable exact-103 recovery material and exercised only on
/// isolated copies or validation projections through existing fail-closed Persistence guards.
/// No new recovery/failure semantics are defined here.
/// </summary>
public static class Qa04ProductionGate3Step7NegativeRecoveryProofRunnerV1
{
    public static async Task<Qa04ProductionGate3Step7NegativeRecoveryProofV1> VerifyAsync(
        Qa04ProductionExact103SnapshotPersistenceProofV1 persisted,
        SqlitePersistenceStore store,
        WorldPersistencePaths world,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(persisted);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(world);

        var decoders = CanonicalSnapshotProductionPhysicalDrainV1.ProductionDecoders();
        var recovered = await CanonicalSnapshotDurableRecoveryV1.RecoverNewestAsync(
            store,
            world,
            decoders,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (recovered.Catalog.SnapshotStep != persisted.SnapshotStep ||
            recovered.Sections.Count != SnapshotManifestValidation.StandardRequiredSectionCount ||
            recovered.Sections.Count != 103 ||
            recovered.Manifest.Chunks.Count == 0 ||
            !CryptographicOperations.FixedTimeEquals(recovered.Catalog.SnapshotDigest, persisted.SnapshotDigest) ||
            !CryptographicOperations.FixedTimeEquals(recovered.Catalog.PhysicalManifestDigest, persisted.PhysicalManifestDigest))
            throw new InvalidDataException("qa04.gate3.step7.production-snapshot-identity-mismatch");

        var finalDirectory = Path.GetFullPath(Path.Combine(
            world.GenerationDirectory,
            recovered.Catalog.RelativeDirectory.Replace('/', Path.DirectorySeparatorChar)));
        var manifestPath = Path.Combine(finalDirectory, "manifest.pb");
        if (!File.Exists(manifestPath))
            throw new InvalidDataException("qa04.gate3.step7.production-manifest-missing");

        var negativeRoot = Path.Combine(
            Path.GetTempPath(),
            "machiverse-qa04-gate3-step7-" + Guid.NewGuid().ToString("N"));
        try
        {
            var tamperedChunkIndex = await ProveTamperedChunkRejectedAsync(
                recovered,
                finalDirectory,
                negativeRoot,
                cancellationToken).ConfigureAwait(false);

            var missingSectionId = ProveMissingRequiredSectionRejected(recovered.Sections);

            var (staleGeneration, currentGeneration) = await ProveStaleGenerationNotSelectedAsync(
                recovered,
                manifestPath,
                negativeRoot,
                decoders,
                cancellationToken).ConfigureAwait(false);

            var wrongOwnerSectionId = ProveWrongOwnerRejected(recovered.Sections);

            return new Qa04ProductionGate3Step7NegativeRecoveryProofV1(
                recovered.Catalog.SnapshotStep,
                tamperedChunkIndex,
                missingSectionId,
                staleGeneration,
                currentGeneration,
                wrongOwnerSectionId);
        }
        finally
        {
            if (Directory.Exists(negativeRoot))
                Directory.Delete(negativeRoot, recursive: true);
        }
    }

    private static async Task<uint> ProveTamperedChunkRejectedAsync(
        CanonicalSnapshotDurableRecoveryResultV1 recovered,
        string finalDirectory,
        string negativeRoot,
        CancellationToken cancellationToken)
    {
        var descriptor = recovered.Manifest.Chunks[0];
        SnapshotChunkFile.ValidateRelativePath(descriptor.RelativePath, descriptor.ChunkIndex);
        var sourcePath = Path.Combine(
            finalDirectory,
            descriptor.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(sourcePath))
            throw new InvalidDataException("qa04.gate3.step7.production-chunk-missing");

        var tamperedPath = Path.Combine(negativeRoot, "tamper", Path.GetFileName(sourcePath));
        Directory.CreateDirectory(Path.GetDirectoryName(tamperedPath)!);
        File.Copy(sourcePath, tamperedPath, overwrite: false);

        await using (var stream = new FileStream(
            tamperedPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            if (stream.Length <= SnapshotChunkFile.HeaderLength)
                throw new InvalidDataException("qa04.gate3.step7.production-chunk-payload-empty");
            stream.Position = SnapshotChunkFile.HeaderLength;
            var original = stream.ReadByte();
            if (original < 0)
                throw new InvalidDataException("qa04.gate3.step7.production-chunk-payload-empty");
            stream.Position = SnapshotChunkFile.HeaderLength;
            stream.WriteByte((byte)(original ^ 0x01));
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }

        await ExpectInvalidAsync(
            () => SnapshotChunkFile.ValidateAsync(tamperedPath, cancellationToken),
            "persistence.snapshot.stored-digest-mismatch").ConfigureAwait(false);
        return descriptor.ChunkIndex;
    }

    private static string ProveMissingRequiredSectionRejected(
        IReadOnlyList<CanonicalSnapshotRecoveredSectionV1> recoveredSections)
    {
        var material = recoveredSections
            .Select(static section => ToValidationMaterial(section))
            .ToArray();
        var missingSection = recoveredSections.First(static section =>
            StandardDomainPartitionRegistry.TryGet(section.SectionId, out _));
        var missing = material
            .Where(section => !string.Equals(section.SectionId, missingSection.SectionId, StringComparison.Ordinal))
            .ToArray();

        ExpectInvalid(
            () => CanonicalSnapshotSectionValidationV1.ValidateStandard(missing),
            "persistence.snapshot.section-count-mismatch");
        return missingSection.SectionId;
    }

    private static async Task<(ulong StaleGeneration, ulong CurrentGeneration)> ProveStaleGenerationNotSelectedAsync(
        CanonicalSnapshotDurableRecoveryResultV1 recovered,
        string productionManifestPath,
        string negativeRoot,
        IEnumerable<ISnapshotChunkCompressionDecoderV1> decoders,
        CancellationToken cancellationToken)
    {
        const ulong staleGeneration = 1;
        const ulong currentGeneration = 2;
        var staleRoot = Path.Combine(negativeRoot, "stale-generation");
        var stale = PersistenceLayout.Resolve(staleRoot, recovered.Manifest.Logical.WorldId, staleGeneration);
        var current = PersistenceLayout.Resolve(staleRoot, recovered.Manifest.Logical.WorldId, currentGeneration);
        PersistenceLayout.EnsureGenerationDirectories(stale);
        PersistenceLayout.EnsureGenerationDirectories(current);

        var staleSnapshotDirectory = Path.Combine(
            stale.GenerationDirectory,
            recovered.Catalog.RelativeDirectory.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(staleSnapshotDirectory);
        File.Copy(
            productionManifestPath,
            Path.Combine(staleSnapshotDirectory, "manifest.pb"),
            overwrite: false);

        await PersistenceLayout.WriteCurrentAsync(stale, currentGeneration, cancellationToken).ConfigureAwait(false);
        var selectedGeneration = PersistenceLayout.ReadCurrent(stale);
        if (selectedGeneration != currentGeneration)
            throw new InvalidDataException("qa04.gate3.step7.current-generation-selection-mismatch");

        var selected = PersistenceLayout.Resolve(staleRoot, recovered.Manifest.Logical.WorldId, selectedGeneration);
        if (!string.Equals(
                Path.GetFullPath(selected.GenerationDirectory),
                Path.GetFullPath(current.GenerationDirectory),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("qa04.gate3.step7.current-generation-resolution-mismatch");
        if (string.Equals(
                Path.GetFullPath(selected.GenerationDirectory),
                Path.GetFullPath(stale.GenerationDirectory),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("qa04.gate3.step7.stale-generation-selected");

        await using var currentStore = await SqlitePersistenceStore.OpenOrCreateAsync(
            selected,
            cancellationToken).ConfigureAwait(false);
        await ExpectInvalidAsync(
            async () =>
            {
                _ = await CanonicalSnapshotDurableRecoveryV1.RecoverNewestAsync(
                    currentStore,
                    selected,
                    decoders,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            },
            "persistence.snapshot.no-usable-recovery-candidate").ConfigureAwait(false);

        return (staleGeneration, currentGeneration);
    }

    private static string ProveWrongOwnerRejected(
        IReadOnlyList<CanonicalSnapshotRecoveredSectionV1> recoveredSections)
    {
        var target = recoveredSections.First(static section =>
            StandardDomainPartitionRegistry.TryGet(section.SectionId, out _));
        var targetIdentity = StandardDomainPartitionRegistry.Get(target.SectionId);
        var wrongOwner = StandardDomainPartitionRegistry.Entries.First(candidate =>
            !string.Equals(
                candidate.OwnerDomain.Value,
                targetIdentity.OwnerDomain.Value,
                StringComparison.Ordinal));

        var material = recoveredSections
            .Select(section => ToValidationMaterial(
                section,
                string.Equals(section.SectionId, target.SectionId, StringComparison.Ordinal)
                    ? wrongOwner.PartitionSchema
                    : section.SectionSchema))
            .ToArray();

        ExpectInvalid(
            () => CanonicalSnapshotSectionValidationV1.ValidateStandard(material),
            $"persistence.snapshot.partition-schema-mismatch:{target.SectionId}");
        return target.SectionId;
    }

    private static CanonicalSnapshotSectionMaterialV1 ToValidationMaterial(
        CanonicalSnapshotRecoveredSectionV1 recovered,
        SchemaRefV1? sectionSchema = null)
    {
        var fragment = new SnapshotSectionFragmentMaterialV1(
            recovered.SectionId,
            0,
            1,
            null,
            null,
            recovered.LogicalItemCount,
            Array.Empty<byte>());
        return new CanonicalSnapshotSectionMaterialV1(
            recovered.SectionId,
            sectionSchema ?? recovered.SectionSchema,
            recovered.LogicalItemCount,
            recovered.LogicalContentDigest.ToArray(),
            new[] { fragment });
    }

    private static async Task ExpectInvalidAsync(
        Func<Task> action,
        string expectedMessage)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (InvalidDataException ex) when (string.Equals(ex.Message, expectedMessage, StringComparison.Ordinal))
        {
            return;
        }

        throw new InvalidOperationException($"Expected InvalidDataException '{expectedMessage}'.");
    }

    private static void ExpectInvalid(Action action, string expectedMessage)
    {
        try
        {
            action();
        }
        catch (InvalidDataException ex) when (string.Equals(ex.Message, expectedMessage, StringComparison.Ordinal))
        {
            return;
        }

        throw new InvalidOperationException($"Expected InvalidDataException '{expectedMessage}'.");
    }
}
