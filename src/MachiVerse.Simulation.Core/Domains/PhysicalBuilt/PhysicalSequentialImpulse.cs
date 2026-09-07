using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Domains.PhysicalBuilt;

public readonly record struct ContactNormalQ30V1(int X, int Y, int Z)
{
    public const long Scale = 1L << 30;

    public void Validate()
    {
        if (X == 0 && Y == 0 && Z == 0)
            throw new InvalidDataException("physical.contact-normal-zero");
        if (Math.Abs((long)X) > Scale || Math.Abs((long)Y) > Scale || Math.Abs((long)Z) > Scale)
            throw new InvalidDataException("physical.contact-normal-out-of-range");
    }
}

public sealed record SequentialImpulseBodyV1(
    OpaqueId128 BodyId,
    long MassGram,
    VelocityUmPerSecondV1 VelocityUmPerSecond)
{
    public void Validate()
    {
        if (BodyId.IsZero) throw new InvalidDataException("physical.body-id-zero");
        if (MassGram <= 0) throw new InvalidDataException("physical.mass-nonpositive");
    }
}

public sealed record SequentialImpulseContactV1(
    OpaqueId128 BodyA,
    OpaqueId128 BodyB,
    OpaqueId128 ContactFeatureKey,
    ContactNormalQ30V1 NormalAToB,
    uint RestitutionPpm,
    uint FrictionPpm)
{
    public BodyPairKeyV1 PairKey => BodyPairKeyV1.Create(BodyA, BodyB);

    public void Validate()
    {
        _ = PairKey;
        if (ContactFeatureKey.IsZero) throw new InvalidDataException("physical.contact-feature-zero");
        NormalAToB.Validate();
        if (RestitutionPpm > 1_000_000) throw new InvalidDataException("physical.restitution-out-of-range");
        if (FrictionPpm > 1_000_000) throw new InvalidDataException("physical.friction-out-of-range");
    }
}

public sealed record SequentialImpulseSolveResultV1(
    IReadOnlyDictionary<OpaqueId128, VelocityUmPerSecondV1> Velocities,
    int Iterations,
    bool Converged);

/// <summary>
/// Deterministic linear sequential-impulse contact solve. Contact iteration is always canonical
/// (body_pair_key, contact_feature_key) and uses the fixed Phase-4 maximum of twelve iterations.
/// This linear kernel intentionally does not make wall-clock/performance driven iteration choices.
/// </summary>
public static class DeterministicSequentialImpulseV1
{
    public const int MaxIterations = 12;
    private const long PpmScale = 1_000_000;

    public static SequentialImpulseSolveResultV1 Solve(
        IEnumerable<SequentialImpulseBodyV1> bodies,
        IEnumerable<SequentialImpulseContactV1> contacts)
    {
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(contacts);

        var bodyArray = bodies.ToArray();
        foreach (var body in bodyArray) body.Validate();
        if (bodyArray.GroupBy(static body => body.BodyId).Any(static group => group.Count() != 1))
            throw new InvalidDataException("physical.duplicate-body-id");

        var contactArray = contacts.ToArray();
        foreach (var contact in contactArray) contact.Validate();
        if (contactArray.GroupBy(static contact => (contact.PairKey, contact.ContactFeatureKey))
            .Any(static group => group.Count() != 1))
            throw new InvalidDataException("physical.duplicate-contact");

        var states = bodyArray.ToDictionary(
            static body => body.BodyId,
            static body => new MutableBodyState(body.MassGram, body.VelocityUmPerSecond));
        foreach (var contact in contactArray)
        {
            if (!states.ContainsKey(contact.BodyA) || !states.ContainsKey(contact.BodyB))
                throw new InvalidDataException("physical.contact-body-missing");
        }

        var ordered = contactArray
            .OrderBy(static contact => contact.PairKey)
            .ThenBy(static contact => contact.ContactFeatureKey)
            .ToArray();
        var restitutionTargets = ordered.ToDictionary(
            static contact => ContactIdentity(contact),
            contact => InitialRestitutionTarget(contact, states));

        var converged = false;
        var iterations = 0;
        for (var iteration = 1; iteration <= MaxIterations; iteration++)
        {
            iterations = iteration;
            var changed = false;
            foreach (var contact in ordered)
                changed |= ResolveContact(contact, restitutionTargets[ContactIdentity(contact)], states);

            if (!changed)
            {
                converged = true;
                break;
            }
        }

        // A fixed hard cap is authoritative. Failure to settle by the cap is explicit and never
        // replaced by a timing-sensitive fallback.
        if (!converged)
        {
            converged = ordered.All(contact =>
                RelativeNormalVelocity(contact, states) >= restitutionTargets[ContactIdentity(contact)]);
        }

        var result = states
            .OrderBy(static pair => pair.Key)
            .ToDictionary(static pair => pair.Key, static pair => pair.Value.Velocity);
        return new SequentialImpulseSolveResultV1(result, iterations, converged);
    }

