using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;

namespace MachiVerse.Simulation.Core.Domains.PhysicalBuilt;

public sealed class ConvexPolytopeV1
{
    private readonly Vec3MmV1[] _vertices;

    public ConvexPolytopeV1(IEnumerable<Vec3MmV1> vertices)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        _vertices = vertices.ToArray();
        if (_vertices.Length == 0)
            throw new ArgumentException("Convex polytope requires at least one vertex.", nameof(vertices));
        if (_vertices.Distinct().Count() != _vertices.Length)
            throw new InvalidDataException("physical.convex-duplicate-vertex");
    }

    public IReadOnlyList<Vec3MmV1> Vertices => Array.AsReadOnly(_vertices);

    internal SupportVertexV1 Support(IntVec3V1 direction)
    {
        if (direction.IsZero) direction = IntVec3V1.UnitX;
        var bestIndex = 0;
        var bestDot = direction.Dot(_vertices[0]);
        for (var index = 1; index < _vertices.Length; index++)
        {
            var dot = direction.Dot(_vertices[index]);
            if (dot > bestDot)
            {
                bestDot = dot;
                bestIndex = index;
            }
        }
        return new SupportVertexV1(_vertices[bestIndex], bestIndex);
    }
}

internal readonly record struct SupportVertexV1(Vec3MmV1 Point, int CanonicalIndex);

internal readonly record struct IntVec3V1(Int128 X, Int128 Y, Int128 Z)
{
    public static IntVec3V1 UnitX => new(1, 0, 0);
    public bool IsZero => X == 0 && Y == 0 && Z == 0;

    public static IntVec3V1 From(Vec3MmV1 value) => new(value.X, value.Y, value.Z);
    public static IntVec3V1 operator +(IntVec3V1 left, IntVec3V1 right)
        => new(checked(left.X + right.X), checked(left.Y + right.Y), checked(left.Z + right.Z));
    public static IntVec3V1 operator -(IntVec3V1 left, IntVec3V1 right)
        => new(checked(left.X - right.X), checked(left.Y - right.Y), checked(left.Z - right.Z));
    public static IntVec3V1 operator -(IntVec3V1 value)
        => new(checked(-value.X), checked(-value.Y), checked(-value.Z));

    public Int128 Dot(IntVec3V1 other)
        => checked(checked(X * other.X) + checked(Y * other.Y) + checked(Z * other.Z));

    public Int128 Dot(Vec3MmV1 other)
        => checked(checked(X * other.X) + checked(Y * other.Y) + checked(Z * other.Z));

    public IntVec3V1 Cross(IntVec3V1 other)
        => new(
            checked(checked(Y * other.Z) - checked(Z * other.Y)),
            checked(checked(Z * other.X) - checked(X * other.Z)),
            checked(checked(X * other.Y) - checked(Y * other.X)));

    public UInt128 LengthSquared()
    {
        try
        {
            var x = AbsToUInt128(X);
            var y = AbsToUInt128(Y);
            var z = AbsToUInt128(Z);
            return checked(checked(x * x) + checked(y * y) + checked(z * z));
        }
        catch (OverflowException ex)
        {
            throw new OverflowException("simulation.numeric-overflow", ex);
        }
    }

    private static UInt128 AbsToUInt128(Int128 value)
    {
        if (value >= 0) return (UInt128)value;
        if (value == Int128.MinValue) throw new OverflowException("simulation.numeric-overflow");
        return (UInt128)(-value);
    }
}

internal readonly record struct MinkowskiSupportV1(
    IntVec3V1 Point,
    int VertexAIndex,
    int VertexBIndex);

internal static class MinkowskiSupportFactoryV1
{
    public static MinkowskiSupportV1 Support(
        ConvexPolytopeV1 a,
        ConvexPolytopeV1 b,
        IntVec3V1 direction)
    {
        var vertexA = a.Support(direction);
        var vertexB = b.Support(-direction);
        return new MinkowskiSupportV1(
            IntVec3V1.From(vertexA.Point) - IntVec3V1.From(vertexB.Point),
            vertexA.CanonicalIndex,
            vertexB.CanonicalIndex);
    }
}

