using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;

internal static class Sim08SequentialImpulseSmoke
{
    internal static void Run()
    {
        VerifyEqualMassRestitution();
        VerifyCanonicalPermutation();
        VerifyFriction();
        VerifyCoefficientBounds();
        VerifyIterationBound();
    }

    private static void VerifyEqualMassRestitution()
    {
        var a = Id("00000000000000000000000000008a01");
        var b = Id("00000000000000000000000000008a02");
        var feature = Id("00000000000000000000000000008aff");
        var result = DeterministicSequentialImpulseV1.SolveOrReject(
            [
                new SequentialImpulseBodyV1(a, 1000, new VelocityUmPerSecondV1(1000, 0, 0)),
                new SequentialImpulseBodyV1(b, 1000, new VelocityUmPerSecondV1(-1000, 0, 0)),
            ],
            [new SequentialImpulseContactV1(
                a, b, feature, new ContactNormalQ30V1(1 << 30, 0, 0),
                RestitutionPpm: 1_000_000, FrictionPpm: 0)]);

        Require(result.Velocities[a].X == -1000 && result.Velocities[b].X == 1000,
            "domain.physical.sequential-impulse: equal-mass elastic collision golden vector mismatch.");
        Require(result.Iterations <= DeterministicSequentialImpulseV1.MaxIterations && result.Converged,
            "domain.physical.sequential-impulse: elastic collision did not converge within the fixed bound.");
    }

    private static void VerifyCanonicalPermutation()
    {
        var a = Id("00000000000000000000000000008b01");
        var b = Id("00000000000000000000000000008b02");
        var c = Id("00000000000000000000000000008b03");
        var d = Id("00000000000000000000000000008b04");
        var bodies = new[]
        {
            new SequentialImpulseBodyV1(a, 1000, new VelocityUmPerSecondV1(2000, 0, 0)),
            new SequentialImpulseBodyV1(b, 1000, new VelocityUmPerSecondV1(0, 0, 0)),
            new SequentialImpulseBodyV1(c, 1000, new VelocityUmPerSecondV1(500, 0, 0)),
            new SequentialImpulseBodyV1(d, 1000, new VelocityUmPerSecondV1(-500, 0, 0)),
        };
        var contacts = new[]
        {
            new SequentialImpulseContactV1(c, d, Id("00000000000000000000000000008b12"), new ContactNormalQ30V1(1 << 30, 0, 0), 0, 0),
            new SequentialImpulseContactV1(a, b, Id("00000000000000000000000000008b11"), new ContactNormalQ30V1(1 << 30, 0, 0), 0, 0),
        };

        var forward = DeterministicSequentialImpulseV1.SolveOrReject(bodies, contacts);
        var reverse = DeterministicSequentialImpulseV1.SolveOrReject(bodies.Reverse(), contacts.Reverse());
        foreach (var id in new[] { a, b, c, d })
            Require(forward.Velocities[id] == reverse.Velocities[id],
                "domain.physical.sequential-impulse: input permutation changed body velocity result.");
    }

    private static void VerifyFriction()
    {
        var a = Id("00000000000000000000000000008c01");
        var b = Id("00000000000000000000000000008c02");
        var without = DeterministicSequentialImpulseV1.SolveOrReject(
            [
                new SequentialImpulseBodyV1(a, 1000, new VelocityUmPerSecondV1(1000, 1000, 0)),
                new SequentialImpulseBodyV1(b, 1000, new VelocityUmPerSecondV1(-1000, -1000, 0)),
            ],
            [new SequentialImpulseContactV1(a, b, Id("00000000000000000000000000008c10"), new ContactNormalQ30V1(1 << 30, 0, 0), 0, 0)]);
        var with = DeterministicSequentialImpulseV1.SolveOrReject(
            [
                new SequentialImpulseBodyV1(a, 1000, new VelocityUmPerSecondV1(1000, 1000, 0)),
                new SequentialImpulseBodyV1(b, 1000, new VelocityUmPerSecondV1(-1000, -1000, 0)),
            ],
            [new SequentialImpulseContactV1(a, b, Id("00000000000000000000000000008c10"), new ContactNormalQ30V1(1 << 30, 0, 0), 0, 500_000)]);

        var noFrictionRelativeY = Math.Abs(without.Velocities[b].Y - without.Velocities[a].Y);
        var frictionRelativeY = Math.Abs(with.Velocities[b].Y - with.Velocities[a].Y);
        Require(frictionRelativeY < noFrictionRelativeY,
            "domain.physical.sequential-impulse: friction ppm must reduce tangential relative velocity.");
    }

    private static void VerifyCoefficientBounds()
    {
        var a = Id("00000000000000000000000000008d01");
        var b = Id("00000000000000000000000000008d02");
        RequireReject(
            () => DeterministicSequentialImpulseV1.Solve(
                [new SequentialImpulseBodyV1(a, 1, default), new SequentialImpulseBodyV1(b, 1, default)],
                [new SequentialImpulseContactV1(a, b, Id("00000000000000000000000000008d10"), new ContactNormalQ30V1(1 << 30, 0, 0), 1_000_001, 0)]),
            "physical.restitution-out-of-range");
        RequireReject(
            () => DeterministicSequentialImpulseV1.Solve(
                [new SequentialImpulseBodyV1(a, 1, default), new SequentialImpulseBodyV1(b, 1, default)],
                [new SequentialImpulseContactV1(a, b, Id("00000000000000000000000000008d10"), new ContactNormalQ30V1(1 << 30, 0, 0), 0, 1_000_001)]),
            "physical.friction-out-of-range");
    }

    private static void VerifyIterationBound()
    {
        var a = Id("00000000000000000000000000008e01");
        var b = Id("00000000000000000000000000008e02");
        var result = DeterministicSequentialImpulseV1.Solve(
            [
                new SequentialImpulseBodyV1(a, 3, new VelocityUmPerSecondV1(999_999, 0, 0)),
                new SequentialImpulseBodyV1(b, 7, new VelocityUmPerSecondV1(-999_999, 0, 0)),
            ],
            [new SequentialImpulseContactV1(a, b, Id("00000000000000000000000000008e10"), new ContactNormalQ30V1(1 << 30, 0, 0), 333_333, 0)]);
        Require(result.Iterations is >= 1 and <= DeterministicSequentialImpulseV1.MaxIterations,
            "domain.physical.sequential-impulse: iteration count escaped fixed 1..12 bound.");
    }

    private static OpaqueId128 Id(string value) => OpaqueId128.Parse(value);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void RequireReject(Action action, string expected)
    {
        try
        {
            action();
        }
        catch (InvalidDataException ex) when (ex.Message == expected)
        {
            return;
        }
        throw new InvalidOperationException($"Expected rejection: {expected}");
    }
}
