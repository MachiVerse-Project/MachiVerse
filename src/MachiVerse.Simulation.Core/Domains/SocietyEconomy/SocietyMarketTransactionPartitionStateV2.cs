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
    private readonly PersistentCanonicalRecordMapV1<SocietyMarketTransactionRecordMaterialV2> _records;

    public SocietyMarketTransactionRecordSetV2(IEnumerable<SocietyMarketTransactionRecordMaterialV2> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        _records = PersistentCanonicalRecordMapV1<SocietyMarketTransactionRecordMaterialV2>.FromUnordered(
            records,
            static record => record.RecordId,
            "society.market-transaction-v2.record-id-duplicate",
            "society.market-transaction-v2.record-id-zero");
    }

    private SocietyMarketTransactionRecordSetV2(
        PersistentCanonicalRecordMapV1<SocietyMarketTransactionRecordMaterialV2> records)
        => _records = records ?? throw new ArgumentNullException(nameof(records));

    internal static SocietyMarketTransactionRecordSetV2 FromCanonicalRecords(
        IReadOnlyList<SocietyMarketTransactionRecordMaterialV2> canonicalRecords)
        => new(PersistentCanonicalRecordMapV1<SocietyMarketTransactionRecordMaterialV2>.FromCanonical(
            canonicalRecords,
            static record => record.RecordId,
            "society.market-transaction-v2.record-order",
            "society.market-transaction-v2.record-id-zero"));

    internal SocietyMarketTransactionRecordSetV2 WithAdditions(
        IEnumerable<SocietyMarketTransactionRecordMaterialV2> additions,
        string collisionCode)
        => new(_records.AddRange(additions, collisionCode));

    public IReadOnlyList<SocietyMarketTransactionRecordMaterialV2> RecordsCanonical => _records;

    public bool TryGet(OpaqueId128 recordId, out SocietyMarketTransactionRecordMaterialV2? record)
        => _records.TryGet(recordId, out record);
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

    internal SocietyMarketTransactionPartitionStateV2 WithAdditions(
        IReadOnlyList<SocietyMarketTransactionRecordMaterialV2> additions,
        string collisionCode)
    {
        ArgumentNullException.ThrowIfNull(additions);
        ArgumentException.ThrowIfNullOrWhiteSpace(collisionCode);

        var envelopes = additions
            .Select(static record => new DomainRecordEnvelopeV1<SocietyMarketTransactionRecordPayloadV2>(
                record.RecordId,
                SocietyMarketTransactionRecordSchemaV2.RecordSchema,
                record.Revision,
                record.CreatedStep,
                record.RetiredStep,
                record.DetailLevel,
                record.LineageRef,
                record.Payload))
            .ToArray();

        return new SocietyMarketTransactionPartitionStateV2(
            RecordSet.WithAdditions(additions, collisionCode),
            State.WithAdditions(envelopes, collisionCode));
    }

    public SocietyMarketTransactionRecordSetV2 RecordSet { get; }
    public DomainPartitionStateV1<SocietyMarketTransactionRecordPayloadV2> State { get; }

    private void RequireAlignedCount()
    {
        if (State.ItemCount != checked((ulong)RecordSet.RecordsCanonical.Count))
            throw new InvalidDataException("society.market-transaction-v2.partition-item-count");
    }
}
