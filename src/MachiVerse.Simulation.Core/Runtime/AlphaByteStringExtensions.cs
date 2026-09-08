using Google.Protobuf;

namespace MachiVerse.Simulation.Core.Runtime;

internal static class AlphaByteStringExtensions
{
    internal static ByteString Clone(this ByteString value)
        => ByteString.CopyFrom((value ?? throw new ArgumentNullException(nameof(value))).Span);
}
