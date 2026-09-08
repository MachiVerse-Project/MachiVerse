using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed record SnapshotSectionSemanticVerificationV1(
    ulong LogicalItemCount,
    byte[] LogicalContentDigest);

public sealed record SnapshotSectionSemanticVerifierV1(
    string SectionId,
    SchemaRefV1 SectionSchema,
    Func<IReadOnlyList<SnapshotSectionFragmentMaterialV1>, SnapshotSectionSemanticVerificationV1> Verify);

public sealed class CanonicalSnapshotSemanticVerifierRegistryV1
{
    private readonly IReadOnlyDictionary<string, SnapshotSectionSemanticVerifierV1> _verifiers;

    public CanonicalSnapshotSemanticVerifierRegistryV1(
        IEnumerable<SnapshotSectionSemanticVerifierV1> verifiers)
    {
        ArgumentNullException.ThrowIfNull(verifiers);
        var ordered = verifiers.OrderBy(static value => value.SectionId, StringComparer.Ordinal).ToArray();
        if (ordered.Length != SnapshotManifestValidation.StandardRequiredSectionCount)
            throw new InvalidDataException("persistence.snapshot.semantic-verifier-count-mismatch");

        var map = new Dictionary<string, SnapshotSectionSemanticVerifierV1>(StringComparer.Ordinal);
        for (var i = 0; i < ordered.Length; i++)
        {
            var verifier = ordered[i] ?? throw new InvalidDataException("persistence.snapshot.semantic-verifier-null");
            var sectionId = new StableToken(verifier.SectionId).Value;
            if (!string.Equals(sectionId, StandardSnapshotSectionSetV1.SectionIds[i], StringComparison.Ordinal))
                throw new InvalidDataException("persistence.snapshot.semantic-verifier-set-mismatch");
            if (verifier.SectionSchema.Version.Major == 0)
                throw new InvalidDataException($"persistence.snapshot.semantic-verifier-schema-invalid:{sectionId}");
            ArgumentNullException.ThrowIfNull(verifier.Verify);
            if (!map.TryAdd(sectionId, verifier))
                throw new InvalidDataException("persistence.snapshot.semantic-verifier-duplicate");
        }
        _verifiers = map;
    }

    public void VerifyAll(IReadOnlyList<CanonicalSnapshotSectionMaterialV1> sections)
    {
        ArgumentNullException.ThrowIfNull(sections);
        if (sections.Count != SnapshotManifestValidation.StandardRequiredSectionCount)
            throw new InvalidDataException("persistence.snapshot.section-count-mismatch");

        foreach (var section in sections)
        {
            if (!_verifiers.TryGetValue(section.SectionId, out var verifier))
                throw new InvalidDataException($"persistence.snapshot.semantic-verifier-missing:{section.SectionId}");
            if (verifier.SectionSchema != section.SectionSchema)
                throw new InvalidDataException($"persistence.snapshot.semantic-verifier-schema-mismatch:{section.SectionId}");

            var verified = verifier.Verify(section.Fragments)
                ?? throw new InvalidDataException($"persistence.snapshot.semantic-verifier-null-result:{section.SectionId}");
            if (verified.LogicalContentDigest is null || verified.LogicalContentDigest.Length != 32)
                throw new InvalidDataException($"persistence.snapshot.semantic-verifier-digest-invalid:{section.SectionId}");
            if (verified.LogicalItemCount != section.LogicalItemCount)
                throw new InvalidDataException($"persistence.snapshot.section-semantic-item-count-mismatch:{section.SectionId}");
            if (!CryptographicOperations.FixedTimeEquals(
                    verified.LogicalContentDigest,
                    section.LogicalContentDigest))
                throw new InvalidDataException($"persistence.snapshot.section-semantic-digest-mismatch:{section.SectionId}");
        }
    }
}

public static class SnapshotChunkLogicalPayloadDigestV1
{
    public static byte[] Compute(SnapshotChunkFragmentPayloadV1 payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var fragments = SnapshotChunkFragmentPayloadValidationV1.ValidateCanonical(payload.Fragments);
        return HashSuite.DomainHash("mv.snapshot-chunk-payload.v1", writer =>
        {
            writer.WriteArrayStart((ulong)fragments.Count);
            foreach (var fragment in fragments)
            {
                writer.WriteMapStart(7);
                writer.WriteUnsigned(0); writer.WriteAsciiText(fragment.SectionId);
                writer.WriteUnsigned(1); writer.WriteUnsigned(fragment.FragmentIndex);
                writer.WriteUnsigned(2); writer.WriteUnsigned(fragment.FragmentCount);
                writer.WriteUnsigned(3); WriteOptionalRecordId(writer, fragment.FirstRecordId);
                writer.WriteUnsigned(4); WriteOptionalRecordId(writer, fragment.LastRecordId);
                writer.WriteUnsigned(5); writer.WriteUnsigned(fragment.ItemCount);
                writer.WriteUnsigned(6); writer.WriteBytes(fragment.FragmentPayload);
            }
        });
    }

