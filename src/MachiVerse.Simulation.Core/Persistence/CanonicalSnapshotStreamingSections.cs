using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

/// <summary>
/// Metadata plus a repeatable fragment factory for one canonical Snapshot section.
/// Unlike <see cref="CanonicalSnapshotSectionMaterialV1"/>, this boundary does not require all
/// fragment payloads to exist simultaneously.
/// </summary>
public sealed class CanonicalSnapshotStreamingSectionV1
{
    private readonly Func<IEnumerable<SnapshotSectionFragmentMaterialV1>> _fragmentFactory;

    public CanonicalSnapshotStreamingSectionV1(
        string sectionId,
        SchemaRefV1 sectionSchema,
        ulong logicalItemCount,
        byte[] logicalContentDigest,
        Func<IEnumerable<SnapshotSectionFragmentMaterialV1>> fragmentFactory)
    {
        SectionId = new StableToken(sectionId).Value;
        SectionSchema = sectionSchema;
        LogicalItemCount = logicalItemCount;
        LogicalContentDigest = logicalContentDigest?.ToArray()
            ?? throw new ArgumentNullException(nameof(logicalContentDigest));
        _fragmentFactory = fragmentFactory ?? throw new ArgumentNullException(nameof(fragmentFactory));
    }

    public string SectionId { get; }
    public SchemaRefV1 SectionSchema { get; }
    public ulong LogicalItemCount { get; }
    public byte[] LogicalContentDigest { get; }

    public IEnumerable<SnapshotSectionFragmentMaterialV1> EnumerateFragments()
        => _fragmentFactory()
            ?? throw new InvalidDataException($"persistence.snapshot.section-fragment-source-null:{SectionId}");

    public static CanonicalSnapshotStreamingSectionV1 FromMaterialized(
        CanonicalSnapshotSectionMaterialV1 section)
    {
        ArgumentNullException.ThrowIfNull(section);
        return new CanonicalSnapshotStreamingSectionV1(
            section.SectionId,
            section.SectionSchema,
            section.LogicalItemCount,
            section.LogicalContentDigest,
            () => section.Fragments);
    }
}

/// <summary>
/// Standard exact-103 validation for streaming section metadata and one-at-a-time fragment streams.
/// The rules intentionally mirror <see cref="CanonicalSnapshotSectionValidationV1"/> without
/// materializing every fragment before validation.
/// </summary>
public static class CanonicalSnapshotStreamingSectionValidationV1
{
    public static IReadOnlyList<CanonicalSnapshotStreamingSectionV1> ValidateStandardMetadata(
        IEnumerable<CanonicalSnapshotStreamingSectionV1> sections,
        WorldStateV1? frozenState = null)
    {
        ArgumentNullException.ThrowIfNull(sections);
        var ordered = sections.OrderBy(static section => section.SectionId, StringComparer.Ordinal).ToArray();
        if (ordered.Length != SnapshotManifestValidation.StandardRequiredSectionCount)
            throw new InvalidDataException("persistence.snapshot.section-count-mismatch");
        if (ordered.Select(static section => section.SectionId).Distinct(StringComparer.Ordinal).Count() != ordered.Length)
            throw new InvalidDataException("persistence.snapshot.section-duplicate");

        for (var i = 0; i < ordered.Length; i++)
        {
            var section = ordered[i] ?? throw new InvalidDataException("persistence.snapshot.section-null");
            var sectionId = new StableToken(section.SectionId).Value;
            if (!string.Equals(sectionId, StandardSnapshotSectionSetV1.SectionIds[i], StringComparison.Ordinal))
                throw new InvalidDataException("persistence.snapshot.required-section-set-mismatch");
            ValidateMetadata(section);

            if (StandardDomainPartitionRegistry.TryGet(sectionId, out var identity) && identity is not null)
            {
                if (section.SectionSchema != identity.PartitionSchema)
                    throw new InvalidDataException($"persistence.snapshot.partition-schema-mismatch:{sectionId}");
                if (frozenState is not null)
                {
                    var header = frozenState.Partitions.Get(sectionId).Header;
                    if (section.LogicalItemCount != header.ItemCount)
                        throw new InvalidDataException($"persistence.snapshot.partition-item-count-mismatch:{sectionId}");
                    if (!section.LogicalContentDigest.AsSpan().SequenceEqual(header.CanonicalDigest))
                        throw new InvalidDataException($"persistence.snapshot.partition-digest-mismatch:{sectionId}");
                }
            }
        }

        return Array.AsReadOnly(ordered);
    }

