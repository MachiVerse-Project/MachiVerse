using System.Security.Cryptography;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed record CanonicalSnapshotRecoveredSectionV1(
    string SectionId,
    SchemaRefV1 SectionSchema,
    ulong LogicalItemCount,
    byte[] LogicalContentDigest,
    uint FragmentCount);

public sealed record CanonicalSnapshotDurableRecoveryResultV1(
    SnapshotCatalogEntry Catalog,
    PhysicalSnapshotManifestMaterialV1 Manifest,
    IReadOnlyList<CanonicalSnapshotRecoveredSectionV1> Sections,
    int ChunkCount,
    ulong FragmentCount);

/// <summary>
/// Bounded-memory recovery boundary for one committed canonical Snapshot. The recovery authority is
/// exclusively the SQLite snapshot catalog plus the final manifest.pb/chunk files selected from the
/// current persistence generation. Every physical chunk is decoded and every canonical fragment is
/// consumed, but fragment payloads are not retained after validation. Schema-owner semantic restore
/// and authoritative WorldState rehash remain a later recovery stage.
/// </summary>
public static class CanonicalSnapshotDurableRecoveryV1
{
    public static async Task<CanonicalSnapshotDurableRecoveryResultV1> RecoverNewestAsync(
        SqlitePersistenceStore store,
        WorldPersistencePaths world,
        IEnumerable<ISnapshotChunkCompressionDecoderV1>? compressionDecoders = null,
        RequiredAddonSnapshotMetadataCodecRegistryV1? addonCodecs = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(world);

        CanonicalSnapshotDurableRecoveryResultV1? recovered = null;
        var selected = await store.SelectRecoverySnapshotAsync(
            world,
            async (directory, candidate, token) =>
            {
                recovered = await RecoverCandidateAsync(
                    directory,
                    candidate,
                    compressionDecoders,
                    addonCodecs,
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        if (selected is null || recovered is null)
            throw new InvalidDataException("persistence.snapshot.no-usable-recovery-candidate");
        if (selected.SnapshotId != recovered.Catalog.SnapshotId)
            throw new InvalidDataException("persistence.snapshot.recovery-selection-drift");
        return recovered;
    }

    private static async Task<CanonicalSnapshotDurableRecoveryResultV1> RecoverCandidateAsync(
        string finalDirectory,
        SnapshotCatalogEntry candidate,
        IEnumerable<ISnapshotChunkCompressionDecoderV1>? compressionDecoders,
        RequiredAddonSnapshotMetadataCodecRegistryV1? addonCodecs,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(finalDirectory);
        ArgumentNullException.ThrowIfNull(candidate);

        var manifestPath = Path.Combine(finalDirectory, "manifest.pb");
        var chunksDirectory = Path.Combine(finalDirectory, "chunks");
        if (!File.Exists(manifestPath) || !Directory.Exists(chunksDirectory))
            throw new InvalidDataException("persistence.snapshot.recovery-material-missing");

        var manifestBytes = await File.ReadAllBytesAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        var manifest = PhysicalSnapshotManifestWireCodecV1.Decode(manifestBytes, addonCodecs);
        RequireCatalogBinding(candidate, manifest);

        var logicalSections = manifest.Logical.Sections;
        if (logicalSections.Count != SnapshotManifestValidation.StandardRequiredSectionCount)
            throw new InvalidDataException("persistence.snapshot.section-count-mismatch");
        var accumulators = logicalSections
            .Select(static section => new SectionAccumulator(section))
            .ToArray();
        var sectionIndex = logicalSections
            .Select(static (section, index) => (section.SectionId, Index: index))
            .ToDictionary(static value => value.SectionId, static value => value.Index, StringComparer.Ordinal);

        var actualFiles = Directory.GetFiles(chunksDirectory)
            .OrderBy(static path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToArray();
        if (actualFiles.Length != manifest.Chunks.Count)
            throw new InvalidDataException("persistence.snapshot.recovery-chunk-count-mismatch");

        var currentSectionIndex = -1;
        ulong recoveredFragmentCount = 0;
        for (var chunkIndex = 0; chunkIndex < manifest.Chunks.Count; chunkIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var descriptor = manifest.Chunks[chunkIndex];
            if (descriptor.ChunkIndex != checked((uint)chunkIndex))
                throw new InvalidDataException("persistence.snapshot.chunk-index-gap");
            SnapshotChunkFile.ValidateRelativePath(descriptor.RelativePath, descriptor.ChunkIndex);

            var expectedPath = Path.Combine(
                finalDirectory,
                descriptor.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!string.Equals(
                    Path.GetFullPath(actualFiles[chunkIndex]),
                    Path.GetFullPath(expectedPath),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidDataException("persistence.snapshot.recovery-chunk-path-mismatch");

            var decoded = await CanonicalSnapshotChunkFileV1.ReadValidatedAsync(
                expectedPath,
                compressionDecoders,
                cancellationToken).ConfigureAwait(false);
            RequireDescriptorHeaderMatch(descriptor, decoded.Header);
            if (decoded.Payload.Fragments.Count == 0)
                throw new InvalidDataException($"persistence.snapshot.manifest-chunk-empty:{descriptor.ChunkIndex}");
            if (!string.Equals(decoded.Payload.Fragments[0].SectionId, descriptor.FirstSectionId, StringComparison.Ordinal) ||
                !string.Equals(decoded.Payload.Fragments[^1].SectionId, descriptor.LastSectionId, StringComparison.Ordinal))
                throw new InvalidDataException($"persistence.snapshot.manifest-chunk-section-range-mismatch:{descriptor.ChunkIndex}");

            foreach (var fragment in decoded.Payload.Fragments)
            {
                if (!sectionIndex.TryGetValue(fragment.SectionId, out var recoveredSectionIndex))
                    throw new InvalidDataException($"persistence.snapshot.recovery-section-unexpected:{fragment.SectionId}");

                if (currentSectionIndex < 0)
                {
                    if (recoveredSectionIndex != 0)
                        throw new InvalidDataException("persistence.snapshot.recovery-section-start-mismatch");
                    currentSectionIndex = 0;
                }
                else if (recoveredSectionIndex != currentSectionIndex)
                {
                    accumulators[currentSectionIndex].RequireComplete();
                    if (recoveredSectionIndex != checked(currentSectionIndex + 1))
                        throw new InvalidDataException("persistence.snapshot.recovery-section-order-mismatch");
                    currentSectionIndex = recoveredSectionIndex;
                }

                accumulators[currentSectionIndex].Accept(fragment);
                recoveredFragmentCount = checked(recoveredFragmentCount + 1UL);
            }
        }

        if (currentSectionIndex < 0)
            throw new InvalidDataException("persistence.snapshot-fragment-invalid:no-fragments");
        accumulators[currentSectionIndex].RequireComplete();
        if (currentSectionIndex != logicalSections.Count - 1)
            throw new InvalidDataException("persistence.snapshot.recovery-section-set-incomplete");

        var recoveredSections = accumulators
            .Select(static accumulator => accumulator.ToRecoveredSection())
            .ToArray();
        if (recoveredSections.Length != SnapshotManifestValidation.StandardRequiredSectionCount ||
            !recoveredSections.Select(static section => section.SectionId)
                .SequenceEqual(StandardSnapshotSectionSetV1.SectionIds, StringComparer.Ordinal))
            throw new InvalidDataException("persistence.snapshot.required-section-set-mismatch");

        for (var i = 0; i < recoveredSections.Length; i++)
        {
            var recoveredSection = recoveredSections[i];
            if (StandardDomainPartitionRegistry.TryGet(recoveredSection.SectionId, out var identity) && identity is not null &&
                recoveredSection.SectionSchema != identity.PartitionSchema)
                throw new InvalidDataException($"persistence.snapshot.partition-schema-mismatch:{recoveredSection.SectionId}");
        }

        return new CanonicalSnapshotDurableRecoveryResultV1(
            candidate with
            {
                HistoryAnchorDigest = candidate.HistoryAnchorDigest.ToArray(),
                StateContinuityToken = candidate.StateContinuityToken.ToArray(),
                SnapshotDigest = candidate.SnapshotDigest.ToArray(),
                PhysicalManifestDigest = candidate.PhysicalManifestDigest.ToArray(),
            },
            manifest,
            Array.AsReadOnly(recoveredSections),
            manifest.Chunks.Count,
            recoveredFragmentCount);
    }

    private static void RequireCatalogBinding(
        SnapshotCatalogEntry candidate,
        PhysicalSnapshotManifestMaterialV1 manifest)
    {
        var logical = manifest.Logical;
        if (logical.SnapshotId != candidate.SnapshotId ||
            logical.SnapshotStep != candidate.SnapshotStep ||
            logical.HistoryAnchorSequence != candidate.HistoryAnchorSequence ||
            !CryptographicOperations.FixedTimeEquals(logical.HistoryAnchorDigest, candidate.HistoryAnchorDigest) ||
            !CryptographicOperations.FixedTimeEquals(logical.StateContinuityToken, candidate.StateContinuityToken) ||
            !CryptographicOperations.FixedTimeEquals(logical.SnapshotDigest, candidate.SnapshotDigest) ||
            !CryptographicOperations.FixedTimeEquals(manifest.PhysicalManifestDigest, candidate.PhysicalManifestDigest))
            throw new InvalidDataException("persistence.snapshot.recovery-catalog-manifest-mismatch");
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

    private sealed class SectionAccumulator
    {
        private readonly LogicalSnapshotSection _logical;
        private uint? _fragmentCount;
        private uint _seenFragments;
        private ulong _itemCount;
        private byte[]? _previousLastRecordId;

        public SectionAccumulator(LogicalSnapshotSection logical)
        {
            _logical = logical ?? throw new ArgumentNullException(nameof(logical));
        }

        public void Accept(SnapshotSectionFragmentMaterialV1 fragment)
        {
            ArgumentNullException.ThrowIfNull(fragment);
            if (!string.Equals(fragment.SectionId, _logical.SectionId, StringComparison.Ordinal))
                throw new InvalidDataException($"persistence.snapshot.fragment-section-mismatch:{_logical.SectionId}");
            if (fragment.FragmentCount == 0 || fragment.FragmentIndex >= fragment.FragmentCount)
                throw new InvalidDataException($"persistence.snapshot-fragment-invalid:{_logical.SectionId}");
            if (_fragmentCount is null)
                _fragmentCount = fragment.FragmentCount;
            if (_fragmentCount.Value != fragment.FragmentCount || fragment.FragmentIndex != _seenFragments)
                throw new InvalidDataException($"persistence.snapshot-fragment-invalid:{_logical.SectionId}");
            if (fragment.FragmentPayload is null || fragment.FragmentPayload.Length > CanonicalSnapshotSectionValidationV1.HardMaxUncompressedBytes)
                throw new InvalidDataException($"persistence.snapshot-fragment-invalid:{_logical.SectionId}");

            var hasFirst = fragment.FirstRecordId is not null;
            var hasLast = fragment.LastRecordId is not null;
            if (hasFirst != hasLast)
                throw new InvalidDataException($"persistence.snapshot.fragment-record-range-incomplete:{_logical.SectionId}");
            if (hasFirst)
            {
                if (fragment.FirstRecordId!.Length != CanonicalSnapshotSectionValidationV1.RecordIdLength ||
                    fragment.LastRecordId!.Length != CanonicalSnapshotSectionValidationV1.RecordIdLength)
                    throw new InvalidDataException($"persistence.snapshot.fragment-record-id-length:{_logical.SectionId}");
                if (fragment.FirstRecordId.AsSpan().SequenceCompareTo(fragment.LastRecordId) > 0)
                    throw new InvalidDataException($"persistence.snapshot.fragment-record-range-reversed:{_logical.SectionId}");
                if (_previousLastRecordId is not null &&
                    _previousLastRecordId.AsSpan().SequenceCompareTo(fragment.FirstRecordId) >= 0)
                    throw new InvalidDataException($"persistence.snapshot.fragment-record-range-overlap:{_logical.SectionId}");
                _previousLastRecordId = fragment.LastRecordId.ToArray();
            }

            _itemCount = checked(_itemCount + fragment.ItemCount);
            _seenFragments = checked(_seenFragments + 1U);
        }

        public void RequireComplete()
        {
            if (_fragmentCount is null || _seenFragments != _fragmentCount.Value)
                throw new InvalidDataException($"persistence.snapshot.recovery-fragment-set-incomplete:{_logical.SectionId}");
            if (_itemCount != _logical.LogicalItemCount)
                throw new InvalidDataException($"persistence.snapshot.fragment-item-count-mismatch:{_logical.SectionId}");
        }

        public CanonicalSnapshotRecoveredSectionV1 ToRecoveredSection()
        {
            RequireComplete();
            return new CanonicalSnapshotRecoveredSectionV1(
                _logical.SectionId,
                new SchemaRefV1(_logical.SchemaId, _logical.SchemaMajor, _logical.SchemaMinor),
                _logical.LogicalItemCount,
                _logical.LogicalContentDigest.ToArray(),
                _fragmentCount!.Value);
        }
    }
}
