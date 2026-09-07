using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Runtime;

public enum TransactionCandidateStatusV1 : byte
{
    Assembling = 0,
    ReadyForValidation = 1,
    Valid = 2,
    Invalid = 3,
}

public static class CrossDomainTransactionKindRegistryV1
{
    public const int StandardKindCount = 17;

    private static readonly StableToken[] StandardKinds =
    [
        new("transaction.mining-excavation"),
        new("transaction.construction"),
        new("transaction.demolition"),
        new("transaction.birth"),
        new("transaction.death"),
        new("transaction.disease-transmission"),
        new("transaction.food-consumption"),
        new("transaction.market-sale-delivery"),
        new("transaction.information-transmission"),
        new("transaction.public-record"),
        new("transaction.crime-justice"),
        new("transaction.border-crossing"),
        new("transaction.natural-disaster-cascade"),
        new("transaction.infrastructure-outage-cascade"),
        new("transaction.medical-service"),
        new("transaction.employment-work"),
        new("transaction.military-operation"),
    ];

    private static readonly IReadOnlyDictionary<string, StableToken> ByValue =
        StandardKinds.ToDictionary(static kind => kind.Value, StringComparer.Ordinal);

    public static IReadOnlyList<StableToken> Entries { get; } = Array.AsReadOnly(StandardKinds);

    static CrossDomainTransactionKindRegistryV1()
    {
        if (Entries.Count != StandardKindCount)
            throw new InvalidOperationException($"CrossDomainTransactionKind registry must contain exactly {StandardKindCount} entries.");
        if (Entries.Select(static kind => kind.Value).Distinct(StringComparer.Ordinal).Count() != StandardKindCount)
            throw new InvalidOperationException("CrossDomainTransactionKind registry contains duplicate tokens.");
    }

    public static StableToken Get(string transactionKind)
        => ByValue.TryGetValue(transactionKind, out var kind)
            ? kind
            : throw new InvalidDataException("transaction.kind-unregistered");

    public static bool Contains(StableToken transactionKind)
        => ByValue.ContainsKey(transactionKind.Value);
}
