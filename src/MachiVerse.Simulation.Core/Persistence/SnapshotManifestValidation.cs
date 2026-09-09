using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed record LogicalSnapshotSection(
    string SectionId,
    string SchemaId,
    ushort SchemaMajor,
    ushort SchemaMinor,
    ulong LogicalItemCount,
    byte[] LogicalContentDigest,
    bool Required);

public sealed record RequiredAddonSnapshotMetadataV1(
    string AddonId,
    string MetadataSchemaId,
    ushort MetadataSchemaMajor,
    ushort MetadataSchemaMinor,
    byte[] MetadataPayload,
    byte[] MetadataSemanticDigest);

public sealed record LogicalSnapshotManifest(
    ushort PersistenceSchemaMajor,
    ushort PersistenceSchemaMinor,
    OpaqueId128 WorldId,
    OpaqueId128 SnapshotId,
    ulong SnapshotStep,
    ulong HistoryAnchorSequence,
    byte[] HistoryAnchorDigest,
    byte[] StateContinuityToken,
    byte[] WorldSeed,
    ulong SimulationConfigGeneration,
    byte[] SimulationConfigDigest,
    ulong MasterGeneration,
    IReadOnlyList<string> RequiredDomains,
    IReadOnlyList<LogicalSnapshotSection> Sections,
    byte[] SnapshotDigest)
{
    public IReadOnlyList<RequiredAddonSnapshotMetadataV1> RequiredAddons { get; init; }
        = Array.Empty<RequiredAddonSnapshotMetadataV1>();
}

public sealed record PhysicalSnapshotChunkDescriptor(
    uint ChunkIndex,
    string FirstSectionId,
    string LastSectionId,
    ulong UncompressedLength,
    ulong StoredLength,
    SnapshotCompression Compression,
    byte[] LogicalPayloadDigest,
    byte[] StoredPayloadDigest,
    string RelativePath);

public static class SnapshotManifestValidation
{
    public const int StandardRequiredSectionCount = 103;

    public static void ValidateLogical(
        LogicalSnapshotManifest manifest,
        IEnumerable<string> expectedRequiredSectionIds)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(expectedRequiredSectionIds);

        if (manifest.PersistenceSchemaMajor == 0)
            throw new InvalidDataException("persistence.snapshot.invalid-schema-version");
        if (manifest.WorldId.IsZero || manifest.SnapshotId.IsZero)
            throw new InvalidDataException("persistence.snapshot.invalid-id");
        RequireHash(manifest.HistoryAnchorDigest, "history-anchor-digest");
        RequireHash(manifest.StateContinuityToken, "state-continuity-token");
        if (manifest.WorldSeed is null || manifest.WorldSeed.Length != 32)
            throw new InvalidDataException("persistence.snapshot.invalid-world-seed");
        if (manifest.SimulationConfigGeneration == 0)
            throw new InvalidDataException("persistence.snapshot.invalid-config-generation");
        RequireHash(manifest.SimulationConfigDigest, "config-digest");
        RequireHash(manifest.SnapshotDigest, "snapshot-digest");

        ValidateSortedStableTokens(manifest.RequiredDomains, "required-domain");
        ValidateRequiredAddons(manifest.RequiredAddons);

        var expected = expectedRequiredSectionIds
            .Select(static id => new StableToken(id).Value)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();
        if (expected.Length != StandardRequiredSectionCount || expected.Distinct(StringComparer.Ordinal).Count() != expected.Length)
            throw new InvalidDataException("persistence.snapshot.invalid-required-section-registry");

        if (manifest.Sections.Count != StandardRequiredSectionCount)
            throw new InvalidDataException("persistence.snapshot.section-count-mismatch");

