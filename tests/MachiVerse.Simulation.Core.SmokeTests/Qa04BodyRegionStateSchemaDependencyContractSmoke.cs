using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Performance;

internal static class Qa04BodyRegionStateSchemaDependencyContractSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04BodyRegionStateSchemaDependencyContractV1.ValidateCanonicalContract();

        Require(Qa04BodyRegionStateSchemaDependencyContractV1.Blockers.Count == 7,
            "QA-04 BodyRegionState schema dependency count drifted.");
        Require(Qa04ReferenceWorldDependencyContractV1.Blockers.Count == 9,
            "BodyRegionState subdependencies must not change the reference-world blocker count.");

        var expected = new[]
        {
            ("body-region.schema.condition-representation", Qa04BodyRegionStateSchemaDependencyKindV1.ConditionRepresentation,
                "qa04.body-region.condition-representation-undefined"),
            ("body-region.schema.field-order", Qa04BodyRegionStateSchemaDependencyKindV1.FieldOrder,
                "qa04.body-region.field-order-undefined"),
            ("body-region.schema.field-set", Qa04BodyRegionStateSchemaDependencyKindV1.FieldSet,
                "qa04.body-region.field-set-undefined"),
            ("body-region.schema.optionality", Qa04BodyRegionStateSchemaDependencyKindV1.Optionality,
                "qa04.body-region.optionality-undefined"),
            ("body-region.schema.reference-closure", Qa04BodyRegionStateSchemaDependencyKindV1.ReferenceClosure,
                "qa04.body-region.reference-closure-undefined"),
            ("body-region.schema.region-vocabulary", Qa04BodyRegionStateSchemaDependencyKindV1.RegionVocabulary,
                "qa04.body-region.region-vocabulary-undefined"),
            ("body-region.schema.scalar-semantics", Qa04BodyRegionStateSchemaDependencyKindV1.ScalarSemantics,
                "qa04.body-region.scalar-semantics-undefined"),
        };

        var actual = Qa04BodyRegionStateSchemaDependencyContractV1.Blockers
            .Select(static blocker => (blocker.DependencyId.Value, blocker.Kind, blocker.FailureCode.Value))
            .ToArray();
        Require(actual.SequenceEqual(expected),
            "QA-04 BodyRegionState schema dependency identity/kind/failure-code drifted.");

        var parents = Qa04ReferenceWorldDependencyContractV1.Blockers
            .Where(blocker => blocker.DependencyId.Value == Qa04BodyRegionStateSchemaDependencyContractV1.ParentWorldDependencyId)
            .ToArray();
        Require(parents.Length == 1 &&
                parents[0].FailureCode.Value == Qa04BodyRegionStateSchemaDependencyContractV1.ParentWorldFailureCode,
            "QA-04 BodyRegionState must remain represented by exactly one compatibility world blocker.");

        var worldCodes = Qa04ReferenceWorldDependencyContractV1.FailureCodes
            .Select(static code => code.Value)
            .ToHashSet(StringComparer.Ordinal);
        Require(Qa04BodyRegionStateSchemaDependencyContractV1.FailureCodes
                .Select(static code => code.Value)
                .All(code => !worldCodes.Contains(code)),
            "BodyRegionState subdependency diagnostics must remain distinct from reference-world blocker codes.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
