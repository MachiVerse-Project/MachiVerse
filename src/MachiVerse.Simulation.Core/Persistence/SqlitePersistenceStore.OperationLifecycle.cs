using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using Microsoft.Data.Sqlite;

namespace MachiVerse.Simulation.Core.Persistence;

public enum DurableOperationLifecycleV1
{
    AcceptedDurable = 1,
    ScheduledDurable = 2,
    TerminalDurable = 3,
}

public sealed record DurableOperationStateV1(
    OpaqueId128 OperationId,
    byte[] OperationPayloadDigest,
    DurableOperationLifecycleV1 Lifecycle,
    ulong? AcceptedSequence,
    ulong? ScheduledSequence,
    ulong? EffectiveStep,
    ulong? TerminalSequence,
    int? TerminalStatus,
    string? ResultCode,
    byte[]? RichResultPayload);

public enum DirectTerminalPersistenceStatusV1
{
    Terminalized = 1,
    Duplicate = 2,
}

public sealed record DirectTerminalPersistenceResultV1(
    DirectTerminalPersistenceStatusV1 Status,
    DurableOperationStateV1 State);

public sealed partial class SqlitePersistenceStore
{
    public async Task<DurableOperationStateV1?> ReadOperationStateAsync(
        OpaqueId128 operationId,
        CancellationToken cancellationToken = default)
    {
        if (operationId.IsZero) throw new ArgumentException("OperationId ZERO is invalid.", nameof(operationId));
        await using var command = _connection.CreateCommand();
        command.CommandText = """
SELECT operation_id, payload_digest, lifecycle,
       accepted_sequence, scheduled_sequence, effective_step,
       terminal_sequence, terminal_status, result_code, rich_result_payload
FROM operation_state
WHERE operation_id=$operation_id;
""";
        command.Parameters.AddWithValue("$operation_id", operationId.ToBytes());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return ReadDurableOperationState(reader);
    }

    public async Task<DurableOperationStateV1?> ReadLatestTerminalByResultCodeAsync(
        string resultCode,
        CancellationToken cancellationToken = default)
    {
        _ = new StableToken(resultCode);
        await using var command = _connection.CreateCommand();
        command.CommandText = """
SELECT operation_id, payload_digest, lifecycle,
       accepted_sequence, scheduled_sequence, effective_step,
       terminal_sequence, terminal_status, result_code, rich_result_payload
FROM operation_state
WHERE lifecycle=$terminal_lifecycle AND result_code=$result_code
ORDER BY terminal_sequence DESC
LIMIT 1;
""";
        command.Parameters.AddWithValue("$terminal_lifecycle", TerminalLifecycle);
        command.Parameters.AddWithValue("$result_code", resultCode);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return ReadDurableOperationState(reader);
    }

    public async Task<DirectTerminalPersistenceResultV1> PersistRejectedUnseenOperationAsync(
        OpaqueId128 operationId,
        byte[] operationPayloadDigest,
        int terminalStatus,
        string resultCode,
        HistoryRecordMaterial history,
        byte[]? richResultPayload = null,
        CancellationToken cancellationToken = default)
    {
        if (operationId.IsZero) throw new ArgumentException("OperationId ZERO is invalid.", nameof(operationId));
        RequireHash256(operationPayloadDigest, nameof(operationPayloadDigest));
        if (terminalStatus <= 0) throw new ArgumentOutOfRangeException(nameof(terminalStatus));
        _ = new StableToken(resultCode);
        ValidateHistoryMaterial(history, "operation.terminal.v1");

        using var transaction = _connection.BeginTransaction();
        try
        {
            var existing = await ReadOperationStateAsync(operationId, transaction, cancellationToken);
            if (existing is not null)
            {
                if (!CryptographicOperations.FixedTimeEquals(existing.OperationPayloadDigest, operationPayloadDigest))
                    throw new InvalidDataException("protocol.operation-payload-mismatch");
                if (existing.Lifecycle != DurableOperationLifecycleV1.TerminalDurable)
                    throw new InvalidDataException("persistence.operation-already-accepted");
                transaction.Commit();
                return new DirectTerminalPersistenceResultV1(DirectTerminalPersistenceStatusV1.Duplicate, existing);
            }

            var context = await ReadHistoryContextAsync(transaction, cancellationToken);
            ValidateNextHistoryRecord(history, context);
            await InsertHistoryRecordAsync(history, transaction, cancellationToken);

            await using (var command = _connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
INSERT INTO operation_state (
  operation_id, payload_digest, lifecycle, accepted_sequence,
  scheduled_sequence, effective_step, terminal_sequence, terminal_status,
  result_code, rich_result_payload
) VALUES (
  $operation_id, $payload_digest, $lifecycle, NULL,
  NULL, NULL, $terminal_sequence, $terminal_status,
  $result_code, $rich_result_payload
);
""";
                command.Parameters.AddWithValue("$operation_id", operationId.ToBytes());
                command.Parameters.AddWithValue("$payload_digest", operationPayloadDigest);
                command.Parameters.AddWithValue("$lifecycle", TerminalLifecycle);
                command.Parameters.AddWithValue("$terminal_sequence", U64Be.Encode(history.Sequence));
                command.Parameters.AddWithValue("$terminal_status", terminalStatus);
                command.Parameters.AddWithValue("$result_code", resultCode);
                command.Parameters.AddWithValue("$rich_result_payload", (object?)richResultPayload ?? DBNull.Value);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await UpdateHistoryAnchorAsync(history, transaction, cancellationToken);
            transaction.Commit();
            var state = new DurableOperationStateV1(
                operationId,
                operationPayloadDigest.ToArray(),
                DurableOperationLifecycleV1.TerminalDurable,
                AcceptedSequence: null,
                ScheduledSequence: null,
                EffectiveStep: null,
                TerminalSequence: history.Sequence,
                TerminalStatus: terminalStatus,
                ResultCode: resultCode,
                RichResultPayload: richResultPayload?.ToArray());
            return new DirectTerminalPersistenceResultV1(DirectTerminalPersistenceStatusV1.Terminalized, state);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private async Task<DurableOperationStateV1?> ReadOperationStateAsync(
        OpaqueId128 operationId,
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
WHERE operation_id=$operation_id;
""";
        command.Parameters.AddWithValue("$operation_id", operationId.ToBytes());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return ReadDurableOperationState(reader);
    }

    private static DurableOperationStateV1 ReadDurableOperationState(SqliteDataReader reader)
    {
        var operationId = OpaqueId128.FromBytes((byte[])reader[0]);
        var payloadDigest = (byte[])reader[1];
        RequireHash256(payloadDigest, "operation_state.payload_digest");
        var lifecycleRaw = reader.GetInt32(2);
        if (!Enum.IsDefined(typeof(DurableOperationLifecycleV1), lifecycleRaw))
            throw new InvalidDataException("persistence.operation-lifecycle-invalid");

        return new DurableOperationStateV1(
            operationId,
            payloadDigest.ToArray(),
            (DurableOperationLifecycleV1)lifecycleRaw,
            reader.IsDBNull(3) ? null : U64Be.Decode((byte[])reader[3]),
            reader.IsDBNull(4) ? null : U64Be.Decode((byte[])reader[4]),
            reader.IsDBNull(5) ? null : U64Be.Decode((byte[])reader[5]),
            reader.IsDBNull(6) ? null : U64Be.Decode((byte[])reader[6]),
            reader.IsDBNull(7) ? null : reader.GetInt32(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : ((byte[])reader[9]).ToArray());
    }
}
