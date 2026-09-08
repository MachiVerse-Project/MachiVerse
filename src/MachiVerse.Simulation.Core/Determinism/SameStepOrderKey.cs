using System.Buffers.Binary;

namespace MachiVerse.Simulation.Core.Determinism;

public sealed class SameStepOrderKey : IComparable<SameStepOrderKey>
{
    public const int ConflictScopeDigestLength = 32;
    public const int DatabaseKeyLength = 55;

    private readonly byte[] _conflictScopeDigest;

    public SameStepOrderKey(byte phase, ushort domainRank, ReadOnlySpan<byte> conflictScopeDigest, int semanticPriority, OpaqueId128 intentId)
    {
        if (phase > 5) throw new ArgumentOutOfRangeException(nameof(phase), "Standard OrderPhase is 0..5.");
        if (conflictScopeDigest.Length != ConflictScopeDigestLength)
            throw new ArgumentException("ConflictScopeDigest requires exactly 32 bytes.", nameof(conflictScopeDigest));
        if (intentId.IsZero) throw new ArgumentException("IntentId must be non-zero.", nameof(intentId));

        Phase = phase;
        DomainRank = domainRank;
        _conflictScopeDigest = conflictScopeDigest.ToArray();
        SemanticPriority = semanticPriority;
        IntentId = intentId;
    }

    public byte Phase { get; }
    public ushort DomainRank { get; }
    public ReadOnlySpan<byte> ConflictScopeDigest => _conflictScopeDigest;
    public int SemanticPriority { get; }
    public OpaqueId128 IntentId { get; }

    public int CompareTo(SameStepOrderKey? other)
    {
        if (other is null) return 1;
        var comparison = Phase.CompareTo(other.Phase);
        if (comparison != 0) return comparison;
        comparison = DomainRank.CompareTo(other.DomainRank);
        if (comparison != 0) return comparison;
        comparison = _conflictScopeDigest.AsSpan().SequenceCompareTo(other._conflictScopeDigest);
        if (comparison != 0) return comparison;
        comparison = SemanticPriority.CompareTo(other.SemanticPriority);
        if (comparison != 0) return comparison;
        return IntentId.CompareTo(other.IntentId);
    }

    public byte[] ToDatabaseBytes()
    {
        var bytes = new byte[DatabaseKeyLength];
        bytes[0] = Phase;
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(1, 2), DomainRank);
        _conflictScopeDigest.CopyTo(bytes.AsSpan(3, ConflictScopeDigestLength));
        var sortablePriority = unchecked((uint)(SemanticPriority ^ int.MinValue));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(35, 4), sortablePriority);
        IntentId.ToBytes().CopyTo(bytes.AsSpan(39, 16));
        return bytes;
    }
}
