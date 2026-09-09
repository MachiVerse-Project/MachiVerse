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
        Require(!Qa04ReferenceWorldMaterialContractV1.AllProductionMaterializersAvailable,
            "QA-04 reference world must remain fail-closed while material bindings are unresolved.");

        var resident = Qa04ReferenceWorldMaterialContractV1.Get(new StableToken("resident.persistent-identity"));
        Require(resident.ProductionMaterializerAvailable &&
                resident.CanonicalCount == 1_000_000 &&
                resident.PrimaryPartitionId?.Value == "resident.identity_lifecycle" &&
                resident.BlockingFailureCode is null,
            "QA-04 Resident production material binding drifted.");

        var blocked = Qa04ReferenceWorldMaterialContractV1.Bindings
            .Where(static binding => !binding.ProductionMaterializerAvailable)
            .ToArray();
        Require(blocked.Length == 7,
            "QA-04 unresolved reference class count drifted.");
        Require(blocked.All(static binding => binding.BlockingFailureCode is not null),
            "QA-04 blocked material binding must expose a stable failure code.");

        var expectedBlockers = new[]
        {
            "qa04.material.physical-presence-shape-authority-undefined",
            "qa04.material.environment-d0-partition-mapping-undefined",
            "qa04.material.environment-d1-partition-mapping-undefined",
            "qa04.material.society-governance-partition-mapping-undefined",
            "qa04.material.infrastructure-node-edge-authority-undefined",
            "qa04.material.terrain-brick-authority-undefined",
            "qa04.material.cross-domain-transaction-authority-undefined",
        };
        Require(Qa04ReferenceWorldMaterialContractV1.BlockingFailureCodes
                .Select(static code => code.Value)
                .SequenceEqual(expectedBlockers, StringComparer.Ordinal),
            "QA-04 material blocker ordering/content drifted.");

        var rejected = false;
        try
        {
            Qa04ReferenceWorldMaterialContractV1.RequireAllProductionMaterializersAvailable();
        }
        catch (InvalidDataException ex) when (
            ex.Message == "qa04.material.physical-presence-shape-authority-undefined")
        {
            rejected = true;
        }
        Require(rejected,
            "QA-04 full materialization guard must fail closed on the first unresolved canonical class.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}