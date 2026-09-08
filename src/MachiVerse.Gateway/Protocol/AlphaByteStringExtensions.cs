using Google.Protobuf;

namespace MachiVerse.Gateway.Protocol;

internal static class AlphaByteStringExtensions
{
    internal static ByteString Clone(this ByteString value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return ByteString.CopyFrom(value.Span);
    }
}
