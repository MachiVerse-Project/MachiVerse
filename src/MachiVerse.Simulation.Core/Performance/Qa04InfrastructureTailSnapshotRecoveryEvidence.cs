using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04InfrastructureTailSnapshotRecoveryEvidenceResultV1(
    ulong AddressPlaceIndexCount,
    ulong FailureRecoveryCount,
    ulong LineageCount);

/// <summary>Production Snapshot/recovery semantic proof for the final QA-04 Infrastructure records.</summary>
public static class Qa04InfrastructureTailSnapshotRecoveryEvidenceV1
{
    public static Qa04InfrastructureTailSnapshotRecoveryEvidenceResultV1 Verify(
        Qa04InfrastructureTailCanonicalMaterializationV1 materialization)
    {
        ArgumentNullException.ThrowIfNull(materialization);
        if (materialization.AddressPlaceIndexes.ItemCount != Qa04InfrastructureTailCanonicalAuthorityV1.AddressCount ||
            materialization.FailureRecoveries.ItemCount != Qa04InfrastructureTailCanonicalAuthorityV1.FailureRecoveryCount ||
            materialization.Lineages.ItemCount != Qa04InfrastructureTailCanonicalAuthorityV1.LineageCount)
            throw new InvalidDataException("qa04.infrastructure.tail-snapshot-record-count");

        var addresses = VerifyPartition(
            materialization.AddressPlaceIndexes,
            static payload => payload.ToStandardPayload(),
            InformationAddressPlaceIndexPayloadV1.FromStandardPayload,
            static payload => payload.CanonicalDigest(),
            materialization.References);
        var failures = VerifyPartition(
            materialization.FailureRecoveries,
            static payload => payload.ToStandardPayload(),
            InfrastructureFailureRecoveryPayloadV1.FromStandardPayload,
            static payload => payload.CanonicalDigest(),
            materialization.References);
        var lineages = VerifyPartition(
            materialization.Lineages,
            static payload => payload.ToStandardPayload(),
            InfrastructureLineagePayloadV1.FromStandardPayload,
            static payload => payload.CanonicalDigest(),
            materialization.References);

        if (addresses != Qa04InfrastructureTailCanonicalAuthorityV1.AddressCount ||
            failures != Qa04InfrastructureTailCanonicalAuthorityV1.FailureRecoveryCount ||
            lineages != Qa04InfrastructureTailCanonicalAuthorityV1.LineageCount)
            throw new InvalidDataException("qa04.infrastructure.tail-snapshot-semantic-count");

        return new Qa04InfrastructureTailSnapshotRecoveryEvidenceResultV1(addresses, failures, lineages);
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
            throw new InvalidDataException($"qa04.infrastructure.tail-snapshot-section:{partition.Identity.PartitionId.Value}");

        var verifier = provider.CreateSemanticVerifier(header, references);
        var recovered = verifier.VerifyWithContext is not null
            ? verifier.VerifyWithContext(section.Fragments, new SnapshotSectionSemanticVerificationContextV1(references))
            : verifier.Verify(section.Fragments);
        if (recovered.LogicalItemCount != header.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(recovered.LogicalContentDigest, header.CanonicalDigest))
            throw new InvalidDataException($"qa04.infrastructure.tail-snapshot-semantic-rehash:{partition.Identity.PartitionId.Value}");

        return recovered.LogicalItemCount;
    }
}
