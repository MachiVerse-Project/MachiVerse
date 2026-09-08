using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed record CoreProtocolPersistenceHeadV1(
    OpaqueId128 WorldId,
    ulong FinalizedStep,
    byte[] StateContinuityToken,
    ulong ConfigGeneration,
    byte[] ConfigDigest,
    ulong MasterGeneration);

public sealed record DurableMasterGenerationResultV1(
    ulong PreviousGeneration,
    ulong NextGeneration,
    ulong HistorySequence);

public sealed partial class SqlitePersistenceStore
{
    public async Task<CoreProtocolPersistenceHeadV1> ReadCoreProtocolHeadAsync(
        CancellationToken cancellationToken = default)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = """
SELECT world_id, finalized_step, state_continuity_token,
       config_generation, config_digest, master_generation
FROM persistence_meta
WHERE singleton=1;
""";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidDataException("persistence.meta-not-initialized");

        var continuity = (byte[])reader[2];
        var configDigest = (byte[])reader[4];
        RequireHash256(continuity, "state_continuity_token");
        RequireHash256(configDigest, "config_digest");
        var masterGeneration = U64Be.Decode((byte[])reader[5]);
        if (masterGeneration == 0)
            throw new InvalidDataException("persistence.master-generation-invalid");

        return new CoreProtocolPersistenceHeadV1(
            OpaqueId128.FromBytes((byte[])reader[0]),
            U64Be.Decode((byte[])reader[1]),
            continuity.ToArray(),
            U64Be.Decode((byte[])reader[3]),
            configDigest.ToArray(),
            masterGeneration);
    }

    public async Task<DurableMasterGenerationResultV1> PersistMasterGenerationChangeAsync(
        ulong previousGeneration,
        ulong nextGeneration,
        HistoryRecordMaterial history,
        CancellationToken cancellationToken = default)
    {
        if (previousGeneration == 0 || previousGeneration == ulong.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(previousGeneration));
        if (nextGeneration != previousGeneration + 1)
            throw new InvalidDataException("master.generation-step-invalid");
        ValidateHistoryMaterial(history, "master.generation.changed.v1");
        if (!string.Equals(history.PayloadSchemaId, "persistence.master-generation-changed", StringComparison.Ordinal) ||
            history.PayloadSchemaMajor != 1 || history.PayloadSchemaMinor != 0)
            throw new InvalidDataException("persistence.master-generation-schema-mismatch");

        using var transaction = _connection.BeginTransaction();
        try
        {
            ulong persistedMetaGeneration;
            await using (var command = _connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT master_generation FROM persistence_meta WHERE singleton=1;";
                var raw = await command.ExecuteScalarAsync(cancellationToken)
                    ?? throw new InvalidDataException("persistence.meta-not-initialized");
                persistedMetaGeneration = U64Be.Decode((byte[])raw);
            }

            ulong operationalGeneration;
            await using (var command = _connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT master_generation FROM core_operational_state WHERE singleton=1;";
                var raw = await command.ExecuteScalarAsync(cancellationToken)
                    ?? throw new InvalidDataException("persistence.operational-state-not-initialized");
                operationalGeneration = U64Be.Decode((byte[])raw);
            }

            if (persistedMetaGeneration != previousGeneration || operationalGeneration != previousGeneration)
                throw new InvalidDataException("master.stale-generation");

            var context = await ReadHistoryContextAsync(transaction, cancellationToken);
            ValidateNextHistoryRecord(history, context);
            await InsertHistoryRecordAsync(history, transaction, cancellationToken);

            await using (var meta = _connection.CreateCommand())
            {
                meta.Transaction = transaction;
                meta.CommandText = """
UPDATE persistence_meta
SET last_history_sequence=$sequence,
    last_history_digest=$digest,
    master_generation=$master_generation
WHERE singleton=1 AND master_generation=$previous_generation;
""";
                meta.Parameters.AddWithValue("$sequence", U64Be.Encode(history.Sequence));
                meta.Parameters.AddWithValue("$digest", history.RecordDigest);
                meta.Parameters.AddWithValue("$master_generation", U64Be.Encode(nextGeneration));
                meta.Parameters.AddWithValue("$previous_generation", U64Be.Encode(previousGeneration));
                if (await meta.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidDataException("master.stale-generation");
            }

            await using (var operational = _connection.CreateCommand())
            {
                operational.Transaction = transaction;
                operational.CommandText = """
UPDATE core_operational_state
SET master_generation=$master_generation
WHERE singleton=1 AND master_generation=$previous_generation;
""";
                operational.Parameters.AddWithValue("$master_generation", U64Be.Encode(nextGeneration));
                operational.Parameters.AddWithValue("$previous_generation", U64Be.Encode(previousGeneration));
                if (await operational.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidDataException("master.stale-generation");
            }

            transaction.Commit();
            return new DurableMasterGenerationResultV1(previousGeneration, nextGeneration, history.Sequence);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }
}
