using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04TerrainRootMaterializationSmoke
{
    private static readonly StableToken SpatialDomain = new("spatial");
    private static readonly StableToken ScopeKind = new("smoke.tile-scope");

    [ModuleInitializer]
    internal static void Run()
    {
        Qa04TerrainRootMaterializerV1.ValidateCanonicalContract();

        var corner = Qa04TerrainRootMaterializerV1.MaterializeTile(0, ScopeForTile);
        var cornerRoot = corner.Root.Payload as SpatialTerrainRootPayloadV2
            ?? throw new InvalidOperationException("Terrain tile root must use terrain_root payload.");
        var cornerAnchor = corner.Anchor.Payload as SpatialTerrainBrickPayloadV2
            ?? throw new InvalidOperationException("Terrain tile anchor must use terrain_brick payload.");
        Require(corner.Root.RecordId == Qa04TerrainRootMaterializerV1.RootId(0) &&
                corner.Anchor.RecordId == Qa04TerrainRootMaterializerV1.AnchorId(0),
            "Terrain root/anchor Step0 identity drifted.");
        Require(corner.Root.Revision == 1 && corner.Root.CreatedStep == 0 &&
                corner.Root.RetiredStep is null && corner.Root.LineageRef is null &&
                corner.Anchor.Revision == 1 && corner.Anchor.CreatedStep == 0 &&
                corner.Anchor.RetiredStep is null && corner.Anchor.LineageRef is null,
            "Terrain root/anchor genesis lifecycle drifted.");
        Require(cornerRoot.GeometryRevision == 1 &&
                cornerRoot.RootBrickRef.RecordId == corner.Anchor.RecordId &&
                cornerRoot.RootBrickRef.PartitionId.Value == SpatialTerrainGeometryRecordSchemaV2.PartitionId,
            "Terrain root must target its canonical D3 anchor.");
        Require(cornerRoot.SurfaceClasses.SequenceEqual(Qa04TerrainCanonicalContentSourceV1.SurfaceClasses),
            "Terrain root surface-class vocabulary drifted.");
        Require(cornerRoot.ConnectivityRefs.Count == 2,
            "Corner terrain root must have exactly E/S connectivity.");
        Require(cornerAnchor.Level == 3 && cornerAnchor.SampleSpacingMm == 64_000 &&
                cornerAnchor.SdfMm.Count == TerrainBrickV1.SdfSampleCount &&
                cornerAnchor.SurfaceMaterialIds.Count == TerrainBrickV1.SurfaceMaterialCount,
            "Terrain D3 anchor payload drifted.");

        const ushort interiorTile = 65;
        var interior = Qa04TerrainRootMaterializerV1.MaterializeTile(interiorTile, ScopeForTile);
        var interiorRoot = interior.Root.Payload as SpatialTerrainRootPayloadV2
            ?? throw new InvalidOperationException("Interior terrain root must use terrain_root payload.");
        Require(interiorRoot.ConnectivityRefs.Count == 4,
            "Interior terrain root must have N/E/S/W connectivity.");
        Require(interiorRoot.ConnectivityRefs.Select(static reference => reference.RecordId).Distinct().Count() == 4 &&
                interiorRoot.ConnectivityRefs.All(static reference => reference.PartitionId.Value == SpatialTerrainGeometryRecordSchemaV2.PartitionId),
            "Terrain root connectivity must target four distinct terrain records in the owning partition.");
        Require(interiorRoot.ConnectivityRefs.SequenceEqual(
                interiorRoot.ConnectivityRefs.OrderBy(static reference => reference.RecordId)),
            "Terrain root connectivity refs must be canonical RecordId order.");

        VerifyAllRootTopology();
    }

    private static void VerifyAllRootTopology()
    {
        var rootIds = new HashSet<OpaqueId128>();
        var anchorIds = new HashSet<OpaqueId128>();
        var expectedRootIds = Enumerable.Range(0, Qa04ReferenceLoadV1.RegionalTileCount)
            .Select(static tile => Qa04TerrainRootMaterializerV1.RootId(checked((ushort)tile)))
            .ToHashSet();

        for (ushort tile = 0; tile < Qa04ReferenceLoadV1.RegionalTileCount; tile++)
        {
            var rootId = Qa04TerrainRootMaterializerV1.RootId(tile);
            var anchorId = Qa04TerrainRootMaterializerV1.AnchorId(tile);
            Require(rootIds.Add(rootId), "Terrain root ids must be unique across all 4096 tiles.");
            Require(anchorIds.Add(anchorId), "Terrain D3 anchor ids must be unique across all 4096 tiles.");
            Require(rootId != anchorId && !anchorIds.Contains(rootId),
                "Terrain root and D3 anchor identity spaces must not collide.");

            var root = Qa04TerrainRootMaterializerV1.CreateRoot(tile, ScopeForTile(tile));
            var payload = root.Payload as SpatialTerrainRootPayloadV2
                ?? throw new InvalidOperationException("Canonical terrain root must use terrain_root payload.");
            var row = tile / Qa04ReferenceLoadV1.RegionalTileColumns;
            var column = tile % Qa04ReferenceLoadV1.RegionalTileColumns;
            var expectedDegree = (row > 0 ? 1 : 0) +
                                 (row + 1 < Qa04ReferenceLoadV1.RegionalTileRows ? 1 : 0) +
                                 (column > 0 ? 1 : 0) +
                                 (column + 1 < Qa04ReferenceLoadV1.RegionalTileColumns ? 1 : 0);
            Require(payload.ConnectivityRefs.Count == expectedDegree,
                "Terrain root connectivity degree must match the rectangular tile lattice.");
            Require(payload.ConnectivityRefs.All(reference =>
                    reference.PartitionId.Value == SpatialTerrainGeometryRecordSchemaV2.PartitionId &&
                    expectedRootIds.Contains(reference.RecordId)),
                "Every terrain root connectivity ref must close onto an actual canonical terrain_root id.");
            Require(payload.RootBrickRef.RecordId == anchorId,
                "Every terrain root must target its tile's canonical D3 anchor id.");
        }

        Require(rootIds.Count == Qa04ReferenceLoadV1.RegionalTileCount &&
                anchorIds.Count == Qa04ReferenceLoadV1.RegionalTileCount &&
                !rootIds.Overlaps(anchorIds),
            "Terrain root/anchor cardinality or identity disjointness drifted.");
    }

    private static PartitionRecordRefV1 ScopeForTile(ushort tile)
        => new(
            SpatialScopeRegistryPayloadV1.PartitionId,
            DerivedIdentity.DeriveEntityId(
                Qa04ReferenceLoadV1.WorldId,
                creationStep: 0,
                SpatialDomain,
                OpaqueId128.Zero,
                ScopeKind,
                tile));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
