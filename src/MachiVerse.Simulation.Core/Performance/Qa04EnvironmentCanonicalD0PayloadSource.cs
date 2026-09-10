using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Environment;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Production perf.reference.v1 D0 Environment payload authority. Every scalar is derived from the
/// benchmark genesis source, regional scope is the canonical TileScope authority, topology uses the
/// canonical same-partition successor rule, and optional relation lists remain empty at genesis.
/// </summary>
public sealed class Qa04EnvironmentCanonicalD0PayloadSourceV1 : IQa04EnvironmentD0PayloadSourceV1
{
    private static readonly IReadOnlyList<PartitionRecordRefV1> EmptyRefs = Array.Empty<PartitionRecordRefV1>();
    private static readonly IReadOnlyList<StableToken> MaterialClasses =
        Array.AsReadOnly(new[] { Qa04EnvironmentGenesisContractV1.MaterialClass });

    public EnvironmentGeologyPayloadV1 Geology(Qa04EnvironmentD0BindingV1 binding)
    {
        Require(binding, EnvironmentGeologyPayloadV1.PartitionId);
        var id = binding.Descriptor.RecordId;
        return new EnvironmentGeologyPayloadV1(
            Scope(binding), MaterialClasses, EmptyRefs,
            Ppm(id, "porosity_ppm"), Ppm(id, "stability_ppm"),
            PositiveLong(id, "permeability_q32"), EmptyRefs, EmptyRefs);
    }

    public EnvironmentSoilPayloadV1 Soil(Qa04EnvironmentD0BindingV1 binding)
    {
        Require(binding, EnvironmentSoilPayloadV1.PartitionId);
        var id = binding.Descriptor.RecordId;
        return new EnvironmentSoilPayloadV1(
            Scope(binding), Qa04EnvironmentGenesisContractV1.SoilClass,
            PositiveLong(id, "depth_mm"), Ppm(id, "moisture_ppm"), Ppm(id, "fertility_ppm"),
            PositiveLong(id, "organic_mass_g"), EmptyRefs);
    }

    public EnvironmentResourceDepositPayloadV1 ResourceDeposit(Qa04EnvironmentD0BindingV1 binding)
    {
        Require(binding, EnvironmentResourceDepositPayloadV1.PartitionId);
        var id = binding.Descriptor.RecordId;
        return new EnvironmentResourceDepositPayloadV1(
            Scope(binding), Qa04EnvironmentGenesisContractV1.ResourceKind,
            PositiveLong(id, "remaining_mass_g"), Ppm(id, "grade_ppm"),
            PositiveLong(id, "renewal_rate_g_per_step"), Ppm(id, "accessibility_ppm"));
    }

    public EnvironmentGroundwaterPayloadV1 Groundwater(Qa04EnvironmentD0BindingV1 binding)
    {
        Require(binding, EnvironmentGroundwaterPayloadV1.PartitionId);
        var id = binding.Descriptor.RecordId;
        return new EnvironmentGroundwaterPayloadV1(
            Scope(binding), PositiveLong(id, "water_volume_ml"), PositiveLong(id, "hydraulic_head_mm"),
            Ppm(id, "quality_ppm"), PositiveInt(id, "temperature_mk"),
            One(Qa04EnvironmentReferenceDecompositionV1.NextD0RecordRef(binding.PartitionId.Value, id)));
    }

    public EnvironmentAtmospherePayloadV1 Atmosphere(Qa04EnvironmentD0BindingV1 binding)
    {
        Require(binding, EnvironmentAtmospherePayloadV1.PartitionId);
        var id = binding.Descriptor.RecordId;
        return new EnvironmentAtmospherePayloadV1(
            Scope(binding), PositiveInt(id, "pressure_pa"), PositiveInt(id, "temperature_mk"),
            Ppm(id, "humidity_ppm"), Vector(id, "wind_um_s"), PositiveLong(id, "vapor_mass_g"),
            PositiveLong(id, "liquid_mass_g"), Qa04EnvironmentGenesisContractV1.AtmosphereGasPpb);
    }

    public EnvironmentClimatePayloadV1 Climate(Qa04EnvironmentD0BindingV1 binding)
    {
        Require(binding, EnvironmentClimatePayloadV1.PartitionId);
        var id = binding.Descriptor.RecordId;
        return new EnvironmentClimatePayloadV1(
            Scope(binding), Qa04EnvironmentGenesisContractV1.ClimateRegime,
            PositiveInt(id, "temperature_mean_mk"), PositiveLong(id, "precipitation_mean_ml"),
            Vector(id, "wind_mean_um_s"), Qa04ReferenceGenesisValueSourceV1.PositiveCount(id, "sample_count"),
            AggregateGeneration: 1);
    }

    public EnvironmentWeatherPayloadV1 Weather(Qa04EnvironmentD0BindingV1 binding)
    {
        Require(binding, EnvironmentWeatherPayloadV1.PartitionId);
        var id = binding.Descriptor.RecordId;
        return new EnvironmentWeatherPayloadV1(
            Scope(binding), Qa04EnvironmentGenesisContractV1.WeatherClass,
            PositiveLong(id, "precipitation_ml_per_step"), Ppm(id, "cloud_ppm"),
            PositiveLong(id, "visibility_mm"), Ppm(id, "storm_intensity_ppm"), BasisAtmosphereRevision: 1);
    }

