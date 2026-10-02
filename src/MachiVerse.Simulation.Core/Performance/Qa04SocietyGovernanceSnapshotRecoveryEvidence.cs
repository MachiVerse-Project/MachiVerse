using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Runs the production generic Domain-partition Snapshot codec over the six newly authoritative
/// perf.reference.v1 Society/Governance partitions and proves semantic recovery against canonical
/// frozen headers. The same actual reference resolver used by canonical materialization is reused by
/// Snapshot encoding and recovery validation; no fixture or permissive resolver participates.
/// </summary>
public static class Qa04SocietyGovernanceSnapshotRecoveryEvidenceV1
{
    public const int CanonicalPartitionCount = 6;
    public const ulong CanonicalRecordCount = 195_000;

    public static ulong Verify(Qa04SocietyGovernanceCanonicalMaterializationV1 materialization)
    {
        ArgumentNullException.ThrowIfNull(materialization);
        if (materialization.MaterializedRecordCount != CanonicalRecordCount ||
            CanonicalRecordCount != Qa04SocietyGovernanceCanonicalMaterializerV1.CanonicalMaterializedCount)
            throw new InvalidDataException("qa04.society-governance.snapshot-recovery-record-count");

        var references = materialization.References;
        ulong verifiedRecords = 0;
        var verifiedPartitions = 0;

        verifiedRecords = checked(verifiedRecords + VerifyPartition(
            materialization.Organizations,
            SocietyOrganizationPayloadV1.PartitionId,
            static payload => payload.ToStandardPayload(),
            SocietyOrganizationPayloadV1.FromStandardPayload,
            static payload => payload.CanonicalDigest(),
            references));
        verifiedPartitions++;

        verifiedRecords = checked(verifiedRecords + VerifyPartition(
            materialization.ContractClaims,
            SocietyContractClaimPayloadV1.PartitionId,
            static payload => payload.ToStandardPayload(),
            SocietyContractClaimPayloadV1.FromStandardPayload,
            static payload => payload.CanonicalDigest(),
            references));
        verifiedPartitions++;

        verifiedRecords = checked(verifiedRecords + VerifyPartition(
            materialization.InformationClaims,
            SocietyInformationClaimPayloadV1.PartitionId,
            static payload => payload.ToStandardPayload(),
            SocietyInformationClaimPayloadV1.FromStandardPayload,
            static payload => payload.CanonicalDigest(),
            references));
        verifiedPartitions++;

        verifiedRecords = checked(verifiedRecords + VerifyPartition(
            materialization.Institutions,
            GovernanceInstitutionPayloadV1.PartitionId,
            static payload => payload.ToStandardPayload(),
            GovernanceInstitutionPayloadV1.FromStandardPayload,
            static payload => payload.CanonicalDigest(),
            references));
        verifiedPartitions++;

        verifiedRecords = checked(verifiedRecords + VerifyPartition(
            materialization.PublicAuthorities,
            GovernancePublicAuthorityPayloadV1.PartitionId,
            static payload => payload.ToStandardPayload(),
            GovernancePublicAuthorityPayloadV1.FromStandardPayload,
            static payload => payload.CanonicalDigest(),
            references));
        verifiedPartitions++;

        verifiedRecords = checked(verifiedRecords + VerifyPartition(
            materialization.PermissionLicenses,
            GovernancePermissionLicensePayloadV1.PartitionId,
            static payload => payload.ToStandardPayload(),
            GovernancePermissionLicensePayloadV1.FromStandardPayload,
            static payload => payload.CanonicalDigest(),
            references));
        verifiedPartitions++;

        if (verifiedPartitions != CanonicalPartitionCount || verifiedRecords != CanonicalRecordCount)
            throw new InvalidDataException("qa04.society-governance.snapshot-recovery-total-mismatch");

        return verifiedRecords;
    }

    private static ulong VerifyPartition<TPayload>(
        DomainPartitionStateV1<TPayload> partition,
        string partitionId,
        Func<TPayload, IReadOnlyDictionary<string, object?>> toStandard,
        Func<IReadOnlyDictionary<string, object?>, TPayload> fromStandard,
        Func<TPayload, byte[]> digest,
        IDomainRecordSchemaResolverV1 references)
    {
        if (partition.Identity.PartitionId.Value != partitionId)
            throw new InvalidDataException($"qa04.society-governance.snapshot-recovery-partition-identity:{partitionId}");

        var header = PartitionStateHeaderV1.CreateCanonical(
            partition,
            revision: 1,
            basisStep: 0,
            detailLevel: DetailLevelV1.D2RegionalAggregate,
            digest);
        if (header.ItemCount != partition.ItemCount)
            throw new InvalidDataException($"qa04.society-governance.snapshot-recovery-header-count:{partitionId}");

        var authority = new DomainPartitionSnapshotAuthorityV1<TPayload>(partition, header, digest);
        var provider = new DomainPartitionSnapshotSectionProviderV1<TPayload>(
            partitionId,
            toStandard,
            fromStandard,
            digest,
            StandardDomainNestedSnapshotCodecRegistryV1.Default);
        var section = provider.Create(authority, references);
        if (section.LogicalItemCount != header.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(section.LogicalContentDigest, header.CanonicalDigest))
            throw new InvalidDataException($"qa04.society-governance.snapshot-recovery-section-authority:{partitionId}");

        var verifier = provider.CreateSemanticVerifier(header, references);
        var result = verifier.VerifyWithContext is not null
            ? verifier.VerifyWithContext(
                section.Fragments,
                new SnapshotSectionSemanticVerificationContextV1(references))
            : verifier.Verify(section.Fragments);
        if (result.LogicalItemCount != header.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(result.LogicalContentDigest, header.CanonicalDigest))
            throw new InvalidDataException($"qa04.society-governance.snapshot-recovery-semantic-rehash:{partitionId}");

        return header.ItemCount;
    }
}