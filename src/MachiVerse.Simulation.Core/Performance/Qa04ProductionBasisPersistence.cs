using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Persists the canonical QA-04 State(S)=1 basis cut. The canonical ACTIVE
/// CrossDomainTransaction set is genesis authority (CreatedStep=UpdatedStep=0) and therefore must
/// already be durable before the 0 -> 1 transition begins. This bridge advances only the recovery
/// head to State(S)=1 and proves that the genesis transaction authority survives unchanged.
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

        await RequireDurableGenesisTransactionsAsync(store, crossDomainTransactions, cancellationToken)
            .ConfigureAwait(false);

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

        var result = await store.PersistTransitionCommitAsync(
            effectiveStep: 0,
            resultingStep: basisState.Header.Step,
            continuity,
            basisState.Header.ConfigGeneration,
            basisState.Diagnostic.ConfigDigest,
            transition,
            Array.Empty<TerminalOperationCommit>(),
            cancellationToken).ConfigureAwait(false);

        await RequireDurableGenesisTransactionsAsync(store, crossDomainTransactions, cancellationToken)
            .ConfigureAwait(false);
        var recoveryAfter = await store.ReadSnapshotRecoveryCutAsync(cancellationToken).ConfigureAwait(false);
        if (recoveryAfter.FinalizedStep != basisState.Header.Step ||
            recoveryAfter.CrossDomainTransactions.Count != crossDomainTransactions.Count)
            throw new InvalidDataException("qa04.production-basis.snapshot-recovery-cut-drift");
        return result;
    }

    private static async Task RequireDurableGenesisTransactionsAsync(
        SqlitePersistenceStore store,
        IReadOnlyCollection<CrossDomainTransactionStateV1> expectedTransactions,
        CancellationToken cancellationToken)
    {
        var expected = expectedTransactions.OrderBy(static state => state.TransactionId).ToArray();
        if (expected.Length == 0 || expected.Select(static state => state.TransactionId).Distinct().Count() != expected.Length)
            throw new InvalidDataException("qa04.production-basis.genesis-transaction-set-invalid");
        foreach (var state in expected)
        {
            if (!state.IsActive || state.Lifecycle != TransactionLifecycleV1.Active ||
                state.CreatedStep != 0 || state.UpdatedStep != 0 || state.TerminalStep is not null)
                throw new InvalidDataException("qa04.production-basis.genesis-transaction-state-invalid");
        }

        var durable = await store.ListActiveCrossDomainTransactionStatesCanonicalAsync(cancellationToken)
            .ConfigureAwait(false);
        if (durable.Count != expected.Length)
            throw new InvalidDataException("qa04.production-basis.genesis-transaction-count-drift");

        for (var index = 0; index < expected.Length; index++)
        {
            var expectedState = expected[index];
            var actual = durable[index];
            var expectedWire = CrossDomainTransactionPersistentWireV1.Encode(expectedState);
            var expectedDigest = expectedState.CanonicalDigest();
            if (actual.TransactionId != expectedState.TransactionId ||
                actual.Lifecycle != expectedState.Lifecycle ||
                actual.CreatedStep != expectedState.CreatedStep ||
                actual.UpdatedStep != expectedState.UpdatedStep ||
                actual.TerminalStep != expectedState.TerminalStep ||
                !actual.StateWire.AsSpan().SequenceEqual(expectedWire) ||
                !CryptographicOperations.FixedTimeEquals(actual.StateDigest, expectedDigest))
                throw new InvalidDataException("qa04.production-basis.genesis-transaction-authority-drift");
        }
    }
}
