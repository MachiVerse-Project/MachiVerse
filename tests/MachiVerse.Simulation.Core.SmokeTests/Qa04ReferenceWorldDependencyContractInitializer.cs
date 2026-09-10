using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Performance;

internal static class Qa04ReferenceWorldDependencyContractInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        Qa04ReferenceWorldDependencyContractV1.ValidateCanonicalContract();
        Require(Qa04ReferenceWorldDependencyContractV1.Blockers.Count == 6,
            "QA-04 unresolved dependency contract count drifted.");
        Require(Qa04ReferenceWorldDependencyContractV1.FailureCodes.All(
                static code => code.Value != "qa04.material.market-ref-authority-undefined"),
            "QA-04 dependency contract must not retain the resolved market_ref authority blocker.");
        Require(Qa04ReferenceWorldDependencyContractV1.FailureCodes.All(
                static code => code.Value != "qa04.material.physical-presence-shape-authority-undefined"),
            "QA-04 dependency contract must not retain the resolved physical shape authority blocker.");
        Require(Qa04ReferenceWorldDependencyContractV1.FailureCodes.All(
                static code => code.Value != "qa04.material.rule-ast-schema-undefined"),
            "QA-04 dependency contract must not retain the resolved RuleAst schema blocker.");
        Require(Qa04ReferenceWorldDependencyContractV1.FailureCodes.All(
                static code => code.Value != "qa04.material.perceived-fact-schema-undefined"),
            "QA-04 dependency contract must not retain the resolved perceived-fact schema blocker.");
        Require(Qa04ReferenceWorldDependencyContractV1.FailureCodes.All(
                static code => code.Value != "qa04.material.body-region-state-schema-undefined"),
            "QA-04 dependency contract must not retain the resolved BodyRegionState schema blocker.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
