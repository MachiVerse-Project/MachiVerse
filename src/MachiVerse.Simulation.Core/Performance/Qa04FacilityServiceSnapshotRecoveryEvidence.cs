using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04FacilityServiceSnapshotRecoveryEvidenceResultV1(
    ulong BuiltStructureCount,
    ulong FacilityServiceCount);

/// <summary>
/// Production Snapshot/recovery semantic proof for the #240 FacilityService package. Both the
/// upstream BuiltStructure identity layer and the Infrastructure FacilityService layer are encoded
/// with their ordinary schema-owner providers and recovered against the same actual reference set.
/// </summary>
public static class Qa04FacilityServiceSnapshotRecoveryEvidenceV1
{
    public static Qa04FacilityServiceSnapshotRecoveryEvidenceResultV1 Verify(
        Qa04FacilityServiceCanonicalMaterializationV1 materialization)
    {
        ArgumentNullException.ThrowIfNull(materialization);
        if (materialization.BuiltStructures.ItemCount != Qa04FacilityServiceCanonicalAuthorityV1.CanonicalCount ||
            materialization.FacilityServices.ItemCount != Qa04FacilityServiceCanonicalAuthorityV1.CanonicalCount)
            throw new InvalidDataException("qa04.infrastructure.facility-service-snapshot-record-count");

        var built = VerifyPartition(
            materialization.BuiltStructures,
            DetailLevelV1.D0Entity,
            static payload => payload.ToStandardPayload(),
            BuiltStructurePayloadV1.FromStandardPayload,
            static payload => payload.CanonicalDigest(),
            materialization.References);
        var facility = VerifyPartition(
            materialization.FacilityServices,
            DetailLevelV1.D2RegionalAggregate,
            static payload => payload.ToStandardPayload(),
            InfrastructureFacilityServicePayloadV1.FromStandardPayload,
            static payload => payload.CanonicalDigest(),
            materialization.References);

        if (built != Qa04FacilityServiceCanonicalAuthorityV1.CanonicalCount ||
            facility != Qa04FacilityServiceCanonicalAuthorityV1.CanonicalCount)
            throw new InvalidDataException("qa04.infrastructure.facility-service-snapshot-semantic-count");

        return new Qa04FacilityServiceSnapshotRecoveryEvidenceResultV1(built, facility);
    }

    private static ulong VerifyPartition<TPayload>(
        DomainPartitionStateV1<TPayload> partition,
        DetailLevelV1 detailLevel,
        Func<TPayload, IReadOnlyDictionary<string, object?>> toStandardPayload,
        Func<IReadOnlyDictionary<string, object?>, TPayload> fromStandardPayload,
        Func<TPayload, byte[]> digest,
        IDomainRecordSchemaResolverV1 references)
    {
        var header = PartitionStateHeaderV1.CreateCanonical(
            partition,
            revision: 1,
            basisStep: 0,
            detailLevel,
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
            throw new InvalidDataException($"qa04.infrastructure.facility-service-snapshot-section:{partition.Identity.PartitionId.Value}");

        var verifier = provider.CreateSemanticVerifier(header, references);
        var recovered = verifier.VerifyWithContext is not null
            ? verifier.VerifyWithContext(
                section.Fragments,
                new SnapshotSectionSemanticVerificationContextV1(references))
            : verifier.Verify(section.Fragments);
        if (recovered.LogicalItemCount != header.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(recovered.LogicalContentDigest, header.CanonicalDigest))
            throw new InvalidDataException($"qa04.infrastructure.facility-service-snapshot-semantic-rehash:{partition.Identity.PartitionId.Value}");

        return recovered.LogicalItemCount;
    }
}
