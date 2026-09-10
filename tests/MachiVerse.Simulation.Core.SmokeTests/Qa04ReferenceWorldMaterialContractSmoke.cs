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
        Require(Qa04ReferenceWorldDependencyContractV1.Blockers.Count == 7,
            "QA-04 dependency contract must remain a separate seven-blocker normative gate.");
        Require(!Qa04ReferenceWorldMaterialContractV1.AllProductionMaterializersAvailable,
            "QA-04 reference world must remain fail-closed while material bindings are unresolved.");

        var resident = Qa04ReferenceWorldMaterialContractV1.Get(new StableToken("resident.persistent-identity"));
        Require(resident.ProductionMaterializerAvailable &&
                resident.CanonicalCount == 1_000_000 &&
                resident.PrimaryPartitionId?.Value == "resident.identity_lifecycle" &&
                resident.BlockingFailureCode is null,
            "QA-04 Resident production material binding drifted.");

        var physical = Qa04ReferenceWorldMaterialContractV1.Get(new StableToken("physical.d0-presence"));
        Require(physical.ProductionMaterializerAvailable &&
                physical.CanonicalCount == Qa04PhysicalD0MaterializerV1.CanonicalPhysicalCount &&
                physical.PrimaryPartitionId?.Value == "physical.presence" &&
                physical.BlockingFailureCode is null,
            "QA-04 Physical D0 production material binding drifted.");

        RequireState("environment.d0-cell-cohort", Qa04ReferenceMaterialBindingStateV1.BlockedByPartitionMapping);
        RequireState("environment.d1-aggregate", Qa04ReferenceMaterialBindingStateV1.BlockedByPartitionMapping);
        RequireState("society-governance.active-record", Qa04ReferenceMaterialBindingStateV1.BlockedByPartitionMapping);
        RequireState("infrastructure.active-record", Qa04ReferenceMaterialBindingStateV1.BlockedByRecordSchema);
        RequireState("spatial.hot-terrain-brick", Qa04ReferenceMaterialBindingStateV1.BlockedByCanonicalMaterial);
        RequireState("transaction.active-cross-domain", Qa04ReferenceMaterialBindingStateV1.BlockedByPersistentAuthority);

        var blocked = Qa04ReferenceWorldMaterialContractV1.Bindings
            .Where(static binding => !binding.ProductionMaterializerAvailable)
            .ToArray();
        Require(blocked.Length == 6,
            "QA-04 unresolved reference class count drifted.");
        Require(blocked.All(static binding => binding.BlockingFailureCode is not null),
            "QA-04 blocked material binding must expose a stable failure code.");

        var expectedBlockers = new[]
        {
            "qa04.material.environment-d0-partition-mapping-undefined",
            "qa04.material.environment-d1-partition-mapping-undefined",
            "qa04.material.society-governance-partition-mapping-undefined",
            "qa04.material.infrastructure-node-edge-authority-undefined",
            "qa04.material.terrain-brick-authority-undefined",
            "qa04.material.cross-domain-transaction-authority-undefined",
        };
        var materialBlockerCodes = Qa04ReferenceWorldMaterialContractV1.BlockingFailureCodes
            .Select(static code => code.Value)
            .ToArray();
        Require(materialBlockerCodes.SequenceEqual(expectedBlockers, StringComparer.Ordinal),
            "QA-04 material blocker ordering/content drifted.");

        var dependencyCodes = Qa04ReferenceWorldDependencyContractV1.FailureCodes
            .Select(static code => code.Value)
            .ToHashSet(StringComparer.Ordinal);
        Require(materialBlockerCodes.All(dependencyCodes.Contains),
            "Every blocked top-level material class must be backed by a dependency-contract failure code.");

        var dependencyOnly = dependencyCodes
            .Except(materialBlockerCodes, StringComparer.Ordinal)
            .OrderBy(static code => code, StringComparer.Ordinal)
            .ToArray();
        Require(dependencyOnly.SequenceEqual(new[]
            {
                "qa04.material.market-ref-authority-undefined",
            }, StringComparer.Ordinal),
            "QA-04 dependency-only blockers must remain distinct from the eight top-level material classes.");

        var rejected = false;
        try
        {
            Qa04ReferenceWorldMaterialContractV1.RequireAllProductionMaterializersAvailable();
        }
        catch (InvalidDataException ex) when (
            ex.Message == "qa04.material.environment-d0-partition-mapping-undefined")
        {
            rejected = true;
        }
        Require(rejected,
            "QA-04 full materialization guard must fail closed on the first unresolved canonical class.");
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
