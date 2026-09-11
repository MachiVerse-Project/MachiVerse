using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

/// <summary>
/// Bounded-memory fragment source for spatial.terrain_geometry record schema 2.0.
///
/// The existing canonical fragment wire and 32 MiB target / 64 MiB hard limit are preserved.
/// A first pass materializes one record at a time only long enough to measure its existing v2 wire
/// length and records the resulting per-fragment item counts. A second pass recreates the canonical
/// record stream and holds only one fragment-sized record group while producing its payload.
/// No list of all record payloads or all fragment payloads is retained by this source.
/// </summary>
public sealed class SpatialTerrainGeometryStreamingFragmentSourceV2
{
    private readonly PartitionStateHeaderV1 _header;
    private readonly Func<IEnumerable<SpatialTerrainGeometryRecordMaterialV2>> _recordFactory;
    private readonly ulong _expectedItemCount;

    public SpatialTerrainGeometryStreamingFragmentSourceV2(
        PartitionStateHeaderV1 header,
        ulong expectedItemCount,
        Func<IEnumerable<SpatialTerrainGeometryRecordMaterialV2>> recordFactory)
    {
        _header = header ?? throw new ArgumentNullException(nameof(header));
        _recordFactory = recordFactory ?? throw new ArgumentNullException(nameof(recordFactory));
        _expectedItemCount = expectedItemCount;
        RequireHeaderIdentity(_header);
        if (_header.ItemCount != expectedItemCount)
            throw new InvalidDataException("persistence.snapshot.terrain-v2-stream-header-item-count");
    }

    public IEnumerable<SnapshotSectionFragmentMaterialV1> EnumerateFragments()
    {
        var plan = BuildPlan();
        var fragmentCount = checked((uint)plan.Length);
        using var enumerator = CreateRecordEnumerator();
        OpaqueId128? previous = null;
        ulong total = 0;

        for (var fragmentIndex = 0; fragmentIndex < plan.Length; fragmentIndex++)
        {
            var itemCount = plan[fragmentIndex];
            var group = new List<SpatialTerrainGeometryRecordMaterialV2>(itemCount);
            for (var itemIndex = 0; itemIndex < itemCount; itemIndex++)
            {
                if (!enumerator.MoveNext())
                    throw new InvalidDataException("persistence.snapshot.terrain-v2-stream-second-pass-short");
                var record = enumerator.Current
                    ?? throw new InvalidDataException("persistence.snapshot.terrain-v2-stream-record-null");
                ValidateRecord(record, ref previous);
                group.Add(record);
            }

            var payload = SpatialTerrainGeometrySnapshotFragmentWireV2.Encode(_header, group);
            if (payload.Length > CanonicalSnapshotSectionValidationV1.HardMaxUncompressedBytes)
                throw new InvalidDataException("persistence.snapshot-item-too-large");

            total = checked(total + (ulong)group.Count);
            yield return new SnapshotSectionFragmentMaterialV1(
                SpatialTerrainGeometryRecordSchemaV2.PartitionId,
                checked((uint)fragmentIndex),
                fragmentCount,
                group.Count == 0 ? null : group[0].RecordId.ToBytes(),
                group.Count == 0 ? null : group[^1].RecordId.ToBytes(),
                checked((ulong)group.Count),
                payload);
        }

        if (enumerator.MoveNext())
            throw new InvalidDataException("persistence.snapshot.terrain-v2-stream-second-pass-long");
        if (total != _expectedItemCount)
            throw new InvalidDataException("persistence.snapshot.terrain-v2-stream-second-pass-count");
    }

