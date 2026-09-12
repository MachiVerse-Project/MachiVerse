using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
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

        var node = new JacobiNetworkNodeV1(
            OpaqueId128.Parse("0000000000000000000000000000ab01"),
            FixedQ32_32.One,
            FixedQ32_32.One);
        var power = PowerNetworkJacobiV1.Solve(
            [node],
            Array.Empty<JacobiNetworkCoefficientV1>());
        var water = WaterNetworkJacobiV1.Solve(
            [node],
            Array.Empty<JacobiNetworkCoefficientV1>());
        Require(power.Iterations == 32 && water.Iterations == 32,
            "QA-04 standard power/water Jacobi wrappers must execute the fixed 32-iteration contract.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
