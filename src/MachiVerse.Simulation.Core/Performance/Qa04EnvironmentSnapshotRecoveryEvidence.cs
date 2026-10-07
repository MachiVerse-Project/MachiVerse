using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Domains.Environment;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Runs the production generic Domain-partition Snapshot codec over all thirteen Environment
/// partitions and proves recovery by semantic rehash against the frozen canonical headers.
/// This consumes already materialized typed state and does not regenerate benchmark records.
/// </summary>
public static class Qa04EnvironmentSnapshotRecoveryEvidenceV1
{
    public const int EnvironmentPartitionCount = 13;

    public static int Verify(
        EnvironmentDomainStateV1 state,
        IReadOnlyList<PartitionStateHeaderV1> headers,
        IDomainRecordSchemaResolverV1 references)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(references);
        if (headers.Count != EnvironmentPartitionCount)
            throw new InvalidDataException("qa04.environment.snapshot-recovery-header-count");

        var byId = headers.ToDictionary(static header => header.PartitionId.Value, StringComparer.Ordinal);
        if (byId.Count != EnvironmentPartitionCount)
            throw new InvalidDataException("qa04.environment.snapshot-recovery-header-duplicate");

        var verified = 0;
        VerifyPartition(state.Geology, EnvironmentGeologyPayloadV1.PartitionId,
            static p => p.ToStandardPayload(), EnvironmentGeologyPayloadV1.FromStandardPayload,
            static p => p.CanonicalDigest(), Header(byId, EnvironmentGeologyPayloadV1.PartitionId), references); verified++;
        VerifyPartition(state.Soil, EnvironmentSoilPayloadV1.PartitionId,
            static p => p.ToStandardPayload(), EnvironmentSoilPayloadV1.FromStandardPayload,
            static p => p.CanonicalDigest(), Header(byId, EnvironmentSoilPayloadV1.PartitionId), references); verified++;
        VerifyPartition(state.ResourceDeposit, EnvironmentResourceDepositPayloadV1.PartitionId,
            static p => p.ToStandardPayload(), EnvironmentResourceDepositPayloadV1.FromStandardPayload,
            static p => p.CanonicalDigest(), Header(byId, EnvironmentResourceDepositPayloadV1.PartitionId), references); verified++;
        VerifyPartition(state.Groundwater, EnvironmentGroundwaterPayloadV1.PartitionId,
            static p => p.ToStandardPayload(), EnvironmentGroundwaterPayloadV1.FromStandardPayload,
            static p => p.CanonicalDigest(), Header(byId, EnvironmentGroundwaterPayloadV1.PartitionId), references); verified++;
        VerifyPartition(state.Atmosphere, EnvironmentAtmospherePayloadV1.PartitionId,
            static p => p.ToStandardPayload(), EnvironmentAtmospherePayloadV1.FromStandardPayload,
            static p => p.CanonicalDigest(), Header(byId, EnvironmentAtmospherePayloadV1.PartitionId), references); verified++;
        VerifyPartition(state.Climate, EnvironmentClimatePayloadV1.PartitionId,
            static p => p.ToStandardPayload(), EnvironmentClimatePayloadV1.FromStandardPayload,
            static p => p.CanonicalDigest(), Header(byId, EnvironmentClimatePayloadV1.PartitionId), references); verified++;
        VerifyPartition(state.Weather, EnvironmentWeatherPayloadV1.PartitionId,
            static p => p.ToStandardPayload(), EnvironmentWeatherPayloadV1.FromStandardPayload,
            static p => p.CanonicalDigest(), Header(byId, EnvironmentWeatherPayloadV1.PartitionId), references); verified++;
        VerifyPartition(state.SurfaceWater, EnvironmentSurfaceWaterPayloadV1.PartitionId,
            static p => p.ToStandardPayload(), EnvironmentSurfaceWaterPayloadV1.FromStandardPayload,
            static p => p.CanonicalDigest(), Header(byId, EnvironmentSurfaceWaterPayloadV1.PartitionId), references); verified++;
        VerifyPartition(state.Ocean, EnvironmentOceanPayloadV1.PartitionId,
            static p => p.ToStandardPayload(), EnvironmentOceanPayloadV1.FromStandardPayload,
            static p => p.CanonicalDigest(), Header(byId, EnvironmentOceanPayloadV1.PartitionId), references); verified++;
        VerifyPartition(state.Ecosystem, EnvironmentEcosystemPayloadV1.PartitionId,
            static p => p.ToStandardPayload(), EnvironmentEcosystemPayloadV1.FromStandardPayload,
            static p => p.CanonicalDigest(), Header(byId, EnvironmentEcosystemPayloadV1.PartitionId), references); verified++;
        VerifyPartition(state.Contaminant, EnvironmentContaminantPayloadV1.PartitionId,
            static p => p.ToStandardPayload(), EnvironmentContaminantPayloadV1.FromStandardPayload,
            static p => p.CanonicalDigest(), Header(byId, EnvironmentContaminantPayloadV1.PartitionId), references); verified++;
        VerifyPartition(state.Hazard, EnvironmentHazardPayloadV1.PartitionId,
            static p => p.ToStandardPayload(), EnvironmentHazardPayloadV1.FromStandardPayload,
            static p => p.CanonicalDigest(), Header(byId, EnvironmentHazardPayloadV1.PartitionId), references); verified++;
        VerifyPartition(state.Lineage, EnvironmentLineagePayloadV1.PartitionId,
            static p => p.ToStandardPayload(), EnvironmentLineagePayloadV1.FromStandardPayload,
            static p => p.CanonicalDigest(), Header(byId, EnvironmentLineagePayloadV1.PartitionId), references); verified++;

        if (verified != EnvironmentPartitionCount)
            throw new InvalidDataException("qa04.environment.snapshot-recovery-partition-count");
        return verified;
    }

    private static void VerifyPartition<TPayload>(
        DomainPartitionStateV1<TPayload> partition,
        string partitionId,
        Func<TPayload, IReadOnlyDictionary<string, object?>> toStandard,
        Func<IReadOnlyDictionary<string, object?>, TPayload> fromStandard,
        Func<TPayload, byte[]> digest,
        PartitionStateHeaderV1 header,
        IDomainRecordSchemaResolverV1 references)
    {
        var authority = new DomainPartitionSnapshotAuthorityV1<TPayload>(partition, header, digest);
        var provider = new DomainPartitionSnapshotSectionProviderV1<TPayload>(
            partitionId, toStandard, fromStandard, digest);
        var section = provider.Create(authority, references);
        if (section.LogicalItemCount != header.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(section.LogicalContentDigest, header.CanonicalDigest))
            throw new InvalidDataException($"qa04.environment.snapshot-recovery-section-authority:{partitionId}");

        var verifier = provider.CreateSemanticVerifier(header, references);
        var result = verifier.VerifyWithContext is not null
            ? verifier.VerifyWithContext(
                section.Fragments,
                new SnapshotSectionSemanticVerificationContextV1(references))
            : verifier.Verify(section.Fragments);
        if (result.LogicalItemCount != header.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(result.LogicalContentDigest, header.CanonicalDigest))
            throw new InvalidDataException($"qa04.environment.snapshot-recovery-semantic-rehash:{partitionId}");
    }

    private static PartitionStateHeaderV1 Header(
        IReadOnlyDictionary<string, PartitionStateHeaderV1> headers,
        string partitionId)
        => headers.TryGetValue(partitionId, out var header)
            ? header
            : throw new InvalidDataException($"qa04.environment.snapshot-recovery-header-missing:{partitionId}");
}
