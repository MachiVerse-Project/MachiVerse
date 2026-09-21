using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Runtime;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed partial class SqlitePersistenceStore
{
    public Task<DurableTransitionResult> PersistQa04CanonicalTransitionCommitAsync(
        ulong injectionStep,
        Qa04TransitionCommittedAuthorityV1 authority,
        IReadOnlyCollection<TerminalOperationCommit> terminalOperations,
        Qa04OperationClosedPrefixV1 basisPrefix,
        Qa04OperationClosedPrefixV1 resultingPrefix,
        CancellationToken cancellationToken = default)
        => PersistQa04CanonicalTransitionCommitAsync(
            injectionStep,
            authority,
            terminalOperations,
            basisPrefix,
            resultingPrefix,
            Array.Empty<CrossDomainTransactionStateV1>(),
            cancellationToken,
            detailDecisionAuthority: null);

    /// <summary>
    /// Gate4 Step2 transition COMMIT path. The compact Operation transition, complete
    /// transition.committed.v1 semantic authority, optional canonical Detail decision authority,
    /// and any canonical CrossDomainTransaction turnover changes become durable in the same
    /// SQLite transaction. When supplied, the Detail decision record is appended immediately
    /// before transition.committed.v1 and the transition record must bind it as its predecessor.
    /// </summary>
    public async Task<DurableTransitionResult> PersistQa04CanonicalTransitionCommitAsync(
        ulong injectionStep,
        Qa04TransitionCommittedAuthorityV1 authority,
        IReadOnlyCollection<TerminalOperationCommit> terminalOperations,
        Qa04OperationClosedPrefixV1 basisPrefix,
        Qa04OperationClosedPrefixV1 resultingPrefix,
        IReadOnlyCollection<CrossDomainTransactionStateV1> crossDomainTransactionStateChanges,
        CancellationToken cancellationToken = default,
        Qa04DetailDecisionAuthorityV1? detailDecisionAuthority = null)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(terminalOperations);
        ArgumentNullException.ThrowIfNull(basisPrefix);
        ArgumentNullException.ThrowIfNull(resultingPrefix);
        ArgumentNullException.ThrowIfNull(crossDomainTransactionStateChanges);
        var effectiveStep = authority.EffectiveStep;
        var resultingStep = authority.ResultingStep;
        if (effectiveStep != checked(injectionStep + 1UL) || resultingStep != checked(effectiveStep + 1UL))
            throw new InvalidDataException("persistence.qa04-canonical-transition.step-drift");
        ValidateHistoryMaterial(authority.History, "transition.committed.v1");
        RequireHash256(authority.ActiveConfigDigest, nameof(authority.ActiveConfigDigest));
        RequireHash256(authority.PreviousStateContinuityToken, nameof(authority.PreviousStateContinuityToken));
        RequireHash256(authority.ResultingStateContinuityToken, nameof(authority.ResultingStateContinuityToken));
        RequireHash256(authority.StateDiagnosticHash, nameof(authority.StateDiagnosticHash));
        basisPrefix.Validate(effectiveStep);
        resultingPrefix.Validate(resultingStep);
        if (resultingPrefix.LastClosedInjectionStep != injectionStep ||
            resultingPrefix.TerminalOperationCount != checked(basisPrefix.TerminalOperationCount + (ulong)terminalOperations.Count))
            throw new InvalidDataException("persistence.qa04-canonical-transition.result-prefix-drift");

        if (detailDecisionAuthority is not null)
        {
            var detail = detailDecisionAuthority;
            ValidateHistoryMaterial(detail.History, "qa04.detail-promotion-decision.v1");
            if (detail.BasisStep != effectiveStep || detail.ResultingStep != resultingStep ||
                detail.History.WorldId != authority.History.WorldId ||
                detail.History.Sequence == ulong.MaxValue ||
                checked(detail.History.Sequence + 1UL) != authority.History.Sequence ||
                !CryptographicOperations.FixedTimeEquals(
                    authority.History.PreviousRecordDigest,
                    detail.History.RecordDigest))
                throw new InvalidDataException("persistence.qa04-canonical-transition.detail-decision-chain-drift");
        }

        var terminalById = terminalOperations.ToDictionary(static value => value.OperationId);
        if (terminalById.Count != terminalOperations.Count)
            throw new InvalidDataException("persistence.qa04-canonical-transition.terminal-duplicate");
        foreach (var terminal in terminalOperations)
        {
            if (terminal.OperationId.IsZero)
                throw new InvalidDataException("persistence.qa04-canonical-transition.terminal-id-zero");
            _ = new StableToken(terminal.ResultCode);
        }

        var transactionChanges = crossDomainTransactionStateChanges
            .OrderBy(static state => state.TransactionId)
            .ToArray();
        if (transactionChanges.Select(static state => state.TransactionId).Distinct().Count() != transactionChanges.Length ||
            transactionChanges.Any(state => state.UpdatedStep != resultingStep))
            throw new InvalidDataException("persistence.qa04-canonical-transition.transaction-change-shape-drift");

        using var transaction = _connection.BeginTransaction();
        try
        {
            var decoded = Qa04TransitionCommittedAuthorityV1.DecodeAndValidate(authority.History);
            Qa04TransitionCommittedAuthorityV1.RequireEquivalent(
                decoded,
                authority,
                "persistence.qa04-canonical-transition.decoded-authority-drift");

            var transitionHead = await ReadTransitionHeadAsync(transaction, cancellationToken).ConfigureAwait(false);
            if (transitionHead.FinalizedStep != effectiveStep)
                throw new InvalidDataException("persistence.qa04-canonical-transition.base-step-mismatch");
            if (!CryptographicOperations.FixedTimeEquals(
                    transitionHead.StateContinuityToken,
                    decoded.PreviousStateContinuityToken))
                throw new InvalidDataException("persistence.qa04-canonical-transition.previous-continuity-drift");

            await using (var configHead = _connection.CreateCommand())
            {
                configHead.Transaction = transaction;
                configHead.CommandText = "SELECT config_generation, config_digest FROM persistence_meta WHERE singleton=1;";
                await using var reader = await configHead.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    throw new InvalidDataException("persistence.qa04-canonical-transition.meta-missing");
                var generation = U64Be.Decode((byte[])reader[0]);
                var digest = (byte[])reader[1];
                RequireHash256(digest, "persistence.qa04-canonical-transition.config-digest");
                if (generation != decoded.ActiveConfigGeneration ||
                    !CryptographicOperations.FixedTimeEquals(digest, decoded.ActiveConfigDigest))
                    throw new InvalidDataException("persistence.qa04-canonical-transition.config-authority-drift");
            }

            var durablePrefix = await ReadQa04OperationClosedPrefixAsync(transaction, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("persistence.qa04-canonical-transition.prefix-missing");
            RequireSameQa04Prefix(
                durablePrefix,
                basisPrefix,
                "persistence.qa04-canonical-transition.basis-prefix-drift");

            var batch = await ReadQa04OperationBatchAsync(injectionStep, transaction, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("persistence.qa04-canonical-transition.batch-missing");
            if (batch.TerminalHistorySequence is not null ||
                batch.EffectiveStep != effectiveStep ||
                batch.OperationCount != checked((ulong)decoded.AppliedOperationIds.Count) ||
                batch.OperationCount != checked((ulong)terminalOperations.Count))
                throw new InvalidDataException("persistence.qa04-canonical-transition.batch-drift");

            var scheduledCount = await RequireQa04CanonicalScheduledCoverageAsync(
                    effectiveStep,
                    decoded.AppliedOperationIds,
                    decoded.OperationOutcomes,
                    terminalById,
                    transaction,
                    cancellationToken)
                .ConfigureAwait(false);

            var context = await ReadHistoryContextAsync(transaction, cancellationToken).ConfigureAwait(false);
            if (detailDecisionAuthority is not null)
            {
                ValidateNextHistoryRecord(detailDecisionAuthority.History, context);
                await InsertHistoryRecordAsync(
                        detailDecisionAuthority.History,
                        transaction,
                        cancellationToken)
                    .ConfigureAwait(false);
                context = new HistoryContext(
                    context.WorldId,
                    new HistoryAnchor(
                        detailDecisionAuthority.History.Sequence,
                        detailDecisionAuthority.History.RecordDigest));
            }

            ValidateNextHistoryRecord(authority.History, context);
            var expectedContinuity = HistoryIntegrity.ComputeTransitionContinuityToken(
                context.WorldId,
                resultingStep,
                transitionHead.StateContinuityToken,
                authority.History.RecordDigest);
            if (!CryptographicOperations.FixedTimeEquals(
                    expectedContinuity,
                    decoded.ResultingStateContinuityToken) ||
                !CryptographicOperations.FixedTimeEquals(
                    expectedContinuity,
                    authority.ResultingStateContinuityToken))
                throw new InvalidDataException("persistence.qa04-canonical-transition.resulting-continuity-drift");

            await InsertHistoryRecordAsync(authority.History, transaction, cancellationToken).ConfigureAwait(false);

            foreach (var state in transactionChanges)
            {
                await UpsertCrossDomainTransactionStateInTransitionAsync(
                        CrossDomainTransactionStateCommitV1.CreateCanonical(state),
                        transaction,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            await using (var removeSchedule = _connection.CreateCommand())
            {
                removeSchedule.Transaction = transaction;
                removeSchedule.CommandText = "DELETE FROM scheduled_operation WHERE effective_step=$effective_step;";
                removeSchedule.Parameters.AddWithValue("$effective_step", U64Be.Encode(effectiveStep));
                if (await removeSchedule.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != scheduledCount)
                    throw new InvalidDataException("persistence.qa04-canonical-transition.scheduled-delete-count-drift");
            }

            await using (var removeOperation = _connection.CreateCommand())
            {
                removeOperation.Transaction = transaction;
                removeOperation.CommandText = """
DELETE FROM operation_state
WHERE lifecycle=$scheduled_lifecycle AND effective_step=$effective_step;
""";
                removeOperation.Parameters.AddWithValue("$scheduled_lifecycle", ScheduledLifecycle);
                removeOperation.Parameters.AddWithValue("$effective_step", U64Be.Encode(effectiveStep));
                if (await removeOperation.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != scheduledCount)
                    throw new InvalidDataException("persistence.qa04-canonical-transition.operation-delete-count-drift");
            }

            await using (var closeBatch = _connection.CreateCommand())
            {
                closeBatch.Transaction = transaction;
                closeBatch.CommandText = """
UPDATE qa04_operation_batch
SET terminal_history_sequence=$terminal_history_sequence
WHERE injection_step=$injection_step AND terminal_history_sequence IS NULL;
""";
                closeBatch.Parameters.AddWithValue("$terminal_history_sequence", U64Be.Encode(authority.History.Sequence));
                closeBatch.Parameters.AddWithValue("$injection_step", U64Be.Encode(injectionStep));
                if (await closeBatch.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                    throw new InvalidDataException("persistence.qa04-canonical-transition.batch-close-failed");
            }

            await WriteQa04OperationClosedPrefixAsync(
                    resultingPrefix,
                    authority.History.Sequence,
                    transaction,
                    cancellationToken)
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
                meta.Parameters.AddWithValue("$history_sequence", U64Be.Encode(authority.History.Sequence));
                meta.Parameters.AddWithValue("$history_digest", authority.History.RecordDigest);
                meta.Parameters.AddWithValue("$finalized_step", U64Be.Encode(resultingStep));
                meta.Parameters.AddWithValue("$continuity_token", authority.ResultingStateContinuityToken);
                meta.Parameters.AddWithValue("$config_generation", U64Be.Encode(authority.ActiveConfigGeneration));
                meta.Parameters.AddWithValue("$config_digest", authority.ActiveConfigDigest);
                if (await meta.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                    throw new InvalidDataException("persistence.qa04-canonical-transition.meta-update-failed");
            }

            var commitStarted = Stopwatch.GetTimestamp();
            transaction.Commit();
            ObserveSuccessfulCommit(Stopwatch.GetElapsedTime(commitStarted));
            return new DurableTransitionResult(resultingStep, authority.History.Sequence);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private async Task<int> RequireQa04CanonicalScheduledCoverageAsync(
        ulong effectiveStep,
        IReadOnlyList<OpaqueId128> appliedOperationIds,
        IReadOnlyList<TerminalOperationCommit> decodedOutcomes,
        IReadOnlyDictionary<OpaqueId128, TerminalOperationCommit> terminalById,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (appliedOperationIds.Count != decodedOutcomes.Count ||
            appliedOperationIds.Count != terminalById.Count)
            throw new InvalidDataException("persistence.qa04-canonical-transition.scheduled-count-drift");

        await using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
SELECT o.operation_id, o.lifecycle, o.effective_step
FROM scheduled_operation s
JOIN operation_state o ON o.operation_id=s.operation_id
WHERE s.effective_step=$effective_step
ORDER BY s.order_key ASC, s.operation_id ASC;
""";
        command.Parameters.AddWithValue("$effective_step", U64Be.Encode(effectiveStep));

        var index = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (index >= appliedOperationIds.Count)
                throw new InvalidDataException("persistence.qa04-canonical-transition.scheduled-count-drift");

            var operationId = OpaqueId128.FromBytes((byte[])reader[0]);
            var lifecycle = (DurableOperationLifecycleV1)reader.GetInt32(1);
            var durableEffectiveStep = reader.IsDBNull(2)
                ? (ulong?)null
                : U64Be.Decode((byte[])reader[2]);
            var expectedOperationId = appliedOperationIds[index];
            var outcome = decodedOutcomes[index];

            if (operationId != expectedOperationId ||
                lifecycle != DurableOperationLifecycleV1.ScheduledDurable ||
                durableEffectiveStep != effectiveStep ||
                !terminalById.TryGetValue(expectedOperationId, out var terminal) ||
                !Qa04TransitionCommittedAuthorityV1.TerminalEquals(outcome, terminal))
                throw new InvalidDataException("persistence.qa04-canonical-transition.operation-outcome-drift");

            index++;
        }

        if (index != appliedOperationIds.Count)
            throw new InvalidDataException("persistence.qa04-canonical-transition.scheduled-count-drift");
        return index;
    }

    public async IAsyncEnumerable<Qa04TransitionCommittedAuthorityV1> StreamQa04CanonicalTransitionHistoryAfterAsync(
        ulong historyAnchorSequence,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        OpaqueId128 worldId;
        await using (var meta = _connection.CreateCommand())
        {
            meta.CommandText = "SELECT world_id FROM persistence_meta WHERE singleton=1;";
            var raw = await meta.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("persistence.qa04-transition-replay.meta-missing");
            worldId = OpaqueId128.FromBytes((byte[])raw);
        }

        await using var command = _connection.CreateCommand();
        command.CommandText = """
SELECT sequence, previous_record_digest, record_type, payload_schema_id,
       payload_schema_major, payload_schema_minor, payload_bytes,
       normalized_payload_digest, record_digest
FROM history_record
WHERE sequence > $anchor_sequence
  AND record_type = 'transition.committed.v1'
ORDER BY sequence ASC;
""";
        command.Parameters.AddWithValue("$anchor_sequence", U64Be.Encode(historyAnchorSequence));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var sequence = U64Be.Decode((byte[])reader[0]);
            var previousRecordDigest = (byte[])reader[1];
            var recordType = reader.GetString(2);
            var payloadSchemaId = reader.GetString(3);
            var majorRaw = reader.GetInt32(4);
            var minorRaw = reader.GetInt32(5);
            if (majorRaw is < 0 or > ushort.MaxValue || minorRaw is < 0 or > ushort.MaxValue)
                throw new InvalidDataException("persistence.qa04-transition-replay.schema-version-invalid");

            yield return Qa04TransitionCommittedAuthorityV1.RestorePersistedAndValidate(
                worldId,
                sequence,
                previousRecordDigest,
                recordType,
                payloadSchemaId,
                checked((ushort)majorRaw),
                checked((ushort)minorRaw),
                (byte[])reader[6],
                (byte[])reader[7],
                (byte[])reader[8]);
        }
    }

}
