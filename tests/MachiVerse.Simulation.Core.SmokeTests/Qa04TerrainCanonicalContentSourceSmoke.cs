using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Performance;

internal static class Qa04TerrainCanonicalContentSourceSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04TerrainCanonicalContentSourceV1.ValidateCanonicalContract();

        Require(Qa04TerrainCanonicalContentSourceV1.FloorDiv(-1, 2_000) == -1,
            "Terrain floor_div must round toward negative infinity.");
        Require(Qa04TerrainCanonicalContentSourceV1.FloorDiv(-2_000, 2_000) == -1,
            "Terrain floor_div must preserve exact negative quotient.");
        Require(Qa04TerrainCanonicalContentSourceV1.FloorDiv(-2_001, 2_000) == -2,
            "Terrain floor_div must round non-exact negative quotient downward.");

        var h0 = Qa04TerrainCanonicalContentSourceV1.HeightMm(0, 0);
        var h1 = Qa04TerrainCanonicalContentSourceV1.HeightMm(0, 0);
        Require(h0 == h1 && h0 is >= -4_000 and <= 4_000,
            "Terrain canonical height must be deterministic and remain in the normative range.");

        var first = Qa04ReferenceLoadV1.Record(new StableToken("spatial.hot-terrain-brick"), 0);
        var sameTile = FindSameTile(first);
        var source = new Qa04TerrainCanonicalContentSourceV1();
        var firstBrick = source.CreateBrick(first);
        var secondBrick = source.CreateBrick(sameTile);

        Require(firstBrick.BrickId == first.RecordId && secondBrick.BrickId == sameTile.RecordId,
            "Terrain canonical content must preserve descriptor RecordId exactly.");
        Require(firstBrick.Level == 0 && firstBrick.SampleSpacingMillimeters == 250 && firstBrick.Revision == 1,
            "Terrain canonical hot brick envelope drifted.");
        Require(firstBrick.SdfMillimeters.Count == TerrainBrickV1.SdfSampleCount &&
                firstBrick.SurfaceMaterialIds.Count == TerrainBrickV1.SurfaceMaterialCount,
            "Terrain canonical hot brick sample cardinality drifted.");
        Require(firstBrick.SurfaceMaterialIds.All(static material => material <= Qa04TerrainCanonicalContentSourceV1.MaterialSediment),
            "Terrain canonical material id is outside the normative 0..3 vocabulary.");
        Require(firstBrick.CellOrigin != secondBrick.CellOrigin,
            "Two canonical hot descriptors in one tile must not collide on cell origin.");

        var materialized = Qa04TerrainBrickDescriptorMaterializerV1.MaterializeRecord(first, source);
        Require(materialized.RecordId == first.RecordId &&
                materialized.Revision == 1 &&
                materialized.CreatedStep == 0 &&
                materialized.RetiredStep is null &&
                materialized.LineageRef is null &&
                materialized.Payload.RecordKind == SpatialTerrainGeometryRecordSchemaV2.TerrainBrickKind &&
                materialized.Payload.Brick.BrickId == first.RecordId,
            "Terrain canonical content must pass the production descriptor materializer without identity drift.");
    }

    private static Qa04ReferenceRecordV1 FindSameTile(Qa04ReferenceRecordV1 first)
    {
        for (ulong ordinal = 1; ordinal < Qa04TerrainBrickDescriptorMaterializerV1.CanonicalTerrainBrickCount; ordinal++)
        {
            var candidate = Qa04ReferenceLoadV1.Record(new StableToken("spatial.hot-terrain-brick"), ordinal);
            if (candidate.RegionalTileIndex == first.RegionalTileIndex && candidate.RecordId != first.RecordId)
                return candidate;
        }
        throw new InvalidOperationException("Could not find a second canonical terrain descriptor in the first tile.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
