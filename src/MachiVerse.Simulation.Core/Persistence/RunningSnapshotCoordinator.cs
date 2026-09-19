using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed record RunningSnapshotCutV1(
    WorldStateV1 FrozenState,
    OpaqueId128 SnapshotId,
    HistoryAnchor HistoryAnchor,
    byte[] StateContinuityToken,
    IReadOnlyList<DurableOperationStateV1> DurableOperations,
    IReadOnlyList<ScheduledOperationRefV1> ScheduledOperations)
{
    public ulong SnapshotStep => FrozenState.Header.Step;

    /// <summary>
    /// Same-SQLite-read CrossDomainTransaction custody frozen with Operation/Scheduler state.
    /// This remains raw durable-row material until core.operation-state /2.0 provider validation.
    /// QA-04 compact cuts contain only the active transaction set at the frozen Snapshot boundary.
    /// </summary>
    public IReadOnlyList<DurableCrossDomainTransactionStateV1> CrossDomainTransactions { get; init; }
        = Array.Empty<DurableCrossDomainTransactionStateV1>();

    /// <summary>
    /// Closed perf.reference.v1 Operation prefix frozen in the same SQLite read transaction.
    /// Null means the ordinary core.operation-state authority path is in use.
    /// </summary>
    public Qa04OperationClosedPrefixV1? Qa04OperationClosedPrefix { get; init; }

    /// <summary>
    /// Present only when the strict owner-material freeze overload was used. The material remains
    /// runtime/schema-owner data; exact Core snapshot wire serialization is intentionally deferred
    /// until the P4-04 Core section payload amendment is authoritative.
    /// </summary>
    public CoreSnapshotOwnerMaterialCutV1? CoreOwnerMaterial { get; init; }
}

/// <summary>
/// Owns the operational trigger/freeze/commit boundary for a running snapshot.
/// The caller invokes TryFreezeIfDueAsync while holding its short Step-boundary consistency barrier.
/// The returned WorldState and RecoveryState cut may be drained after the barrier is released while
/// later Steps continue. Physical serialization remains schema-owned by the supplied drain implementation.
/// </summary>
public sealed class RunningSnapshotCoordinatorV1
{
    public const ulong StandardIntervalSteps = 18_000;

    private readonly ulong _intervalSteps;
    private readonly object _sync = new();
    private OpaqueId128? _inFlightSnapshotId;

    public RunningSnapshotCoordinatorV1(ulong intervalSteps = StandardIntervalSteps)
    {
        if (intervalSteps < 30 || intervalSteps > 100_000_000)
            throw new ArgumentOutOfRangeException(nameof(intervalSteps));
        _intervalSteps = intervalSteps;
    }

    public ulong IntervalSteps => _intervalSteps;

    public bool IsDue(ulong finalizedStep, ulong? newestCommittedSnapshotStep = null)
        => finalizedStep != 0 &&
           finalizedStep % _intervalSteps == 0 &&
           (newestCommittedSnapshotStep is null || newestCommittedSnapshotStep.Value < finalizedStep);

    public Task<RunningSnapshotCutV1?> TryFreezeIfDueAsync(
        WorldStateV1 finalizedState,
        SqlitePersistenceStore store,
        CancellationToken cancellationToken = default)
        => TryFreezeInternalAsync(
            finalizedState,
            store,
            supplementalOwnerMaterial: null,
            operationAuthorityV2: false,
            cancellationToken);

    /// <summary>
    /// Strict running-snapshot freeze for the six Core recovery owners using core.operation-state /1.0.
    /// Scheduler/Operation material is read from the same SQLite recovery transaction as the snapshot
    /// head; detail/domain-registry/Config material is supplied by its owning runtime and must
    /// independently recompute the frozen authoritative digest.
    /// </summary>
    public Task<RunningSnapshotCutV1?> TryFreezeWithCoreOwnerMaterialIfDueAsync(
        WorldStateV1 finalizedState,
        SqlitePersistenceStore store,
        IEnumerable<IFrozenCoreSnapshotOwnerMaterialV1> supplementalOwnerMaterial,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(supplementalOwnerMaterial);
        return TryFreezeInternalAsync(
            finalizedState,
            store,
            supplementalOwnerMaterial,
            operationAuthorityV2: false,
            cancellationToken);
    }

