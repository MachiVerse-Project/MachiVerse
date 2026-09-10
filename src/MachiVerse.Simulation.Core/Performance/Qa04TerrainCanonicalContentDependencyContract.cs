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
/// Terrain canonical-content authority audit beneath the single Terrain CanonicalMaterial world blocker.
/// The normative generation inputs are now implemented: hot-cell origin mapping, deterministic SDF and
/// surface material generation, surface-class vocabulary, TileScope/root identity, root connectivity,
/// and genesis revision/lineage. The parent world blocker intentionally remains until the complete
/// production-path materialization / target-kind closure / exact-103 recovery semantic-rehash evidence
/// required by phase4-alpha11-terrain-canonical-generation.md has been produced.
/// </summary>
public static class Qa04TerrainCanonicalContentDependencyContractV1
{
    public const string ParentWorldDependencyId = "spatial.terrain-geometry.root-brick-target";
    public const string ParentWorldFailureCode = "qa04.material.terrain-brick-authority-undefined";

    private static readonly IReadOnlyList<Qa04TerrainCanonicalContentDependencyV1> BlockersValue =
        Array.AsReadOnly(Array.Empty<Qa04TerrainCanonicalContentDependencyV1>());

    public static IReadOnlyList<Qa04TerrainCanonicalContentDependencyV1> Blockers => BlockersValue;
    public static IReadOnlyList<StableToken> FailureCodes =>
        BlockersValue.Select(static blocker => blocker.FailureCode).ToArray();

    public static void ValidateCanonicalContract()
    {
        Qa04ReferenceWorldDependencyContractV1.ValidateCanonicalContract();
        Qa04TerrainBrickDescriptorMaterializerV1.ValidateCanonicalContract();
        Qa04TerrainCanonicalContentSourceV1.ValidateCanonicalContract();
        Qa04TerrainRootMaterializerV1.ValidateCanonicalContract();

        if (BlockersValue.Count != 0 || FailureCodes.Count != 0)
            throw new InvalidDataException("qa04.terrain.implemented-subdependency-retained");

        ValidateFixedTerrainBoundary();
        ValidateImplementedAuthority();
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

    private static void ValidateImplementedAuthority()
    {
        if (Qa04TerrainCanonicalContentSourceV1.D0SampleSpacingMm != 250 ||
            Qa04TerrainCanonicalContentSourceV1.D0BrickWidthMm != 2_000 ||
            Qa04TerrainCanonicalContentSourceV1.SurfaceClasses.Count != 3 ||
            Qa04TerrainRootMaterializerV1.CanonicalRootCount != 4_096 ||
            Qa04TerrainRootMaterializerV1.CanonicalAnchorCount != 4_096 ||
            Qa04TerrainRootMaterializerV1.D3SampleSpacingMm != 64_000 ||
            Qa04TerrainRootMaterializerV1.InitialRevision != 1)
            throw new InvalidDataException("qa04.terrain.implemented-authority-drift");

        var scopeRef = Qa04SpatialTileScopeAuthorityV1.ScopeRef(0);
        var tile = Qa04TerrainRootMaterializerV1.MaterializeTile(0, static index => Qa04SpatialTileScopeAuthorityV1.ScopeRef(index));
        var root = tile.Root.Payload as SpatialTerrainRootPayloadV2
            ?? throw new InvalidDataException("qa04.terrain.root-payload-kind-drift");
        if (tile.ScopeRef != scopeRef ||
            root.ScopeRef != scopeRef ||
            root.RootBrickRef.RecordId != tile.Anchor.RecordId ||
            root.GeometryRevision != 1 ||
            tile.Root.Revision != 1 || tile.Root.CreatedStep != 0 || tile.Root.RetiredStep is not null || tile.Root.LineageRef is not null ||
            tile.Anchor.Revision != 1 || tile.Anchor.CreatedStep != 0 || tile.Anchor.RetiredStep is not null || tile.Anchor.LineageRef is not null)
            throw new InvalidDataException("qa04.terrain.root-authority-drift");
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
    }
}
