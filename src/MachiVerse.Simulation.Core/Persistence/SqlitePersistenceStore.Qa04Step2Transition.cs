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
    public Task<DurableTransitionResult> PersistQa04CanonicalTransitionCommitAsync(
        ulong injectionStep,
        Qa04TransitionCommittedAuthorityV1 authority,
        IReadOnlyCollection<TerminalOperationCommit> terminalOperations,
        Qa04OperationClosedPrefixV1 basisPrefix,
        Qa04OperationClosedPrefixV1 resultingPrefix,
        IReadOnlyCollection<CrossDomainTransactionStateV1> crossDomainTransactionStateChanges,
        CancellationToken cancellationToken = default,
        Qa04DetailDecisionAuthorityV1? detailDecisionAuthority = null,
        byte[]? expectedScheduledBatchDigest = null,
        Action<string, double>? diagnosticPhaseObserver = null,
        bool terminalOperationsAreCanonical = false)
        => PersistQa04CanonicalTransitionCommitCoreAsync(
            injectionStep,
            authority,
            terminalOperations,
            basisPrefix,
            resultingPrefix,
            crossDomainTransactionStateChanges,
            cancellationToken,
            detailDecisionAuthority,
            expectedScheduledBatchDigest,
            diagnosticPhaseObserver,
            terminalOperationsAreCanonical,
            authorityIntegrityAlreadyValidated: false);

    internal Task<DurableTransitionResult> PersistQa04ValidatedCanonicalTransitionCommitAsync(
        ulong injectionStep,
        Qa04TransitionCommittedAuthorityV1 authority,
        IReadOnlyList<TerminalOperationCommit> terminalOperations,
        Qa04OperationClosedPrefixV1 basisPrefix,
        Qa04OperationClosedPrefixV1 resultingPrefix,
        IReadOnlyCollection<CrossDomainTransactionStateV1> crossDomainTransactionStateChanges,
        CancellationToken cancellationToken,
        Qa04DetailDecisionAuthorityV1? detailDecisionAuthority,
        byte[] expectedScheduledBatchDigest,
        Action<string, double>? diagnosticPhaseObserver)
    {
        ArgumentNullException.ThrowIfNull(terminalOperations);
        if (!ReferenceEquals(terminalOperations, authority.OperationOutcomes))
            throw new InvalidDataException("persistence.qa04-canonical-transition.validated-terminal-reference-drift");

        return PersistQa04CanonicalTransitionCommitCoreAsync(
            injectionStep,
            authority,
            terminalOperations,
            basisPrefix,
            resultingPrefix,
            crossDomainTransactionStateChanges,
            cancellationToken,
            detailDecisionAuthority,
            expectedScheduledBatchDigest,
            diagnosticPhaseObserver,
            terminalOperationsAreCanonical: true,
            authorityIntegrityAlreadyValidated: true);
    }

    private async Task<DurableTransitionResult> PersistQa04CanonicalTransitionCommitCoreAsync(
        ulong injectionStep,
        Qa04TransitionCommittedAuthorityV1 authority,
        IReadOnlyCollection<TerminalOperationCommit> terminalOperations,
        Qa04OperationClosedPrefixV1 basisPrefix,
        Qa04OperationClosedPrefixV1 resultingPrefix,
        IReadOnlyCollection<CrossDomainTransactionStateV1> crossDomainTransactionStateChanges,
        CancellationToken cancellationToken,
        Qa04DetailDecisionAuthorityV1? detailDecisionAuthority,
        byte[]? expectedScheduledBatchDigest,
        Action<string, double>? diagnosticPhaseObserver,
        bool terminalOperationsAreCanonical,
        bool authorityIntegrityAlreadyValidated)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(terminalOperations);
        ArgumentNullException.ThrowIfNull(basisPrefix);
        ArgumentNullException.ThrowIfNull(resultingPrefix);
        ArgumentNullException.ThrowIfNull(crossDomainTransactionStateChanges);
        if (expectedScheduledBatchDigest is not null)
            RequireHash256(expectedScheduledBatchDigest, nameof(expectedScheduledBatchDigest));

        var diagnosticPhaseStarted = Stopwatch.GetTimestamp();
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

        IReadOnlyList<TerminalOperationCommit>? canonicalTerminalOperations = null;
        Dictionary<OpaqueId128, TerminalOperationCommit>? terminalById = null;
        var terminalOperationsReferenceAuthority = false;
        if (terminalOperationsAreCanonical)
        {
            canonicalTerminalOperations = terminalOperations as IReadOnlyList<TerminalOperationCommit>
                ?? throw new InvalidDataException("persistence.qa04-canonical-transition.terminal-canonical-list-required");
            terminalOperationsReferenceAuthority =
                ReferenceEquals(canonicalTerminalOperations, authority.OperationOutcomes);
            if (!terminalOperationsReferenceAuthority)
            {
                foreach (var terminal in canonicalTerminalOperations)
                {
                    if (terminal.OperationId.IsZero)
                        throw new InvalidDataException("persistence.qa04-canonical-transition.terminal-id-zero");
                    _ = new StableToken(terminal.ResultCode);
                }
            }
        }
        else
        {
            terminalById = terminalOperations.ToDictionary(static value => value.OperationId);
            if (terminalById.Count != terminalOperations.Count)
                throw new InvalidDataException("persistence.qa04-canonical-transition.terminal-duplicate");
            foreach (var terminal in terminalOperations)
            {
                if (terminal.OperationId.IsZero)
                    throw new InvalidDataException("persistence.qa04-canonical-transition.terminal-id-zero");
                _ = new StableToken(terminal.ResultCode);
            }
        }

        var transactionChanges = crossDomainTransactionStateChanges
            .OrderBy(static state => state.TransactionId)
            .ToArray();
        if (transactionChanges.Select(static state => state.TransactionId).Distinct().Count() != transactionChanges.Length ||
            transactionChanges.Any(state => state.UpdatedStep != resultingStep))
            throw new InvalidDataException("persistence.qa04-canonical-transition.transaction-change-shape-drift");
        ObserveQa04TransitionCommitPhase(diagnosticPhaseObserver, "persist-preflight", ref diagnosticPhaseStarted);

        using var transaction = _connection.BeginTransaction();
        try
        {
            if (!authorityIntegrityAlreadyValidated)
                Qa04TransitionCommittedAuthorityV1.RequireMaterializedAuthorityIntegrity(authority);
            var decoded = authority;
            ObserveQa04TransitionCommitPhase(diagnosticPhaseObserver, "persist-validate-authority", ref diagnosticPhaseStarted);

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
            ObserveQa04TransitionCommitPhase(diagnosticPhaseObserver, "persist-read-authority-heads", ref diagnosticPhaseStarted);

            var scheduledBatchDigest = expectedScheduledBatchDigest;
            if (scheduledBatchDigest is null)
            {
                var regeneratedBindings = Qa04ReferenceLoadV1.OperationsForStep(injectionStep)
                    .Select(descriptor => Qa04CanonicalOperationBindingV1.Bind(
                        descriptor,
                        authority.ActiveConfigGeneration))
                    .OrderBy(static binding => binding.OrderKey)
                    .ThenBy(static binding => binding.SourceDescriptor.OperationId)
                    .ToArray();
                if (regeneratedBindings.Length != decoded.AppliedOperationIds.Count)
                    throw new InvalidDataException("persistence.qa04-canonical-transition.scheduled-count-drift");
                for (var index = 0; index < regeneratedBindings.Length; index++)
                {
                    if (regeneratedBindings[index].SourceDescriptor.OperationId != decoded.AppliedOperationIds[index])
                        throw new InvalidDataException("persistence.qa04-canonical-transition.operation-outcome-drift");
                }
                scheduledBatchDigest =
                    Qa04ScheduledOperationBatchAuthorityBuilderV1.ComputeScheduledBatchDigest(regeneratedBindings);
            }

            if (!CryptographicOperations.FixedTimeEquals(batch.ScheduledBatchDigest, scheduledBatchDigest))
                throw new InvalidDataException("persistence.qa04-canonical-transition.scheduled-batch-digest-drift");

            if (!terminalOperationsReferenceAuthority)
            {
                for (var index = 0; index < decoded.AppliedOperationIds.Count; index++)
                {
                    var operationId = decoded.AppliedOperationIds[index];
                    var outcome = decoded.OperationOutcomes[index];
                    TerminalOperationCommit terminal;
                    if (canonicalTerminalOperations is not null)
                    {
                        terminal = canonicalTerminalOperations[index];
                        if (terminal.OperationId != operationId)
                            throw new InvalidDataException("persistence.qa04-canonical-transition.operation-outcome-drift");
                    }
                    else
                    {
                        if (terminalById is null ||
                            !terminalById.TryGetValue(operationId, out var mappedTerminal))
                        {
                            throw new InvalidDataException("persistence.qa04-canonical-transition.operation-outcome-drift");
                        }
                        terminal = mappedTerminal;
                    }

                    if (!Qa04TransitionCommittedAuthorityV1.TerminalEquals(outcome, terminal))
                        throw new InvalidDataException("persistence.qa04-canonical-transition.operation-outcome-drift");
                }
            }
            ObserveQa04TransitionCommitPhase(diagnosticPhaseObserver, "persist-operation-coverage", ref diagnosticPhaseStarted);

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
            ObserveQa04TransitionCommitPhase(diagnosticPhaseObserver, "persist-history", ref diagnosticPhaseStarted);

            foreach (var state in transactionChanges)
            {
                await UpsertCrossDomainTransactionStateInTransitionAsync(
                        CrossDomainTransactionStateCommitV1.CreateCanonical(state),
                        transaction,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            ObserveQa04TransitionCommitPhase(diagnosticPhaseObserver, "persist-crossdomain", ref diagnosticPhaseStarted);

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
            ObserveQa04TransitionCommitPhase(diagnosticPhaseObserver, "persist-batch-close", ref diagnosticPhaseStarted);

            await WriteQa04OperationClosedPrefixAsync(
                    resultingPrefix,
                    authority.History.Sequence,
                    transaction,
                    cancellationToken)
                .ConfigureAwait(false);
            ObserveQa04TransitionCommitPhase(diagnosticPhaseObserver, "persist-prefix", ref diagnosticPhaseStarted);

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
            ObserveQa04TransitionCommitPhase(diagnosticPhaseObserver, "persist-meta", ref diagnosticPhaseStarted);

            var commitStarted = Stopwatch.GetTimestamp();
            transaction.Commit();
            ObserveSuccessfulCommit(Stopwatch.GetElapsedTime(commitStarted));
            ObserveQa04TransitionCommitPhase(diagnosticPhaseObserver, "persist-sqlite-commit", ref diagnosticPhaseStarted);
            return new DurableTransitionResult(resultingStep, authority.History.Sequence);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static void ObserveQa04TransitionCommitPhase(
        Action<string, double>? observer,
        string phase,
        ref long startedTimestamp)
    {
        if (observer is null)
            return;

        var now = Stopwatch.GetTimestamp();
        observer(phase, Stopwatch.GetElapsedTime(startedTimestamp, now).TotalMilliseconds);
        startedTimestamp = now;
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
