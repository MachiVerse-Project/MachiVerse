using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Runtime;

public enum TransactionCandidateStatusV1 : byte
{
    Assembling = 0,
    ReadyForValidation = 1,
    Valid = 2,
    Invalid = 3,
}

public sealed record CrossDomainTransactionKindRegistrationV1(
    StableToken TransactionKind,
    IReadOnlyList<StableToken> RequiredDomains,
    IReadOnlyList<StableToken> OptionalDomains);

public static class CrossDomainTransactionKindRegistryV1
{
    public const int StandardKindCount = 17;

    private static readonly StableToken Spatial = new("spatial");
    private static readonly StableToken Environment = new("environment");
    private static readonly StableToken PhysicalBuilt = new("physical_built");
    private static readonly StableToken Participation = new("participation");
    private static readonly StableToken Resident = new("resident");
    private static readonly StableToken SocietyEconomy = new("society_economy");
    private static readonly StableToken GovernanceSecurity = new("governance_security");
    private static readonly StableToken InfrastructureInformation = new("infrastructure_information");

    private static readonly CrossDomainTransactionKindRegistrationV1[] StandardRegistrations =
    [
        Register("transaction.mining-excavation", [Spatial, Environment, PhysicalBuilt], [SocietyEconomy]),
        Register("transaction.construction", [PhysicalBuilt, SocietyEconomy], [GovernanceSecurity, Spatial]),
        Register("transaction.demolition", [PhysicalBuilt], [Spatial, SocietyEconomy, GovernanceSecurity]),
        Register("transaction.birth", [Resident], [SocietyEconomy]),
        Register("transaction.death", [Resident], [Participation, SocietyEconomy, GovernanceSecurity]),
        Register("transaction.disease-transmission", [Resident], [Environment, InfrastructureInformation]),
        Register("transaction.food-consumption", [Resident, PhysicalBuilt], [SocietyEconomy]),
        Register("transaction.market-sale-delivery", [SocietyEconomy, PhysicalBuilt], [InfrastructureInformation]),
        Register("transaction.information-transmission", [InfrastructureInformation], [SocietyEconomy, Resident]),
        Register("transaction.public-record", [InfrastructureInformation], [GovernanceSecurity, SocietyEconomy]),
        Register("transaction.crime-justice", [GovernanceSecurity], [Resident, PhysicalBuilt, SocietyEconomy]),
        Register("transaction.border-crossing", [GovernanceSecurity, PhysicalBuilt], [InfrastructureInformation]),
        Register("transaction.natural-disaster-cascade", [Environment, PhysicalBuilt, Resident, InfrastructureInformation], [Spatial, GovernanceSecurity]),
        Register("transaction.infrastructure-outage-cascade", [InfrastructureInformation], [PhysicalBuilt, SocietyEconomy, Resident]),
        Register("transaction.medical-service", [Resident, InfrastructureInformation], [SocietyEconomy, PhysicalBuilt]),
        Register("transaction.employment-work", [SocietyEconomy, Resident, PhysicalBuilt], [InfrastructureInformation]),
        Register("transaction.military-operation", [GovernanceSecurity, Resident, PhysicalBuilt, InfrastructureInformation], [SocietyEconomy]),
    ];

    private static readonly StableToken[] StandardKinds =
        StandardRegistrations.Select(static registration => registration.TransactionKind).ToArray();

    private static readonly IReadOnlyDictionary<string, StableToken> ByValue =
        StandardKinds.ToDictionary(static kind => kind.Value, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<StableToken, CrossDomainTransactionKindRegistrationV1> ByKind =
        StandardRegistrations.ToDictionary(static registration => registration.TransactionKind);

    public static IReadOnlyList<StableToken> Entries { get; } = Array.AsReadOnly(StandardKinds);
    public static IReadOnlyList<CrossDomainTransactionKindRegistrationV1> Registrations { get; } =
        Array.AsReadOnly(StandardRegistrations);

    static CrossDomainTransactionKindRegistryV1()
    {
        if (Entries.Count != StandardKindCount || Registrations.Count != StandardKindCount)
            throw new InvalidOperationException($"CrossDomainTransactionKind registry must contain exactly {StandardKindCount} entries.");
        if (Entries.Select(static kind => kind.Value).Distinct(StringComparer.Ordinal).Count() != StandardKindCount)
            throw new InvalidOperationException("CrossDomainTransactionKind registry contains duplicate tokens.");
    }

    public static StableToken Get(string transactionKind)
        => ByValue.TryGetValue(transactionKind, out var kind)
            ? kind
            : throw new InvalidDataException("transaction.kind-unregistered");

    public static CrossDomainTransactionKindRegistrationV1 GetRegistration(StableToken transactionKind)
        => ByKind.TryGetValue(transactionKind, out var registration)
            ? registration
            : throw new InvalidDataException("transaction.kind-unregistered");

    public static bool Contains(StableToken transactionKind)
        => ByValue.ContainsKey(transactionKind.Value);

    private static CrossDomainTransactionKindRegistrationV1 Register(
        string transactionKind,
        IEnumerable<StableToken> requiredDomains,
        IEnumerable<StableToken> optionalDomains)
    {
        var required = CanonicalDomains(requiredDomains);
        var optional = CanonicalDomains(optionalDomains);
        if (required.Intersect(optional).Any())
            throw new InvalidOperationException("Transaction participant domain cannot be both required and optional.");
        if (required.Count == 0)
            throw new InvalidOperationException("Transaction registration must have at least one required participant domain.");

        return new CrossDomainTransactionKindRegistrationV1(
            new StableToken(transactionKind),
            required,
            optional);
    }

    private static IReadOnlyList<StableToken> CanonicalDomains(IEnumerable<StableToken> domains)
    {
        ArgumentNullException.ThrowIfNull(domains);
        var rankByDomain = StandardDomainExecutionPlanV1.Create().Entries
            .ToDictionary(static entry => entry.DomainToken, static entry => entry.DomainRank);
        var materialized = domains
            .OrderBy(domain => rankByDomain.TryGetValue(domain, out var rank) ? rank : ushort.MaxValue)
            .ThenBy(static domain => domain.Value, StringComparer.Ordinal)
            .ToArray();
        if (materialized.Distinct().Count() != materialized.Length ||
            materialized.Any(domain => !rankByDomain.ContainsKey(domain)))
            throw new InvalidOperationException("Transaction registry contains an invalid participant domain.");
        return Array.AsReadOnly(materialized);
    }
}
