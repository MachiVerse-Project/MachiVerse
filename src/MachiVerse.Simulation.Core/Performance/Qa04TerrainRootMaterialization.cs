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
        var anchor = CreateAnchorValidated(tileIndex);
        var root = CreateRootValidated(tileIndex, scopeRef);
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
            CreateD3SdfSamples(origin),
            CreateD3SurfaceMaterials(origin),
            InitialRevision);
        return SpatialTerrainGeometryRecordMaterialV2.FromTerrainBrick(
            brick,
            createdStep: 0,
            detailLevel: DetailLevelV1.D3BoundarySummary);
    }

    private static int[] CreateD3SdfSamples(SpatialCellKeyV1 origin)
    {
        if (origin.Level != 3)
            throw new ArgumentException("D3 anchor origin must be level 3.", nameof(origin));
        var values = new int[TerrainBrickV1.SdfSampleCount];
        for (var z = 0; z < TerrainBrickV1.SamplesPerAxis; z++)
        for (var y = 0; y < TerrainBrickV1.SamplesPerAxis; y++)
        for (var x = 0; x < TerrainBrickV1.SamplesPerAxis; x++)
        {
            var wx = checked(((long)origin.X + x) * D3SampleSpacingMm);
            var wy = checked(((long)origin.Y + y) * D3SampleSpacingMm);
            var wz = checked(((long)origin.Z + z) * D3SampleSpacingMm);
            var sdf = checked((int)(wz - Qa04TerrainCanonicalContentSourceV1.HeightMm(wx, wy)));
            var index = checked(((z * TerrainBrickV1.SamplesPerAxis) + y) * TerrainBrickV1.SamplesPerAxis + x);
            values[index] = sdf;
        }
        return values;
    }

    private static ushort[] CreateD3SurfaceMaterials(SpatialCellKeyV1 origin)
    {
        if (origin.Level != 3)
            throw new ArgumentException("D3 anchor origin must be level 3.", nameof(origin));
        var values = new ushort[TerrainBrickV1.SurfaceMaterialCount];
        for (var z = 0; z < TerrainBrickV1.CellsPerAxis; z++)
        for (var y = 0; y < TerrainBrickV1.CellsPerAxis; y++)
        for (var x = 0; x < TerrainBrickV1.CellsPerAxis; x++)
        {
            var cx = checked((checked(2L * ((long)origin.X + x)) + 1) * D3SampleSpacingMm / 2);
            var cy = checked((checked(2L * ((long)origin.Y + y)) + 1) * D3SampleSpacingMm / 2);
            var cz = checked((checked(2L * ((long)origin.Z + z)) + 1) * D3SampleSpacingMm / 2);
            var distance = checked(cz - Qa04TerrainCanonicalContentSourceV1.HeightMm(cx, cy));
            var material = distance switch
            {
                > 0 => Qa04TerrainCanonicalContentSourceV1.MaterialVoid,
                > -500 => Qa04TerrainCanonicalContentSourceV1.MaterialSoil,
                > -2_000 => Qa04TerrainCanonicalContentSourceV1.MaterialSediment,
                _ => Qa04TerrainCanonicalContentSourceV1.MaterialRock,
            };
            var index = checked(((z * TerrainBrickV1.CellsPerAxis) + y) * TerrainBrickV1.CellsPerAxis + x);
            values[index] = material;
        }
        return values;
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
