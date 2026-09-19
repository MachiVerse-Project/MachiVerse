using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Performance;

internal static class Qa04ReferenceWorldDependencyContractInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        Qa04ReferenceWorldDependencyContractV1.ValidateCanonicalContract();
        Require(Qa04ReferenceWorldDependencyContractV1.Blockers.Count == 0,
            "QA-04 unresolved dependency contract must be empty after full reference-world production proof.");
        Require(Qa04ReferenceWorldDependencyContractV1.FailureCodes.Count == 0,
            "QA-04 unresolved dependency failure-code set must be empty after full reference-world production proof.");

        var resolved = new[]
        {
            "qa04.material.market-ref-authority-undefined",
            "qa04.material.physical-presence-shape-authority-undefined",
            "qa04.material.rule-ast-schema-undefined",
            "qa04.material.perceived-fact-schema-undefined",
            "qa04.material.body-region-state-schema-undefined",
            "qa04.material.environment-d0-partition-mapping-undefined",
            "qa04.material.environment-d1-partition-mapping-undefined",
            "qa04.material.cross-domain-transaction-authority-undefined",
            "qa04.material.terrain-brick-authority-undefined",
            "qa04.material.society-governance-partition-mapping-undefined",
            "qa04.material.infrastructure-node-edge-authority-undefined",
        };
        Require(Qa04ReferenceWorldDependencyContractV1.FailureCodes.All(
                code => !resolved.Contains(code.Value, StringComparer.Ordinal)),
            "QA-04 dependency contract must not retain any resolved reference-world blocker.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
