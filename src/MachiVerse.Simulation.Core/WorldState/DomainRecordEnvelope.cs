using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.WorldState;

public enum DetailLevelV1 : byte
{
    D0Entity = 0,
    D1LocalAggregate = 1,
    D2RegionalAggregate = 2,
    D3BoundarySummary = 3
}

public readonly record struct PartitionRecordRefV1
{
    public PartitionRecordRefV1(string partitionId, OpaqueId128 recordId)
        : this(new StableToken(partitionId), recordId)
    {
    }

    public PartitionRecordRefV1(StableToken partitionId, OpaqueId128 recordId)
    {
        if (recordId.IsZero) throw new ArgumentException("PartitionRecordId ZERO is invalid.", nameof(recordId));
        PartitionId = partitionId;
        RecordId = recordId;
    }

    public StableToken PartitionId { get; }
    public OpaqueId128 RecordId { get; }
}

public sealed class DomainRecordEnvelopeV1<TPayload>
{
    public DomainRecordEnvelopeV1(
        OpaqueId128 recordId,
        SchemaRefV1 recordSchema,
        ulong revision,
        ulong createdStep,
        ulong? retiredStep,
        DetailLevelV1 detailLevel,
        OpaqueId128? lineageRef,
        TPayload payload)
    {
        if (recordId.IsZero) throw new ArgumentException("PartitionRecordId ZERO is invalid.", nameof(recordId));
        if (revision == 0) throw new ArgumentOutOfRangeException(nameof(revision), "Record revision starts at 1.");
        if (retiredStep is not null && retiredStep.Value < createdStep)
            throw new ArgumentOutOfRangeException(nameof(retiredStep), "retired_step cannot precede created_step.");
        if (lineageRef is { IsZero: true })
            throw new ArgumentException("lineage_ref ZERO is invalid when present.", nameof(lineageRef));
        if (!Enum.IsDefined(detailLevel))
            throw new ArgumentOutOfRangeException(nameof(detailLevel));
        ArgumentNullException.ThrowIfNull(payload);

        RecordId = recordId;
        RecordSchema = recordSchema;
        Revision = revision;
        CreatedStep = createdStep;
        RetiredStep = retiredStep;
        DetailLevel = detailLevel;
        LineageRef = lineageRef;
        Payload = payload;
    }

    public OpaqueId128 RecordId { get; }
    public SchemaRefV1 RecordSchema { get; }
    public ulong Revision { get; }
    public ulong CreatedStep { get; }
    public ulong? RetiredStep { get; }
    public DetailLevelV1 DetailLevel { get; }
    public OpaqueId128? LineageRef { get; }
    public TPayload Payload { get; }
    public bool IsRetired => RetiredStep is not null;

    public DomainRecordEnvelopeV1<TPayload> Revise(TPayload nextPayload, DetailLevelV1? nextDetailLevel = null)
    {
        if (IsRetired) throw new InvalidOperationException("Retired records cannot be silently revised.");
        if (Revision == ulong.MaxValue) throw new OverflowException("Record revision cannot wrap.");
        return new DomainRecordEnvelopeV1<TPayload>(
            RecordId,
            RecordSchema,
            Revision + 1,
            CreatedStep,
            null,
            nextDetailLevel ?? DetailLevel,
            LineageRef,
            nextPayload);
    }

    public DomainRecordEnvelopeV1<TPayload> Retire(ulong retiredStep)
    {
        if (IsRetired) throw new InvalidOperationException("Record is already retired.");
        if (Revision == ulong.MaxValue) throw new OverflowException("Record revision cannot wrap.");
        return new DomainRecordEnvelopeV1<TPayload>(
            RecordId,
            RecordSchema,
            Revision + 1,
            CreatedStep,
            retiredStep,
            DetailLevel,
            LineageRef,
            Payload);
    }
}

public sealed class DomainPartitionStateV1<TPayload>
{
    private readonly SortedDictionary<OpaqueId128, DomainRecordEnvelopeV1<TPayload>>? _records;
    private readonly IReadOnlyList<DomainRecordEnvelopeV1<TPayload>>? _canonicalRecords;

    public DomainPartitionStateV1(
        DomainPartitionIdentityV1 identity,
        IEnumerable<DomainRecordEnvelopeV1<TPayload>> records)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(records);

        Identity = identity;
        _records = new SortedDictionary<OpaqueId128, DomainRecordEnvelopeV1<TPayload>>();
        foreach (var record in records)
        {
            ArgumentNullException.ThrowIfNull(record);
            if (record.RecordSchema != identity.RecordSchema)
                throw new InvalidDataException("domain.record-schema-mismatch");
            if (!_records.TryAdd(record.RecordId, record))
                throw new InvalidDataException("domain.duplicate-record-id");
        }
    }

    private DomainPartitionStateV1(
        DomainPartitionIdentityV1 identity,
        IReadOnlyList<DomainRecordEnvelopeV1<TPayload>> canonicalRecords,
        bool canonicalValidated)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(canonicalRecords);
        if (!canonicalValidated)
            throw new ArgumentException("Canonical-record fast path requires validated ordering.", nameof(canonicalValidated));

        Identity = identity;
        OpaqueId128? previous = null;
        for (var index = 0; index < canonicalRecords.Count; index++)
        {
            var record = canonicalRecords[index]
                ?? throw new InvalidDataException("domain.record-null");
            if (record.RecordSchema != identity.RecordSchema)
                throw new InvalidDataException("domain.record-schema-mismatch");
            if (record.RecordId.IsZero)
                throw new InvalidDataException("domain.record-id-zero");
            if (previous is { } prior && prior.CompareTo(record.RecordId) >= 0)
                throw new InvalidDataException("domain.record-order-not-canonical");
            previous = record.RecordId;
        }

        _canonicalRecords = canonicalRecords;
    }

    internal static DomainPartitionStateV1<TPayload> FromCanonicalRecords(
        DomainPartitionIdentityV1 identity,
        IReadOnlyList<DomainRecordEnvelopeV1<TPayload>> canonicalRecords)
        => new(identity, canonicalRecords, canonicalValidated: true);

    public DomainPartitionIdentityV1 Identity { get; }
    public ulong ItemCount => checked((ulong)(_records?.Count ?? _canonicalRecords!.Count));
    public IEnumerable<DomainRecordEnvelopeV1<TPayload>> RecordsCanonical
        => _records is not null ? _records.Values : _canonicalRecords!;

    public bool TryGet(OpaqueId128 recordId, out DomainRecordEnvelopeV1<TPayload>? record)
    {
        if (_records is not null)
            return _records.TryGetValue(recordId, out record);

        var records = _canonicalRecords!;
        var low = 0;
        var high = records.Count - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) >> 1);
            var candidate = records[middle];
            var comparison = candidate.RecordId.CompareTo(recordId);
            if (comparison == 0)
            {
                record = candidate;
                return true;
            }

            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }

        record = null;
        return false;
    }
}
