using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Runtime;
using Microsoft.Data.Sqlite;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed record DurableCrossDomainTransactionStateV1(
    OpaqueId128 TransactionId,
    TransactionLifecycleV1 Lifecycle,
    ulong CreatedStep,
    ulong UpdatedStep,
    ulong? TerminalStep,
    byte[] StateWire,
    byte[] StateDigest);

public sealed partial class SqlitePersistenceStore
{
    /// <summary>
    /// Persists one authoritative CrossDomainTransaction state row. The semantic digest is always
    /// derived from the logical state; callers cannot supply an independent digest. Existing rows
    /// may only advance monotonically from ACTIVE to a newer ACTIVE revision or terminal state.
    /// Terminal rows are immutable. Exact retries are idempotent.
    /// </summary>
    public async Task<DurableCrossDomainTransactionStateV1> PersistCrossDomainTransactionStateAsync(
        CrossDomainTransactionStateV1 state,
        ReadOnlyMemory<byte> stateWire,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (stateWire.IsEmpty) throw new ArgumentException("State wire must not be empty.", nameof(stateWire));
        var digest = state.CanonicalDigest();
        RequireHash256(digest, nameof(digest));

        using var transaction = _connection.BeginTransaction();
        try
        {
            var existing = await ReadCrossDomainTransactionStateAsync(state.TransactionId, transaction, cancellationToken);
            if (existing is not null)
            {
                ValidateCrossDomainTransactionAdvance(existing, state, stateWire.Span, digest);
                if (IsExactCrossDomainTransactionRetry(existing, state, stateWire.Span, digest))
                {
                    transaction.Commit();
                    return existing;
                }
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
            command.Parameters.AddWithValue("$state_wire", stateWire.ToArray());
            command.Parameters.AddWithValue("$state_digest", digest);
            await command.ExecuteNonQueryAsync(cancellationToken);
            transaction.Commit();

            return new DurableCrossDomainTransactionStateV1(
                state.TransactionId,
                state.Lifecycle,
                state.CreatedStep,
                state.UpdatedStep,
                state.TerminalStep,
                stateWire.ToArray(),
                digest.ToArray());
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<DurableCrossDomainTransactionStateV1?> ReadCrossDomainTransactionStateAsync(
        OpaqueId128 transactionId,
        CancellationToken cancellationToken = default)
    {
        if (transactionId.IsZero) throw new ArgumentException("TransactionId ZERO is invalid.", nameof(transactionId));
        await using var command = _connection.CreateCommand();
        command.CommandText = """
SELECT transaction_id, lifecycle, created_step, updated_step, terminal_step, state_wire, state_digest
FROM cross_domain_transaction_state
WHERE transaction_id=$transaction_id;
""";
        command.Parameters.AddWithValue("$transaction_id", transactionId.ToBytes());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return ReadCrossDomainTransactionState(reader);
    }

    public async Task<IReadOnlyList<DurableCrossDomainTransactionStateV1>> ListActiveCrossDomainTransactionStatesCanonicalAsync(
        CancellationToken cancellationToken = default)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = """
SELECT transaction_id, lifecycle, created_step, updated_step, terminal_step, state_wire, state_digest
FROM cross_domain_transaction_state
WHERE lifecycle=$active
ORDER BY transaction_id ASC;
""";
        command.Parameters.AddWithValue("$active", checked((int)TransactionLifecycleV1.Active));
        var rows = new List<DurableCrossDomainTransactionStateV1>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) rows.Add(ReadCrossDomainTransactionState(reader));
        if (rows.Any(static row => row.Lifecycle != TransactionLifecycleV1.Active || row.TerminalStep is not null))
            throw new InvalidDataException("persistence.cross-domain-transaction-active-catalog-invalid");
        if (rows.Select(static row => row.TransactionId).Distinct().Count() != rows.Count)
            throw new InvalidDataException("persistence.cross-domain-transaction-active-catalog-duplicate-id");
        return Array.AsReadOnly(rows.ToArray());
    }

    private async Task<DurableCrossDomainTransactionStateV1?> ReadCrossDomainTransactionStateAsync(
        OpaqueId128 transactionId,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
SELECT transaction_id, lifecycle, created_step, updated_step, terminal_step, state_wire, state_digest
FROM cross_domain_transaction_state
WHERE transaction_id=$transaction_id;
""";
        command.Parameters.AddWithValue("$transaction_id", transactionId.ToBytes());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return ReadCrossDomainTransactionState(reader);
    }

    private static DurableCrossDomainTransactionStateV1 ReadCrossDomainTransactionState(SqliteDataReader reader)
    {
        var id = OpaqueId128.FromBytes((byte[])reader[0]);
        if (id.IsZero) throw new InvalidDataException("persistence.cross-domain-transaction-id-zero");
        var lifecycleRaw = reader.GetInt32(1);
        if (!Enum.IsDefined(typeof(TransactionLifecycleV1), lifecycleRaw))
            throw new InvalidDataException("persistence.cross-domain-transaction-lifecycle-invalid");
        var lifecycle = (TransactionLifecycleV1)lifecycleRaw;
        var created = U64Be.Decode((byte[])reader[2]);
        var updated = U64Be.Decode((byte[])reader[3]);
        var terminal = reader.IsDBNull(4) ? null : U64Be.Decode((byte[])reader[4]);
        var wire = ((byte[])reader[5]).ToArray();
        var digest = ((byte[])reader[6]).ToArray();
        if (wire.Length == 0) throw new InvalidDataException("persistence.cross-domain-transaction-wire-empty");
        RequireHash256(digest, "cross_domain_transaction_state.state_digest");
        if (updated < created || (lifecycle == TransactionLifecycleV1.Active) != (terminal is null) ||
            (terminal is { } terminalStep && terminalStep != updated))
            throw new InvalidDataException("persistence.cross-domain-transaction-row-invariant");
        return new DurableCrossDomainTransactionStateV1(id, lifecycle, created, updated, terminal, wire, digest);
    }

    private static void ValidateCrossDomainTransactionAdvance(
        DurableCrossDomainTransactionStateV1 existing,
        CrossDomainTransactionStateV1 next,
        ReadOnlySpan<byte> nextWire,
        ReadOnlySpan<byte> nextDigest)
    {
        if (existing.TransactionId != next.TransactionId || existing.CreatedStep != next.CreatedStep)
            throw new InvalidDataException("persistence.cross-domain-transaction-immutable-identity-drift");
        if (IsExactCrossDomainTransactionRetry(existing, next, nextWire, nextDigest)) return;
        if (existing.Lifecycle != TransactionLifecycleV1.Active)
            throw new InvalidDataException("persistence.cross-domain-transaction-terminal-immutable");
        if (next.UpdatedStep <= existing.UpdatedStep)
            throw new InvalidDataException("persistence.cross-domain-transaction-update-not-forward");
        if (next.Lifecycle == TransactionLifecycleV1.Active && next.TerminalStep is not null)
            throw new InvalidDataException("persistence.cross-domain-transaction-active-terminal-step");
        if (next.Lifecycle != TransactionLifecycleV1.Active && next.TerminalStep != next.UpdatedStep)
            throw new InvalidDataException("persistence.cross-domain-transaction-terminal-step-invalid");
    }

    private static bool IsExactCrossDomainTransactionRetry(
        DurableCrossDomainTransactionStateV1 existing,
        CrossDomainTransactionStateV1 next,
        ReadOnlySpan<byte> nextWire,
        ReadOnlySpan<byte> nextDigest)
        => existing.Lifecycle == next.Lifecycle &&
           existing.CreatedStep == next.CreatedStep &&
           existing.UpdatedStep == next.UpdatedStep &&
           existing.TerminalStep == next.TerminalStep &&
           existing.StateWire.AsSpan().SequenceEqual(nextWire) &&
           CryptographicOperations.FixedTimeEquals(existing.StateDigest, nextDigest);
}
