using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Runtime;

namespace MachiVerse.Simulation.Core.Persistence;

/// <summary>
/// Converts the single-SQLite-read Snapshot recovery cut into the canonical
/// core.operation-state /2.0 section. Every transaction row must carry canonical persistent wire
/// whose decoded semantic state matches the row columns and digest.
/// </summary>
public static class CoreOperationStateSnapshotCutV2
{
    public static CanonicalSnapshotSectionMaterialV1 Materialize(SnapshotRecoveryStateCutV1 cut)
    {
        ArgumentNullException.ThrowIfNull(cut);
        var transactions = DecodeTransactions(cut.CrossDomainTransactions, cut.FinalizedStep);
        return CoreOperationStateSnapshotSectionProviderV2.Create(
            cut.FinalizedStep,
            cut.DurableOperations,
            transactions);
    }

    public static IReadOnlyList<CrossDomainTransactionStateV1> DecodeTransactions(
        IReadOnlyList<DurableCrossDomainTransactionStateV1> rows,
        ulong snapshotStep)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var states = new CrossDomainTransactionStateV1[rows.Count];
        OpaqueId128? previous = null;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i] ?? throw new InvalidDataException("snapshot-cut.cross-domain-transaction-null-row");
            if (previous is { } prior && prior.CompareTo(row.TransactionId) >= 0)
                throw new InvalidDataException("snapshot-cut.cross-domain-transaction-noncanonical-order");
            var state = CrossDomainTransactionPersistentWireV1.DecodeAndValidate(row);
            if (state.UpdatedStep > snapshotStep)
                throw new InvalidDataException("snapshot-cut.cross-domain-transaction-future-state");
            states[i] = state;
            previous = row.TransactionId;
        }
        _ = CoreOperationStateSnapshotAuthorityV2.Create([], states, snapshotStep);
        return Array.AsReadOnly(states);
    }
}
