using System.Buffers.Binary;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Canonical perf.reference.v1 Terrain content source defined by
/// phase4-alpha11-terrain-canonical-generation.md. It materializes the existing 500,000 hot D0
/// descriptor identities into deterministic cell origins, SDF samples, and surface materials.
/// Root/scope authority is intentionally separate so the Spatial ownership cycle can be closed by
/// the production scope/root materializer rather than synthesized here.
/// </summary>
public sealed class Qa04TerrainCanonicalContentSourceV1 : IQa04TerrainBrickContentSourceV1
{
    public const long TileWidthMm = 512_000;
    public const int HotSlotsPerAxis = 256;
    public const int HotSlotsPerTile = HotSlotsPerAxis * HotSlotsPerAxis;
    public const int HotSlotPermutationMultiplier = 40_503;
    public const int HotSlotTileOffsetMultiplier = 17;
    public const uint D0SampleSpacingMm = 250;
    public const long D0BrickWidthMm = 2_000;

    public const ushort MaterialVoid = 0;
    public const ushort MaterialSoil = 1;
    public const ushort MaterialRock = 2;
    public const ushort MaterialSediment = 3;

    private static readonly byte[] WorldSeedBytes = Convert.FromHexString(Qa04ReferenceLoadV1.WorldSeedHex);
    private static readonly StableToken TerrainClass = new("spatial.hot-terrain-brick");
    private static readonly Lazy<IReadOnlyDictionary<OpaqueId128, int>> RankByRecordId =
        new(BuildCanonicalRanks, LazyThreadSafetyMode.ExecutionAndPublication);

    public static IReadOnlyList<StableToken> SurfaceClasses { get; } = Array.AsReadOnly(new[]
    {
        new StableToken("terrain.rock"),
        new StableToken("terrain.sediment"),
        new StableToken("terrain.soil"),
    });

    public static void ValidateCanonicalContract()
    {
        Qa04ReferenceLoadV1.ValidateCanonicalContract();
        Qa04TerrainBrickDescriptorMaterializerV1.ValidateCanonicalContract();
        if (Qa04ReferenceLoadV1.RegionalTileRows != 64 ||
            Qa04ReferenceLoadV1.RegionalTileColumns != 64 ||
            Qa04ReferenceLoadV1.RegionalTileCount != 4_096 ||
            TileWidthMm != 512_000 ||
            HotSlotsPerTile != 65_536 ||
            D0SampleSpacingMm != 250 ||
            D0BrickWidthMm != 2_000)
            throw new InvalidDataException("qa04.terrain.canonical-lattice-drift");
        if ((HotSlotPermutationMultiplier & 1) == 0)
            throw new InvalidDataException("qa04.terrain.hot-slot-permutation-not-bijective");
        if (!SurfaceClasses.Select(static value => value.Value).SequenceEqual(
                new[] { "terrain.rock", "terrain.sediment", "terrain.soil" }, StringComparer.Ordinal))
            throw new InvalidDataException("qa04.terrain.surface-class-vocabulary-drift");
        if (WorldSeedBytes.Length != 32)
            throw new InvalidDataException("qa04.terrain.world-seed-length");
    }

    public TerrainBrickV1 CreateBrick(Qa04ReferenceRecordV1 descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ValidateDescriptor(descriptor);
        var origin = HotCellOrigin(descriptor);
        var sdf = CreateSdfSamples(origin, D0SampleSpacingMm);
        var materials = CreateSurfaceMaterials(origin, D0SampleSpacingMm);
        return new TerrainBrickV1(
            descriptor.RecordId,
            level: 0,
            origin,
            D0SampleSpacingMm,
            sdf,
            materials,
            revision: Qa04TerrainBrickDescriptorMaterializerV1.InitialRecordRevision);
    }