public enum GjkClassificationV1 : byte
{
    Separated = 1,
    Intersecting = 2,
    NonConvergent = 3,
}

public sealed record GjkResultV1(
    GjkClassificationV1 Classification,
    int Iterations);

public static class DeterministicGjkV1
{
    public const int MaxIterations = 32;

    public static GjkResultV1 Classify(ConvexPolytopeV1 a, ConvexPolytopeV1 b)
        => Solve(a, b).PublicResult;

    internal static GjkInternalResultV1 Solve(ConvexPolytopeV1 a, ConvexPolytopeV1 b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        var direction = InitialDirection(a, b);
        var first = MinkowskiSupportFactoryV1.Support(a, b, direction);
        var simplex = new List<MinkowskiSupportV1> { first };
        direction = -first.Point;
        if (direction.IsZero)
            return new GjkInternalResultV1(
                new GjkResultV1(GjkClassificationV1.Intersecting, 1),
                simplex.ToArray());

        var seen = new HashSet<IntVec3V1> { first.Point };
        for (var iteration = 1; iteration <= MaxIterations; iteration++)
        {
            var support = MinkowskiSupportFactoryV1.Support(a, b, direction);
            if (support.Point.Dot(direction) < 0)
                return new GjkInternalResultV1(
                    new GjkResultV1(GjkClassificationV1.Separated, iteration),
                    simplex.ToArray());

            if (!seen.Add(support.Point))
                return new GjkInternalResultV1(
                    new GjkResultV1(GjkClassificationV1.NonConvergent, iteration),
                    simplex.ToArray());

            simplex.Insert(0, support);
            if (UpdateSimplex(simplex, ref direction))
                return new GjkInternalResultV1(
                    new GjkResultV1(GjkClassificationV1.Intersecting, iteration),
                    simplex.ToArray());

            if (direction.IsZero)
                return new GjkInternalResultV1(
                    new GjkResultV1(GjkClassificationV1.Intersecting, iteration),
                    simplex.ToArray());
        }

        return new GjkInternalResultV1(
            new GjkResultV1(GjkClassificationV1.NonConvergent, MaxIterations),
            simplex.ToArray());
    }

    private static IntVec3V1 InitialDirection(ConvexPolytopeV1 a, ConvexPolytopeV1 b)
    {
        var direction = IntVec3V1.From(b.Vertices[0]) - IntVec3V1.From(a.Vertices[0]);
        return direction.IsZero ? IntVec3V1.UnitX : direction;
    }

    private static bool UpdateSimplex(List<MinkowskiSupportV1> simplex, ref IntVec3V1 direction)
        => simplex.Count switch
        {
            2 => Line(simplex, ref direction),
            3 => Triangle(simplex, ref direction),
            4 => Tetrahedron(simplex, ref direction),
            _ => throw new InvalidDataException("physical.gjk-simplex-size"),
        };

    private static bool Line(List<MinkowskiSupportV1> simplex, ref IntVec3V1 direction)
    {
        var a = simplex[0].Point;
        var b = simplex[1].Point;
        var ao = -a;
        var ab = b - a;
        if (SameDirection(ab, ao))
        {
            direction = TripleCross(ab, ao, ab);
            if (direction.IsZero) direction = DeterministicPerpendicular(ab, ao);
        }
        else
        {
            simplex.RemoveAt(1);
            direction = ao;
        }
        return direction.IsZero;
    }

