using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains.PhysicalBuilt;

public readonly record struct SphereColliderV1(Vec3MmV1 CenterMm, long RadiusMm)
{
    public void Validate()
    {
        if (RadiusMm <= 0) throw new InvalidDataException("physical.sphere-radius-nonpositive");
    }
}

public readonly record struct CapsuleColliderV1(Vec3MmV1 SegmentStartMm, Vec3MmV1 SegmentEndMm, long RadiusMm)
{
    public void Validate()
    {
        if (RadiusMm <= 0) throw new InvalidDataException("physical.capsule-radius-nonpositive");
        if (SegmentStartMm == SegmentEndMm) throw new InvalidDataException("physical.capsule-segment-degenerate");
    }
}

public readonly record struct OrientedBoxColliderV1(
    Vec3MmV1 CenterMm,
    Vec3MmV1 HalfExtentsMm,
    QuaternionQ30V1 Orientation)
{
    public void Validate()
    {
        if (HalfExtentsMm.X <= 0 || HalfExtentsMm.Y <= 0 || HalfExtentsMm.Z <= 0)
            throw new InvalidDataException("physical.obb-half-extent-nonpositive");
        if (Orientation.X == 0 && Orientation.Y == 0 && Orientation.Z == 0 && Orientation.W == 0)
            throw new InvalidDataException("physical.obb-orientation-zero");
    }
}

public static class DeterministicPrimitiveCollisionV1
{
    private const long Q30 = 1L << 30;

    public static bool Intersects(SphereColliderV1 left, SphereColliderV1 right)
    {
        left.Validate();
        right.Validate();
        var radius = checked(left.RadiusMm + right.RadiusMm);
        return SquaredDistance(left.CenterMm, right.CenterMm) <= CheckedSquare(radius);
    }

    public static bool Intersects(SphereColliderV1 sphere, CapsuleColliderV1 capsule)
    {
        sphere.Validate();
        capsule.Validate();
        var closest = ClosestPointOnSegment(sphere.CenterMm, capsule.SegmentStartMm, capsule.SegmentEndMm);
        var radius = checked(sphere.RadiusMm + capsule.RadiusMm);
        return SquaredDistance(sphere.CenterMm, closest) <= CheckedSquare(radius);
    }

    public static bool Intersects(CapsuleColliderV1 capsule, SphereColliderV1 sphere)
        => Intersects(sphere, capsule);

    public static bool Intersects(OrientedBoxColliderV1 left, OrientedBoxColliderV1 right)
    {
        left.Validate();
        right.Validate();
        var leftAxes = Axes(left.Orientation);
        var rightAxes = Axes(right.Orientation);
        var delta = Subtract(right.CenterMm, left.CenterMm);

        Span<AxisQ30V1> axes = stackalloc AxisQ30V1[15];
        var count = 0;
        foreach (var axis in leftAxes) axes[count++] = axis;
        foreach (var axis in rightAxes) axes[count++] = axis;
        foreach (var leftAxis in leftAxes)
        foreach (var rightAxis in rightAxes)
        {
            var cross = CrossQ30(leftAxis, rightAxis);
            if (!cross.IsZero) axes[count++] = cross;
        }

        for (var index = 0; index < count; index++)
        {
            var axis = axes[index];
            var distance = Abs(Project(delta, axis));
            var leftRadius = ProjectionRadius(left.HalfExtentsMm, leftAxes, axis);
            var rightRadius = ProjectionRadius(right.HalfExtentsMm, rightAxes, axis);
            if (distance > checked((Int128)leftRadius + rightRadius)) return false;
        }
        return true;
    }

