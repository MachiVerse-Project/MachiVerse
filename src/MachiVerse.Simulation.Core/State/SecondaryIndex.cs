using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.State;

public enum IndexAuthorityV1 : byte
{
    Authoritative = 0,
    DerivedRebuildable = 1,
}

public enum IndexUniquenessV1 : byte
{
    Unique = 0,
    MultiValue = 1,
}

public sealed record PartitionIndexDescriptorV1(
    StableToken IndexId,
    StableToken PartitionId,
    IndexAuthorityV1 Authority,
    SchemaRefV1 KeySchema,
    SchemaRefV1 ValueSchema,
    IndexUniquenessV1 Uniqueness,
    CanonicalOrderKindV1 CanonicalKeyOrder,
    SchemaRefV1? RebuildRecipe);

public sealed class CanonicalIndexKeyV1 : IComparable<CanonicalIndexKeyV1>, IEquatable<CanonicalIndexKeyV1>
{
    private readonly byte[] _bytes;

    public CanonicalIndexKeyV1(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) throw new ArgumentException("Index key cannot be empty.", nameof(bytes));
        _bytes = bytes.ToArray();
    }

    public ReadOnlyMemory<byte> Bytes => _bytes;

    public int CompareTo(CanonicalIndexKeyV1? other)
    {
        if (other is null) return 1;
        return _bytes.AsSpan().SequenceCompareTo(other._bytes);
    }

    public bool Equals(CanonicalIndexKeyV1? other)
        => other is not null && _bytes.AsSpan().SequenceEqual(other._bytes);

    public override bool Equals(object? obj) => obj is CanonicalIndexKeyV1 other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var value in _bytes) hash.Add(value);
        return hash.ToHashCode();
    }
}

public readonly record struct PartitionRecordRefV1(StableToken PartitionId, OpaqueId128 RecordId)
    : IComparable<PartitionRecordRefV1>
{
    public PartitionRecordRefV1(StableToken partitionId, OpaqueId128 recordId) : this()
    {
        if (recordId.IsZero) throw new ArgumentException("PartitionRecordRef RecordId ZERO is invalid.", nameof(recordId));
        PartitionId = partitionId;
        RecordId = recordId;
    }

    public int CompareTo(PartitionRecordRefV1 other)
    {
        var partition = string.CompareOrdinal(PartitionId.Value, other.PartitionId.Value);
        return partition != 0 ? partition : RecordId.CompareTo(other.RecordId);
    }
}

public sealed record DerivedIndexBucketV1(
    CanonicalIndexKeyV1 Key,
    IReadOnlyList<PartitionRecordRefV1> Records);

public sealed class DerivedPartitionIndexV1
{
    private readonly IReadOnlyList<DerivedIndexBucketV1> _buckets;

    internal DerivedPartitionIndexV1(PartitionIndexDescriptorV1 descriptor, IReadOnlyList<DerivedIndexBucketV1> buckets)
    {
        Descriptor = descriptor;
        _buckets = buckets;
    }

    public PartitionIndexDescriptorV1 Descriptor { get; }
    public IReadOnlyList<DerivedIndexBucketV1> Buckets => _buckets;

    public bool CanonicallyEquivalentTo(DerivedPartitionIndexV1 other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Descriptor != other.Descriptor || Buckets.Count != other.Buckets.Count) return false;
        for (var i = 0; i < Buckets.Count; i++)
        {
            var left = Buckets[i];
            var right = other.Buckets[i];
            if (!left.Key.Equals(right.Key) || !left.Records.SequenceEqual(right.Records)) return false;
        }
        return true;
    }
}

public static class PartitionSecondaryIndexBuilder
{
    public static DerivedPartitionIndexV1 Rebuild(
        DomainPartitionStateV1 partition,
        PartitionIndexDescriptorV1 descriptor,
        Func<DomainRecordEnvelopeV1, IEnumerable<CanonicalIndexKeyV1>> selectKeys)
    {
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(selectKeys);
        if (descriptor.PartitionId != partition.Header.PartitionId)
            throw new InvalidDataException("state.index.partition-id-mismatch");
        if (descriptor.Authority != IndexAuthorityV1.DerivedRebuildable)
            throw new InvalidOperationException("state.index.authoritative-index-cannot-use-derived-rebuild");
        if (descriptor.CanonicalKeyOrder != CanonicalOrderKindV1.RecordIdBytewiseAscending)
            throw new InvalidDataException("state.index.unsupported-canonical-key-order");

        var buckets = new SortedDictionary<CanonicalIndexKeyV1, SortedSet<PartitionRecordRefV1>>();
        foreach (var record in partition.Records)
        {
            var keys = selectKeys(record)
                ?? throw new InvalidDataException("state.index.key-selector-returned-null");
            foreach (var key in keys.Distinct().Order())
            {
                if (!buckets.TryGetValue(key, out var refs))
                {
                    refs = [];
                    buckets.Add(key, refs);
                }
                refs.Add(new PartitionRecordRefV1(partition.Header.PartitionId, record.RecordId));
                if (descriptor.Uniqueness == IndexUniquenessV1.Unique && refs.Count > 1)
                    throw new InvalidDataException("state.index.uniqueness-violation");
            }
        }

        var materialized = buckets
            .Select(static pair => new DerivedIndexBucketV1(pair.Key, Array.AsReadOnly(pair.Value.ToArray())))
            .ToArray();
        return new DerivedPartitionIndexV1(descriptor, Array.AsReadOnly(materialized));
    }
}
