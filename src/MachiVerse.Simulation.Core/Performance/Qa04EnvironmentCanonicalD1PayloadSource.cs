using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Environment;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public interface IQa04EnvironmentD1PayloadSourceV1
{
    EnvironmentGeologyPayloadV1 Geology(Qa04EnvironmentD1BindingV1 binding);
    EnvironmentSoilPayloadV1 Soil(Qa04EnvironmentD1BindingV1 binding);
    EnvironmentResourceDepositPayloadV1 ResourceDeposit(Qa04EnvironmentD1BindingV1 binding);
    EnvironmentGroundwaterPayloadV1 Groundwater(Qa04EnvironmentD1BindingV1 binding);
    EnvironmentAtmospherePayloadV1 Atmosphere(Qa04EnvironmentD1BindingV1 binding);
    EnvironmentClimatePayloadV1 Climate(Qa04EnvironmentD1BindingV1 binding);
    EnvironmentWeatherPayloadV1 Weather(Qa04EnvironmentD1BindingV1 binding);
    EnvironmentSurfaceWaterPayloadV1 SurfaceWater(Qa04EnvironmentD1BindingV1 binding);
    EnvironmentOceanPayloadV1 Ocean(Qa04EnvironmentD1BindingV1 binding);
    EnvironmentEcosystemPayloadV1 Ecosystem(Qa04EnvironmentD1BindingV1 binding);
    EnvironmentContaminantPayloadV1 Contaminant(Qa04EnvironmentD1BindingV1 binding);
    EnvironmentHazardPayloadV1 Hazard(Qa04EnvironmentD1BindingV1 binding);
    EnvironmentLineagePayloadV1 Lineage(Qa04EnvironmentD1BindingV1 binding);
}

public sealed class Qa04EnvironmentCanonicalD1PayloadSourceV1 : IQa04EnvironmentD1PayloadSourceV1
{
    public static readonly StableToken AggregateMaterializationKind = new("perf.aggregate-d1");
    private readonly Qa04EnvironmentCanonicalD0PayloadSourceV1 _d0 = new();

    public EnvironmentGeologyPayloadV1 Geology(Qa04EnvironmentD1BindingV1 binding)
    {
        Require(binding, EnvironmentGeologyPayloadV1.PartitionId);
        var s = Sources(binding, _d0.Geology);
        return new EnvironmentGeologyPayloadV1(Scope(binding), RequireInvariantTokenList(s.Select(static x => x.MaterialClasses).ToArray(), "geology.material_classes"), Union(s.Select(static x => x.StrataRefs).ToArray()), Mean(s.Select(static x => x.PorosityPpm).ToArray()), Mean(s.Select(static x => x.StabilityPpm).ToArray()), Mean(s.Select(static x => x.PermeabilityQ32).ToArray()), Union(s.Select(static x => x.FaultRefs).ToArray()), Union(s.Select(static x => x.ResourceRefs).ToArray()));
    }

    public EnvironmentSoilPayloadV1 Soil(Qa04EnvironmentD1BindingV1 binding)
    {
        Require(binding, EnvironmentSoilPayloadV1.PartitionId);
        var s = Sources(binding, _d0.Soil);
        return new EnvironmentSoilPayloadV1(Scope(binding), Mode(s.Select(static x => x.SoilClass).ToArray()), Mean(s.Select(static x => x.DepthMm).ToArray()), Mean(s.Select(static x => x.MoisturePpm).ToArray()), Mean(s.Select(static x => x.FertilityPpm).ToArray()), Sum(s.Select(static x => x.OrganicMassGram).ToArray()), Union(s.Select(static x => x.ContaminantRefs).ToArray()));
    }

    public EnvironmentResourceDepositPayloadV1 ResourceDeposit(Qa04EnvironmentD1BindingV1 binding)
    {
        Require(binding, EnvironmentResourceDepositPayloadV1.PartitionId);
        var s = Sources(binding, _d0.ResourceDeposit);
        return new EnvironmentResourceDepositPayloadV1(Scope(binding), Mode(s.Select(static x => x.ResourceKind).ToArray()), Sum(s.Select(static x => x.RemainingMassGram).ToArray()), Mean(s.Select(static x => x.GradePpm).ToArray()), Sum(s.Select(static x => x.RenewalRateGramPerStep).ToArray()), Mean(s.Select(static x => x.AccessibilityPpm).ToArray()));
    }

