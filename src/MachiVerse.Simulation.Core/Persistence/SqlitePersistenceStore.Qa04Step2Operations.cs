using System.Diagnostics;
using System.Text;
using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Runtime;
using Microsoft.Data.Sqlite;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed record Qa04ScheduledOperationBatchDurableResultV1(
    ulong InjectionStep,
    ulong EffectiveStep,
    ulong HistorySequence,
    bool Duplicate,
    IReadOnlyList<DurableOperationStateV1> ScheduledOperations);

/// <summary>
/// QA-04 Gate4 Step2 persistence authority for the closed perf.reference.v1 Operation set.
/// Generated Operations are admitted/scheduled as one compact durable batch per injection Step.
/// The complete logical Operation set is deterministically regenerable from perf.reference.v1 and
/// is bound by ScheduledBatchDigest, so the hot path does not expand 5,000 generated Operations into
/// transient operation_state/scheduled_operation rows. Ordinary/non-profile Operations continue to
/// use the standard per-Operation persistence APIs.
/// </summary>
public sealed partial class SqlitePersistenceStore
{
    private async Task EnsureQa04Step2OperationSchemaAsync(CancellationToken cancellationToken)
    {
        const string sql = """
CREATE TABLE IF NOT EXISTS qa04_operation_closed_prefix (
  singleton INTEGER PRIMARY KEY CHECK (singleton = 1),
  profile_id TEXT NOT NULL,
  first_injection_step BLOB NOT NULL CHECK (length(first_injection_step) = 8),
  last_closed_injection_step BLOB CHECK (last_closed_injection_step IS NULL OR length(last_closed_injection_step) = 8),
  terminal_operation_count BLOB NOT NULL CHECK (length(terminal_operation_count) = 8),
  terminal_semantic_digest BLOB NOT NULL CHECK (length(terminal_semantic_digest) = 32),
  updated_history_sequence BLOB CHECK (updated_history_sequence IS NULL OR length(updated_history_sequence) = 8)
);

CREATE TABLE IF NOT EXISTS qa04_operation_batch (
  injection_step BLOB PRIMARY KEY CHECK (length(injection_step) = 8),
  profile_id TEXT NOT NULL,
  effective_step BLOB NOT NULL CHECK (length(effective_step) = 8),
  operation_count BLOB NOT NULL CHECK (length(operation_count) = 8),
  scheduled_batch_digest BLOB NOT NULL CHECK (length(scheduled_batch_digest) = 32),
  scheduled_history_sequence BLOB NOT NULL UNIQUE CHECK (length(scheduled_history_sequence) = 8),
  terminal_history_sequence BLOB CHECK (terminal_history_sequence IS NULL OR length(terminal_history_sequence) = 8)
) WITHOUT ROWID;
""";
        await ExecuteNonQueryAsync(sql, cancellationToken).ConfigureAwait(false);
    }