    private static bool Triangle(List<MinkowskiSupportV1> simplex, ref IntVec3V1 direction)
    {
        var a = simplex[0].Point;
        var b = simplex[1].Point;
        var c = simplex[2].Point;
        var ao = -a;
        var ab = b - a;
        var ac = c - a;
        var abc = ab.Cross(ac);

        var acPerp = abc.Cross(ac);
        if (SameDirection(acPerp, ao))
        {
            if (SameDirection(ac, ao))
            {
                simplex.RemoveAt(1);
                direction = TripleCross(ac, ao, ac);
                if (direction.IsZero) direction = DeterministicPerpendicular(ac, ao);
                return direction.IsZero;
            }

            simplex.RemoveAt(2);
            return Line(simplex, ref direction);
        }

        var abPerp = ab.Cross(abc);
        if (SameDirection(abPerp, ao))
        {
            simplex.RemoveAt(2);
            return Line(simplex, ref direction);
        }

        if (SameDirection(abc, ao))
        {
            direction = abc;
        }
        else
        {
            (simplex[1], simplex[2]) = (simplex[2], simplex[1]);
            direction = -abc;
        }
        return direction.IsZero;
    }

    private static bool Tetrahedron(List<MinkowskiSupportV1> simplex, ref IntVec3V1 direction)
    {
        var a = simplex[0].Point;
        var b = simplex[1].Point;
        var c = simplex[2].Point;
        var d = simplex[3].Point;
        var ao = -a;

        if (OutsideFace(a, b, c, d, ao, out var abcNormal))
        {
            simplex.RemoveAt(3);
            direction = abcNormal;
            return Triangle(simplex, ref direction);
        }

        if (OutsideFace(a, c, d, b, ao, out var acdNormal))
        {
            simplex[1] = simplex[2];
            simplex[2] = simplex[3];
            simplex.RemoveAt(3);
            direction = acdNormal;
            return Triangle(simplex, ref direction);
        }

        if (OutsideFace(a, d, b, c, ao, out var adbNormal))
        {
            var originalB = simplex[1];
            simplex[1] = simplex[3];
            simplex[2] = originalB;
            simplex.RemoveAt(3);
            direction = adbNormal;
            return Triangle(simplex, ref direction);
        }

        return true;
    }

    private static bool OutsideFace(
        IntVec3V1 a,
        IntVec3V1 b,
        IntVec3V1 c,
        IntVec3V1 opposite,
        IntVec3V1 ao,
        out IntVec3V1 outwardNormal)
    {
        var normal = (b - a).Cross(c - a);
        if (normal.Dot(opposite - a) > 0) normal = -normal;
        outwardNormal = normal;
        return SameDirection(normal, ao);
    }

    private static bool SameDirection(IntVec3V1 direction, IntVec3V1 toward)
        => direction.Dot(toward) > 0;

    private static IntVec3V1 TripleCross(IntVec3V1 a, IntVec3V1 b, IntVec3V1 c)
        => a.Cross(b).Cross(c);

    private static IntVec3V1 DeterministicPerpendicular(IntVec3V1 line, IntVec3V1 toward)
    {
        var candidate = line.Cross(toward).Cross(line);
        if (!candidate.IsZero) return candidate;
        candidate = line.Cross(new IntVec3V1(1, 0, 0));
        if (!candidate.IsZero) return candidate;
        candidate = line.Cross(new IntVec3V1(0, 1, 0));
        return candidate.IsZero ? new IntVec3V1(0, 0, 1) : candidate;
    }
}

internal sealed record GjkInternalResultV1(
    GjkResultV1 PublicResult,
    IReadOnlyList<MinkowskiSupportV1> Simplex);

public enum EpaStatusV1 : byte
{
    Penetrating = 1,
    NotIntersecting = 2,
    NonConvergent = 3,
}

public sealed record EpaPenetrationResultV1(
    EpaStatusV1 Status,
    long PenetrationDepthMm,
    int Iterations);

public static class DeterministicEpaV1
{
    public const int MaxIterations = 32;
    public const long ConvergenceThresholdMm = 1;

