using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Performance;

internal static class Qa04TerrainCanonicalReleasePreflightSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        var evidence = Qa04TerrainCanonicalReleasePreflightV1.Build();

        Require(evidence.HotDescriptorCount == 500_000 && evidence.UniqueHotOriginCount == 500_000,
            "Terrain release preflight must cover all 500,000 hot descriptors with unique D0 origins.");
        Require(evidence.ProbeBrickCount >= 900,
            "Terrain release preflight must exercise canonical SDF/material generation across the descriptor range.");
        Require(evidence.RootCount == 4_096 && evidence.AnchorIdentityCount == 4_096,
            "Terrain release preflight must close all canonical root and D3 anchor identities.");
        Require(evidence.SharedFacePairCount >= 0,
            "Terrain release preflight shared-face evidence count must not be negative.");

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
