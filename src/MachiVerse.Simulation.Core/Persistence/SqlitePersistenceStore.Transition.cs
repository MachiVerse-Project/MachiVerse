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

            foreach (var terminal in orderedTerminalOperations)
            {
                await CommitTerminalOperationAsync(terminal, effectiveStep, history.Sequence, transaction, cancellationToken);
            }

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

            transaction.Commit();
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

    private async Task CommitTerminalOperationAsync(
        TerminalOperationCommit terminal,
        ulong effectiveStep,
        ulong terminalSequence,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using (var state = _connection.CreateCommand())
        {
            state.Transaction = transaction;
            state.CommandText = "SELECT lifecycle, effective_step FROM operation_state WHERE operation_id=$operation_id;";
            state.Parameters.AddWithValue("$operation_id", terminal.OperationId.ToBytes());
            await using var reader = await state.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidDataException("persistence.transition-operation-missing");
            if (reader.GetInt32(0) != ScheduledLifecycle || reader.IsDBNull(1) || U64Be.Decode((byte[])reader[1]) != effectiveStep)
                throw new InvalidDataException("persistence.transition-operation-not-scheduled-for-step");
        }

        await using (var update = _connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
UPDATE operation_state
SET lifecycle=$lifecycle,
    terminal_sequence=$terminal_sequence,
    terminal_status=$terminal_status,
    result_code=$result_code,
    rich_result_payload=$rich_result_payload
WHERE operation_id=$operation_id;
""";
            update.Parameters.AddWithValue("$lifecycle", TerminalLifecycle);
            update.Parameters.AddWithValue("$terminal_sequence", U64Be.Encode(terminalSequence));
            update.Parameters.AddWithValue("$terminal_status", terminal.TerminalStatus);
            update.Parameters.AddWithValue("$result_code", terminal.ResultCode);
            update.Parameters.AddWithValue("$rich_result_payload", (object?)terminal.RichResultPayload ?? DBNull.Value);
            update.Parameters.AddWithValue("$operation_id", terminal.OperationId.ToBytes());
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidDataException("persistence.transition-operation-update-failed");
        }

        await using (var remove = _connection.CreateCommand())
        {
            remove.Transaction = transaction;
            remove.CommandText = "DELETE FROM scheduled_operation WHERE operation_id=$operation_id;";
            remove.Parameters.AddWithValue("$operation_id", terminal.OperationId.ToBytes());
            if (await remove.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidDataException("persistence.transition-scheduled-row-missing");
        }
    }
}