    public static EpaPenetrationResultV1 Penetration(ConvexPolytopeV1 a, ConvexPolytopeV1 b)
    {
        var gjk = DeterministicGjkV1.Solve(a, b);
        if (gjk.PublicResult.Classification == GjkClassificationV1.Separated)
            return new EpaPenetrationResultV1(EpaStatusV1.NotIntersecting, 0, gjk.PublicResult.Iterations);
        if (gjk.PublicResult.Classification == GjkClassificationV1.NonConvergent)
            return new EpaPenetrationResultV1(EpaStatusV1.NonConvergent, 0, gjk.PublicResult.Iterations);

        var vertices = SeedTetrahedron(a, b, gjk.Simplex);
        if (vertices is null)
            return new EpaPenetrationResultV1(EpaStatusV1.NonConvergent, 0, gjk.PublicResult.Iterations);

        var faces = new List<EpaFaceV1>
        {
            EpaFaceV1.Create(0, 1, 2, vertices),
            EpaFaceV1.Create(0, 3, 1, vertices),
            EpaFaceV1.Create(0, 2, 3, vertices),
            EpaFaceV1.Create(1, 3, 2, vertices),
        };

        for (var iteration = 1; iteration <= MaxIterations; iteration++)
        {
            faces.Sort(EpaFaceV1Comparer.Instance);
            var closest = faces[0];
            var support = MinkowskiSupportFactoryV1.Support(a, b, closest.Normal);
            var supportDistance = ProjectDistanceMm(closest.Normal, support.Point);
            if (supportDistance - closest.DistanceMm <= ConvergenceThresholdMm)
                return new EpaPenetrationResultV1(
                    EpaStatusV1.Penetrating,
                    Math.Max(0, supportDistance),
                    iteration);

            var existingIndex = vertices.FindIndex(vertex => vertex.Point == support.Point);
            if (existingIndex >= 0)
                return new EpaPenetrationResultV1(EpaStatusV1.NonConvergent, 0, iteration);

            var newIndex = vertices.Count;
            vertices.Add(support);

            var visible = faces
                .Where(face => face.IsVisibleFrom(support.Point, vertices))
                .ToArray();
            if (visible.Length == 0)
                return new EpaPenetrationResultV1(EpaStatusV1.NonConvergent, 0, iteration);

            var boundary = new Dictionary<(int Low, int High), EpaBoundaryEdgeV1>();
            foreach (var face in visible)
            {
                AddBoundary(boundary, face.A, face.B);
                AddBoundary(boundary, face.B, face.C);
                AddBoundary(boundary, face.C, face.A);
            }
            faces.RemoveAll(face => visible.Contains(face));

            foreach (var edge in boundary.Values
                         .Where(static edge => edge.Count == 1)
                         .OrderBy(static edge => edge.Low)
                         .ThenBy(static edge => edge.High))
            {
                faces.Add(EpaFaceV1.Create(edge.From, edge.To, newIndex, vertices));
            }

            if (faces.Count == 0)
                return new EpaPenetrationResultV1(EpaStatusV1.NonConvergent, 0, iteration);
        }

        return new EpaPenetrationResultV1(EpaStatusV1.NonConvergent, 0, MaxIterations);
    }

    private static List<MinkowskiSupportV1>? SeedTetrahedron(
        ConvexPolytopeV1 a,
        ConvexPolytopeV1 b,
        IReadOnlyList<MinkowskiSupportV1> simplex)
    {
        var vertices = simplex.DistinctBy(static item => item.Point).Take(4).ToList();
        var directions = new[]
        {
            new IntVec3V1(1, 0, 0),
            new IntVec3V1(-1, 0, 0),
            new IntVec3V1(0, 1, 0),
            new IntVec3V1(0, -1, 0),
            new IntVec3V1(0, 0, 1),
            new IntVec3V1(0, 0, -1),
        };
        foreach (var direction in directions)
        {
            if (vertices.Count == 4) break;
            var support = MinkowskiSupportFactoryV1.Support(a, b, direction);
            if (vertices.All(existing => existing.Point != support.Point))
                vertices.Add(support);
        }

        if (vertices.Count != 4) return null;
        var volume6 = (vertices[1].Point - vertices[0].Point)
            .Cross(vertices[2].Point - vertices[0].Point)
            .Dot(vertices[3].Point - vertices[0].Point);
        return volume6 == 0 ? null : vertices;
    }

