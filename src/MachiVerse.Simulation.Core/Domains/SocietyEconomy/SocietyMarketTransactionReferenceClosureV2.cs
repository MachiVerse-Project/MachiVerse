using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains.SocietyEconomy;

/// <summary>
/// Enforces the heterogeneous intra-partition reference contract for society.market_transaction /2.0.
/// Every market_ref carried by an order or fact must resolve to a record whose kind is market_state.
/// </summary>
public static class SocietyMarketTransactionReferenceClosureV2
{
    public static void Validate(IReadOnlyList<SocietyMarketTransactionRecordMaterialV2> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var kinds = new Dictionary<OpaqueId128, string>();
        foreach (var record in records)
        {
            ArgumentNullException.ThrowIfNull(record);
            if (!kinds.TryAdd(record.RecordId, record.Payload.RecordKind))
                throw new InvalidDataException("society.market-transaction-v2.reference-closure-record-id-duplicate");
        }

        foreach (var record in records)
        {
            var marketRef = record.Payload switch
            {
                SocietyMarketOrderPayloadV2 order => order.MarketRef,
                SocietyMarketFactPayloadV2 fact => fact.MarketRef,
                _ => (PartitionRecordRefV1?)null,
            };
            if (marketRef is not { } reference) continue;
            if (!string.Equals(reference.PartitionId.Value, SocietyMarketTransactionRecordSchemaV2.PartitionId, StringComparison.Ordinal))
                throw new InvalidDataException("society.market-transaction-v2.reference-closure-market-partition");
            if (!kinds.TryGetValue(reference.RecordId, out var targetKind))
                throw new InvalidDataException("society.market-transaction-v2.reference-closure-market-missing");
            if (!string.Equals(targetKind, SocietyMarketTransactionRecordSchemaV2.MarketStateKind, StringComparison.Ordinal))
                throw new InvalidDataException("society.market-transaction-v2.reference-closure-market-target-kind");
        }
    }
}
