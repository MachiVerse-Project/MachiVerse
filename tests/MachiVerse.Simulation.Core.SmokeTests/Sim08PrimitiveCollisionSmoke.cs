using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Domains.Spatial;

internal static class Sim08PrimitiveCollisionSmoke
{
    internal static void Run()
    {
        VerifySphere();
        VerifyCapsule();
        VerifyOrientedBox();
        VerifyStaticMeshQuery();
    }

    private static void VerifySphere()
    {
        var a = new SphereColliderV1(new Vec3MmV1(0, 0, 0), 500);
        var touching = new SphereColliderV1(new Vec3MmV1(1_000, 0, 0), 500);
        var separated = new SphereColliderV1(new Vec3MmV1(1_001, 0, 0), 500);
        Require(DeterministicPrimitiveCollisionV1.Intersects(a, touching),
            "domain.physical.primitive-sphere: touching spheres must intersect deterministically.");
        Require(!DeterministicPrimitiveCollisionV1.Intersects(a, separated),
            "domain.physical.primitive-sphere: separated spheres must remain separated.");
    }

    private static void VerifyCapsule()
    {
        var capsule = new CapsuleColliderV1(
            new Vec3MmV1(-1_000, 0, 0),
            new Vec3MmV1(1_000, 0, 0),
            250);
        var touching = new SphereColliderV1(new Vec3MmV1(0, 500, 0), 250);
        var separated = new SphereColliderV1(new Vec3MmV1(0, 501, 0), 250);
        Require(DeterministicPrimitiveCollisionV1.Intersects(touching, capsule),
            "domain.physical.primitive-capsule: sphere/capsule touching case mismatch.");
        Require(!DeterministicPrimitiveCollisionV1.Intersects(separated, capsule),
            "domain.physical.primitive-capsule: sphere/capsule separated case mismatch.");
    }

    private static void VerifyOrientedBox()
    {
        var identity = new MachiVerse.Simulation.Core.Domains.QuaternionQ30V1(0, 0, 0, 1 << 30);
        const int sinCos45Q30 = 759_250_125;
        var rotate90Z = new MachiVerse.Simulation.Core.Domains.QuaternionQ30V1(0, 0, sinCos45Q30, sinCos45Q30);
        var longBoxRotated = new OrientedBoxColliderV1(
            new Vec3MmV1(0, 0, 0),
            new Vec3MmV1(1_000, 100, 100),
            rotate90Z);
        var probe = new OrientedBoxColliderV1(
            new Vec3MmV1(0, 900, 0),
            new Vec3MmV1(50, 50, 50),
            identity);
        var farProbe = probe with { CenterMm = new Vec3MmV1(0, 1_101, 0) };

        Require(DeterministicPrimitiveCollisionV1.Intersects(longBoxRotated, probe),
            "domain.physical.primitive-obb: rotated long axis must participate in SAT projection.");
        Require(!DeterministicPrimitiveCollisionV1.Intersects(longBoxRotated, farProbe),
            "domain.physical.primitive-obb: separated rotated boxes must be rejected.");
    }

    private static void VerifyStaticMeshQuery()
    {
        var mesh = new TriangleMeshStaticV1([
            new StaticTriangleV1(9, new Vec3MmV1(10_000, 0, 0), new Vec3MmV1(11_000, 0, 0), new Vec3MmV1(10_000, 1_000, 0)),
            new StaticTriangleV1(2, new Vec3MmV1(0, 0, 0), new Vec3MmV1(1_000, 0, 0), new Vec3MmV1(0, 1_000, 0)),
            new StaticTriangleV1(1, new Vec3MmV1(-500, -500, 0), new Vec3MmV1(500, -500, 0), new Vec3MmV1(0, 500, 0)),
        ]);
        var candidates = mesh.QueryAabb(new Vec3MmV1(-100, -100, -10), new Vec3MmV1(100, 100, 10));
        Require(candidates.Select(static triangle => triangle.CanonicalIndex).SequenceEqual(new uint[] { 1, 2 }),
            "domain.physical.static-mesh: candidate triangles must be returned in canonical index order.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
