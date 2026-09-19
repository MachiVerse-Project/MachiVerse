using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Production Snapshot/recovery proof for the #240-approved Society social-relations package.
/// Actual Organization/Resident authority remains available to every section encoder and semantic verifier.
/// </summary>
public static class Qa04SocietySocialRelationsSnapshotRecoveryEvidenceV1
{
    public const ulong CanonicalRecordCount = Qa04SocietySocialRelationsCanonicalAuthorityV1.CanonicalCount;

    public static ulong Verify(Qa04SocietySocialRelationsCanonicalMaterializationV1 materialization)
    {
        ArgumentNullException.ThrowIfNull(materialization);
        if (materialization.MaterializedRecordCount != CanonicalRecordCount)
            throw new InvalidDataException("qa04.society.social-relations-snapshot-record-count");

        var educationRecovered = VerifyPartition(
            materialization.Educations,
            SocietyEducationPayloadV1.PartitionId,
            Qa04SocietySocialRelationsCanonicalAuthorityV1.EducationCount,
            materialization.References,
            static payload => payload.ToStandardPayload(),
            SocietyEducationPayloadV1.FromStandardPayload,
            static payload => payload.CanonicalDigest());
        var cultureRecovered = VerifyPartition(
            materialization.Cultures,
            SocietyCulturePayloadV1.PartitionId,
            Qa04SocietySocialRelationsCanonicalAuthorityV1.CultureCount,
            materialization.References,
            static payload => payload.ToStandardPayload(),
            SocietyCulturePayloadV1.FromStandardPayload,
            static payload => payload.CanonicalDigest());
        var reputationRecovered = VerifyPartition(
            materialization.Reputations,
            SocietyReputationPayloadV1.PartitionId,
            Qa04SocietySocialRelationsCanonicalAuthorityV1.ReputationCount,
            materialization.References,
            static payload => payload.ToStandardPayload(),
            SocietyReputationPayloadV1.FromStandardPayload,
            static payload => payload.CanonicalDigest());

        var recovered = checked(educationRecovered + cultureRecovered + reputationRecovered);
        if (recovered != CanonicalRecordCount)
            throw new InvalidDataException("qa04.society.social-relations-snapshot-total-count");
        return recovered;
    }

    private static ulong VerifyPartition<TPayload>(
        DomainPartitionStateV1<TPayload> partition,
        string partitionId,
        ulong expectedCount,
        IDomainRecordSchemaResolverV1 references,
        Func<TPayload, IReadOnlyDictionary<string, object?>> toStandardPayload,
        Func<IReadOnlyDictionary<string, object?>, TPayload> fromStandardPayload,
        Func<TPayload, byte[]> canonicalDigest)
    {
        if (partition.Identity.PartitionId.Value != partitionId || partition.ItemCount != expectedCount)
            throw new InvalidDataException($"qa04.society.social-relations-snapshot-partition-drift:{partitionId}");

        var header = PartitionStateHeaderV1.CreateCanonical(
            partition,
            revision: 1,
            basisStep: 0,
            detailLevel: DetailLevelV1.D2RegionalAggregate,
            canonicalDigest);
        if (header.ItemCount != expectedCount)
            throw new InvalidDataException($"qa04.society.social-relations-snapshot-header-count:{partitionId}");

        var authority = new DomainPartitionSnapshotAuthorityV1<TPayload>(partition, header, canonicalDigest);
        var provider = new DomainPartitionSnapshotSectionProviderV1<TPayload>(
            partitionId,
            toStandardPayload,
            fromStandardPayload,
            canonicalDigest,
            StandardDomainNestedSnapshotCodecRegistryV1.Default);

        var section = provider.Create(authority, references);
        if (section.LogicalItemCount != header.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(section.LogicalContentDigest, header.CanonicalDigest))
            throw new InvalidDataException($"qa04.society.social-relations-snapshot-section-authority:{partitionId}");

        var verifier = provider.CreateSemanticVerifier(header, references);
        var recovered = verifier.VerifyWithContext is not null
            ? verifier.VerifyWithContext(
                section.Fragments,
                new SnapshotSectionSemanticVerificationContextV1(references))
            : verifier.Verify(section.Fragments);
        if (recovered.LogicalItemCount != header.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(recovered.LogicalContentDigest, header.CanonicalDigest))
            throw new InvalidDataException($"qa04.society.social-relations-snapshot-semantic-rehash:{partitionId}");

        return recovered.LogicalItemCount;
    }
}
