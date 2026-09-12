using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains.Environment;

public sealed record EnvironmentGeologyPayloadV1(
    PartitionRecordRefV1 SpatialScope,
    IReadOnlyList<StableToken> MaterialClasses,
    IReadOnlyList<PartitionRecordRefV1> StrataRefs,
    uint PorosityPpm,
    uint StabilityPpm,
    long PermeabilityQ32,
    IReadOnlyList<PartitionRecordRefV1> FaultRefs,
    IReadOnlyList<PartitionRecordRefV1> ResourceRefs)
{
    public const string PartitionId = "environment.geology";
    public IReadOnlyDictionary<string, object?> ToStandardPayload() => EnvironmentPayloadFields.Map(
        ("spatial_scope", SpatialScope),
        ("material_classes", EnvironmentPayloadFields.TokenValues(MaterialClasses)),
        ("strata_refs", StrataRefs),
        ("porosity_ppm", PorosityPpm),
        ("stability_ppm", StabilityPpm),
        ("permeability_q32", PermeabilityQ32),
        ("fault_refs", FaultRefs),
        ("resource_refs", ResourceRefs));
    public byte[] CanonicalDigest() => EnvironmentPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static EnvironmentGeologyPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        EnvironmentPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "spatial_scope"),
        EnvironmentPayloadFields.Tokens(values, PartitionId, "material_classes"),
        EnvironmentPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "strata_refs"),
        EnvironmentPayloadFields.Required<uint>(values, PartitionId, "porosity_ppm"),
        EnvironmentPayloadFields.Required<uint>(values, PartitionId, "stability_ppm"),
        EnvironmentPayloadFields.Required<long>(values, PartitionId, "permeability_q32"),
        EnvironmentPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "fault_refs"),
        EnvironmentPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "resource_refs"));
}

public sealed record EnvironmentSoilPayloadV1(
    PartitionRecordRefV1 SpatialScope,
    StableToken SoilClass,
    long DepthMm,
    uint MoisturePpm,
    uint FertilityPpm,
    long OrganicMassGram,
    IReadOnlyList<PartitionRecordRefV1> ContaminantRefs)
{
    public const string PartitionId = "environment.soil";
    public IReadOnlyDictionary<string, object?> ToStandardPayload() => EnvironmentPayloadFields.Map(
        ("spatial_scope", SpatialScope),
        ("soil_class", SoilClass.Value),
        ("depth_mm", DepthMm),
        ("moisture_ppm", MoisturePpm),
        ("fertility_ppm", FertilityPpm),
        ("organic_mass_g", OrganicMassGram),
        ("contaminant_refs", ContaminantRefs));
    public byte[] CanonicalDigest() => EnvironmentPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static EnvironmentSoilPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        EnvironmentPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "spatial_scope"),
        new StableToken(EnvironmentPayloadFields.Required<string>(values, PartitionId, "soil_class")),
        EnvironmentPayloadFields.Required<long>(values, PartitionId, "depth_mm"),
        EnvironmentPayloadFields.Required<uint>(values, PartitionId, "moisture_ppm"),
        EnvironmentPayloadFields.Required<uint>(values, PartitionId, "fertility_ppm"),
        EnvironmentPayloadFields.Required<long>(values, PartitionId, "organic_mass_g"),
        EnvironmentPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "contaminant_refs"));
}

public sealed record EnvironmentResourceDepositPayloadV1(
    PartitionRecordRefV1 SpatialScope,
    StableToken ResourceKind,
    long RemainingMassGram,
    uint GradePpm,
    long RenewalRateGramPerStep,
    uint AccessibilityPpm)
{
    public const string PartitionId = "environment.resource_deposit";
    public IReadOnlyDictionary<string, object?> ToStandardPayload() => EnvironmentPayloadFields.Map(
        ("spatial_scope", SpatialScope),
        ("resource_kind", ResourceKind.Value),
        ("remaining_mass_g", RemainingMassGram),
        ("grade_ppm", GradePpm),
        ("renewal_rate_g_per_step", RenewalRateGramPerStep),
        ("accessibility_ppm", AccessibilityPpm));
    public byte[] CanonicalDigest() => EnvironmentPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static EnvironmentResourceDepositPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        EnvironmentPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "spatial_scope"),
        new StableToken(EnvironmentPayloadFields.Required<string>(values, PartitionId, "resource_kind")),
        EnvironmentPayloadFields.Required<long>(values, PartitionId, "remaining_mass_g"),
        EnvironmentPayloadFields.Required<uint>(values, PartitionId, "grade_ppm"),
        EnvironmentPayloadFields.Required<long>(values, PartitionId, "renewal_rate_g_per_step"),
        EnvironmentPayloadFields.Required<uint>(values, PartitionId, "accessibility_ppm"));
}

