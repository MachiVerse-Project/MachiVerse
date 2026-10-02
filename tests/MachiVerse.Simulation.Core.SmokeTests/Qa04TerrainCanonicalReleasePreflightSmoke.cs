using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Performance;

internal static class Qa04TerrainCanonicalReleasePreflightSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04TerrainCanonicalContentSourceV1.ValidateCanonicalContract();
        Qa04TerrainBrickDescriptorMaterializerV1.ValidateCanonicalContract();
        Qa04TerrainRootMaterializerV1.ValidateCanonicalContract();
        Qa04SpatialTileScopeAuthorityV1.ValidateCanonicalContract();

        Require(Qa04ReferenceWorldDependencyContractV1.Blockers.All(static blocker =>
                blocker.DependencyId.Value != "spatial.terrain-geometry.root-brick-target" &&
                blocker.FailureCode.Value != "qa04.material.terrain-brick-authority-undefined"),
            "Terrain parent world blocker must remain released after full production Snapshot/recovery proof.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
