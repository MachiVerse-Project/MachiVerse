using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

/// <summary>
/// Frozen Terrain v2 authority for record sets that are too large to retain as a complete
/// SpatialTerrainGeometryPartitionStateV2. The canonical header is computed from a repeatable record
/// stream while the authority retains only the existing canonical RecordId index and the record
/// factory needed by the streaming section provider.
/// </summary>
public sealed class SpatialTerrainGeometryStreamingSnapshotAuthorityV2 : IDomainPartitionSnapshotAuthorityV1
{
    private readonly IReadOnlyList<OpaqueId128> _recordIdsCanonical;
    private readonly Func<IEnumerable<SpatialTerrainGeometryRecordMaterialV2>> _recordFactory;

    private SpatialTerrainGeometryStreamingSnapshotAuthorityV2(
        IReadOnlyList<OpaqueId128> recordIdsCanonical,
        Func<IEnumerable<SpatialTerrainGeometryRecordMaterialV2>> recordFactory,
        PartitionStateHeaderV1 header)
    {
        _recordIdsCanonical = recordIdsCanonical ?? throw new ArgumentNullException(nameof(recordIdsCanonical));
        _recordFactory = recordFactory ?? throw new ArgumentNullException(nameof(recordFactory));
        Header = header ?? throw new ArgumentNullException(nameof(header));
        VerifyBoundAuthority();
    }

    public StableToken PartitionId => Identity.PartitionId;
    public DomainPartitionIdentityV1 Identity => SpatialTerrainGeometryPartitionIdentityV2.Identity;
    public PartitionStateHeaderV1 Header { get; }
    public SchemaRefV1 RecordSchema => SpatialTerrainGeometryRecordSchemaV2.RecordSchema;
    public ulong ActualItemCount => checked((ulong)_recordIdsCanonical.Count);
    public IReadOnlyList<OpaqueId128> RecordIdsCanonical => _recordIdsCanonical;

    public static SpatialTerrainGeometryStreamingSnapshotAuthorityV2 CreateCanonical(
        IReadOnlyList<OpaqueId128> recordIdsCanonical,
        Func<IEnumerable<SpatialTerrainGeometryRecordMaterialV2>> recordFactory,
        ulong revision,
        ulong basisStep,
        DetailLevelV1 detailLevel)
    {
        ArgumentNullException.ThrowIfNull(recordIdsCanonical);
        ArgumentNullException.ThrowIfNull(recordFactory);
        SpatialTerrainGeometryPartitionIdentityV2.ValidateCanonicalContract();
        SpatialTerrainGeometryRecordSchemaV2.ValidateCanonicalContract();

        var header = PartitionStateHeaderStreamingV1.CreateCanonical(
            SpatialTerrainGeometryPartitionIdentityV2.Identity,
            revision,
            basisStep,
            detailLevel,
            checked((ulong)recordIdsCanonical.Count),
            EnumerateCanonicalEnvelopes(recordIdsCanonical, recordFactory),
            static payload => SpatialTerrainGeometryPayloadCanonicalDigestV2.Compute(payload));

        return new SpatialTerrainGeometryStreamingSnapshotAuthorityV2(
            recordIdsCanonical,
            recordFactory,
            header);
    }

    public IEnumerable<SpatialTerrainGeometryRecordMaterialV2> EnumerateCanonicalRecords()
        => EnumerateAndValidateRecords(_recordIdsCanonical, _recordFactory);

    public void VerifyBoundAuthority()
    {
        SpatialTerrainGeometryPartitionIdentityV2.ValidateCanonicalContract();
        SpatialTerrainGeometryRecordSchemaV2.ValidateCanonicalContract();
        if (Identity != SpatialTerrainGeometryPartitionIdentityV2.Identity)
            throw new InvalidDataException("persistence.snapshot.terrain-v2-stream-authority-identity");
        if (Header.PartitionId != Identity.PartitionId ||
            Header.OwnerDomain != Identity.OwnerDomain ||
            Header.Schema != Identity.PartitionSchema)
            throw new InvalidDataException("persistence.snapshot.terrain-v2-stream-header-identity");
        if (Header.ItemCount != ActualItemCount)
            throw new InvalidDataException("persistence.snapshot.terrain-v2-stream-item-count");

        OpaqueId128? previous = null;
        foreach (var recordId in _recordIdsCanonical)
        {
            if (recordId.IsZero)
                throw new InvalidDataException("persistence.snapshot.terrain-v2-stream-record-id-zero");
            if (previous is { } prior && prior.CompareTo(recordId) >= 0)
                throw new InvalidDataException("persistence.snapshot.terrain-v2-stream-record-id-order");
            previous = recordId;
        }
    }

    private static IEnumerable<DomainRecordEnvelopeV1<SpatialTerrainGeometryPayloadV2>> EnumerateCanonicalEnvelopes(
        IReadOnlyList<OpaqueId128> expectedRecordIds,
        Func<IEnumerable<SpatialTerrainGeometryRecordMaterialV2>> recordFactory)
    {
        foreach (var record in EnumerateAndValidateRecords(expectedRecordIds, recordFactory))
        {
            yield return new DomainRecordEnvelopeV1<SpatialTerrainGeometryPayloadV2>(
                record.RecordId,
                SpatialTerrainGeometryRecordSchemaV2.RecordSchema,
                record.Revision,
                record.CreatedStep,
                record.RetiredStep,
                record.DetailLevel,
                record.LineageRef,
                record.Payload);
        }
    }

    private static IEnumerable<SpatialTerrainGeometryRecordMaterialV2> EnumerateAndValidateRecords(
        IReadOnlyList<OpaqueId128> expectedRecordIds,
        Func<IEnumerable<SpatialTerrainGeometryRecordMaterialV2>> recordFactory)
    {
        var source = recordFactory()
            ?? throw new InvalidDataException("persistence.snapshot.terrain-v2-stream-record-source-null");
        var index = 0;
        foreach (var record in source)
        {
            ArgumentNullException.ThrowIfNull(record);
            if ((uint)index >= (uint)expectedRecordIds.Count)
                throw new InvalidDataException("persistence.snapshot.terrain-v2-stream-record-source-long");
            if (record.RecordId != expectedRecordIds[index])
                throw new InvalidDataException("persistence.snapshot.terrain-v2-stream-record-id-mismatch");
            if (record.RecordSchema != SpatialTerrainGeometryRecordSchemaV2.RecordSchema)
                throw new InvalidDataException("persistence.snapshot.terrain-v2-stream-record-schema");
            index++;
            yield return record;
        }

        if (index != expectedRecordIds.Count)
            throw new InvalidDataException("persistence.snapshot.terrain-v2-stream-record-source-short");
    }
}