public sealed record EnvironmentGroundwaterPayloadV1(
    PartitionRecordRefV1 SpatialScope,
    long WaterVolumeMl,
    long HydraulicHeadMm,
    uint QualityPpm,
    int TemperatureMilliKelvin,
    IReadOnlyList<PartitionRecordRefV1> NeighborRefs)
{
    public const string PartitionId = "environment.groundwater";
    public IReadOnlyDictionary<string, object?> ToStandardPayload() => EnvironmentPayloadFields.Map(
        ("spatial_scope", SpatialScope),
        ("water_volume_ml", WaterVolumeMl),
        ("hydraulic_head_mm", HydraulicHeadMm),
        ("quality_ppm", QualityPpm),
        ("temperature_mk", TemperatureMilliKelvin),
        ("neighbor_refs", NeighborRefs));
    public byte[] CanonicalDigest() => EnvironmentPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static EnvironmentGroundwaterPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        EnvironmentPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "spatial_scope"),
        EnvironmentPayloadFields.Required<long>(values, PartitionId, "water_volume_ml"),
        EnvironmentPayloadFields.Required<long>(values, PartitionId, "hydraulic_head_mm"),
        EnvironmentPayloadFields.Required<uint>(values, PartitionId, "quality_ppm"),
        EnvironmentPayloadFields.Required<int>(values, PartitionId, "temperature_mk"),
        EnvironmentPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "neighbor_refs"));
}

public sealed record EnvironmentAtmospherePayloadV1(
    PartitionRecordRefV1 SpatialScope,
    int PressurePa,
    int TemperatureMilliKelvin,
    uint HumidityPpm,
    global::MachiVerse.Simulation.Core.WorldState.Vec3Int64V1 WindUmPerSecond,
    long VaporMassGram,
    long LiquidMassGram,
    IReadOnlyList<KeyValuePair<string, uint>> GasPpb)
{
    public const string PartitionId = "environment.atmosphere";
    public IReadOnlyDictionary<string, object?> ToStandardPayload() => EnvironmentPayloadFields.Map(
        ("spatial_scope", SpatialScope),
        ("pressure_pa", PressurePa),
        ("temperature_mk", TemperatureMilliKelvin),
        ("humidity_ppm", HumidityPpm),
        ("wind_um_s", WindUmPerSecond),
        ("vapor_mass_g", VaporMassGram),
        ("liquid_mass_g", LiquidMassGram),
        ("gas_ppb", GasPpb));
    public byte[] CanonicalDigest() => EnvironmentPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static EnvironmentAtmospherePayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        EnvironmentPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "spatial_scope"),
        EnvironmentPayloadFields.Required<int>(values, PartitionId, "pressure_pa"),
        EnvironmentPayloadFields.Required<int>(values, PartitionId, "temperature_mk"),
        EnvironmentPayloadFields.Required<uint>(values, PartitionId, "humidity_ppm"),
        EnvironmentPayloadFields.Required<global::MachiVerse.Simulation.Core.WorldState.Vec3Int64V1>(values, PartitionId, "wind_um_s"),
        EnvironmentPayloadFields.Required<long>(values, PartitionId, "vapor_mass_g"),
        EnvironmentPayloadFields.Required<long>(values, PartitionId, "liquid_mass_g"),
        EnvironmentPayloadFields.Required<IReadOnlyList<KeyValuePair<string, uint>>>(values, PartitionId, "gas_ppb"));
}

