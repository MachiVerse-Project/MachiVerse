using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

/// <summary>
/// Frozen authoritative material boundary for spatial.terrain_geometry record schema 2.0.
/// This is intentionally specialized instead of weakening DomainPartitionSnapshotAuthorityV1,
/// whose exact-standard-v1 identity checks continue to protect the existing 97-partition path.
/// </summary>
public sealed class SpatialTerrainGeometrySnapshotAuthorityV2 : IDomainPartitionSnapshotAuthorityV1
{
    public SpatialTerrainGeometrySnapshotAuthorityV2(
        SpatialTerrainGeometryPartitionStateV2 partition,
        PartitionStateHeaderV1 header)
    {
        Partition = partition ?? throw new ArgumentNullException(nameof(partition));
        Header = header ?? throw new ArgumentNullException(nameof(header));
        RecordIdsCanonical = Array.AsReadOnly(
            Partition.State.RecordsCanonical.Select(static record => record.RecordId).ToArray());
        VerifyBoundAuthority();
    }

    public SpatialTerrainGeometryPartitionStateV2 Partition { get; }
    public StableToken PartitionId => Identity.PartitionId;
    public DomainPartitionIdentityV1 Identity => SpatialTerrainGeometryPartitionIdentityV2.Identity;
    public PartitionStateHeaderV1 Header { get; }
    public SchemaRefV1 RecordSchema => SpatialTerrainGeometryRecordSchemaV2.RecordSchema;
    public ulong ActualItemCount => Partition.State.ItemCount;
    public IReadOnlyList<OpaqueId128> RecordIdsCanonical { get; }

    public static SpatialTerrainGeometrySnapshotAuthorityV2 CreateCanonical(
        SpatialTerrainGeometryPartitionStateV2 partition,
        ulong revision,
        ulong basisStep,
        DetailLevelV1 detailLevel)
    {
        ArgumentNullException.ThrowIfNull(partition);
        var header = PartitionStateHeaderV1.CreateCanonical(
            partition.State,
            revision,
            basisStep,
            detailLevel,
            payload => SpatialTerrainGeometryPayloadCanonicalDigestV2.Compute(payload));
        return new SpatialTerrainGeometrySnapshotAuthorityV2(partition, header);
    }

    public void VerifyBoundAuthority()
    {
        SpatialTerrainGeometryPartitionIdentityV2.ValidateCanonicalContract();
        if (Partition.State.Identity != Identity)
            throw new InvalidDataException("persistence.snapshot.terrain-v2-authority-identity");
        if (Header.PartitionId != Identity.PartitionId ||
            Header.OwnerDomain != Identity.OwnerDomain ||
            Header.Schema != Identity.PartitionSchema)
            throw new InvalidDataException("persistence.snapshot.terrain-v2-header-identity");
        if (Header.ItemCount != ActualItemCount ||
            ActualItemCount != checked((ulong)RecordIdsCanonical.Count))
            throw new InvalidDataException("persistence.snapshot.terrain-v2-item-count");

        OpaqueId128? previous = null;
        foreach (var recordId in RecordIdsCanonical)
        {
            if (recordId.IsZero)
                throw new InvalidDataException("persistence.snapshot.terrain-v2-record-id-zero");
            if (previous is { } prior && prior.CompareTo(recordId) >= 0)
                throw new InvalidDataException("persistence.snapshot.terrain-v2-record-order");
            previous = recordId;
        }

        Partition.RecordSet.ValidateReferences();
        var recomputed = PartitionStateHeaderV1.CreateCanonical(
            Partition.State,
            Header.Revision,
            Header.BasisStep,
            Header.DetailLevel,
            payload => SpatialTerrainGeometryPayloadCanonicalDigestV2.Compute(payload));
        if (recomputed.PartitionId != Header.PartitionId ||
            recomputed.OwnerDomain != Header.OwnerDomain ||
            recomputed.Schema != Header.Schema ||
            recomputed.Revision != Header.Revision ||
            recomputed.BasisStep != Header.BasisStep ||
            recomputed.DetailLevel != Header.DetailLevel ||
            recomputed.ItemCount != Header.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(recomputed.CanonicalDigest, Header.CanonicalDigest))
            throw new InvalidDataException("persistence.snapshot.terrain-v2-header-material");
    }
}
