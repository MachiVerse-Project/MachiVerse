using System.Diagnostics;
using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using Microsoft.Data.Sqlite;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed record TerminalOperationCommit(
    OpaqueId128 OperationId,
    int TerminalStatus,
    string ResultCode,
    byte[]? RichResultPayload = null);

public sealed record DurableTransitionResult(ulong ResultingStep, ulong HistorySequence);

public sealed partial class SqlitePersistenceStore
{
    private const int TerminalLifecycle = 3;

    public async Task<DurableTransitionResult> PersistTransitionCommitAsync(
        ulong effectiveStep,
        ulong resultingStep,
        byte[] resultingStateContinuityToken,
        ulong activeConfigGeneration,
        byte[] activeConfigDigest,
        HistoryRecordMaterial history,
        IReadOnlyCollection<TerminalOperationCommit> terminalOperations,
        CancellationToken cancellationToken = default)
    {
        if (effectiveStep == ulong.MaxValue || resultingStep != effectiveStep + 1)
            throw new ArgumentException("resultingStep must equal effectiveStep + 1.", nameof(resultingStep));
        if (activeConfigGeneration == 0)
            throw new ArgumentOutOfRangeException(nameof(activeConfigGeneration), "ConfigGeneration starts at 1.");
        RequireHash256(resultingStateContinuityToken, nameof(resultingStateContinuityToken));
        RequireHash256(activeConfigDigest, nameof(activeConfigDigest));
        ValidateHistoryMaterial(history, "transition.committed.v1");
        ArgumentNullException.ThrowIfNull(terminalOperations);

        var orderedTerminalOperations = terminalOperations
            .OrderBy(static item => item.OperationId)
            .ToArray();

        if (orderedTerminalOperations.Select(static item => item.OperationId).Distinct().Count() != orderedTerminalOperations.Length)
            throw new ArgumentException("terminalOperations contains duplicate OperationId.", nameof(terminalOperations));

        foreach (var terminal in orderedTerminalOperations)
        {
            if (terminal.OperationId.IsZero) throw new ArgumentException("Terminal OperationId ZERO is invalid.", nameof(terminalOperations));
            _ = new StableToken(terminal.ResultCode);
        }

        Qa04PersistenceCrashInjectionV1.Hit("transition-commit", "before-db-begin");
        using var transaction = _connection.BeginTransaction();
        try
        {
            var transitionHead = await ReadTransitionHeadAsync(transaction, cancellationToken);
            if (transitionHead.FinalizedStep != effectiveStep)
                throw new InvalidDataException("persistence.transition-base-step-mismatch");

            var context = await ReadHistoryContextAsync(transaction, cancellationToken);
            ValidateNextHistoryRecord(history, context);

            var expectedContinuity = HistoryIntegrity.ComputeTransitionContinuityToken(
                context.WorldId,
                resultingStep,
                transitionHead.StateContinuityToken,
                history.RecordDigest);
            if (!CryptographicOperations.FixedTimeEquals(expectedContinuity, resultingStateContinuityToken))
                throw new InvalidDataException("persistence.transition-continuity-token-mismatch");

            await InsertHistoryRecordAsync(history, transaction, cancellationToken);

            await CommitTerminalOperationsBatchedAsync(
                orderedTerminalOperations,
                effectiveStep,
                history.Sequence,
                transaction,
                cancellationToken).ConfigureAwait(false);

            await using (var meta = _connection.CreateCommand())
            {
                meta.Transaction = transaction;
                meta.CommandText = """
UPDATE persistence_meta
SET last_history_sequence=$history_sequence,
    last_history_digest=$history_digest,
    finalized_step=$finalized_step,
    state_continuity_token=$continuity_token,
    config_generation=$config_generation,
    config_digest=$config_digest
WHERE singleton=1;
""";
                meta.Parameters.AddWithValue("$history_sequence", U64Be.Encode(history.Sequence));
                meta.Parameters.AddWithValue("$history_digest", history.RecordDigest);
                meta.Parameters.AddWithValue("$finalized_step", U64Be.Encode(resultingStep));
                meta.Parameters.AddWithValue("$continuity_token", resultingStateContinuityToken);
                meta.Parameters.AddWithValue("$config_generation", U64Be.Encode(activeConfigGeneration));
                meta.Parameters.AddWithValue("$config_digest", activeConfigDigest);
                if (await meta.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidDataException("persistence.meta-update-failed");
            }

            Qa04PersistenceCrashInjectionV1.Hit("transition-commit", "mid-write");
            Qa04PersistenceCrashInjectionV1.Hit("transition-commit", "before-fsync-or-commit");
            var commitStarted = Stopwatch.GetTimestamp();
            transaction.Commit();
            ObserveSuccessfulCommit(Stopwatch.GetElapsedTime(commitStarted));
            Qa04PersistenceCrashInjectionV1.Hit("transition-commit", "immediately-after-commit");
            Qa04PersistenceCrashInjectionV1.Hit("transition-commit", "before-response-or-publication");
            return new DurableTransitionResult(resultingStep, history.Sequence);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<(ulong FinalizedStep, byte[] ContinuityToken, ulong ConfigGeneration, byte[] ConfigDigest)> ReadRecoveryHeadAsync(
        CancellationToken cancellationToken = default)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = """
SELECT finalized_step, state_continuity_token, config_generation, config_digest
FROM persistence_meta
WHERE singleton=1;
""";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidDataException("persistence.meta-not-initialized");

        var continuity = (byte[])reader[1];
        var configDigest = (byte[])reader[3];
        RequireHash256(continuity, "state_continuity_token");
        RequireHash256(configDigest, "config_digest");
        return (
            U64Be.Decode((byte[])reader[0]),
            continuity,
            U64Be.Decode((byte[])reader[2]),
            configDigest);
    }

    private sealed record TransitionHead(ulong FinalizedStep, byte[] StateContinuityToken);

    private async Task<TransitionHead> ReadTransitionHeadAsync(
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT finalized_step, state_continuity_token FROM persistence_meta WHERE singleton=1;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidDataException("persistence.meta-not-initialized");
        var continuity = (byte[])reader[1];
        RequireHash256(continuity, "state_continuity_token");
        return new TransitionHead(U64Be.Decode((byte[])reader[0]), continuity);
    }

    private async Task CommitTerminalOperationsBatchedAsync(
        IReadOnlyList<TerminalOperationCommit> terminals,
        ulong effectiveStep,
        ulong terminalSequence,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (terminals.Count == 0)
            return;

        await using (var create = _connection.CreateCommand())
        {
            create.Transaction = transaction;
            create.CommandText = """
CREATE TEMP TABLE IF NOT EXISTS qa04_terminal_commit_batch (
    operation_id BLOB PRIMARY KEY NOT NULL,
    terminal_status INTEGER NOT NULL,
    result_code TEXT NOT NULL,
    rich_result_payload BLOB NULL
) WITHOUT ROWID;
DELETE FROM qa04_terminal_commit_batch;
""";
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        // Keep well below SQLite's variable limit while replacing thousands of per-row commands
        // with bounded multi-row inserts. This chunk size is an implementation safety bound, not
        // an operational tuning input and does not affect authority or canonical ordering.
        const int rowsPerInsert = 400;
        for (var offset = 0; offset < terminals.Count; offset += rowsPerInsert)
        {
            var count = Math.Min(rowsPerInsert, terminals.Count - offset);
            await using var insert = _connection.CreateCommand();
            insert.Transaction = transaction;
            var sql = new System.Text.StringBuilder(
                "INSERT INTO qa04_terminal_commit_batch(operation_id,terminal_status,result_code,rich_result_payload) VALUES ");
            for (var index = 0; index < count; index++)
            {
                if (index != 0)
                    sql.Append(',');
                sql.Append("($id").Append(index)
                    .Append(",$status").Append(index)
                    .Append(",$code").Append(index)
                    .Append(",$payload").Append(index).Append(')');

                var terminal = terminals[offset + index];
                insert.Parameters.AddWithValue("$id" + index, terminal.OperationId.ToBytes());
                insert.Parameters.AddWithValue("$status" + index, terminal.TerminalStatus);
                insert.Parameters.AddWithValue("$code" + index, terminal.ResultCode);
                insert.Parameters.AddWithValue(
                    "$payload" + index,
                    (object?)terminal.RichResultPayload ?? DBNull.Value);
            }
            insert.CommandText = sql.ToString();
            if (await insert.ExecuteNonQueryAsync(cancellationToken) != count)
                throw new InvalidDataException("persistence.transition-terminal-stage-count-drift");
        }

        await using (var validate = _connection.CreateCommand())
        {
            validate.Transaction = transaction;
            validate.CommandText = """
SELECT
    SUM(CASE WHEN o.operation_id IS NULL THEN 1 ELSE 0 END),
    SUM(CASE WHEN o.operation_id IS NOT NULL
              AND (o.lifecycle <> $scheduled_lifecycle
                   OR o.effective_step IS NULL
                   OR o.effective_step <> $effective_step)
             THEN 1 ELSE 0 END)
FROM qa04_terminal_commit_batch AS t
LEFT JOIN operation_state AS o ON o.operation_id = t.operation_id;
""";
            validate.Parameters.AddWithValue("$scheduled_lifecycle", ScheduledLifecycle);
            validate.Parameters.AddWithValue("$effective_step", U64Be.Encode(effectiveStep));
            await using var reader = await validate.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidDataException("persistence.transition-terminal-validation-missing");
            var missing = reader.IsDBNull(0) ? 0L : reader.GetInt64(0);
            var invalid = reader.IsDBNull(1) ? 0L : reader.GetInt64(1);
            if (missing != 0)
                throw new InvalidDataException("persistence.transition-operation-missing");
            if (invalid != 0)
                throw new InvalidDataException("persistence.transition-operation-not-scheduled-for-step");
        }

        await using (var update = _connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
UPDATE operation_state
SET lifecycle=$terminal_lifecycle,
    terminal_sequence=$terminal_sequence,
    terminal_status=(
        SELECT t.terminal_status
        FROM qa04_terminal_commit_batch AS t
        WHERE t.operation_id=operation_state.operation_id),
    result_code=(
        SELECT t.result_code
        FROM qa04_terminal_commit_batch AS t
        WHERE t.operation_id=operation_state.operation_id),
    rich_result_payload=(
        SELECT t.rich_result_payload
        FROM qa04_terminal_commit_batch AS t
        WHERE t.operation_id=operation_state.operation_id)
WHERE operation_id IN (SELECT operation_id FROM qa04_terminal_commit_batch);
""";
            update.Parameters.AddWithValue("$terminal_lifecycle", TerminalLifecycle);
            update.Parameters.AddWithValue("$terminal_sequence", U64Be.Encode(terminalSequence));
            if (await update.ExecuteNonQueryAsync(cancellationToken) != terminals.Count)
                throw new InvalidDataException("persistence.transition-operation-update-failed");
        }

        await using (var remove = _connection.CreateCommand())
        {
            remove.Transaction = transaction;
            remove.CommandText = """
DELETE FROM scheduled_operation
WHERE operation_id IN (SELECT operation_id FROM qa04_terminal_commit_batch);
""";
            if (await remove.ExecuteNonQueryAsync(cancellationToken) != terminals.Count)
                throw new InvalidDataException("persistence.transition-scheduled-row-missing");
        }
    }

}