public sealed record EnvironmentClimatePayloadV1(
    PartitionRecordRefV1 SpatialScope,
    StableToken Regime,
    int TemperatureMeanMilliKelvin,
    long PrecipitationMeanMl,
    global::MachiVerse.Simulation.Core.WorldState.Vec3Int64V1 WindMeanUmPerSecond,
    ulong SampleCount,
    uint AggregateGeneration)
{
    public const string PartitionId = "environment.climate";
    public IReadOnlyDictionary<string, object?> ToStandardPayload() => EnvironmentPayloadFields.Map(
        ("spatial_scope", SpatialScope),
        ("regime", Regime.Value),
        ("temperature_mean_mk", TemperatureMeanMilliKelvin),
        ("precipitation_mean_ml", PrecipitationMeanMl),
        ("wind_mean_um_s", WindMeanUmPerSecond),
        ("sample_count", SampleCount),
        ("aggregate_generation", AggregateGeneration));
    public byte[] CanonicalDigest() => EnvironmentPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static EnvironmentClimatePayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        EnvironmentPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "spatial_scope"),
        new StableToken(EnvironmentPayloadFields.Required<string>(values, PartitionId, "regime")),
        EnvironmentPayloadFields.Required<int>(values, PartitionId, "temperature_mean_mk"),
        EnvironmentPayloadFields.Required<long>(values, PartitionId, "precipitation_mean_ml"),
        EnvironmentPayloadFields.Required<global::MachiVerse.Simulation.Core.WorldState.Vec3Int64V1>(values, PartitionId, "wind_mean_um_s"),
        EnvironmentPayloadFields.Required<ulong>(values, PartitionId, "sample_count"),
        EnvironmentPayloadFields.Required<uint>(values, PartitionId, "aggregate_generation"));
}

public sealed record EnvironmentWeatherPayloadV1(
    PartitionRecordRefV1 SpatialScope,
    StableToken WeatherClass,
    long PrecipitationMlPerStep,
    uint CloudPpm,
    long VisibilityMm,
    uint StormIntensityPpm,
    ulong BasisAtmosphereRevision)
{
    public const string PartitionId = "environment.weather";
    public IReadOnlyDictionary<string, object?> ToStandardPayload() => EnvironmentPayloadFields.Map(
        ("spatial_scope", SpatialScope),
        ("weather_class", WeatherClass.Value),
        ("precipitation_ml_per_step", PrecipitationMlPerStep),
        ("cloud_ppm", CloudPpm),
        ("visibility_mm", VisibilityMm),
        ("storm_intensity_ppm", StormIntensityPpm),
        ("basis_atmosphere_revision", BasisAtmosphereRevision));
    public byte[] CanonicalDigest() => EnvironmentPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static EnvironmentWeatherPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        EnvironmentPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "spatial_scope"),
        new StableToken(EnvironmentPayloadFields.Required<string>(values, PartitionId, "weather_class")),
        EnvironmentPayloadFields.Required<long>(values, PartitionId, "precipitation_ml_per_step"),
        EnvironmentPayloadFields.Required<uint>(values, PartitionId, "cloud_ppm"),
        EnvironmentPayloadFields.Required<long>(values, PartitionId, "visibility_mm"),
        EnvironmentPayloadFields.Required<uint>(values, PartitionId, "storm_intensity_ppm"),
        EnvironmentPayloadFields.Required<ulong>(values, PartitionId, "basis_atmosphere_revision"));
}

public sealed record EnvironmentSurfaceWaterPayloadV1(
    PartitionRecordRefV1 SpatialScope,
    StableToken WaterBodyClass,
    long VolumeMl,
    long SurfaceLevelMm,
    global::MachiVerse.Simulation.Core.WorldState.Vec3Int64V1 FlowUmPerSecond,
    int TemperatureMilliKelvin,
    uint QualityPpm,
    IReadOnlyList<PartitionRecordRefV1> DownstreamRefs)
{
    public const string PartitionId = "environment.surface_water";
    public IReadOnlyDictionary<string, object?> ToStandardPayload() => EnvironmentPayloadFields.Map(
        ("spatial_scope", SpatialScope),
        ("water_body_class", WaterBodyClass.Value),
        ("volume_ml", VolumeMl),
        ("surface_level_mm", SurfaceLevelMm),
        ("flow_um_s", FlowUmPerSecond),
        ("temperature_mk", TemperatureMilliKelvin),
        ("quality_ppm", QualityPpm),
        ("downstream_refs", DownstreamRefs));
    public byte[] CanonicalDigest() => EnvironmentPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static EnvironmentSurfaceWaterPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        EnvironmentPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "spatial_scope"),
        new StableToken(EnvironmentPayloadFields.Required<string>(values, PartitionId, "water_body_class")),
        EnvironmentPayloadFields.Required<long>(values, PartitionId, "volume_ml"),
        EnvironmentPayloadFields.Required<long>(values, PartitionId, "surface_level_mm"),
        EnvironmentPayloadFields.Required<global::MachiVerse.Simulation.Core.WorldState.Vec3Int64V1>(values, PartitionId, "flow_um_s"),
        EnvironmentPayloadFields.Required<int>(values, PartitionId, "temperature_mk"),
        EnvironmentPayloadFields.Required<uint>(values, PartitionId, "quality_ppm"),
        EnvironmentPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "downstream_refs"));
}

