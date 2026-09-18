using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains.SocietyEconomy;

public static class SocietyMarketTransactionPartitionIdentityV2
{
    public static DomainPartitionIdentityV1 Identity { get; } = Create();

    public static void ValidateCanonicalContract()
    {
        SocietyMarketTransactionRecordSchemaV2.ValidateCanonicalContract();
        var current = StandardDomainPartitionRegistry.Get(SocietyMarketTransactionRecordSchemaV2.PartitionId);
        if (Identity.PartitionId != current.PartitionId ||
            Identity.OwnerDomain != current.OwnerDomain ||
            Identity.OwnerDomainRank != current.OwnerDomainRank ||
            Identity.PartitionSchema != current.PartitionSchema ||
            Identity.PrimaryKeyKind != current.PrimaryKeyKind ||
            Identity.PersistenceClass != current.PersistenceClass ||
            Identity.CanonicalOrder != current.CanonicalOrder)
            throw new InvalidDataException("society.market-transaction-v2.partition-identity-drift");
        if (Identity.RecordSchema != SocietyMarketTransactionRecordSchemaV2.RecordSchema)
            throw new InvalidDataException("society.market-transaction-v2.partition-record-schema");
        if (current.RecordSchema.Version != new SchemaVersionV1(1, 0))
            throw new InvalidDataException("society.market-transaction-v2.production-registry-flipped");
    }

    private static DomainPartitionIdentityV1 Create()
    {
        var current = StandardDomainPartitionRegistry.Get(SocietyMarketTransactionRecordSchemaV2.PartitionId);
        return current with { RecordSchema = SocietyMarketTransactionRecordSchemaV2.RecordSchema };
    }
}

public sealed class SocietyMarketTransactionRecordSetV2
{
    private readonly SortedDictionary<OpaqueId128, SocietyMarketTransactionRecordMaterialV2>? _records;
    private IReadOnlyList<SocietyMarketTransactionRecordMaterialV2>? _canonicalRecords;

    public SocietyMarketTransactionRecordSetV2(IEnumerable<SocietyMarketTransactionRecordMaterialV2> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        _records = new SortedDictionary<OpaqueId128, SocietyMarketTransactionRecordMaterialV2>();
        foreach (var record in records)
        {
            ArgumentNullException.ThrowIfNull(record);
            if (!_records.TryAdd(record.RecordId, record))
                throw new InvalidDataException("society.market-transaction-v2.record-id-duplicate");
        }
    }

    private SocietyMarketTransactionRecordSetV2(
        IReadOnlyList<SocietyMarketTransactionRecordMaterialV2> canonicalRecords,
        bool canonicalValidated)
    {
        ArgumentNullException.ThrowIfNull(canonicalRecords);
        if (!canonicalValidated)
            throw new ArgumentException("Canonical-record fast path requires validated ordering.", nameof(canonicalValidated));

        OpaqueId128? previous = null;
        for (var index = 0; index < canonicalRecords.Count; index++)
        {
            var record = canonicalRecords[index]
                ?? throw new InvalidDataException("society.market-transaction-v2.record-null");
            if (record.RecordId.IsZero)
                throw new InvalidDataException("society.market-transaction-v2.record-id-zero");
            if (previous is { } prior && prior.CompareTo(record.RecordId) >= 0)
                throw new InvalidDataException("society.market-transaction-v2.record-order");
            previous = record.RecordId;
        }

        _canonicalRecords = canonicalRecords;
    }

    internal static SocietyMarketTransactionRecordSetV2 FromCanonicalRecords(
        IReadOnlyList<SocietyMarketTransactionRecordMaterialV2> canonicalRecords)
        => new(canonicalRecords, canonicalValidated: true);

    public IReadOnlyList<SocietyMarketTransactionRecordMaterialV2> RecordsCanonical
        => _canonicalRecords ??= Array.AsReadOnly(_records!.Values.ToArray());

    public bool TryGet(OpaqueId128 recordId, out SocietyMarketTransactionRecordMaterialV2? record)
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

public sealed class SocietyMarketTransactionPartitionStateV2
{
    public SocietyMarketTransactionPartitionStateV2(IEnumerable<SocietyMarketTransactionRecordMaterialV2> records)
    {
        SocietyMarketTransactionPartitionIdentityV2.ValidateCanonicalContract();
        RecordSet = new SocietyMarketTransactionRecordSetV2(records);
        var envelopes = RecordSet.RecordsCanonical.Select(static record =>
            new DomainRecordEnvelopeV1<SocietyMarketTransactionRecordPayloadV2>(
                record.RecordId,
                SocietyMarketTransactionRecordSchemaV2.RecordSchema,
                record.Revision,
                record.CreatedStep,
                record.RetiredStep,
                record.DetailLevel,
                record.LineageRef,
                record.Payload));
        State = new DomainPartitionStateV1<SocietyMarketTransactionRecordPayloadV2>(
            SocietyMarketTransactionPartitionIdentityV2.Identity,
            envelopes);
        RequireAlignedCount();
    }

    private SocietyMarketTransactionPartitionStateV2(
        SocietyMarketTransactionRecordSetV2 recordSet,
        DomainPartitionStateV1<SocietyMarketTransactionRecordPayloadV2> state)
    {
        SocietyMarketTransactionPartitionIdentityV2.ValidateCanonicalContract();
        RecordSet = recordSet ?? throw new ArgumentNullException(nameof(recordSet));
        State = state ?? throw new ArgumentNullException(nameof(state));
        if (State.Identity != SocietyMarketTransactionPartitionIdentityV2.Identity)
            throw new InvalidDataException("society.market-transaction-v2.partition-identity-drift");
        RequireAlignedCount();
    }

    internal static SocietyMarketTransactionPartitionStateV2 FromCanonicalMaterial(
        IReadOnlyList<SocietyMarketTransactionRecordMaterialV2> canonicalRecords,
        IReadOnlyList<DomainRecordEnvelopeV1<SocietyMarketTransactionRecordPayloadV2>> canonicalEnvelopes)
        => new(
            SocietyMarketTransactionRecordSetV2.FromCanonicalRecords(canonicalRecords),
            DomainPartitionStateV1<SocietyMarketTransactionRecordPayloadV2>.FromCanonicalRecords(
                SocietyMarketTransactionPartitionIdentityV2.Identity,
                canonicalEnvelopes));

    public SocietyMarketTransactionRecordSetV2 RecordSet { get; }
    public DomainPartitionStateV1<SocietyMarketTransactionRecordPayloadV2> State { get; }

    private void RequireAlignedCount()
    {
        if (State.ItemCount != checked((ulong)RecordSet.RecordsCanonical.Count))
            throw new InvalidDataException("society.market-transaction-v2.partition-item-count");
    }
}
