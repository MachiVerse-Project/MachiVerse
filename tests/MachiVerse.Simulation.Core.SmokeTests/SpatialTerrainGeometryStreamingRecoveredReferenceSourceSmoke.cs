using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class SpatialTerrainGeometryStreamingRecoveredReferenceSourceSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        var brickId = OpaqueId128.Parse("00000000000000000000000000000020");
        var rootId = OpaqueId128.Parse("00000000000000000000000000000010");
        var scopeRef = new PartitionRecordRefV1(
            "spatial.scope_registry",
            OpaqueId128.Parse("00000000000000000000000000000030"));

        var brick = SpatialTerrainGeometryRecordMaterialV2.FromTerrainBrick(
            new TerrainBrickV1(
                brickId,
                level: 3,
                new SpatialCellKeyV1(3, 0, 0, 0),
                sampleSpacingMm: 64_000,
                Enumerable.Repeat(0, TerrainBrickV1.SdfSampleCount),
                Enumerable.Repeat((ushort)1, TerrainBrickV1.SurfaceMaterialCount),
                revision: 1),
            createdStep: 0,
            detailLevel: DetailLevelV1.D3BoundarySummary);
        var root = new SpatialTerrainGeometryRecordMaterialV2(
            rootId,
            revision: 1,
            createdStep: 0,
            retiredStep: null,
            detailLevel: DetailLevelV1.D3BoundarySummary,
            lineageRef: null,
            new SpatialTerrainRootPayloadV2(
                scopeRef,
                new PartitionRecordRefV1(SpatialTerrainGeometryRecordSchemaV2.PartitionId, brickId),
                geometryRevision: 1,
                [new StableToken("terrain.rock")],
                Array.Empty<PartitionRecordRefV1>(),
                archiveAnchor: null));

        var partition = new SpatialTerrainGeometryPartitionStateV2([root, brick]);
        var authority = SpatialTerrainGeometrySnapshotAuthorityV2.CreateCanonical(
            partition,
            revision: 1,
            basisStep: 0,
            detailLevel: DetailLevelV1.D3BoundarySummary);
        var section = new SpatialTerrainGeometrySnapshotSectionProviderV2().Create(authority);

        var legacy = new SpatialTerrainGeometryRecoveredReferenceSourceV2(section.Fragments);
        var streaming = new SpatialTerrainGeometryStreamingRecoveredReferenceSourceV2(section.Fragments);

        Require(streaming.ActualItemCount == legacy.ActualItemCount && streaming.ActualItemCount == 2,
            "Streaming Terrain recovered source must preserve exact item count.");
        Require(streaming.RecordIdsCanonical.SequenceEqual(legacy.RecordIdsCanonical),
            "Streaming Terrain recovered source must preserve canonical record identities.");
        Require(streaming.Header.CanonicalDigest.AsSpan().SequenceEqual(legacy.Header.CanonicalDigest),
            "Streaming Terrain recovered source must preserve the recovered partition header digest.");
        Require(streaming.TryGetKind(rootId, out var rootKind) &&
                rootKind == SpatialTerrainGeometryRecoveredRecordKindV2.Root &&
                streaming.TryGetKind(brickId, out var brickKind) &&
                brickKind == SpatialTerrainGeometryRecoveredRecordKindV2.Brick,
            "Streaming Terrain recovered source must retain compact root/brick kind identity.");
        Require(streaming.RootClosures.Count == 1 &&
                streaming.RootClosures[0].RootId == rootId &&
                streaming.RootClosures[0].RootBrickId == brickId,
            "Streaming Terrain recovered source must retain root -> brick closure without brick payload retention.");

        ProveWrongRootTargetKindRejected(authority.Header, rootId, brickId, scopeRef);
    }

    private static void ProveWrongRootTargetKindRejected(
        PartitionStateHeaderV1 header,
        OpaqueId128 rootId,
        OpaqueId128 secondId,
        PartitionRecordRefV1 scopeRef)
    {
        var firstRoot = Root(
            rootId,
            scopeRef,
            new PartitionRecordRefV1(SpatialTerrainGeometryRecordSchemaV2.PartitionId, secondId));
        var secondRoot = Root(
            secondId,
            scopeRef,
            new PartitionRecordRefV1(SpatialTerrainGeometryRecordSchemaV2.PartitionId, rootId));
        var payload = SpatialTerrainGeometrySnapshotFragmentWireV2.Encode(header, [firstRoot, secondRoot]);
        var fragment = new SnapshotSectionFragmentMaterialV1(
            SpatialTerrainGeometryRecordSchemaV2.PartitionId,
            0,
            1,
            rootId.ToBytes(),
            secondId.ToBytes(),
            2,
            payload);

        var rejected = false;
        try
        {
            _ = new SpatialTerrainGeometryStreamingRecoveredReferenceSourceV2([fragment]);
        }
        catch (InvalidDataException ex) when (ex.Message == "persistence.snapshot.terrain-v2-root-brick-kind")
        {
            rejected = true;
        }
        Require(rejected,
            "Streaming Terrain recovered source must reject terrain_root root_brick targets that resolve to another root.");
    }

    private static SpatialTerrainGeometryRecordMaterialV2 Root(
        OpaqueId128 id,
        PartitionRecordRefV1 scopeRef,
        PartitionRecordRefV1 rootBrickRef)
        => new(
            id,
            revision: 1,
            createdStep: 0,
            retiredStep: null,
            detailLevel: DetailLevelV1.D3BoundarySummary,
            lineageRef: null,
            new SpatialTerrainRootPayloadV2(
                scopeRef,
                rootBrickRef,
                geometryRevision: 1,
                [new StableToken("terrain.rock")],
                Array.Empty<PartitionRecordRefV1>(),
                archiveAnchor: null));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