    public EnvironmentGroundwaterPayloadV1 Groundwater(Qa04EnvironmentD1BindingV1 binding)
    {
        Require(binding, EnvironmentGroundwaterPayloadV1.PartitionId);
        var s = Sources(binding, _d0.Groundwater);
        return new EnvironmentGroundwaterPayloadV1(Scope(binding), Sum(s.Select(static x => x.WaterVolumeMl).ToArray()), Mean(s.Select(static x => x.HydraulicHeadMm).ToArray()), Mean(s.Select(static x => x.QualityPpm).ToArray()), Mean(s.Select(static x => x.TemperatureMilliKelvin).ToArray()), Union(s.Select(static x => x.NeighborRefs).ToArray()));
    }

    public EnvironmentAtmospherePayloadV1 Atmosphere(Qa04EnvironmentD1BindingV1 binding)
    {
        Require(binding, EnvironmentAtmospherePayloadV1.PartitionId);
        var s = Sources(binding, _d0.Atmosphere);
        return new EnvironmentAtmospherePayloadV1(Scope(binding), Mean(s.Select(static x => x.PressurePa).ToArray()), Mean(s.Select(static x => x.TemperatureMilliKelvin).ToArray()), Mean(s.Select(static x => x.HumidityPpm).ToArray()), Mean(s.Select(static x => x.WindUmPerSecond).ToArray()), Sum(s.Select(static x => x.VaporMassGram).ToArray()), Sum(s.Select(static x => x.LiquidMassGram).ToArray()), AggregateGas(s));
    }

    public EnvironmentClimatePayloadV1 Climate(Qa04EnvironmentD1BindingV1 binding)
    {
        Require(binding, EnvironmentClimatePayloadV1.PartitionId);
        var s = Sources(binding, _d0.Climate);
        return new EnvironmentClimatePayloadV1(Scope(binding), Mode(s.Select(static x => x.Regime).ToArray()), Mean(s.Select(static x => x.TemperatureMeanMilliKelvin).ToArray()), Mean(s.Select(static x => x.PrecipitationMeanMl).ToArray()), Mean(s.Select(static x => x.WindMeanUmPerSecond).ToArray()), Qa04EnvironmentD1AggregationV1.CheckedSum(s.Select(static x => x.SampleCount).ToArray()), Qa04EnvironmentD1AggregationV1.MaxPlusOne(s.Select(static x => x.AggregateGeneration).ToArray()));
    }

    public EnvironmentWeatherPayloadV1 Weather(Qa04EnvironmentD1BindingV1 binding)
    {
        Require(binding, EnvironmentWeatherPayloadV1.PartitionId);
        var s = Sources(binding, _d0.Weather);
        return new EnvironmentWeatherPayloadV1(Scope(binding), Mode(s.Select(static x => x.WeatherClass).ToArray()), Sum(s.Select(static x => x.PrecipitationMlPerStep).ToArray()), Mean(s.Select(static x => x.CloudPpm).ToArray()), Mean(s.Select(static x => x.VisibilityMm).ToArray()), Mean(s.Select(static x => x.StormIntensityPpm).ToArray()), Qa04EnvironmentD1AggregationV1.MaxPlusOne(s.Select(static x => x.BasisAtmosphereRevision).ToArray()));
    }

    public EnvironmentSurfaceWaterPayloadV1 SurfaceWater(Qa04EnvironmentD1BindingV1 binding)
    {
        Require(binding, EnvironmentSurfaceWaterPayloadV1.PartitionId);
        var s = Sources(binding, _d0.SurfaceWater);
        return new EnvironmentSurfaceWaterPayloadV1(Scope(binding), Mode(s.Select(static x => x.WaterBodyClass).ToArray()), Sum(s.Select(static x => x.VolumeMl).ToArray()), Mean(s.Select(static x => x.SurfaceLevelMm).ToArray()), Mean(s.Select(static x => x.FlowUmPerSecond).ToArray()), Mean(s.Select(static x => x.TemperatureMilliKelvin).ToArray()), Mean(s.Select(static x => x.QualityPpm).ToArray()), Union(s.Select(static x => x.DownstreamRefs).ToArray()));
    }

