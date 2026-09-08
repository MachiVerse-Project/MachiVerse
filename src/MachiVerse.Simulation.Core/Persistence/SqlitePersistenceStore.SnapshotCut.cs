using MachiVerse.Simulation.Core.Determinism;
using Microsoft.Data.Sqlite;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed record SnapshotScheduledOperationStateV1(
    ulong EffectiveStep,
    byte[] OrderKey,
    OpaqueId128 OperationId);

public sealed record SnapshotRecoveryStateCutV1(
    ulong FinalizedStep,
    byte[] StateContinuityToken,
    ulong ConfigGeneration,
    byte[] ConfigDigest,
    HistoryAnchor HistoryAnchor,
    IReadOnlyList<DurableOperationStateV1> DurableOperations,
    IReadOnlyList<SnapshotScheduledOperationStateV1> ScheduledOperations);

public sealed partial class SqlitePersistenceStore
{
    /// <summary>
    /// Captures mutable recovery authority for a running snapshot in one SQLite read transaction.
    /// The result may be serialized after the Step-boundary barrier is released without re-reading
    /// operation/scheduler tables that may have advanced in the meantime.
    /// </summary>
    public async Task<SnapshotRecoveryStateCutV1> ReadSnapshotRecoveryCutAsync(
        CancellationToken cancellationToken = default)
    {
        using var transaction = _connection.BeginTransaction();
        try
        {
            var head = await ReadSnapshotHeadAsync(transaction, cancellationToken).ConfigureAwait(false);
            var context = await ReadHistoryContextAsync(transaction, cancellationToken).ConfigureAwait(false);
            var operations = await ReadSnapshotOperationStatesAsync(transaction, cancellationToken).ConfigureAwait(false);
            var scheduled = await ReadSnapshotScheduledOperationsAsync(transaction, cancellationToken).ConfigureAwait(false);
            transaction.Commit();
            return new SnapshotRecoveryStateCutV1(
                head.FinalizedStep,
                head.StateContinuityToken,
                head.ConfigGeneration,
                head.ConfigDigest,
                context.Anchor,
                operations,
                scheduled);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private async Task<(ulong FinalizedStep, byte[] StateContinuityToken, ulong ConfigGeneration, byte[] ConfigDigest)>
        ReadSnapshotHeadAsync(SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
SELECT finalized_step, state_continuity_token, config_generation, config_digest
FROM persistence_meta
WHERE singleton=1;
""";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException("persistence.meta-not-initialized");
        var continuity = (byte[])reader[1];
        var configDigest = (byte[])reader[3];
        RequireHash256(continuity, "snapshot-cut.state-continuity-token");
        RequireHash256(configDigest, "snapshot-cut.config-digest");
        return (
            U64Be.Decode((byte[])reader[0]),
            continuity.ToArray(),
            U64Be.Decode((byte[])reader[2]),
            configDigest.ToArray());
    }

    private async Task<IReadOnlyList<DurableOperationStateV1>> ReadSnapshotOperationStatesAsync(
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
SELECT operation_id, payload_digest, lifecycle,
       accepted_sequence, scheduled_sequence, effective_step,
       terminal_sequence, terminal_status, result_code, rich_result_payload
FROM operation_state
ORDER BY operation_id ASC;
""";
        var result = new List<DurableOperationStateV1>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(ReadDurableOperationState(reader));
        return Array.AsReadOnly(result.ToArray());
    }

    private async Task<IReadOnlyList<SnapshotScheduledOperationStateV1>> ReadSnapshotScheduledOperationsAsync(
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
SELECT effective_step, order_key, operation_id
FROM scheduled_operation
ORDER BY effective_step ASC, order_key ASC, operation_id ASC;
""";
        var result = new List<SnapshotScheduledOperationStateV1>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var orderKey = (byte[])reader[1];
            if (orderKey.Length != SameStepOrderKey.DatabaseKeyLength)
                throw new InvalidDataException("snapshot-cut.scheduled-order-key-length");
            result.Add(new SnapshotScheduledOperationStateV1(
                U64Be.Decode((byte[])reader[0]),
                orderKey.ToArray(),
                OpaqueId128.FromBytes((byte[])reader[2])));
        }
        return Array.AsReadOnly(result.ToArray());
    }
}
