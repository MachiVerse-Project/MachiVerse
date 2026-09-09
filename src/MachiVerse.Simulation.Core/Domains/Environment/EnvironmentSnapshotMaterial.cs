using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains.Environment;

public sealed class EnvironmentDomainStateV1
{
    public EnvironmentDomainStateV1(
        DomainPartitionStateV1<EnvironmentGeologyPayloadV1> geology,
        DomainPartitionStateV1<EnvironmentSoilPayloadV1> soil,
        DomainPartitionStateV1<EnvironmentResourceDepositPayloadV1> resourceDeposit,
        DomainPartitionStateV1<EnvironmentGroundwaterPayloadV1> groundwater,
        DomainPartitionStateV1<EnvironmentAtmospherePayloadV1> atmosphere,
        DomainPartitionStateV1<EnvironmentClimatePayloadV1> climate,
        DomainPartitionStateV1<EnvironmentWeatherPayloadV1> weather,
        DomainPartitionStateV1<EnvironmentSurfaceWaterPayloadV1> surfaceWater,
        DomainPartitionStateV1<EnvironmentOceanPayloadV1> ocean,
        DomainPartitionStateV1<EnvironmentEcosystemPayloadV1> ecosystem,
        DomainPartitionStateV1<EnvironmentContaminantPayloadV1> contaminant,
        DomainPartitionStateV1<EnvironmentHazardPayloadV1> hazard,
        DomainPartitionStateV1<EnvironmentLineagePayloadV1> lineage)
    {
        Geology = RequireIdentity(geology, EnvironmentGeologyPayloadV1.PartitionId);
        Soil = RequireIdentity(soil, EnvironmentSoilPayloadV1.PartitionId);
        ResourceDeposit = RequireIdentity(resourceDeposit, EnvironmentResourceDepositPayloadV1.PartitionId);
        Groundwater = RequireIdentity(groundwater, EnvironmentGroundwaterPayloadV1.PartitionId);
        Atmosphere = RequireIdentity(atmosphere, EnvironmentAtmospherePayloadV1.PartitionId);
        Climate = RequireIdentity(climate, EnvironmentClimatePayloadV1.PartitionId);
        Weather = RequireIdentity(weather, EnvironmentWeatherPayloadV1.PartitionId);
        SurfaceWater = RequireIdentity(surfaceWater, EnvironmentSurfaceWaterPayloadV1.PartitionId);
        Ocean = RequireIdentity(ocean, EnvironmentOceanPayloadV1.PartitionId);
        Ecosystem = RequireIdentity(ecosystem, EnvironmentEcosystemPayloadV1.PartitionId);
        Contaminant = RequireIdentity(contaminant, EnvironmentContaminantPayloadV1.PartitionId);
        Hazard = RequireIdentity(hazard, EnvironmentHazardPayloadV1.PartitionId);
        Lineage = RequireIdentity(lineage, EnvironmentLineagePayloadV1.PartitionId);
    }

    public DomainPartitionStateV1<EnvironmentGeologyPayloadV1> Geology { get; }
    public DomainPartitionStateV1<EnvironmentSoilPayloadV1> Soil { get; }
    public DomainPartitionStateV1<EnvironmentResourceDepositPayloadV1> ResourceDeposit { get; }
    public DomainPartitionStateV1<EnvironmentGroundwaterPayloadV1> Groundwater { get; }
    public DomainPartitionStateV1<EnvironmentAtmospherePayloadV1> Atmosphere { get; }
    public DomainPartitionStateV1<EnvironmentClimatePayloadV1> Climate { get; }
    public DomainPartitionStateV1<EnvironmentWeatherPayloadV1> Weather { get; }
    public DomainPartitionStateV1<EnvironmentSurfaceWaterPayloadV1> SurfaceWater { get; }
    public DomainPartitionStateV1<EnvironmentOceanPayloadV1> Ocean { get; }
    public DomainPartitionStateV1<EnvironmentEcosystemPayloadV1> Ecosystem { get; }
    public DomainPartitionStateV1<EnvironmentContaminantPayloadV1> Contaminant { get; }
    public DomainPartitionStateV1<EnvironmentHazardPayloadV1> Hazard { get; }
    public DomainPartitionStateV1<EnvironmentLineagePayloadV1> Lineage { get; }

