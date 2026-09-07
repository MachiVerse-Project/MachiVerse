using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace MachiVerse.Gateway.Audit;

internal sealed class MvDcborWriter
{
    private readonly ArrayBufferWriter<byte> _buffer = new();

    public void WriteUnsigned(ulong value) => WriteInitialValue(0, value);

    public void WriteInt64(long value)
    {
        if (value >= 0) WriteInitialValue(0, (ulong)value);
        else WriteInitialValue(1, checked((ulong)(-1 - value)));
    }

    public void WriteBytes(ReadOnlySpan<byte> value)
    {
        WriteInitialValue(2, (ulong)value.Length);
        WriteRaw(value);
    }

    public void WriteAsciiText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Any(static c => c > 0x7f))
            throw new ArgumentException("MV-DCBOR StableToken text must be ASCII.", nameof(value));
        var bytes = Encoding.ASCII.GetBytes(value);
        WriteInitialValue(3, (ulong)bytes.Length);
        WriteRaw(bytes);
    }

    public void WriteUtf8Text(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteInitialValue(3, (ulong)bytes.Length);
        WriteRaw(bytes);
    }

    public void WriteArrayStart(ulong count) => WriteInitialValue(4, count);
    public void WriteMapStart(ulong count) => WriteInitialValue(5, count);
    public void WriteNull() => WriteByte(0xf6);
    public void WriteBoolean(bool value) => WriteByte(value ? (byte)0xf5 : (byte)0xf4);

    public void WriteCanonicalValue(ReadOnlySpan<byte> canonicalValue)
    {
        if (canonicalValue.IsEmpty)
            throw new ArgumentException("Canonical MV-DCBOR value cannot be empty.", nameof(canonicalValue));
        WriteRaw(canonicalValue);
    }

    public byte[] ToArray() => _buffer.WrittenSpan.ToArray();

    public static byte[] EncodeAsciiTextKey(string value)
    {
        var writer = new MvDcborWriter();
        writer.WriteAsciiText(value);
        return writer.ToArray();
    }

    private void WriteInitialValue(byte majorType, ulong value)
    {
        if (value < 24)
        {
            WriteByte((byte)((majorType << 5) | (byte)value));
            return;
        }

        if (value <= byte.MaxValue)
        {
            WriteByte((byte)((majorType << 5) | 24));
            WriteByte((byte)value);
            return;
        }

        if (value <= ushort.MaxValue)
        {
            WriteByte((byte)((majorType << 5) | 25));
            var span = _buffer.GetSpan(2);
            BinaryPrimitives.WriteUInt16BigEndian(span, (ushort)value);
            _buffer.Advance(2);
            return;
        }

        if (value <= uint.MaxValue)
        {
            WriteByte((byte)((majorType << 5) | 26));
            var span = _buffer.GetSpan(4);
            BinaryPrimitives.WriteUInt32BigEndian(span, (uint)value);
            _buffer.Advance(4);
            return;
        }

        WriteByte((byte)((majorType << 5) | 27));
        var destination = _buffer.GetSpan(8);
        BinaryPrimitives.WriteUInt64BigEndian(destination, value);
        _buffer.Advance(8);
    }

    private void WriteByte(byte value)
    {
        var span = _buffer.GetSpan(1);
        span[0] = value;
        _buffer.Advance(1);
    }

    private void WriteRaw(ReadOnlySpan<byte> value)
    {
        var span = _buffer.GetSpan(value.Length);
        value.CopyTo(span);
        _buffer.Advance(value.Length);
    }
}

internal sealed class BytewiseLexicographicComparer : IComparer<byte[]>
{
    public static readonly BytewiseLexicographicComparer Instance = new();

    public int Compare(byte[]? left, byte[]? right)
    {
        if (ReferenceEquals(left, right)) return 0;
        if (left is null) return -1;
        if (right is null) return 1;
        var common = Math.Min(left.Length, right.Length);
        for (var index = 0; index < common; index++)
        {
            var comparison = left[index].CompareTo(right[index]);
            if (comparison != 0) return comparison;
        }
        return left.Length.CompareTo(right.Length);
    }
}
