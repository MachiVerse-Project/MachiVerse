using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;

namespace MachiVerse.Simulation.Core.Performance;

public enum Qa04TerrainCanonicalContentDependencyKindV1 : byte
{
    SpatialMapping = 1,
    SampleGeneration = 2,
    SurfaceMaterialGeneration = 3,
    SurfaceMaterialVocabulary = 4,
    RootClosure = 5,
    Lifecycle = 6,
}

public sealed record Qa04TerrainCanonicalContentDependencyV1(
    StableToken DependencyId,
    Qa04TerrainCanonicalContentDependencyKindV1 Kind,
    StableToken FailureCode);

/// <summary>
/// Fail-closed audit of the unresolved normative inputs required to turn the already-canonical
/// perf.reference.v1 hot-Terrain descriptors into canonical Terrain v2 content.
///
/// This is a subdependency contract beneath the single Terrain CanonicalMaterial blocker in
/// Qa04ReferenceWorldDependencyContractV1. It intentionally does not add seven world blockers or
/// replace the compatibility failure code qa04.material.terrain-brick-authority-undefined.
/// Existing SBO-SDF shape, descriptor identity/count, D0 spacing, common initial record revision,
/// migration, Snapshot, and recovery contracts are treated as fixed. Only missing benchmark
/// content-generation/closure rules appear here; no synthetic smoke value is promoted into release
/// material.
/// </summary>
public static class Qa04TerrainCanonicalContentDependencyContractV1
{
    public const string ParentWorldDependencyId = "spatial.terrain-geometry.root-brick-target";
    public const string ParentWorldFailureCode = "qa04.material.terrain-brick-authority-undefined";

    private static readonly IReadOnlyList<Qa04TerrainCanonicalContentDependencyV1> BlockersValue = Array.AsReadOnly(new[]
    {
        Blocker(
            "terrain.content.cell-origin-mapping",
            Qa04TerrainCanonicalContentDependencyKindV1.SpatialMapping,
            "qa04.terrain.cell-origin-mapping-undefined"),
        Blocker(
            "terrain.content.sdf-sample-generation",
            Qa04TerrainCanonicalContentDependencyKindV1.SampleGeneration,
            "qa04.terrain.sdf-generation-undefined"),
        Blocker(
            "terrain.content.surface-material-generation",
            Qa04TerrainCanonicalContentDependencyKindV1.SurfaceMaterialGeneration,
            "qa04.terrain.surface-material-generation-undefined"),
        Blocker(
            "terrain.content.surface-class-vocabulary",
            Qa04TerrainCanonicalContentDependencyKindV1.SurfaceMaterialVocabulary,
            "qa04.terrain.surface-class-tokens-undefined"),
        Blocker(
            "terrain.content.root-scope-identity",
            Qa04TerrainCanonicalContentDependencyKindV1.RootClosure,
            "qa04.terrain.root-scope-identity-undefined"),
        Blocker(
            "terrain.content.root-topology-connectivity",
            Qa04TerrainCanonicalContentDependencyKindV1.RootClosure,
            "qa04.terrain.root-topology-connectivity-undefined"),
        Blocker(
            "terrain.content.geometry-revision-lineage",
            Qa04TerrainCanonicalContentDependencyKindV1.Lifecycle,
            "qa04.terrain.geometry-revision-lineage-undefined"),
    }
    .OrderBy(static blocker => blocker.DependencyId.Value, StringComparer.Ordinal)
    .ToArray());

    public static IReadOnlyList<Qa04TerrainCanonicalContentDependencyV1> Blockers => BlockersValue;

    public static IReadOnlyList<StableToken> FailureCodes
        => BlockersValue.Select(static blocker => blocker.FailureCode).ToArray();

    public static void ValidateCanonicalContract()
    {
        Qa04ReferenceWorldDependencyContractV1.ValidateCanonicalContract();
        Qa04TerrainBrickDescriptorMaterializerV1.ValidateCanonicalContract();

        if (BlockersValue.Count != 7)
            throw new InvalidDataException("qa04.terrain.dependency-blocker-count-drift");
        if (BlockersValue.Select(static blocker => blocker.DependencyId).Distinct().Count() != BlockersValue.Count)
            throw new InvalidDataException("qa04.terrain.dependency-blocker-id-duplicate");
        if (BlockersValue.Select(static blocker => blocker.FailureCode).Distinct().Count() != BlockersValue.Count)
            throw new InvalidDataException("qa04.terrain.dependency-blocker-code-duplicate");
        if (BlockersValue.Any(static blocker => !Enum.IsDefined(blocker.Kind)))
            throw new InvalidDataException("qa04.terrain.dependency-blocker-kind-invalid");

        var ordered = BlockersValue.Select(static blocker => blocker.DependencyId.Value).ToArray();
        if (!ordered.SequenceEqual(ordered.OrderBy(static value => value, StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException("qa04.terrain.dependency-blocker-order");

        ValidateFixedTerrainBoundary();
        ValidateParentWorldBlocker();
    }

    private static void ValidateFixedTerrainBoundary()
    {
        if (Qa04TerrainBrickDescriptorMaterializerV1.CanonicalTerrainBrickCount != 500_000)
            throw new InvalidDataException("qa04.terrain.canonical-brick-count-drift");
        if (Qa04TerrainBrickDescriptorMaterializerV1.D0SampleSpacingMm != 250)
            throw new InvalidDataException("qa04.terrain.d0-spacing-drift");
        if (Qa04TerrainBrickDescriptorMaterializerV1.InitialRecordRevision != 1)
            throw new InvalidDataException("qa04.terrain.initial-record-revision-drift");
        if (Qa04ReferenceLoadV1.RegionalTileRows != 64 ||
            Qa04ReferenceLoadV1.RegionalTileColumns != 64 ||
            Qa04ReferenceLoadV1.RegionalTileCount != 4_096)
            throw new InvalidDataException("qa04.terrain.regional-tile-contract-drift");
        if (TerrainBrickV1.SdfSampleCount != 729 || TerrainBrickV1.SurfaceMaterialCount != 512)
            throw new InvalidDataException("qa04.terrain.brick-cardinality-drift");
    }

    private static void ValidateParentWorldBlocker()
    {
        var parent = Qa04ReferenceWorldDependencyContractV1.Blockers.SingleOrDefault(
            static blocker => blocker.DependencyId.Value == ParentWorldDependencyId)
            ?? throw new InvalidDataException("qa04.terrain.parent-world-blocker-missing");

        if (parent.Kind != Qa04ReferenceDependencyBlockerKindV1.CanonicalMaterial ||
            parent.PartitionId?.Value != "spatial.terrain_geometry" ||
            !string.Equals(parent.FieldName, "root_brick_ref", StringComparison.Ordinal) ||
            parent.FailureCode.Value != ParentWorldFailureCode)
            throw new InvalidDataException("qa04.terrain.parent-world-blocker-drift");

        var worldCodes = Qa04ReferenceWorldDependencyContractV1.FailureCodes
            .Select(static code => code.Value)
            .ToHashSet(StringComparer.Ordinal);
        if (FailureCodes.Any(code => worldCodes.Contains(code.Value)))
            throw new InvalidDataException("qa04.terrain.subdependency-code-collides-with-world-blocker");
    }

    private static Qa04TerrainCanonicalContentDependencyV1 Blocker(
        string dependencyId,
        Qa04TerrainCanonicalContentDependencyKindV1 kind,
        string failureCode)
        => new(new StableToken(dependencyId), kind, new StableToken(failureCode));
}