    public static IReadOnlyList<LogicalSnapshotSection> ToLogicalSections(
        IEnumerable<CanonicalSnapshotStreamingSectionV1> sections,
        WorldStateV1? frozenState = null)
        => Array.AsReadOnly(ValidateStandardMetadata(sections, frozenState)
            .Select(static section => new LogicalSnapshotSection(
                section.SectionId,
                section.SectionSchema.SchemaId.Value,
                section.SectionSchema.Version.Major,
                section.SectionSchema.Version.Minor,
                section.LogicalItemCount,
                section.LogicalContentDigest.ToArray(),
                Required: true))
            .ToArray());

    public static IEnumerable<SnapshotSectionFragmentMaterialV1> EnumerateValidatedFragments(
        CanonicalSnapshotStreamingSectionV1 section)
    {
        ArgumentNullException.ThrowIfNull(section);
        ValidateMetadata(section);

        ulong itemTotal = 0;
        ulong seen = 0;
        uint? declaredFragmentCount = null;
        byte[]? previousLast = null;

        foreach (var fragment in section.EnumerateFragments())
        {
            ArgumentNullException.ThrowIfNull(fragment);
            if (!string.Equals(fragment.SectionId, section.SectionId, StringComparison.Ordinal))
                throw new InvalidDataException($"persistence.snapshot.fragment-section-mismatch:{section.SectionId}");
            if (fragment.FragmentCount == 0 || fragment.FragmentIndex >= fragment.FragmentCount)
                throw new InvalidDataException($"persistence.snapshot-fragment-invalid:{section.SectionId}");

            declaredFragmentCount ??= fragment.FragmentCount;
            if (fragment.FragmentCount != declaredFragmentCount.Value ||
                seen > uint.MaxValue || fragment.FragmentIndex != checked((uint)seen))
                throw new InvalidDataException($"persistence.snapshot-fragment-invalid:{section.SectionId}");
            if (fragment.FragmentPayload is null)
                throw new InvalidDataException($"persistence.snapshot.fragment-payload-null:{section.SectionId}");
            if (fragment.FragmentPayload.Length > CanonicalSnapshotSectionValidationV1.HardMaxUncompressedBytes)
                throw new InvalidDataException("persistence.snapshot-item-too-large");

            var hasFirst = fragment.FirstRecordId is not null;
            var hasLast = fragment.LastRecordId is not null;
            if (hasFirst != hasLast)
                throw new InvalidDataException($"persistence.snapshot.fragment-record-range-incomplete:{section.SectionId}");
            if (hasFirst)
            {
                if (fragment.FirstRecordId!.Length != CanonicalSnapshotSectionValidationV1.RecordIdLength ||
                    fragment.LastRecordId!.Length != CanonicalSnapshotSectionValidationV1.RecordIdLength)
                    throw new InvalidDataException($"persistence.snapshot.fragment-record-id-length:{section.SectionId}");
                if (fragment.FirstRecordId.AsSpan().SequenceCompareTo(fragment.LastRecordId) > 0)
                    throw new InvalidDataException($"persistence.snapshot.fragment-record-range-reversed:{section.SectionId}");
                if (previousLast is not null && previousLast.AsSpan().SequenceCompareTo(fragment.FirstRecordId) >= 0)
                    throw new InvalidDataException($"persistence.snapshot.fragment-record-range-overlap:{section.SectionId}");
                previousLast = fragment.LastRecordId.ToArray();
            }

            try
            {
                itemTotal = checked(itemTotal + fragment.ItemCount);
                seen = checked(seen + 1);
            }
            catch (OverflowException ex)
            {
                throw new InvalidDataException($"persistence.snapshot.fragment-item-count-overflow:{section.SectionId}", ex);
            }

            yield return fragment;
        }

        if (seen == 0)
            throw new InvalidDataException($"persistence.snapshot.section-fragment-missing:{section.SectionId}");
        if (declaredFragmentCount is null || seen != declaredFragmentCount.Value)
            throw new InvalidDataException($"persistence.snapshot-fragment-invalid:{section.SectionId}");
        if (itemTotal != section.LogicalItemCount)
            throw new InvalidDataException($"persistence.snapshot.fragment-item-count-mismatch:{section.SectionId}");
    }

