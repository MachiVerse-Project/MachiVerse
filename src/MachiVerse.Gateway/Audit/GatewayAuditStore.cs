using System.Buffers.Binary;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace MachiVerse.Gateway.Audit;

public interface IAuditWriterV1
{
    ValueTask<AuditRecordV1> AppendAsync(AuditRecordDraftV1 draft, CancellationToken cancellationToken = default);
}

public sealed class GatewayAuditStoreV1 : IAuditWriterV1, IAsyncDisposable
{
    private const string NextSequenceKey = "next_sequence";
    private const string LastDigestKey = "last_digest";
    private const string RetentionAnchorKey = "retention_anchor";

    private readonly string _databasePath;
    private readonly int _queryMaxPageSize;
    private readonly SemaphoreSlim _writer = new(1, 1);
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private bool _initialized;

    public GatewayAuditStoreV1(string componentDataDirectory, int queryMaxPageSize)
    {
        if (string.IsNullOrWhiteSpace(componentDataDirectory))
            throw new ArgumentException("Component data directory is required.", nameof(componentDataDirectory));
        if (queryMaxPageSize is < 100 or > 10000)
            throw new ArgumentOutOfRangeException(nameof(queryMaxPageSize));

        var auditDirectory = Path.Combine(Path.GetFullPath(componentDataDirectory), "audit");
        Directory.CreateDirectory(auditDirectory);
        _databasePath = Path.Combine(auditDirectory, "audit.sqlite");
        _queryMaxPageSize = queryMaxPageSize;
    }

    public string DatabasePath => _databasePath;