    public static SequentialImpulseSolveResultV1 SolveOrReject(
        IEnumerable<SequentialImpulseBodyV1> bodies,
        IEnumerable<SequentialImpulseContactV1> contacts)
    {
        var result = Solve(bodies, contacts);
        PhysicalSolverGuardV1.RequireConverged(result.Converged);
        return result;
    }

    private static bool ResolveContact(
        SequentialImpulseContactV1 contact,
        long restitutionTarget,
        IDictionary<OpaqueId128, MutableBodyState> states)
    {
        var a = states[contact.BodyA];
        var b = states[contact.BodyB];
        var currentNormal = RelativeNormalVelocity(contact, states);
        var requiredNormalCorrection = checked(restitutionTarget - currentNormal);
        if (requiredNormalCorrection <= 0) return false;

        ApplyRelativeCorrection(a, b, contact.NormalAToB, requiredNormalCorrection);

        if (contact.FrictionPpm != 0)
        {
            var relative = Subtract(b.Velocity, a.Velocity);
            var normalVelocity = ProjectQ30(relative, contact.NormalAToB);
            var tangent = Subtract(relative, Scale(contact.NormalAToB, normalVelocity));
            var tangentLength = VectorLength(tangent);
            if (tangentLength > 0)
            {
                var maxTangentCorrection = PhysicalIntegerMathV1.DivideRoundToEven(
                    checked((Int128)requiredNormalCorrection * contact.FrictionPpm), PpmScale);
                var tangentCorrection = Math.Min(tangentLength, Math.Max(0, maxTangentCorrection));
                if (tangentCorrection > 0)
                {
                    var correction = ScaleToLength(tangent, checked(-tangentCorrection));
                    ApplyRelativeVectorCorrection(a, b, correction);
                }
            }
        }
        return true;
    }

    private static long InitialRestitutionTarget(
        SequentialImpulseContactV1 contact,
        IReadOnlyDictionary<OpaqueId128, MutableBodyState> states)
    {
        var initial = RelativeNormalVelocity(contact, states);
        if (initial >= 0 || contact.RestitutionPpm == 0) return 0;
        return PhysicalIntegerMathV1.DivideRoundToEven(
            checked(-(Int128)initial * contact.RestitutionPpm), PpmScale);
    }

    private static long RelativeNormalVelocity(
        SequentialImpulseContactV1 contact,
        IReadOnlyDictionary<OpaqueId128, MutableBodyState> states)
        => ProjectQ30(Subtract(states[contact.BodyB].Velocity, states[contact.BodyA].Velocity), contact.NormalAToB);

    private static void ApplyRelativeCorrection(
        MutableBodyState a,
        MutableBodyState b,
        ContactNormalQ30V1 normal,
        long relativeCorrection)
        => ApplyRelativeVectorCorrection(a, b, Scale(normal, relativeCorrection));

