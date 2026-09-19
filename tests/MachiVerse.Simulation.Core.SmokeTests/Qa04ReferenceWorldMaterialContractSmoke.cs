using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Performance;

internal static class Qa04ReferenceWorldMaterialContractSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04ReferenceWorldMaterialContractV1.ValidateCanonicalContract();

        Require(Qa04ReferenceWorldMaterialContractV1.Bindings.Count == 9,
            "QA-04 material contract must cover all nine canonical reference classes.");
        Require(Qa04ReferenceWorldDependencyContractV1.Blockers.Count == 0,
            "QA-04 dependency contract must be empty after full reference-world production proof.");
        Require(Qa04ReferenceWorldMaterialContractV1.AllProductionMaterializersAvailable,
            "QA-04 reference world must expose production materializers for all nine canonical classes.");

        RequireAvailable("resident.persistent-identity", "resident.identity_lifecycle");
        RequireAvailable("participation.control_mode", "participation.control_mode");
        RequireAvailable("physical.d0-presence", "physical.presence");
        RequireAvailable("environment.d0-cell-cohort", null);
        RequireAvailable("environment.d1-aggregate", null);
        RequireAvailable("society-governance.active-record", null);
        RequireAvailable("infrastructure.active-record", null);
        RequireAvailable("spatial.hot-terrain-brick", "spatial.terrain_geometry");
        RequireAvailable("transaction.active-cross-domain", null);

        var participation = Qa04ReferenceWorldMaterialContractV1.Get(new StableToken("participation.control_mode"));
        Require(participation.CanonicalCount == 1_000_000 && participation.ProductionMaterializerAvailable,
            "QA-04 Participation control-mode material binding must expose the approved 1,000,000-record production authority.");

        var societyGovernance = Qa04ReferenceWorldMaterialContractV1.Get(new StableToken("society-governance.active-record"));
        Require(societyGovernance.CanonicalCount == 2_000_000 && societyGovernance.ProductionMaterializerAvailable,
            "QA-04 Society/Governance material binding must expose the proven 2,000,000-record production authority.");

        var infrastructure = Qa04ReferenceWorldMaterialContractV1.Get(new StableToken("infrastructure.active-record"));
        Require(infrastructure.CanonicalCount == Qa04InfrastructureReferenceDecompositionV1.CanonicalCount &&
                infrastructure.ProductionMaterializerAvailable,
            "QA-04 Infrastructure material binding must expose the proven 500,000-record production authority.");

        Require(Qa04ReferenceWorldMaterialContractV1.BlockingFailureCodes.Count == 0,
            "QA-04 complete material contract must expose no blocking failure codes.");
        Require(Qa04ReferenceWorldDependencyContractV1.FailureCodes.Count == 0,
            "QA-04 complete dependency contract must expose no failure codes.");

        Qa04ReferenceWorldMaterialContractV1.RequireAllProductionMaterializersAvailable();
    }

    private static void RequireAvailable(string classToken, string? partitionId)
    {
        var binding = Qa04ReferenceWorldMaterialContractV1.Get(new StableToken(classToken));
        Require(binding.ProductionMaterializerAvailable &&
                binding.State == Qa04ReferenceMaterialBindingStateV1.ProductionMaterializerAvailable &&
                binding.PrimaryPartitionId?.Value == partitionId &&
                binding.BlockingFailureCode is null,
            $"QA-04 material binding must be available for {classToken}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
