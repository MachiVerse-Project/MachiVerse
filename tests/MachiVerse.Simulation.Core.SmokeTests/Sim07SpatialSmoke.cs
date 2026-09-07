using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.WorldState;

internal static class Sim07SpatialSmoke
{
    internal static void Run()
    {
        Require(TerrainBrickV1.Classify(-1) == SignedDistanceClassV1.Solid,
            "domain.spatial.sdf.sign: negative SDF must be solid.");
        Require(TerrainBrickV1.Classify(0) == SignedDistanceClassV1.Boundary,
            "domain.spatial.sdf.sign: zero SDF must be boundary.");
        Require(TerrainBrickV1.Classify(1) == SignedDistanceClassV1.Void,
            "domain.spatial.sdf.sign: positive SDF must be void.");

        var samples = Enumerable.Repeat(1, TerrainBrickV1.SdfSampleCount).ToArray();
        SetSample(samples, 0, 0, 0, 0);
        SetSample(samples, 1, 0, 0, 8);
        SetSample(samples, 0, 1, 0, 16);
        SetSample(samples, 1, 1, 0, 24);
        SetSample(samples, 0, 0, 1, 32);
        SetSample(samples, 1, 0, 1, 40);
        SetSample(samples, 0, 1, 1, 48);
        SetSample(samples, 1, 1, 1, 56);

        // Multiple sign changes along the same XY column prove that the representation is not a height field.
        SetSample(samples, 2, 2, 0, -10);
        SetSample(samples, 2, 2, 1, 10);
        SetSample(samples, 2, 2, 2, -10);
        SetSample(samples, 2, 2, 3, 10);

        var brick = new TerrainBrickV1(
            OpaqueId128.Parse("00000000000000000000000000000701"),
            level: 0,
            new SpatialCellKeyV1(0, 0, 0, 0),
            sampleSpacingMm: 250,
            samples,
            new ushort[TerrainBrickV1.SurfaceMaterialCount],
            revision: 1);

        var half = FixedQ32_32.FromRatio(1, 2);
        var interpolated = brick.InterpolateCellMm(0, 0, 0, half, half, half);
        Require(interpolated.RoundToInteger() == 28,
            "domain.spatial.sdf.interpolation: Q32.32 trilinear interpolation golden vector mismatch.");

        Require(TerrainOctreeV1.CanonicalChildTraversal.SequenceEqual(Enumerable.Range(0, 8)),
            "domain.spatial.sdf.octree-order: octree traversal must be 0..7.");
        Require(TerrainOctreeV1.ChildIndex(false, false, false) == 0 &&
                TerrainOctreeV1.ChildIndex(true, false, false) == 1 &&
                TerrainOctreeV1.ChildIndex(false, true, false) == 2 &&
                TerrainOctreeV1.ChildIndex(false, false, true) == 4 &&
                TerrainOctreeV1.ChildIndex(true, true, true) == 7,
            "domain.spatial.sdf.octree-order: child bit mapping mismatch.");

        var vertical = brick.VerticalSampleClasses(2, 2);
        Require(vertical[0] == SignedDistanceClassV1.Solid &&
                vertical[1] == SignedDistanceClassV1.Void &&
                vertical[2] == SignedDistanceClassV1.Solid &&
                vertical[3] == SignedDistanceClassV1.Void,
            "domain.spatial.cave-overhang: same XY must support multiple vertical solid/void surfaces.");

        var cellOrder = new[]
        {
            new SpatialCellKeyV1(0, 0, 0, 0),
            new SpatialCellKeyV1(0, -1, 0, 0),
            new SpatialCellKeyV1(1, int.MinValue, 0, 0),
            new SpatialCellKeyV1(0, int.MinValue, 0, 0),
        }.OrderBy(static key => key).ToArray();
        Require(cellOrder[0] == new SpatialCellKeyV1(0, int.MinValue, 0, 0) &&
                cellOrder[1] == new SpatialCellKeyV1(0, -1, 0, 0) &&
                cellOrder[2] == new SpatialCellKeyV1(0, 0, 0, 0) &&
                cellOrder[3].Level == 1,
            "SpatialCellKey canonical signed-bias order mismatch.");

        var subjectA = new PartitionRecordRefV1(
            "physical.presence",
            OpaqueId128.Parse("00000000000000000000000000000710"));
        var subjectB = new PartitionRecordRefV1(
            "physical.presence",
            OpaqueId128.Parse("00000000000000000000000000000711"));
        var scopeA = new PartitionRecordRefV1(
            "spatial.scope_registry",
            OpaqueId128.Parse("00000000000000000000000000000720"));
        var scopeB = new PartitionRecordRefV1(
            "spatial.scope_registry",
            OpaqueId128.Parse("00000000000000000000000000000721"));
        var relationA = new SpatialContainmentRelationV1(subjectA, scopeB, new StableToken("inside"), 4);
        var relationB = new SpatialContainmentRelationV1(subjectB, scopeA, new StableToken("inside"), 3);
        var canonicalA = new SpatialContainmentIndexV1([relationA, relationB]);
        var canonicalB = new SpatialContainmentIndexV1([relationB, relationA]);
        Require(canonicalA.CanonicalRelations.SequenceEqual(canonicalB.CanonicalRelations),
            "domain.spatial.containment: relation order must be input-permutation independent.");
        Require(canonicalA.ForSubject(subjectA).Single() == relationA,
            "domain.spatial.containment: subject lookup returned the wrong canonical relation.");

        SpatialGeometryRevisionV1.RequireCurrent(7, 7);
        RequireReject(
            () => SpatialGeometryRevisionV1.RequireCurrent(6, 7),
            "spatial.geometry-stale-revision");
    }

    private static void SetSample(int[] samples, int x, int y, int z, int value)
        => samples[((z * TerrainBrickV1.SamplesPerAxis) + y) * TerrainBrickV1.SamplesPerAxis + x] = value;

    private static void RequireReject(Action action, string expectedMessage)
    {
        try
        {
            action();
        }
        catch (InvalidDataException ex) when (ex.Message == expectedMessage)
        {
            return;
        }
        throw new InvalidOperationException($"Expected SIM-07 Spatial rejection: {expectedMessage}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
