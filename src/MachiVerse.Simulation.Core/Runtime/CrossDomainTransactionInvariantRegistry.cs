using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Runtime;

public static class CrossDomainTransactionInvariantRegistryV1
{
    private static readonly IReadOnlyDictionary<StableToken, IReadOnlyList<StableToken>> RequiredByKind =
        CrossDomainTransactionKindRegistryV1.Entries.ToDictionary(
            static kind => kind,
            static kind => (IReadOnlyList<StableToken>)Array.AsReadOnly(
                new[] { new StableToken(kind.Value + ".atomicity") }));

    static CrossDomainTransactionInvariantRegistryV1()
    {
        if (RequiredByKind.Count != CrossDomainTransactionKindRegistryV1.StandardKindCount)
            throw new InvalidOperationException("Transaction invariant registry coverage mismatch.");
        if (RequiredByKind.Any(static pair => pair.Value.Count == 0 || pair.Value.Distinct().Count() != pair.Value.Count))
            throw new InvalidOperationException("Transaction invariant registry contains invalid required invariant IDs.");
    }

    public static IReadOnlyList<StableToken> GetRequiredInvariantIds(StableToken transactionKind)
        => RequiredByKind.TryGetValue(transactionKind, out var invariantIds)
            ? invariantIds
            : throw new InvalidDataException("transaction.kind-unregistered");
}
