using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Runtime;

internal static class Sim13GoldenScenarioSmoke
{
    internal static void Run()
    {
        Verify(
            "transaction.mining-excavation",
            ["spatial", "environment", "physical_built"],
            ["society_economy"]);
        Verify(
            "transaction.market-sale-delivery",
            ["physical_built", "society_economy"],
            ["infrastructure_information"]);
        Verify(
            "transaction.death",
            ["resident"],
            ["participation", "society_economy", "governance_security"]);
        Verify(
            "transaction.natural-disaster-cascade",
            ["environment", "physical_built", "resident", "infrastructure_information"],
            ["spatial", "governance_security"]);
        Verify(
            "transaction.military-operation",
            ["physical_built", "resident", "governance_security", "infrastructure_information"],
            ["society_economy"]);
    }

    private static void Verify(
        string transactionKind,
        IReadOnlyList<string> requiredDomains,
        IReadOnlyList<string> optionalDomains)
    {
        var registration = CrossDomainTransactionKindRegistryV1.GetRegistration(
            CrossDomainTransactionKindRegistryV1.Get(transactionKind));
        Require(
            registration.RequiredDomains.Select(static token => token.Value).SequenceEqual(requiredDomains) &&
            registration.OptionalDomains.Select(static token => token.Value).SequenceEqual(optionalDomains),
            $"{transactionKind}: Phase 4 golden participant contract mismatch.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