    /// <summary>
    /// Strict running-snapshot freeze for core.operation-state /2.0. The same SQLite recovery cut
    /// freezes durable Operation rows and persistent CrossDomainTransaction rows, decodes the latter
    /// at the frozen finalized Step, and validates the combined v2 authority before exposing the cut.
    /// When a QA-04 closed prefix exists, the compact prefix plus mutable rows plus active transaction
    /// set are recomputed instead of the ordinary full Operation-state authority.
    /// </summary>
    public Task<RunningSnapshotCutV1?> TryFreezeWithCoreOwnerMaterialV2IfDueAsync(
        WorldStateV1 finalizedState,
        SqlitePersistenceStore store,
        IEnumerable<IFrozenCoreSnapshotOwnerMaterialV1> supplementalOwnerMaterial,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(supplementalOwnerMaterial);
        return TryFreezeInternalAsync(
            finalizedState,
            store,
            supplementalOwnerMaterial,
            operationAuthorityV2: true,
            cancellationToken);
    }

    private async Task<RunningSnapshotCutV1?> TryFreezeInternalAsync(
        WorldStateV1 finalizedState,
        SqlitePersistenceStore store,
        IEnumerable<IFrozenCoreSnapshotOwnerMaterialV1>? supplementalOwnerMaterial,
        bool operationAuthorityV2,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(finalizedState);
        ArgumentNullException.ThrowIfNull(store);

        lock (_sync)
        {
            if (_inFlightSnapshotId is not null)
                return null;
        }

        var committed = await store.ListSnapshotCandidatesNewestFirstAsync(cancellationToken).ConfigureAwait(false);
        var newestStep = committed.Count == 0 ? (ulong?)null : committed[0].SnapshotStep;
        if (!IsDue(finalizedState.Header.Step, newestStep))
            return null;

        var recovery = await store.ReadSnapshotRecoveryCutAsync(cancellationToken).ConfigureAwait(false);
        if (recovery.FinalizedStep != finalizedState.Header.Step)
            throw new InvalidDataException("snapshot-running.state-not-finalized-head");
        if (recovery.ConfigGeneration != finalizedState.Header.ConfigGeneration ||
            !CryptographicOperations.FixedTimeEquals(recovery.ConfigDigest, finalizedState.Diagnostic.ConfigDigest))
            throw new InvalidDataException("snapshot-running.config-authority-mismatch");

        IReadOnlyList<CrossDomainTransactionStateV1>? transactions = null;
        WorldSubstateRefV1 operationAuthority;
        if (operationAuthorityV2)
        {
            transactions = CoreOperationStateSnapshotCutV2.DecodeTransactions(
                recovery.CrossDomainTransactions,
                recovery.FinalizedStep);
            operationAuthority = recovery.Qa04OperationClosedPrefix is { } prefix
                ? Qa04OperationAuthorityV1.Canonicalize(
                    recovery.DurableOperations,
                    prefix,
                    transactions,
                    recovery.FinalizedStep)
                : CoreOperationStateSubstateV2.Canonicalize(
                    recovery.DurableOperations,
                    transactions,
                    recovery.FinalizedStep);
        }
        else
        {
            operationAuthority = DurableOperationSubstateV1.Canonicalize(recovery.DurableOperations);
        }

        if (!SubstateEquals(finalizedState.OperationState, operationAuthority))
            throw new InvalidDataException(operationAuthorityV2
                ? recovery.Qa04OperationClosedPrefix is null
                    ? "snapshot-running.operation-v2-authority-mismatch"
                    : "snapshot-running.qa04-operation-v2-authority-mismatch"
                : "snapshot-running.operation-authority-mismatch");

        var schedulerProjection = new OperationSchedulerStateV1(
            nextSchedulableStep: finalizedState.Header.Step,
            freezeStep: null,
            scheduled: recovery.ScheduledOperations);
        var schedulerAuthority = OperationSchedulerSubstateV1.Canonicalize(
            schedulerProjection,
            finalizedState.Header.Step);
        if (!SubstateEquals(finalizedState.SchedulerState, schedulerAuthority))
            throw new InvalidDataException("snapshot-running.scheduler-authority-mismatch");

        CoreSnapshotOwnerMaterialCutV1? coreOwnerMaterial = null;
        if (supplementalOwnerMaterial is not null)
        {
            if (operationAuthorityV2)
            {
                var frozenTransactions = transactions
                    ?? throw new InvalidDataException("snapshot-running.operation-v2-material-missing");
                coreOwnerMaterial = recovery.Qa04OperationClosedPrefix is { } prefix
                    ? CoreSnapshotOwnerMaterialCutV1.CreateQa04V2(
                        finalizedState,
                        recovery.DurableOperations,
                        recovery.ScheduledOperations,
                        frozenTransactions,
                        prefix,
                        supplementalOwnerMaterial)
                    : CoreSnapshotOwnerMaterialCutV1.CreateV2(
                        finalizedState,
                        recovery.DurableOperations,
                        recovery.ScheduledOperations,
                        frozenTransactions,
                        supplementalOwnerMaterial);
            }
            else
            {
                coreOwnerMaterial = CoreSnapshotOwnerMaterialCutV1.Create(
                    finalizedState,
                    recovery.DurableOperations,
                    recovery.ScheduledOperations,
                    supplementalOwnerMaterial);
            }
        }

        var snapshotId = DeriveSnapshotId(
            finalizedState.Header.WorldId,
            finalizedState.Header.Step,
            recovery.HistoryAnchor.Sequence,
            recovery.HistoryAnchor.Digest,
            recovery.StateContinuityToken);
        var cut = new RunningSnapshotCutV1(
            finalizedState,
            snapshotId,
            recovery.HistoryAnchor,
            recovery.StateContinuityToken.ToArray(),
            recovery.DurableOperations,
            recovery.ScheduledOperations)
        {
            CrossDomainTransactions = recovery.CrossDomainTransactions,
            Qa04OperationClosedPrefix = recovery.Qa04OperationClosedPrefix,
            CoreOwnerMaterial = coreOwnerMaterial,
        };

        lock (_sync)
        {
            if (_inFlightSnapshotId is not null)
                return null;
            _inFlightSnapshotId = snapshotId;
        }
        return cut;
    }

