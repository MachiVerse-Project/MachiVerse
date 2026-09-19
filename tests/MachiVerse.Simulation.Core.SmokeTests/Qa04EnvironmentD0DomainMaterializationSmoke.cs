using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Environment;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04EnvironmentD0DomainMaterializationSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        var scope = new PartitionRecordRefV1(
            "spatial.scope_registry",
            OpaqueId128.Parse("00000000000000000000000000e20001"));
        var topologyRefs = new[]
        {
            CanonicalNext(EnvironmentGroundwaterPayloadV1.PartitionId),
            CanonicalNext(EnvironmentSurfaceWaterPayloadV1.PartitionId),
            CanonicalNext(EnvironmentOceanPayloadV1.PartitionId),
        };
        var resolver = new Resolver(new[] { scope }.Concat(topologyRefs));
        var source = new Source(scope);

        var materialized = Qa04EnvironmentD0DomainMaterializerV1.MaterializeReduced(
            source,
            resolver,
            recordsPerPartition: 1);

        Require(materialized.MaterializedRecordCount == 13,
            "Reduced QA-04 Environment D0 domain materialization must create one record in each owner partition.");
        Require(materialized.CountsByPartition.Count == 13 && materialized.CountsByPartition.Values.All(static count => count == 1),
            "Reduced QA-04 Environment D0 domain materialization must cover all thirteen typed roots.");
        Require(!materialized.FullCanonicalD0Materialized,
            "Reduced Environment materialization must never be promoted to full canonical D0 evidence.");

        var state = materialized.State;
        Require(state.Geology.ItemCount == 1 && state.Soil.ItemCount == 1 && state.ResourceDeposit.ItemCount == 1 &&
                state.Groundwater.ItemCount == 1 && state.Atmosphere.ItemCount == 1 && state.Climate.ItemCount == 1 &&
                state.Weather.ItemCount == 1 && state.SurfaceWater.ItemCount == 1 && state.Ocean.ItemCount == 1 &&
                state.Ecosystem.ItemCount == 1 && state.Contaminant.ItemCount == 1 && state.Hazard.ItemCount == 1 &&
                state.Lineage.ItemCount == 1,
            "EnvironmentDomainStateV1 typed root coverage drifted.");
    }

    private static PartitionRecordRefV1 CanonicalNext(string partitionId)
    {
        var first = Qa04EnvironmentReferenceDecompositionV1.Get(partitionId).D0StartOrdinal;
        var binding = Qa04EnvironmentReferenceDecompositionV1.BindD0(first);
        return Qa04EnvironmentReferenceDecompositionV1.NextD0RecordRef(partitionId, binding.Descriptor.RecordId);
    }

    private sealed class Source(PartitionRecordRefV1 scope) : IQa04EnvironmentD0PayloadSourceV1
    {
        private static readonly IReadOnlyList<PartitionRecordRefV1> EmptyRefs = Array.Empty<PartitionRecordRefV1>();

        public EnvironmentGeologyPayloadV1 Geology(Qa04EnvironmentD0BindingV1 binding)
            => new(
                scope,
                Array.AsReadOnly(new[] { Qa04EnvironmentGenesisContractV1.MaterialClass }),
                EmptyRefs,
                Ppm(binding, "porosity_ppm"),
                Ppm(binding, "stability_ppm"),
                Signed(binding, "permeability_q32"),
                EmptyRefs,
                EmptyRefs);

        public EnvironmentSoilPayloadV1 Soil(Qa04EnvironmentD0BindingV1 binding)
            => new(
                scope,
                Qa04EnvironmentGenesisContractV1.SoilClass,
                PositiveLong(binding, "depth_mm"),
                Ppm(binding, "moisture_ppm"),
                Ppm(binding, "fertility_ppm"),
                PositiveLong(binding, "organic_mass_g"),
                EmptyRefs);

        public EnvironmentResourceDepositPayloadV1 ResourceDeposit(Qa04EnvironmentD0BindingV1 binding)
            => new(
                scope,
                Qa04EnvironmentGenesisContractV1.ResourceKind,
                PositiveLong(binding, "remaining_mass_g"),
                Ppm(binding, "grade_ppm"),
                PositiveLong(binding, "renewal_rate_g_per_step"),
                Ppm(binding, "accessibility_ppm"));

        public EnvironmentGroundwaterPayloadV1 Groundwater(Qa04EnvironmentD0BindingV1 binding)
            => new(
                scope,
                PositiveLong(binding, "water_volume_ml"),
                Signed(binding, "hydraulic_head_mm"),
                Ppm(binding, "quality_ppm"),
                PositiveInt(binding, "temperature_mk"),
                Array.AsReadOnly(new[] { Qa04EnvironmentReferenceDecompositionV1.NextD0RecordRef(
                    EnvironmentGroundwaterPayloadV1.PartitionId, binding.Descriptor.RecordId) }));

        public EnvironmentAtmospherePayloadV1 Atmosphere(Qa04EnvironmentD0BindingV1 binding)
            => new(
                scope,
                PositiveInt(binding, "pressure_pa"),
                PositiveInt(binding, "temperature_mk"),
                Ppm(binding, "humidity_ppm"),
                Vector(binding, "wind_um_s"),
                PositiveLong(binding, "vapor_mass_g"),
                PositiveLong(binding, "liquid_mass_g"),
                Qa04EnvironmentGenesisContractV1.AtmosphereGasPpb);

        public EnvironmentClimatePayloadV1 Climate(Qa04EnvironmentD0BindingV1 binding)
            => new(
                scope,
                Qa04EnvironmentGenesisContractV1.ClimateRegime,
                PositiveInt(binding, "temperature_mean_mk"),
                PositiveLong(binding, "precipitation_mean_ml"),
                Vector(binding, "wind_mean_um_s"),
                Positive(binding, "sample_count"),
                1);

        public EnvironmentWeatherPayloadV1 Weather(Qa04EnvironmentD0BindingV1 binding)
            => new(
                scope,
                Qa04EnvironmentGenesisContractV1.WeatherClass,
                PositiveLong(binding, "precipitation_ml_per_step"),
                Ppm(binding, "cloud_ppm"),
                PositiveLong(binding, "visibility_mm"),
                Ppm(binding, "storm_intensity_ppm"),
                1);

        public EnvironmentSurfaceWaterPayloadV1 SurfaceWater(Qa04EnvironmentD0BindingV1 binding)
            => new(
                scope,
                Qa04EnvironmentGenesisContractV1.WaterBodyClass,
                PositiveLong(binding, "volume_ml"),
                Signed(binding, "surface_level_mm"),
                Vector(binding, "flow_um_s"),
                PositiveInt(binding, "temperature_mk"),
                Ppm(binding, "quality_ppm"),
                Array.AsReadOnly(new[] { Qa04EnvironmentReferenceDecompositionV1.NextD0RecordRef(
                    EnvironmentSurfaceWaterPayloadV1.PartitionId, binding.Descriptor.RecordId) }));

        public EnvironmentOceanPayloadV1 Ocean(Qa04EnvironmentD0BindingV1 binding)
            => new(
                scope,
                PositiveLong(binding, "water_volume_ml"),
                Signed(binding, "surface_level_mm"),
                Vector(binding, "velocity_um_s"),
                PositiveInt(binding, "temperature_mk"),
                Ppm(binding, "salinity_ppm"),
                Array.AsReadOnly(new[] { Qa04EnvironmentReferenceDecompositionV1.NextD0RecordRef(
                    EnvironmentOceanPayloadV1.PartitionId, binding.Descriptor.RecordId) }));

        public EnvironmentEcosystemPayloadV1 Ecosystem(Qa04EnvironmentD0BindingV1 binding)
            => new(
                scope,
                Qa04EnvironmentGenesisContractV1.SpeciesOrCohort,
                Positive(binding, "population"),
                PositiveLong(binding, "biomass_g"),
                Ppm(binding, "birth_rate_ppm"),
                Ppm(binding, "death_rate_ppm"),
                Ppm(binding, "migration_rate_ppm"),
                EmptyRefs);

        public EnvironmentContaminantPayloadV1 Contaminant(Qa04EnvironmentD0BindingV1 binding)
            => new(
                scope,
                Qa04EnvironmentGenesisContractV1.ContaminantKind,
                PositiveLong(binding, "stock_mass_g"),
                checked((uint)Positive(binding, "concentration_ppb")),
                EmptyRefs,
                EmptyRefs);

        public EnvironmentHazardPayloadV1 Hazard(Qa04EnvironmentD0BindingV1 binding)
            => new(
                scope,
                Qa04EnvironmentGenesisContractV1.HazardKind,
                Ppm(binding, "intensity_ppm"),
                StartedStep: 0,
                ExpectedEndStep: null,
                EmptyRefs,
                Array.AsReadOnly(new[] { scope }));

        public EnvironmentLineagePayloadV1 Lineage(Qa04EnvironmentD0BindingV1 binding)
            => new(
                scope,
                EmptyRefs,
                Generation: 1,
                Qa04EnvironmentGenesisContractV1.MaterializationKind,
                Qa04ReferenceGenesisValueSourceV1.Hash(binding.Descriptor.RecordId, "source_digest"));

        private static uint Ppm(Qa04EnvironmentD0BindingV1 binding, string field)
            => Qa04ReferenceGenesisValueSourceV1.BoundedPpm(binding.Descriptor.RecordId, field);

        private static ulong Positive(Qa04EnvironmentD0BindingV1 binding, string field)
            => Qa04ReferenceGenesisValueSourceV1.PositiveCount(binding.Descriptor.RecordId, field);

        private static long PositiveLong(Qa04EnvironmentD0BindingV1 binding, string field)
            => checked((long)Positive(binding, field));

        private static int PositiveInt(Qa04EnvironmentD0BindingV1 binding, string field)
            => checked((int)Positive(binding, field));

        private static long Signed(Qa04EnvironmentD0BindingV1 binding, string field)
            => Qa04ReferenceGenesisValueSourceV1.SmallSignedValue(binding.Descriptor.RecordId, field);

        private static Vec3Int64V1 Vector(Qa04EnvironmentD0BindingV1 binding, string field)
            => new(
                Signed(binding, field + ".x"),
                Signed(binding, field + ".y"),
                Signed(binding, field + ".z"));
    }

    private sealed class Resolver(IEnumerable<PartitionRecordRefV1> existing) : IDomainRecordSchemaResolverV1
    {
        private readonly HashSet<PartitionRecordRefV1> _existing = existing.ToHashSet();

        public bool Exists(PartitionRecordRefV1 reference) => _existing.Contains(reference);

        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
        {
            if (!_existing.Contains(reference))
            {
                schema = default;
                return false;
            }
            schema = StandardDomainPartitionRegistry.Get(reference.PartitionId.Value).RecordSchema;
            return true;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
