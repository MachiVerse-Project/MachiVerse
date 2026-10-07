using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains.Spatial;

/// <summary>
/// Spatial owner state generation that migrates only spatial.terrain_geometry to the registered
/// record schema 2.0. The other seven standard Spatial partitions remain their exact v1 types.
/// </summary>
public sealed class SpatialDomainStateV2
{
    public SpatialDomainStateV2(
        DomainPartitionStateV1<SpatialWorldFramePayloadV1> worldFrame,
        DomainPartitionStateV1<SpatialScopeRegistryPayloadV1> scopeRegistry,
        SpatialTerrainGeometryPartitionStateV2 terrainGeometry,
        DomainPartitionStateV1<SpatialVoidGeometryPayloadV1> voidGeometry,
        DomainPartitionStateV1<SpatialContainmentTopologyPayloadV1> containmentTopology,
        DomainPartitionStateV1<SpatialBoundaryTopologyPayloadV1> boundaryTopology,
        DomainPartitionStateV1<SpatialDetailRegionsPayloadV1> detailRegions,
        DomainPartitionStateV1<SpatialGeometryLineagePayloadV1> geometryLineage)
    {
        WorldFrame = RequireStandard(worldFrame, SpatialWorldFramePayloadV1.PartitionId);
        ScopeRegistry = RequireStandard(scopeRegistry, SpatialScopeRegistryPayloadV1.PartitionId);
        TerrainGeometry = terrainGeometry ?? throw new ArgumentNullException(nameof(terrainGeometry));
        if (TerrainGeometry.State.Identity != SpatialTerrainGeometryPartitionIdentityV2.Identity)
            throw new InvalidDataException("spatial.runtime-state-v2.partition-identity:spatial.terrain_geometry");
        VoidGeometry = RequireStandard(voidGeometry, SpatialVoidGeometryPayloadV1.PartitionId);
        ContainmentTopology = RequireStandard(containmentTopology, SpatialContainmentTopologyPayloadV1.PartitionId);
        BoundaryTopology = RequireStandard(boundaryTopology, SpatialBoundaryTopologyPayloadV1.PartitionId);
        DetailRegions = RequireStandard(detailRegions, SpatialDetailRegionsPayloadV1.PartitionId);
        GeometryLineage = RequireStandard(geometryLineage, SpatialGeometryLineagePayloadV1.PartitionId);
    }

    public DomainPartitionStateV1<SpatialWorldFramePayloadV1> WorldFrame { get; }
    public DomainPartitionStateV1<SpatialScopeRegistryPayloadV1> ScopeRegistry { get; }
    public SpatialTerrainGeometryPartitionStateV2 TerrainGeometry { get; }
    public DomainPartitionStateV1<SpatialVoidGeometryPayloadV1> VoidGeometry { get; }
    public DomainPartitionStateV1<SpatialContainmentTopologyPayloadV1> ContainmentTopology { get; }
    public DomainPartitionStateV1<SpatialBoundaryTopologyPayloadV1> BoundaryTopology { get; }
    public DomainPartitionStateV1<SpatialDetailRegionsPayloadV1> DetailRegions { get; }
    public DomainPartitionStateV1<SpatialGeometryLineagePayloadV1> GeometryLineage { get; }

    public static SpatialDomainStateV2 CreateWithTerrain(
        SpatialTerrainGeometryPartitionStateV2 terrainGeometry)
        => new(
            Empty<SpatialWorldFramePayloadV1>(SpatialWorldFramePayloadV1.PartitionId),
            Empty<SpatialScopeRegistryPayloadV1>(SpatialScopeRegistryPayloadV1.PartitionId),
            terrainGeometry,
            Empty<SpatialVoidGeometryPayloadV1>(SpatialVoidGeometryPayloadV1.PartitionId),
            Empty<SpatialContainmentTopologyPayloadV1>(SpatialContainmentTopologyPayloadV1.PartitionId),
            Empty<SpatialBoundaryTopologyPayloadV1>(SpatialBoundaryTopologyPayloadV1.PartitionId),
            Empty<SpatialDetailRegionsPayloadV1>(SpatialDetailRegionsPayloadV1.PartitionId),
            Empty<SpatialGeometryLineagePayloadV1>(SpatialGeometryLineagePayloadV1.PartitionId));

