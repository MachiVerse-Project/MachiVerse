using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Domains.Spatial;

internal static class Sim08CollisionSmoke
{
    internal static void Run()
    {
        VerifyGjk();
        VerifyEpa();
        VerifyTerrainContact();
        VerifyContactOrdering();
        VerifyNonConvergenceBoundary();
    }

    private static void VerifyGjk()
    {
        var a = Cube(0, 0, 0, 1000);
        var overlapping = Cube(1500, 0, 0, 1000);
        var separated = Cube(3000, 0, 0, 1000);

        var hit = DeterministicGjkV1.Classify(a, overlapping);
        var miss = DeterministicGjkV1.Classify(a, separated);

        Require(hit.Classification == GjkClassificationV1.Intersecting &&
                hit.Iterations <= DeterministicGjkV1.MaxIterations,
            "domain.physical.gjk: overlapping convex corpus pair must intersect within the fixed bound.");
        Require(miss.Classification == GjkClassificationV1.Separated &&
                miss.Iterations <= DeterministicGjkV1.MaxIterations,
            "domain.physical.gjk: separated convex corpus pair classification mismatch.");
    }

    private static void VerifyEpa()
    {
        var a = Cube(0, 0, 0, 1000);
        var b = Cube(1500, 0, 0, 1000);
        var result = DeterministicEpaV1.Penetration(a, b);

        Require(result.Status == EpaStatusV1.Penetrating,
            "domain.physical.epa: overlapping cubes must produce penetration.");
        Require(Math.Abs(result.PenetrationDepthMm - 500) <= DeterministicEpaV1.ConvergenceThresholdMm,
            "domain.physical.epa: penetration depth must be within deterministic 1mm representation.");
        Require(result.Iterations <= DeterministicEpaV1.MaxIterations,
            "domain.physical.epa: iteration bound exceeded.");
    }

    private static void VerifyTerrainContact()
    {
        var result = TerrainSdfConservativeAdvancementV1.SweepSphere(
            new Vec3MmV1(0, 0, 3000),
            new Vec3MmV1(0, 0, 0),
            radiusMm: 500,
            static position => checked((int)(position.Z - 1000)));

        Require(result.Status == TerrainSweepStatusV1.Contact,
            "domain.physical.terrain-contact: sweep must find the plane contact.");
        Require(result.Fraction == FixedQ32_32.FromRatio(1, 2),
            "domain.physical.terrain-contact: expected contact fraction is exactly 0.5.");
        Require(result.PositionMm == new Vec3MmV1(0, 0, 1500),
            "domain.physical.terrain-contact: contact position mismatch.");
        Require(result.Iterations <= TerrainSdfConservativeAdvancementV1.MaxIterations,
            "domain.physical.terrain-contact: iteration bound exceeded.");
    }

    private static void VerifyContactOrdering()
    {
        var bodyA = Id("00000000000000000000000000008801");
        var bodyB = Id("00000000000000000000000000008802");
        var bodyC = Id("00000000000000000000000000008803");
        var feature1 = Id("00000000000000000000000000008901");
        var feature2 = Id("00000000000000000000000000008902");

        var contacts = new[]
        {
            new ContactConstraintV1(bodyC, bodyA, feature2, -3),
            new ContactConstraintV1(bodyB, bodyA, feature2, -2),
            new ContactConstraintV1(bodyA, bodyB, feature1, -1),
        };

        var forward = CanonicalContactOrderV1.Sort(contacts);
        var reverse = CanonicalContactOrderV1.Sort(contacts.Reverse());

        Require(forward.SequenceEqual(reverse),
            "domain.physical.contact-order: input permutation changed canonical contact order.");
        Require(forward[0].PairKey == BodyPairKeyV1.Create(bodyA, bodyB) &&
                forward[0].ContactFeatureKey == feature1 &&
                forward[1].ContactFeatureKey == feature2 &&
                forward[2].PairKey == BodyPairKeyV1.Create(bodyA, bodyC),
            "domain.physical.contact-order: pair/feature ordering mismatch.");
    }

    private static void VerifyNonConvergenceBoundary()
    {
        var rejected = false;
        try
        {
            PhysicalSolverGuardV1.RequireConverged(false);
        }
        catch (InvalidDataException ex) when (ex.Message == "physical.solver-nonconvergent")
        {
            rejected = true;
        }

        Require(rejected,
            "domain.physical.nonconvergence: solver failure must remain explicit and must not select a fallback.");
    }

    private static ConvexPolytopeV1 Cube(long centerX, long centerY, long centerZ, long halfExtent)
    {
        var vertices = new List<Vec3MmV1>(8);
        foreach (var x in new[] { -halfExtent, halfExtent })
        foreach (var y in new[] { -halfExtent, halfExtent })
        foreach (var z in new[] { -halfExtent, halfExtent })
            vertices.Add(new Vec3MmV1(
                checked(centerX + x),
                checked(centerY + y),
                checked(centerZ + z)));
        return new ConvexPolytopeV1(vertices);
    }

    private static OpaqueId128 Id(string value) => OpaqueId128.Parse(value);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
