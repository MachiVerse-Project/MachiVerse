using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Persists the canonical QA-04 State(S)=1 basis cut. Persistent CrossDomainTransaction authority
/// is committed in the same SQLite transition as the finalized-step/config/recovery head so a
/// later running Snapshot recovery cut observes the same core.operation-state /2.0 authority as
/// the live WorldState.
/// </summary>
public static class Qa04ProductionBasisPersistenceV1
{
    public static async Task<DurableTransitionResult> PersistAsync(
        SqlitePersistenceStore store,
        WorldStateV1 basisState,
        ReadOnlyMemory<byte> initialContinuity,
        IReadOnlyCollection<CrossDomainTransactionStateV1> crossDomainTransactions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(basisState);
        ArgumentNullException.ThrowIfNull(crossDomainTransactions);
        if (initialContinuity.Length != 32)
            throw new ArgumentException("Initial continuity token must be exactly 32 bytes.", nameof(initialContinuity));
        if (basisState.Header.WorldId != Qa04ReferenceLoadV1.WorldId ||
            basisState.Header.Step != Qa04ProductionReferenceWorldAssemblerV1.CanonicalBasisStep)
            throw new InvalidDataException("qa04.production-basis.identity-or-step-drift");

        var recoveryBefore = await store.ReadRecoveryHeadAsync(cancellationToken).ConfigureAwait(false);
        if (recoveryBefore.FinalizedStep != 0 ||
            !CryptographicOperations.FixedTimeEquals(recoveryBefore.ContinuityToken, initialContinuity.Span))
            throw new InvalidDataException("qa04.production-basis.recovery-head-drift");

        var durableOperations = await store.ListOperationStatesCanonicalAsync(cancellationToken).ConfigureAwait(false);
        var expectedOperation = CoreOperationStateSubstateV2.Canonicalize(
            durableOperations,
            crossDomainTransactions,
            basisState.Header.Step);
        if (basisState.OperationState.Schema != expectedOperation.Schema ||
            !CryptographicOperations.FixedTimeEquals(
                basisState.OperationState.CanonicalDigest,
                expectedOperation.CanonicalDigest))
            throw new InvalidDataException("qa04.production-basis.operation-v2-authority-drift");

        var anchor = await store.ReadHistoryAnchorAsync(cancellationToken).ConfigureAwait(false);
        var transition = HistoryRecordMaterial.Create(
            Qa04ReferenceLoadV1.WorldId,
            checked(anchor.Sequence + 1),
            anchor.Digest,
            recordType: "transition.committed.v1",
            payloadSchemaId: "persistence.transition-committed",
            payloadSchemaMajor: 1,
            payloadSchemaMinor: 0,
            payloadBytes: basisState.Diagnostic.StateDigest,
            writeNormalizedPayload: writer =>
            {
                writer.WriteMapStart(3);
                writer.WriteUnsigned(0); writer.WriteUnsigned(0);
                writer.WriteUnsigned(1); writer.WriteUnsigned(basisState.Header.Step);
                writer.WriteUnsigned(2); writer.WriteBytes(basisState.Diagnostic.StateDigest);
            });
        var continuity = HistoryIntegrity.ComputeTransitionContinuityToken(
            Qa04ReferenceLoadV1.WorldId,
            basisState.Header.Step,
            initialContinuity.Span,
            transition.RecordDigest);

        var result = await store.PersistTransitionCommitWithCanonicalCrossDomainTransactionsAsync(
            effectiveStep: 0,
            resultingStep: basisState.Header.Step,
            continuity,
            basisState.Header.ConfigGeneration,
            basisState.Diagnostic.ConfigDigest,
            transition,
            Array.Empty<TerminalOperationCommit>(),
            crossDomainTransactions,
            cancellationToken).ConfigureAwait(false);

        var recoveryAfter = await store.ReadSnapshotRecoveryCutAsync(cancellationToken).ConfigureAwait(false);
        if (recoveryAfter.FinalizedStep != basisState.Header.Step ||
            recoveryAfter.CrossDomainTransactions.Count != crossDomainTransactions.Count)
            throw new InvalidDataException("qa04.production-basis.snapshot-recovery-cut-drift");
        return result;
    }
}