    private static void AddBoundary(
        IDictionary<(int Low, int High), EpaBoundaryEdgeV1> boundary,
        int from,
        int to)
    {
        var key = from < to ? (from, to) : (to, from);
        if (boundary.TryGetValue(key, out var existing))
            boundary[key] = existing with { Count = existing.Count + 1 };
        else
            boundary.Add(key, new EpaBoundaryEdgeV1(key.Item1, key.Item2, from, to, 1));
    }

    private static long ProjectDistanceMm(IntVec3V1 normal, IntVec3V1 point)
    {
        var normSquared = normal.LengthSquared();
        if (normSquared == 0) throw new InvalidDataException("physical.epa-degenerate-face");
        var norm = IntegerSqrtV1.Floor(normSquared);
        if (norm == 0 || norm > (UInt128)Int128.MaxValue)
            throw new OverflowException("simulation.numeric-overflow");
        var numerator = normal.Dot(point);
        if (numerator < 0) numerator = -numerator;
        return PhysicalIntegerMathV1.DivideRoundToEven(numerator, (Int128)norm);
    }

    private sealed record EpaFaceV1(
        int A,
        int B,
        int C,
        IntVec3V1 Normal,
        long DistanceMm)
    {
        public static EpaFaceV1 Create(
            int a,
            int b,
            int c,
            IReadOnlyList<MinkowskiSupportV1> vertices)
        {
            var pa = vertices[a].Point;
            var pb = vertices[b].Point;
            var pc = vertices[c].Point;
            var normal = (pb - pa).Cross(pc - pa);
            if (normal.IsZero) throw new InvalidDataException("physical.epa-degenerate-face");
            if (normal.Dot(pa) < 0)
            {
                (b, c) = (c, b);
                normal = -normal;
            }
            return new EpaFaceV1(a, b, c, normal, ProjectDistanceMm(normal, pa));
        }

        public bool IsVisibleFrom(IntVec3V1 point, IReadOnlyList<MinkowskiSupportV1> vertices)
            => Normal.Dot(point - vertices[A].Point) > 0;
    }

    private sealed class EpaFaceV1Comparer : IComparer<EpaFaceV1>
    {
        public static EpaFaceV1Comparer Instance { get; } = new();

        public int Compare(EpaFaceV1? x, EpaFaceV1? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            var compare = x.DistanceMm.CompareTo(y.DistanceMm);
            if (compare != 0) return compare;
            compare = x.A.CompareTo(y.A);
            if (compare != 0) return compare;
            compare = x.B.CompareTo(y.B);
            if (compare != 0) return compare;
            return x.C.CompareTo(y.C);
        }
    }

    private sealed record EpaBoundaryEdgeV1(
        int Low,
        int High,
        int From,
        int To,
        int Count);
}

public static class IntegerSqrtV1
{
    public static UInt128 Floor(UInt128 value)
    {
        if (value < 2) return value;
        UInt128 low = 1;
        UInt128 high = ((UInt128)1 << 64);
        while (low + 1 < high)
        {
            var mid = low + ((high - low) >> 1);
            if (mid <= value / mid)
                low = mid;
            else
                high = mid;
        }
        return low;
    }
}

public enum TerrainSweepStatusV1 : byte
{
    NoContact = 1,
    Contact = 2,
    NonConvergent = 3,
}

public sealed record TerrainSweepResultV1(
    TerrainSweepStatusV1 Status,
    FixedQ32_32 Fraction,
    Vec3MmV1 PositionMm,
    int Iterations);

public static class TerrainSdfConservativeAdvancementV1
{
    public const int MaxIterations = 16;
    public const long ConvergenceThresholdMm = 1;

