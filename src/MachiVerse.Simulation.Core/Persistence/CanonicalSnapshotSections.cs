using System.Buffers.Binary;
using System.Text;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed record SnapshotSectionFragmentMaterialV1(
    string SectionId,
    uint FragmentIndex,
    uint FragmentCount,
    byte[]? FirstRecordId,
    byte[]? LastRecordId,
    ulong ItemCount,
    byte[] FragmentPayload);

public sealed record CanonicalSnapshotSectionMaterialV1(
    string SectionId,
    SchemaRefV1 SectionSchema,
    ulong LogicalItemCount,
    byte[] LogicalContentDigest,
    IReadOnlyList<SnapshotSectionFragmentMaterialV1> Fragments);

public static class StandardSnapshotSectionSetV1
{
    private static readonly string[] CoreSectionIds =
    [
        "core.world-state-header",
        "core.scheduler-state",
        "core.operation-state",
        "core.detail-directory",
        "core.domain-registry",
        "core.config-state",
    ];

    private static readonly string[] CanonicalIds = CoreSectionIds
        .Concat(StandardDomainPartitionRegistry.Entries.Select(static entry => entry.PartitionId.Value))
        .OrderBy(static value => value, StringComparer.Ordinal)
        .ToArray();

    public static IReadOnlyList<string> SectionIds { get; } = Array.AsReadOnly(CanonicalIds);

    static StandardSnapshotSectionSetV1()
    {
        if (CanonicalIds.Length != SnapshotManifestValidation.StandardRequiredSectionCount)
            throw new InvalidOperationException($"Standard snapshot section count must be {SnapshotManifestValidation.StandardRequiredSectionCount}.");
        if (CanonicalIds.Distinct(StringComparer.Ordinal).Count() != CanonicalIds.Length)
            throw new InvalidOperationException("Standard snapshot section registry contains a duplicate id.");
        for (var i = 1; i < CanonicalIds.Length; i++)
        {
            if (string.CompareOrdinal(CanonicalIds[i - 1], CanonicalIds[i]) >= 0)
                throw new InvalidOperationException("Standard snapshot section registry is not ASCII ascending.");
        }
    }

    public static bool IsCoreSection(string sectionId)
        => CoreSectionIds.Contains(sectionId, StringComparer.Ordinal);
}

public static class CanonicalSnapshotSectionValidationV1
{
    public const int RecordIdLength = 16;
    public const int TargetUncompressedBytes = 32 * 1024 * 1024;
    public const int HardMaxUncompressedBytes = 64 * 1024 * 1024;

    public static IReadOnlyList<CanonicalSnapshotSectionMaterialV1> ValidateStandard(
        IEnumerable<CanonicalSnapshotSectionMaterialV1> sections,
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
            var section = ordered[i];
            var sectionId = new StableToken(section.SectionId).Value;
            if (!string.Equals(sectionId, StandardSnapshotSectionSetV1.SectionIds[i], StringComparison.Ordinal))
                throw new InvalidDataException("persistence.snapshot.required-section-set-mismatch");
            ValidateSection(section);

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
        IEnumerable<CanonicalSnapshotSectionMaterialV1> sections,
        WorldStateV1? frozenState = null)
        => Array.AsReadOnly(ValidateStandard(sections, frozenState)
            .Select(static section => new LogicalSnapshotSection(
                section.SectionId,
                section.SectionSchema.SchemaId.Value,
                section.SectionSchema.Version.Major,
                section.SectionSchema.Version.Minor,
                section.LogicalItemCount,
                section.LogicalContentDigest.ToArray(),
                Required: true))
            .ToArray());

