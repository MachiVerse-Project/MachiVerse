using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace MachiVerse.Simulation.Core.Determinism;

public sealed class MvDcborWriter
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
        if (value.Any(static c => c > 0x7f)) throw new ArgumentException("MV-DCBOR StableToken text must be ASCII.", nameof(value));
        var byteCount = Encoding.ASCII.GetByteCount(value);
        WriteInitialValue(3, (ulong)byteCount);
        var span = _buffer.GetSpan(byteCount);
        Encoding.ASCII.GetBytes(value, span);
        _buffer.Advance(byteCount);
    }

    public void WriteArrayStart(ulong count) => WriteInitialValue(4, count);
    public void WriteMapStart(ulong count) => WriteInitialValue(5, count);
    public void WriteBoolean(bool value) => WriteByte(value ? (byte)0xf5 : (byte)0xf4);

    // Appends one already-canonical MV-DCBOR value without wrapping it as a byte string.
    // Callers should only pass bytes produced by this writer or a verified golden fixture.
    public void WriteCanonicalValue(ReadOnlySpan<byte> canonicalValue)
    {
        if (canonicalValue.IsEmpty) throw new ArgumentException("Canonical MV-DCBOR value cannot be empty.", nameof(canonicalValue));
        WriteRaw(canonicalValue);
    }

    public byte[] ToArray() => _buffer.WrittenSpan.ToArray();

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