    public async Task InitializeQa04OperationClosedPrefixAsync(
        Qa04OperationClosedPrefixV1 prefix,
        ulong stateStep,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        prefix.Validate(stateStep);

        using var transaction = _connection.BeginTransaction();
        try
        {
            var existing = await ReadQa04OperationClosedPrefixAsync(transaction, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                RequireSameQa04Prefix(existing, prefix, "persistence.qa04-operation-prefix-initialize-mismatch");
                transaction.Commit();
                return;
            }

            await using var command = _connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
INSERT INTO qa04_operation_closed_prefix (
  singleton, profile_id, first_injection_step, last_closed_injection_step,
  terminal_operation_count, terminal_semantic_digest, updated_history_sequence
) VALUES (1, $profile_id, $first_injection_step, $last_closed_injection_step,
          $terminal_operation_count, $terminal_semantic_digest, NULL);
""";
            command.Parameters.AddWithValue("$profile_id", prefix.ProfileId);
            command.Parameters.AddWithValue("$first_injection_step", U64Be.Encode(prefix.FirstInjectionStep));
            command.Parameters.AddWithValue(
                "$last_closed_injection_step",
                prefix.LastClosedInjectionStep is { } last ? U64Be.Encode(last) : DBNull.Value);
            command.Parameters.AddWithValue("$terminal_operation_count", U64Be.Encode(prefix.TerminalOperationCount));
            command.Parameters.AddWithValue("$terminal_semantic_digest", prefix.TerminalSemanticDigest);
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new InvalidDataException("persistence.qa04-operation-prefix-initialize-failed");
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public Task<Qa04OperationClosedPrefixV1?> ReadQa04OperationClosedPrefixAsync(
        CancellationToken cancellationToken = default)
        => ReadQa04OperationClosedPrefixAsync(transaction: null, cancellationToken);

    public Task<Qa04ScheduledOperationBatchDurableResultV1> PersistQa04ScheduledOperationBatchAsync(
        Qa04ScheduledOperationBatchAuthorityV1 authority,
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> bindings,
        CancellationToken cancellationToken = default)
        => PersistQa04ScheduledOperationBatchCoreAsync(
            authority,
            bindings,
            insertBatchSize: null,
            cancellationToken,
            authorityAlreadyValidated: false);

    public Task<Qa04ScheduledOperationBatchDurableResultV1> PersistQa04ScheduledOperationBatchBatchedAsync(
        Qa04ScheduledOperationBatchAuthorityV1 authority,
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> bindings,
        int insertBatchSize,
        CancellationToken cancellationToken = default)
    {
        if (insertBatchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(insertBatchSize));
        return PersistQa04ScheduledOperationBatchCoreAsync(
            authority,
            bindings,
            insertBatchSize,
            cancellationToken,
            authorityAlreadyValidated: false);
    }

    internal Task<Qa04ScheduledOperationBatchDurableResultV1> PersistQa04ValidatedScheduledOperationBatchAsync(
        Qa04ScheduledOperationBatchAuthorityV1 authority,
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> bindings,
        int? insertBatchSize,
        CancellationToken cancellationToken)
    {
        if (insertBatchSize is <= 0)
            throw new ArgumentOutOfRangeException(nameof(insertBatchSize));
        return PersistQa04ScheduledOperationBatchCoreAsync(
            authority,
            bindings,
            insertBatchSize,
            cancellationToken,
            authorityAlreadyValidated: true);
    }

    private async Task<Qa04ScheduledOperationBatchDurableResultV1> PersistQa04ScheduledOperationBatchCoreAsync(
        Qa04ScheduledOperationBatchAuthorityV1 authority,
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> bindings,
        int? insertBatchSize,
        CancellationToken cancellationToken,
        bool authorityAlreadyValidated)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(bindings);
        ValidateHistoryMaterial(authority.History, "qa04.operation-batch.scheduled.v1");
        if (authority.EffectiveStep != checked(authority.InjectionStep + 1UL))
            throw new InvalidDataException("persistence.qa04-operation-batch-effective-step-drift");
        if (authority.OperationCount != checked((ulong)bindings.Count) ||
            authority.OperationCount != Qa04ReferenceLoadV1.OperationCountForStep(authority.InjectionStep))
            throw new InvalidDataException("persistence.qa04-operation-batch-cardinality-drift");

        if (!authorityAlreadyValidated)
        {
            var expected = Qa04ScheduledOperationBatchAuthorityBuilderV1.Create(
                authority.History.WorldId,
                new HistoryAnchor(checked(authority.History.Sequence - 1UL), authority.History.PreviousRecordDigest),
                authority.InjectionStep,
                authority.EffectiveStep,
                bindings);
            if (!CryptographicOperations.FixedTimeEquals(expected.ScheduledBatchDigest, authority.ScheduledBatchDigest) ||
                !CryptographicOperations.FixedTimeEquals(expected.History.RecordDigest, authority.History.RecordDigest))
                throw new InvalidDataException("persistence.qa04-operation-batch-authority-drift");
        }

        using var transaction = _connection.BeginTransaction();
        try
        {
            var prefix = await ReadQa04OperationClosedPrefixAsync(transaction, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("persistence.qa04-operation-prefix-missing");
            prefix.Validate(authority.EffectiveStep);
            var expectedLastClosed = authority.InjectionStep == 0
                ? (ulong?)null
                : checked(authority.InjectionStep - 1UL);
            if (prefix.LastClosedInjectionStep != expectedLastClosed)
                throw new InvalidDataException("persistence.qa04-operation-batch-prefix-not-predecessor");

            var existing = await ReadQa04OperationBatchAsync(authority.InjectionStep, transaction, cancellationToken)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                RequireSameQa04BatchIdentity(existing, authority);
                if (existing.TerminalHistorySequence is not null)
                    throw new InvalidDataException("persistence.qa04-operation-batch-already-closed");
                var durableExisting = CreateQa04LogicalScheduledOperations(
                    bindings,
                    authority.EffectiveStep,
                    existing.ScheduledHistorySequence);
                transaction.Commit();
                return new Qa04ScheduledOperationBatchDurableResultV1(
                    authority.InjectionStep,
                    authority.EffectiveStep,
                    existing.ScheduledHistorySequence,
                    Duplicate: true,
                    durableExisting);
            }

            var context = await ReadHistoryContextAsync(transaction, cancellationToken).ConfigureAwait(false);
            ValidateNextHistoryRecord(authority.History, context);
            await InsertHistoryRecordAsync(authority.History, transaction, cancellationToken).ConfigureAwait(false);

            await using (var batch = _connection.CreateCommand())
            {
                batch.Transaction = transaction;
                batch.CommandText = """
INSERT INTO qa04_operation_batch (
  injection_step, profile_id, effective_step, operation_count,
  scheduled_batch_digest, scheduled_history_sequence, terminal_history_sequence
) VALUES ($injection_step, $profile_id, $effective_step, $operation_count,
          $scheduled_batch_digest, $scheduled_history_sequence, NULL);
""";
                batch.Parameters.AddWithValue("$injection_step", U64Be.Encode(authority.InjectionStep));
                batch.Parameters.AddWithValue("$profile_id", Qa04ReferenceLoadV1.BenchmarkProfileId);
                batch.Parameters.AddWithValue("$effective_step", U64Be.Encode(authority.EffectiveStep));
                batch.Parameters.AddWithValue("$operation_count", U64Be.Encode(authority.OperationCount));
                batch.Parameters.AddWithValue("$scheduled_batch_digest", authority.ScheduledBatchDigest);
                batch.Parameters.AddWithValue("$scheduled_history_sequence", U64Be.Encode(authority.History.Sequence));
                if (await batch.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                    throw new InvalidDataException("persistence.qa04-operation-batch-insert-failed");
            }

            // perf.reference.v1 generated Operations are represented durably by the batch row,
            // history record and ScheduledBatchDigest. Keep the complete logical Operation set
            // in memory for the current Step instead of expanding it into 10,000 transient rows.
            _ = insertBatchSize;

            await UpdateHistoryAnchorAsync(authority.History, transaction, cancellationToken).ConfigureAwait(false);
            var commitStarted = Stopwatch.GetTimestamp();
            transaction.Commit();
            ObserveSuccessfulCommit(Stopwatch.GetElapsedTime(commitStarted));

            var durable = CreateQa04LogicalScheduledOperations(
                bindings,
                authority.EffectiveStep,
                authority.History.Sequence);
            return new Qa04ScheduledOperationBatchDurableResultV1(
                authority.InjectionStep,
                authority.EffectiveStep,
                authority.History.Sequence,
                Duplicate: false,
                durable);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<DurableTransitionResult> PersistQa04CompactTransitionCommitAsync(
        ulong injectionStep,
        ulong effectiveStep,
        ulong resultingStep,
        byte[] resultingStateContinuityToken,
        ulong activeConfigGeneration,
        byte[] activeConfigDigest,
        HistoryRecordMaterial history,
        IReadOnlyCollection<TerminalOperationCommit> terminalOperations,
        Qa04OperationClosedPrefixV1 basisPrefix,
        Qa04OperationClosedPrefixV1 resultingPrefix,
        CancellationToken cancellationToken = default)
    {
        if (effectiveStep != checked(injectionStep + 1UL) || resultingStep != checked(effectiveStep + 1UL))
            throw new InvalidDataException("persistence.qa04-transition-step-drift");
        RequireHash256(resultingStateContinuityToken, nameof(resultingStateContinuityToken));
        RequireHash256(activeConfigDigest, nameof(activeConfigDigest));
        ValidateHistoryMaterial(history, "transition.committed.v1");
        ArgumentNullException.ThrowIfNull(terminalOperations);
        ArgumentNullException.ThrowIfNull(basisPrefix);
        ArgumentNullException.ThrowIfNull(resultingPrefix);
        basisPrefix.Validate(effectiveStep);
        resultingPrefix.Validate(resultingStep);
        if (resultingPrefix.LastClosedInjectionStep != injectionStep ||
            resultingPrefix.TerminalOperationCount != checked(basisPrefix.TerminalOperationCount + (ulong)terminalOperations.Count))
            throw new InvalidDataException("persistence.qa04-transition-result-prefix-drift");

        var terminalById = terminalOperations.ToDictionary(static value => value.OperationId);
        if (terminalById.Count != terminalOperations.Count)
            throw new InvalidDataException("persistence.qa04-transition-terminal-duplicate");
        foreach (var terminal in terminalOperations)
        {
            if (terminal.OperationId.IsZero)
                throw new InvalidDataException("persistence.qa04-transition-terminal-id-zero");
            _ = new StableToken(terminal.ResultCode);
        }

        using var transaction = _connection.BeginTransaction();
        try
        {
            var transitionHead = await ReadTransitionHeadAsync(transaction, cancellationToken).ConfigureAwait(false);
            if (transitionHead.FinalizedStep != effectiveStep)
                throw new InvalidDataException("persistence.qa04-transition-base-step-mismatch");

            var durablePrefix = await ReadQa04OperationClosedPrefixAsync(transaction, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("persistence.qa04-operation-prefix-missing");
            RequireSameQa04Prefix(durablePrefix, basisPrefix, "persistence.qa04-transition-basis-prefix-drift");

            var batch = await ReadQa04OperationBatchAsync(injectionStep, transaction, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("persistence.qa04-transition-batch-missing");
            if (batch.TerminalHistorySequence is not null ||
                batch.EffectiveStep != effectiveStep ||
                batch.OperationCount != checked((ulong)terminalOperations.Count))
                throw new InvalidDataException("persistence.qa04-transition-batch-drift");

            var regeneratedBindings = Qa04ReferenceLoadV1.OperationsForStep(injectionStep)
                .Select(descriptor => Qa04CanonicalOperationBindingV1.Bind(descriptor, activeConfigGeneration))
                .OrderBy(static binding => binding.OrderKey)
                .ThenBy(static binding => binding.SourceDescriptor.OperationId)
                .ToArray();
            if (regeneratedBindings.Length != terminalOperations.Count ||
                !CryptographicOperations.FixedTimeEquals(
                    batch.ScheduledBatchDigest,
                    Qa04ScheduledOperationBatchAuthorityBuilderV1.ComputeScheduledBatchDigest(regeneratedBindings)))
                throw new InvalidDataException("persistence.qa04-transition-scheduled-batch-digest-drift");
            foreach (var binding in regeneratedBindings)
            {
                if (!terminalById.ContainsKey(binding.SourceDescriptor.OperationId))
                    throw new InvalidDataException("persistence.qa04-transition-terminal-coverage-drift");
            }

            var context = await ReadHistoryContextAsync(transaction, cancellationToken).ConfigureAwait(false);
            ValidateNextHistoryRecord(history, context);
            var expectedContinuity = HistoryIntegrity.ComputeTransitionContinuityToken(
                context.WorldId,
                resultingStep,
                transitionHead.StateContinuityToken,
                history.RecordDigest);
            if (!CryptographicOperations.FixedTimeEquals(expectedContinuity, resultingStateContinuityToken))
                throw new InvalidDataException("persistence.qa04-transition-continuity-token-mismatch");
            await InsertHistoryRecordAsync(history, transaction, cancellationToken).ConfigureAwait(false);

            await using (var closeBatch = _connection.CreateCommand())
            {
                closeBatch.Transaction = transaction;
                closeBatch.CommandText = """
UPDATE qa04_operation_batch
SET terminal_history_sequence=$terminal_history_sequence
WHERE injection_step=$injection_step AND terminal_history_sequence IS NULL;
""";
                closeBatch.Parameters.AddWithValue("$terminal_history_sequence", U64Be.Encode(history.Sequence));
                closeBatch.Parameters.AddWithValue("$injection_step", U64Be.Encode(injectionStep));
                if (await closeBatch.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                    throw new InvalidDataException("persistence.qa04-transition-batch-close-failed");
            }

            await WriteQa04OperationClosedPrefixAsync(resultingPrefix, history.Sequence, transaction, cancellationToken)
                .ConfigureAwait(false);

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
                if (await meta.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                    throw new InvalidDataException("persistence.qa04-transition-meta-update-failed");
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

    private static IReadOnlyList<DurableOperationStateV1> CreateQa04LogicalScheduledOperations(
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> bindings,
        ulong effectiveStep,
        ulong scheduledHistorySequence)
    {
        if (bindings.Count == 0 || scheduledHistorySequence == 0)
            throw new InvalidDataException("persistence.qa04-operation-batch-logical-count-drift");

        var durable = new DurableOperationStateV1[bindings.Count];
        for (var index = 0; index < bindings.Count; index++)
        {
            var binding = bindings[index];
            if (binding.ScheduledOperation.EffectiveStep != effectiveStep ||
                binding.SourceDescriptor.OperationId.IsZero ||
                binding.BoundDescriptor.PayloadDigest.Length != 32)
                throw new InvalidDataException("persistence.qa04-operation-batch-logical-item-drift");

            durable[index] = new DurableOperationStateV1(
                binding.SourceDescriptor.OperationId,
                binding.BoundDescriptor.PayloadDigest.ToArray(),
                DurableOperationLifecycleV1.ScheduledDurable,
                scheduledHistorySequence,
                scheduledHistorySequence,
                effectiveStep,
                null,
                null,
                null,
                null);
        }
        return Array.AsReadOnly(durable);
    }

    private async Task InsertQa04ScheduledOperationsBatchedAsync(
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> bindings,
        Qa04ScheduledOperationBatchAuthorityV1 authority,
        int insertBatchSize,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        for (var offset = 0; offset < bindings.Count; offset = checked(offset + insertBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(insertBatchSize, bindings.Count - offset);

            await using (var operation = _connection.CreateCommand())
            {
                operation.Transaction = transaction;
                var sql = new StringBuilder(
                    "INSERT INTO operation_state (" +
                    "operation_id, payload_digest, lifecycle, accepted_sequence, scheduled_sequence, " +
                    "effective_step, terminal_sequence, terminal_status, result_code, rich_result_payload) VALUES ");
                operation.Parameters.AddWithValue("$lifecycle", ScheduledLifecycle);
                operation.Parameters.AddWithValue("$sequence", U64Be.Encode(authority.History.Sequence));
                operation.Parameters.AddWithValue("$effective_step", U64Be.Encode(authority.EffectiveStep));

                for (var localIndex = 0; localIndex < count; localIndex++)
                {
                    var binding = bindings[offset + localIndex];
                    var operationId = binding.SourceDescriptor.OperationId;
                    var payloadDigest = binding.BoundDescriptor.PayloadDigest;
                    var orderKey = binding.OrderKey.ToDatabaseBytes();
                    if (operationId.IsZero ||
                        payloadDigest.Length != 32 ||
                        orderKey.Length != SameStepOrderKey.DatabaseKeyLength)
                        throw new InvalidDataException("persistence.qa04-operation-batch-item-invalid");
                    if (binding.ScheduledOperation.EffectiveStep != authority.EffectiveStep)
                        throw new InvalidDataException("persistence.qa04-operation-batch-item-effective-step-drift");

                    if (localIndex > 0) sql.Append(',');
                    var idParameter = $"$operation_id_{localIndex}";
                    var payloadParameter = $"$payload_digest_{localIndex}";
                    sql.Append('(')
                        .Append(idParameter).Append(',')
                        .Append(payloadParameter).Append(',')
                        .Append("$lifecycle,$sequence,$sequence,$effective_step,NULL,NULL,NULL,NULL)");
                    operation.Parameters.AddWithValue(idParameter, operationId.ToBytes());
                    operation.Parameters.AddWithValue(payloadParameter, payloadDigest);
                }

                operation.CommandText = sql.ToString();
                if (await operation.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != count)
                    throw new InvalidDataException("persistence.qa04-operation-batch-item-insert-failed");
            }

            await using (var schedule = _connection.CreateCommand())
            {
                schedule.Transaction = transaction;
                var sql = new StringBuilder(
                    "INSERT INTO scheduled_operation (effective_step, order_key, operation_id) VALUES ");
                schedule.Parameters.AddWithValue("$effective_step", U64Be.Encode(authority.EffectiveStep));

                for (var localIndex = 0; localIndex < count; localIndex++)
                {
                    var binding = bindings[offset + localIndex];
                    var operationId = binding.SourceDescriptor.OperationId;
                    var orderKey = binding.OrderKey.ToDatabaseBytes();
                    if (localIndex > 0) sql.Append(',');
                    var orderKeyParameter = $"$order_key_{localIndex}";
                    var idParameter = $"$operation_id_{localIndex}";
                    sql.Append("($effective_step,")
                        .Append(orderKeyParameter).Append(',')
                        .Append(idParameter).Append(')');
                    schedule.Parameters.AddWithValue(orderKeyParameter, orderKey);
                    schedule.Parameters.AddWithValue(idParameter, operationId.ToBytes());
                }

                schedule.CommandText = sql.ToString();
                if (await schedule.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != count)
                    throw new InvalidDataException("persistence.qa04-operation-batch-schedule-insert-failed");
            }
        }
    }

    private async Task InsertQa04ScheduledOperationAsync(
        Qa04CanonicalOperationBindingResultV1 binding,
        Qa04ScheduledOperationBatchAuthorityV1 authority,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var operationId = binding.SourceDescriptor.OperationId;
        var payloadDigest = binding.BoundDescriptor.PayloadDigest;
        var orderKey = binding.OrderKey.ToDatabaseBytes();
        if (operationId.IsZero || payloadDigest.Length != 32 || orderKey.Length != SameStepOrderKey.DatabaseKeyLength)
            throw new InvalidDataException("persistence.qa04-operation-batch-item-invalid");
        if (binding.ScheduledOperation.EffectiveStep != authority.EffectiveStep)
            throw new InvalidDataException("persistence.qa04-operation-batch-item-effective-step-drift");

        await using (var operation = _connection.CreateCommand())
        {
            operation.Transaction = transaction;
            operation.CommandText = """
INSERT INTO operation_state (
  operation_id, payload_digest, lifecycle, accepted_sequence, scheduled_sequence,
  effective_step, terminal_sequence, terminal_status, result_code, rich_result_payload
) VALUES ($operation_id, $payload_digest, $lifecycle, $accepted_sequence, $scheduled_sequence,
          $effective_step, NULL, NULL, NULL, NULL);
""";
            operation.Parameters.AddWithValue("$operation_id", operationId.ToBytes());
            operation.Parameters.AddWithValue("$payload_digest", payloadDigest);
            operation.Parameters.AddWithValue("$lifecycle", ScheduledLifecycle);
            operation.Parameters.AddWithValue("$accepted_sequence", U64Be.Encode(authority.History.Sequence));
            operation.Parameters.AddWithValue("$scheduled_sequence", U64Be.Encode(authority.History.Sequence));
            operation.Parameters.AddWithValue("$effective_step", U64Be.Encode(authority.EffectiveStep));
            if (await operation.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new InvalidDataException("persistence.qa04-operation-batch-item-insert-failed");
        }

        await using var schedule = _connection.CreateCommand();
        schedule.Transaction = transaction;
        schedule.CommandText = """
INSERT INTO scheduled_operation (effective_step, order_key, operation_id)
VALUES ($effective_step, $order_key, $operation_id);
""";
        schedule.Parameters.AddWithValue("$effective_step", U64Be.Encode(authority.EffectiveStep));
        schedule.Parameters.AddWithValue("$order_key", orderKey);
        schedule.Parameters.AddWithValue("$operation_id", operationId.ToBytes());
        if (await schedule.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new InvalidDataException("persistence.qa04-operation-batch-schedule-insert-failed");
    }

    private async Task<Qa04OperationClosedPrefixV1?> ReadQa04OperationClosedPrefixAsync(
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
SELECT profile_id, first_injection_step, last_closed_injection_step,
       terminal_operation_count, terminal_semantic_digest
FROM qa04_operation_closed_prefix
WHERE singleton=1;
""";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        return new Qa04OperationClosedPrefixV1(
            reader.GetString(0),
            U64Be.Decode((byte[])reader[1]),
            reader.IsDBNull(2) ? null : U64Be.Decode((byte[])reader[2]),
            U64Be.Decode((byte[])reader[3]),
            ((byte[])reader[4]).ToArray());
    }

    private async Task WriteQa04OperationClosedPrefixAsync(
        Qa04OperationClosedPrefixV1 prefix,
        ulong historySequence,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
UPDATE qa04_operation_closed_prefix
SET profile_id=$profile_id,
    first_injection_step=$first_injection_step,
    last_closed_injection_step=$last_closed_injection_step,
    terminal_operation_count=$terminal_operation_count,
    terminal_semantic_digest=$terminal_semantic_digest,
    updated_history_sequence=$updated_history_sequence
WHERE singleton=1;
""";
        command.Parameters.AddWithValue("$profile_id", prefix.ProfileId);
        command.Parameters.AddWithValue("$first_injection_step", U64Be.Encode(prefix.FirstInjectionStep));
        command.Parameters.AddWithValue(
            "$last_closed_injection_step",
            prefix.LastClosedInjectionStep is { } last ? U64Be.Encode(last) : DBNull.Value);
        command.Parameters.AddWithValue("$terminal_operation_count", U64Be.Encode(prefix.TerminalOperationCount));
        command.Parameters.AddWithValue("$terminal_semantic_digest", prefix.TerminalSemanticDigest);
        command.Parameters.AddWithValue("$updated_history_sequence", U64Be.Encode(historySequence));
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new InvalidDataException("persistence.qa04-operation-prefix-update-failed");
    }

    private sealed record Qa04OperationBatchRowV1(
        ulong InjectionStep,
        string ProfileId,
        ulong EffectiveStep,
        ulong OperationCount,
        byte[] ScheduledBatchDigest,
        ulong ScheduledHistorySequence,
        ulong? TerminalHistorySequence);

    private async Task<Qa04OperationBatchRowV1?> ReadQa04OperationBatchAsync(
        ulong injectionStep,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
SELECT injection_step, profile_id, effective_step, operation_count,
       scheduled_batch_digest, scheduled_history_sequence, terminal_history_sequence
FROM qa04_operation_batch
WHERE injection_step=$injection_step;
""";
        command.Parameters.AddWithValue("$injection_step", U64Be.Encode(injectionStep));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        return new Qa04OperationBatchRowV1(
            U64Be.Decode((byte[])reader[0]),
            reader.GetString(1),
            U64Be.Decode((byte[])reader[2]),
            U64Be.Decode((byte[])reader[3]),
            ((byte[])reader[4]).ToArray(),
            U64Be.Decode((byte[])reader[5]),
            reader.IsDBNull(6) ? null : U64Be.Decode((byte[])reader[6]));
    }

    private sealed record Qa04ScheduledBatchRowV1(DurableOperationStateV1 State, byte[] OrderKey);

    private async Task<IReadOnlyList<Qa04ScheduledBatchRowV1>> ReadQa04ScheduledBatchOperationsAsync(
        ulong effectiveStep,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var rows = new List<Qa04ScheduledBatchRowV1>();
        await using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
SELECT o.operation_id, o.payload_digest, o.lifecycle,
       o.accepted_sequence, o.scheduled_sequence, o.effective_step,
       o.terminal_sequence, o.terminal_status, o.result_code, o.rich_result_payload,
       s.order_key
FROM scheduled_operation s
JOIN operation_state o ON o.operation_id=s.operation_id
WHERE s.effective_step=$effective_step
ORDER BY s.order_key ASC, s.operation_id ASC;
""";
        command.Parameters.AddWithValue("$effective_step", U64Be.Encode(effectiveStep));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new Qa04ScheduledBatchRowV1(
                new DurableOperationStateV1(
                    OpaqueId128.FromBytes((byte[])reader[0]),
                    ((byte[])reader[1]).ToArray(),
                    (DurableOperationLifecycleV1)reader.GetInt32(2),
                    reader.IsDBNull(3) ? null : U64Be.Decode((byte[])reader[3]),
                    reader.IsDBNull(4) ? null : U64Be.Decode((byte[])reader[4]),
                    reader.IsDBNull(5) ? null : U64Be.Decode((byte[])reader[5]),
                    reader.IsDBNull(6) ? null : U64Be.Decode((byte[])reader[6]),
                    reader.IsDBNull(7) ? null : reader.GetInt32(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8),
                    reader.IsDBNull(9) ? null : ((byte[])reader[9]).ToArray()),
                ((byte[])reader[10]).ToArray()));
        }
        return Array.AsReadOnly(rows.ToArray());
    }

    private static void RequireScheduledRowsMatchBindings(
        IReadOnlyList<Qa04ScheduledBatchRowV1> rows,
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> bindings,
        ulong historySequence,
        ulong effectiveStep)
    {
        if (rows.Count != bindings.Count)
            throw new InvalidDataException("persistence.qa04-operation-batch-duplicate-row-count-drift");
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var binding = bindings[index];
            if (row.State.OperationId != binding.SourceDescriptor.OperationId ||
                !CryptographicOperations.FixedTimeEquals(row.State.OperationPayloadDigest, binding.BoundDescriptor.PayloadDigest) ||
                row.State.Lifecycle != DurableOperationLifecycleV1.ScheduledDurable ||
                row.State.AcceptedSequence != historySequence ||
                row.State.ScheduledSequence != historySequence ||
                row.State.EffectiveStep != effectiveStep ||
                !row.OrderKey.AsSpan().SequenceEqual(binding.OrderKey.ToDatabaseBytes()))
                throw new InvalidDataException("persistence.qa04-operation-batch-duplicate-row-drift");
        }
    }

    private static void RequireSameQa04BatchIdentity(
        Qa04OperationBatchRowV1 existing,
        Qa04ScheduledOperationBatchAuthorityV1 expected)
    {
        if (existing.InjectionStep != expected.InjectionStep ||
            !string.Equals(existing.ProfileId, Qa04ReferenceLoadV1.BenchmarkProfileId, StringComparison.Ordinal) ||
            existing.EffectiveStep != expected.EffectiveStep ||
            existing.OperationCount != expected.OperationCount ||
            !CryptographicOperations.FixedTimeEquals(existing.ScheduledBatchDigest, expected.ScheduledBatchDigest))
            throw new InvalidDataException("persistence.qa04-operation-batch-idempotency-mismatch");
    }

    private static void RequireSameQa04Prefix(
        Qa04OperationClosedPrefixV1 actual,
        Qa04OperationClosedPrefixV1 expected,
        string error)
    {
        if (!string.Equals(actual.ProfileId, expected.ProfileId, StringComparison.Ordinal) ||
            actual.FirstInjectionStep != expected.FirstInjectionStep ||
            actual.LastClosedInjectionStep != expected.LastClosedInjectionStep ||
            actual.TerminalOperationCount != expected.TerminalOperationCount ||
            !CryptographicOperations.FixedTimeEquals(actual.TerminalSemanticDigest, expected.TerminalSemanticDigest))
            throw new InvalidDataException(error);
    }
}