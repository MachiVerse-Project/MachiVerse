using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Performance;

internal static class Qa04ReferenceWorldMaterialContractSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04ReferenceWorldMaterialContractV1.ValidateCanonicalContract();

        Require(Qa04ReferenceWorldMaterialContractV1.Bindings.Count == 8,
            "QA-04 material contract must cover all eight canonical reference classes.");
        Require(Qa04ReferenceWorldDependencyContractV1.Blockers.Count == 2,
            "QA-04 dependency contract must expose only Society/Governance and Infrastructure blockers.");
        Require(!Qa04ReferenceWorldMaterialContractV1.AllProductionMaterializersAvailable,
            "QA-04 reference world must remain fail-closed while Society/Governance and Infrastructure are unresolved.");

        RequireAvailable("resident.persistent-identity", "resident.identity_lifecycle");
        RequireAvailable("physical.d0-presence", "physical.presence");
        RequireAvailable("environment.d0-cell-cohort", null);
        RequireAvailable("environment.d1-aggregate", null);
        RequireState("society-governance.active-record", Qa04ReferenceMaterialBindingStateV1.BlockedByPartitionMapping);
        RequireState("infrastructure.active-record", Qa04ReferenceMaterialBindingStateV1.BlockedByRecordSchema);
        RequireAvailable("spatial.hot-terrain-brick", "spatial.terrain_geometry");
        RequireAvailable("transaction.active-cross-domain", null);

        var blocked = Qa04ReferenceWorldMaterialContractV1.Bindings
            .Where(static binding => !binding.ProductionMaterializerAvailable)
            .ToArray();
        Require(blocked.Length == 2,
            "QA-04 unresolved reference class count drifted.");
        Require(blocked.All(static binding => binding.BlockingFailureCode is not null),
            "QA-04 blocked material binding must expose a stable failure code.");

        var expectedBlockers = new[]
        {
            "qa04.material.society-governance-partition-mapping-undefined",
            "qa04.material.infrastructure-node-edge-authority-undefined",
        };
        var materialBlockerCodes = Qa04ReferenceWorldMaterialContractV1.BlockingFailureCodes
            .Select(static code => code.Value)
            .ToArray();
        Require(materialBlockerCodes.SequenceEqual(expectedBlockers, StringComparer.Ordinal),
            "QA-04 material blocker ordering/content drifted.");

        var dependencyCodes = Qa04ReferenceWorldDependencyContractV1.FailureCodes
            .Select(static code => code.Value)
            .ToArray();
        Require(dependencyCodes.SequenceEqual(expectedBlockers.OrderBy(static code => code, StringComparer.Ordinal), StringComparer.Ordinal),
            "QA-04 material/dependency blocker sets must remain identical after Terrain release.");

        var rejected = false;
        try
        {
            Qa04ReferenceWorldMaterialContractV1.RequireAllProductionMaterializersAvailable();
        }
        catch (InvalidDataException ex) when (
            ex.Message == "qa04.material.society-governance-partition-mapping-undefined")
        {
            rejected = true;
        }
        Require(rejected,
            "QA-04 full materialization guard must fail closed on the first unresolved canonical class.");
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

    private static void RequireState(string classToken, Qa04ReferenceMaterialBindingStateV1 expected)
    {
        var binding = Qa04ReferenceWorldMaterialContractV1.Get(new StableToken(classToken));
        Require(!binding.ProductionMaterializerAvailable && binding.State == expected,
            $"QA-04 material binding state drifted for {classToken}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
