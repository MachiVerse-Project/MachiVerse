using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04TerrainTileAuthorityV1(
    ushort TileIndex,
    PartitionRecordRefV1 ScopeRef,
    SpatialTerrainGeometryRecordMaterialV2 Root,
    SpatialTerrainGeometryRecordMaterialV2 Anchor);

/// <summary>
/// Canonical perf.reference.v1 terrain root/D3-anchor materializer.
/// TileScope identity remains an explicit external Spatial authority input; this type owns only the
/// Terrain identities/content whose Step0 derivation is fixed by the normative terrain generation spec.
/// </summary>
public static class Qa04TerrainRootMaterializerV1
{
    public const ulong CanonicalRootCount = 4_096;
    public const ulong CanonicalAnchorCount = 4_096;
    public const uint D3SampleSpacingMm = 64_000;
    public const long D3BrickWidthMm = 512_000;
    public const ulong InitialRevision = 1;

    private static readonly StableToken SpatialDomain = new("spatial");
    private static readonly StableToken RootCreationKind = new("perf.terrain-root");
    private static readonly StableToken AnchorCreationKind = new("perf.terrain-root-brick");

    public static void ValidateCanonicalContract()
    {
        Qa04ReferenceLoadV1.ValidateCanonicalContract();
        Qa04TerrainCanonicalContentSourceV1.ValidateCanonicalContract();
        SpatialTerrainGeometryRecordSchemaV2.ValidateCanonicalContract();

        if (CanonicalRootCount != Qa04ReferenceLoadV1.RegionalTileCount ||
            CanonicalAnchorCount != Qa04ReferenceLoadV1.RegionalTileCount ||
            D3SampleSpacingMm != 64_000 ||
            D3BrickWidthMm != Qa04TerrainCanonicalContentSourceV1.TileWidthMm ||
            checked((long)D3SampleSpacingMm * TerrainBrickV1.CellsPerAxis) != D3BrickWidthMm ||
            InitialRevision != 1)
            throw new InvalidDataException("qa04.terrain.root-contract-drift");
    }

    public static OpaqueId128 RootId(ushort tileIndex)
        => DeriveTileId(tileIndex, RootCreationKind);

    public static OpaqueId128 AnchorId(ushort tileIndex)
        => DeriveTileId(tileIndex, AnchorCreationKind);

    public static Qa04TerrainTileAuthorityV1 MaterializeTile(
        ushort tileIndex,
        Func<ushort, PartitionRecordRefV1> tileScopeForTile)
    {
        ArgumentNullException.ThrowIfNull(tileScopeForTile);
        ValidateCanonicalContract();
        if (tileIndex >= Qa04ReferenceLoadV1.RegionalTileCount)
            throw new ArgumentOutOfRangeException(nameof(tileIndex));

        var scopeRef = tileScopeForTile(tileIndex);
        ValidateScopeRef(scopeRef);
        var anchor = CreateAnchor(tileIndex);
        var root = CreateRoot(tileIndex, scopeRef);
        return new Qa04TerrainTileAuthorityV1(tileIndex, scopeRef, root, anchor);
    }

    public static IEnumerable<SpatialTerrainGeometryRecordMaterialV2> MaterializeCanonical(
        Func<ushort, PartitionRecordRefV1> tileScopeForTile)
    {
        ArgumentNullException.ThrowIfNull(tileScopeForTile);
        ValidateCanonicalContract();
        for (ushort tile = 0; tile < Qa04ReferenceLoadV1.RegionalTileCount; tile++)
        {
            var material = MaterializeTileValidated(tile, tileScopeForTile);
            yield return material.Root;
            yield return material.Anchor;
        }
    }

    public static SpatialTerrainGeometryRecordMaterialV2 CreateAnchor(ushort tileIndex)
    {
        ValidateCanonicalContract();
        if (tileIndex >= Qa04ReferenceLoadV1.RegionalTileCount)
            throw new ArgumentOutOfRangeException(nameof(tileIndex));
        return CreateAnchorValidated(tileIndex);
    }

    public static SpatialTerrainGeometryRecordMaterialV2 CreateRoot(
        ushort tileIndex,
        PartitionRecordRefV1 scopeRef)
    {
        ValidateCanonicalContract();
        if (tileIndex >= Qa04ReferenceLoadV1.RegionalTileCount)
            throw new ArgumentOutOfRangeException(nameof(tileIndex));
        ValidateScopeRef(scopeRef);
        return CreateRootValidated(tileIndex, scopeRef);
    }