    private static Vec3MmV1 ClosestPointOnSegment(Vec3MmV1 point, Vec3MmV1 start, Vec3MmV1 end)
    {
        var direction = Subtract(end, start);
        var fromStart = Subtract(point, start);
        var denominator = Dot(direction, direction);
        if (denominator <= 0) throw new InvalidDataException("physical.segment-degenerate");
        var numerator = Dot(fromStart, direction);
        if (numerator <= 0) return start;
        if (numerator >= denominator) return end;
        return new Vec3MmV1(
            checked(start.X + PhysicalIntegerMathV1.DivideRoundToEven(checked((Int128)direction.X * numerator), denominator)),
            checked(start.Y + PhysicalIntegerMathV1.DivideRoundToEven(checked((Int128)direction.Y * numerator), denominator)),
            checked(start.Z + PhysicalIntegerMathV1.DivideRoundToEven(checked((Int128)direction.Z * numerator), denominator)));
    }

    private static AxisQ30V1[] Axes(QuaternionQ30V1 quaternion)
    {
        var x = (Int128)quaternion.X;
        var y = (Int128)quaternion.Y;
        var z = (Int128)quaternion.Z;
        var w = (Int128)quaternion.W;

        long Term(Int128 numerator) => PhysicalIntegerMathV1.DivideRoundToEven(numerator, Q30);
        var two = (Int128)2;
        return
        [
            new AxisQ30V1(
                checked(Q30 - Term(checked(two * checked(y * y + z * z)))),
                Term(checked(two * checked(x * y + z * w))),
                Term(checked(two * checked(x * z - y * w)))),
            new AxisQ30V1(
                Term(checked(two * checked(x * y - z * w))),
                checked(Q30 - Term(checked(two * checked(x * x + z * z)))),
                Term(checked(two * checked(y * z + x * w)))),
            new AxisQ30V1(
                Term(checked(two * checked(x * z + y * w))),
                Term(checked(two * checked(y * z - x * w))),
                checked(Q30 - Term(checked(two * checked(x * x + y * y))))),
        ];
    }

    private static AxisQ30V1 CrossQ30(AxisQ30V1 left, AxisQ30V1 right)
        => new(
            PhysicalIntegerMathV1.DivideRoundToEven(
                checked(checked((Int128)left.Y * right.Z) - checked((Int128)left.Z * right.Y)), Q30),
            PhysicalIntegerMathV1.DivideRoundToEven(
                checked(checked((Int128)left.Z * right.X) - checked((Int128)left.X * right.Z)), Q30),
            PhysicalIntegerMathV1.DivideRoundToEven(
                checked(checked((Int128)left.X * right.Y) - checked((Int128)left.Y * right.X)), Q30));

    private static long ProjectionRadius(Vec3MmV1 halfExtents, AxisQ30V1[] boxAxes, AxisQ30V1 axis)
    {
        var numerator = checked(
            checked((Int128)halfExtents.X * Abs(Dot(boxAxes[0], axis))) +
            checked((Int128)halfExtents.Y * Abs(Dot(boxAxes[1], axis))) +
            checked((Int128)halfExtents.Z * Abs(Dot(boxAxes[2], axis))));
        return PhysicalIntegerMathV1.DivideRoundToEven(numerator, Q30);
    }

    private static Int128 Project(Vec3MmV1 value, AxisQ30V1 axis)
        => checked(checked((Int128)value.X * axis.X) +
                   checked((Int128)value.Y * axis.Y) +
                   checked((Int128)value.Z * axis.Z));

    private static Int128 Dot(AxisQ30V1 left, AxisQ30V1 right)
        => checked(checked((Int128)left.X * right.X) +
                   checked((Int128)left.Y * right.Y) +
                   checked((Int128)left.Z * right.Z));

    private static Int128 Dot(Vec3MmV1 left, Vec3MmV1 right)
        => checked(checked((Int128)left.X * right.X) +
                   checked((Int128)left.Y * right.Y) +
                   checked((Int128)left.Z * right.Z));

    private static Vec3MmV1 Subtract(Vec3MmV1 left, Vec3MmV1 right)
        => new(checked(left.X - right.X), checked(left.Y - right.Y), checked(left.Z - right.Z));