    private static void ValidateSection(CanonicalSnapshotSectionMaterialV1 section)
    {
        ArgumentNullException.ThrowIfNull(section);
        _ = new StableToken(section.SectionId);
        _ = section.SectionSchema.SchemaId.Value;
        if (section.SectionSchema.Version.Major == 0)
            throw new InvalidDataException($"persistence.snapshot.section-schema-version-invalid:{section.SectionId}");
        if (section.LogicalContentDigest is null || section.LogicalContentDigest.Length != 32)
            throw new InvalidDataException($"persistence.snapshot.section-digest-invalid:{section.SectionId}");
        ArgumentNullException.ThrowIfNull(section.Fragments);
        if (section.Fragments.Count == 0)
            throw new InvalidDataException($"persistence.snapshot.section-fragment-missing:{section.SectionId}");
        if (section.Fragments.Count > uint.MaxValue)
            throw new InvalidDataException($"persistence.snapshot.section-fragment-count-overflow:{section.SectionId}");

        ulong itemTotal = 0;
        byte[]? previousLast = null;
        for (var i = 0; i < section.Fragments.Count; i++)
        {
            var fragment = section.Fragments[i];
            if (!string.Equals(fragment.SectionId, section.SectionId, StringComparison.Ordinal))
                throw new InvalidDataException($"persistence.snapshot.fragment-section-mismatch:{section.SectionId}");
            if (fragment.FragmentIndex != (uint)i || fragment.FragmentCount != (uint)section.Fragments.Count)
                throw new InvalidDataException($"persistence.snapshot-fragment-invalid:{section.SectionId}");
            if (fragment.FragmentPayload is null)
                throw new InvalidDataException($"persistence.snapshot.fragment-payload-null:{section.SectionId}");
            if (fragment.FragmentPayload.Length > HardMaxUncompressedBytes)
                throw new InvalidDataException("persistence.snapshot-item-too-large");

            var hasFirst = fragment.FirstRecordId is not null;
            var hasLast = fragment.LastRecordId is not null;
            if (hasFirst != hasLast)
                throw new InvalidDataException($"persistence.snapshot.fragment-record-range-incomplete:{section.SectionId}");
            if (hasFirst)
            {
                if (fragment.FirstRecordId!.Length != RecordIdLength || fragment.LastRecordId!.Length != RecordIdLength)
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
            }
            catch (OverflowException ex)
            {
                throw new InvalidDataException($"persistence.snapshot.fragment-item-count-overflow:{section.SectionId}", ex);
            }
        }

        if (itemTotal != section.LogicalItemCount)
            throw new InvalidDataException($"persistence.snapshot.fragment-item-count-mismatch:{section.SectionId}");
    }
}

/// <summary>
/// Exact protobuf proto3 codec for P4-04 SnapshotSectionFragmentV1 only.
/// The parent chunk wrapper for fragmented sections is deliberately not invented here because
/// Phase 4 currently defines SnapshotChunkPayloadV1.sections and SnapshotSectionFragmentV1 but
/// does not define their parent-message relationship.
/// </summary>
public static class SnapshotSectionFragmentWireCodecV1
{
    public static byte[] Encode(SnapshotSectionFragmentMaterialV1 fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        _ = new StableToken(fragment.SectionId);
        using var stream = new MemoryStream();
        WriteString(stream, 1, fragment.SectionId);
        WriteUInt32(stream, 2, fragment.FragmentIndex);
        WriteUInt32(stream, 3, fragment.FragmentCount);
        if (fragment.FirstRecordId is not null) WriteBytes(stream, 4, fragment.FirstRecordId);
        if (fragment.LastRecordId is not null) WriteBytes(stream, 5, fragment.LastRecordId);
        WriteUInt64(stream, 6, fragment.ItemCount);
        WriteBytes(stream, 7, fragment.FragmentPayload ?? throw new InvalidDataException("persistence.snapshot.fragment-payload-null"));
        return stream.ToArray();
    }

    public static SnapshotSectionFragmentMaterialV1 Decode(ReadOnlySpan<byte> encoded)
    {
        if (encoded.IsEmpty) throw new InvalidDataException("persistence.snapshot-fragment-invalid:empty");
        var reader = new ProtoReader(encoded);
        string? sectionId = null;
        uint fragmentIndex = 0;
        uint fragmentCount = 0;
        byte[]? first = null;
        byte[]? last = null;
        ulong itemCount = 0;
        byte[]? payload = null;
        var seen = new HashSet<int>();

        while (!reader.End)
        {
            var tag = reader.ReadVarUInt64();
            if (tag == 0) throw new InvalidDataException("persistence.snapshot-fragment-invalid:tag-zero");
            var field = checked((int)(tag >> 3));
            var wire = checked((int)(tag & 7));
            if (!seen.Add(field)) throw new InvalidDataException("persistence.snapshot-fragment-invalid:duplicate-field");
            switch (field)
            {
                case 1: RequireWire(wire, 2); sectionId = reader.ReadString(); break;
                case 2: RequireWire(wire, 0); fragmentIndex = reader.ReadUInt32(); break;
                case 3: RequireWire(wire, 0); fragmentCount = reader.ReadUInt32(); break;
                case 4: RequireWire(wire, 2); first = reader.ReadBytes(); break;
                case 5: RequireWire(wire, 2); last = reader.ReadBytes(); break;
                case 6: RequireWire(wire, 0); itemCount = reader.ReadVarUInt64(); break;
                case 7: RequireWire(wire, 2); payload = reader.ReadBytes(); break;
                default: reader.SkipField(wire); break;
            }
        }

        if (sectionId is null || fragmentCount == 0 || payload is null)
            throw new InvalidDataException("persistence.snapshot-fragment-invalid:required-field");
        _ = new StableToken(sectionId);
        return new SnapshotSectionFragmentMaterialV1(sectionId, fragmentIndex, fragmentCount, first, last, itemCount, payload);
    }

