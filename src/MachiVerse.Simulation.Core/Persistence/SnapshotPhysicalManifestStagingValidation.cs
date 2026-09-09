using System.Security.Cryptography;

namespace MachiVerse.Simulation.Core.Persistence;

/// <summary>
/// Validates the authoritative manifest.pb before canonical Snapshot staging can be finalized.
/// This layer proves logical/physical manifest digests and binds every physical descriptor to the
/// actual staged MVCHNK01 file header. Deeper section semantics remain owned by the canonical
/// reassembly verifier because they require decompression and schema-owner codecs.
/// </summary>
public static class SnapshotPhysicalManifestStagingValidationV1
{
    public static async Task<PhysicalSnapshotManifestMaterialV1> ValidateAsync(
        SnapshotPhysicalPaths physical,
        RequiredAddonSnapshotMetadataCodecRegistryV1? addonCodecs = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(physical);
        if (!File.Exists(physical.StagingManifestPath))
            throw new InvalidDataException("persistence.snapshot-manifest-missing");
        if (!Directory.Exists(physical.StagingChunksDirectory))
            throw new InvalidDataException("persistence.snapshot-chunks-missing");

        var manifestBytes = await File.ReadAllBytesAsync(physical.StagingManifestPath, cancellationToken)
            .ConfigureAwait(false);
        var manifest = PhysicalSnapshotManifestWireCodecV1.Decode(manifestBytes, addonCodecs);
        if (manifest.Logical.SnapshotId != physical.SnapshotId)
            throw new InvalidDataException("persistence.snapshot.manifest-physical-id-mismatch");

        var actualFiles = Directory.GetFiles(physical.StagingChunksDirectory)
            .OrderBy(static path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToArray();
        if (actualFiles.Length != manifest.Chunks.Count)
            throw new InvalidDataException("persistence.snapshot.manifest-chunk-count-mismatch");

        for (var i = 0; i < manifest.Chunks.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var descriptor = manifest.Chunks[i];
            if (descriptor.ChunkIndex != checked((uint)i))
                throw new InvalidDataException("persistence.snapshot.chunk-index-gap");
            SnapshotChunkFile.ValidateRelativePath(descriptor.RelativePath, descriptor.ChunkIndex);

            var expectedName = Path.GetFileName(descriptor.RelativePath);
            if (!string.Equals(Path.GetFileName(actualFiles[i]), expectedName, StringComparison.Ordinal))
                throw new InvalidDataException("persistence.snapshot.manifest-chunk-file-mismatch");

            var expectedPath = Path.Combine(
                physical.StagingDirectory,
                descriptor.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!string.Equals(
                    Path.GetFullPath(actualFiles[i]),
                    Path.GetFullPath(expectedPath),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidDataException("persistence.snapshot.manifest-chunk-path-mismatch");

            var header = await SnapshotChunkFile.ValidateAsync(expectedPath, cancellationToken)
                .ConfigureAwait(false);
            RequireDescriptorHeaderMatch(descriptor, header);
        }

        return manifest;
    }

    public static void RequireExpectedAuthority(
        PhysicalSnapshotManifestMaterialV1 manifest,
        IReadOnlyList<CanonicalSnapshotSectionMaterialV1> expectedSections,
        WorldStateV1? frozenState = null,
        RunningSnapshotCutV1? expectedCut = null,
        ReadOnlyMemory<byte> expectedSnapshotDigest = default,
        ReadOnlyMemory<byte> expectedPhysicalManifestDigest = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(expectedSections);

        if (!expectedSnapshotDigest.IsEmpty)
        {
            if (expectedSnapshotDigest.Length != 32)
                throw new ArgumentException("Expected SnapshotDigest must be 32 bytes.", nameof(expectedSnapshotDigest));
            if (!CryptographicOperations.FixedTimeEquals(expectedSnapshotDigest.Span, manifest.Logical.SnapshotDigest))
                throw new InvalidDataException("persistence.snapshot.manifest-logical-digest-authority-mismatch");
        }
        if (!expectedPhysicalManifestDigest.IsEmpty)
        {
            if (expectedPhysicalManifestDigest.Length != 32)
                throw new ArgumentException("Expected physical manifest digest must be 32 bytes.", nameof(expectedPhysicalManifestDigest));
            if (!CryptographicOperations.FixedTimeEquals(expectedPhysicalManifestDigest.Span, manifest.PhysicalManifestDigest))
                throw new InvalidDataException("persistence.snapshot.manifest-physical-digest-authority-mismatch");
        }

        var expectedLogicalSections = CanonicalSnapshotSectionValidationV1.ToLogicalSections(expectedSections, frozenState);
        if (manifest.Logical.Sections.Count != expectedLogicalSections.Count)
            throw new InvalidDataException("persistence.snapshot.manifest-section-count-mismatch");
        for (var i = 0; i < expectedLogicalSections.Count; i++)
            RequireLogicalSectionMatch(expectedLogicalSections[i], manifest.Logical.Sections[i]);

        var expectedDomains = StandardDomainPartitionRegistry.Entries
            .Select(static entry => entry.OwnerDomain.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        if (!manifest.Logical.RequiredDomains.SequenceEqual(expectedDomains, StringComparer.Ordinal))
            throw new InvalidDataException("persistence.snapshot.manifest-required-domain-set-mismatch");

        if (frozenState is not null)
            RequireFrozenStateMatch(manifest.Logical, frozenState);
        if (expectedCut is not null)
            RequireRunningCutMatch(manifest.Logical, expectedCut);
    }

    private static void RequireDescriptorHeaderMatch(
        PhysicalSnapshotChunkDescriptor descriptor,
        SnapshotChunkHeader header)
    {
        if (descriptor.UncompressedLength != header.UncompressedLength ||
            descriptor.StoredLength != header.StoredLength ||
            descriptor.Compression != header.Compression ||
            !CryptographicOperations.FixedTimeEquals(descriptor.LogicalPayloadDigest, header.LogicalPayloadDigest) ||
            !CryptographicOperations.FixedTimeEquals(descriptor.StoredPayloadDigest, header.StoredPayloadDigest))
            throw new InvalidDataException($"persistence.snapshot.manifest-chunk-header-mismatch:{descriptor.ChunkIndex}");
    }

    private static void RequireLogicalSectionMatch(LogicalSnapshotSection expected, LogicalSnapshotSection actual)
    {
        if (!string.Equals(expected.SectionId, actual.SectionId, StringComparison.Ordinal) ||
            !string.Equals(expected.SchemaId, actual.SchemaId, StringComparison.Ordinal) ||
            expected.SchemaMajor != actual.SchemaMajor ||
            expected.SchemaMinor != actual.SchemaMinor ||
            expected.LogicalItemCount != actual.LogicalItemCount ||
            expected.Required != actual.Required ||
            !CryptographicOperations.FixedTimeEquals(expected.LogicalContentDigest, actual.LogicalContentDigest))
            throw new InvalidDataException($"persistence.snapshot.manifest-section-authority-mismatch:{expected.SectionId}");
    }

    private static void RequireFrozenStateMatch(LogicalSnapshotManifest logical, WorldStateV1 frozenState)
    {
        if (logical.WorldId != frozenState.Header.WorldId ||
            logical.SnapshotStep != frozenState.Header.Step ||
            logical.SimulationConfigGeneration != frozenState.Header.ConfigGeneration ||
            logical.MasterGeneration != frozenState.Header.MasterGeneration ||
            !CryptographicOperations.FixedTimeEquals(logical.SimulationConfigDigest, frozenState.Diagnostic.ConfigDigest))
            throw new InvalidDataException("persistence.snapshot.manifest-frozen-state-mismatch");
    }

    private static void RequireRunningCutMatch(LogicalSnapshotManifest logical, RunningSnapshotCutV1 cut)
    {
        if (logical.WorldId != cut.FrozenState.Header.WorldId ||
            logical.SnapshotId != cut.SnapshotId ||
            logical.SnapshotStep != cut.SnapshotStep ||
            logical.HistoryAnchorSequence != cut.HistoryAnchor.Sequence ||
            !CryptographicOperations.FixedTimeEquals(logical.HistoryAnchorDigest, cut.HistoryAnchor.Digest) ||
            !CryptographicOperations.FixedTimeEquals(logical.StateContinuityToken, cut.StateContinuityToken))
            throw new InvalidDataException("persistence.snapshot.manifest-running-cut-mismatch");
    }
}
