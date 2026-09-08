using System.Globalization;
using Microsoft.Data.Sqlite;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed record PersistencePragmaSnapshot(
    string JournalMode,
    int Synchronous,
    int ForeignKeys,
    int WalAutoCheckpoint,
    int BusyTimeout);

public sealed partial class SqlitePersistenceStore : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    private SqlitePersistenceStore(SqliteConnection connection)
    {
        _connection = connection;
    }

    public static async Task<SqlitePersistenceStore> OpenOrCreateAsync(
        WorldPersistencePaths paths,
        CancellationToken cancellationToken = default)
    {
        PersistenceLayout.EnsureGenerationDirectories(paths);

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        };

        var connection = new SqliteConnection(builder.ToString());
        try
        {
            await connection.OpenAsync(cancellationToken);
            var store = new SqlitePersistenceStore(connection);
            await store.ApplyAndValidateRequiredPragmasAsync(cancellationToken);
            await store.CreateInitialSchemaAsync(cancellationToken);
            await store.ValidateQuickCheckAsync(cancellationToken);
            return store;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task<PersistencePragmaSnapshot> ReadRequiredPragmasAsync(CancellationToken cancellationToken = default)
        => new(
            await ReadPragmaTextAsync("journal_mode", cancellationToken),
            await ReadPragmaIntAsync("synchronous", cancellationToken),
            await ReadPragmaIntAsync("foreign_keys", cancellationToken),
            await ReadPragmaIntAsync("wal_autocheckpoint", cancellationToken),
            await ReadPragmaIntAsync("busy_timeout", cancellationToken));

    public async Task<bool> HasTableAsync(string tableName, CancellationToken cancellationToken = default)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name LIMIT 1;";
        command.Parameters.AddWithValue("$name", tableName);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();

    private async Task ApplyAndValidateRequiredPragmasAsync(CancellationToken cancellationToken)
    {
        await ExecuteScalarAsync("PRAGMA journal_mode = WAL;", cancellationToken);
        await ExecuteNonQueryAsync("PRAGMA synchronous = FULL;", cancellationToken);
        await ExecuteNonQueryAsync("PRAGMA foreign_keys = ON;", cancellationToken);
        await ExecuteNonQueryAsync("PRAGMA wal_autocheckpoint = 0;", cancellationToken);
        await ExecuteNonQueryAsync("PRAGMA busy_timeout = 5000;", cancellationToken);

        var snapshot = await ReadRequiredPragmasAsync(cancellationToken);
        if (!string.Equals(snapshot.JournalMode, "wal", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("persistence.sqlite.journal-mode-not-wal");
        if (snapshot.Synchronous != 2)
            throw new InvalidDataException("persistence.sqlite.synchronous-not-full");
        if (snapshot.ForeignKeys != 1)
            throw new InvalidDataException("persistence.sqlite.foreign-keys-disabled");
        if (snapshot.WalAutoCheckpoint != 0)
            throw new InvalidDataException("persistence.sqlite.wal-autocheckpoint-enabled");
        if (snapshot.BusyTimeout != 5000)
            throw new InvalidDataException("persistence.sqlite.busy-timeout-mismatch");
    }

    private async Task CreateInitialSchemaAsync(CancellationToken cancellationToken)
    {
        const string sql = """
CREATE TABLE IF NOT EXISTS persistence_meta (
  singleton INTEGER PRIMARY KEY CHECK (singleton = 1),
  world_id BLOB NOT NULL CHECK (length(world_id) = 16),
  persistence_generation BLOB NOT NULL CHECK (length(persistence_generation) = 8),
  schema_major INTEGER NOT NULL CHECK (schema_major BETWEEN 0 AND 65535),
  schema_minor INTEGER NOT NULL CHECK (schema_minor BETWEEN 0 AND 65535),
  world_seed BLOB NOT NULL CHECK (length(world_seed) = 32),
  last_history_sequence BLOB NOT NULL CHECK (length(last_history_sequence) = 8),
  last_history_digest BLOB NOT NULL CHECK (length(last_history_digest) = 32),
  finalized_step BLOB NOT NULL CHECK (length(finalized_step) = 8),
  state_continuity_token BLOB NOT NULL CHECK (length(state_continuity_token) = 32),
  config_generation BLOB NOT NULL CHECK (length(config_generation) = 8),
  config_digest BLOB NOT NULL CHECK (length(config_digest) = 32),
  master_generation BLOB NOT NULL CHECK (length(master_generation) = 8)
);

CREATE TABLE IF NOT EXISTS history_record (
  sequence BLOB PRIMARY KEY CHECK (length(sequence) = 8),
  previous_record_digest BLOB NOT NULL CHECK (length(previous_record_digest) = 32),
  record_type TEXT NOT NULL,
  payload_schema_id TEXT NOT NULL,
  payload_schema_major INTEGER NOT NULL,
  payload_schema_minor INTEGER NOT NULL,
  payload_bytes BLOB NOT NULL,
  normalized_payload_digest BLOB NOT NULL CHECK (length(normalized_payload_digest) = 32),
  record_digest BLOB NOT NULL UNIQUE CHECK (length(record_digest) = 32)
) WITHOUT ROWID;

CREATE TABLE IF NOT EXISTS operation_state (
  operation_id BLOB PRIMARY KEY CHECK (length(operation_id) = 16),
  payload_digest BLOB NOT NULL CHECK (length(payload_digest) = 32),
  lifecycle INTEGER NOT NULL,
  accepted_sequence BLOB CHECK (accepted_sequence IS NULL OR length(accepted_sequence) = 8),
  scheduled_sequence BLOB CHECK (scheduled_sequence IS NULL OR length(scheduled_sequence) = 8),
  effective_step BLOB CHECK (effective_step IS NULL OR length(effective_step) = 8),
  terminal_sequence BLOB CHECK (terminal_sequence IS NULL OR length(terminal_sequence) = 8),
  terminal_status INTEGER,
  result_code TEXT,
  rich_result_payload BLOB
) WITHOUT ROWID;

CREATE TABLE IF NOT EXISTS scheduled_operation (
  effective_step BLOB NOT NULL CHECK (length(effective_step) = 8),
  order_key BLOB NOT NULL,
  operation_id BLOB NOT NULL CHECK (length(operation_id) = 16),
  PRIMARY KEY (effective_step, order_key, operation_id),
  UNIQUE (operation_id)
) WITHOUT ROWID;

CREATE TABLE IF NOT EXISTS simulation_config_state (
  generation BLOB PRIMARY KEY CHECK (length(generation) = 8),
  config_digest BLOB NOT NULL CHECK (length(config_digest) = 32),
  effective_step BLOB CHECK (effective_step IS NULL OR length(effective_step) = 8),
  normalized_config_bytes BLOB NOT NULL,
  history_sequence BLOB NOT NULL CHECK (length(history_sequence) = 8)
) WITHOUT ROWID;

CREATE TABLE IF NOT EXISTS core_operational_state (
  singleton INTEGER PRIMARY KEY CHECK (singleton = 1),
  master_generation BLOB NOT NULL CHECK (length(master_generation) = 8),
  world_pause_state INTEGER NOT NULL,
  pause_basis_step BLOB CHECK (pause_basis_step IS NULL OR length(pause_basis_step) = 8)
);

CREATE TABLE IF NOT EXISTS snapshot_catalog (
  snapshot_id BLOB PRIMARY KEY CHECK (length(snapshot_id) = 16),
  snapshot_step BLOB NOT NULL CHECK (length(snapshot_step) = 8),
  history_anchor_sequence BLOB NOT NULL CHECK (length(history_anchor_sequence) = 8),
  history_anchor_digest BLOB NOT NULL CHECK (length(history_anchor_digest) = 32),
  state_continuity_token BLOB NOT NULL CHECK (length(state_continuity_token) = 32),
  snapshot_digest BLOB NOT NULL CHECK (length(snapshot_digest) = 32),
  physical_manifest_digest BLOB NOT NULL CHECK (length(physical_manifest_digest) = 32),
  relative_directory TEXT NOT NULL UNIQUE
) WITHOUT ROWID;
""";
        await ExecuteNonQueryAsync(sql, cancellationToken);
    }

    private async Task<string> ReadPragmaTextAsync(string name, CancellationToken cancellationToken)
    {
        var value = await ExecuteScalarAsync($"PRAGMA {name};", cancellationToken);
        return Convert.ToString(value, CultureInfo.InvariantCulture)
            ?? throw new InvalidDataException($"persistence.sqlite.pragma-missing:{name}");
    }

    private async Task<int> ReadPragmaIntAsync(string name, CancellationToken cancellationToken)
    {
        var value = await ExecuteScalarAsync($"PRAGMA {name};", cancellationToken);
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private async Task<object?> ExecuteScalarAsync(string sql, CancellationToken cancellationToken)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(cancellationToken);
    }

    private async Task ExecuteNonQueryAsync(string sql, CancellationToken cancellationToken)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
