using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Environment;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Domains.Resident;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04AlgorithmIterationBudgetReceiptV1(
    int GjkIterations,
    int EpaIterations,
    int TerrainConservativeAdvancementIterations,
    int SequentialImpulseIterations,
    int GroundwaterJacobiIterations,
    uint InfrastructureJacobiIterations,
    uint ResidentGoapExpandedNodes);

/// <summary>
/// P4-06 が固定する標準 algorithm semantic budget と production runtime の drift を
/// fail-closed に検出する。
///
/// wall-clock pressure に応じた hidden iteration reduction を許可しないため、固定定数に加えて
/// standard Power/Water Jacobi wrapper が実際に32反復を要求することも deterministic fixture で確認する。
/// ただし実 benchmark run が各 solver path を通過したことを証明する execution coverage evidence ではない。
/// </summary>
public static class Qa04AlgorithmIterationBudgetGuardV1
{
    public const int ExpectedGjkIterations = 32;
    public const int ExpectedEpaIterations = 32;
    public const int ExpectedTerrainConservativeAdvancementIterations = 16;
    public const int ExpectedSequentialImpulseIterations = 12;
    public const int ExpectedGroundwaterJacobiIterations = 16;
    public const uint ExpectedInfrastructureJacobiIterations = 32;
    public const uint ExpectedResidentGoapExpandedNodes = 256;

    public static Qa04AlgorithmIterationBudgetReceiptV1 ValidateCanonicalContract()
    {
        if (DeterministicGjkV1.MaxIterations != ExpectedGjkIterations)
            throw new InvalidDataException("qa04.algorithm.gjk-iteration-budget-drift");
        if (DeterministicEpaV1.MaxIterations != ExpectedEpaIterations)
            throw new InvalidDataException("qa04.algorithm.epa-iteration-budget-drift");
        if (TerrainSdfConservativeAdvancementV1.MaxIterations != ExpectedTerrainConservativeAdvancementIterations)
            throw new InvalidDataException("qa04.algorithm.terrain-iteration-budget-drift");
        if (DeterministicSequentialImpulseV1.MaxIterations != ExpectedSequentialImpulseIterations)
            throw new InvalidDataException("qa04.algorithm.contact-iteration-budget-drift");
        if (GroundwaterJacobiV1.StandardIterations != ExpectedGroundwaterJacobiIterations)
            throw new InvalidDataException("qa04.algorithm.groundwater-iteration-budget-drift");
        if (DeterministicJacobiNetworkV1.MaxIterations != ExpectedInfrastructureJacobiIterations)
            throw new InvalidDataException("qa04.algorithm.infrastructure-iteration-budget-drift");
        if (ResidentGoapPlannerV1.MaxExpandedNodes != ExpectedResidentGoapExpandedNodes)
            throw new InvalidDataException("qa04.algorithm.goap-expansion-budget-drift");

        ValidateStandardInfrastructureJacobiWrappers();

        return new Qa04AlgorithmIterationBudgetReceiptV1(
            DeterministicGjkV1.MaxIterations,
            DeterministicEpaV1.MaxIterations,
            TerrainSdfConservativeAdvancementV1.MaxIterations,
            DeterministicSequentialImpulseV1.MaxIterations,
            GroundwaterJacobiV1.StandardIterations,
            DeterministicJacobiNetworkV1.MaxIterations,
            ResidentGoapPlannerV1.MaxExpandedNodes);
    }

    private static void ValidateStandardInfrastructureJacobiWrappers()
    {
        var node = new JacobiNetworkNodeV1(
            OpaqueId128.Parse("0000000000000000000000000000ab01"),
            FixedQ32_32.One,
            FixedQ32_32.One);
        var coefficients = Array.Empty<JacobiNetworkCoefficientV1>();
        var power = PowerNetworkJacobiV1.Solve([node], coefficients);
        var water = WaterNetworkJacobiV1.Solve([node], coefficients);
        if (power.Iterations != ExpectedInfrastructureJacobiIterations ||
            water.Iterations != ExpectedInfrastructureJacobiIterations)
            throw new InvalidDataException("qa04.algorithm.infrastructure-standard-wrapper-iteration-drift");
    }
}