        string? previous = null;
        for (var i = 0; i < manifest.Sections.Count; i++)
        {
            var section = manifest.Sections[i] ?? throw new InvalidDataException("persistence.snapshot.section-null");
            var sectionId = new StableToken(section.SectionId).Value;
            _ = new StableToken(section.SchemaId);
            if (section.SchemaMajor == 0)
                throw new InvalidDataException($"persistence.snapshot.section-schema-version-invalid:{sectionId}");
            RequireHash(section.LogicalContentDigest, "section-logical-content-digest");
            if (!section.Required)
                throw new InvalidDataException("persistence.snapshot.required-section-marked-optional");
            if (previous is not null && string.CompareOrdinal(previous, sectionId) >= 0)
                throw new InvalidDataException("persistence.snapshot.sections-not-ascii-ascending");
            if (!string.Equals(sectionId, expected[i], StringComparison.Ordinal))
                throw new InvalidDataException("persistence.snapshot.required-section-set-mismatch");
            previous = sectionId;
        }
    }

    public static void ValidatePhysicalMapping(
        LogicalSnapshotManifest logical,
        IReadOnlyList<PhysicalSnapshotChunkDescriptor> chunks)
    {
        ArgumentNullException.ThrowIfNull(logical);
        ArgumentNullException.ThrowIfNull(chunks);
        if (chunks.Count == 0)
            throw new InvalidDataException("persistence.snapshot.no-physical-chunks");

        var sectionIds = logical.Sections.Select(static section => section.SectionId).ToArray();
        var nextSectionIndex = 0;

        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            if (chunk.ChunkIndex != (uint)i)
                throw new InvalidDataException("persistence.snapshot.chunk-index-gap");
            var first = new StableToken(chunk.FirstSectionId).Value;
            var last = new StableToken(chunk.LastSectionId).Value;
            if (string.CompareOrdinal(first, last) > 0)
                throw new InvalidDataException("persistence.snapshot.invalid-section-range");
            if (chunk.StoredLength == 0 || chunk.UncompressedLength == 0)
                throw new InvalidDataException("persistence.snapshot.invalid-chunk-length");
            if (chunk.Compression == SnapshotCompression.None && chunk.StoredLength != chunk.UncompressedLength)
                throw new InvalidDataException("persistence.snapshot.none-length-mismatch");
            if (chunk.Compression is not (SnapshotCompression.None or SnapshotCompression.Zstd))
                throw new InvalidDataException("persistence.snapshot.unsupported-compression");
            RequireHash(chunk.LogicalPayloadDigest, "chunk-logical-payload-digest");
            RequireHash(chunk.StoredPayloadDigest, "chunk-stored-payload-digest");
            SnapshotChunkFile.ValidateRelativePath(chunk.RelativePath, chunk.ChunkIndex);

            if (nextSectionIndex >= sectionIds.Length || !string.Equals(sectionIds[nextSectionIndex], first, StringComparison.Ordinal))
                throw new InvalidDataException("persistence.snapshot.chunk-section-coverage-gap");

            var foundLast = false;
            while (nextSectionIndex < sectionIds.Length)
            {
                var current = sectionIds[nextSectionIndex++];
                if (string.Equals(current, last, StringComparison.Ordinal))
                {
                    foundLast = true;
                    break;
                }
                if (string.CompareOrdinal(current, last) > 0)
                    break;
            }
            if (!foundLast)
                throw new InvalidDataException("persistence.snapshot.chunk-section-range-mismatch");
        }

        if (nextSectionIndex != sectionIds.Length)
            throw new InvalidDataException("persistence.snapshot.chunk-section-coverage-incomplete");
    }

    private static void ValidateRequiredAddons(IReadOnlyList<RequiredAddonSnapshotMetadataV1> addons)
    {
        ArgumentNullException.ThrowIfNull(addons);
        string? previous = null;
        foreach (var addon in addons)
        {
            ArgumentNullException.ThrowIfNull(addon);
            var addonId = new StableToken(addon.AddonId).Value;
            _ = new StableToken(addon.MetadataSchemaId);
            if (addon.MetadataSchemaMajor == 0)
                throw new InvalidDataException($"persistence.snapshot.addon-schema-version-invalid:{addonId}");
            ArgumentNullException.ThrowIfNull(addon.MetadataPayload);
            RequireHash(addon.MetadataSemanticDigest, "addon-metadata-semantic-digest");
            if (previous is not null && string.CompareOrdinal(previous, addonId) >= 0)
                throw new InvalidDataException("persistence.snapshot.required-addons-not-ascii-ascending");
            previous = addonId;
        }
    }

    private static void ValidateSortedStableTokens(IReadOnlyList<string> values, string field)
    {
        ArgumentNullException.ThrowIfNull(values);
        string? previous = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in values)
        {
            var value = new StableToken(raw).Value;
            if (!seen.Add(value)) throw new InvalidDataException($"persistence.snapshot.duplicate-{field}");
            if (previous is not null && string.CompareOrdinal(previous, value) >= 0)
                throw new InvalidDataException($"persistence.snapshot.unsorted-{field}");
            previous = value;
        }
    }

    private static void RequireHash(byte[] value, string field)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length != 32)
            throw new InvalidDataException($"persistence.snapshot.invalid-hash:{field}");
    }
}
