using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Production Snapshot/recovery proof for the approved media_distribution and record_store packages.
/// Semantic recovery is verified against canonical partition headers using the same production Ref
/// resolver as materialization.
/// </summary>
public static class Qa04RemainingInformationSnapshotRecoveryEvidenceV1
{
    public static ulong VerifyMediaDistribution(Qa04RemainingInformationCanonicalMaterializationV1 materialization)
    {
        ArgumentNullException.ThrowIfNull(materialization);
        var partition = materialization.MediaDistribution;
        if (partition.ItemCount != Qa04RemainingInformationCanonicalAuthorityV1.MediaDistributionCount ||
            partition.Identity.PartitionId.Value != InformationMediaDistributionPayloadV1.PartitionId)
            throw new InvalidDataException("qa04.information.media-distribution-snapshot-partition-drift");

        var header = PartitionStateHeaderV1.CreateCanonical(
            partition,
            revision: 1,
            basisStep: 0,
            detailLevel: DetailLevelV1.D2RegionalAggregate,
            static payload => payload.CanonicalDigest());
        if (header.ItemCount != Qa04RemainingInformationCanonicalAuthorityV1.MediaDistributionCount)
            throw new InvalidDataException("qa04.information.media-distribution-snapshot-header-count");

        var authority = new DomainPartitionSnapshotAuthorityV1<InformationMediaDistributionPayloadV1>(
            partition,
            header,
            static payload => payload.CanonicalDigest());
        var provider = new DomainPartitionSnapshotSectionProviderV1<InformationMediaDistributionPayloadV1>(
            InformationMediaDistributionPayloadV1.PartitionId,
            static payload => payload.ToStandardPayload(),
            InformationMediaDistributionPayloadV1.FromStandardPayload,
            static payload => payload.CanonicalDigest(),
            StandardDomainNestedSnapshotCodecRegistryV1.Default);

        var section = provider.Create(authority, materialization.References);
        if (section.LogicalItemCount != header.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(section.LogicalContentDigest, header.CanonicalDigest))
            throw new InvalidDataException("qa04.information.media-distribution-snapshot-section-authority");

        var verifier = provider.CreateSemanticVerifier(header, materialization.References);
        var recovered = verifier.VerifyWithContext is not null
            ? verifier.VerifyWithContext(
                section.Fragments,
                new SnapshotSectionSemanticVerificationContextV1(materialization.References))
            : verifier.Verify(section.Fragments);
        if (recovered.LogicalItemCount != header.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(recovered.LogicalContentDigest, header.CanonicalDigest))
            throw new InvalidDataException("qa04.information.media-distribution-snapshot-semantic-rehash");

        return recovered.LogicalItemCount;
    }

    public static ulong VerifyRecordStore(Qa04RemainingInformationCanonicalMaterializationV1 materialization)
    {
        ArgumentNullException.ThrowIfNull(materialization);
        var partition = materialization.RecordStore;
        if (partition.ItemCount != Qa04RemainingInformationCanonicalAuthorityV1.RecordStoreCount ||
            partition.Identity.PartitionId.Value != InformationRecordStorePayloadV1.PartitionId)
            throw new InvalidDataException("qa04.information.record-store-snapshot-partition-drift");

        var header = PartitionStateHeaderV1.CreateCanonical(
            partition,
            revision: 1,
            basisStep: 0,
            detailLevel: DetailLevelV1.D2RegionalAggregate,
            static payload => payload.CanonicalDigest());
        if (header.ItemCount != Qa04RemainingInformationCanonicalAuthorityV1.RecordStoreCount)
            throw new InvalidDataException("qa04.information.record-store-snapshot-header-count");

        var authority = new DomainPartitionSnapshotAuthorityV1<InformationRecordStorePayloadV1>(
            partition,
            header,
            static payload => payload.CanonicalDigest());
        var provider = new DomainPartitionSnapshotSectionProviderV1<InformationRecordStorePayloadV1>(
            InformationRecordStorePayloadV1.PartitionId,
            static payload => payload.ToStandardPayload(),
            InformationRecordStorePayloadV1.FromStandardPayload,
            static payload => payload.CanonicalDigest(),
            StandardDomainNestedSnapshotCodecRegistryV1.Default);

        var section = provider.Create(authority, materialization.References);
        if (section.LogicalItemCount != header.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(section.LogicalContentDigest, header.CanonicalDigest))
            throw new InvalidDataException("qa04.information.record-store-snapshot-section-authority");

        var verifier = provider.CreateSemanticVerifier(header, materialization.References);
        var recovered = verifier.VerifyWithContext is not null
            ? verifier.VerifyWithContext(
                section.Fragments,
                new SnapshotSectionSemanticVerificationContextV1(materialization.References))
            : verifier.Verify(section.Fragments);
        if (recovered.LogicalItemCount != header.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(recovered.LogicalContentDigest, header.CanonicalDigest))
            throw new InvalidDataException("qa04.information.record-store-snapshot-semantic-rehash");

        return recovered.LogicalItemCount;
    }
}
