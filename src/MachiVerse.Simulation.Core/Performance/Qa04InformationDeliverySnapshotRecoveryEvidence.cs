using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Production Snapshot/recovery proof for the approved 20,000 information.delivery records.
/// Recovery uses the same actual InformationClaim/Resident/CommunicationService resolver as
/// production materialization and verifies the recovered semantic digest against the canonical
/// partition header.
/// </summary>
public static class Qa04InformationDeliverySnapshotRecoveryEvidenceV1
{
    public const ulong CanonicalRecordCount = Qa04InformationDeliveryCanonicalAuthorityV1.CanonicalCount;

    public static ulong Verify(Qa04InformationDeliveryCanonicalMaterializationV1 materialization)
    {
        ArgumentNullException.ThrowIfNull(materialization);
        if (materialization.MaterializedRecordCount != CanonicalRecordCount)
            throw new InvalidDataException("qa04.information.delivery-snapshot-record-count");

        var partition = materialization.Partition;
        if (partition.Identity.PartitionId.Value != InformationDeliveryPayloadV1.PartitionId)
            throw new InvalidDataException("qa04.information.delivery-snapshot-partition-identity");

        var header = PartitionStateHeaderV1.CreateCanonical(
            partition,
            revision: 1,
            basisStep: 0,
            detailLevel: DetailLevelV1.D2RegionalAggregate,
            static payload => payload.CanonicalDigest());
        if (header.ItemCount != CanonicalRecordCount)
            throw new InvalidDataException("qa04.information.delivery-snapshot-header-count");

        var authority = new DomainPartitionSnapshotAuthorityV1<InformationDeliveryPayloadV1>(
            partition,
            header,
            static payload => payload.CanonicalDigest());
        var provider = new DomainPartitionSnapshotSectionProviderV1<InformationDeliveryPayloadV1>(
            InformationDeliveryPayloadV1.PartitionId,
            static payload => payload.ToStandardPayload(),
            InformationDeliveryPayloadV1.FromStandardPayload,
            static payload => payload.CanonicalDigest(),
            StandardDomainNestedSnapshotCodecRegistryV1.Default);

        var section = provider.Create(authority, materialization.References);
        if (section.LogicalItemCount != header.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(section.LogicalContentDigest, header.CanonicalDigest))
            throw new InvalidDataException("qa04.information.delivery-snapshot-section-authority");

        var verifier = provider.CreateSemanticVerifier(header, materialization.References);
        var recovered = verifier.VerifyWithContext is not null
            ? verifier.VerifyWithContext(
                section.Fragments,
                new SnapshotSectionSemanticVerificationContextV1(materialization.References))
            : verifier.Verify(section.Fragments);
        if (recovered.LogicalItemCount != header.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(recovered.LogicalContentDigest, header.CanonicalDigest))
            throw new InvalidDataException("qa04.information.delivery-snapshot-semantic-rehash");

        return recovered.LogicalItemCount;
    }
}
