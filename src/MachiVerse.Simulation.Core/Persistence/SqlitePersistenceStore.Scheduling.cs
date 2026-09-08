using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using Microsoft.Data.Sqlite;

namespace MachiVerse.Simulation.Core.Persistence;

public enum DurableSchedulingStatus
{
    Scheduled = 1,
    Duplicate = 2
}

public sealed record DurableSchedulingResult(
    DurableSchedulingStatus Status,
    ulong ScheduledSequence,
    ulong EffectiveStep);

public sealed partial class SqlitePersistenceStore
{
    public async Task<DurableSchedulingResult> PersistScheduledOperationAsync(
        OpaqueId128 operationId,
        ulong effectiveStep,
        SameStepOrderKey orderKey,
        HistoryRecordMaterial history,
        CancellationToken cancellationToken = default)
    {
        if (operationId.IsZero) throw new ArgumentException("OperationId ZERO is invalid.", nameof(operationId));
        ArgumentNullException.ThrowIfNull(orderKey);
        ValidateHistoryMaterial(history, "operation.scheduled.v1");
        var databaseOrderKey = orderKey.ToDatabaseBytes();
        if (databaseOrderKey.Length != SameStepOrderKey.DatabaseKeyLength)
            throw new InvalidDataException("persistence.same-step-order-key-length");

        using var transaction = _connection.BeginTransaction();
        try
        {
            var current = await ReadSchedulingStateAsync(operationId, transaction, cancellationToken)
                ?? throw new InvalidDataException("persistence.operation-not-accepted");

            if (current.Lifecycle == ScheduledLifecycle)
            {
                if (current.ScheduledSequence is null || current.EffectiveStep is null || current.OrderKey is null)
                    throw new InvalidDataException("persistence.scheduled-operation-index-incomplete");
                if (current.EffectiveStep.Value != effectiveStep ||
                    !CryptographicOperations.FixedTimeEquals(current.OrderKey, databaseOrderKey))
                    throw new InvalidDataException("persistence.operation-schedule-mismatch");

                transaction.Commit();
                return new DurableSchedulingResult(
                    DurableSchedulingStatus.Duplicate,
                    current.ScheduledSequence.Value,
                    current.EffectiveStep.Value);
            }

            if (current.Lifecycle != AcceptedLifecycle)
                throw new InvalidDataException("persistence.operation-invalid-lifecycle-for-schedule");

            var context = await ReadHistoryContextAsync(transaction, cancellationToken);
            ValidateNextHistoryRecord(history, context);
            await InsertHistoryRecordAsync(history, transaction, cancellationToken);

            await using (var operation = _connection.CreateCommand())
            {
                operation.Transaction = transaction;
                operation.CommandText = """
UPDATE operation_state
SET lifecycle=$scheduled_lifecycle,
    scheduled_sequence=$scheduled_sequence,
    effective_step=$effective_step
WHERE operation_id=$operation_id AND lifecycle=$accepted_lifecycle;
""";
                operation.Parameters.AddWithValue("$scheduled_lifecycle", ScheduledLifecycle);
                operation.Parameters.AddWithValue("$scheduled_sequence", U64Be.Encode(history.Sequence));
                operation.Parameters.AddWithValue("$effective_step", U64Be.Encode(effectiveStep));
                operation.Parameters.AddWithValue("$operation_id", operationId.ToBytes());
                operation.Parameters.AddWithValue("$accepted_lifecycle", AcceptedLifecycle);
                if (await operation.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidDataException("persistence.operation-schedule-update-failed");
            }

            await using (var schedule = _connection.CreateCommand())
            {
                schedule.Transaction = transaction;
                schedule.CommandText = """
INSERT INTO scheduled_operation (effective_step, order_key, operation_id)
VALUES ($effective_step, $order_key, $operation_id);
""";
                schedule.Parameters.AddWithValue("$effective_step", U64Be.Encode(effectiveStep));
                schedule.Parameters.AddWithValue("$order_key", databaseOrderKey);
                schedule.Parameters.AddWithValue("$operation_id", operationId.ToBytes());
                await schedule.ExecuteNonQueryAsync(cancellationToken);
            }

            await UpdateHistoryAnchorAsync(history, transaction, cancellationToken);
            transaction.Commit();
            return new DurableSchedulingResult(DurableSchedulingStatus.Scheduled, history.Sequence, effectiveStep);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private sealed record SchedulingState(
        int Lifecycle,
        ulong? ScheduledSequence,
        ulong? EffectiveStep,
        byte[]? OrderKey);

    private async Task<SchedulingState?> ReadSchedulingStateAsync(
        OpaqueId128 operationId,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
SELECT o.lifecycle, o.scheduled_sequence, o.effective_step, s.order_key
FROM operation_state o
LEFT JOIN scheduled_operation s ON s.operation_id = o.operation_id
WHERE o.operation_id=$operation_id;
""";
        command.Parameters.AddWithValue("$operation_id", operationId.ToBytes());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        return new SchedulingState(
            reader.GetInt32(0),
            reader.IsDBNull(1) ? null : U64Be.Decode((byte[])reader[1]),
            reader.IsDBNull(2) ? null : U64Be.Decode((byte[])reader[2]),
            reader.IsDBNull(3) ? null : (byte[])reader[3]);
    }
}
