namespace MachiVerse.Simulation.Core.Determinism;

public readonly record struct FixedQ32_32(long Raw) : IComparable<FixedQ32_32>
{
    public const int FractionalBits = 32;
    private static readonly Int128 Scale = (Int128)1 << FractionalBits;

    public static FixedQ32_32 Zero => new(0);
    public static FixedQ32_32 One => new(1L << FractionalBits);

    public static FixedQ32_32 FromInteger(long value)
        => new(checked(value * (1L << FractionalBits)));

    public static FixedQ32_32 FromRatio(long numerator, long denominator)
    {
        if (denominator == 0) throw new DivideByZeroException();
        var scaled = (Int128)numerator << FractionalBits;
        return new FixedQ32_32(ToInt64Checked(DivideRoundToEven(scaled, denominator)));
    }

    public long RoundToInteger() => ToInt64Checked(DivideRoundToEven(Raw, Scale));
    public int CompareTo(FixedQ32_32 other) => Raw.CompareTo(other.Raw);

    public static FixedQ32_32 operator +(FixedQ32_32 left, FixedQ32_32 right)
        => new(checked(left.Raw + right.Raw));
    public static FixedQ32_32 operator -(FixedQ32_32 left, FixedQ32_32 right)
        => new(checked(left.Raw - right.Raw));
    public static FixedQ32_32 operator -(FixedQ32_32 value)
        => new(checked(-value.Raw));

    public static FixedQ32_32 operator *(FixedQ32_32 left, FixedQ32_32 right)
    {
        var product = (Int128)left.Raw * right.Raw;
        return new FixedQ32_32(ToInt64Checked(DivideRoundToEven(product, Scale)));
    }

    private static long ToInt64Checked(Int128 value)
    {
        if (value < long.MinValue || value > long.MaxValue) throw new OverflowException("FixedQ32_32 result exceeds int64 storage.");
        return (long)value;
    }

    private static Int128 DivideRoundToEven(Int128 numerator, Int128 denominator)
    {
        if (denominator == 0) throw new DivideByZeroException();
        if (denominator < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }

        var quotient = numerator / denominator;
        var remainder = numerator % denominator;
        var absoluteRemainder = remainder < 0 ? -remainder : remainder;
        var twiceRemainder = absoluteRemainder * 2;
        if (twiceRemainder > denominator ||
            (twiceRemainder == denominator && (quotient & (Int128)1) != 0))
        {
            quotient += numerator < 0 ? -1 : 1;
        }
        return quotient;
    }
}
