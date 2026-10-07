using System.Collections.ObjectModel;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

/// <summary>
/// Memory-bounded all-97 record-schema resolver.
///
/// Record schema is uniform within one partition source, so a global dictionary entry per record is
/// unnecessary. This resolver keeps the existing canonical per-partition sorted RecordId lists and
/// performs binary search inside the referenced partition. It therefore preserves the exact
/// IDomainRecordSchemaResolverV1 semantics while avoiding a second dictionary object for every
/// recovered or production record.
/// </summary>
public sealed class DomainSnapshotCompactReferenceResolverV1 : IDomainRecordSchemaResolverV1
{
    private sealed record PartitionIndexV1(
        SchemaRefV1 RecordSchema,
        IReadOnlyList<OpaqueId128> RecordIdsCanonical);

    private readonly IReadOnlyDictionary<string, PartitionIndexV1> _partitions;

    public DomainSnapshotCompactReferenceResolverV1(
        IEnumerable<IDomainPartitionSnapshotReferenceSourceV1> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var materialized = sources.ToArray();
        if (materialized.Length != StandardDomainPartitionRegistry.StandardPartitionCount)
            throw new InvalidDataException("persistence.snapshot.reference-source-count-not-97");

        var map = new Dictionary<string, PartitionIndexV1>(StringComparer.Ordinal);
        ulong recordCount = 0;
        foreach (var source in materialized)
        {
            ArgumentNullException.ThrowIfNull(source);
            var identity = StandardDomainPartitionRegistry.Get(source.PartitionId.Value);
            if (!StandardDomainRecordSchemaMigrationRegistryV1.IsAllowedRecordSchema(
                    identity.PartitionId.Value,
                    source.RecordSchema))
                throw new InvalidDataException($"persistence.snapshot.reference-source-schema-mismatch:{identity.PartitionId.Value}");
            if (source.ActualItemCount != checked((ulong)source.RecordIdsCanonical.Count))
                throw new InvalidDataException($"persistence.snapshot.reference-source-count-mismatch:{identity.PartitionId.Value}");

            OpaqueId128? previous = null;
            foreach (var recordId in source.RecordIdsCanonical)
            {
                if (recordId.IsZero)
                    throw new InvalidDataException($"persistence.snapshot.reference-source-zero-id:{identity.PartitionId.Value}");
                if (previous is { } prior && prior.CompareTo(recordId) >= 0)
                    throw new InvalidDataException($"persistence.snapshot.reference-source-order:{identity.PartitionId.Value}");
                previous = recordId;
            }

            if (!map.TryAdd(
                    identity.PartitionId.Value,
                    new PartitionIndexV1(source.RecordSchema, source.RecordIdsCanonical)))
                throw new InvalidDataException($"persistence.snapshot.reference-source-duplicate:{identity.PartitionId.Value}");
            recordCount = checked(recordCount + source.ActualItemCount);
        }

        foreach (var identity in StandardDomainPartitionRegistry.Entries)
        {
            if (!map.ContainsKey(identity.PartitionId.Value))
                throw new InvalidDataException($"persistence.snapshot.reference-source-missing:{identity.PartitionId.Value}");
        }

        RecordCount = recordCount;
        _partitions = new ReadOnlyDictionary<string, PartitionIndexV1>(map);
    }

    public ulong RecordCount { get; }

    public bool Exists(PartitionRecordRefV1 reference)
        => TryGetRecordSchema(reference, out _);

    public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
    {
        if (!_partitions.TryGetValue(reference.PartitionId.Value, out var partition) ||
            !Contains(partition.RecordIdsCanonical, reference.RecordId))
        {
            schema = default;
            return false;
        }

        schema = partition.RecordSchema;
        return true;
    }

    private static bool Contains(IReadOnlyList<OpaqueId128> values, OpaqueId128 target)
    {
        var low = 0;
        var high = values.Count - 1;
        while (low <= high)
        {
            var mid = low + ((high - low) >> 1);
            var comparison = values[mid].CompareTo(target);
            if (comparison == 0) return true;
            if (comparison < 0) low = mid + 1;
            else high = mid - 1;
        }
        return false;
    }
}
