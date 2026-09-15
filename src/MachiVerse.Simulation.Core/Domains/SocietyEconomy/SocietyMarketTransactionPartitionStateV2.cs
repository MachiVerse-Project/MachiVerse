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
    private readonly SortedDictionary<OpaqueId128, SocietyMarketTransactionRecordMaterialV2> _records = new();

    public SocietyMarketTransactionRecordSetV2(IEnumerable<SocietyMarketTransactionRecordMaterialV2> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        foreach (var record in records)
        {
            ArgumentNullException.ThrowIfNull(record);
            if (!_records.TryAdd(record.RecordId, record))
                throw new InvalidDataException("society.market-transaction-v2.record-id-duplicate");
        }
    }

    public IReadOnlyList<SocietyMarketTransactionRecordMaterialV2> RecordsCanonical
        => Array.AsReadOnly(_records.Values.ToArray());

    public bool TryGet(OpaqueId128 recordId, out SocietyMarketTransactionRecordMaterialV2? record)
        => _records.TryGetValue(recordId, out record);
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
        if (State.ItemCount != checked((ulong)RecordSet.RecordsCanonical.Count))
            throw new InvalidDataException("society.market-transaction-v2.partition-item-count");
    }

    public SocietyMarketTransactionRecordSetV2 RecordSet { get; }
    public DomainPartitionStateV1<SocietyMarketTransactionRecordPayloadV2> State { get; }
}
