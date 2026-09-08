using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using Microsoft.Data.Sqlite;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed record WorldPersistenceMetadataSeed(
    OpaqueId128 WorldId,
    ulong PersistenceGeneration,
    WorldSeed256 WorldSeed,
    byte[] InitialStateContinuityToken,
    ulong ConfigGeneration,
    byte[] ConfigDigest,
    ulong MasterGeneration);

public sealed record HistoryAnchor(ulong Sequence, byte[] Digest);

public enum DurableAcceptanceStatus
{
    Accepted = 1,
    Duplicate = 2
}

public sealed record DurableAcceptanceResult(DurableAcceptanceStatus Status, ulong AcceptedSequence);

public sealed partial class SqlitePersistenceStore
{
    private const int AcceptedLifecycle = 1;
    private const int ScheduledLifecycle = 2;
    private static readonly byte[] ZeroHash256 = new byte[32];

    public async Task InitializeWorldMetadataAsync(
        WorldPersistenceMetadataSeed seed,
        HistoryRecordMaterial genesisHistory,
        CancellationToken cancellationToken = default)
    {
        if (seed.WorldId.IsZero) throw new ArgumentException("WorldId ZERO is invalid.", nameof(seed));
        if (seed.PersistenceGeneration == 0) throw new ArgumentOutOfRangeException(nameof(seed), "PersistenceGeneration starts at 1.");
        if (seed.ConfigGeneration == 0) throw new ArgumentOutOfRangeException(nameof(seed), "ConfigGeneration starts at 1 for initialized worlds.");
        if (seed.MasterGeneration == 0) throw new ArgumentOutOfRangeException(nameof(seed), "MasterGeneration starts at 1.");
        RequireHash256(seed.InitialStateContinuityToken, nameof(seed.InitialStateContinuityToken));
        RequireHash256(seed.ConfigDigest, nameof(seed.ConfigDigest));
        ValidateHistoryMaterial(genesisHistory, "world.genesis.v1");
        if (genesisHistory.WorldId != seed.WorldId)
            throw new InvalidDataException("persistence.history-world-mismatch");
        if (genesisHistory.Sequence != 1 || !CryptographicOperations.FixedTimeEquals(genesisHistory.PreviousRecordDigest, ZeroHash256))
            throw new InvalidDataException("persistence.genesis-history-anchor-invalid");

        var expectedContinuity = HistoryIntegrity.ComputeGenesisContinuityToken(seed.WorldId, genesisHistory.RecordDigest);
        if (!CryptographicOperations.FixedTimeEquals(expectedContinuity, seed.InitialStateContinuityToken))
            throw new InvalidDataException("persistence.genesis-continuity-token-mismatch");

        using var transaction = _connection.BeginTransaction();
        try
        {
            await using (var existing = _connection.CreateCommand())
            {
                existing.Transaction = transaction;
                existing.CommandText = "SELECT COUNT(*) FROM persistence_meta;";
                var count = Convert.ToInt32(await existing.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
                if (count != 0) throw new InvalidDataException("persistence.meta-already-initialized");
            }

            await InsertHistoryRecordAsync(genesisHistory, transaction, cancellationToken);

            await using (var command = _connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
INSERT INTO persistence_meta (
  singleton, world_id, persistence_generation, schema_major, schema_minor, world_seed,
  last_history_sequence, last_history_digest, finalized_step, state_continuity_token,
  config_generation, config_digest, master_generation
) VALUES (
  1, $world_id, $persistence_generation, 1, 0, $world_seed,
  $last_history_sequence, $last_history_digest, $finalized_step, $state_continuity_token,
  $config_generation, $config_digest, $master_generation
);
""";
                command.Parameters.AddWithValue("$world_id", seed.WorldId.ToBytes());
                command.Parameters.AddWithValue("$persistence_generation", U64Be.Encode(seed.PersistenceGeneration));
                command.Parameters.AddWithValue("$world_seed", seed.WorldSeed.ToBytes());
                command.Parameters.AddWithValue("$last_history_sequence", U64Be.Encode(genesisHistory.Sequence));
                command.Parameters.AddWithValue("$last_history_digest", genesisHistory.RecordDigest);
                command.Parameters.AddWithValue("$finalized_step", U64Be.Encode(0));
                command.Parameters.AddWithValue("$state_continuity_token", seed.InitialStateContinuityToken);
                command.Parameters.AddWithValue("$config_generation", U64Be.Encode(seed.ConfigGeneration));
                command.Parameters.AddWithValue("$config_digest", seed.ConfigDigest);
                command.Parameters.AddWithValue("$master_generation", U64Be.Encode(seed.MasterGeneration));
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var operational = _connection.CreateCommand())
            {
                operational.Transaction = transaction;
                operational.CommandText = """
INSERT INTO core_operational_state (singleton, master_generation, world_pause_state, pause_basis_step)
VALUES (1, $master_generation, 0, NULL);
""";
                operational.Parameters.AddWithValue("$master_generation", U64Be.Encode(seed.MasterGeneration));
                await operational.ExecuteNonQueryAsync(cancellationToken);
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<HistoryAnchor> ReadHistoryAnchorAsync(CancellationToken cancellationToken = default)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT last_history_sequence, last_history_digest FROM persistence_meta WHERE singleton=1;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidDataException("persistence.meta-not-initialized");
        var sequence = U64Be.Decode((byte[])reader[0]);
        var digest = (byte[])reader[1];
        RequireHash256(digest, "last_history_digest");
        return new HistoryAnchor(sequence, digest);
    }

    public async Task<DurableAcceptanceResult> PersistAcceptedOperationAsync(
        OpaqueId128 operationId,
        byte[] operationPayloadDigest,
        HistoryRecordMaterial history,
        CancellationToken cancellationToken = default)
    {
        if (operationId.IsZero) throw new ArgumentException("OperationId ZERO is invalid.", nameof(operationId));
        RequireHash256(operationPayloadDigest, nameof(operationPayloadDigest));
        ValidateHistoryMaterial(history, "operation.accepted.v1");

        using var transaction = _connection.BeginTransaction();
        try
        {
            var duplicate = await TryReadOperationPayloadDigestAsync(operationId, transaction, cancellationToken);
            if (duplicate is not null)
            {
                if (!CryptographicOperations.FixedTimeEquals(duplicate.Value.Digest, operationPayloadDigest))
                    throw new InvalidDataException("protocol.operation-payload-mismatch");
                transaction.Commit();
                return new DurableAcceptanceResult(DurableAcceptanceStatus.Duplicate, duplicate.Value.AcceptedSequence);
            }

            var context = await ReadHistoryContextAsync(transaction, cancellationToken);
            ValidateNextHistoryRecord(history, context);
            await InsertHistoryRecordAsync(history, transaction, cancellationToken);

            await using (var operation = _connection.CreateCommand())
            {
                operation.Transaction = transaction;
                operation.CommandText = """
INSERT INTO operation_state (
  operation_id, payload_digest, lifecycle, accepted_sequence,
  scheduled_sequence, effective_step, terminal_sequence, terminal_status, result_code, rich_result_payload
) VALUES (
  $operation_id, $payload_digest, $lifecycle, $accepted_sequence,
  NULL, NULL, NULL, NULL, NULL, NULL
);
""";
                operation.Parameters.AddWithValue("$operation_id", operationId.ToBytes());
                operation.Parameters.AddWithValue("$payload_digest", operationPayloadDigest);
                operation.Parameters.AddWithValue("$lifecycle", AcceptedLifecycle);
                operation.Parameters.AddWithValue("$accepted_sequence", U64Be.Encode(history.Sequence));
                await operation.ExecuteNonQueryAsync(cancellationToken);
            }

            await UpdateHistoryAnchorAsync(history, transaction, cancellationToken);
            transaction.Commit();
            return new DurableAcceptanceResult(DurableAcceptanceStatus.Accepted, history.Sequence);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private async Task<(byte[] Digest, ulong AcceptedSequence)?> TryReadOperationPayloadDigestAsync(
        OpaqueId128 operationId,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT payload_digest, accepted_sequence FROM operation_state WHERE operation_id=$operation_id;";
        command.Parameters.AddWithValue("$operation_id", operationId.ToBytes());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var digest = (byte[])reader[0];
        RequireHash256(digest, "operation_state.payload_digest");
        if (reader.IsDBNull(1)) throw new InvalidDataException("persistence.accepted-operation-missing-sequence");
        return (digest, U64Be.Decode((byte[])reader[1]));
    }

    private sealed record HistoryContext(OpaqueId128 WorldId, HistoryAnchor Anchor);

    private async Task<HistoryContext> ReadHistoryContextAsync(
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT world_id, last_history_sequence, last_history_digest FROM persistence_meta WHERE singleton=1;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidDataException("persistence.meta-not-initialized");
        var worldId = OpaqueId128.FromBytes((byte[])reader[0]);
        var digest = (byte[])reader[2];
        RequireHash256(digest, "last_history_digest");
        return new HistoryContext(worldId, new HistoryAnchor(U64Be.Decode((byte[])reader[1]), digest));
    }

    private static void ValidateNextHistoryRecord(HistoryRecordMaterial history, HistoryContext context)
    {
        if (history.WorldId != context.WorldId)
            throw new InvalidDataException("persistence.history-world-mismatch");
        if (context.Anchor.Sequence == ulong.MaxValue)
            throw new OverflowException("HistorySequence cannot wrap.");
        if (history.Sequence != context.Anchor.Sequence + 1)
            throw new InvalidDataException("persistence.history-sequence-gap");
        if (!CryptographicOperations.FixedTimeEquals(history.PreviousRecordDigest, context.Anchor.Digest))
            throw new InvalidDataException("persistence.history-previous-digest-mismatch");
    }

    private async Task InsertHistoryRecordAsync(
        HistoryRecordMaterial history,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
INSERT INTO history_record (
  sequence, previous_record_digest, record_type, payload_schema_id,
  payload_schema_major, payload_schema_minor, payload_bytes,
  normalized_payload_digest, record_digest
) VALUES (
  $sequence, $previous_record_digest, $record_type, $payload_schema_id,
  $payload_schema_major, $payload_schema_minor, $payload_bytes,
  $normalized_payload_digest, $record_digest
);
""";
        command.Parameters.AddWithValue("$sequence", U64Be.Encode(history.Sequence));
        command.Parameters.AddWithValue("$previous_record_digest", history.PreviousRecordDigest);
        command.Parameters.AddWithValue("$record_type", history.RecordType);
        command.Parameters.AddWithValue("$payload_schema_id", history.PayloadSchemaId);
        command.Parameters.AddWithValue("$payload_schema_major", history.PayloadSchemaMajor);
        command.Parameters.AddWithValue("$payload_schema_minor", history.PayloadSchemaMinor);
        command.Parameters.AddWithValue("$payload_bytes", history.PayloadBytes);
        command.Parameters.AddWithValue("$normalized_payload_digest", history.NormalizedPayloadDigest);
        command.Parameters.AddWithValue("$record_digest", history.RecordDigest);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task UpdateHistoryAnchorAsync(
        HistoryRecordMaterial history,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var meta = _connection.CreateCommand();
        meta.Transaction = transaction;
        meta.CommandText = """
UPDATE persistence_meta
SET last_history_sequence=$sequence, last_history_digest=$digest
WHERE singleton=1;
""";
        meta.Parameters.AddWithValue("$sequence", U64Be.Encode(history.Sequence));
        meta.Parameters.AddWithValue("$digest", history.RecordDigest);
        if (await meta.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidDataException("persistence.meta-update-failed");
    }

    private static void ValidateHistoryMaterial(HistoryRecordMaterial history, string expectedRecordType)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (history.Sequence == 0) throw new ArgumentOutOfRangeException(nameof(history), "HistorySequence starts at 1.");
        _ = new StableToken(history.RecordType);
        _ = new StableToken(history.PayloadSchemaId);
        if (!string.Equals(history.RecordType, expectedRecordType, StringComparison.Ordinal))
            throw new InvalidDataException($"persistence.unexpected-history-record-type:{history.RecordType}");
        RequireHash256(history.PreviousRecordDigest, nameof(history.PreviousRecordDigest));
        RequireHash256(history.NormalizedPayloadDigest, nameof(history.NormalizedPayloadDigest));
        RequireHash256(history.RecordDigest, nameof(history.RecordDigest));
        ArgumentNullException.ThrowIfNull(history.PayloadBytes);
        if (history.NormalizedPayloadBytes.Length == 0)
            throw new InvalidDataException("persistence.normalized-history-payload-empty");

        var normalizedDigest = HashSuite.Hash256(history.NormalizedPayloadBytes);
        if (!CryptographicOperations.FixedTimeEquals(normalizedDigest, history.NormalizedPayloadDigest))
            throw new InvalidDataException("persistence.normalized-history-payload-digest-mismatch");

        var recordDigest = HistoryIntegrity.ComputeHistoryRecordDigest(
            history.WorldId,
            history.Sequence,
            history.PreviousRecordDigest,
            new StableToken(history.RecordType),
            history.NormalizedPayloadBytes);
        if (!CryptographicOperations.FixedTimeEquals(recordDigest, history.RecordDigest))
            throw new InvalidDataException("persistence.history-record-digest-mismatch");
    }

    private static void RequireHash256(byte[] value, string field)
    {
        ArgumentNullException.ThrowIfNull(value, field);
        if (value.Length != 32) throw new ArgumentException($"{field} must be exactly 32 bytes.", field);
    }
}