    private static void WriteString(Stream stream, int field, string value)
        => WriteBytes(stream, field, Encoding.UTF8.GetBytes(value));

    private static void WriteBytes(Stream stream, int field, ReadOnlySpan<byte> value)
    {
        WriteVarUInt64(stream, ((ulong)field << 3) | 2UL);
        WriteVarUInt64(stream, checked((ulong)value.Length));
        stream.Write(value);
    }

    private static void WriteUInt32(Stream stream, int field, uint value)
    {
        WriteVarUInt64(stream, (ulong)field << 3);
        WriteVarUInt64(stream, value);
    }

    private static void WriteUInt64(Stream stream, int field, ulong value)
    {
        WriteVarUInt64(stream, (ulong)field << 3);
        WriteVarUInt64(stream, value);
    }

    private static void WriteVarUInt64(Stream stream, ulong value)
    {
        while (value >= 0x80)
        {
            stream.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }
        stream.WriteByte((byte)value);
    }

    private static void RequireWire(int actual, int expected)
    {
        if (actual != expected) throw new InvalidDataException("persistence.snapshot-fragment-invalid:wire-type");
    }

    private ref struct ProtoReader
    {
        private ReadOnlySpan<byte> _remaining;

        public ProtoReader(ReadOnlySpan<byte> encoded) => _remaining = encoded;
        public bool End => _remaining.IsEmpty;

        public ulong ReadVarUInt64()
        {
            ulong value = 0;
            for (var shift = 0; shift < 64; shift += 7)
            {
                if (_remaining.IsEmpty) throw new InvalidDataException("persistence.snapshot-fragment-invalid:truncated-varint");
                var current = _remaining[0];
                _remaining = _remaining[1..];
                value |= (ulong)(current & 0x7f) << shift;
                if ((current & 0x80) == 0) return value;
            }
            throw new InvalidDataException("persistence.snapshot-fragment-invalid:varint-overflow");
        }

        public uint ReadUInt32()
        {
            var value = ReadVarUInt64();
            if (value > uint.MaxValue) throw new InvalidDataException("persistence.snapshot-fragment-invalid:uint32-overflow");
            return (uint)value;
        }

        public byte[] ReadBytes()
        {
            var length = ReadVarUInt64();
            if (length > int.MaxValue || (ulong)_remaining.Length < length)
                throw new InvalidDataException("persistence.snapshot-fragment-invalid:truncated-bytes");
            var result = _remaining[..(int)length].ToArray();
            _remaining = _remaining[(int)length..];
            return result;
        }

        public string ReadString()
        {
            var bytes = ReadBytes();
            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException ex)
            {
                throw new InvalidDataException("persistence.snapshot-fragment-invalid:utf8", ex);
            }
        }

        public void SkipField(int wire)
        {
            switch (wire)
            {
                case 0: _ = ReadVarUInt64(); break;
                case 1: Skip(8); break;
                case 2:
                    var length = ReadVarUInt64();
                    if (length > int.MaxValue) throw new InvalidDataException("persistence.snapshot-fragment-invalid:length-overflow");
                    Skip((int)length);
                    break;
                case 5: Skip(4); break;
                default: throw new InvalidDataException("persistence.snapshot-fragment-invalid:unsupported-wire-type");
            }
        }

        private void Skip(int count)
        {
            if (_remaining.Length < count) throw new InvalidDataException("persistence.snapshot-fragment-invalid:truncated-field");
            _remaining = _remaining[count..];
        }
    }
}
