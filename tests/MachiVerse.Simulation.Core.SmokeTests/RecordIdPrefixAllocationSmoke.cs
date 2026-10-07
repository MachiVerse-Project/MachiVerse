using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

internal static class RecordIdPrefixAllocationSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        // Compare against the canonical big-endian byte representation at both ends of every
        // prefix. Trailing bits must not affect the digest's existing 14-bit slice identity.
        var trailingMask = ((UInt128)1 << (128 - RecordIdPrefixPartitionDigestV2.PrefixBits)) - 1;
        for (var prefix = 0; prefix < RecordIdPrefixPartitionDigestV2.PrefixCount; prefix++)
        {
            var first = ((UInt128)prefix << (128 - RecordIdPrefixPartitionDigestV2.PrefixBits)) | 1;
            RequireCanonicalPrefix(new OpaqueId128(first), (ushort)prefix);
            RequireCanonicalPrefix(new OpaqueId128(first | trailingMask), (ushort)prefix);
        }
        try
        {
            RecordIdPrefixPartitionDigestV2.PrefixOf(OpaqueId128.Zero);
            throw new InvalidOperationException("ZERO RecordId was accepted.");
        }
        catch (ArgumentException) { }

        ConsumePrefixes(); // Warm JIT before observing only this thread's managed allocations.
        var before = GC.GetAllocatedBytesForCurrentThread();
        var checksum = ConsumePrefixes();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        if (checksum != (ulong)RecordIdPrefixPartitionDigestV2.PrefixCount *
            (RecordIdPrefixPartitionDigestV2.PrefixCount - 1) / 2 * 16)
            throw new InvalidOperationException("Prefix sweep was not fully evaluated.");
        if (allocated != 0)
            throw new InvalidOperationException($"Prefix lookup allocated {allocated} bytes in the digest hot path.");
        Console.WriteLine("RecordIdPrefixV2 canonical boundaries / ZERO rejection / zero allocation PASS");
    }

    private static void RequireCanonicalPrefix(OpaqueId128 id, ushort expected)
    {
        var bytes = id.ToBytes();
        var canonical = (ushort)(((uint)bytes[0] << 6) | ((uint)bytes[1] >> 2));
        if (canonical != expected || RecordIdPrefixPartitionDigestV2.PrefixOf(id) != canonical)
            throw new InvalidOperationException("RecordIdPrefixV2 differs from its canonical byte prefix.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong ConsumePrefixes()
    {
        ulong sum = 0;
        for (var sweep = 0; sweep < 16; sweep++)
            for (var prefix = 0; prefix < RecordIdPrefixPartitionDigestV2.PrefixCount; prefix++)
                sum += RecordIdPrefixPartitionDigestV2.PrefixOf(new OpaqueId128(
                    ((UInt128)prefix << (128 - RecordIdPrefixPartitionDigestV2.PrefixBits)) | (uint)(sweep + 1)));
        return sum;
    }
}