    private static void ValidateMetadata(CanonicalSnapshotStreamingSectionV1 section)
    {
        _ = new StableToken(section.SectionId);
        _ = section.SectionSchema.SchemaId.Value;
        if (section.SectionSchema.Version.Major == 0)
            throw new InvalidDataException($"persistence.snapshot.section-schema-version-invalid:{section.SectionId}");
        if (section.LogicalContentDigest is null || section.LogicalContentDigest.Length != 32)
            throw new InvalidDataException($"persistence.snapshot.section-digest-invalid:{section.SectionId}");
    }
}

/// <summary>
/// Iterator equivalent of <see cref="SnapshotChunkPackerV1"/>. It retains only the current chunk's
/// fragments and therefore can drain a large streaming section without first collecting all section
/// fragments or all chunks.
/// </summary>
public static class SnapshotChunkStreamingPackerV1
{
    public static IEnumerable<SnapshotChunkFragmentPayloadV1> PackStandard(
        IEnumerable<CanonicalSnapshotStreamingSectionV1> sections,
        WorldStateV1? frozenState = null)
    {
        var canonicalSections = CanonicalSnapshotStreamingSectionValidationV1.ValidateStandardMetadata(
            sections,
            frozenState);

        var current = new List<SnapshotSectionFragmentMaterialV1>();
        var currentLength = 0;
        var anyFragment = false;

        foreach (var section in canonicalSections)
        {
            foreach (var fragment in CanonicalSnapshotStreamingSectionValidationV1.EnumerateValidatedFragments(section))
            {
                anyFragment = true;
                var fieldLength = SnapshotChunkPayloadWireCodecV1.EncodedFragmentFieldLength(fragment);
                if (fieldLength > CanonicalSnapshotSectionValidationV1.HardMaxUncompressedBytes)
                    throw new InvalidDataException("persistence.snapshot-item-too-large");

                if (current.Count > 0 &&
                    checked(currentLength + fieldLength) > CanonicalSnapshotSectionValidationV1.TargetUncompressedBytes)
                {
                    yield return CreateChunk(current);
                    current = [];
                    currentLength = 0;
                }

                current.Add(fragment);
                currentLength = checked(currentLength + fieldLength);
                if (currentLength > CanonicalSnapshotSectionValidationV1.HardMaxUncompressedBytes)
                    throw new InvalidDataException("persistence.snapshot-item-too-large");
            }
        }

        if (!anyFragment)
            throw new InvalidDataException("persistence.snapshot-fragment-invalid:no-fragments");
        if (current.Count > 0)
            yield return CreateChunk(current);
    }

    private static SnapshotChunkFragmentPayloadV1 CreateChunk(
        IReadOnlyList<SnapshotSectionFragmentMaterialV1> fragments)
    {
        var validated = SnapshotChunkFragmentPayloadValidationV1.ValidateCanonical(fragments);
        return new SnapshotChunkFragmentPayloadV1(validated);
    }
}
