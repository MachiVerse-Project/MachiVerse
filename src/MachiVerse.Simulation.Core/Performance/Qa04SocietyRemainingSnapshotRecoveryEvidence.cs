using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04SocietyRemainingSnapshotRecoveryEvidenceResultV1(
    ulong BusinessProductionCount,
    ulong LogisticsObligationCount,
    ulong HistoryLineageCount);

/// <summary>Production Snapshot/recovery semantic proof for the final QA-04 Society records.</summary>
public static class Qa04SocietyRemainingSnapshotRecoveryEvidenceV1
{
    public static Qa04SocietyRemainingSnapshotRecoveryEvidenceResultV1 Verify(
        Qa04SocietyRemainingCanonicalMaterializationV1 materialization)
    {
        ArgumentNullException.ThrowIfNull(materialization);
        if (materialization.BusinessProductions.ItemCount != Qa04SocietyRemainingCanonicalAuthorityV1.BusinessCount ||
            materialization.LogisticsObligations.ItemCount != Qa04SocietyRemainingCanonicalAuthorityV1.LogisticsCount ||
            materialization.HistoryLineages.ItemCount != Qa04SocietyRemainingCanonicalAuthorityV1.HistoryCount)
            throw new InvalidDataException("qa04.society.remaining-snapshot-record-count");

        var business = VerifyPartition(
            materialization.BusinessProductions,
            static payload => payload.ToStandardPayload(),
            SocietyBusinessProductionPayloadV1.FromStandardPayload,
            static payload => payload.CanonicalDigest(),
            materialization.References);
        var logistics = VerifyPartition(
            materialization.LogisticsObligations,
            static payload => payload.ToStandardPayload(),
            SocietyLogisticsObligationPayloadV1.FromStandardPayload,
            static payload => payload.CanonicalDigest(),
            materialization.References);
        var history = VerifyPartition(
            materialization.HistoryLineages,
            static payload => payload.ToStandardPayload(),
            SocietyHistoryLineagePayloadV1.FromStandardPayload,
            static payload => payload.CanonicalDigest(),
            materialization.References);

        if (business != Qa04SocietyRemainingCanonicalAuthorityV1.BusinessCount ||
            logistics != Qa04SocietyRemainingCanonicalAuthorityV1.LogisticsCount ||
            history != Qa04SocietyRemainingCanonicalAuthorityV1.HistoryCount)
            throw new InvalidDataException("qa04.society.remaining-snapshot-semantic-count");

        return new Qa04SocietyRemainingSnapshotRecoveryEvidenceResultV1(business, logistics, history);
    }

    private static ulong VerifyPartition<TPayload>(
        DomainPartitionStateV1<TPayload> partition,
        Func<TPayload, IReadOnlyDictionary<string, object?>> toStandardPayload,
        Func<IReadOnlyDictionary<string, object?>, TPayload> fromStandardPayload,
        Func<TPayload, byte[]> digest,
        IDomainRecordSchemaResolverV1 references)
    {
        var header = PartitionStateHeaderV1.CreateCanonical(
            partition,
            revision: 1,
            basisStep: 0,
            DetailLevelV1.D2RegionalAggregate,
            digest);
        var authority = new DomainPartitionSnapshotAuthorityV1<TPayload>(partition, header, digest);
        var provider = new DomainPartitionSnapshotSectionProviderV1<TPayload>(
            partition.Identity.PartitionId.Value,
            toStandardPayload,
            fromStandardPayload,
            digest,
            StandardDomainNestedSnapshotCodecRegistryV1.Default);

        var section = provider.Create(authority, references);
        if (section.LogicalItemCount != header.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(section.LogicalContentDigest, header.CanonicalDigest))
            throw new InvalidDataException($"qa04.society.remaining-snapshot-section:{partition.Identity.PartitionId.Value}");

        var verifier = provider.CreateSemanticVerifier(header, references);
        var recovered = verifier.VerifyWithContext is not null
            ? verifier.VerifyWithContext(section.Fragments, new SnapshotSectionSemanticVerificationContextV1(references))
            : verifier.Verify(section.Fragments);
        if (recovered.LogicalItemCount != header.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(recovered.LogicalContentDigest, header.CanonicalDigest))
            throw new InvalidDataException($"qa04.society.remaining-snapshot-semantic-rehash:{partition.Identity.PartitionId.Value}");

        return recovered.LogicalItemCount;
    }
}
