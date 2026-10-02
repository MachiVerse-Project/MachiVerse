using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Canonical perf.reference.v1 authority for the 64x64 regional TileScope set.
/// IDs are independently derivable at Step 0 so reciprocal TerrainRoot references do not create
/// materialization-order dependence.
/// </summary>
public static class Qa04SpatialTileScopeAuthorityV1
{
    public const int CanonicalScopeCount = 4_096;
    public const ulong InitialRevision = 1;
    public const uint InitialScopeFlags = 0;

    private static readonly StableToken SpatialDomain = new("spatial");
    private static readonly StableToken CreationKind = new("perf.tile-scope");
    private static readonly StableToken ScopeClass = new("perf.regional-tile");

    public static void ValidateCanonicalContract()
    {
        Qa04ReferenceLoadV1.ValidateCanonicalContract();
        Qa04TerrainRootMaterializerV1.ValidateCanonicalContract();

        if (CanonicalScopeCount != Qa04ReferenceLoadV1.RegionalTileCount ||
            InitialRevision != 1 ||
            InitialScopeFlags != 0 ||
            CreationKind.Value != "perf.tile-scope" ||
            ScopeClass.Value != "perf.regional-tile")
            throw new InvalidDataException("qa04.spatial.tile-scope-contract-drift");

        var identity = StandardDomainPartitionRegistry.Get(SpatialScopeRegistryPayloadV1.PartitionId);
        if (identity.OwnerDomain.Value != "spatial")
            throw new InvalidDataException("qa04.spatial.tile-scope-owner-drift");
    }

    public static OpaqueId128 ScopeId(ushort tileIndex)
    {
        ValidateTileIndex(tileIndex);
        return DerivedIdentity.DeriveEntityId(
            Qa04ReferenceLoadV1.WorldId,
            creationStep: 0,
            SpatialDomain,
            OpaqueId128.Zero,
            CreationKind,
            tileIndex);
    }

    public static PartitionRecordRefV1 ScopeRef(ushort tileIndex)
        => new(SpatialScopeRegistryPayloadV1.PartitionId, ScopeId(tileIndex));

    public static DomainRecordEnvelopeV1<SpatialScopeRegistryPayloadV1> MaterializeTile(ushort tileIndex)
    {
        ValidateCanonicalContract();
        ValidateTileIndex(tileIndex);
        return MaterializeTileValidated(
            tileIndex,
            StandardDomainPartitionRegistry.Get(SpatialScopeRegistryPayloadV1.PartitionId));
    }

    public static DomainPartitionStateV1<SpatialScopeRegistryPayloadV1> MaterializeCanonical()
    {
        ValidateCanonicalContract();
        var identity = StandardDomainPartitionRegistry.Get(SpatialScopeRegistryPayloadV1.PartitionId);
        var records = new DomainRecordEnvelopeV1<SpatialScopeRegistryPayloadV1>[CanonicalScopeCount];
        for (ushort tile = 0; tile < Qa04ReferenceLoadV1.RegionalTileCount; tile++)
            records[tile] = MaterializeTileValidated(tile, identity);
        return new DomainPartitionStateV1<SpatialScopeRegistryPayloadV1>(identity, records);
    }

    public static void ValidateTerrainReciprocalClosure(
        IEnumerable<SpatialTerrainGeometryRecordMaterialV2> terrainRecords)
    {
        ArgumentNullException.ThrowIfNull(terrainRecords);
        ValidateCanonicalContract();

        var roots = terrainRecords
            .Where(static record => record.Payload is SpatialTerrainRootPayloadV2)
            .ToDictionary(static record => record.RecordId);
        if (roots.Count != Qa04ReferenceLoadV1.RegionalTileCount)
            throw new InvalidDataException("qa04.spatial.tile-scope-terrain-root-count");

        for (ushort tile = 0; tile < Qa04ReferenceLoadV1.RegionalTileCount; tile++)
        {
            var rootId = Qa04TerrainRootMaterializerV1.RootId(tile);
            if (!roots.TryGetValue(rootId, out var root) || root.Payload is not SpatialTerrainRootPayloadV2 payload)
                throw new InvalidDataException("qa04.spatial.tile-scope-terrain-root-missing");
            if (payload.ScopeRef != ScopeRef(tile))
                throw new InvalidDataException("qa04.spatial.tile-scope-terrain-root-scope-mismatch");
        }
    }

    private static DomainRecordEnvelopeV1<SpatialScopeRegistryPayloadV1> MaterializeTileValidated(
        ushort tileIndex,
        DomainPartitionIdentityV1 identity)
        => new(
            ScopeId(tileIndex),
            identity.RecordSchema,
            InitialRevision,
            createdStep: 0,
            retiredStep: null,
            DetailLevelV1.D2RegionalAggregate,
            lineageRef: null,
            new SpatialScopeRegistryPayloadV1(
                ScopeClass,
                new PartitionRecordRefV1(
                    SpatialTerrainGeometryRecordSchemaV2.PartitionId,
                    Qa04TerrainRootMaterializerV1.RootId(tileIndex)),
                ParentScope: null,
                ActiveFrom: 0,
                RetiredAt: null,
                ScopeFlags: InitialScopeFlags));

    private static void ValidateTileIndex(ushort tileIndex)
    {
        if (tileIndex >= Qa04ReferenceLoadV1.RegionalTileCount)
            throw new ArgumentOutOfRangeException(nameof(tileIndex));
    }
}
