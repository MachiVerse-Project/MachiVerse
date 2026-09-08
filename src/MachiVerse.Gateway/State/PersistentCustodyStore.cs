using System.Buffers.Binary;
using System.Security.Cryptography;
using Google.Protobuf;
using MachiVerse.Protocol.V1;
using Microsoft.Data.Sqlite;

namespace MachiVerse.Gateway.State;

public sealed record PersistedCustodyOperation(
    byte[] OperationId,
    byte[] ImmutablePayloadDigest,
    StandardOperationV1 Operation,
    GatewayCustodyState State,
    ulong LastObservedMasterGeneration,
    ResultV1? TerminalResult)
{
    public string OperationIdHex => Convert.ToHexStringLower(OperationId);
}

public sealed class PersistentCustodyStore : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    private PersistentCustodyStore(SqliteConnection connection)
    {
        _connection = connection;
    }

    public static async Task<PersistentCustodyStore> OpenAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        };
        var connection = new SqliteConnection(builder.ToString());
        try
        {
            await connection.OpenAsync(cancellationToken);
            var store = new PersistentCustodyStore(connection);
            await store.InitializeAsync(cancellationToken);
            return store;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task<PersistedCustodyOperation> HoldSourceAsync(
        StandardOperationV1 operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var operationId = ValidateId128(operation.OperationId, "operation_id");
        var digest = ValidateHash256(operation.ImmutablePayloadDigest, "immutable_payload_digest");

        using var transaction = _connection.BeginTransaction();
        try
        {
            var existing = await ReadAsync(operationId, transaction, cancellationToken);
            if (existing is not null)
            {
                RequireSameDigest(existing.ImmutablePayloadDigest, digest);
                transaction.Commit();
                return existing;
            }

            await using var command = _connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
INSERT INTO custody_operation (
  operation_id, payload_digest, operation_wire, custody_state,
  last_master_generation, terminal_result_wire
) VALUES (
  $operation_id, $payload_digest, $operation_wire, $custody_state,
  $last_master_generation, NULL
);
""";
            command.Parameters.AddWithValue("$operation_id", operationId);
            command.Parameters.AddWithValue("$payload_digest", digest);
            command.Parameters.AddWithValue("$operation_wire", operation.ToByteArray());
            command.Parameters.AddWithValue("$custody_state", (int)GatewayCustodyState.SourceHeld);
            command.Parameters.AddWithValue("$last_master_generation", U64Be(0));
            await command.ExecuteNonQueryAsync(cancellationToken);
            transaction.Commit();

            return new PersistedCustodyOperation(
                operationId,
                digest,
                operation.Clone(),
                GatewayCustodyState.SourceHeld,
                0,
                null);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<PersistedCustodyOperation> AdvanceAsync(
        ByteString operationId,
        ByteString immutablePayloadDigest,
        GatewayCustodyState target,
        ulong observedMasterGeneration,
        ResultV1? terminalResult = null,
        CancellationToken cancellationToken = default)
    {
        var id = ValidateId128(operationId, "operation_id");
        var digest = ValidateHash256(immutablePayloadDigest, "immutable_payload_digest");
        if ((int)target is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(target));

        using var transaction = _connection.BeginTransaction();
        try
        {
            var current = await ReadAsync(id, transaction, cancellationToken)
                ?? throw new InvalidDataException("custody.unknown-operation");
            RequireSameDigest(current.ImmutablePayloadDigest, digest);
            if ((int)target < (int)current.State)
                throw new InvalidDataException("custody.state-regression");
            if ((int)target < (int)GatewayCustodyState.Terminal && terminalResult is not null)
                throw new InvalidDataException("custody.premature-terminal-result");
            if (target == GatewayCustodyState.Terminal && terminalResult is null && current.TerminalResult is null)
                throw new InvalidDataException("custody.terminal-result-missing");

            var effectiveGeneration = Math.Max(current.LastObservedMasterGeneration, observedMasterGeneration);
            var effectiveResult = target == GatewayCustodyState.Terminal
                ? terminalResult ?? current.TerminalResult
                : current.TerminalResult;

            await using var command = _connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
UPDATE custody_operation
SET custody_state=$custody_state,
    last_master_generation=$last_master_generation,
    terminal_result_wire=$terminal_result_wire
WHERE operation_id=$operation_id;
""";
            command.Parameters.AddWithValue("$custody_state", (int)target);
            command.Parameters.AddWithValue("$last_master_generation", U64Be(effectiveGeneration));
            command.Parameters.AddWithValue("$terminal_result_wire", (object?)effectiveResult?.ToByteArray() ?? DBNull.Value);
            command.Parameters.AddWithValue("$operation_id", id);
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidDataException("custody.update-failed");
            transaction.Commit();

            return current with
            {
                State = target,
                LastObservedMasterGeneration = effectiveGeneration,
                TerminalResult = effectiveResult?.Clone(),
            };
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<IReadOnlyList<PersistedCustodyOperation>> LoadAllAsync(
        CancellationToken cancellationToken = default)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = """
SELECT operation_id, payload_digest, operation_wire, custody_state,
       last_master_generation, terminal_result_wire
FROM custody_operation
ORDER BY operation_id ASC;
""";
        var result = new List<PersistedCustodyOperation>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(ParseRow(reader));
        return result;
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync("PRAGMA journal_mode=WAL;", cancellationToken);
        await ExecuteNonQueryAsync("PRAGMA synchronous=FULL;", cancellationToken);
        await ExecuteNonQueryAsync("PRAGMA busy_timeout=5000;", cancellationToken);
        await ExecuteNonQueryAsync("""
CREATE TABLE IF NOT EXISTS custody_operation (
  operation_id BLOB PRIMARY KEY CHECK (length(operation_id)=16),
  payload_digest BLOB NOT NULL CHECK (length(payload_digest)=32),
  operation_wire BLOB NOT NULL,
  custody_state INTEGER NOT NULL CHECK (custody_state BETWEEN 1 AND 4),
  last_master_generation BLOB NOT NULL CHECK (length(last_master_generation)=8),
  terminal_result_wire BLOB
) WITHOUT ROWID;
""", cancellationToken);
    }

    private async Task<PersistedCustodyOperation?> ReadAsync(
        byte[] operationId,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
SELECT operation_id, payload_digest, operation_wire, custody_state,
       last_master_generation, terminal_result_wire
FROM custody_operation
WHERE operation_id=$operation_id;
""";
        command.Parameters.AddWithValue("$operation_id", operationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ParseRow(reader) : null;
    }

    private static PersistedCustodyOperation ParseRow(SqliteDataReader reader)
    {
        var operationId = (byte[])reader[0];
        var digest = (byte[])reader[1];
        if (operationId.Length != 16 || digest.Length != 32)
            throw new InvalidDataException("custody.corrupt-identity");
        var stateValue = reader.GetInt32(3);
        if (stateValue is < 1 or > 4)
            throw new InvalidDataException("custody.corrupt-state");
        var generationBytes = (byte[])reader[4];
        if (generationBytes.Length != 8)
            throw new InvalidDataException("custody.corrupt-master-generation");

        var operation = StandardOperationV1.Parser.ParseFrom((byte[])reader[2]);
        if (!operation.OperationId.Span.SequenceEqual(operationId) ||
            !CryptographicOperations.FixedTimeEquals(operation.ImmutablePayloadDigest.Span, digest))
            throw new InvalidDataException("custody.corrupt-operation-wire");

        ResultV1? terminalResult = null;
        if (!reader.IsDBNull(5)) terminalResult = ResultV1.Parser.ParseFrom((byte[])reader[5]);

        return new PersistedCustodyOperation(
            operationId,
            digest,
            operation,
            (GatewayCustodyState)stateValue,
            DecodeU64Be(generationBytes),
            terminalResult);
    }

    private async Task ExecuteNonQueryAsync(string sql, CancellationToken cancellationToken)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static byte[] ValidateId128(ByteString value, string field)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length != 16 || value.Span.IndexOfAnyExcept((byte)0) < 0)
            throw new InvalidDataException($"protocol.invalid-id:{field}");
        return value.ToByteArray();
    }

    private static byte[] ValidateHash256(ByteString value, string field)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length != 32) throw new InvalidDataException($"protocol.invalid-hash:{field}");
        return value.ToByteArray();
    }

    private static void RequireSameDigest(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
    {
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            throw new InvalidDataException("protocol.operation-payload-mismatch");
    }

    private static byte[] U64Be(ulong value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        return bytes;
    }

    private static ulong DecodeU64Be(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 8) throw new InvalidDataException("custody.invalid-u64be");
        return BinaryPrimitives.ReadUInt64BigEndian(bytes);
    }
}
