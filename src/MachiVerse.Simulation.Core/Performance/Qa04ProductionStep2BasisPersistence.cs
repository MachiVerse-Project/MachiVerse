using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Establishes the Gate4 Step2 canonical State(1) Operation authority. No generated Operation has
/// closed yet, so mutable Operation rows are empty and the closed prefix is the canonical genesis.
/// The existing CrossDomainTransaction genesis set remains ordinary durable authority.
/// </summary>
public static class Qa04ProductionStep2BasisPersistenceV1
{
    public static WorldStateV1 BindInitialBasis(
        WorldStateV1 partitionAuthorityState,
        OperationSchedulerStateV1 scheduler,
        IReadOnlyCollection<CrossDomainTransactionStateV1> activeTransactions,
        Qa04OperationClosedPrefixV1? closedPrefix = null)
    {
        ArgumentNullException.ThrowIfNull(partitionAuthorityState);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(activeTransactions);
        var prefix = closedPrefix ?? Qa04OperationClosedPrefixV1.Empty();
        return Qa04ProductionStep2BasisAuthorityV1.Bind(
            partitionAuthorityState,
            scheduler,
            Array.Empty<DurableOperationStateV1>(),
            prefix,
            activeTransactions);
    }

    public static async Task<DurableTransitionResult> PersistAsync(
        SqlitePersistenceStore store,
        WorldStateV1 basisState,
        ReadOnlyMemory<byte> initialContinuity,
        Qa04OperationClosedPrefixV1 closedPrefix,
        IReadOnlyCollection<CrossDomainTransactionStateV1> activeTransactions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(basisState);
        ArgumentNullException.ThrowIfNull(closedPrefix);
        ArgumentNullException.ThrowIfNull(activeTransactions);
        if (initialContinuity.Length != 32)
            throw new ArgumentException("Initial continuity token must be exactly 32 bytes.", nameof(initialContinuity));
        if (basisState.Header.WorldId != Qa04ReferenceLoadV1.WorldId ||
            basisState.Header.Step != Qa04ProductionReferenceWorldAssemblerV1.CanonicalBasisStep)
            throw new InvalidDataException("qa04.step2.production-basis.identity-or-step-drift");
        closedPrefix.Validate(basisState.Header.Step);

        var recoveryBefore = await store.ReadRecoveryHeadAsync(cancellationToken).ConfigureAwait(false);
        if (recoveryBefore.FinalizedStep != 0 ||
            !CryptographicOperations.FixedTimeEquals(recoveryBefore.ContinuityToken, initialContinuity.Span))
            throw new InvalidDataException("qa04.step2.production-basis.recovery-head-drift");

        await store.InitializeQa04OperationClosedPrefixAsync(
            closedPrefix,
            basisState.Header.Step,
            cancellationToken).ConfigureAwait(false);
        await RequireDurableGenesisTransactionsAsync(store, activeTransactions, cancellationToken)
            .ConfigureAwait(false);

        var durableOperations = await store.ListOperationStatesCanonicalAsync(cancellationToken).ConfigureAwait(false);
        if (durableOperations.Count != 0)
            throw new InvalidDataException("qa04.step2.production-basis.generated-operation-row-not-empty");
        var expectedOperation = Qa04OperationAuthorityV1.Canonicalize(
            durableOperations,
            closedPrefix,
            activeTransactions,
            basisState.Header.Step);
        Qa04ProductionStep2BasisAuthorityV1.RequireSubstateMatch(
            expectedOperation,
            basisState.OperationState,
            "qa04.step2.production-basis.operation-authority-drift");

        var anchor = await store.ReadHistoryAnchorAsync(cancellationToken).ConfigureAwait(false);
        var transition = HistoryRecordMaterial.Create(
            Qa04ReferenceLoadV1.WorldId,
            checked(anchor.Sequence + 1UL),
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

        var durablePrefix = await store.ReadQa04OperationClosedPrefixAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("qa04.step2.production-basis.closed-prefix-missing");
        RequireSamePrefix(durablePrefix, closedPrefix);
        var recoveryAfter = await store.ReadRecoveryHeadAsync(cancellationToken).ConfigureAwait(false);
        if (recoveryAfter.FinalizedStep != basisState.Header.Step)
            throw new InvalidDataException("qa04.step2.production-basis.finalized-step-drift");
        await RequireDurableGenesisTransactionsAsync(store, activeTransactions, cancellationToken)
            .ConfigureAwait(false);
        return result;
    }

    private static async Task RequireDurableGenesisTransactionsAsync(
        SqlitePersistenceStore store,
        IReadOnlyCollection<CrossDomainTransactionStateV1> expectedTransactions,
        CancellationToken cancellationToken)
    {
        var expected = expectedTransactions.OrderBy(static state => state.TransactionId).ToArray();
        if (expected.Length != checked((int)Qa04CrossDomainTransactionGenesisMaterializerV1.CanonicalActiveCount) ||
            expected.Select(static state => state.TransactionId).Distinct().Count() != expected.Length)
            throw new InvalidDataException("qa04.step2.production-basis.genesis-transaction-set-invalid");
        if (expected.Any(static state => !state.IsActive || state.CreatedStep != 0 ||
                                         state.UpdatedStep != 0 || state.TerminalStep is not null))
            throw new InvalidDataException("qa04.step2.production-basis.genesis-transaction-state-invalid");

        var durable = await store.ListActiveCrossDomainTransactionStatesCanonicalAsync(cancellationToken)
            .ConfigureAwait(false);
        if (durable.Count != expected.Length)
            throw new InvalidDataException("qa04.step2.production-basis.genesis-transaction-count-drift");
        for (var index = 0; index < expected.Length; index++)
        {
            var expectedState = expected[index];
            var actual = durable[index];
            if (actual.TransactionId != expectedState.TransactionId ||
                actual.Lifecycle != expectedState.Lifecycle ||
                actual.CreatedStep != expectedState.CreatedStep ||
                actual.UpdatedStep != expectedState.UpdatedStep ||
                actual.TerminalStep != expectedState.TerminalStep ||
                !actual.StateWire.AsSpan().SequenceEqual(CrossDomainTransactionPersistentWireV1.Encode(expectedState)) ||
                !CryptographicOperations.FixedTimeEquals(actual.StateDigest, expectedState.CanonicalDigest()))
                throw new InvalidDataException("qa04.step2.production-basis.genesis-transaction-authority-drift");
        }
    }

    private static void RequireSamePrefix(
        Qa04OperationClosedPrefixV1 actual,
        Qa04OperationClosedPrefixV1 expected)
    {
        if (!string.Equals(actual.ProfileId, expected.ProfileId, StringComparison.Ordinal) ||
            actual.FirstInjectionStep != expected.FirstInjectionStep ||
            actual.LastClosedInjectionStep != expected.LastClosedInjectionStep ||
            actual.TerminalOperationCount != expected.TerminalOperationCount ||
            !CryptographicOperations.FixedTimeEquals(actual.TerminalSemanticDigest, expected.TerminalSemanticDigest))
            throw new InvalidDataException("qa04.step2.production-basis.closed-prefix-drift");
    }
}
