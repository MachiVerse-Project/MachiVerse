using System.Buffers.Binary;
using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Canonical perf.reference.v1 genesis scalar source from the Alpha 1.1 normative closure.
/// Every derived value is a pure function of WorldSeed, authoritative record id, and field tag;
/// callers must still apply any more-specific field rule before using these generic conversions.
/// </summary>
public static class Qa04ReferenceGenesisValueSourceV1
{
    private const string HashDomain = "mv.perf-reference-genesis-value.v1";

    public static byte[] Hash(OpaqueId128 recordId, string fieldTag)
    {
        if (recordId.IsZero) throw new ArgumentException("Record id ZERO is invalid.", nameof(recordId));
        if (string.IsNullOrWhiteSpace(fieldTag)) throw new ArgumentException("Field tag is required.", nameof(fieldTag));

        return HashSuite.DomainHash(HashDomain, writer =>
        {
            writer.WriteArrayStart(4);
            writer.WriteAsciiText(Qa04ReferenceLoadV1.BenchmarkProfileId);
            writer.WriteBytes(Qa04ReferenceLoadV1.WorldSeed.ToBytes());
            writer.WriteBytes(recordId.ToBytes());
            writer.WriteAsciiText(fieldTag);
        });
    }

    public static ulong U64(OpaqueId128 recordId, string fieldTag)
        => BinaryPrimitives.ReadUInt64BigEndian(Hash(recordId, fieldTag));

    public static uint BoundedPpm(OpaqueId128 recordId, string fieldTag)
        => checked(500_000u + (uint)(U64(recordId, fieldTag) % 400_001UL));

    public static ulong PositiveCount(OpaqueId128 recordId, string fieldTag)
        => checked(1UL + U64(recordId, fieldTag) % 1_000UL);

    public static long Money(OpaqueId128 recordId, string fieldTag)
        => checked(1_000L + (long)(U64(recordId, fieldTag) % 1_000_000UL));

    public static long SmallSignedValue(OpaqueId128 recordId, string fieldTag)
        => checked((long)(U64(recordId, fieldTag) % 2_001UL) - 1_000L);
}
