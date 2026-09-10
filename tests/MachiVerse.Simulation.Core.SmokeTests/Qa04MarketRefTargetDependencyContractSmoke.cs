using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Performance;

internal static class Qa04MarketRefTargetDependencyContractSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04MarketRefTargetDependencyContractV1.ValidateCanonicalContract();
        Require(Qa04ReferenceWorldDependencyContractV1.Blockers.Count == 6,
            "Resolved Market ref dependency must leave exactly six world blockers.");
        Require(Qa04ReferenceWorldDependencyContractV1.FailureCodes.All(
                static code => code.Value != Qa04MarketRefTargetDependencyContractV1.ParentWorldFailureCode),
            "Resolved Market ref failure code must remain absent from the world dependency contract.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