    public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _writer.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized) return;
            await using var connection = OpenConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ExecuteNonQueryAsync(connection, null, "PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);
            await ExecuteNonQueryAsync(connection, null, "PRAGMA synchronous=FULL;", cancellationToken).ConfigureAwait(false);
            await ExecuteNonQueryAsync(connection, null, "PRAGMA foreign_keys=ON;", cancellationToken).ConfigureAwait(false);
            await ExecuteNonQueryAsync(connection, null, """
                CREATE TABLE IF NOT EXISTS audit_record(
                    sequence BLOB NOT NULL PRIMARY KEY CHECK(length(sequence)=8),
                    digest BLOB NOT NULL CHECK(length(digest)=32),
                    previous_digest BLOB NOT NULL CHECK(length(previous_digest)=32),
                    kind TEXT NOT NULL,
                    payload BLOB NOT NULL
                ) WITHOUT ROWID;
                CREATE TABLE IF NOT EXISTS audit_meta(
                    key TEXT NOT NULL PRIMARY KEY,
                    value BLOB NOT NULL
                ) WITHOUT ROWID;
                """, cancellationToken).ConfigureAwait(false);

            var nextSequence = await ReadMetaAsync(connection, null, NextSequenceKey, cancellationToken).ConfigureAwait(false);
            if (nextSequence is null)
            {
                await WriteMetaAsync(connection, null, NextSequenceKey, U64Be(1), cancellationToken).ConfigureAwait(false);
                await WriteMetaAsync(connection, null, LastDigestKey, new byte[32], cancellationToken).ConfigureAwait(false);
            }
            _initialized = true;
        }
        finally
        {
            _writer.Release();
        }
    }

    public async ValueTask<AuditRecordV1> AppendAsync(
        AuditRecordDraftV1 draft,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _writer.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = OpenConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var record = await AppendInTransactionAsync(connection, transaction, draft, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return record;
        }
        finally
        {
            _writer.Release();
        }
    }

    public async ValueTask<IReadOnlyList<AuditRecordV1>> QueryAsync(
        ulong? afterSequence,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        if (pageSize <= 0 || pageSize > _queryMaxPageSize)
            throw new InvalidDataException("audit.query-page-size-out-of-range");

        await using var connection = OpenConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = afterSequence.HasValue
            ? "SELECT payload FROM audit_record WHERE sequence > $after ORDER BY sequence LIMIT $limit;"
            : "SELECT payload FROM audit_record ORDER BY sequence LIMIT $limit;";
        if (afterSequence.HasValue)
            command.Parameters.AddWithValue("$after", U64Be(afterSequence.Value));
        command.Parameters.AddWithValue("$limit", pageSize);

        var records = new List<AuditRecordV1>(pageSize);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var payload = (byte[])reader[0];
            records.Add(DeserializeRecord(payload));
        }
        return records;
    }

    public async ValueTask<AuditRetentionAnchorV1?> ReadRetentionAnchorAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = OpenConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var bytes = await ReadMetaAsync(connection, null, RetentionAnchorKey, cancellationToken).ConfigureAwait(false);
        return bytes is null
            ? null
            : JsonSerializer.Deserialize<AuditRetentionAnchorV1>(bytes, _json)
              ?? throw new InvalidDataException("audit.retention-anchor-invalid");
    }

    public async ValueTask<AuditRetentionAnchorV1?> ApplyRetentionAsync(
        long cutoffObservedAtUnixNs,
        ulong policyGeneration,
        AuditRecordDraftV1 retentionActionAudit,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        if (policyGeneration == 0) throw new InvalidDataException("audit.retention-policy-generation-zero");

        await _writer.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = OpenConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            _ = await AppendInTransactionAsync(connection, transaction, retentionActionAudit, cancellationToken).ConfigureAwait(false);
            var records = await ReadAllInTransactionAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            if (records.Count <= 1)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }

            ulong deletedThrough = 0;
            byte[]? priorFinalDigest = null;
            foreach (var record in records.Take(records.Count - 1))
            {
                if (record.ObservedAtUnixNs >= cutoffObservedAtUnixNs) break;
                deletedThrough = record.AuditSequence;
                priorFinalDigest = record.RecordDigest;
            }

            if (deletedThrough == 0 || priorFinalDigest is null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }

            var firstRetained = checked(deletedThrough + 1);
            var anchor = new AuditRetentionAnchorV1(
                firstRetained,
                priorFinalDigest.ToArray(),
                deletedThrough,
                policyGeneration);

            await using (var delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM audit_record WHERE sequence <= $deletedThrough;";
                delete.Parameters.AddWithValue("$deletedThrough", U64Be(deletedThrough));
                await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            await WriteMetaAsync(
                connection,
                transaction,
                RetentionAnchorKey,
                JsonSerializer.SerializeToUtf8Bytes(anchor, _json),
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return anchor;
        }
        finally
        {
            _writer.Release();
        }
    }

    public async ValueTask ValidateChainAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = OpenConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var records = await ReadAllInTransactionAsync(connection, null, cancellationToken).ConfigureAwait(false);
        var anchorBytes = await ReadMetaAsync(connection, null, RetentionAnchorKey, cancellationToken).ConfigureAwait(false);
        var anchor = anchorBytes is null
            ? null
            : JsonSerializer.Deserialize<AuditRetentionAnchorV1>(anchorBytes, _json)
              ?? throw new InvalidDataException("audit.retention-anchor-invalid");

        if (records.Count == 0) return;

        var first = records[0];
        if (anchor is null)
        {
            if (first.AuditSequence != 1 || first.PreviousDigest.Any(static value => value != 0))
                throw new InvalidDataException("audit.chain-missing-origin");
        }
        else
        {
            if (anchor.FirstRetainedSequence != checked(anchor.DeletedThroughSequence + 1) ||
                first.AuditSequence != anchor.FirstRetainedSequence ||
                !first.PreviousDigest.AsSpan().SequenceEqual(anchor.PriorFinalDigest))
                throw new InvalidDataException("audit.retention-anchor-mismatch");
        }

        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            AuditRecordCodecV1.ValidateDigest(record);
            if (index == 0) continue;
            var previous = records[index - 1];
            if (previous.AuditSequence == ulong.MaxValue || record.AuditSequence != previous.AuditSequence + 1)
                throw new InvalidDataException("audit.sequence-gap");
            if (!record.PreviousDigest.AsSpan().SequenceEqual(previous.RecordDigest))
                throw new InvalidDataException("audit.previous-digest-mismatch");
        }

        var last = records[^1];
        var nextSequenceBytes = await ReadMetaAsync(connection, null, NextSequenceKey, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("audit.meta-next-sequence-missing");
        var lastDigest = await ReadMetaAsync(connection, null, LastDigestKey, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("audit.meta-last-digest-missing");
        if (ReadU64Be(nextSequenceBytes) != checked(last.AuditSequence + 1) ||
            !lastDigest.AsSpan().SequenceEqual(last.RecordDigest))
            throw new InvalidDataException("audit.meta-chain-mismatch");
    }

    public ValueTask DisposeAsync()
    {
        _writer.Dispose();
        return ValueTask.CompletedTask;
    }

    private async ValueTask EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (!_initialized) await InitializeAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<AuditRecordV1> AppendInTransactionAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        AuditRecordDraftV1 draft,
        CancellationToken cancellationToken)
    {
        var nextBytes = await ReadMetaAsync(connection, transaction, NextSequenceKey, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("audit.meta-next-sequence-missing");
        var previousDigest = await ReadMetaAsync(connection, transaction, LastDigestKey, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("audit.meta-last-digest-missing");
        var sequence = ReadU64Be(nextBytes);
        if (sequence == 0) throw new InvalidDataException("audit.sequence-zero");

        if (sequence > 1)
        {
            await using var last = connection.CreateCommand();
            last.Transaction = (SqliteTransaction)transaction;
            last.CommandText = "SELECT sequence, digest FROM audit_record ORDER BY sequence DESC LIMIT 1;";
            await using var reader = await last.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException("audit.meta-without-chain-tail");
            var lastSequence = ReadU64Be((byte[])reader[0]);
            var tailDigest = (byte[])reader[1];
            if (lastSequence == ulong.MaxValue || sequence != lastSequence + 1 ||
                !tailDigest.AsSpan().SequenceEqual(previousDigest))
                throw new InvalidDataException("audit.meta-chain-mismatch");
        }

        var record = AuditRecordCodecV1.Create(sequence, previousDigest, draft);
        var payload = JsonSerializer.SerializeToUtf8Bytes(record, _json);
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = (SqliteTransaction)transaction;
            insert.CommandText = """
                INSERT INTO audit_record(sequence, digest, previous_digest, kind, payload)
                VALUES($sequence, $digest, $previous, $kind, $payload);
                """;
            insert.Parameters.AddWithValue("$sequence", U64Be(record.AuditSequence));
            insert.Parameters.AddWithValue("$digest", record.RecordDigest);
            insert.Parameters.AddWithValue("$previous", record.PreviousDigest);
            insert.Parameters.AddWithValue("$kind", record.AuditKind);
            insert.Parameters.AddWithValue("$payload", payload);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (sequence == ulong.MaxValue) throw new InvalidDataException("audit.sequence-exhausted");
        await WriteMetaAsync(connection, transaction, NextSequenceKey, U64Be(sequence + 1), cancellationToken).ConfigureAwait(false);
        await WriteMetaAsync(connection, transaction, LastDigestKey, record.RecordDigest, cancellationToken).ConfigureAwait(false);
        return record;
    }

    private async ValueTask<List<AuditRecordV1>> ReadAllInTransactionAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction as SqliteTransaction;
        command.CommandText = "SELECT payload FROM audit_record ORDER BY sequence;";
        var records = new List<AuditRecordV1>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            records.Add(DeserializeRecord((byte[])reader[0]));
        return records;
    }

    private AuditRecordV1 DeserializeRecord(byte[] payload)
    {
        var record = JsonSerializer.Deserialize<AuditRecordV1>(payload, _json)
            ?? throw new InvalidDataException("audit.payload-invalid");
        AuditRecordCodecV1.ValidateDigest(record);
        return record;
    }

    private SqliteConnection OpenConnection()
        => new(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString());

    private static async ValueTask ExecuteNonQueryAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction? transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction as SqliteTransaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<byte[]?> ReadMetaAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction? transaction,
        string key,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction as SqliteTransaction;
        command.CommandText = "SELECT value FROM audit_meta WHERE key=$key;";
        command.Parameters.AddWithValue("$key", key);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is null or DBNull ? null : (byte[])value;
    }

    private static async ValueTask WriteMetaAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction? transaction,
        string key,
        byte[] value,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction as SqliteTransaction;
        command.CommandText = """
            INSERT INTO audit_meta(key, value) VALUES($key, $value)
            ON CONFLICT(key) DO UPDATE SET value=excluded.value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static byte[] U64Be(ulong value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        return bytes;
    }

    private static ulong ReadU64Be(ReadOnlySpan<byte> value)
    {
        if (value.Length != 8) throw new InvalidDataException("audit.u64be-length");
        return BinaryPrimitives.ReadUInt64BigEndian(value);
    }
}
