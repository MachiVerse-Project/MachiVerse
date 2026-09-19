using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Production Snapshot/recovery proof for the approved 20,000 infrastructure.dependency records.
/// The actual Water/Power/Communication authority resolver from the accepted 290,000-record service
/// package is reused for encode validation and semantic recovery.
/// </summary>
public static class Qa04InfrastructureDependencySnapshotRecoveryEvidenceV1
{
    public const ulong CanonicalRecordCount = Qa04InfrastructureDependencyCanonicalAuthorityV1.CanonicalCount;

    public static ulong Verify(Qa04InfrastructureDependencyCanonicalMaterializationV1 materialization)
    {
        ArgumentNullException.ThrowIfNull(materialization);
        if (materialization.MaterializedRecordCount != CanonicalRecordCount)
            throw new InvalidDataException("qa04.infrastructure.dependency-snapshot-record-count");

        var partition = materialization.Partition;
        if (partition.Identity.PartitionId.Value != InfrastructureDependencyPayloadV1.PartitionId)
            throw new InvalidDataException("qa04.infrastructure.dependency-snapshot-partition-identity");

        var header = PartitionStateHeaderV1.CreateCanonical(
            partition,
            revision: 1,
            basisStep: 0,
            detailLevel: DetailLevelV1.D2RegionalAggregate,
            static payload => payload.CanonicalDigest());
        if (header.ItemCount != CanonicalRecordCount)
            throw new InvalidDataException("qa04.infrastructure.dependency-snapshot-header-count");

        var authority = new DomainPartitionSnapshotAuthorityV1<InfrastructureDependencyPayloadV1>(
            partition,
            header,
            static payload => payload.CanonicalDigest());
        var provider = new DomainPartitionSnapshotSectionProviderV1<InfrastructureDependencyPayloadV1>(
            InfrastructureDependencyPayloadV1.PartitionId,
            static payload => payload.ToStandardPayload(),
            InfrastructureDependencyPayloadV1.FromStandardPayload,
            static payload => payload.CanonicalDigest(),
            StandardDomainNestedSnapshotCodecRegistryV1.Default);

        var section = provider.Create(authority, materialization.References);
        if (section.LogicalItemCount != header.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(section.LogicalContentDigest, header.CanonicalDigest))
            throw new InvalidDataException("qa04.infrastructure.dependency-snapshot-section-authority");

        var verifier = provider.CreateSemanticVerifier(header, materialization.References);
        var recovered = verifier.VerifyWithContext is not null
            ? verifier.VerifyWithContext(
                section.Fragments,
                new SnapshotSectionSemanticVerificationContextV1(materialization.References))
            : verifier.Verify(section.Fragments);
        if (recovered.LogicalItemCount != header.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(recovered.LogicalContentDigest, header.CanonicalDigest))
            throw new InvalidDataException("qa04.infrastructure.dependency-snapshot-semantic-rehash");

        return recovered.LogicalItemCount;
    }
}