    public static EnvironmentDomainStateV1 CreateEmpty()
        => new(
            Empty<EnvironmentGeologyPayloadV1>(EnvironmentGeologyPayloadV1.PartitionId),
            Empty<EnvironmentSoilPayloadV1>(EnvironmentSoilPayloadV1.PartitionId),
            Empty<EnvironmentResourceDepositPayloadV1>(EnvironmentResourceDepositPayloadV1.PartitionId),
            Empty<EnvironmentGroundwaterPayloadV1>(EnvironmentGroundwaterPayloadV1.PartitionId),
            Empty<EnvironmentAtmospherePayloadV1>(EnvironmentAtmospherePayloadV1.PartitionId),
            Empty<EnvironmentClimatePayloadV1>(EnvironmentClimatePayloadV1.PartitionId),
            Empty<EnvironmentWeatherPayloadV1>(EnvironmentWeatherPayloadV1.PartitionId),
            Empty<EnvironmentSurfaceWaterPayloadV1>(EnvironmentSurfaceWaterPayloadV1.PartitionId),
            Empty<EnvironmentOceanPayloadV1>(EnvironmentOceanPayloadV1.PartitionId),
            Empty<EnvironmentEcosystemPayloadV1>(EnvironmentEcosystemPayloadV1.PartitionId),
            Empty<EnvironmentContaminantPayloadV1>(EnvironmentContaminantPayloadV1.PartitionId),
            Empty<EnvironmentHazardPayloadV1>(EnvironmentHazardPayloadV1.PartitionId),
            Empty<EnvironmentLineagePayloadV1>(EnvironmentLineagePayloadV1.PartitionId));

    public EnvironmentDomainSnapshotMaterialV1 BindSnapshotMaterial(WorldStateV1 frozenState)
        => EnvironmentDomainSnapshotMaterialV1.Bind(frozenState, this);

    private static DomainPartitionStateV1<TPayload> Empty<TPayload>(string partitionId)
        => new(StandardDomainPartitionRegistry.Get(partitionId), Array.Empty<DomainRecordEnvelopeV1<TPayload>>());

    private static DomainPartitionStateV1<TPayload> RequireIdentity<TPayload>(DomainPartitionStateV1<TPayload> partition, string partitionId)
    {
        ArgumentNullException.ThrowIfNull(partition);
        if (partition.Identity != StandardDomainPartitionRegistry.Get(partitionId))
            throw new InvalidDataException($"environment.runtime-state.partition-identity:{partitionId}");
        return partition;
    }
}

public sealed class EnvironmentDomainSnapshotMaterialV1
{
    private EnvironmentDomainSnapshotMaterialV1(IEnumerable<IDomainPartitionSnapshotAuthorityV1> authorities)
    {
        var materialized = authorities?.ToArray() ?? throw new ArgumentNullException(nameof(authorities));
        if (materialized.Length != 13)
            throw new InvalidDataException("environment.snapshot-material.authority-count");
        var byId = new Dictionary<string, IDomainPartitionSnapshotAuthorityV1>(StringComparer.Ordinal);
        foreach (var authority in materialized)
        {
            ArgumentNullException.ThrowIfNull(authority);
            authority.VerifyBoundAuthority();
            if (!string.Equals(authority.Identity.OwnerDomain.Value, "environment", StringComparison.Ordinal))
                throw new InvalidDataException($"environment.snapshot-material.foreign-owner:{authority.PartitionId.Value}");
            if (!byId.TryAdd(authority.PartitionId.Value, authority))
                throw new InvalidDataException($"environment.snapshot-material.duplicate:{authority.PartitionId.Value}");
        }
        Geology = Require<EnvironmentGeologyPayloadV1>(byId, EnvironmentGeologyPayloadV1.PartitionId);
        Soil = Require<EnvironmentSoilPayloadV1>(byId, EnvironmentSoilPayloadV1.PartitionId);
        ResourceDeposit = Require<EnvironmentResourceDepositPayloadV1>(byId, EnvironmentResourceDepositPayloadV1.PartitionId);
        Groundwater = Require<EnvironmentGroundwaterPayloadV1>(byId, EnvironmentGroundwaterPayloadV1.PartitionId);
        Atmosphere = Require<EnvironmentAtmospherePayloadV1>(byId, EnvironmentAtmospherePayloadV1.PartitionId);
        Climate = Require<EnvironmentClimatePayloadV1>(byId, EnvironmentClimatePayloadV1.PartitionId);
        Weather = Require<EnvironmentWeatherPayloadV1>(byId, EnvironmentWeatherPayloadV1.PartitionId);
        SurfaceWater = Require<EnvironmentSurfaceWaterPayloadV1>(byId, EnvironmentSurfaceWaterPayloadV1.PartitionId);
        Ocean = Require<EnvironmentOceanPayloadV1>(byId, EnvironmentOceanPayloadV1.PartitionId);
        Ecosystem = Require<EnvironmentEcosystemPayloadV1>(byId, EnvironmentEcosystemPayloadV1.PartitionId);
        Contaminant = Require<EnvironmentContaminantPayloadV1>(byId, EnvironmentContaminantPayloadV1.PartitionId);
        Hazard = Require<EnvironmentHazardPayloadV1>(byId, EnvironmentHazardPayloadV1.PartitionId);
        Lineage = Require<EnvironmentLineagePayloadV1>(byId, EnvironmentLineagePayloadV1.PartitionId);
        Authorities = Array.AsReadOnly(materialized.OrderBy(static value => value.PartitionId.Value, StringComparer.Ordinal).ToArray());
    }