    private static void WriteOptionalRecordId(MvDcborWriter writer, byte[]? value)
    {
        if (value is null)
        {
            writer.WriteArrayStart(0);
            return;
        }
        if (value.Length != CanonicalSnapshotSectionValidationV1.RecordIdLength)
            throw new InvalidDataException("persistence.snapshot.fragment-record-id-length");
        writer.WriteArrayStart(1);
        writer.WriteBytes(value);
    }
}

public interface ISnapshotChunkCompressionDecoderV1
{
    SnapshotCompression Compression { get; }
    byte[] Decode(ReadOnlyMemory<byte> storedPayload, ulong expectedUncompressedLength);
}

public sealed record CanonicalSnapshotChunkReadV1(
    SnapshotChunkHeader Header,
    SnapshotChunkFragmentPayloadV1 Payload);

public static class CanonicalSnapshotChunkFileV1
{
    public static async Task<SnapshotChunkHeader> WriteUncompressedAsync(
        string path,
        SnapshotChunkFragmentPayloadV1 payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(payload);
        var encoded = SnapshotChunkPayloadWireCodecV1.Encode(payload);
        var logicalDigest = SnapshotChunkLogicalPayloadDigestV1.Compute(payload);
        return await SnapshotChunkFile.WriteAsync(
            path,
            encoded,
            checked((ulong)encoded.Length),
            logicalDigest,
            SnapshotCompression.None,
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task<CanonicalSnapshotChunkReadV1> ReadValidatedAsync(
        string path,
        IEnumerable<ISnapshotChunkCompressionDecoderV1>? compressionDecoders = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var header = await SnapshotChunkFile.ValidateAsync(path, cancellationToken).ConfigureAwait(false);
        var fileBytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        if (fileBytes.Length < SnapshotChunkFile.HeaderLength)
            throw new InvalidDataException("persistence.snapshot.truncated-chunk");
        var stored = fileBytes.AsMemory(SnapshotChunkFile.HeaderLength);
        if ((ulong)stored.Length != header.StoredLength)
            throw new InvalidDataException("persistence.snapshot.chunk-length-mismatch");
        if (!CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(stored.Span),
                header.StoredPayloadDigest))
            throw new InvalidDataException("persistence.snapshot.stored-digest-mismatch");

        byte[] decoded;
        if (header.Compression == SnapshotCompression.None)
        {
            decoded = stored.ToArray();
        }
        else
        {
            var decoder = (compressionDecoders ?? Array.Empty<ISnapshotChunkCompressionDecoderV1>())
                .SingleOrDefault(value => value.Compression == header.Compression);
            if (decoder is null)
                throw new InvalidDataException($"persistence.snapshot.compression-codec-unavailable:{header.Compression.ToString().ToLowerInvariant()}");
            decoded = decoder.Decode(stored, header.UncompressedLength)
                ?? throw new InvalidDataException("persistence.snapshot.compression-decoder-null");
        }

        if ((ulong)decoded.Length != header.UncompressedLength)
            throw new InvalidDataException("persistence.snapshot.uncompressed-length-mismatch");
        var payload = SnapshotChunkPayloadWireCodecV1.Decode(decoded);
        var logicalDigest = SnapshotChunkLogicalPayloadDigestV1.Compute(payload);
        if (!CryptographicOperations.FixedTimeEquals(logicalDigest, header.LogicalPayloadDigest))
            throw new InvalidDataException("persistence.snapshot.logical-payload-digest-mismatch");
        return new CanonicalSnapshotChunkReadV1(header, payload);
    }
}

public static class CanonicalSnapshotPhysicalDrainV1
{
    public static async Task<IReadOnlyList<PhysicalSnapshotChunkDescriptor>> StageUncompressedAsync(
        SnapshotPhysicalPaths physical,
        IEnumerable<CanonicalSnapshotSectionMaterialV1> sections,
        WorldStateV1? frozenState = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(physical);
        if (!Directory.Exists(physical.StagingChunksDirectory))
            throw new InvalidDataException("persistence.snapshot-chunks-missing");

        var chunks = SnapshotChunkPackerV1.PackStandard(sections, frozenState);
        var descriptors = new List<PhysicalSnapshotChunkDescriptor>(chunks.Count);
        for (var i = 0; i < chunks.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunk = chunks[i];
            var relative = SnapshotChunkFile.RelativePath((uint)i);
            var path = Path.Combine(physical.StagingDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
            var header = await CanonicalSnapshotChunkFileV1.WriteUncompressedAsync(
                path,
                chunk,
                cancellationToken).ConfigureAwait(false);
            descriptors.Add(new PhysicalSnapshotChunkDescriptor(
                (uint)i,
                chunk.Fragments[0].SectionId,
                chunk.Fragments[^1].SectionId,
                header.UncompressedLength,
                header.StoredLength,
                header.Compression,
                header.LogicalPayloadDigest.ToArray(),
                header.StoredPayloadDigest.ToArray(),
                relative));
        }
        return Array.AsReadOnly(descriptors.ToArray());
    }
}

public static class CanonicalSnapshotStagingValidatorV1
{
    public static async Task ValidateAsync(
        SnapshotPhysicalPaths physical,
        IEnumerable<CanonicalSnapshotSectionMaterialV1> expectedSections,
        CanonicalSnapshotSemanticVerifierRegistryV1 semanticVerifiers,
        WorldStateV1? frozenState = null,
        IEnumerable<ISnapshotChunkCompressionDecoderV1>? compressionDecoders = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(physical);
        ArgumentNullException.ThrowIfNull(expectedSections);
        ArgumentNullException.ThrowIfNull(semanticVerifiers);
        if (!File.Exists(physical.StagingManifestPath))
            throw new InvalidDataException("persistence.snapshot-manifest-missing");
        if (!Directory.Exists(physical.StagingChunksDirectory))
            throw new InvalidDataException("persistence.snapshot-chunks-missing");

        var expected = CanonicalSnapshotSectionValidationV1.ValidateStandard(expectedSections, frozenState);
        var files = Directory.GetFiles(physical.StagingChunksDirectory)
            .OrderBy(static path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToArray();
        if (files.Length == 0)
            throw new InvalidDataException("persistence.snapshot.no-physical-chunks");

        var fragments = new List<SnapshotSectionFragmentMaterialV1>();
        for (var i = 0; i < files.Length; i++)
        {
            var expectedName = Path.GetFileName(SnapshotChunkFile.RelativePath((uint)i));
            if (!string.Equals(Path.GetFileName(files[i]), expectedName, StringComparison.Ordinal))
                throw new InvalidDataException("persistence.snapshot.chunk-index-gap");
            var decoded = await CanonicalSnapshotChunkFileV1.ReadValidatedAsync(
                files[i],
                compressionDecoders,
                cancellationToken).ConfigureAwait(false);
            fragments.AddRange(decoded.Payload.Fragments);
        }

        ValidateGlobalFragmentOrder(fragments);
        var groups = fragments
            .GroupBy(static fragment => fragment.SectionId, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<SnapshotSectionFragmentMaterialV1>)Array.AsReadOnly(group.ToArray()),
                StringComparer.Ordinal);

        var reassembled = new List<CanonicalSnapshotSectionMaterialV1>(expected.Count);
        foreach (var section in expected)
        {
            if (!groups.TryGetValue(section.SectionId, out var sectionFragments))
                throw new InvalidDataException($"persistence.snapshot-section-missing:{section.SectionId}");
            reassembled.Add(section with { Fragments = sectionFragments });
        }
        if (groups.Count != reassembled.Count)
            throw new InvalidDataException("persistence.snapshot.required-section-set-mismatch");

        var validated = CanonicalSnapshotSectionValidationV1.ValidateStandard(reassembled, frozenState);
        semanticVerifiers.VerifyAll(validated);
    }

