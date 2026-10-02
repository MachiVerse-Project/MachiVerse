using System.Globalization;
using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.WorldState;

/// <summary>
/// Explicit algorithm identity for PartitionStateHeaderV1.CanonicalDigest.
///
/// LegacyFlatV1 preserves the exact mv.state-diagnostic.v1 flat-record preimage.
/// RecordIdPrefixV2 is an explicitly versioned large-world digest which commits the same canonical
/// record values through stable 14-bit RecordId-prefix slices and a compact partition root.
/// </summary>
public enum PartitionCanonicalDigestAlgorithmV1 : byte
{
    LegacyFlatV1 = 0,
    RecordIdPrefixV2 = 1,
}

internal sealed class PartitionDigestSliceV2
{
    public PartitionDigestSliceV2(ushort prefix, ulong recordCount, byte[] contentDigest)
    {
        if (prefix >= RecordIdPrefixPartitionDigestV2.PrefixCount)
            throw new ArgumentOutOfRangeException(nameof(prefix));
        if (recordCount == 0)
            throw new ArgumentOutOfRangeException(nameof(recordCount));
        ArgumentNullException.ThrowIfNull(contentDigest);
        if (contentDigest.Length != 32)
            throw new ArgumentException("Slice content digest must be exactly 32 bytes.", nameof(contentDigest));

        Prefix = prefix;
        RecordCount = recordCount;
        ContentDigest = contentDigest.ToArray();
    }

    public ushort Prefix { get; }
    public ulong RecordCount { get; }
    public byte[] ContentDigest { get; }
}

/// <summary>
/// Versioned large-world canonical partition digest.
///
/// The logical slice is the first 14 bits of the canonical RecordId. Slice content hashes are
/// independent from Step/revision so unchanged slices can be retained across authoritative Steps.
/// The partition root binds revision/basis/detail/item-count plus the complete ordered slice set.
///
/// This does not redefine LegacyFlatV1. Callers must carry DigestAlgorithm on the header.
/// </summary>
public static class RecordIdPrefixPartitionDigestV2
{
    public const int PrefixBits = 14;
    public const ushort PrefixCount = 1 << PrefixBits;
    public const uint DiagnosticPartitionVersion = 2;

    private const string SliceContentLabel = "mv.partition-recordid-prefix-slice.v2";
    private const string PartitionRootLabel = "mv.partition-recordid-prefix.v2";

    public static ushort PrefixOf(OpaqueId128 recordId)
    {
        if (recordId.IsZero)
            throw new ArgumentException("RecordId ZERO is invalid.", nameof(recordId));

        var bytes = recordId.ToBytes();
        return checked((ushort)(((uint)bytes[0] << 6) | ((uint)bytes[1] >> 2)));
    }

    public static StableToken SliceKey(DomainPartitionIdentityV1 identity, ushort prefix)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (prefix >= PrefixCount)
            throw new ArgumentOutOfRangeException(nameof(prefix));