    public SpatialDomainSnapshotMaterialV2 BindSnapshotMaterial(WorldStateV1 frozenState)
        => SpatialDomainSnapshotMaterialV2.Bind(frozenState, this);

    private static DomainPartitionStateV1<TPayload> Empty<TPayload>(string partitionId)
        => new(
            StandardDomainPartitionRegistry.Get(partitionId),
            Array.Empty<DomainRecordEnvelopeV1<TPayload>>());

    private static DomainPartitionStateV1<TPayload> RequireStandard<TPayload>(
        DomainPartitionStateV1<TPayload> partition,
        string partitionId)
    {
        ArgumentNullException.ThrowIfNull(partition);
        if (partition.Identity != StandardDomainPartitionRegistry.Get(partitionId))
            throw new InvalidDataException($"spatial.runtime-state-v2.partition-identity:{partitionId}");
        return partition;
    }
}

/// <summary>
/// Frozen eight-partition Spatial owner material with the registered Terrain record-schema migration.
/// </summary>
public sealed class SpatialDomainSnapshotMaterialV2
{
    private SpatialDomainSnapshotMaterialV2(IEnumerable<IDomainPartitionSnapshotAuthorityV1> authorities)
    {
        var materialized = authorities?.ToArray() ?? throw new ArgumentNullException(nameof(authorities));
        if (materialized.Length != 8)
            throw new InvalidDataException("spatial.snapshot-material-v2.authority-count");

        var byId = new Dictionary<string, IDomainPartitionSnapshotAuthorityV1>(StringComparer.Ordinal);
        foreach (var authority in materialized)
        {
            ArgumentNullException.ThrowIfNull(authority);
            authority.VerifyBoundAuthority();
            if (!string.Equals(authority.Identity.OwnerDomain.Value, "spatial", StringComparison.Ordinal))
                throw new InvalidDataException($"spatial.snapshot-material-v2.foreign-owner:{authority.PartitionId.Value}");
            if (!StandardDomainRecordSchemaMigrationRegistryV1.IsAllowedPartitionIdentity(authority.Identity))
                throw new InvalidDataException($"spatial.snapshot-material-v2.partition-identity:{authority.PartitionId.Value}");
            if (!byId.TryAdd(authority.PartitionId.Value, authority))
                throw new InvalidDataException($"spatial.snapshot-material-v2.duplicate:{authority.PartitionId.Value}");
        }

        WorldFrame = RequireStandard<SpatialWorldFramePayloadV1>(byId, SpatialWorldFramePayloadV1.PartitionId);
        ScopeRegistry = RequireStandard<SpatialScopeRegistryPayloadV1>(byId, SpatialScopeRegistryPayloadV1.PartitionId);
        TerrainGeometry = RequireTerrainV2(byId);
        VoidGeometry = RequireStandard<SpatialVoidGeometryPayloadV1>(byId, SpatialVoidGeometryPayloadV1.PartitionId);
        ContainmentTopology = RequireStandard<SpatialContainmentTopologyPayloadV1>(byId, SpatialContainmentTopologyPayloadV1.PartitionId);
        BoundaryTopology = RequireStandard<SpatialBoundaryTopologyPayloadV1>(byId, SpatialBoundaryTopologyPayloadV1.PartitionId);
        DetailRegions = RequireStandard<SpatialDetailRegionsPayloadV1>(byId, SpatialDetailRegionsPayloadV1.PartitionId);
        GeometryLineage = RequireStandard<SpatialGeometryLineagePayloadV1>(byId, SpatialGeometryLineagePayloadV1.PartitionId);
        Authorities = Array.AsReadOnly(materialized.OrderBy(static value => value.PartitionId.Value, StringComparer.Ordinal).ToArray());
    }

