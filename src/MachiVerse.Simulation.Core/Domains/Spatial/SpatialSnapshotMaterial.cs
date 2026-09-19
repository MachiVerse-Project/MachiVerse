using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains.Spatial;

public sealed class SpatialDomainStateV1
{
    public SpatialDomainStateV1(
        DomainPartitionStateV1<SpatialWorldFramePayloadV1> worldFrame,
        DomainPartitionStateV1<SpatialScopeRegistryPayloadV1> scopeRegistry,
        DomainPartitionStateV1<SpatialTerrainGeometryPayloadV1> terrainGeometry,
        DomainPartitionStateV1<SpatialVoidGeometryPayloadV1> voidGeometry,
        DomainPartitionStateV1<SpatialContainmentTopologyPayloadV1> containmentTopology,
        DomainPartitionStateV1<SpatialBoundaryTopologyPayloadV1> boundaryTopology,
        DomainPartitionStateV1<SpatialDetailRegionsPayloadV1> detailRegions,
        DomainPartitionStateV1<SpatialGeometryLineagePayloadV1> geometryLineage)
    {
        WorldFrame = RequireIdentity(worldFrame, SpatialWorldFramePayloadV1.PartitionId);
        ScopeRegistry = RequireIdentity(scopeRegistry, SpatialScopeRegistryPayloadV1.PartitionId);
        TerrainGeometry = RequireIdentity(terrainGeometry, SpatialTerrainGeometryPayloadV1.PartitionId);
        VoidGeometry = RequireIdentity(voidGeometry, SpatialVoidGeometryPayloadV1.PartitionId);
        ContainmentTopology = RequireIdentity(containmentTopology, SpatialContainmentTopologyPayloadV1.PartitionId);
        BoundaryTopology = RequireIdentity(boundaryTopology, SpatialBoundaryTopologyPayloadV1.PartitionId);
        DetailRegions = RequireIdentity(detailRegions, SpatialDetailRegionsPayloadV1.PartitionId);
        GeometryLineage = RequireIdentity(geometryLineage, SpatialGeometryLineagePayloadV1.PartitionId);
    }

    public DomainPartitionStateV1<SpatialWorldFramePayloadV1> WorldFrame { get; }
    public DomainPartitionStateV1<SpatialScopeRegistryPayloadV1> ScopeRegistry { get; }
    public DomainPartitionStateV1<SpatialTerrainGeometryPayloadV1> TerrainGeometry { get; }
    public DomainPartitionStateV1<SpatialVoidGeometryPayloadV1> VoidGeometry { get; }
    public DomainPartitionStateV1<SpatialContainmentTopologyPayloadV1> ContainmentTopology { get; }
    public DomainPartitionStateV1<SpatialBoundaryTopologyPayloadV1> BoundaryTopology { get; }
    public DomainPartitionStateV1<SpatialDetailRegionsPayloadV1> DetailRegions { get; }
    public DomainPartitionStateV1<SpatialGeometryLineagePayloadV1> GeometryLineage { get; }

    public static SpatialDomainStateV1 CreateEmpty()
        => new(
            Empty<SpatialWorldFramePayloadV1>(SpatialWorldFramePayloadV1.PartitionId),
            Empty<SpatialScopeRegistryPayloadV1>(SpatialScopeRegistryPayloadV1.PartitionId),
            Empty<SpatialTerrainGeometryPayloadV1>(SpatialTerrainGeometryPayloadV1.PartitionId),
            Empty<SpatialVoidGeometryPayloadV1>(SpatialVoidGeometryPayloadV1.PartitionId),
            Empty<SpatialContainmentTopologyPayloadV1>(SpatialContainmentTopologyPayloadV1.PartitionId),
            Empty<SpatialBoundaryTopologyPayloadV1>(SpatialBoundaryTopologyPayloadV1.PartitionId),
            Empty<SpatialDetailRegionsPayloadV1>(SpatialDetailRegionsPayloadV1.PartitionId),
            Empty<SpatialGeometryLineagePayloadV1>(SpatialGeometryLineagePayloadV1.PartitionId));

    public SpatialDomainSnapshotMaterialV1 BindSnapshotMaterial(WorldStateV1 frozenState)
        => SpatialDomainSnapshotMaterialV1.Bind(frozenState, this);

    private static DomainPartitionStateV1<TPayload> Empty<TPayload>(string partitionId)
        => new(
            StandardDomainPartitionRegistry.Get(partitionId),
            Array.Empty<DomainRecordEnvelopeV1<TPayload>>());

    private static DomainPartitionStateV1<TPayload> RequireIdentity<TPayload>(
        DomainPartitionStateV1<TPayload> partition,
        string partitionId)
    {
        ArgumentNullException.ThrowIfNull(partition);
        if (partition.Identity != StandardDomainPartitionRegistry.Get(partitionId))
            throw new InvalidDataException($"spatial.runtime-state.partition-identity:{partitionId}");
        return partition;
    }
}