    private static Qa04TerrainTileAuthorityV1 MaterializeTileValidated(
        ushort tileIndex,
        Func<ushort, PartitionRecordRefV1> tileScopeForTile)
    {
        var scopeRef = tileScopeForTile(tileIndex);
        ValidateScopeRef(scopeRef);
        return new Qa04TerrainTileAuthorityV1(
            tileIndex,
            scopeRef,
            CreateRootValidated(tileIndex, scopeRef),
            CreateAnchorValidated(tileIndex));
    }

    private static SpatialTerrainGeometryRecordMaterialV2 CreateAnchorValidated(ushort tileIndex)
    {
        var row = tileIndex / Qa04ReferenceLoadV1.RegionalTileColumns;
        var column = tileIndex % Qa04ReferenceLoadV1.RegionalTileColumns;
        var centerXmm = checked((long)column * D3BrickWidthMm + D3BrickWidthMm / 2);
        var centerYmm = checked((long)row * D3BrickWidthMm + D3BrickWidthMm / 2);
        var brickZ = Qa04TerrainCanonicalContentSourceV1.FloorDiv(
            Qa04TerrainCanonicalContentSourceV1.HeightMm(centerXmm, centerYmm),
            D3BrickWidthMm);
        var origin = new SpatialCellKeyV1(
            3,
            checked(column * TerrainBrickV1.CellsPerAxis),
            checked(row * TerrainBrickV1.CellsPerAxis),
            checked((int)(brickZ * TerrainBrickV1.CellsPerAxis)));
        var brick = new TerrainBrickV1(
            AnchorId(tileIndex),
            level: 3,
            origin,
            D3SampleSpacingMm,
            Qa04TerrainCanonicalContentSourceV1.CreateSdfSamples(origin, D3SampleSpacingMm),
            Qa04TerrainCanonicalContentSourceV1.CreateSurfaceMaterials(origin, D3SampleSpacingMm),
            InitialRevision);
        return SpatialTerrainGeometryRecordMaterialV2.FromTerrainBrick(
            brick,
            createdStep: 0,
            detailLevel: DetailLevelV1.D3BoundarySummary);
    }

    private static SpatialTerrainGeometryRecordMaterialV2 CreateRootValidated(
        ushort tileIndex,
        PartitionRecordRefV1 scopeRef)
    {
        var connectivity = NeighborTiles(tileIndex)
            .Select(static neighbor => new PartitionRecordRefV1(
                SpatialTerrainGeometryRecordSchemaV2.PartitionId,
                RootId(neighbor)))
            .OrderBy(static reference => reference.PartitionId.Value, StringComparer.Ordinal)
            .ThenBy(static reference => reference.RecordId)
            .ToArray();

        return new SpatialTerrainGeometryRecordMaterialV2(
            RootId(tileIndex),
            revision: InitialRevision,
            createdStep: 0,
            retiredStep: null,
            detailLevel: DetailLevelV1.D3BoundarySummary,
            lineageRef: null,
            new SpatialTerrainRootPayloadV2(
                scopeRef,
                new PartitionRecordRefV1(SpatialTerrainGeometryRecordSchemaV2.PartitionId, AnchorId(tileIndex)),
                geometryRevision: InitialRevision,
                Qa04TerrainCanonicalContentSourceV1.SurfaceClasses,
                connectivity,
                archiveAnchor: null));
    }

    private static IEnumerable<ushort> NeighborTiles(ushort tileIndex)
    {
        var row = tileIndex / Qa04ReferenceLoadV1.RegionalTileColumns;
        var column = tileIndex % Qa04ReferenceLoadV1.RegionalTileColumns;
        if (row > 0) yield return checked((ushort)(tileIndex - Qa04ReferenceLoadV1.RegionalTileColumns));
        if (column + 1 < Qa04ReferenceLoadV1.RegionalTileColumns) yield return checked((ushort)(tileIndex + 1));
        if (row + 1 < Qa04ReferenceLoadV1.RegionalTileRows) yield return checked((ushort)(tileIndex + Qa04ReferenceLoadV1.RegionalTileColumns));
        if (column > 0) yield return checked((ushort)(tileIndex - 1));
    }

    private static OpaqueId128 DeriveTileId(ushort tileIndex, StableToken creationKind)
    {
        if (tileIndex >= Qa04ReferenceLoadV1.RegionalTileCount)
            throw new ArgumentOutOfRangeException(nameof(tileIndex));
        return DerivedIdentity.DeriveEntityId(
            Qa04ReferenceLoadV1.WorldId,
            creationStep: 0,
            SpatialDomain,
            OpaqueId128.Zero,
            creationKind,
            tileIndex);
    }

    private static void ValidateScopeRef(PartitionRecordRefV1 scopeRef)
    {
        if (scopeRef.PartitionId.Value != SpatialScopeRegistryPayloadV1.PartitionId || scopeRef.RecordId.IsZero)
            throw new InvalidDataException("qa04.terrain.root-tile-scope-ref-invalid");
    }
}