    public DomainPartitionSnapshotAuthorityV1<SpatialWorldFramePayloadV1> WorldFrame { get; }
    public DomainPartitionSnapshotAuthorityV1<SpatialScopeRegistryPayloadV1> ScopeRegistry { get; }
    public SpatialTerrainGeometrySnapshotAuthorityV2 TerrainGeometry { get; }
    public DomainPartitionSnapshotAuthorityV1<SpatialVoidGeometryPayloadV1> VoidGeometry { get; }
    public DomainPartitionSnapshotAuthorityV1<SpatialContainmentTopologyPayloadV1> ContainmentTopology { get; }
    public DomainPartitionSnapshotAuthorityV1<SpatialBoundaryTopologyPayloadV1> BoundaryTopology { get; }
    public DomainPartitionSnapshotAuthorityV1<SpatialDetailRegionsPayloadV1> DetailRegions { get; }
    public DomainPartitionSnapshotAuthorityV1<SpatialGeometryLineagePayloadV1> GeometryLineage { get; }
    public IReadOnlyList<IDomainPartitionSnapshotAuthorityV1> Authorities { get; }

    public static SpatialDomainSnapshotMaterialV2 Bind(
        WorldStateV1 frozenState,
        SpatialDomainStateV2 state)
    {
        ArgumentNullException.ThrowIfNull(frozenState);
        ArgumentNullException.ThrowIfNull(state);
        return new SpatialDomainSnapshotMaterialV2(
        [
            BindStandard(frozenState, state.WorldFrame, SpatialWorldFramePayloadV1.PartitionId, static value => value.CanonicalDigest()),
            BindStandard(frozenState, state.ScopeRegistry, SpatialScopeRegistryPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            new SpatialTerrainGeometrySnapshotAuthorityV2(
                state.TerrainGeometry,
                frozenState.Partitions.Get(SpatialTerrainGeometryRecordSchemaV2.PartitionId).Header),
            BindStandard(frozenState, state.VoidGeometry, SpatialVoidGeometryPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            BindStandard(frozenState, state.ContainmentTopology, SpatialContainmentTopologyPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            BindStandard(frozenState, state.BoundaryTopology, SpatialBoundaryTopologyPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            BindStandard(frozenState, state.DetailRegions, SpatialDetailRegionsPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            BindStandard(frozenState, state.GeometryLineage, SpatialGeometryLineagePayloadV1.PartitionId, static value => value.CanonicalDigest()),
        ]);
    }

    private static DomainPartitionSnapshotAuthorityV1<TPayload> BindStandard<TPayload>(
        WorldStateV1 frozenState,
        DomainPartitionStateV1<TPayload> partition,
        string partitionId,
        Func<TPayload, byte[]> digest)
    {
        if (partition.Identity != StandardDomainPartitionRegistry.Get(partitionId))
            throw new InvalidDataException($"spatial.snapshot-material-v2.partition-identity:{partitionId}");
        return new DomainPartitionSnapshotAuthorityV1<TPayload>(
            partition,
            frozenState.Partitions.Get(partitionId).Header,
            digest);
    }

    private static DomainPartitionSnapshotAuthorityV1<TPayload> RequireStandard<TPayload>(
        IReadOnlyDictionary<string, IDomainPartitionSnapshotAuthorityV1> byId,
        string partitionId)
    {
        if (!byId.TryGetValue(partitionId, out var authority))
            throw new InvalidDataException($"spatial.snapshot-material-v2.missing:{partitionId}");
        return authority as DomainPartitionSnapshotAuthorityV1<TPayload>
            ?? throw new InvalidDataException($"spatial.snapshot-material-v2.payload-type:{partitionId}");
    }

    private static SpatialTerrainGeometrySnapshotAuthorityV2 RequireTerrainV2(
        IReadOnlyDictionary<string, IDomainPartitionSnapshotAuthorityV1> byId)
    {
        if (!byId.TryGetValue(SpatialTerrainGeometryRecordSchemaV2.PartitionId, out var authority))
            throw new InvalidDataException("spatial.snapshot-material-v2.missing:spatial.terrain_geometry");
        return authority as SpatialTerrainGeometrySnapshotAuthorityV2
            ?? throw new InvalidDataException("spatial.snapshot-material-v2.payload-type:spatial.terrain_geometry");
    }
}
