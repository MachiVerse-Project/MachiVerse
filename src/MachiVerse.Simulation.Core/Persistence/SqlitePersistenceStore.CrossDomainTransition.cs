using System.Diagnostics;
using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Runtime;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed record CrossDomainTransactionStateCommitV1(
    CrossDomainTransactionStateV1 State,
    byte[] StateWire);

public sealed partial class SqlitePersistenceStore
{
    /// <summary>
    /// Commits a Step transition and CrossDomainTransaction authority changes in one SQLite
    /// transaction. History, terminal Operation rows, transaction state rows, and persistence_meta
    /// therefore become durable atomically or not at all.
    /// </summary>
    public async Task<DurableTransitionResult> PersistTransitionCommitWithCrossDomainTransactionsAsync(
        ulong effectiveStep,
        ulong resultingStep,
        byte[] resultingStateContinuityToken,
        ulong activeConfigGeneration,
        byte[] activeConfigDigest,
        HistoryRecordMaterial history,
        IReadOnlyCollection<TerminalOperationCommit> terminalOperations,
        IReadOnlyCollection<CrossDomainTransactionStateCommitV1> crossDomainTransactions,
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
        ArgumentNullException.ThrowIfNull(crossDomainTransactions);

        var orderedTerminalOperations = terminalOperations
            .OrderBy(static item => item.OperationId)
            .ToArray();
        if (orderedTerminalOperations.Select(static item => item.OperationId).Distinct().Count() != orderedTerminalOperations.Length)
            throw new ArgumentException("terminalOperations contains duplicate OperationId.", nameof(terminalOperations));
        foreach (var terminal in orderedTerminalOperations)
        {
            if (terminal.OperationId.IsZero) throw new ArgumentException("Terminal OperationId ZERO is invalid.", nameof(terminalOperations));
            _ = new Determinism.StableToken(terminal.ResultCode);
        }

        var orderedTransactions = crossDomainTransactions
            .Select(static item => item ?? throw new ArgumentNullException(nameof(crossDomainTransactions)))
            .OrderBy(static item => item.State.TransactionId)
            .ToArray();
        if (orderedTransactions.Select(static item => item.State.TransactionId).Distinct().Count() != orderedTransactions.Length)
            throw new ArgumentException("crossDomainTransactions contains duplicate TransactionId.", nameof(crossDomainTransactions));
        foreach (var item in orderedTransactions)
        {
            ArgumentNullException.ThrowIfNull(item.State);
            if (item.StateWire is null || item.StateWire.Length == 0)
                throw new ArgumentException("CrossDomainTransaction StateWire must not be empty.", nameof(crossDomainTransactions));
            if (item.State.UpdatedStep != resultingStep)
                throw new InvalidDataException("persistence.cross-domain-transaction-transition-step-mismatch");
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
                await CommitTerminalOperationAsync(terminal, effectiveStep, history.Sequence, transaction, cancellationToken);

            foreach (var item in orderedTransactions)
                await UpsertCrossDomainTransactionStateInTransitionAsync(item, transaction, cancellationToken);

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

            var commitStarted = Stopwatch.GetTimestamp();
            transaction.Commit();
            ObserveSuccessfulCommit(Stopwatch.GetElapsedTime(commitStarted));
            return new DurableTransitionResult(resultingStep, history.Sequence);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private async Task UpsertCrossDomainTransactionStateInTransitionAsync(
        CrossDomainTransactionStateCommitV1 item,
        Microsoft.Data.Sqlite.SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var state = item.State;
        var wire = item.StateWire;
        var digest = state.CanonicalDigest();
        RequireHash256(digest, "cross_domain_transaction_state.state_digest");

        var existing = await ReadCrossDomainTransactionStateAsync(state.TransactionId, transaction, cancellationToken);
        if (existing is not null)
        {
            ValidateCrossDomainTransactionAdvance(existing, state, wire, digest);
            if (IsExactCrossDomainTransactionRetry(existing, state, wire, digest)) return;
        }

        await using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
INSERT INTO cross_domain_transaction_state (
  transaction_id, lifecycle, created_step, updated_step, terminal_step, state_wire, state_digest
) VALUES (
  $transaction_id, $lifecycle, $created_step, $updated_step, $terminal_step, $state_wire, $state_digest
)
ON CONFLICT(transaction_id) DO UPDATE SET
  lifecycle=excluded.lifecycle,
  created_step=excluded.created_step,
  updated_step=excluded.updated_step,
  terminal_step=excluded.terminal_step,
  state_wire=excluded.state_wire,
  state_digest=excluded.state_digest;
""";
        command.Parameters.AddWithValue("$transaction_id", state.TransactionId.ToBytes());
        command.Parameters.AddWithValue("$lifecycle", checked((int)state.Lifecycle));
        command.Parameters.AddWithValue("$created_step", U64Be.Encode(state.CreatedStep));
        command.Parameters.AddWithValue("$updated_step", U64Be.Encode(state.UpdatedStep));
        command.Parameters.AddWithValue("$terminal_step", state.TerminalStep is { } terminal ? U64Be.Encode(terminal) : DBNull.Value);
        command.Parameters.AddWithValue("$state_wire", wire);
        command.Parameters.AddWithValue("$state_digest", digest);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidDataException("persistence.cross-domain-transaction-transition-upsert-failed");
    }
}