    private static void ApplyRelativeVectorCorrection(
        MutableBodyState a,
        MutableBodyState b,
        VelocityUmPerSecondV1 relativeCorrection)
    {
        var totalMass = checked((Int128)a.MassGram + b.MassGram);
        var correctionA = ScaleByMass(relativeCorrection, b.MassGram, totalMass);
        var correctionB = ScaleByMass(relativeCorrection, a.MassGram, totalMass);
        a.Velocity = Subtract(a.Velocity, correctionA);
        b.Velocity = Add(b.Velocity, correctionB);
    }

    private static VelocityUmPerSecondV1 ScaleByMass(
        VelocityUmPerSecondV1 value,
        long numeratorMass,
        Int128 totalMass)
        => new(
            PhysicalIntegerMathV1.DivideRoundToEven(checked((Int128)value.X * numeratorMass), totalMass),
            PhysicalIntegerMathV1.DivideRoundToEven(checked((Int128)value.Y * numeratorMass), totalMass),
            PhysicalIntegerMathV1.DivideRoundToEven(checked((Int128)value.Z * numeratorMass), totalMass));

    private static long ProjectQ30(VelocityUmPerSecondV1 value, ContactNormalQ30V1 normal)
    {
        var dot = checked(checked((Int128)value.X * normal.X) +
                          checked((Int128)value.Y * normal.Y) +
                          checked((Int128)value.Z * normal.Z));
        return PhysicalIntegerMathV1.DivideRoundToEven(dot, ContactNormalQ30V1.Scale);
    }

    private static VelocityUmPerSecondV1 Scale(ContactNormalQ30V1 normal, long magnitude)
        => new(
            PhysicalIntegerMathV1.DivideRoundToEven(checked((Int128)normal.X * magnitude), ContactNormalQ30V1.Scale),
            PhysicalIntegerMathV1.DivideRoundToEven(checked((Int128)normal.Y * magnitude), ContactNormalQ30V1.Scale),
            PhysicalIntegerMathV1.DivideRoundToEven(checked((Int128)normal.Z * magnitude), ContactNormalQ30V1.Scale));

    private static VelocityUmPerSecondV1 ScaleToLength(VelocityUmPerSecondV1 vector, long targetLength)
    {
        var length = VectorLength(vector);
        if (length == 0) return default;
        return new VelocityUmPerSecondV1(
            PhysicalIntegerMathV1.DivideRoundToEven(checked((Int128)vector.X * targetLength), length),
            PhysicalIntegerMathV1.DivideRoundToEven(checked((Int128)vector.Y * targetLength), length),
            PhysicalIntegerMathV1.DivideRoundToEven(checked((Int128)vector.Z * targetLength), length));
    }

    private static long VectorLength(VelocityUmPerSecondV1 value)
    {
        var x = AbsToUInt128(value.X);
        var y = AbsToUInt128(value.Y);
        var z = AbsToUInt128(value.Z);
        var squared = checked(checked(x * x) + checked(y * y) + checked(z * z));
        var root = IntegerSqrtV1.Floor(squared);
        if (root > long.MaxValue) throw new OverflowException("simulation.numeric-overflow");
        return (long)root;
    }

    private static UInt128 AbsToUInt128(long value)
        => value >= 0 ? (UInt128)value : (UInt128)(-(Int128)value);

    private static VelocityUmPerSecondV1 Add(VelocityUmPerSecondV1 a, VelocityUmPerSecondV1 b)
        => new(checked(a.X + b.X), checked(a.Y + b.Y), checked(a.Z + b.Z));

    private static VelocityUmPerSecondV1 Subtract(VelocityUmPerSecondV1 a, VelocityUmPerSecondV1 b)
        => new(checked(a.X - b.X), checked(a.Y - b.Y), checked(a.Z - b.Z));

    private static (BodyPairKeyV1 Pair, OpaqueId128 Feature) ContactIdentity(SequentialImpulseContactV1 contact)
        => (contact.PairKey, contact.ContactFeatureKey);

    private sealed class MutableBodyState(long massGram, VelocityUmPerSecondV1 velocity)
    {
        public long MassGram { get; } = massGram;
        public VelocityUmPerSecondV1 Velocity { get; set; } = velocity;
    }
}