    public EnvironmentSurfaceWaterPayloadV1 SurfaceWater(Qa04EnvironmentD0BindingV1 binding)
    {
        Require(binding, EnvironmentSurfaceWaterPayloadV1.PartitionId);
        var id = binding.Descriptor.RecordId;
        return new EnvironmentSurfaceWaterPayloadV1(
            Scope(binding), Qa04EnvironmentGenesisContractV1.WaterBodyClass,
            PositiveLong(id, "volume_ml"), PositiveLong(id, "surface_level_mm"), Vector(id, "flow_um_s"),
            PositiveInt(id, "temperature_mk"), Ppm(id, "quality_ppm"),
            One(Qa04EnvironmentReferenceDecompositionV1.NextD0RecordRef(binding.PartitionId.Value, id)));
    }

    public EnvironmentOceanPayloadV1 Ocean(Qa04EnvironmentD0BindingV1 binding)
    {
        Require(binding, EnvironmentOceanPayloadV1.PartitionId);
        var id = binding.Descriptor.RecordId;
        return new EnvironmentOceanPayloadV1(
            Scope(binding), PositiveLong(id, "water_volume_ml"), PositiveLong(id, "surface_level_mm"),
            Vector(id, "velocity_um_s"), PositiveInt(id, "temperature_mk"), Ppm(id, "salinity_ppm"),
            One(Qa04EnvironmentReferenceDecompositionV1.NextD0RecordRef(binding.PartitionId.Value, id)));
    }

    public EnvironmentEcosystemPayloadV1 Ecosystem(Qa04EnvironmentD0BindingV1 binding)
    {
        Require(binding, EnvironmentEcosystemPayloadV1.PartitionId);
        var id = binding.Descriptor.RecordId;
        return new EnvironmentEcosystemPayloadV1(
            Scope(binding), Qa04EnvironmentGenesisContractV1.SpeciesOrCohort,
            Qa04ReferenceGenesisValueSourceV1.PositiveCount(id, "population"), PositiveLong(id, "biomass_g"),
            Ppm(id, "birth_rate_ppm"), Ppm(id, "death_rate_ppm"), Ppm(id, "migration_rate_ppm"), EmptyRefs);
    }

    public EnvironmentContaminantPayloadV1 Contaminant(Qa04EnvironmentD0BindingV1 binding)
    {
        Require(binding, EnvironmentContaminantPayloadV1.PartitionId);
        var id = binding.Descriptor.RecordId;
        return new EnvironmentContaminantPayloadV1(
            Scope(binding), Qa04EnvironmentGenesisContractV1.ContaminantKind,
            PositiveLong(id, "stock_mass_g"), Ppm(id, "concentration_ppb"), EmptyRefs, EmptyRefs);
    }

    public EnvironmentHazardPayloadV1 Hazard(Qa04EnvironmentD0BindingV1 binding)
    {
        Require(binding, EnvironmentHazardPayloadV1.PartitionId);
        var id = binding.Descriptor.RecordId;
        var scope = Scope(binding);
        return new EnvironmentHazardPayloadV1(
            scope, Qa04EnvironmentGenesisContractV1.HazardKind, Ppm(id, "intensity_ppm"),
            StartedStep: 0, ExpectedEndStep: null, DriverRefs: EmptyRefs, AffectedScopeRefs: One(scope));
    }

    public EnvironmentLineagePayloadV1 Lineage(Qa04EnvironmentD0BindingV1 binding)
    {
        Require(binding, EnvironmentLineagePayloadV1.PartitionId);
        var subject = Qa04EnvironmentLineageAuthorityV1.ResolveD0Subject(binding);
        return new EnvironmentLineagePayloadV1(
            subject, EmptyRefs, Qa04EnvironmentLineageAuthorityV1.D0Generation,
            Qa04EnvironmentGenesisContractV1.MaterializationKind, SubjectIdentityDigest(subject));
    }

    private static PartitionRecordRefV1 Scope(Qa04EnvironmentD0BindingV1 binding)
        => Qa04EnvironmentD0PartitionMaterializerV1.ResolveSpatialScope(binding);

    private static uint Ppm(OpaqueId128 id, string tag)
        => Qa04ReferenceGenesisValueSourceV1.BoundedPpm(id, tag);

    private static long PositiveLong(OpaqueId128 id, string tag)
        => checked((long)Qa04ReferenceGenesisValueSourceV1.PositiveCount(id, tag));

    private static int PositiveInt(OpaqueId128 id, string tag)
        => checked((int)Qa04ReferenceGenesisValueSourceV1.PositiveCount(id, tag));

    private static Vec3Int64V1 Vector(OpaqueId128 id, string tag)
        => new(
            Qa04ReferenceGenesisValueSourceV1.SmallSignedValue(id, tag + ".x"),
            Qa04ReferenceGenesisValueSourceV1.SmallSignedValue(id, tag + ".y"),
            Qa04ReferenceGenesisValueSourceV1.SmallSignedValue(id, tag + ".z"));

    private static IReadOnlyList<PartitionRecordRefV1> One(PartitionRecordRefV1 reference)
        => Array.AsReadOnly(new[] { reference });

    private static byte[] SubjectIdentityDigest(PartitionRecordRefV1 subject)
        => HashSuite.DomainHash("mv.perf-reference-environment-lineage-source.v1", writer =>
        {
            writer.WriteArrayStart(2);
            writer.WriteAsciiText(subject.PartitionId.Value);
            writer.WriteBytes(subject.RecordId.ToBytes());
        });

    private static void Require(Qa04EnvironmentD0BindingV1 binding, string partitionId)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (binding.PartitionId.Value != partitionId || binding.Descriptor.DetailLevel != DetailLevelV1.D0Entity)
            throw new InvalidDataException($"qa04.environment.canonical-d0-binding-invalid:{partitionId}");
    }
}
