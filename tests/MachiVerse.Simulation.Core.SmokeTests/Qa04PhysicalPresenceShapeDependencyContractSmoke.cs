using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Performance;

internal static class Qa04PhysicalPresenceShapeDependencyContractSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04PhysicalPresenceShapeDependencyContractV1.ValidateCanonicalContract();
        Require(Qa04ReferenceWorldDependencyContractV1.Blockers.Count == 7,
            "Resolved Physical shape dependency must leave exactly seven world blockers.");
        Require(Qa04ReferenceWorldDependencyContractV1.FailureCodes.All(
                static code => code.Value != Qa04PhysicalPresenceShapeDependencyContractV1.ParentWorldFailureCode),
            "Resolved Physical shape failure code must remain absent from the world dependency contract.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
