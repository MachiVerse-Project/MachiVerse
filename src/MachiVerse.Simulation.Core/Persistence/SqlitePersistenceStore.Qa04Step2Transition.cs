using System.Diagnostics;
using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Runtime;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed partial class SqlitePersistenceStore
{
    /// <summary>
    /// Gate4 Step2 transition COMMIT path. Unlike the legacy compact transition seam, this path
    /// strictly decodes the stored physical transition wrapper and cross-checks the reconstructed
    /// semantic authority against the exact finalization material before any authoritative mutation.
    /// </summary>
    public async Task<DurableTransitionResult> PersistQa04CanonicalTransitionCommitAsync(
        ulong injectionStep,
        Qa04TransitionCommittedAuthorityV1 authority,
        IReadOnlyCollection<TerminalOperationCommit> terminalOperations,
        Qa04OperationClosedPrefixV1 basisPrefix,
        Qa04OperationClosedPrefixV1 resultingPrefix,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(terminalOperations);
        ArgumentNullException.ThrowIfNull(basisPrefix);
        ArgumentNullException.ThrowIfNull(resultingPrefix);
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

        var terminalById = terminalOperations.ToDictionary(static value => value.OperationId);
        if (terminalById.Count != terminalOperations.Count)
            throw new InvalidDataException("persistence.qa04-canonical-transition.terminal-duplicate");
        foreach (var terminal in terminalOperations)
        {
            if (terminal.OperationId.IsZero)
                throw new InvalidDataException("persistence.qa04-canonical-transition.terminal-id-zero");
            _ = new StableToken(terminal.ResultCode);
        }

        using var transaction = _connection.BeginTransaction();
        try
        {
            // Persistence deliberately decodes the physical payload here rather than trusting the
            // in-memory builder object. This is the required semantic cross-check boundary.
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

            var scheduled = await ReadQa04ScheduledBatchOperationsAsync(effectiveStep, transaction, cancellationToken)
                .ConfigureAwait(false);
            if (scheduled.Count != decoded.AppliedOperationIds.Count)
                throw new InvalidDataException("persistence.qa04-canonical-transition.scheduled-count-drift");
            for (var index = 0; index < scheduled.Count; index++)
            {
                var row = scheduled[index];
                var operationId = decoded.AppliedOperationIds[index];
                var outcome = decoded.OperationOutcomes[index];
                if (row.State.OperationId != operationId ||
                    row.State.Lifecycle != DurableOperationLifecycleV1.ScheduledDurable ||
                    row.State.EffectiveStep != effectiveStep ||
                    !terminalById.TryGetValue(operationId, out var terminal) ||
                    !Qa04TransitionCommittedAuthorityV1.TerminalEquals(outcome, terminal))
                    throw new InvalidDataException("persistence.qa04-canonical-transition.operation-outcome-drift");
            }

            var context = await ReadHistoryContextAsync(transaction, cancellationToken).ConfigureAwait(false);
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

            // At this point the decoded state diagnostic / partition digests have already been
            // checked byte-for-byte against the builder authority captured from Prepared State(S+1).
            // Only now may the transition history and closed-prefix mutation become durable.
            await InsertHistoryRecordAsync(authority.History, transaction, cancellationToken).ConfigureAwait(false);

            await using (var removeSchedule = _connection.CreateCommand())
            {
                removeSchedule.Transaction = transaction;
                removeSchedule.CommandText = "DELETE FROM scheduled_operation WHERE effective_step=$effective_step;";
                removeSchedule.Parameters.AddWithValue("$effective_step", U64Be.Encode(effectiveStep));
                if (await removeSchedule.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != scheduled.Count)
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
                if (await removeOperation.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != scheduled.Count)
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
}