        return new StableToken(
            identity.PartitionId.Value +
            "/rid14-" +
            prefix.ToString("x4", CultureInfo.InvariantCulture));
    }

    public static PartitionStateHeaderV1 CreateHeader<TPayload>(
        DomainPartitionStateV1<TPayload> partition,
        ulong revision,
        ulong basisStep,
        DetailLevelV1 detailLevel,
        Func<DomainRecordEnvelopeV1<TPayload>, byte[]> canonicalRecordEncoding)
    {
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(canonicalRecordEncoding);
        if (revision == 0)
            throw new ArgumentOutOfRangeException(nameof(revision));
        if (!Enum.IsDefined(detailLevel))
            throw new ArgumentOutOfRangeException(nameof(detailLevel));

        var slices = new List<PartitionDigestSliceV2>();
        var currentPrefix = -1;
        var currentRecords = new List<ReadOnlyMemory<byte>>();

        void Flush()
        {
            if (currentRecords.Count == 0)
                return;

            slices.Add(CreateSlice(
                partition.Identity,
                checked((ushort)currentPrefix),
                currentRecords));
            currentRecords.Clear();
        }

        OpaqueId128? previous = null;
        ulong actualCount = 0;
        foreach (var record in partition.RecordsCanonical)
        {
            ArgumentNullException.ThrowIfNull(record);
            if (record.RecordSchema != partition.Identity.RecordSchema)
                throw new InvalidDataException("domain.record-schema-mismatch");
            if (record.CreatedStep > basisStep)
                throw new InvalidDataException("domain.record-created-after-partition-basis");
            if (record.RetiredStep is { } retiredAfterBasis && retiredAfterBasis > basisStep)
                throw new InvalidDataException("domain.record-retired-after-partition-basis");
            if (previous is { } prior && prior.CompareTo(record.RecordId) >= 0)
                throw new InvalidDataException("domain.record-order-not-canonical");
            previous = record.RecordId;

            var prefix = PrefixOf(record.RecordId);
            if (currentPrefix != prefix)
            {
                Flush();
                currentPrefix = prefix;
            }

            var encoded = canonicalRecordEncoding(record)
                ?? throw new InvalidDataException("domain.record-canonical-encoding-null");
            if (encoded.Length == 0)
                throw new InvalidDataException("domain.record-canonical-encoding-empty");
            currentRecords.Add(encoded);
            actualCount = checked(actualCount + 1UL);
        }
        Flush();

        if (actualCount != partition.ItemCount)
            throw new InvalidDataException("domain.record-prefix-digest-count-drift");

        return CreateHeaderFromPrevalidatedSlices(
            partition.Identity,
            revision,
            basisStep,
            detailLevel,
            partition.ItemCount,
            slices);
    }

    internal static PartitionDigestSliceV2 CreateSlice(
        DomainPartitionIdentityV1 identity,
        ushort prefix,
        IReadOnlyList<ReadOnlyMemory<byte>> canonicalRecords)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(canonicalRecords);
        if (prefix >= PrefixCount)
            throw new ArgumentOutOfRangeException(nameof(prefix));
        if (canonicalRecords.Count == 0)
            throw new ArgumentException("A diagnostic slice cannot be empty.", nameof(canonicalRecords));

        using var session = HashSuite.BeginDomainHashStreaming(SliceContentLabel);
        var writer = session.Writer;
        writer.WriteMapStart(7);
        writer.WriteUnsigned(0); writer.WriteAsciiText(identity.PartitionId.Value);
        writer.WriteUnsigned(1); writer.WriteAsciiText(identity.RecordSchema.SchemaId.Value);
        writer.WriteUnsigned(2); writer.WriteUnsigned(identity.RecordSchema.Version.Major);
        writer.WriteUnsigned(3); writer.WriteUnsigned(identity.RecordSchema.Version.Minor);
        writer.WriteUnsigned(4); writer.WriteUnsigned(DiagnosticPartitionVersion);
        writer.WriteUnsigned(5); writer.WriteUnsigned(prefix);
        writer.WriteUnsigned(6);
        writer.WriteArrayStart(checked((ulong)canonicalRecords.Count));
        foreach (var encoded in canonicalRecords)
        {
            if (encoded.IsEmpty)
                throw new InvalidDataException("domain.record-canonical-encoding-empty");
            session.AppendCanonicalBytes(encoded.Span);
        }

        return new PartitionDigestSliceV2(
            prefix,
            checked((ulong)canonicalRecords.Count),
            session.Complete());
    }

    internal static PartitionDigestSliceV2 CreateSliceFromPrevalidatedCanonicalBytes(
        DomainPartitionIdentityV1 identity,
        ushort prefix,
        ulong recordCount,
        ReadOnlySpan<byte> concatenatedCanonicalRecords)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (prefix >= PrefixCount)
            throw new ArgumentOutOfRangeException(nameof(prefix));
        if (recordCount == 0)
            throw new ArgumentOutOfRangeException(nameof(recordCount));
        if (concatenatedCanonicalRecords.IsEmpty)
            throw new ArgumentException(
                "Canonical record bytes cannot be empty.",
                nameof(concatenatedCanonicalRecords));

        using var session = HashSuite.BeginDomainHashStreaming(SliceContentLabel);
        var writer = session.Writer;
        writer.WriteMapStart(7);
        writer.WriteUnsigned(0); writer.WriteAsciiText(identity.PartitionId.Value);
        writer.WriteUnsigned(1); writer.WriteAsciiText(identity.RecordSchema.SchemaId.Value);
        writer.WriteUnsigned(2); writer.WriteUnsigned(identity.RecordSchema.Version.Major);
        writer.WriteUnsigned(3); writer.WriteUnsigned(identity.RecordSchema.Version.Minor);
        writer.WriteUnsigned(4); writer.WriteUnsigned(DiagnosticPartitionVersion);
        writer.WriteUnsigned(5); writer.WriteUnsigned(prefix);
        writer.WriteUnsigned(6);
        writer.WriteArrayStart(recordCount);
        session.AppendCanonicalBytes(concatenatedCanonicalRecords);

        return new PartitionDigestSliceV2(
            prefix,
            recordCount,
            session.Complete());
    }

    internal static PartitionStateHeaderV1 CreateHeaderFromPrevalidatedSlices(
        DomainPartitionIdentityV1 identity,
        ulong revision,
        ulong basisStep,
        DetailLevelV1 detailLevel,
        ulong itemCount,
        IReadOnlyList<PartitionDigestSliceV2> slices)
        => CreateHeaderFromPrevalidatedSlices(
            identity,
            revision,
            basisStep,
            detailLevel,
            itemCount,
            slices,
            slices?.Count ?? throw new ArgumentNullException(nameof(slices)));

    internal static PartitionStateHeaderV1 CreateHeaderFromPrevalidatedSlices(
        DomainPartitionIdentityV1 identity,
        ulong revision,
        ulong basisStep,
        DetailLevelV1 detailLevel,
        ulong itemCount,
        IEnumerable<PartitionDigestSliceV2> slices,
        int sliceCount)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(slices);
        if (sliceCount < 0)
            throw new ArgumentOutOfRangeException(nameof(sliceCount));
        if (revision == 0)
            throw new ArgumentOutOfRangeException(nameof(revision));
        if (!Enum.IsDefined(detailLevel))
            throw new ArgumentOutOfRangeException(nameof(detailLevel));

        if (itemCount == 0 && sliceCount != 0)
            throw new InvalidDataException("domain.record-prefix-slice-count-drift");

        using var session = HashSuite.BeginDomainHashStreaming(PartitionRootLabel);
        var writer = session.Writer;
        writer.WriteMapStart(11);
        writer.WriteUnsigned(0); writer.WriteAsciiText(identity.PartitionId.Value);
        writer.WriteUnsigned(1); writer.WriteAsciiText(identity.OwnerDomain.Value);
        writer.WriteUnsigned(2); writer.WriteAsciiText(identity.PartitionSchema.SchemaId.Value);
        writer.WriteUnsigned(3); writer.WriteUnsigned(identity.PartitionSchema.Version.Major);
        writer.WriteUnsigned(4); writer.WriteUnsigned(identity.PartitionSchema.Version.Minor);
        writer.WriteUnsigned(5); writer.WriteUnsigned(revision);
        writer.WriteUnsigned(6); writer.WriteUnsigned(basisStep);
        writer.WriteUnsigned(7); writer.WriteUnsigned((byte)detailLevel);
        writer.WriteUnsigned(8); writer.WriteUnsigned(itemCount);
        writer.WriteUnsigned(9); writer.WriteUnsigned(DiagnosticPartitionVersion);
        writer.WriteUnsigned(10);
        writer.WriteArrayStart(checked((ulong)sliceCount));

        ushort? previous = null;
        ulong actualCount = 0;
        var actualSliceCount = 0;
        foreach (var slice in slices)
        {
            actualSliceCount = checked(actualSliceCount + 1);
            ArgumentNullException.ThrowIfNull(slice);
            if (previous is { } prior && prior >= slice.Prefix)
                throw new InvalidDataException("domain.record-prefix-slice-order");
            previous = slice.Prefix;
            actualCount = checked(actualCount + slice.RecordCount);
            if (actualCount > itemCount || actualSliceCount > sliceCount)
                throw new InvalidDataException("domain.record-prefix-slice-count-drift");

            writer.WriteArrayStart(3);
            writer.WriteUnsigned(slice.Prefix);
            writer.WriteUnsigned(slice.RecordCount);
            writer.WriteBytes(slice.ContentDigest);
        }

        if (actualCount != itemCount || actualSliceCount != sliceCount)
            throw new InvalidDataException("domain.record-prefix-slice-count-drift");

        var digest = session.Complete();

        return new PartitionStateHeaderV1(
            identity,
            revision,
            basisStep,
            detailLevel,
            itemCount,
            digest,
            PartitionCanonicalDigestAlgorithmV1.RecordIdPrefixV2);
    }
}
