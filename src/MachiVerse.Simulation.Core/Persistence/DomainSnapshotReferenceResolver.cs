using System.Collections.ObjectModel;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

/// <summary>
/// Untyped actual-record source used to build the frozen Snapshot reference index. Implementations
/// must enumerate real DomainPartitionStateV1 records; header counts are not accepted as material.
/// </summary>
public interface IDomainPartitionSnapshotReferenceSourceV1
{
    StableToken PartitionId { get; }
    SchemaRefV1 RecordSchema { get; }
    ulong ActualItemCount { get; }
    IReadOnlyList<OpaqueId128> RecordIdsCanonical { get; }
}

public sealed class DomainPartitionSnapshotReferenceSourceV1<TPayload> : IDomainPartitionSnapshotReferenceSourceV1
{
    public DomainPartitionSnapshotReferenceSourceV1(DomainPartitionSnapshotAuthorityV1<TPayload> authority)
    {
        Authority = authority ?? throw new ArgumentNullException(nameof(authority));
        Authority.VerifyBoundAuthority();
        var ids = Authority.Partition.RecordsCanonical.Select(static record => record.RecordId).ToArray();
        if (checked((ulong)ids.Length) != Authority.ActualItemCount)
            throw new InvalidDataException($"persistence.snapshot.reference-source-count-mismatch:{Authority.PartitionId.Value}");
        for (var i = 1; i < ids.Length; i++)
        {
            if (ids[i - 1].CompareTo(ids[i]) >= 0)
                throw new InvalidDataException($"persistence.snapshot.reference-source-order:{Authority.PartitionId.Value}");
        }
        RecordIdsCanonical = Array.AsReadOnly(ids);
    }

    public DomainPartitionSnapshotAuthorityV1<TPayload> Authority { get; }
    public StableToken PartitionId => Authority.PartitionId;
    public SchemaRefV1 RecordSchema => Authority.Identity.RecordSchema;
    public ulong ActualItemCount => Authority.ActualItemCount;
    public IReadOnlyList<OpaqueId128> RecordIdsCanonical { get; }
}

/// <summary>
/// Frozen all-97 actual-record resolver used by Snapshot production/recovery semantic validation.
/// Construction fails unless every standard partition contributes exactly one actual record source.
/// </summary>
public sealed class DomainSnapshotReferenceResolverV1 : IDomainRecordSchemaResolverV1
{
    private readonly IReadOnlyDictionary<(string PartitionId, OpaqueId128 RecordId), SchemaRefV1> _records;

    public DomainSnapshotReferenceResolverV1(IEnumerable<IDomainPartitionSnapshotReferenceSourceV1> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var materialized = sources.ToArray();
        if (materialized.Length != StandardDomainPartitionRegistry.StandardPartitionCount)
            throw new InvalidDataException("persistence.snapshot.reference-source-count-not-97");

        var sourceByPartition = new Dictionary<string, IDomainPartitionSnapshotReferenceSourceV1>(StringComparer.Ordinal);
        foreach (var source in materialized)
        {
            ArgumentNullException.ThrowIfNull(source);
            var identity = StandardDomainPartitionRegistry.Get(source.PartitionId.Value);
            if (source.RecordSchema != identity.RecordSchema)
                throw new InvalidDataException($"persistence.snapshot.reference-source-schema-mismatch:{identity.PartitionId.Value}");
            if (source.ActualItemCount != checked((ulong)source.RecordIdsCanonical.Count))
                throw new InvalidDataException($"persistence.snapshot.reference-source-count-mismatch:{identity.PartitionId.Value}");
            if (!sourceByPartition.TryAdd(identity.PartitionId.Value, source))
                throw new InvalidDataException($"persistence.snapshot.reference-source-duplicate:{identity.PartitionId.Value}");
        }

        var records = new Dictionary<(string PartitionId, OpaqueId128 RecordId), SchemaRefV1>();
        foreach (var identity in StandardDomainPartitionRegistry.Entries)
        {
            if (!sourceByPartition.TryGetValue(identity.PartitionId.Value, out var source))
                throw new InvalidDataException($"persistence.snapshot.reference-source-missing:{identity.PartitionId.Value}");

            OpaqueId128? previous = null;
            foreach (var recordId in source.RecordIdsCanonical)
            {
                if (recordId.IsZero)
                    throw new InvalidDataException($"persistence.snapshot.reference-source-zero-id:{identity.PartitionId.Value}");
                if (previous is { } prior && prior.CompareTo(recordId) >= 0)
                    throw new InvalidDataException($"persistence.snapshot.reference-source-order:{identity.PartitionId.Value}");
                previous = recordId;
                if (!records.TryAdd((identity.PartitionId.Value, recordId), identity.RecordSchema))
                    throw new InvalidDataException($"persistence.snapshot.reference-source-record-duplicate:{identity.PartitionId.Value}");
            }
        }

        _records = new ReadOnlyDictionary<(string PartitionId, OpaqueId128 RecordId), SchemaRefV1>(records);
    }

    public ulong RecordCount => checked((ulong)_records.Count);

    public bool Exists(PartitionRecordRefV1 reference)
        => _records.ContainsKey((reference.PartitionId.Value, reference.RecordId));

    public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
        => _records.TryGetValue((reference.PartitionId.Value, reference.RecordId), out schema);
}