public sealed record EnvironmentOceanPayloadV1(
    PartitionRecordRefV1 SpatialScope,
    long WaterVolumeMl,
    long SurfaceLevelMm,
    global::MachiVerse.Simulation.Core.WorldState.Vec3Int64V1 VelocityUmPerSecond,
    int TemperatureMilliKelvin,
    uint SalinityPpm,
    IReadOnlyList<PartitionRecordRefV1> NeighborRefs)
{
    public const string PartitionId = "environment.ocean";
    public IReadOnlyDictionary<string, object?> ToStandardPayload() => EnvironmentPayloadFields.Map(
        ("spatial_scope", SpatialScope),
        ("water_volume_ml", WaterVolumeMl),
        ("surface_level_mm", SurfaceLevelMm),
        ("velocity_um_s", VelocityUmPerSecond),
        ("temperature_mk", TemperatureMilliKelvin),
        ("salinity_ppm", SalinityPpm),
        ("neighbor_refs", NeighborRefs));
    public byte[] CanonicalDigest() => EnvironmentPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static EnvironmentOceanPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        EnvironmentPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "spatial_scope"),
        EnvironmentPayloadFields.Required<long>(values, PartitionId, "water_volume_ml"),
        EnvironmentPayloadFields.Required<long>(values, PartitionId, "surface_level_mm"),
        EnvironmentPayloadFields.Required<global::MachiVerse.Simulation.Core.WorldState.Vec3Int64V1>(values, PartitionId, "velocity_um_s"),
        EnvironmentPayloadFields.Required<int>(values, PartitionId, "temperature_mk"),
        EnvironmentPayloadFields.Required<uint>(values, PartitionId, "salinity_ppm"),
        EnvironmentPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "neighbor_refs"));
}

public sealed record EnvironmentEcosystemPayloadV1(
    PartitionRecordRefV1 SpatialScope,
    StableToken SpeciesOrCohort,
    ulong Population,
    long BiomassGram,
    uint BirthRatePpm,
    uint DeathRatePpm,
    uint MigrationRatePpm,
    IReadOnlyList<PartitionRecordRefV1> ResourceRefs)
{
    public const string PartitionId = "environment.ecosystem";
    public IReadOnlyDictionary<string, object?> ToStandardPayload() => EnvironmentPayloadFields.Map(
        ("spatial_scope", SpatialScope),
        ("species_or_cohort", SpeciesOrCohort.Value),
        ("population", Population),
        ("biomass_g", BiomassGram),
        ("birth_rate_ppm", BirthRatePpm),
        ("death_rate_ppm", DeathRatePpm),
        ("migration_rate_ppm", MigrationRatePpm),
        ("resource_refs", ResourceRefs));
    public byte[] CanonicalDigest() => EnvironmentPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static EnvironmentEcosystemPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        EnvironmentPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "spatial_scope"),
        new StableToken(EnvironmentPayloadFields.Required<string>(values, PartitionId, "species_or_cohort")),
        EnvironmentPayloadFields.Required<ulong>(values, PartitionId, "population"),
        EnvironmentPayloadFields.Required<long>(values, PartitionId, "biomass_g"),
        EnvironmentPayloadFields.Required<uint>(values, PartitionId, "birth_rate_ppm"),
        EnvironmentPayloadFields.Required<uint>(values, PartitionId, "death_rate_ppm"),
        EnvironmentPayloadFields.Required<uint>(values, PartitionId, "migration_rate_ppm"),
        EnvironmentPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "resource_refs"));
}

