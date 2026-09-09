using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Performance;

internal static class Qa04AlgorithmIterationBudgetGuardSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        var receipt = Qa04AlgorithmIterationBudgetGuardV1.ValidateCanonicalContract();
        Require(receipt.GjkIterations == 32, "QA-04 GJK iteration budget drifted.");
        Require(receipt.EpaIterations == 32, "QA-04 EPA iteration budget drifted.");
        Require(receipt.TerrainConservativeAdvancementIterations == 16,
            "QA-04 terrain conservative advancement iteration budget drifted.");
        Require(receipt.SequentialImpulseIterations == 12,
            "QA-04 sequential impulse iteration budget drifted.");
        Require(receipt.GroundwaterJacobiIterations == 16,
            "QA-04 groundwater Jacobi iteration budget drifted.");
        Require(receipt.InfrastructureJacobiIterations == 32,
            "QA-04 infrastructure Jacobi iteration budget drifted.");
        Require(receipt.ResidentGoapExpandedNodes == 256,
            "QA-04 Resident GOAP expansion budget drifted.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
