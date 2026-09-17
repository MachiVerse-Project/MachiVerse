using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// QA-04 production transition durability adapter. Ordinary Steps use the standard transition
/// commit. A turnover Step supplies only the CrossDomainTransaction rows whose authority changes
/// in State(S+1), so terminalization/replacement and the transition recovery head commit atomically.
/// The durable authority verifier below is intentionally reusable after store reopen so the same
/// canonical ACTIVE transaction authority is checked on both sides of the recovery boundary.
/// </summary>
public sealed class Qa04CrossDomainStepTransitionDurabilityV1 : IStepTransitionDurabilityV1
{
    private readonly SqlitePersistenceStore _store;
    private readonly IReadOnlyList<CrossDomainTransactionStateV1> _transactionChanges;

    public Qa04CrossDomainStepTransitionDurabilityV1(
        SqlitePersistenceStore store,
        IReadOnlyCollection<CrossDomainTransactionStateV1> transactionChanges)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        ArgumentNullException.ThrowIfNull(transactionChanges);
        var ordered = transactionChanges.OrderBy(static state => state.TransactionId).ToArray();
        if (ordered.Select(static state => state.TransactionId).Distinct().Count() != ordered.Length)
            throw new InvalidDataException("qa04.production-turnover.persistence-change-duplicate");
        _transactionChanges = Array.AsReadOnly(ordered);
    }

    public Task<DurableTransitionResult> CommitAsync(
        StepCandidateV1 candidate,
        StepFinalizeMaterialV1 material,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(material);

        if (_transactionChanges.Count == 0)
        {
            return _store.PersistTransitionCommitAsync(
                candidate.BasisStep,
                candidate.TargetStep,
                material.ResultingStateContinuityToken,
                material.ActiveConfigGeneration,
                material.ActiveConfigDigest,
                material.TransitionHistory,
                material.TerminalOperations,
                cancellationToken);
        }

        if (_transactionChanges.Any(state => state.UpdatedStep != candidate.TargetStep))
            throw new InvalidDataException("qa04.production-turnover.persistence-change-step-drift");

        return _store.PersistTransitionCommitWithCanonicalCrossDomainTransactionsAsync(
            candidate.BasisStep,
            candidate.TargetStep,
            material.ResultingStateContinuityToken,
            material.ActiveConfigGeneration,
            material.ActiveConfigDigest,
            material.TransitionHistory,
            material.TerminalOperations,
            _transactionChanges,
            cancellationToken);
    }
}

public static class Qa04CrossDomainDurableAuthorityVerifierV1
{
    public static async Task RequireActiveAuthorityAsync(
        SqlitePersistenceStore store,
        IReadOnlyCollection<CrossDomainTransactionStateV1> expectedActive,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(expectedActive);

        var rows = await store.ListActiveCrossDomainTransactionStatesCanonicalAsync(cancellationToken)
            .ConfigureAwait(false);
        if (rows.Count != expectedActive.Count)
            throw new InvalidDataException("qa04.production-turnover.durable-active-count-drift");

        var durable = rows
            .Select(CrossDomainTransactionPersistentWireV1.DecodeAndValidate)
            .OrderBy(static state => state.TransactionId)
            .ToArray();
        var expected = expectedActive.OrderBy(static state => state.TransactionId).ToArray();
        for (var index = 0; index < expected.Length; index++)
        {
            if (durable[index].TransactionId != expected[index].TransactionId ||
                !CryptographicOperations.FixedTimeEquals(
                    durable[index].CanonicalDigest(),
                    expected[index].CanonicalDigest()))
                throw new InvalidDataException("qa04.production-turnover.durable-active-authority-drift");
        }
    }
}