public sealed class SpatialDomainSnapshotMaterialV1
{
    private SpatialDomainSnapshotMaterialV1(IEnumerable<IDomainPartitionSnapshotAuthorityV1> authorities)
    {
        var materialized = authorities?.ToArray() ?? throw new ArgumentNullException(nameof(authorities));
        if (materialized.Length != 8)
            throw new InvalidDataException("spatial.snapshot-material.authority-count");

        var byId = new Dictionary<string, IDomainPartitionSnapshotAuthorityV1>(StringComparer.Ordinal);
        foreach (var authority in materialized)
        {
            ArgumentNullException.ThrowIfNull(authority);
            authority.VerifyBoundAuthority();
            if (!string.Equals(authority.Identity.OwnerDomain.Value, "spatial", StringComparison.Ordinal))
                throw new InvalidDataException($"spatial.snapshot-material.foreign-owner:{authority.PartitionId.Value}");
            if (!byId.TryAdd(authority.PartitionId.Value, authority))
                throw new InvalidDataException($"spatial.snapshot-material.duplicate:{authority.PartitionId.Value}");
        }

        WorldFrame = Require<SpatialWorldFramePayloadV1>(byId, SpatialWorldFramePayloadV1.PartitionId);
        ScopeRegistry = Require<SpatialScopeRegistryPayloadV1>(byId, SpatialScopeRegistryPayloadV1.PartitionId);
        TerrainGeometry = Require<SpatialTerrainGeometryPayloadV1>(byId, SpatialTerrainGeometryPayloadV1.PartitionId);
        VoidGeometry = Require<SpatialVoidGeometryPayloadV1>(byId, SpatialVoidGeometryPayloadV1.PartitionId);
        ContainmentTopology = Require<SpatialContainmentTopologyPayloadV1>(byId, SpatialContainmentTopologyPayloadV1.PartitionId);
        BoundaryTopology = Require<SpatialBoundaryTopologyPayloadV1>(byId, SpatialBoundaryTopologyPayloadV1.PartitionId);
        DetailRegions = Require<SpatialDetailRegionsPayloadV1>(byId, SpatialDetailRegionsPayloadV1.PartitionId);
        GeometryLineage = Require<SpatialGeometryLineagePayloadV1>(byId, SpatialGeometryLineagePayloadV1.PartitionId);
        Authorities = Array.AsReadOnly(materialized.OrderBy(static value => value.PartitionId.Value, StringComparer.Ordinal).ToArray());
    }

    public DomainPartitionSnapshotAuthorityV1<SpatialWorldFramePayloadV1> WorldFrame { get; }
    public DomainPartitionSnapshotAuthorityV1<SpatialScopeRegistryPayloadV1> ScopeRegistry { get; }
    public DomainPartitionSnapshotAuthorityV1<SpatialTerrainGeometryPayloadV1> TerrainGeometry { get; }
    public DomainPartitionSnapshotAuthorityV1<SpatialVoidGeometryPayloadV1> VoidGeometry { get; }
    public DomainPartitionSnapshotAuthorityV1<SpatialContainmentTopologyPayloadV1> ContainmentTopology { get; }
    public DomainPartitionSnapshotAuthorityV1<SpatialBoundaryTopologyPayloadV1> BoundaryTopology { get; }
    public DomainPartitionSnapshotAuthorityV1<SpatialDetailRegionsPayloadV1> DetailRegions { get; }
    public DomainPartitionSnapshotAuthorityV1<SpatialGeometryLineagePayloadV1> GeometryLineage { get; }
    public IReadOnlyList<IDomainPartitionSnapshotAuthorityV1> Authorities { get; }

    public static SpatialDomainSnapshotMaterialV1 Bind(WorldStateV1 frozenState, SpatialDomainStateV1 state)
    {
        ArgumentNullException.ThrowIfNull(frozenState);
        ArgumentNullException.ThrowIfNull(state);
        return new SpatialDomainSnapshotMaterialV1(
        [
            Bind(frozenState, state.WorldFrame, SpatialWorldFramePayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.ScopeRegistry, SpatialScopeRegistryPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.TerrainGeometry, SpatialTerrainGeometryPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.VoidGeometry, SpatialVoidGeometryPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.ContainmentTopology, SpatialContainmentTopologyPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.BoundaryTopology, SpatialBoundaryTopologyPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.DetailRegions, SpatialDetailRegionsPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.GeometryLineage, SpatialGeometryLineagePayloadV1.PartitionId, static value => value.CanonicalDigest()),
        ]);
    }

    private static DomainPartitionSnapshotAuthorityV1<TPayload> Bind<TPayload>(
        WorldStateV1 frozenState,
        DomainPartitionStateV1<TPayload> partition,
        string partitionId,
        Func<TPayload, byte[]> digest)
    {
        if (partition.Identity != StandardDomainPartitionRegistry.Get(partitionId))
            throw new InvalidDataException($"spatial.snapshot-material.partition-identity:{partitionId}");
        return new DomainPartitionSnapshotAuthorityV1<TPayload>(
            partition,
            frozenState.Partitions.Get(partitionId).Header,
            digest);
    }

    private static DomainPartitionSnapshotAuthorityV1<TPayload> Require<TPayload>(
        IReadOnlyDictionary<string, IDomainPartitionSnapshotAuthorityV1> byId,
        string partitionId)
    {
        if (!byId.TryGetValue(partitionId, out var authority))
            throw new InvalidDataException($"spatial.snapshot-material.missing:{partitionId}");
        return authority as DomainPartitionSnapshotAuthorityV1<TPayload>
            ?? throw new InvalidDataException($"spatial.snapshot-material.payload-type:{partitionId}");
    }
}
