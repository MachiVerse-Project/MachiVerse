using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Production Snapshot/recovery proof for the #240-approved 100 society.currency_money records.
/// Issuer references remain backed by actual canonical Organization authority during encode validation
/// and semantic recovery.
/// </summary>
public static class Qa04SocietyCurrencyMoneySnapshotRecoveryEvidenceV1
{
    public const ulong CanonicalRecordCount = Qa04SocietyCurrencyMoneyCanonicalAuthorityV1.CanonicalCount;

    public static ulong Verify(Qa04SocietyCurrencyMoneyCanonicalMaterializationV1 materialization)
    {
        ArgumentNullException.ThrowIfNull(materialization);
        if (materialization.MaterializedRecordCount != CanonicalRecordCount)
            throw new InvalidDataException("qa04.society.currency-money-snapshot-record-count");

        var partition = materialization.Currencies;
        if (partition.Identity.PartitionId.Value != SocietyCurrencyMoneyPayloadV1.PartitionId)
            throw new InvalidDataException("qa04.society.currency-money-snapshot-partition-identity");

        var header = PartitionStateHeaderV1.CreateCanonical(
            partition,
            revision: 1,
            basisStep: 0,
            detailLevel: DetailLevelV1.D2RegionalAggregate,
            static payload => payload.CanonicalDigest());
        if (header.ItemCount != CanonicalRecordCount)
            throw new InvalidDataException("qa04.society.currency-money-snapshot-header-count");

        var authority = new DomainPartitionSnapshotAuthorityV1<SocietyCurrencyMoneyPayloadV1>(
            partition,
            header,
            static payload => payload.CanonicalDigest());
        var provider = new DomainPartitionSnapshotSectionProviderV1<SocietyCurrencyMoneyPayloadV1>(
            SocietyCurrencyMoneyPayloadV1.PartitionId,
            static payload => payload.ToStandardPayload(),
            SocietyCurrencyMoneyPayloadV1.FromStandardPayload,
            static payload => payload.CanonicalDigest(),
            StandardDomainNestedSnapshotCodecRegistryV1.Default);

        var section = provider.Create(authority, materialization.References);
        if (section.LogicalItemCount != header.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(section.LogicalContentDigest, header.CanonicalDigest))
            throw new InvalidDataException("qa04.society.currency-money-snapshot-section-authority");

        var verifier = provider.CreateSemanticVerifier(header, materialization.References);
        var recovered = verifier.VerifyWithContext is not null
            ? verifier.VerifyWithContext(
                section.Fragments,
                new SnapshotSectionSemanticVerificationContextV1(materialization.References))
            : verifier.Verify(section.Fragments);
        if (recovered.LogicalItemCount != header.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(recovered.LogicalContentDigest, header.CanonicalDigest))
            throw new InvalidDataException("qa04.society.currency-money-snapshot-semantic-rehash");

        return recovered.LogicalItemCount;
    }
}