public sealed record EnvironmentContaminantPayloadV1(
    PartitionRecordRefV1 SpatialScope,
    StableToken ContaminantKind,
    long StockMassGram,
    uint ConcentrationPpb,
    IReadOnlyList<PartitionRecordRefV1> SourceRefs,
    IReadOnlyList<PartitionRecordRefV1> SinkRefs)
{
    public const string PartitionId = "environment.contaminant";
    public IReadOnlyDictionary<string, object?> ToStandardPayload() => EnvironmentPayloadFields.Map(
        ("spatial_scope", SpatialScope),
        ("contaminant_kind", ContaminantKind.Value),
        ("stock_mass_g", StockMassGram),
        ("concentration_ppb", ConcentrationPpb),
        ("source_refs", SourceRefs),
        ("sink_refs", SinkRefs));
    public byte[] CanonicalDigest() => EnvironmentPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static EnvironmentContaminantPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        EnvironmentPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "spatial_scope"),
        new StableToken(EnvironmentPayloadFields.Required<string>(values, PartitionId, "contaminant_kind")),
        EnvironmentPayloadFields.Required<long>(values, PartitionId, "stock_mass_g"),
        EnvironmentPayloadFields.Required<uint>(values, PartitionId, "concentration_ppb"),
        EnvironmentPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "source_refs"),
        EnvironmentPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "sink_refs"));
}

public sealed record EnvironmentHazardPayloadV1(
    PartitionRecordRefV1 SpatialScope,
    StableToken HazardKind,
    uint IntensityPpm,
    ulong StartedStep,
    ulong? ExpectedEndStep,
    IReadOnlyList<PartitionRecordRefV1> DriverRefs,
    IReadOnlyList<PartitionRecordRefV1> AffectedScopeRefs)
{
    public const string PartitionId = "environment.hazard";
    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var values = EnvironmentPayloadFields.Map(
            ("spatial_scope", SpatialScope),
            ("hazard_kind", HazardKind.Value),
            ("intensity_ppm", IntensityPpm),
            ("started_step", StartedStep),
            ("driver_refs", DriverRefs),
            ("affected_scope_refs", AffectedScopeRefs));
        EnvironmentPayloadFields.AddOptional(values, "expected_end_step", ExpectedEndStep);
        return values;
    }
    public byte[] CanonicalDigest() => EnvironmentPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static EnvironmentHazardPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        EnvironmentPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "spatial_scope"),
        new StableToken(EnvironmentPayloadFields.Required<string>(values, PartitionId, "hazard_kind")),
        EnvironmentPayloadFields.Required<uint>(values, PartitionId, "intensity_ppm"),
        EnvironmentPayloadFields.Required<ulong>(values, PartitionId, "started_step"),
        EnvironmentPayloadFields.Optional<ulong>(values, "expected_end_step"),
        EnvironmentPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "driver_refs"),
        EnvironmentPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "affected_scope_refs"));
}

public sealed record EnvironmentLineagePayloadV1(
    PartitionRecordRefV1 SubjectRef,
    IReadOnlyList<PartitionRecordRefV1> ParentRefs,
    uint Generation,
    StableToken MaterializationKind,
    byte[] SourceDigest)
{
    public const string PartitionId = "environment.environment_lineage";
    public IReadOnlyDictionary<string, object?> ToStandardPayload() => EnvironmentPayloadFields.Map(
        ("subject_ref", SubjectRef),
        ("parent_refs", ParentRefs),
        ("generation", Generation),
        ("materialization_kind", MaterializationKind.Value),
        ("source_digest", SourceDigest));
    public byte[] CanonicalDigest() => EnvironmentPayloadFields.Digest(PartitionId, ToStandardPayload());
    public static EnvironmentLineagePayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> values) => new(
        EnvironmentPayloadFields.Required<PartitionRecordRefV1>(values, PartitionId, "subject_ref"),
        EnvironmentPayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(values, PartitionId, "parent_refs"),
        EnvironmentPayloadFields.Required<uint>(values, PartitionId, "generation"),
        new StableToken(EnvironmentPayloadFields.Required<string>(values, PartitionId, "materialization_kind")),
        EnvironmentPayloadFields.Required<byte[]>(values, PartitionId, "source_digest").ToArray());
}