    public EnvironmentOceanPayloadV1 Ocean(Qa04EnvironmentD1BindingV1 binding)
    {
        Require(binding, EnvironmentOceanPayloadV1.PartitionId);
        var s = Sources(binding, _d0.Ocean);
        return new EnvironmentOceanPayloadV1(Scope(binding), Sum(s.Select(static x => x.WaterVolumeMl).ToArray()), Mean(s.Select(static x => x.SurfaceLevelMm).ToArray()), Mean(s.Select(static x => x.VelocityUmPerSecond).ToArray()), Mean(s.Select(static x => x.TemperatureMilliKelvin).ToArray()), Mean(s.Select(static x => x.SalinityPpm).ToArray()), Union(s.Select(static x => x.NeighborRefs).ToArray()));
    }

    public EnvironmentEcosystemPayloadV1 Ecosystem(Qa04EnvironmentD1BindingV1 binding)
    {
        Require(binding, EnvironmentEcosystemPayloadV1.PartitionId);
        var s = Sources(binding, _d0.Ecosystem);
        return new EnvironmentEcosystemPayloadV1(Scope(binding), Mode(s.Select(static x => x.SpeciesOrCohort).ToArray()), Qa04EnvironmentD1AggregationV1.CheckedSum(s.Select(static x => x.Population).ToArray()), Sum(s.Select(static x => x.BiomassGram).ToArray()), Mean(s.Select(static x => x.BirthRatePpm).ToArray()), Mean(s.Select(static x => x.DeathRatePpm).ToArray()), Mean(s.Select(static x => x.MigrationRatePpm).ToArray()), Union(s.Select(static x => x.ResourceRefs).ToArray()));
    }

    public EnvironmentContaminantPayloadV1 Contaminant(Qa04EnvironmentD1BindingV1 binding)
    {
        Require(binding, EnvironmentContaminantPayloadV1.PartitionId);
        var s = Sources(binding, _d0.Contaminant);
        return new EnvironmentContaminantPayloadV1(Scope(binding), Mode(s.Select(static x => x.ContaminantKind).ToArray()), Sum(s.Select(static x => x.StockMassGram).ToArray()), Mean(s.Select(static x => x.ConcentrationPpb).ToArray()), Union(s.Select(static x => x.SourceRefs).ToArray()), Union(s.Select(static x => x.SinkRefs).ToArray()));
    }

    public EnvironmentHazardPayloadV1 Hazard(Qa04EnvironmentD1BindingV1 binding)
    {
        Require(binding, EnvironmentHazardPayloadV1.PartitionId);
        var s = Sources(binding, _d0.Hazard);
        return new EnvironmentHazardPayloadV1(Scope(binding), Mode(s.Select(static x => x.HazardKind).ToArray()), Mean(s.Select(static x => x.IntensityPpm).ToArray()), Qa04EnvironmentD1AggregationV1.MaxStep(s.Select(static x => x.StartedStep).ToArray()), Qa04EnvironmentD1AggregationV1.OptionalEndMinimum(s.Select(static x => x.ExpectedEndStep).ToArray()), Union(s.Select(static x => x.DriverRefs).ToArray()), Union(s.Select(static x => x.AffectedScopeRefs).ToArray()));
    }

    public EnvironmentLineagePayloadV1 Lineage(Qa04EnvironmentD1BindingV1 binding)
    {
        Require(binding, EnvironmentLineagePayloadV1.PartitionId);
        var sources = Sources(binding, _d0.Lineage);
        var subject = Qa04EnvironmentLineageAuthorityV1.ResolveD1Subject(binding);
        var parents = Qa04EnvironmentLineageAuthorityV1.ResolveD1Parents(binding);
        var generation = Qa04EnvironmentD1AggregationV1.MaxPlusOne(sources.Select(static x => x.Generation).ToArray());
        return new EnvironmentLineagePayloadV1(subject, parents, generation, AggregateMaterializationKind, SourceDigest(binding, sources));
    }