    public static SpatialCellKeyV1 HotCellOrigin(Qa04ReferenceRecordV1 descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ValidateDescriptor(descriptor);
        var tile = descriptor.RegionalTileIndex;
        var rank = RankByRecordId.Value.TryGetValue(descriptor.RecordId, out var value)
            ? value
            : throw new InvalidDataException("qa04.terrain.hot-rank-missing");
        var slot = checked((rank * HotSlotPermutationMultiplier + tile * HotSlotTileOffsetMultiplier) % HotSlotsPerTile);
        var localBrickX = slot & 255;
        var localBrickY = slot >> 8;
        var tileRow = tile / Qa04ReferenceLoadV1.RegionalTileColumns;
        var tileColumn = tile % Qa04ReferenceLoadV1.RegionalTileColumns;
        var globalBrickX = checked(tileColumn * HotSlotsPerAxis + localBrickX);
        var globalBrickY = checked(tileRow * HotSlotsPerAxis + localBrickY);
        var centerXmm = checked((long)globalBrickX * D0BrickWidthMm + D0BrickWidthMm / 2);
        var centerYmm = checked((long)globalBrickY * D0BrickWidthMm + D0BrickWidthMm / 2);
        var brickZ = FloorDiv(HeightMm(centerXmm, centerYmm), D0BrickWidthMm);
        return new SpatialCellKeyV1(
            0,
            checked(globalBrickX * TerrainBrickV1.CellsPerAxis),
            checked(globalBrickY * TerrainBrickV1.CellsPerAxis),
            checked((int)(brickZ * TerrainBrickV1.CellsPerAxis)));
    }

    public static int HeightMm(long xMm, long yMm)
    {
        var digest = HashSuite.DomainHash("mv.perf-reference-terrain-height.v1", writer =>
        {
            writer.WriteArrayStart(3);
            writer.WriteBytes(WorldSeedBytes);
            writer.WriteInt64(xMm);
            writer.WriteInt64(yMm);
        });
        var value = BinaryPrimitives.ReadUInt32BigEndian(digest.AsSpan(0, sizeof(uint)));
        return checked((int)(value % 8_001u) - 4_000);
    }

    public static int[] CreateSdfSamples(SpatialCellKeyV1 origin, uint sampleSpacingMm)
    {
        if (origin.Level != 0) throw new ArgumentException("Hot D0 origin must be level 0.", nameof(origin));
        if (sampleSpacingMm != D0SampleSpacingMm) throw new ArgumentOutOfRangeException(nameof(sampleSpacingMm));
        var values = new int[TerrainBrickV1.SdfSampleCount];
        for (var z = 0; z < TerrainBrickV1.SamplesPerAxis; z++)
        for (var y = 0; y < TerrainBrickV1.SamplesPerAxis; y++)
        for (var x = 0; x < TerrainBrickV1.SamplesPerAxis; x++)
        {
            var index = checked(((z * TerrainBrickV1.SamplesPerAxis) + y) * TerrainBrickV1.SamplesPerAxis + x);
            values[index] = SdfSampleAt(origin, sampleSpacingMm, x, y, z);
        }
        return values;
    }

    internal static int SdfSampleAt(
        SpatialCellKeyV1 origin,
        uint sampleSpacingMm,
        int x,
        int y,
        int z)
    {
        if (origin.Level != 0) throw new ArgumentException("Hot D0 origin must be level 0.", nameof(origin));
        if (sampleSpacingMm != D0SampleSpacingMm) throw new ArgumentOutOfRangeException(nameof(sampleSpacingMm));
        if ((uint)x >= TerrainBrickV1.SamplesPerAxis ||
            (uint)y >= TerrainBrickV1.SamplesPerAxis ||
            (uint)z >= TerrainBrickV1.SamplesPerAxis)
            throw new ArgumentOutOfRangeException(nameof(x), "Terrain SDF sample coordinate is outside the canonical brick lattice.");

        var wx = checked(((long)origin.X + x) * sampleSpacingMm);
        var wy = checked(((long)origin.Y + y) * sampleSpacingMm);
        var wz = checked(((long)origin.Z + z) * sampleSpacingMm);
        return checked((int)(wz - HeightMm(wx, wy)));
    }

