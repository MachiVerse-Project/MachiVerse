using System.Buffers.Binary;
using System.Globalization;

namespace MachiVerse.Simulation.Core.Determinism;

public readonly record struct OpaqueId128(UInt128 Value) : IComparable<OpaqueId128>
{
    public static OpaqueId128 Zero => new(0);
    public bool IsZero => Value == 0;

    public byte[] ToBytes()
    {
        var bytes = new byte[16];
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(0, 8), (ulong)(Value >> 64));
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(8, 8), (ulong)Value);
        return bytes;
    }

    public static OpaqueId128 FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 16) throw new ArgumentException("OpaqueId128 requires exactly 16 bytes.", nameof(bytes));
        var high = BinaryPrimitives.ReadUInt64BigEndian(bytes[..8]);
        var low = BinaryPrimitives.ReadUInt64BigEndian(bytes[8..]);
        return new OpaqueId128(((UInt128)high << 64) | low);
    }

    public static OpaqueId128 Parse(string lowercaseHex)
    {
        if (lowercaseHex.Length != 32 || lowercaseHex.Any(static c => c is >= 'A' and <= 'F'))
            throw new FormatException("OpaqueId128 canonical text is 32 lowercase hexadecimal digits.");
        return new OpaqueId128(UInt128.Parse(lowercaseHex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
    }

    public int CompareTo(OpaqueId128 other) => Value.CompareTo(other.Value);
    public override string ToString() => Value.ToString("x32", CultureInfo.InvariantCulture);
}

public readonly struct WorldSeed256 : IEquatable<WorldSeed256>
{
    private readonly ulong _a;
    private readonly ulong _b;
    private readonly ulong _c;
    private readonly ulong _d;

    public WorldSeed256(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 32) throw new ArgumentException("WorldSeed256 requires exactly 32 bytes.", nameof(bytes));
        _a = BinaryPrimitives.ReadUInt64BigEndian(bytes[0..8]);
        _b = BinaryPrimitives.ReadUInt64BigEndian(bytes[8..16]);
        _c = BinaryPrimitives.ReadUInt64BigEndian(bytes[16..24]);
        _d = BinaryPrimitives.ReadUInt64BigEndian(bytes[24..32]);
    }

    public byte[] ToBytes()
    {
        var bytes = new byte[32];
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(0, 8), _a);
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(8, 8), _b);
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(16, 8), _c);
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(24, 8), _d);
        return bytes;
    }

    public bool Equals(WorldSeed256 other) => _a == other._a && _b == other._b && _c == other._c && _d == other._d;
    public override bool Equals(object? obj) => obj is WorldSeed256 other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(_a, _b, _c, _d);
}
