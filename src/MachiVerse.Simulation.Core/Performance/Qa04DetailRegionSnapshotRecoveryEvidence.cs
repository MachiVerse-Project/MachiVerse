using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Production Snapshot/recovery proof for the full 4,096-record canonical DetailRegion authority.
/// The same actual TileScope resolver used by materialization is reused for encode validation and
/// semantic recovery verification.
/// </summary>
public static class Qa04DetailRegionSnapshotRecoveryEvidenceV1
{
    public const ulong CanonicalRecordCount = Qa04DetailRegionCanonicalAuthorityV1.CanonicalRegionCount;

    public static ulong Verify(Qa04DetailRegionCanonicalMaterializationV1 materialization)
    {
        ArgumentNullException.ThrowIfNull(materialization);
        if (materialization.MaterializedRecordCount != CanonicalRecordCount)
            throw new InvalidDataException("qa04.detail-region.snapshot-recovery-record-count");

        var partition = materialization.Partition;
        if (partition.Identity.PartitionId.Value != SpatialDetailRegionsPayloadV1.PartitionId)
            throw new InvalidDataException("qa04.detail-region.snapshot-partition-identity");

        var header = PartitionStateHeaderV1.CreateCanonical(
            partition,
            revision: 1,
            basisStep: 0,
            detailLevel: DetailLevelV1.D2RegionalAggregate,
            static payload => payload.CanonicalDigest());
        if (header.ItemCount != CanonicalRecordCount)
            throw new InvalidDataException("qa04.detail-region.snapshot-header-count");

        var authority = new DomainPartitionSnapshotAuthorityV1<SpatialDetailRegionsPayloadV1>(
            partition,
            header,
            static payload => payload.CanonicalDigest());
        var provider = new DomainPartitionSnapshotSectionProviderV1<SpatialDetailRegionsPayloadV1>(
            SpatialDetailRegionsPayloadV1.PartitionId,
            static payload => payload.ToStandardPayload(),
            SpatialDetailRegionsPayloadV1.FromStandardPayload,
            static payload => payload.CanonicalDigest(),
            StandardDomainNestedSnapshotCodecRegistryV1.Default);

        var section = provider.Create(authority, materialization.References);
        if (section.LogicalItemCount != header.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(section.LogicalContentDigest, header.CanonicalDigest))
            throw new InvalidDataException("qa04.detail-region.snapshot-section-authority");

        var verifier = provider.CreateSemanticVerifier(header, materialization.References);
        var recovered = verifier.VerifyWithContext is not null
            ? verifier.VerifyWithContext(
                section.Fragments,
                new SnapshotSectionSemanticVerificationContextV1(materialization.References))
            : verifier.Verify(section.Fragments);
        if (recovered.LogicalItemCount != header.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(recovered.LogicalContentDigest, header.CanonicalDigest))
            throw new InvalidDataException("qa04.detail-region.snapshot-semantic-rehash");

        return recovered.LogicalItemCount;
    }
}