    private static UInt128 SquaredDistance(Vec3MmV1 left, Vec3MmV1 right)
    {
        var delta = Subtract(left, right);
        try
        {
            var x = AbsToUInt128(delta.X);
            var y = AbsToUInt128(delta.Y);
            var z = AbsToUInt128(delta.Z);
            return checked(checked(x * x) + checked(y * y) + checked(z * z));
        }
        catch (OverflowException ex)
        {
            throw new OverflowException("simulation.numeric-overflow", ex);
        }
    }

    private static UInt128 CheckedSquare(long value)
    {
        try
        {
            var unsigned = AbsToUInt128(value);
            return checked(unsigned * unsigned);
        }
        catch (OverflowException ex)
        {
            throw new OverflowException("simulation.numeric-overflow", ex);
        }
    }

    private static UInt128 AbsToUInt128(long value)
        => value >= 0 ? (UInt128)value : (UInt128)(-(Int128)value);

    private static Int128 Abs(Int128 value) => value < 0 ? checked(-value) : value;

    private readonly record struct AxisQ30V1(long X, long Y, long Z)
    {
        public bool IsZero => X == 0 && Y == 0 && Z == 0;
    }
}

public readonly record struct StaticTriangleV1(uint CanonicalIndex, Vec3MmV1 A, Vec3MmV1 B, Vec3MmV1 C);

/// <summary>
/// Deterministic static-mesh broad/narrow query boundary. Dynamic concave bodies are intentionally
/// excluded; callers receive candidate triangles in canonical index order and perform the required
/// fixed-point triangle test for their query kind.
/// </summary>
public sealed class TriangleMeshStaticV1
{
    private readonly StaticTriangleV1[] _triangles;

    public TriangleMeshStaticV1(IEnumerable<StaticTriangleV1> triangles)
    {
        ArgumentNullException.ThrowIfNull(triangles);
        _triangles = triangles.OrderBy(static triangle => triangle.CanonicalIndex).ToArray();
        if (_triangles.Length == 0) throw new ArgumentException("Static mesh requires at least one triangle.", nameof(triangles));
        if (_triangles.GroupBy(static triangle => triangle.CanonicalIndex).Any(static group => group.Count() != 1))
            throw new InvalidDataException("physical.static-mesh-duplicate-triangle-index");
    }

    public IReadOnlyList<StaticTriangleV1> QueryAabb(Vec3MmV1 minimum, Vec3MmV1 maximum)
    {
        if (minimum.X > maximum.X || minimum.Y > maximum.Y || minimum.Z > maximum.Z)
            throw new InvalidDataException("physical.static-mesh-invalid-aabb");
        return Array.AsReadOnly(_triangles.Where(triangle => Overlaps(triangle, minimum, maximum)).ToArray());
    }

    private static bool Overlaps(StaticTriangleV1 triangle, Vec3MmV1 minimum, Vec3MmV1 maximum)
    {
        var triangleMin = new Vec3MmV1(
            Math.Min(triangle.A.X, Math.Min(triangle.B.X, triangle.C.X)),
            Math.Min(triangle.A.Y, Math.Min(triangle.B.Y, triangle.C.Y)),
            Math.Min(triangle.A.Z, Math.Min(triangle.B.Z, triangle.C.Z)));
        var triangleMax = new Vec3MmV1(
            Math.Max(triangle.A.X, Math.Max(triangle.B.X, triangle.C.X)),
            Math.Max(triangle.A.Y, Math.Max(triangle.B.Y, triangle.C.Y)),
            Math.Max(triangle.A.Z, Math.Max(triangle.B.Z, triangle.C.Z)));
        return triangleMax.X >= minimum.X && triangleMin.X <= maximum.X &&
               triangleMax.Y >= minimum.Y && triangleMin.Y <= maximum.Y &&
               triangleMax.Z >= minimum.Z && triangleMin.Z <= maximum.Z;
    }
}
