using System.Buffers.Binary;

namespace MachiVerse.Simulation.Core.Persistence;

public static class U64Be
{
    public const int ByteLength = 8;

    public static byte[] Encode(ulong value)
    {
        var bytes = new byte[ByteLength];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        return bytes;
    }

    public static ulong Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != ByteLength)
            throw new ArgumentException("U64BE requires exactly 8 bytes.", nameof(bytes));
        return BinaryPrimitives.ReadUInt64BigEndian(bytes);
    }
}