    private int[] BuildPlan()
    {
        var headerBytes = DomainPartitionSnapshotWireCodecV1.EncodeHeader(_header);
        var baseSize = LengthDelimitedFieldSize(1, headerBytes.Length);
        if (baseSize > CanonicalSnapshotSectionValidationV1.HardMaxUncompressedBytes)
            throw new InvalidDataException("persistence.snapshot-item-too-large");

        if (_expectedItemCount == 0)
        {
            using var empty = CreateRecordEnumerator();
            if (empty.MoveNext())
                throw new InvalidDataException("persistence.snapshot.terrain-v2-stream-first-pass-long");
            return [0];
        }

        var counts = new List<int>();
        var currentCount = 0;
        var currentSize = baseSize;
        ulong total = 0;
        OpaqueId128? previous = null;

        using var enumerator = CreateRecordEnumerator();
        while (enumerator.MoveNext())
        {
            var record = enumerator.Current
                ?? throw new InvalidDataException("persistence.snapshot.terrain-v2-stream-record-null");
            ValidateRecord(record, ref previous);

            var recordBytes = SpatialTerrainGeometryRecordWireCodecV2.Encode(record);
            var recordSize = LengthDelimitedFieldSize(2, recordBytes.Length);
            if (checked(baseSize + recordSize) > CanonicalSnapshotSectionValidationV1.HardMaxUncompressedBytes)
                throw new InvalidDataException("persistence.snapshot-item-too-large");

            if (currentCount > 0 &&
                checked(currentSize + recordSize) > CanonicalSnapshotSectionValidationV1.TargetUncompressedBytes)
            {
                counts.Add(currentCount);
                currentCount = 0;
                currentSize = baseSize;
            }

            currentCount = checked(currentCount + 1);
            currentSize = checked(currentSize + recordSize);
            total = checked(total + 1);
            if (total > _expectedItemCount)
                throw new InvalidDataException("persistence.snapshot.terrain-v2-stream-first-pass-long");
        }

        if (currentCount > 0) counts.Add(currentCount);
        if (total != _expectedItemCount)
            throw new InvalidDataException("persistence.snapshot.terrain-v2-stream-first-pass-count");
        if (counts.Count == 0 || counts.Count > uint.MaxValue)
            throw new InvalidDataException("persistence.snapshot.terrain-v2-fragment-count");
        return counts.ToArray();
    }

    private IEnumerator<SpatialTerrainGeometryRecordMaterialV2> CreateRecordEnumerator()
        => (_recordFactory()
            ?? throw new InvalidDataException("persistence.snapshot.terrain-v2-stream-record-source-null"))
            .GetEnumerator();

    private void ValidateRecord(
        SpatialTerrainGeometryRecordMaterialV2 record,
        ref OpaqueId128? previous)
    {
        if (record.RecordSchema != SpatialTerrainGeometryRecordSchemaV2.RecordSchema)
            throw new InvalidDataException("persistence.snapshot.terrain-v2-stream-record-schema");
        if (record.CreatedStep > _header.BasisStep ||
            record.RetiredStep is { } retired && retired > _header.BasisStep)
            throw new InvalidDataException("persistence.snapshot.terrain-v2-stream-record-step-after-basis");
        if (previous is { } prior && prior.CompareTo(record.RecordId) >= 0)
            throw new InvalidDataException("persistence.snapshot.terrain-v2-stream-record-order");
        previous = record.RecordId;
    }

    private static void RequireHeaderIdentity(PartitionStateHeaderV1 header)
    {
        var identity = StandardDomainPartitionRegistry.Get(SpatialTerrainGeometryRecordSchemaV2.PartitionId);
        if (header.PartitionId != identity.PartitionId ||
            header.OwnerDomain != identity.OwnerDomain ||
            header.Schema != identity.PartitionSchema)
            throw new InvalidDataException("persistence.snapshot.terrain-v2-stream-header-identity");
    }

    private static int LengthDelimitedFieldSize(int fieldNumber, int payloadLength)
    {
        if (fieldNumber <= 0 || payloadLength < 0) throw new ArgumentOutOfRangeException();
        var tag = checked((ulong)fieldNumber << 3) | 2UL;
        return checked(VarUIntSize(tag) + VarUIntSize(checked((ulong)payloadLength)) + payloadLength);
    }

    private static int VarUIntSize(ulong value)
    {
        var size = 1;
        while (value >= 0x80)
        {
            size++;
            value >>= 7;
        }
        return size;
    }
}
