using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04TerrainGeometryRecordSetV2Initializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        VerifyResolvedBrickTarget();
        VerifyMissingBrickRejects();
        VerifyWrongKindRejects();
    }

    private static void VerifyResolvedBrickTarget()
    {
        var brickId = Id("00000000000000000000000000024300");
        var brick = Brick(brickId);
        var root = Root(Id("00000000000000000000000000024301"), brickId);
        var set = new SpatialTerrainGeometryRecordSetV2([root, brick]);
        Require(set.RecordsCanonical.Count == 2, "terrain v2 record set must retain both root and brick records.");
        Require(set.RecordsCanonical[0].RecordId.CompareTo(set.RecordsCanonical[1].RecordId) < 0,
            "terrain v2 record set must iterate in canonical record-id order.");
        Require(set.TryGet(brickId, out var target) && target?.Payload is SpatialTerrainBrickPayloadV2,
            "terrain root target must resolve to an actual terrain_brick arm.");
    }

    private static void VerifyMissingBrickRejects()
    {
        var missing = Id("00000000000000000000000000024400");
        ExpectReject(
            () => _ = new SpatialTerrainGeometryRecordSetV2([Root(Id("00000000000000000000000000024401"), missing)]),
            "spatial.terrain-v2.root-brick-missing");
    }

    private static void VerifyWrongKindRejects()
    {
        var targetId = Id("00000000000000000000000000024500");
        var targetRoot = Root(targetId, Id("00000000000000000000000000024502"));
        var sourceRoot = Root(Id("00000000000000000000000000024501"), targetId);
        var unrelatedBrick = Brick(Id("00000000000000000000000000024502"));
        ExpectReject(
            () => _ = new SpatialTerrainGeometryRecordSetV2([sourceRoot, targetRoot, unrelatedBrick]),
            "spatial.terrain-v2.root-brick-kind");
    }

    private static SpatialTerrainGeometryRecordMaterialV2 Root(OpaqueId128 recordId, OpaqueId128 brickId)
        => new(
            recordId,
            1,
            0,
            null,
            DetailLevelV1.D0Entity,
            null,
            new SpatialTerrainRootPayloadV2(
                new PartitionRecordRefV1("spatial.scope_registry", Id("00000000000000000000000000024990")),
                new PartitionRecordRefV1(SpatialTerrainGeometryRecordSchemaV2.PartitionId, brickId),
                1,
                Array.Empty<StableToken>(),
                Array.Empty<PartitionRecordRefV1>(),
                null));

    private static SpatialTerrainGeometryRecordMaterialV2 Brick(OpaqueId128 recordId)
        => SpatialTerrainGeometryRecordMaterialV2.FromTerrainBrick(
            new TerrainBrickV1(
                recordId,
                0,
                new SpatialCellKeyV1(0, 0, 0, 0),
                250,
                Enumerable.Repeat(0, TerrainBrickV1.SdfSampleCount),
                Enumerable.Repeat((ushort)0, TerrainBrickV1.SurfaceMaterialCount),
                1),
            0,
            DetailLevelV1.D0Entity);

    private static OpaqueId128 Id(string value) => OpaqueId128.Parse(value);

    private static void ExpectReject(Action action, string code)
    {
        try
        {
            action();
            throw new InvalidOperationException($"Expected rejection containing '{code}'.");
        }
        catch (InvalidDataException ex) when (ex.Message.Contains(code, StringComparison.Ordinal))
        {
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
