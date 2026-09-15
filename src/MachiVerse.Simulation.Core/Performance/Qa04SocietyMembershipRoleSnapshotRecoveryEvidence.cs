using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Production Snapshot/recovery proof for the approved 80,000 society.membership_role records.
/// Actual Organization and Resident authority is retained in the materialization resolver and reused
/// during encode validation and semantic recovery.
/// </summary>
public static class Qa04SocietyMembershipRoleSnapshotRecoveryEvidenceV1
{
    public const ulong CanonicalRecordCount = Qa04SocietyMembershipRoleCanonicalAuthorityV1.CanonicalCount;

    public static ulong Verify(Qa04SocietyMembershipRoleCanonicalMaterializationV1 materialization)
    {
        ArgumentNullException.ThrowIfNull(materialization);
        if (materialization.MaterializedRecordCount != CanonicalRecordCount)
            throw new InvalidDataException("qa04.society.membership-role-snapshot-record-count");

        var partition = materialization.MembershipRoles;
        if (partition.Identity.PartitionId.Value != SocietyMembershipRolePayloadV1.PartitionId)
            throw new InvalidDataException("qa04.society.membership-role-snapshot-partition-identity");

        var header = PartitionStateHeaderV1.CreateCanonical(
            partition,
            revision: 1,
            basisStep: 0,
            detailLevel: DetailLevelV1.D2RegionalAggregate,
            static payload => payload.CanonicalDigest());
        if (header.ItemCount != CanonicalRecordCount)
            throw new InvalidDataException("qa04.society.membership-role-snapshot-header-count");

        var authority = new DomainPartitionSnapshotAuthorityV1<SocietyMembershipRolePayloadV1>(
            partition,
            header,
            static payload => payload.CanonicalDigest());
        var provider = new DomainPartitionSnapshotSectionProviderV1<SocietyMembershipRolePayloadV1>(
            SocietyMembershipRolePayloadV1.PartitionId,
            static payload => payload.ToStandardPayload(),
            SocietyMembershipRolePayloadV1.FromStandardPayload,
            static payload => payload.CanonicalDigest(),
            StandardDomainNestedSnapshotCodecRegistryV1.Default);

        var section = provider.Create(authority, materialization.References);
        if (section.LogicalItemCount != header.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(section.LogicalContentDigest, header.CanonicalDigest))
            throw new InvalidDataException("qa04.society.membership-role-snapshot-section-authority");

        var verifier = provider.CreateSemanticVerifier(header, materialization.References);
        var recovered = verifier.VerifyWithContext is not null
            ? verifier.VerifyWithContext(
                section.Fragments,
                new SnapshotSectionSemanticVerificationContextV1(materialization.References))
            : verifier.Verify(section.Fragments);
        if (recovered.LogicalItemCount != header.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(recovered.LogicalContentDigest, header.CanonicalDigest))
            throw new InvalidDataException("qa04.society.membership-role-snapshot-semantic-rehash");

        return recovered.LogicalItemCount;
    }
}
