using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Performance;

internal static class Qa04TerrainCanonicalContentDependencyContractSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04TerrainCanonicalContentDependencyContractV1.ValidateCanonicalContract();

        Require(Qa04TerrainCanonicalContentDependencyContractV1.Blockers.Count == 7,
            "QA-04 Terrain canonical content dependency count drifted.");
        Require(Qa04ReferenceWorldDependencyContractV1.Blockers.Count == 6,
            "Terrain subdependencies must not change the reference-world blocker count.");
        Require(Qa04TerrainBrickDescriptorMaterializerV1.InitialRecordRevision == 1,
            "Common Domain initial revision must already be fixed before Terrain content binding.");

        var expected = new[]
        {
            ("terrain.content.cell-origin-mapping", Qa04TerrainCanonicalContentDependencyKindV1.SpatialMapping,
                "qa04.terrain.cell-origin-mapping-undefined"),
            ("terrain.content.geometry-revision-lineage", Qa04TerrainCanonicalContentDependencyKindV1.Lifecycle,
                "qa04.terrain.geometry-revision-lineage-undefined"),
            ("terrain.content.root-scope-identity", Qa04TerrainCanonicalContentDependencyKindV1.RootClosure,
                "qa04.terrain.root-scope-identity-undefined"),
            ("terrain.content.root-topology-connectivity", Qa04TerrainCanonicalContentDependencyKindV1.RootClosure,
                "qa04.terrain.root-topology-connectivity-undefined"),
            ("terrain.content.sdf-sample-generation", Qa04TerrainCanonicalContentDependencyKindV1.SampleGeneration,
                "qa04.terrain.sdf-generation-undefined"),
            ("terrain.content.surface-class-vocabulary", Qa04TerrainCanonicalContentDependencyKindV1.SurfaceMaterialVocabulary,
                "qa04.terrain.surface-class-tokens-undefined"),
            ("terrain.content.surface-material-generation", Qa04TerrainCanonicalContentDependencyKindV1.SurfaceMaterialGeneration,
                "qa04.terrain.surface-material-generation-undefined"),
        };

        var actual = Qa04TerrainCanonicalContentDependencyContractV1.Blockers
            .Select(static blocker => (blocker.DependencyId.Value, blocker.Kind, blocker.FailureCode.Value))
            .ToArray();
        Require(actual.SequenceEqual(expected),
            "QA-04 Terrain canonical content dependency identity/kind/failure-code drifted.");

        var terrainParents = Qa04ReferenceWorldDependencyContractV1.Blockers
            .Where(blocker => blocker.DependencyId.Value == Qa04TerrainCanonicalContentDependencyContractV1.ParentWorldDependencyId)
            .ToArray();
        Require(terrainParents.Length == 1 &&
                terrainParents[0].FailureCode.Value == Qa04TerrainCanonicalContentDependencyContractV1.ParentWorldFailureCode,
            "QA-04 Terrain must remain represented by exactly one compatibility world blocker.");

        var worldCodes = Qa04ReferenceWorldDependencyContractV1.FailureCodes
            .Select(static code => code.Value)
            .ToHashSet(StringComparer.Ordinal);
        Require(Qa04TerrainCanonicalContentDependencyContractV1.FailureCodes
                .Select(static code => code.Value)
                .All(code => !worldCodes.Contains(code)),
            "Terrain subdependency diagnostics must remain distinct from reference-world blocker codes.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