    public async Task<DurableSnapshotCommitResult> CommitDrainedAsync(
        RunningSnapshotCutV1 cut,
        SqlitePersistenceStore store,
        WorldPersistencePaths world,
        SnapshotPhysicalPaths physical,
        ReadOnlyMemory<byte> snapshotDigest,
        ReadOnlyMemory<byte> physicalManifestDigest,
        Func<SnapshotPhysicalPaths, CancellationToken, Task> validateStaging,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cut);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(physical);
        ArgumentNullException.ThrowIfNull(validateStaging);
        RequireDigest(snapshotDigest, nameof(snapshotDigest));
        RequireDigest(physicalManifestDigest, nameof(physicalManifestDigest));
        if (physical.SnapshotId != cut.SnapshotId)
            throw new InvalidDataException("snapshot-running.physical-id-mismatch");

        lock (_sync)
        {
            if (_inFlightSnapshotId is not { } inFlight || inFlight != cut.SnapshotId)
                throw new InvalidDataException("snapshot-running.cut-not-in-flight");
        }

        try
        {
            var currentAnchor = await store.ReadHistoryAnchorAsync(cancellationToken).ConfigureAwait(false);
            if (currentAnchor.Sequence == ulong.MaxValue)
                throw new OverflowException("HistorySequence cannot wrap.");

            var logicalDigest = snapshotDigest.ToArray();
            var physicalDigest = physicalManifestDigest.ToArray();
            var relativeDirectory = Path.GetRelativePath(world.GenerationDirectory, physical.FinalDirectory)
                .Replace('\\', '/');
            var snapshot = new SnapshotCommitMaterial(
                cut.SnapshotId,
                cut.SnapshotStep,
                cut.HistoryAnchor,
                cut.StateContinuityToken.ToArray(),
                logicalDigest,
                physicalDigest,
                relativeDirectory);

            var payloadBytes = cut.SnapshotId.ToBytes()
                .Concat(logicalDigest)
                .Concat(physicalDigest)
                .ToArray();
            var history = HistoryRecordMaterial.Create(
                cut.FrozenState.Header.WorldId,
                checked(currentAnchor.Sequence + 1),
                currentAnchor.Digest,
                "snapshot.committed.v1",
                "core.snapshot-committed.v1",
                1,
                0,
                payloadBytes,
                writer =>
                {
                    writer.WriteMapStart(7);
                    writer.WriteUnsigned(0); writer.WriteBytes(cut.SnapshotId.ToBytes());
                    writer.WriteUnsigned(1); writer.WriteUnsigned(cut.SnapshotStep);
                    writer.WriteUnsigned(2); writer.WriteUnsigned(cut.HistoryAnchor.Sequence);
                    writer.WriteUnsigned(3); writer.WriteBytes(cut.HistoryAnchor.Digest);
                    writer.WriteUnsigned(4); writer.WriteBytes(cut.StateContinuityToken);
                    writer.WriteUnsigned(5); writer.WriteBytes(logicalDigest);
                    writer.WriteUnsigned(6); writer.WriteBytes(physicalDigest);
                });

            return await SnapshotCommitCoordinator.CommitAsync(
                store,
                world,
                physical,
                snapshot,
                history,
                validateStaging,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_sync)
            {
                if (_inFlightSnapshotId == cut.SnapshotId)
                    _inFlightSnapshotId = null;
            }
        }
    }

    public void Abandon(RunningSnapshotCutV1 cut)
    {
        ArgumentNullException.ThrowIfNull(cut);
        lock (_sync)
        {
            if (_inFlightSnapshotId == cut.SnapshotId)
                _inFlightSnapshotId = null;
        }
    }

    public static OpaqueId128 DeriveSnapshotId(
        OpaqueId128 worldId,
        ulong snapshotStep,
        ulong historyAnchorSequence,
        ReadOnlySpan<byte> historyAnchorDigest,
        ReadOnlySpan<byte> stateContinuityToken)
    {
        if (worldId.IsZero) throw new ArgumentException("WorldId ZERO is invalid.", nameof(worldId));
        if (historyAnchorSequence == 0) throw new ArgumentOutOfRangeException(nameof(historyAnchorSequence));
        if (historyAnchorDigest.Length != 32) throw new ArgumentException("History anchor digest must be 32 bytes.", nameof(historyAnchorDigest));
        if (stateContinuityToken.Length != 32) throw new ArgumentException("State continuity token must be 32 bytes.", nameof(stateContinuityToken));

        var anchorDigest = historyAnchorDigest.ToArray();
        var continuityToken = stateContinuityToken.ToArray();
        for (ulong nonce = 0; ; nonce++)
        {
            var digest = HashSuite.DomainHash("mv.snapshot-id.v1", writer =>
            {
                writer.WriteMapStart(nonce == 0 ? 5UL : 6UL);
                writer.WriteUnsigned(0); writer.WriteBytes(worldId.ToBytes());
                writer.WriteUnsigned(1); writer.WriteUnsigned(snapshotStep);
                writer.WriteUnsigned(2); writer.WriteUnsigned(historyAnchorSequence);
                writer.WriteUnsigned(3); writer.WriteBytes(anchorDigest);
                writer.WriteUnsigned(4); writer.WriteBytes(continuityToken);
                if (nonce != 0)
                {
                    writer.WriteUnsigned(5); writer.WriteUnsigned(nonce);
                }
            });
            var id = HashSuite.Trunc128(digest);
            if (!id.IsZero) return id;
            if (nonce == ulong.MaxValue) throw new InvalidDataException("snapshot-running.snapshot-id-derivation-exhausted");
        }
    }

    private static bool SubstateEquals(WorldSubstateRefV1 left, WorldSubstateRefV1 right)
        => left.Schema == right.Schema &&
           CryptographicOperations.FixedTimeEquals(left.CanonicalDigest, right.CanonicalDigest);

    private static void RequireDigest(ReadOnlyMemory<byte> value, string field)
    {
        if (value.Length != 32)
            throw new ArgumentException($"{field} must be exactly 32 bytes.", field);
    }
}
