using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace MachiVerse.Simulation.Core.Persistence;

/// <summary>
/// Reads a staged Snapshot in physical chunk order and yields canonical section fragments without
/// collecting the complete Snapshot. Each MVCHNK01 file is validated/decompressed independently;
/// global section/fragment ordering is checked incrementally across chunk boundaries.
/// </summary>
public static class CanonicalSnapshotStagedFragmentStreamV1
{
    public static async IAsyncEnumerable<SnapshotSectionFragmentMaterialV1> ReadValidatedAsync(
        SnapshotPhysicalPaths physical,
        PhysicalSnapshotManifestMaterialV1 manifest,
        IEnumerable<ISnapshotChunkCompressionDecoderV1>? compressionDecoders = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(physical);
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.Logical.SnapshotId != physical.SnapshotId)
            throw new InvalidDataException("persistence.snapshot.manifest-physical-id-mismatch");
        if (manifest.Chunks.Count == 0)
            throw new InvalidDataException("persistence.snapshot.no-physical-chunks");

        string? previousSection = null;
        uint previousIndex = 0;
        uint previousCount = 0;
        var anyFragment = false;

        for (var chunkIndex = 0; chunkIndex < manifest.Chunks.Count; chunkIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var descriptor = manifest.Chunks[chunkIndex];
            if (descriptor.ChunkIndex != checked((uint)chunkIndex))
                throw new InvalidDataException("persistence.snapshot.chunk-index-gap");
            SnapshotChunkFile.ValidateRelativePath(descriptor.RelativePath, descriptor.ChunkIndex);

            var path = Path.Combine(
                physical.StagingDirectory,
                descriptor.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var decoded = await CanonicalSnapshotChunkFileV1.ReadValidatedAsync(
                path,
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
                ValidateNextFragment(
                    fragment,
                    ref previousSection,
                    ref previousIndex,
                    ref previousCount,
                    anyFragment);
                anyFragment = true;
                yield return fragment;
            }
        }

        if (!anyFragment)
            throw new InvalidDataException("persistence.snapshot-fragment-invalid:no-fragments");
        if (checked(previousIndex + 1) != previousCount)
            throw new InvalidDataException("persistence.snapshot-fragment-invalid:global-fragment-incomplete");
    }

    private static void ValidateNextFragment(
        SnapshotSectionFragmentMaterialV1 fragment,
        ref string? previousSection,
        ref uint previousIndex,
        ref uint previousCount,
        bool hasPrevious)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        if (fragment.FragmentCount == 0 || fragment.FragmentIndex >= fragment.FragmentCount)
            throw new InvalidDataException("persistence.snapshot-fragment-invalid:index-range");

        if (!hasPrevious)
        {
            if (fragment.FragmentIndex != 0)
                throw new InvalidDataException("persistence.snapshot-fragment-invalid:global-fragment-start");
        }
        else
        {
            var order = string.CompareOrdinal(previousSection, fragment.SectionId);
            if (order > 0)
                throw new InvalidDataException("persistence.snapshot-fragment-invalid:global-section-order");
            if (order == 0)
            {
                if (fragment.FragmentCount != previousCount ||
                    fragment.FragmentIndex != checked(previousIndex + 1))
                    throw new InvalidDataException("persistence.snapshot-fragment-invalid:global-fragment-order");
            }
            else
            {
                if (checked(previousIndex + 1) != previousCount)
                    throw new InvalidDataException("persistence.snapshot-fragment-invalid:global-fragment-incomplete");
                if (fragment.FragmentIndex != 0)
                    throw new InvalidDataException("persistence.snapshot-fragment-invalid:global-fragment-start");
            }
        }

        previousSection = fragment.SectionId;
        previousIndex = fragment.FragmentIndex;
        previousCount = fragment.FragmentCount;
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
}
