using System.Text;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed record SnapshotChunkFragmentPayloadV1(
    IReadOnlyList<SnapshotSectionFragmentMaterialV1> Fragments);

public static class SnapshotChunkFragmentPayloadValidationV1
{
    public static IReadOnlyList<SnapshotSectionFragmentMaterialV1> ValidateCanonical(
        IEnumerable<SnapshotSectionFragmentMaterialV1> fragments)
    {
        ArgumentNullException.ThrowIfNull(fragments);
        var materialized = fragments.ToArray();
        if (materialized.Length == 0)
            throw new InvalidDataException("persistence.snapshot-fragment-invalid:chunk-empty");

        string? previousSection = null;
        uint? previousIndex = null;
        uint? previousCount = null;
        foreach (var fragment in materialized)
        {
            ArgumentNullException.ThrowIfNull(fragment);
            _ = new Determinism.StableToken(fragment.SectionId);
            if (fragment.FragmentCount == 0 || fragment.FragmentIndex >= fragment.FragmentCount)
                throw new InvalidDataException("persistence.snapshot-fragment-invalid:index-range");

            if (previousSection is not null)
            {
                var sectionOrder = string.CompareOrdinal(previousSection, fragment.SectionId);
                if (sectionOrder > 0)
                    throw new InvalidDataException("persistence.snapshot-fragment-invalid:section-order");
                if (sectionOrder == 0)
                {
                    if (previousCount != fragment.FragmentCount)
                        throw new InvalidDataException("persistence.snapshot-fragment-invalid:fragment-count");
                    if (previousIndex is null || fragment.FragmentIndex != checked(previousIndex.Value + 1))
                        throw new InvalidDataException("persistence.snapshot-fragment-invalid:fragment-order");
                }
            }

            previousSection = fragment.SectionId;
            previousIndex = fragment.FragmentIndex;
            previousCount = fragment.FragmentCount;
        }
        return Array.AsReadOnly(materialized);
    }
}

/// <summary>
/// Standard P4-04 v1.0 logical payload inside one MVCHNK01 physical chunk.
/// Field 1 is repeated SnapshotSectionFragmentV1, per the snapshot fragment parent-wire amendment.
/// </summary>
public static class SnapshotChunkPayloadWireCodecV1
{
    private const byte FragmentFieldTag = 0x0A; // field 1, length-delimited

    public static byte[] Encode(SnapshotChunkFragmentPayloadV1 payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var fragments = SnapshotChunkFragmentPayloadValidationV1.ValidateCanonical(payload.Fragments);
        using var stream = new MemoryStream();
        foreach (var fragment in fragments)
        {
            var nested = SnapshotSectionFragmentWireCodecV1.Encode(fragment);
            stream.WriteByte(FragmentFieldTag);
            WriteVarUInt64(stream, checked((ulong)nested.Length));
            stream.Write(nested);
        }
        return stream.ToArray();
    }

    public static SnapshotChunkFragmentPayloadV1 Decode(ReadOnlySpan<byte> encoded)
    {
        if (encoded.IsEmpty)
            throw new InvalidDataException("persistence.snapshot-fragment-invalid:chunk-empty");
        var remaining = encoded;
        var fragments = new List<SnapshotSectionFragmentMaterialV1>();
        while (!remaining.IsEmpty)
        {
            var tag = ReadVarUInt64(ref remaining);
            var field = tag >> 3;
            var wire = tag & 7;
            if (field != 1 || wire != 2)
                throw new InvalidDataException("persistence.snapshot-fragment-invalid:chunk-field");
            var nested = ReadBytes(ref remaining);
            fragments.Add(SnapshotSectionFragmentWireCodecV1.Decode(nested));
        }
        return new SnapshotChunkFragmentPayloadV1(
            SnapshotChunkFragmentPayloadValidationV1.ValidateCanonical(fragments));
    }

    internal static int EncodedFragmentFieldLength(SnapshotSectionFragmentMaterialV1 fragment)
    {
        var nestedLength = SnapshotSectionFragmentWireCodecV1.Encode(fragment).Length;
        return checked(1 + VarUInt64Length((ulong)nestedLength) + nestedLength);
    }

    private static int VarUInt64Length(ulong value)
    {
        var length = 1;
        while (value >= 0x80)
        {
            value >>= 7;
            length++;
        }
        return length;
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

    private static ulong ReadVarUInt64(ref ReadOnlySpan<byte> remaining)
    {
        ulong value = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            if (remaining.IsEmpty)
                throw new InvalidDataException("persistence.snapshot-fragment-invalid:chunk-varint-truncated");
            var current = remaining[0];
            remaining = remaining[1..];
            value |= (ulong)(current & 0x7f) << shift;
            if ((current & 0x80) == 0) return value;
        }
        throw new InvalidDataException("persistence.snapshot-fragment-invalid:chunk-varint-overflow");
    }

    private static byte[] ReadBytes(ref ReadOnlySpan<byte> remaining)
    {
        var length = ReadVarUInt64(ref remaining);
        if (length > int.MaxValue || (ulong)remaining.Length < length)
            throw new InvalidDataException("persistence.snapshot-fragment-invalid:chunk-bytes-truncated");
        var result = remaining[..(int)length].ToArray();
        remaining = remaining[(int)length..];
        return result;
    }
}

public static class SnapshotChunkPackerV1
{
    public static IReadOnlyList<SnapshotChunkFragmentPayloadV1> PackStandard(
        IEnumerable<CanonicalSnapshotSectionMaterialV1> sections,
        WorldState.WorldStateV1? frozenState = null)
    {
        var canonicalSections = CanonicalSnapshotSectionValidationV1.ValidateStandard(sections, frozenState);
        var fragments = canonicalSections.SelectMany(static section => section.Fragments).ToArray();
        if (fragments.Length == 0)
            throw new InvalidDataException("persistence.snapshot-fragment-invalid:no-fragments");

        var chunks = new List<SnapshotChunkFragmentPayloadV1>();
        var current = new List<SnapshotSectionFragmentMaterialV1>();
        var currentLength = 0;
        foreach (var fragment in fragments)
        {
            var fieldLength = SnapshotChunkPayloadWireCodecV1.EncodedFragmentFieldLength(fragment);
            if (fieldLength > CanonicalSnapshotSectionValidationV1.HardMaxUncompressedBytes)
                throw new InvalidDataException("persistence.snapshot-item-too-large");

            if (current.Count > 0 &&
                checked(currentLength + fieldLength) > CanonicalSnapshotSectionValidationV1.TargetUncompressedBytes)
            {
                chunks.Add(new SnapshotChunkFragmentPayloadV1(Array.AsReadOnly(current.ToArray())));
                current = [];
                currentLength = 0;
            }

            current.Add(fragment);
            currentLength = checked(currentLength + fieldLength);
            if (currentLength > CanonicalSnapshotSectionValidationV1.HardMaxUncompressedBytes)
                throw new InvalidDataException("persistence.snapshot-item-too-large");
        }

        if (current.Count > 0)
            chunks.Add(new SnapshotChunkFragmentPayloadV1(Array.AsReadOnly(current.ToArray())));

        foreach (var chunk in chunks)
        {
            var encoded = SnapshotChunkPayloadWireCodecV1.Encode(chunk);
            if (encoded.Length > CanonicalSnapshotSectionValidationV1.HardMaxUncompressedBytes)
                throw new InvalidDataException("persistence.snapshot-item-too-large");
        }
        return Array.AsReadOnly(chunks.ToArray());
    }
}