    private static void ValidateGlobalFragmentOrder(IReadOnlyList<SnapshotSectionFragmentMaterialV1> fragments)
    {
        if (fragments.Count == 0)
            throw new InvalidDataException("persistence.snapshot-fragment-invalid:no-fragments");

        string? previousSection = null;
        uint previousIndex = 0;
        uint previousCount = 0;
        foreach (var fragment in fragments)
        {
            if (previousSection is null)
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
        if (checked(previousIndex + 1) != previousCount)
            throw new InvalidDataException("persistence.snapshot-fragment-invalid:global-fragment-incomplete");
    }
}

public static class RunningSnapshotCanonicalDrainExtensionsV1
{
    public static Task<DurableSnapshotCommitResult> CommitCanonicalDrainedAsync(
        this RunningSnapshotCoordinatorV1 coordinator,
        RunningSnapshotCutV1 cut,
        SqlitePersistenceStore store,
        WorldPersistencePaths world,
        SnapshotPhysicalPaths physical,
        ReadOnlyMemory<byte> snapshotDigest,
        ReadOnlyMemory<byte> physicalManifestDigest,
        IEnumerable<CanonicalSnapshotSectionMaterialV1> expectedSections,
        CanonicalSnapshotSemanticVerifierRegistryV1 semanticVerifiers,
        IEnumerable<ISnapshotChunkCompressionDecoderV1>? compressionDecoders = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(cut);
        var expected = CanonicalSnapshotSectionValidationV1.ValidateStandard(expectedSections, cut.FrozenState);
        return coordinator.CommitDrainedAsync(
            cut,
            store,
            world,
            physical,
            snapshotDigest,
            physicalManifestDigest,
            (candidate, token) => CanonicalSnapshotStagingValidatorV1.ValidateAsync(
                candidate,
                expected,
                semanticVerifiers,
                cut.FrozenState,
                compressionDecoders,
                token),
            cancellationToken);
    }
}
