using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Performance;

internal static class Qa04TerrainCanonicalContentDependencyContractSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04TerrainCanonicalContentDependencyContractV1.ValidateCanonicalContract();

        Require(Qa04TerrainCanonicalContentDependencyContractV1.Blockers.Count == 0,
            "Implemented Terrain canonical generation inputs must expose no unresolved subdependencies.");
        Require(Qa04TerrainCanonicalContentDependencyContractV1.FailureCodes.Count == 0,
            "Implemented Terrain canonical generation inputs must expose no unresolved failure codes.");
        Require(Qa04ReferenceWorldDependencyContractV1.Blockers.Count == 2,
            "Full Terrain production proof must leave only Society/Governance and Infrastructure world blockers.");
        Require(Qa04TerrainBrickDescriptorMaterializerV1.InitialRecordRevision == 1,
            "Common Domain initial revision must remain fixed for Terrain content binding.");

        var terrainParents = Qa04ReferenceWorldDependencyContractV1.Blockers
            .Where(blocker =>
                blocker.DependencyId.Value == Qa04TerrainCanonicalContentDependencyContractV1.ParentWorldDependencyId ||
                blocker.FailureCode.Value == Qa04TerrainCanonicalContentDependencyContractV1.ParentWorldFailureCode)
            .ToArray();
        Require(terrainParents.Length == 0,
            "QA-04 Terrain compatibility world blocker must be absent after full production Snapshot/recovery proof.");

        Require(Qa04TerrainCanonicalContentSourceV1.SurfaceClasses
                .Select(static value => value.Value)
                .SequenceEqual(new[] { "terrain.rock", "terrain.sediment", "terrain.soil" }, StringComparer.Ordinal),
            "Terrain canonical surface-class vocabulary drifted.");
        Require(Qa04TerrainRootMaterializerV1.CanonicalRootCount == 4_096 &&
                Qa04TerrainRootMaterializerV1.CanonicalAnchorCount == 4_096,
            "Terrain root/anchor canonical cardinality drifted.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
