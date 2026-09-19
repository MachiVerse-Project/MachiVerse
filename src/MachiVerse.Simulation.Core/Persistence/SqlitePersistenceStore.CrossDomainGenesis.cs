using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Runtime;
using Microsoft.Data.Sqlite;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed partial class SqlitePersistenceStore
{
    /// <summary>
    /// Initializes world metadata and the canonical CrossDomainTransaction authority that already
    /// exists at genesis in one SQLite transaction. This is deliberately separate from transition
    /// persistence: genesis transaction rows must be ACTIVE at CreatedStep=UpdatedStep=0 and must
    /// never be misrepresented as changes produced by the first Step transition.
    /// </summary>
    public async Task InitializeWorldMetadataWithCanonicalCrossDomainTransactionsAsync(
        WorldPersistenceMetadataSeed seed,
        HistoryRecordMaterial genesisHistory,
        IReadOnlyCollection<CrossDomainTransactionStateV1> genesisTransactions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(genesisTransactions);
        if (genesisTransactions.Count == 0)
            throw new ArgumentException("Genesis CrossDomainTransaction authority must not be empty.", nameof(genesisTransactions));

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

        var orderedTransactions = genesisTransactions
            .Select(CrossDomainTransactionStateCommitV1.CreateCanonical)
            .OrderBy(static item => item.State.TransactionId)
            .ToArray();
        if (orderedTransactions.Select(static item => item.State.TransactionId).Distinct().Count() != orderedTransactions.Length)
            throw new InvalidDataException("persistence.genesis-cross-domain-transaction-duplicate");
        foreach (var item in orderedTransactions)
        {
            var state = item.State;
            if (!state.IsActive || state.Lifecycle != TransactionLifecycleV1.Active ||
                state.CreatedStep != 0 || state.UpdatedStep != 0 || state.TerminalStep is not null)
                throw new InvalidDataException("persistence.genesis-cross-domain-transaction-state-invalid");
            if (item.StateWire.Length == 0)
                throw new InvalidDataException("persistence.genesis-cross-domain-transaction-wire-empty");
        }

        using var transaction = _connection.BeginTransaction();
        try
        {
            await using (var existing = _connection.CreateCommand())
            {
                existing.Transaction = transaction;
                existing.CommandText = "SELECT COUNT(*) FROM persistence_meta;";
                var count = Convert.ToInt32(
                    await existing.ExecuteScalarAsync(cancellationToken),
                    System.Globalization.CultureInfo.InvariantCulture);
                if (count != 0) throw new InvalidDataException("persistence.meta-already-initialized");
            }

            await using (var existingTransactions = _connection.CreateCommand())
            {
                existingTransactions.Transaction = transaction;
                existingTransactions.CommandText = "SELECT COUNT(*) FROM cross_domain_transaction_state;";
                var count = Convert.ToInt32(
                    await existingTransactions.ExecuteScalarAsync(cancellationToken),
                    System.Globalization.CultureInfo.InvariantCulture);
                if (count != 0)
                    throw new InvalidDataException("persistence.genesis-cross-domain-transaction-table-not-empty");
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
                if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidDataException("persistence.genesis-meta-insert-failed");
            }

            await using (var operational = _connection.CreateCommand())
            {
                operational.Transaction = transaction;
                operational.CommandText = """
INSERT INTO core_operational_state (singleton, master_generation, world_pause_state, pause_basis_step)
VALUES (1, $master_generation, 0, NULL);
""";
                operational.Parameters.AddWithValue("$master_generation", U64Be.Encode(seed.MasterGeneration));
                if (await operational.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidDataException("persistence.genesis-operational-state-insert-failed");
            }

            foreach (var item in orderedTransactions)
                await InsertGenesisCrossDomainTransactionAsync(item, transaction, cancellationToken).ConfigureAwait(false);

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private async Task InsertGenesisCrossDomainTransactionAsync(
        CrossDomainTransactionStateCommitV1 item,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var state = item.State;
        var digest = state.CanonicalDigest();
        RequireHash256(digest, "cross_domain_transaction_state.state_digest");

        await using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
INSERT INTO cross_domain_transaction_state (
  transaction_id, lifecycle, created_step, updated_step, terminal_step, state_wire, state_digest
) VALUES (
  $transaction_id, $lifecycle, $created_step, $updated_step, NULL, $state_wire, $state_digest
);
""";
        command.Parameters.AddWithValue("$transaction_id", state.TransactionId.ToBytes());
        command.Parameters.AddWithValue("$lifecycle", checked((int)state.Lifecycle));
        command.Parameters.AddWithValue("$created_step", U64Be.Encode(state.CreatedStep));
        command.Parameters.AddWithValue("$updated_step", U64Be.Encode(state.UpdatedStep));
        command.Parameters.AddWithValue("$state_wire", item.StateWire);
        command.Parameters.AddWithValue("$state_digest", digest);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidDataException("persistence.genesis-cross-domain-transaction-insert-failed");
    }
}
