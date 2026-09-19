using MachiVerse.Simulation.Core.Domains.Environment;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed class Qa04EnvironmentD1DomainMaterializationV1
{
    internal Qa04EnvironmentD1DomainMaterializationV1(EnvironmentDomainStateV1 state)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        CountsByPartition = StateCounts(state);
        MaterializedRecordCount = CountsByPartition.Aggregate(
            0UL,
            static (total, pair) => checked(total + pair.Value));
    }

    public EnvironmentDomainStateV1 State { get; }
    public IReadOnlyDictionary<string, ulong> CountsByPartition { get; }
    public ulong MaterializedRecordCount { get; }
    public bool FullCanonicalD1Materialized
        => MaterializedRecordCount == Qa04EnvironmentReferenceDecompositionV1.CanonicalD1Count &&
           Qa04EnvironmentReferenceDecompositionV1.Partitions.All(slice =>
               CountsByPartition.TryGetValue(slice.PartitionId.Value, out var count) && count == slice.D1Count);

    private static IReadOnlyDictionary<string, ulong> StateCounts(EnvironmentDomainStateV1 state)
        => new Dictionary<string, ulong>(StringComparer.Ordinal)
        {
            [EnvironmentGeologyPayloadV1.PartitionId] = state.Geology.ItemCount,
            [EnvironmentSoilPayloadV1.PartitionId] = state.Soil.ItemCount,
            [EnvironmentResourceDepositPayloadV1.PartitionId] = state.ResourceDeposit.ItemCount,
            [EnvironmentGroundwaterPayloadV1.PartitionId] = state.Groundwater.ItemCount,
            [EnvironmentAtmospherePayloadV1.PartitionId] = state.Atmosphere.ItemCount,
            [EnvironmentClimatePayloadV1.PartitionId] = state.Climate.ItemCount,
            [EnvironmentWeatherPayloadV1.PartitionId] = state.Weather.ItemCount,
            [EnvironmentSurfaceWaterPayloadV1.PartitionId] = state.SurfaceWater.ItemCount,
            [EnvironmentOceanPayloadV1.PartitionId] = state.Ocean.ItemCount,
            [EnvironmentEcosystemPayloadV1.PartitionId] = state.Ecosystem.ItemCount,
            [EnvironmentContaminantPayloadV1.PartitionId] = state.Contaminant.ItemCount,
            [EnvironmentHazardPayloadV1.PartitionId] = state.Hazard.ItemCount,
            [EnvironmentLineagePayloadV1.PartitionId] = state.Lineage.ItemCount,
        };
}

/// <summary>
/// Composes all thirteen canonical D1 Environment partitions into the production typed owner state.
/// Every payload is produced from the exact four canonical D0 sources selected by the decomposition.
/// </summary>
public static class Qa04EnvironmentD1DomainMaterializerV1
{
    public static Qa04EnvironmentD1DomainMaterializationV1 MaterializeCanonical(
        IQa04EnvironmentD1PayloadSourceV1 payloadSource,
        IDomainRecordSchemaResolverV1 referenceResolver)
        => Materialize(payloadSource, referenceResolver, static slice => slice.D1Count);

    public static Qa04EnvironmentD1DomainMaterializationV1 MaterializeReduced(
        IQa04EnvironmentD1PayloadSourceV1 payloadSource,
        IDomainRecordSchemaResolverV1 referenceResolver,
        ulong recordsPerPartition)
    {
        if (recordsPerPartition == 0)
            throw new ArgumentOutOfRangeException(nameof(recordsPerPartition));
        return Materialize(
            payloadSource,
            referenceResolver,
            slice => Math.Min(recordsPerPartition, slice.D1Count));
    }

