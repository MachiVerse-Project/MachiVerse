using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Production Snapshot/recovery proof for the approved Governance territorial foundation. The
/// materialization's real Polity, TileScope and PublicAuthority resolver is reused during Snapshot
/// encoding and semantic recovery, so missing or wrong reference authority fails closed.
/// </summary>
public static class Qa04GovernanceTerritorialFoundationSnapshotRecoveryEvidenceV1
{
    public const int CanonicalPartitionCount = 3;
    public const ulong CanonicalRecordCount = Qa04GovernanceTerritorialFoundationCanonicalAuthorityV1.CanonicalCount;

    public static ulong Verify(Qa04GovernanceTerritorialFoundationCanonicalMaterializationV1 materialization)
    {
        ArgumentNullException.ThrowIfNull(materialization);
        if (materialization.MaterializedRecordCount != CanonicalRecordCount)
            throw new InvalidDataException("qa04.governance.territorial-foundation-snapshot-record-count");

        ulong verifiedRecords = 0;
        var verifiedPartitions = 0;

        verifiedRecords = checked(verifiedRecords + VerifyPartition(
            materialization.Jurisdictions,
            GovernanceJurisdictionPayloadV1.PartitionId,
            static payload => payload.ToStandardPayload(),
            GovernanceJurisdictionPayloadV1.FromStandardPayload,
            static payload => payload.CanonicalDigest(),
            materialization.References));
        verifiedPartitions++;

        verifiedRecords = checked(verifiedRecords + VerifyPartition(
            materialization.TerritorialClaims,
            GovernanceTerritorialClaimPayloadV1.PartitionId,
            static payload => payload.ToStandardPayload(),
            GovernanceTerritorialClaimPayloadV1.FromStandardPayload,
            static payload => payload.CanonicalDigest(),
            materialization.References));
        verifiedPartitions++;

        verifiedRecords = checked(verifiedRecords + VerifyPartition(
            materialization.EffectiveControls,
            GovernanceEffectiveControlPayloadV1.PartitionId,
            static payload => payload.ToStandardPayload(),
            GovernanceEffectiveControlPayloadV1.FromStandardPayload,
            static payload => payload.CanonicalDigest(),
            materialization.References));
        verifiedPartitions++;

        if (verifiedPartitions != CanonicalPartitionCount || verifiedRecords != CanonicalRecordCount)
            throw new InvalidDataException("qa04.governance.territorial-foundation-snapshot-total-mismatch");

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
            throw new InvalidDataException($"qa04.governance.territorial-foundation-snapshot-partition:{partitionId}");

        var header = PartitionStateHeaderV1.CreateCanonical(
            partition,
            revision: 1,
            basisStep: 0,
            detailLevel: DetailLevelV1.D2RegionalAggregate,
            digest);
        if (header.ItemCount != partition.ItemCount)
            throw new InvalidDataException($"qa04.governance.territorial-foundation-snapshot-header:{partitionId}");

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
            throw new InvalidDataException($"qa04.governance.territorial-foundation-snapshot-section:{partitionId}");

        var verifier = provider.CreateSemanticVerifier(header, references);
        var recovered = verifier.VerifyWithContext is not null
            ? verifier.VerifyWithContext(
                section.Fragments,
                new SnapshotSectionSemanticVerificationContextV1(references))
            : verifier.Verify(section.Fragments);
        if (recovered.LogicalItemCount != header.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(recovered.LogicalContentDigest, header.CanonicalDigest))
            throw new InvalidDataException($"qa04.governance.territorial-foundation-snapshot-semantic-rehash:{partitionId}");

        return recovered.LogicalItemCount;
    }
}