    public DomainPartitionSnapshotAuthorityV1<EnvironmentGeologyPayloadV1> Geology { get; }
    public DomainPartitionSnapshotAuthorityV1<EnvironmentSoilPayloadV1> Soil { get; }
    public DomainPartitionSnapshotAuthorityV1<EnvironmentResourceDepositPayloadV1> ResourceDeposit { get; }
    public DomainPartitionSnapshotAuthorityV1<EnvironmentGroundwaterPayloadV1> Groundwater { get; }
    public DomainPartitionSnapshotAuthorityV1<EnvironmentAtmospherePayloadV1> Atmosphere { get; }
    public DomainPartitionSnapshotAuthorityV1<EnvironmentClimatePayloadV1> Climate { get; }
    public DomainPartitionSnapshotAuthorityV1<EnvironmentWeatherPayloadV1> Weather { get; }
    public DomainPartitionSnapshotAuthorityV1<EnvironmentSurfaceWaterPayloadV1> SurfaceWater { get; }
    public DomainPartitionSnapshotAuthorityV1<EnvironmentOceanPayloadV1> Ocean { get; }
    public DomainPartitionSnapshotAuthorityV1<EnvironmentEcosystemPayloadV1> Ecosystem { get; }
    public DomainPartitionSnapshotAuthorityV1<EnvironmentContaminantPayloadV1> Contaminant { get; }
    public DomainPartitionSnapshotAuthorityV1<EnvironmentHazardPayloadV1> Hazard { get; }
    public DomainPartitionSnapshotAuthorityV1<EnvironmentLineagePayloadV1> Lineage { get; }
    public IReadOnlyList<IDomainPartitionSnapshotAuthorityV1> Authorities { get; }

    public static EnvironmentDomainSnapshotMaterialV1 Bind(WorldStateV1 frozenState, EnvironmentDomainStateV1 state)
    {
        ArgumentNullException.ThrowIfNull(frozenState);
        ArgumentNullException.ThrowIfNull(state);
        return new EnvironmentDomainSnapshotMaterialV1(
        [
            Bind(frozenState, state.Geology, EnvironmentGeologyPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.Soil, EnvironmentSoilPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.ResourceDeposit, EnvironmentResourceDepositPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.Groundwater, EnvironmentGroundwaterPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.Atmosphere, EnvironmentAtmospherePayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.Climate, EnvironmentClimatePayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.Weather, EnvironmentWeatherPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.SurfaceWater, EnvironmentSurfaceWaterPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.Ocean, EnvironmentOceanPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.Ecosystem, EnvironmentEcosystemPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.Contaminant, EnvironmentContaminantPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.Hazard, EnvironmentHazardPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.Lineage, EnvironmentLineagePayloadV1.PartitionId, static value => value.CanonicalDigest()),
        ]);
    }

    private static DomainPartitionSnapshotAuthorityV1<TPayload> Bind<TPayload>(WorldStateV1 frozenState, DomainPartitionStateV1<TPayload> partition, string partitionId, Func<TPayload, byte[]> digest)
    {
        if (partition.Identity != StandardDomainPartitionRegistry.Get(partitionId))
            throw new InvalidDataException($"environment.snapshot-material.partition-identity:{partitionId}");
        return new DomainPartitionSnapshotAuthorityV1<TPayload>(partition, frozenState.Partitions.Get(partitionId).Header, digest);
    }

    private static DomainPartitionSnapshotAuthorityV1<TPayload> Require<TPayload>(IReadOnlyDictionary<string, IDomainPartitionSnapshotAuthorityV1> byId, string partitionId)
    {
        if (!byId.TryGetValue(partitionId, out var authority))
            throw new InvalidDataException($"environment.snapshot-material.missing:{partitionId}");
        return authority as DomainPartitionSnapshotAuthorityV1<TPayload>
            ?? throw new InvalidDataException($"environment.snapshot-material.payload-type:{partitionId}");
    }
}