    private static Qa04EnvironmentD1DomainMaterializationV1 Materialize(
        IQa04EnvironmentD1PayloadSourceV1 payloadSource,
        IDomainRecordSchemaResolverV1 referenceResolver,
        Func<Qa04EnvironmentPartitionDecompositionV1, ulong> countForSlice)
    {
        ArgumentNullException.ThrowIfNull(payloadSource);
        ArgumentNullException.ThrowIfNull(referenceResolver);
        ArgumentNullException.ThrowIfNull(countForSlice);
        Qa04EnvironmentD1PartitionMaterializerV1.ValidateCanonicalContract();

        ulong Count(string partitionId) => countForSlice(Qa04EnvironmentReferenceDecompositionV1.Get(partitionId));

        var state = new EnvironmentDomainStateV1(
            Qa04EnvironmentD1PartitionMaterializerV1.MaterializePartition(
                EnvironmentGeologyPayloadV1.PartitionId, Count(EnvironmentGeologyPayloadV1.PartitionId),
                payloadSource.Geology, static value => value.ToStandardPayload(), referenceResolver),
            Qa04EnvironmentD1PartitionMaterializerV1.MaterializePartition(
                EnvironmentSoilPayloadV1.PartitionId, Count(EnvironmentSoilPayloadV1.PartitionId),
                payloadSource.Soil, static value => value.ToStandardPayload(), referenceResolver),
            Qa04EnvironmentD1PartitionMaterializerV1.MaterializePartition(
                EnvironmentResourceDepositPayloadV1.PartitionId, Count(EnvironmentResourceDepositPayloadV1.PartitionId),
                payloadSource.ResourceDeposit, static value => value.ToStandardPayload(), referenceResolver),
            Qa04EnvironmentD1PartitionMaterializerV1.MaterializePartition(
                EnvironmentGroundwaterPayloadV1.PartitionId, Count(EnvironmentGroundwaterPayloadV1.PartitionId),
                payloadSource.Groundwater, static value => value.ToStandardPayload(), referenceResolver),
            Qa04EnvironmentD1PartitionMaterializerV1.MaterializePartition(
                EnvironmentAtmospherePayloadV1.PartitionId, Count(EnvironmentAtmospherePayloadV1.PartitionId),
                payloadSource.Atmosphere, static value => value.ToStandardPayload(), referenceResolver),
            Qa04EnvironmentD1PartitionMaterializerV1.MaterializePartition(
                EnvironmentClimatePayloadV1.PartitionId, Count(EnvironmentClimatePayloadV1.PartitionId),
                payloadSource.Climate, static value => value.ToStandardPayload(), referenceResolver),
            Qa04EnvironmentD1PartitionMaterializerV1.MaterializePartition(
                EnvironmentWeatherPayloadV1.PartitionId, Count(EnvironmentWeatherPayloadV1.PartitionId),
                payloadSource.Weather, static value => value.ToStandardPayload(), referenceResolver),
            Qa04EnvironmentD1PartitionMaterializerV1.MaterializePartition(
                EnvironmentSurfaceWaterPayloadV1.PartitionId, Count(EnvironmentSurfaceWaterPayloadV1.PartitionId),
                payloadSource.SurfaceWater, static value => value.ToStandardPayload(), referenceResolver),
            Qa04EnvironmentD1PartitionMaterializerV1.MaterializePartition(
                EnvironmentOceanPayloadV1.PartitionId, Count(EnvironmentOceanPayloadV1.PartitionId),
                payloadSource.Ocean, static value => value.ToStandardPayload(), referenceResolver),
            Qa04EnvironmentD1PartitionMaterializerV1.MaterializePartition(
                EnvironmentEcosystemPayloadV1.PartitionId, Count(EnvironmentEcosystemPayloadV1.PartitionId),
                payloadSource.Ecosystem, static value => value.ToStandardPayload(), referenceResolver),
            Qa04EnvironmentD1PartitionMaterializerV1.MaterializePartition(
                EnvironmentContaminantPayloadV1.PartitionId, Count(EnvironmentContaminantPayloadV1.PartitionId),
                payloadSource.Contaminant, static value => value.ToStandardPayload(), referenceResolver),
            Qa04EnvironmentD1PartitionMaterializerV1.MaterializePartition(
                EnvironmentHazardPayloadV1.PartitionId, Count(EnvironmentHazardPayloadV1.PartitionId),
                payloadSource.Hazard, static value => value.ToStandardPayload(), referenceResolver),
            Qa04EnvironmentD1PartitionMaterializerV1.MaterializePartition(
                EnvironmentLineagePayloadV1.PartitionId, Count(EnvironmentLineagePayloadV1.PartitionId),
                payloadSource.Lineage, static value => value.ToStandardPayload(), referenceResolver));

        return new Qa04EnvironmentD1DomainMaterializationV1(state);
    }
}
