using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Production Snapshot/recovery proof for the canonical 40,000 network-service records plus
/// 250,000 ServiceQueue records. The same actual reference resolver used by materialization is
/// reused for Snapshot encoding and semantic recovery rehash.
/// </summary>
public static class Qa04InfrastructureServiceQueueSnapshotRecoveryEvidenceV1
{
    public const int CanonicalPartitionCount = 5;
    public const ulong CanonicalRecordCount = 290_000;

    public static ulong Verify(Qa04InfrastructureServiceQueueCanonicalMaterializationV1 materialization)
    {
        ArgumentNullException.ThrowIfNull(materialization);
        if (materialization.MaterializedRecordCount != CanonicalRecordCount ||
            CanonicalRecordCount != Qa04InfrastructureServiceQueueCanonicalMaterializerV1.CanonicalMaterializedCount)
            throw new InvalidDataException("qa04.infrastructure.service-queue-snapshot-recovery-record-count");

        var references = materialization.References;
        ulong verifiedRecords = 0;
        var verifiedPartitions = 0;

        verifiedRecords = checked(verifiedRecords + VerifyPartition(
            materialization.TransportServices,
            InfrastructureTransportServicePayloadV1.PartitionId,
            static payload => payload.ToStandardPayload(),
            InfrastructureTransportServicePayloadV1.FromStandardPayload,
            static payload => payload.CanonicalDigest(),
            references));
        verifiedPartitions++;

        verifiedRecords = checked(verifiedRecords + VerifyPartition(
            materialization.WaterServices,
            InfrastructureWaterServicePayloadV1.PartitionId,
            static payload => payload.ToStandardPayload(),
            InfrastructureWaterServicePayloadV1.FromStandardPayload,
            static payload => payload.CanonicalDigest(),
            references));
        verifiedPartitions++;

        verifiedRecords = checked(verifiedRecords + VerifyPartition(
            materialization.PowerServices,
            InfrastructurePowerServicePayloadV1.PartitionId,
            static payload => payload.ToStandardPayload(),
            InfrastructurePowerServicePayloadV1.FromStandardPayload,
            static payload => payload.CanonicalDigest(),
            references));
        verifiedPartitions++;

        verifiedRecords = checked(verifiedRecords + VerifyPartition(
            materialization.CommunicationServices,
            InfrastructureCommunicationServicePayloadV1.PartitionId,
            static payload => payload.ToStandardPayload(),
            InfrastructureCommunicationServicePayloadV1.FromStandardPayload,
            static payload => payload.CanonicalDigest(),
            references));
        verifiedPartitions++;

        verifiedRecords = checked(verifiedRecords + VerifyPartition(
            materialization.ServiceQueue,
            InfrastructureServiceQueuePayloadV1.PartitionId,
            static payload => payload.ToStandardPayload(),
            InfrastructureServiceQueuePayloadV1.FromStandardPayload,
            static payload => payload.CanonicalDigest(),
            references));
        verifiedPartitions++;

        if (verifiedPartitions != CanonicalPartitionCount || verifiedRecords != CanonicalRecordCount)
            throw new InvalidDataException("qa04.infrastructure.service-queue-snapshot-recovery-total-mismatch");
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
            throw new InvalidDataException($"qa04.infrastructure.service-queue-snapshot-partition-identity:{partitionId}");

        var header = PartitionStateHeaderV1.CreateCanonical(
            partition,
            revision: 1,
            basisStep: 0,
            detailLevel: DetailLevelV1.D2RegionalAggregate,
            digest);
        if (header.ItemCount != partition.ItemCount)
            throw new InvalidDataException($"qa04.infrastructure.service-queue-snapshot-header-count:{partitionId}");

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
            throw new InvalidDataException($"qa04.infrastructure.service-queue-snapshot-section-authority:{partitionId}");

        var verifier = provider.CreateSemanticVerifier(header, references);
        var result = verifier.VerifyWithContext is not null
            ? verifier.VerifyWithContext(
                section.Fragments,
                new SnapshotSectionSemanticVerificationContextV1(references))
            : verifier.Verify(section.Fragments);
        if (result.LogicalItemCount != header.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(result.LogicalContentDigest, header.CanonicalDigest))
            throw new InvalidDataException($"qa04.infrastructure.service-queue-snapshot-semantic-rehash:{partitionId}");

        return header.ItemCount;
    }
}
