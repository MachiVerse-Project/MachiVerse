using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

/// <summary>
/// Frozen authority for infrastructure.network_topology record schema 2.0.
/// StandardDomainPartitionRegistry remains v1; migration-aware composition opts into this authority explicitly.
/// </summary>
public sealed class InfrastructureNetworkTopologySnapshotAuthorityV2 : IDomainPartitionSnapshotAuthorityV1
{
    public InfrastructureNetworkTopologySnapshotAuthorityV2(
        InfrastructureNetworkTopologyPartitionStateV2 partition,
        PartitionStateHeaderV1 header)
    {
        Partition = partition ?? throw new ArgumentNullException(nameof(partition));
        Header = header ?? throw new ArgumentNullException(nameof(header));
        RecordIdsCanonical = Array.AsReadOnly(
            Partition.State.RecordsCanonical.Select(static record => record.RecordId).ToArray());
        VerifyBoundAuthority();
    }

    public InfrastructureNetworkTopologyPartitionStateV2 Partition { get; }
    public StableToken PartitionId => Identity.PartitionId;
    public DomainPartitionIdentityV1 Identity => InfrastructureNetworkTopologyPartitionIdentityV2.Identity;
    public PartitionStateHeaderV1 Header { get; }
    public SchemaRefV1 RecordSchema => InfrastructureNetworkTopologyRecordSchemaV2.RecordSchema;
    public ulong ActualItemCount => Partition.State.ItemCount;
    public IReadOnlyList<OpaqueId128> RecordIdsCanonical { get; }

    public static InfrastructureNetworkTopologySnapshotAuthorityV2 CreateCanonical(
        InfrastructureNetworkTopologyPartitionStateV2 partition,
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
            static payload => InfrastructureNetworkTopologyPayloadCanonicalDigestV2.Compute(payload));
        return new InfrastructureNetworkTopologySnapshotAuthorityV2(partition, header);
    }

    public void VerifyBoundAuthority()
    {
        InfrastructureNetworkTopologyPartitionIdentityV2.ValidateCanonicalContract();
        InfrastructureNetworkTopologyReferenceClosureV2.Validate(Partition.RecordSet.RecordsCanonical);
        if (Partition.State.Identity != Identity)
            throw new InvalidDataException("persistence.snapshot.infrastructure-network-v2-authority-identity");
        if (Header.PartitionId != Identity.PartitionId ||
            Header.OwnerDomain != Identity.OwnerDomain ||
            Header.Schema != Identity.PartitionSchema)
            throw new InvalidDataException("persistence.snapshot.infrastructure-network-v2-header-identity");
        if (Header.ItemCount != ActualItemCount ||
            ActualItemCount != checked((ulong)RecordIdsCanonical.Count))
            throw new InvalidDataException("persistence.snapshot.infrastructure-network-v2-item-count");

        OpaqueId128? previous = null;
        foreach (var recordId in RecordIdsCanonical)
        {
            if (recordId.IsZero)
                throw new InvalidDataException("persistence.snapshot.infrastructure-network-v2-record-id-zero");
            if (previous is { } prior && prior.CompareTo(recordId) >= 0)
                throw new InvalidDataException("persistence.snapshot.infrastructure-network-v2-record-order");
            previous = recordId;
        }

        var recomputed = PartitionStateHeaderV1.CreateCanonical(
            Partition.State,
            Header.Revision,
            Header.BasisStep,
            Header.DetailLevel,
            static payload => InfrastructureNetworkTopologyPayloadCanonicalDigestV2.Compute(payload));
        if (recomputed.PartitionId != Header.PartitionId ||
            recomputed.OwnerDomain != Header.OwnerDomain ||
            recomputed.Schema != Header.Schema ||
            recomputed.Revision != Header.Revision ||
            recomputed.BasisStep != Header.BasisStep ||
            recomputed.DetailLevel != Header.DetailLevel ||
            recomputed.ItemCount != Header.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(recomputed.CanonicalDigest, Header.CanonicalDigest))
            throw new InvalidDataException("persistence.snapshot.infrastructure-network-v2-header-material");
    }
}