    public static TerrainSweepResultV1 SweepSphere(
        Vec3MmV1 startCenterMm,
        Vec3MmV1 endCenterMm,
        long radiusMm,
        Func<Vec3MmV1, int> signedDistanceMm)
    {
        ArgumentNullException.ThrowIfNull(signedDistanceMm);
        if (radiusMm < 0) throw new ArgumentOutOfRangeException(nameof(radiusMm));

        var delta = IntVec3V1.From(endCenterMm) - IntVec3V1.From(startCenterMm);
        var lengthSquared = delta.LengthSquared();
        var pathLengthMm = IntegerSqrtV1.Floor(lengthSquared);
        if (pathLengthMm > long.MaxValue) throw new OverflowException("simulation.numeric-overflow");

        var oneRaw = FixedQ32_32.One.Raw;
        long tRaw = 0;
        for (var iteration = 1; iteration <= MaxIterations; iteration++)
        {
            var position = Interpolate(startCenterMm, endCenterMm, tRaw);
            var clearance = checked((long)signedDistanceMm(position) - radiusMm);
            if (clearance <= ConvergenceThresholdMm)
                return new TerrainSweepResultV1(
                    TerrainSweepStatusV1.Contact,
                    new FixedQ32_32(tRaw),
                    position,
                    iteration);

            if (tRaw >= oneRaw || pathLengthMm == 0)
                return new TerrainSweepResultV1(
                    TerrainSweepStatusV1.NoContact,
                    new FixedQ32_32(oneRaw),
                    position,
                    iteration);

            var advanceRaw = PhysicalIntegerMathV1.DivideRoundToEven(
                checked((Int128)clearance * oneRaw),
                (Int128)pathLengthMm);
            if (advanceRaw <= 0) advanceRaw = 1;
            var next = checked(tRaw + advanceRaw);
            tRaw = next >= oneRaw ? oneRaw : next;
        }

        return new TerrainSweepResultV1(
            TerrainSweepStatusV1.NonConvergent,
            new FixedQ32_32(tRaw),
            Interpolate(startCenterMm, endCenterMm, tRaw),
            MaxIterations);
    }

    private static Vec3MmV1 Interpolate(Vec3MmV1 from, Vec3MmV1 to, long tRaw)
    {
        var oneRaw = FixedQ32_32.One.Raw;
        return new Vec3MmV1(
            checked(from.X + PhysicalIntegerMathV1.DivideRoundToEven(
                checked(((Int128)to.X - from.X) * tRaw), oneRaw)),
            checked(from.Y + PhysicalIntegerMathV1.DivideRoundToEven(
                checked(((Int128)to.Y - from.Y) * tRaw), oneRaw)),
            checked(from.Z + PhysicalIntegerMathV1.DivideRoundToEven(
                checked(((Int128)to.Z - from.Z) * tRaw), oneRaw)));
    }
}

public readonly record struct BodyPairKeyV1(OpaqueId128 Low, OpaqueId128 High) : IComparable<BodyPairKeyV1>
{
    public static BodyPairKeyV1 Create(OpaqueId128 a, OpaqueId128 b)
    {
        if (a.IsZero || b.IsZero) throw new InvalidDataException("physical.body-id-zero");
        if (a == b) throw new InvalidDataException("physical.body-pair-self");
        return a.CompareTo(b) < 0 ? new BodyPairKeyV1(a, b) : new BodyPairKeyV1(b, a);
    }

    public int CompareTo(BodyPairKeyV1 other)
    {
        var compare = Low.CompareTo(other.Low);
        return compare != 0 ? compare : High.CompareTo(other.High);
    }
}

public sealed record ContactConstraintV1(
    OpaqueId128 BodyA,
    OpaqueId128 BodyB,
    OpaqueId128 ContactFeatureKey,
    long RelativeNormalVelocityUmPerSecond)
{
    public BodyPairKeyV1 PairKey => BodyPairKeyV1.Create(BodyA, BodyB);
}

public static class CanonicalContactOrderV1
{
    public static IReadOnlyList<ContactConstraintV1> Sort(IEnumerable<ContactConstraintV1> contacts)
    {
        ArgumentNullException.ThrowIfNull(contacts);
        return Array.AsReadOnly(contacts
            .OrderBy(static contact => contact.PairKey)
            .ThenBy(static contact => contact.ContactFeatureKey)
            .ToArray());
    }
}

public static class PhysicalSolverGuardV1
{
    public static void RequireConverged(bool converged)
    {
        if (!converged)
            throw new InvalidDataException("physical.solver-nonconvergent");
    }
}
