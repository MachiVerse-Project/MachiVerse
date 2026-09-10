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
        Require(corner.Root.Payload is SpatialTerrainRootPayloadV2 cornerRoot,
            "Terrain tile root must use terrain_root payload.");
        Require(corner.Anchor.Payload is SpatialTerrainBrickPayloadV2 cornerAnchor,
            "Terrain tile anchor must use terrain_brick payload.");
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
        Require(interior.Root.Payload is SpatialTerrainRootPayloadV2 interiorRoot && interiorRoot.ConnectivityRefs.Count == 4,
            "Interior terrain root must have N/E/S/W connectivity.");
        Require(interiorRoot.ConnectivityRefs.Select(static reference => reference.RecordId).Distinct().Count() == 4 &&
                interiorRoot.ConnectivityRefs.All(static reference => reference.PartitionId.Value == SpatialTerrainGeometryRecordSchemaV2.PartitionId),
            "Terrain root connectivity must target four distinct terrain records in the owning partition.");
        Require(interiorRoot.ConnectivityRefs.SequenceEqual(
                interiorRoot.ConnectivityRefs.OrderBy(static reference => reference.RecordId)),
            "Terrain root connectivity refs must be canonical RecordId order.");

        Require(Qa04TerrainRootMaterializerV1.RootId(0) != Qa04TerrainRootMaterializerV1.AnchorId(0) &&
                Qa04TerrainRootMaterializerV1.RootId(0) != Qa04TerrainRootMaterializerV1.RootId(1),
            "Terrain root/anchor identities must be distinct across kinds and tiles.");
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