public static class EnvironmentDomainSnapshotProviderV1
{
    public static IReadOnlyList<IDomainPartitionSnapshotSectionProviderV1> CreateAll()
    {
        IDomainPartitionSnapshotSectionProviderV1[] providers =
        {
            Provider<EnvironmentGeologyPayloadV1>(EnvironmentGeologyPayloadV1.PartitionId, static value => value.ToStandardPayload(), EnvironmentGeologyPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<EnvironmentSoilPayloadV1>(EnvironmentSoilPayloadV1.PartitionId, static value => value.ToStandardPayload(), EnvironmentSoilPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<EnvironmentResourceDepositPayloadV1>(EnvironmentResourceDepositPayloadV1.PartitionId, static value => value.ToStandardPayload(), EnvironmentResourceDepositPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<EnvironmentGroundwaterPayloadV1>(EnvironmentGroundwaterPayloadV1.PartitionId, static value => value.ToStandardPayload(), EnvironmentGroundwaterPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<EnvironmentAtmospherePayloadV1>(EnvironmentAtmospherePayloadV1.PartitionId, static value => value.ToStandardPayload(), EnvironmentAtmospherePayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<EnvironmentClimatePayloadV1>(EnvironmentClimatePayloadV1.PartitionId, static value => value.ToStandardPayload(), EnvironmentClimatePayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<EnvironmentWeatherPayloadV1>(EnvironmentWeatherPayloadV1.PartitionId, static value => value.ToStandardPayload(), EnvironmentWeatherPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<EnvironmentSurfaceWaterPayloadV1>(EnvironmentSurfaceWaterPayloadV1.PartitionId, static value => value.ToStandardPayload(), EnvironmentSurfaceWaterPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<EnvironmentOceanPayloadV1>(EnvironmentOceanPayloadV1.PartitionId, static value => value.ToStandardPayload(), EnvironmentOceanPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<EnvironmentEcosystemPayloadV1>(EnvironmentEcosystemPayloadV1.PartitionId, static value => value.ToStandardPayload(), EnvironmentEcosystemPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<EnvironmentContaminantPayloadV1>(EnvironmentContaminantPayloadV1.PartitionId, static value => value.ToStandardPayload(), EnvironmentContaminantPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<EnvironmentHazardPayloadV1>(EnvironmentHazardPayloadV1.PartitionId, static value => value.ToStandardPayload(), EnvironmentHazardPayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
            Provider<EnvironmentLineagePayloadV1>(EnvironmentLineagePayloadV1.PartitionId, static value => value.ToStandardPayload(), EnvironmentLineagePayloadV1.FromStandardPayload, static value => value.CanonicalDigest()),
        };
        return Array.AsReadOnly(providers.OrderBy(static provider => provider.SectionId, StringComparer.Ordinal).ToArray());
    }

    private static IDomainPartitionSnapshotSectionProviderV1 Provider<TPayload>(
        string partitionId,
        Func<TPayload, IReadOnlyDictionary<string, object?>> toStandard,
        Func<IReadOnlyDictionary<string, object?>, TPayload> fromStandard,
        Func<TPayload, byte[]> digest)
        => new DomainPartitionSnapshotSectionProviderV1<TPayload>(
            partitionId,
            toStandard,
            fromStandard,
            digest,
            StandardDomainNestedSnapshotCodecRegistryV1.Default);
}

internal static class EnvironmentPayloadFields
{
    public static Dictionary<string, object?> Map(params (string Name, object? Value)[] values)
        => values.ToDictionary(static pair => pair.Name, static pair => pair.Value, StringComparer.Ordinal);

    public static void AddOptional<T>(Dictionary<string, object?> values, string field, T? value)
        where T : struct
    {
        if (value is { } present) values[field] = present;
    }

    public static T Required<T>(IReadOnlyDictionary<string, object?> values, string partitionId, string field)
        => values.TryGetValue(field, out var value) && value is T typed
            ? typed
            : throw new InvalidDataException($"environment.snapshot-payload.required:{partitionId}:{field}");

    public static T? Optional<T>(IReadOnlyDictionary<string, object?> values, string field)
        where T : struct
        => values.TryGetValue(field, out var value) && value is not null ? (T)value : null;

    public static IReadOnlyList<string> TokenValues(IReadOnlyList<StableToken> tokens)
        => Array.AsReadOnly(tokens.Select(static token => token.Value).ToArray());

    public static IReadOnlyList<StableToken> Tokens(IReadOnlyDictionary<string, object?> values, string partitionId, string field)
    {
        var source = Required<IReadOnlyList<string>>(values, partitionId, field);
        return Array.AsReadOnly(source.Select(static token => new StableToken(token)).ToArray());
    }

    public static byte[] Digest(string partitionId, IReadOnlyDictionary<string, object?> payload)
        => StandardDomainPayloadCanonicalDigestV1.Compute(partitionId, payload);
}