    public static ushort[] CreateSurfaceMaterials(SpatialCellKeyV1 origin, uint sampleSpacingMm)
    {
        if (origin.Level != 0) throw new ArgumentException("Hot D0 origin must be level 0.", nameof(origin));
        if (sampleSpacingMm != D0SampleSpacingMm) throw new ArgumentOutOfRangeException(nameof(sampleSpacingMm));
        var values = new ushort[TerrainBrickV1.SurfaceMaterialCount];
        for (var z = 0; z < TerrainBrickV1.CellsPerAxis; z++)
        for (var y = 0; y < TerrainBrickV1.CellsPerAxis; y++)
        for (var x = 0; x < TerrainBrickV1.CellsPerAxis; x++)
        {
            var cx = checked((checked(2L * ((long)origin.X + x)) + 1) * sampleSpacingMm / 2);
            var cy = checked((checked(2L * ((long)origin.Y + y)) + 1) * sampleSpacingMm / 2);
            var cz = checked((checked(2L * ((long)origin.Z + z)) + 1) * sampleSpacingMm / 2);
            var distance = checked(cz - HeightMm(cx, cy));
            var material = distance switch
            {
                > 0 => MaterialVoid,
                > -500 => MaterialSoil,
                > -2_000 => MaterialSediment,
                _ => MaterialRock,
            };
            var index = checked(((z * TerrainBrickV1.CellsPerAxis) + y) * TerrainBrickV1.CellsPerAxis + x);
            values[index] = material;
        }
        return values;
    }

    public static long FloorDiv(long numerator, long denominator)
    {
        if (denominator <= 0) throw new ArgumentOutOfRangeException(nameof(denominator));
        var quotient = numerator / denominator;
        var remainder = numerator % denominator;
        return remainder < 0 ? checked(quotient - 1) : quotient;
    }

    private static IReadOnlyDictionary<OpaqueId128, int> BuildCanonicalRanks()
    {
        ValidateCanonicalContractWithoutRanks();
        var byTile = Enumerable.Range(0, Qa04ReferenceLoadV1.RegionalTileCount)
            .Select(static _ => new List<OpaqueId128>())
            .ToArray();
        for (ulong ordinal = 0; ordinal < Qa04TerrainBrickDescriptorMaterializerV1.CanonicalTerrainBrickCount; ordinal++)
        {
            var descriptor = Qa04ReferenceLoadV1.Record(TerrainClass, ordinal);
            byTile[descriptor.RegionalTileIndex].Add(descriptor.RecordId);
        }

        var result = new Dictionary<OpaqueId128, int>(checked((int)Qa04TerrainBrickDescriptorMaterializerV1.CanonicalTerrainBrickCount));
        foreach (var tile in byTile)
        {
            if (tile.Count >= HotSlotsPerTile)
                throw new InvalidDataException("qa04.terrain.hot-tile-capacity-exceeded");
            tile.Sort();
            for (var rank = 0; rank < tile.Count; rank++)
            {
                if (!result.TryAdd(tile[rank], rank))
                    throw new InvalidDataException("qa04.terrain.hot-descriptor-id-duplicate");
            }
        }
        if ((ulong)result.Count != Qa04TerrainBrickDescriptorMaterializerV1.CanonicalTerrainBrickCount)
            throw new InvalidDataException("qa04.terrain.hot-rank-count-mismatch");
        return result;
    }

    private static void ValidateCanonicalContractWithoutRanks()
    {
        Qa04ReferenceLoadV1.ValidateCanonicalContract();
        if (Qa04TerrainBrickDescriptorMaterializerV1.CanonicalTerrainBrickCount != 500_000 ||
            Qa04ReferenceLoadV1.RegionalTileCount != 4_096)
            throw new InvalidDataException("qa04.terrain.rank-contract-drift");
    }

    private static void ValidateDescriptor(Qa04ReferenceRecordV1 descriptor)
    {
        if (descriptor.ClassToken != TerrainClass || descriptor.DetailLevel != DetailLevelV1.D0Entity)
            throw new InvalidDataException("qa04.terrain.hot-descriptor-contract");
        if (descriptor.RegionalTileIndex >= Qa04ReferenceLoadV1.RegionalTileCount)
            throw new InvalidDataException("qa04.terrain.hot-descriptor-tile-range");
    }
}
