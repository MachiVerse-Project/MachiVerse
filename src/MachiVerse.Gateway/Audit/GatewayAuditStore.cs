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
            await using var connection = await OpenConfiguredConnectionAsync(cancellationToken).ConfigureAwait(false);
            await ExecAsync(connection, null, "PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);
            await ExecAsync(connection, null, """
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

            var next = await ReadMetaAsync(connection, null, NextSequenceKey, cancellationToken).ConfigureAwait(false);
            if (next is null)
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

    public async ValueTask<AuditRecordV1> AppendAsync(AuditRecordDraftV1 draft, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _writer.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConfiguredConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var record = await AppendInTransactionAsync(connection, transaction, draft, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return record;
        }
        finally
        {
            _writer.Release();
        }
    }

    public async ValueTask<IReadOnlyList<AuditRecordV1>> QueryAsync(ulong? afterSequence, int pageSize, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        if (pageSize <= 0 || pageSize > _queryMaxPageSize)
            throw new InvalidDataException("audit.query-page-size-out-of-range");

        await using var connection = await OpenConfiguredConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = afterSequence.HasValue
            ? "SELECT sequence,digest,previous_digest,kind,payload FROM audit_record WHERE sequence>$after ORDER BY sequence LIMIT $limit;"
            : "SELECT sequence,digest,previous_digest,kind,payload FROM audit_record ORDER BY sequence LIMIT $limit;";
        if (afterSequence.HasValue) command.Parameters.AddWithValue("$after", U64Be(afterSequence.Value));
        command.Parameters.AddWithValue("$limit", pageSize);

        var records = new List<AuditRecordV1>(pageSize);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            records.Add(ReadAndValidateRow(reader));
        return records;
    }

    public async ValueTask<AuditRetentionAnchorV1?> ReadRetentionAnchorAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConfiguredConnectionAsync(cancellationToken).ConfigureAwait(false);
        var bytes = await ReadMetaAsync(connection, null, RetentionAnchorKey, cancellationToken).ConfigureAwait(false);
        return bytes is null ? null : DeserializeAnchor(bytes);
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
            await using var connection = await OpenConfiguredConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            _ = await AppendInTransactionAsync(connection, transaction, retentionActionAudit, cancellationToken).ConfigureAwait(false);
            var records = await ReadAllAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
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
                priorFinalDigest = record.RecordDigest.ToArray();
            }

            if (deletedThrough == 0 || priorFinalDigest is null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }

            var anchor = new AuditRetentionAnchorV1(
                checked(deletedThrough + 1), priorFinalDigest, deletedThrough, policyGeneration);
            await using (var delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM audit_record WHERE sequence <= $through;";
                delete.Parameters.AddWithValue("$through", U64Be(deletedThrough));
                await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            await WriteMetaAsync(connection, transaction, RetentionAnchorKey,
                JsonSerializer.SerializeToUtf8Bytes(anchor, _json), cancellationToken).ConfigureAwait(false);
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
        await using var connection = await OpenConfiguredConnectionAsync(cancellationToken).ConfigureAwait(false);
        var records = await ReadAllAsync(connection, null, cancellationToken).ConfigureAwait(false);
        var anchorBytes = await ReadMetaAsync(connection, null, RetentionAnchorKey, cancellationToken).ConfigureAwait(false);
        var anchor = anchorBytes is null ? null : DeserializeAnchor(anchorBytes);

        if (records.Count == 0) return;
        var first = records[0];
        if (anchor is null)
        {
            if (first.AuditSequence != 1 || first.PreviousDigest.Any(static b => b != 0))
                throw new InvalidDataException("audit.chain-missing-origin");
        }
        else if (anchor.FirstRetainedSequence != checked(anchor.DeletedThroughSequence + 1) ||
                 first.AuditSequence != anchor.FirstRetainedSequence ||
                 !first.PreviousDigest.AsSpan().SequenceEqual(anchor.PriorFinalDigest))
        {
            throw new InvalidDataException("audit.retention-anchor-mismatch");
        }

        for (var i = 1; i < records.Count; i++)
        {
            var previous = records[i - 1];
            var current = records[i];
            if (previous.AuditSequence == ulong.MaxValue || current.AuditSequence != previous.AuditSequence + 1)
                throw new InvalidDataException("audit.sequence-gap");
            if (!current.PreviousDigest.AsSpan().SequenceEqual(previous.RecordDigest))
                throw new InvalidDataException("audit.previous-digest-mismatch");
        }

        var tail = records[^1];
        var nextBytes = await ReadMetaAsync(connection, null, NextSequenceKey, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("audit.meta-next-sequence-missing");
        var lastDigest = await ReadMetaAsync(connection, null, LastDigestKey, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("audit.meta-last-digest-missing");
        if (tail.AuditSequence == ulong.MaxValue || ReadU64Be(nextBytes) != tail.AuditSequence + 1 ||
            !lastDigest.AsSpan().SequenceEqual(tail.RecordDigest))
            throw new InvalidDataException("audit.meta-chain-mismatch");
    }

    public ValueTask DisposeAsync()
    {
        _writer.Dispose();
        return ValueTask.CompletedTask;
    }

    private async ValueTask<AuditRecordV1> AppendInTransactionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
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
            await using var tail = connection.CreateCommand();
            tail.Transaction = transaction;
            tail.CommandText = "SELECT sequence,digest,previous_digest,kind,payload FROM audit_record ORDER BY sequence DESC LIMIT 1;";
            await using var reader = await tail.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException("audit.meta-without-chain-tail");
            var previous = ReadAndValidateRow(reader);
            if (previous.AuditSequence == ulong.MaxValue || sequence != previous.AuditSequence + 1 ||
                !previous.RecordDigest.AsSpan().SequenceEqual(previousDigest))
                throw new InvalidDataException("audit.meta-chain-mismatch");
        }
        else if (previousDigest.Any(static b => b != 0))
        {
            throw new InvalidDataException("audit.meta-chain-mismatch");
        }

        var record = AuditRecordCodecV1.Create(sequence, previousDigest, draft);
        var payload = JsonSerializer.SerializeToUtf8Bytes(record, _json);
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO audit_record(sequence,digest,previous_digest,kind,payload) VALUES($s,$d,$p,$k,$v);";
            insert.Parameters.AddWithValue("$s", U64Be(record.AuditSequence));
            insert.Parameters.AddWithValue("$d", record.RecordDigest);
            insert.Parameters.AddWithValue("$p", record.PreviousDigest);
            insert.Parameters.AddWithValue("$k", record.AuditKind);
            insert.Parameters.AddWithValue("$v", payload);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (sequence == ulong.MaxValue) throw new InvalidDataException("audit.sequence-exhausted");
        await WriteMetaAsync(connection, transaction, NextSequenceKey, U64Be(sequence + 1), cancellationToken).ConfigureAwait(false);
        await WriteMetaAsync(connection, transaction, LastDigestKey, record.RecordDigest, cancellationToken).ConfigureAwait(false);
        return record;
    }

    private async ValueTask<List<AuditRecordV1>> ReadAllAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT sequence,digest,previous_digest,kind,payload FROM audit_record ORDER BY sequence;";
        var records = new List<AuditRecordV1>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) records.Add(ReadAndValidateRow(reader));
        return records;
    }

    private AuditRecordV1 ReadAndValidateRow(SqliteDataReader reader)
    {
        var sequenceBytes = (byte[])reader[0];
        var digest = (byte[])reader[1];
        var previous = (byte[])reader[2];
        var kind = reader.GetString(3);
        var payload = (byte[])reader[4];
        var record = JsonSerializer.Deserialize<AuditRecordV1>(payload, _json)
            ?? throw new InvalidDataException("audit.payload-invalid");
        AuditRecordCodecV1.ValidateDigest(record);
        if (ReadU64Be(sequenceBytes) != record.AuditSequence ||
            !digest.AsSpan().SequenceEqual(record.RecordDigest) ||
            !previous.AsSpan().SequenceEqual(record.PreviousDigest) ||
            !string.Equals(kind, record.AuditKind, StringComparison.Ordinal))
            throw new InvalidDataException("audit.storage-row-mismatch");
        return record;
    }

    private async ValueTask EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (!_initialized) await InitializeAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<SqliteConnection> OpenConfiguredConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ExecAsync(connection, null, "PRAGMA synchronous=FULL;", cancellationToken).ConfigureAwait(false);
        await ExecAsync(connection, null, "PRAGMA foreign_keys=ON;", cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static async ValueTask ExecAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<byte[]?> ReadMetaAsync(SqliteConnection connection, SqliteTransaction? transaction, string key, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT value FROM audit_meta WHERE key=$key;";
        command.Parameters.AddWithValue("$key", key);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is null or DBNull ? null : (byte[])value;
    }

    private static async ValueTask WriteMetaAsync(SqliteConnection connection, SqliteTransaction? transaction, string key, byte[] value, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO audit_meta(key,value) VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=excluded.value;";
        command.Parameters.AddWithValue("$k", key);
        command.Parameters.AddWithValue("$v", value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private AuditRetentionAnchorV1 DeserializeAnchor(byte[] bytes)
        => JsonSerializer.Deserialize<AuditRetentionAnchorV1>(bytes, _json)
           ?? throw new InvalidDataException("audit.retention-anchor-invalid");

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