    private static T[] Sources<T>(Qa04EnvironmentD1BindingV1 binding, Func<Qa04EnvironmentD0BindingV1, T> factory)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(factory);
        if (binding.SourceD0GlobalOrdinals.Count != Qa04EnvironmentD1AggregationV1.SourceCount)
            throw new InvalidDataException("qa04.environment.d1-canonical-source-cardinality");
        return binding.SourceD0GlobalOrdinals.Select(Qa04EnvironmentReferenceDecompositionV1.BindD0).Select(source =>
        {
            if (source.PartitionId != binding.PartitionId)
                throw new InvalidDataException("qa04.environment.d1-canonical-source-partition");
            return factory(source);
        }).ToArray();
    }

    private static PartitionRecordRefV1 Scope(Qa04EnvironmentD1BindingV1 binding) => Qa04EnvironmentD1PartitionMaterializerV1.ResolveSpatialScope(binding);
    private static long Sum(IReadOnlyList<long> values) => Qa04EnvironmentD1AggregationV1.CheckedSum(values);
    private static long Mean(IReadOnlyList<long> values) => Qa04EnvironmentD1AggregationV1.MeanRoundToEven(values);
    private static int Mean(IReadOnlyList<int> values) => Qa04EnvironmentD1AggregationV1.MeanRoundToEven(values);
    private static uint Mean(IReadOnlyList<uint> values) => Qa04EnvironmentD1AggregationV1.MeanRoundToEven(values);
    private static Vec3Int64V1 Mean(IReadOnlyList<Vec3Int64V1> values) => Qa04EnvironmentD1AggregationV1.MeanRoundToEven(values);
    private static StableToken Mode(IReadOnlyList<StableToken> values) => Qa04EnvironmentD1AggregationV1.Mode(values);
    private static IReadOnlyList<PartitionRecordRefV1> Union(IReadOnlyList<IReadOnlyList<PartitionRecordRefV1>> values) => Qa04EnvironmentD1AggregationV1.CanonicalSetUnion(values);

    private static IReadOnlyList<StableToken> RequireInvariantTokenList(IReadOnlyList<IReadOnlyList<StableToken>> values, string field)
    {
        if (values.Count != Qa04EnvironmentD1AggregationV1.SourceCount)
            throw new InvalidDataException($"qa04.environment.d1-token-list-cardinality:{field}");
        var first = values[0].Select(static x => x.Value).ToArray();
        if (values.Skip(1).Any(value => !value.Select(static x => x.Value).SequenceEqual(first, StringComparer.Ordinal)))
            throw new InvalidDataException($"qa04.environment.d1-token-list-divergent:{field}");
        return Array.AsReadOnly(first.Select(static value => new StableToken(value)).ToArray());
    }

    private static IReadOnlyList<KeyValuePair<string, uint>> AggregateGas(IReadOnlyList<EnvironmentAtmospherePayloadV1> sources)
    {
        if (sources.Count != Qa04EnvironmentD1AggregationV1.SourceCount)
            throw new InvalidDataException("qa04.environment.d1-gas-source-cardinality");
        var keys = sources[0].GasPpb.Select(static pair => pair.Key).ToArray();
        if (sources.Skip(1).Any(source => !source.GasPpb.Select(static pair => pair.Key).SequenceEqual(keys, StringComparer.Ordinal)))
            throw new InvalidDataException("qa04.environment.d1-gas-key-drift");
        var result = new KeyValuePair<string, uint>[keys.Length];
        for (var index = 0; index < keys.Length; index++)
            result[index] = new KeyValuePair<string, uint>(keys[index], Mean(sources.Select(source => source.GasPpb[index].Value).ToArray()));
        return Array.AsReadOnly(result);
    }

    private static byte[] SourceDigest(Qa04EnvironmentD1BindingV1 binding, IReadOnlyList<EnvironmentLineagePayloadV1> sources)
    {
        var ordered = binding.SourceD0GlobalOrdinals.Select(Qa04EnvironmentReferenceDecompositionV1.BindD0).Zip(sources, static (source, payload) => new { source.Descriptor.RecordId, Payload = payload }).OrderBy(static value => value.RecordId).ToArray();
        return HashSuite.DomainHash("mv.perf-reference-environment-d1-source.v1", writer =>
        {
            writer.WriteArrayStart(checked((ulong)ordered.Length));
            foreach (var source in ordered)
                writer.WriteBytes(source.Payload.CanonicalDigest());
        });
    }

    private static void Require(Qa04EnvironmentD1BindingV1 binding, string partitionId)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (binding.PartitionId.Value != partitionId || binding.Descriptor.DetailLevel != DetailLevelV1.D1LocalAggregate)
            throw new InvalidDataException($"qa04.environment.canonical-d1-binding-invalid:{partitionId}");
    }
}
