using System.Buffers.Binary;

namespace MachiVerse.Simulation.Core.Determinism;

public readonly record struct RandomContextV1(
    OpaqueId128 WorldId,
    ulong Step,
    StableToken Domain,
    StableToken Purpose,
    OpaqueId128 SubjectEntityId,
    OpaqueId128 EventId,
    OpaqueId128 OperationId,
    ulong LocalOrdinal);

public static class DeterministicRandom
{
    public static ulong RandomWord64(WorldSeed256 worldSeed, RandomContextV1 context, ulong drawIndex, ulong retryIndex = 0)
    {
        var digest = HashSuite.DomainHash("mv.random.v1", writer =>
        {
            writer.WriteMapStart(4);
            writer.WriteUnsigned(0); writer.WriteBytes(worldSeed.ToBytes());
            writer.WriteUnsigned(1); WriteContext(writer, context);
            writer.WriteUnsigned(2); writer.WriteUnsigned(drawIndex);
            writer.WriteUnsigned(3); writer.WriteUnsigned(retryIndex);
        });
        return BinaryPrimitives.ReadUInt64BigEndian(digest.AsSpan(0, 8));
    }

    public static ulong BoundedUInt64(WorldSeed256 seed, RandomContextV1 context, ulong drawIndex, ulong bound)
    {
        if (bound == 0) throw new ArgumentOutOfRangeException(nameof(bound));
        var twoTo64 = (UInt128)ulong.MaxValue + 1;
        var limit = (twoTo64 / bound) * bound;
        for (ulong retry = 0; ; retry++)
        {
            var x = RandomWord64(seed, context, drawIndex, retry);
            if ((UInt128)x < limit) return x % bound;
            if (retry == ulong.MaxValue) throw new InvalidOperationException("Random rejection sampling retry exhausted.");
        }
    }

    public static double UniformDouble(WorldSeed256 seed, RandomContextV1 context, ulong drawIndex)
    {
        var word = RandomWord64(seed, context, drawIndex);
        return (word >> 11) * (1.0 / (1UL << 53));
    }

    private static void WriteContext(MvDcborWriter writer, RandomContextV1 context)
    {
        writer.WriteMapStart(8);
        writer.WriteUnsigned(0); writer.WriteBytes(context.WorldId.ToBytes());
        writer.WriteUnsigned(1); writer.WriteUnsigned(context.Step);
        writer.WriteUnsigned(2); writer.WriteAsciiText(context.Domain.Value);
        writer.WriteUnsigned(3); writer.WriteAsciiText(context.Purpose.Value);
        writer.WriteUnsigned(4); writer.WriteBytes(context.SubjectEntityId.ToBytes());
        writer.WriteUnsigned(5); writer.WriteBytes(context.EventId.ToBytes());
        writer.WriteUnsigned(6); writer.WriteBytes(context.OperationId.ToBytes());
        writer.WriteUnsigned(7); writer.WriteUnsigned(context.LocalOrdinal);
    }
}
