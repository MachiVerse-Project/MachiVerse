using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Performance;

internal static class Qa04EnvironmentMaterializationDependencyContractSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04EnvironmentMaterializationDependencyContractV1.ValidateCanonicalContract();

        Require(Qa04EnvironmentMaterializationDependencyContractV1.Blockers.Count == 4,
            "QA-04 Environment materialization dependency count drifted.");
        Require(Qa04ReferenceWorldDependencyContractV1.Blockers.Count == 6,
            "Environment subdependencies must not change the reference-world blocker count.");

        var expected = new[]
        {
            ("environment.materialization.d0-lineage-subject-binding", Qa04EnvironmentMaterializationDependencyKindV1.LineageSubjectBinding,
                "qa04.environment.d0-lineage-subject-binding-pending"),
            ("environment.materialization.d1-lineage-subject-binding", Qa04EnvironmentMaterializationDependencyKindV1.LineageSubjectBinding,
                "qa04.environment.d1-lineage-subject-binding-pending"),
            ("environment.materialization.d1-spatial-scope-binding", Qa04EnvironmentMaterializationDependencyKindV1.AggregateScopeBinding,
                "qa04.environment.d1-spatial-scope-binding-pending"),
            ("environment.materialization.tile-scope-authority", Qa04EnvironmentMaterializationDependencyKindV1.SpatialScopeAuthority,
                "qa04.environment.tile-scope-authority-pending"),
        };
        var actual = Qa04EnvironmentMaterializationDependencyContractV1.Blockers
            .Select(static blocker => (blocker.DependencyId.Value, blocker.Kind, blocker.FailureCode.Value))
            .ToArray();
        Require(actual.SequenceEqual(expected),
            "QA-04 Environment materialization dependency identity/kind/failure-code drifted.");

        var d0 = Qa04ReferenceWorldDependencyContractV1.Blockers.Single(
            static blocker => blocker.DependencyId.Value == Qa04EnvironmentMaterializationDependencyContractV1.D0ParentWorldDependencyId);
        var d1 = Qa04ReferenceWorldDependencyContractV1.Blockers.Single(
            static blocker => blocker.DependencyId.Value == Qa04EnvironmentMaterializationDependencyContractV1.D1ParentWorldDependencyId);
        Require(d0.FailureCode.Value == Qa04EnvironmentMaterializationDependencyContractV1.D0ParentWorldFailureCode,
            "Environment D0 parent blocker drifted.");
        Require(d1.FailureCode.Value == Qa04EnvironmentMaterializationDependencyContractV1.D1ParentWorldFailureCode,
            "Environment D1 parent blocker drifted.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
