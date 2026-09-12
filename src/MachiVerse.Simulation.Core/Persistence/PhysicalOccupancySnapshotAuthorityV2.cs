using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

/// <summary>
/// physical.occupancy record schema 2.0 専用の frozen authority。
/// generic DomainPartitionSnapshotAuthorityV1 の standard-v1 厳格性は緩めない。
/// </summary>
public sealed class PhysicalOccupancySnapshotAuthorityV2 : IDomainPartitionSnapshotAuthorityV1
{
    public PhysicalOccupancySnapshotAuthorityV2(
        PhysicalOccupancyPartitionStateV2 partition,
        PartitionStateHeaderV1 header)
    {
        Partition = partition ?? throw new ArgumentNullException(nameof(partition));
        Header = header ?? throw new ArgumentNullException(nameof(header));
        RecordIdsCanonical = Array.AsReadOnly(
            Partition.State.RecordsCanonical.Select(static record => record.RecordId).ToArray());
        VerifyBoundAuthority();
    }

    public PhysicalOccupancyPartitionStateV2 Partition { get; }
    public StableToken PartitionId => Identity.PartitionId;
    public DomainPartitionIdentityV1 Identity => PhysicalOccupancyPartitionIdentityV2.Identity;
    public PartitionStateHeaderV1 Header { get; }
    public SchemaRefV1 RecordSchema => PhysicalOccupancyRecordSchemaV2.RecordSchema;
    public ulong ActualItemCount => Partition.State.ItemCount;
    public IReadOnlyList<OpaqueId128> RecordIdsCanonical { get; }

    public static PhysicalOccupancySnapshotAuthorityV2 CreateCanonical(
        PhysicalOccupancyPartitionStateV2 partition,
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
            static payload => PhysicalOccupancyPayloadCanonicalDigestV2.Compute(payload));
        return new PhysicalOccupancySnapshotAuthorityV2(partition, header);
    }

    public void VerifyBoundAuthority()
    {
        PhysicalOccupancyPartitionIdentityV2.ValidateCanonicalContract();
        if (Partition.State.Identity != Identity)
            throw new InvalidDataException("persistence.snapshot.physical-occupancy-v2-authority-identity");
        if (Header.PartitionId != Identity.PartitionId ||
            Header.OwnerDomain != Identity.OwnerDomain ||
            Header.Schema != Identity.PartitionSchema)
            throw new InvalidDataException("persistence.snapshot.physical-occupancy-v2-header-identity");
        if (Header.ItemCount != ActualItemCount ||
            ActualItemCount != checked((ulong)RecordIdsCanonical.Count))
            throw new InvalidDataException("persistence.snapshot.physical-occupancy-v2-item-count");

        OpaqueId128? previous = null;
        foreach (var recordId in RecordIdsCanonical)
        {
            if (recordId.IsZero)
                throw new InvalidDataException("persistence.snapshot.physical-occupancy-v2-record-id-zero");
            if (previous is { } prior && prior.CompareTo(recordId) >= 0)
                throw new InvalidDataException("persistence.snapshot.physical-occupancy-v2-record-order");
            previous = recordId;
        }

        var recomputed = PartitionStateHeaderV1.CreateCanonical(
            Partition.State,
            Header.Revision,
            Header.BasisStep,
            Header.DetailLevel,
            static payload => PhysicalOccupancyPayloadCanonicalDigestV2.Compute(payload));
        if (recomputed.PartitionId != Header.PartitionId ||
            recomputed.OwnerDomain != Header.OwnerDomain ||
            recomputed.Schema != Header.Schema ||
            recomputed.Revision != Header.Revision ||
            recomputed.BasisStep != Header.BasisStep ||
            recomputed.DetailLevel != Header.DetailLevel ||
            recomputed.ItemCount != Header.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(recomputed.CanonicalDigest, Header.CanonicalDigest))
            throw new InvalidDataException("persistence.snapshot.physical-occupancy-v2-header-material");
    }
}
