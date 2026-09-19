using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04TerrainCanonicalReleasePreflightEvidenceV1(
    ulong HotDescriptorCount,
    int UniqueHotOriginCount,
    int ProbeBrickCount,
    int SharedFacePairCount,
    int RootCount,
    int AnchorIdentityCount);

/// <summary>
/// Memory-safe preflight for the canonical Terrain release criteria that can be proven without
/// retaining all 500,000 payload records at once. This intentionally does not claim production
/// Snapshot/recovery completion; that remains a parent world-blocker condition.
/// </summary>
public static class Qa04TerrainCanonicalReleasePreflightV1
{
    private const ulong ProbeStride = 512;
    private static readonly StableToken TerrainClass = new("spatial.hot-terrain-brick");

    public static Qa04TerrainCanonicalReleasePreflightEvidenceV1 Build()
    {
        Qa04TerrainCanonicalContentSourceV1.ValidateCanonicalContract();
        Qa04TerrainBrickDescriptorMaterializerV1.ValidateCanonicalContract();
        Qa04TerrainRootMaterializerV1.ValidateCanonicalContract();
        Qa04SpatialTileScopeAuthorityV1.ValidateCanonicalContract();

        var originCapacity = checked((int)Qa04TerrainBrickDescriptorMaterializerV1.CanonicalTerrainBrickCount);
        var origins = new HashSet<SpatialCellKeyV1>(originCapacity);
        var descriptorIds = new HashSet<OpaqueId128>(originCapacity);
        var source = new Qa04TerrainCanonicalContentSourceV1();
        var probeCount = 0;

        for (ulong ordinal = 0; ordinal < Qa04TerrainBrickDescriptorMaterializerV1.CanonicalTerrainBrickCount; ordinal++)
        {
            var descriptor = Qa04ReferenceLoadV1.Record(TerrainClass, ordinal);
            if (descriptor.ClassToken != TerrainClass || descriptor.DetailLevel != DetailLevelV1.D0Entity)
                throw new InvalidDataException("qa04.terrain.preflight-descriptor-contract");
            if (!descriptorIds.Add(descriptor.RecordId))
                throw new InvalidDataException("qa04.terrain.preflight-descriptor-id-duplicate");

            var origin = Qa04TerrainCanonicalContentSourceV1.HotCellOrigin(descriptor);
            if (!origins.Add(origin))
                throw new InvalidDataException("qa04.terrain.preflight-hot-origin-duplicate");

            if (ordinal % ProbeStride == 0 || ordinal + 1 == Qa04TerrainBrickDescriptorMaterializerV1.CanonicalTerrainBrickCount)
            {
                var brick = source.CreateBrick(descriptor);
                if (brick.BrickId != descriptor.RecordId || brick.CellOrigin != origin ||
                    brick.Level != 0 || brick.SampleSpacingMm != Qa04TerrainCanonicalContentSourceV1.D0SampleSpacingMm ||
                    brick.Revision != Qa04TerrainBrickDescriptorMaterializerV1.InitialRecordRevision ||
                    brick.SdfMm.Count != TerrainBrickV1.SdfSampleCount ||
                    brick.SurfaceMaterialIds.Count != TerrainBrickV1.SurfaceMaterialCount ||
                    brick.SurfaceMaterialIds.Any(static material => material > Qa04TerrainCanonicalContentSourceV1.MaterialSediment))
                    throw new InvalidDataException("qa04.terrain.preflight-probe-brick-drift");
                probeCount++;
            }
        }

        if ((ulong)descriptorIds.Count != Qa04TerrainBrickDescriptorMaterializerV1.CanonicalTerrainBrickCount ||
            descriptorIds.Count != origins.Count)
            throw new InvalidDataException("qa04.terrain.preflight-hot-count");

        var sharedFacePairs = VerifyAllCanonicalSharedFaces(origins);
        var (rootCount, anchorCount) = VerifyRootAndAnchorIdentityClosure();

        return new Qa04TerrainCanonicalReleasePreflightEvidenceV1(
            Qa04TerrainBrickDescriptorMaterializerV1.CanonicalTerrainBrickCount,
            origins.Count,
            probeCount,
            sharedFacePairs,
            rootCount,
            anchorCount);
    }

    private static int VerifyAllCanonicalSharedFaces(IReadOnlySet<SpatialCellKeyV1> origins)
    {
        var pairCount = 0;
        foreach (var origin in origins)
        {
            pairCount = checked(pairCount + VerifyHorizontalNeighborFaces(
                origins,
                origin,
                checked(origin.X + TerrainBrickV1.CellsPerAxis),
                origin.Y,
                xAxis: true));
            pairCount = checked(pairCount + VerifyHorizontalNeighborFaces(
                origins,
                origin,
                origin.X,
                checked(origin.Y + TerrainBrickV1.CellsPerAxis),
                xAxis: false));
        }
        return pairCount;
    }

    private static int VerifyHorizontalNeighborFaces(
        IReadOnlySet<SpatialCellKeyV1> origins,
        SpatialCellKeyV1 origin,
        int neighborX,
        int neighborY,
        bool xAxis)
    {
        var count = 0;
        for (var deltaZ = -TerrainBrickV1.CellsPerAxis;
             deltaZ <= TerrainBrickV1.CellsPerAxis;
             deltaZ += TerrainBrickV1.CellsPerAxis)
        {
            var neighbor = new SpatialCellKeyV1(
                origin.Level,
                neighborX,
                neighborY,
                checked(origin.Z + deltaZ));
            if (!origins.Contains(neighbor)) continue;
            VerifySharedFace(origin, neighbor, xAxis);
            count++;
        }
        return count;
    }

    private static void VerifySharedFace(SpatialCellKeyV1 a, SpatialCellKeyV1 b, bool xAxis)
    {
        var firstSharedGlobalZ = Math.Max(a.Z, b.Z);
        var lastSharedGlobalZ = Math.Min(
            checked(a.Z + TerrainBrickV1.CellsPerAxis),
            checked(b.Z + TerrainBrickV1.CellsPerAxis));
        if (firstSharedGlobalZ > lastSharedGlobalZ)
            throw new InvalidDataException("qa04.terrain.preflight-shared-face-without-sample-overlap");

        for (var globalZ = firstSharedGlobalZ; globalZ <= lastSharedGlobalZ; globalZ++)
        for (var orthogonal = 0; orthogonal < TerrainBrickV1.SamplesPerAxis; orthogonal++)
        {
            var aZ = checked(globalZ - a.Z);
            var bZ = checked(globalZ - b.Z);
            var aSdf = xAxis
                ? Qa04TerrainCanonicalContentSourceV1.SdfSampleAt(
                    a,
                    Qa04TerrainCanonicalContentSourceV1.D0SampleSpacingMm,
                    TerrainBrickV1.CellsPerAxis,
                    orthogonal,
                    aZ)
                : Qa04TerrainCanonicalContentSourceV1.SdfSampleAt(
                    a,
                    Qa04TerrainCanonicalContentSourceV1.D0SampleSpacingMm,
                    orthogonal,
                    TerrainBrickV1.CellsPerAxis,
                    aZ);
            var bSdf = xAxis
                ? Qa04TerrainCanonicalContentSourceV1.SdfSampleAt(
                    b,
                    Qa04TerrainCanonicalContentSourceV1.D0SampleSpacingMm,
                    0,
                    orthogonal,
                    bZ)
                : Qa04TerrainCanonicalContentSourceV1.SdfSampleAt(
                    b,
                    Qa04TerrainCanonicalContentSourceV1.D0SampleSpacingMm,
                    orthogonal,
                    0,
                    bZ);
            if (aSdf != bSdf)
                throw new InvalidDataException("qa04.terrain.preflight-shared-face-sdf-mismatch");
        }
    }

    private static (int RootCount, int AnchorCount) VerifyRootAndAnchorIdentityClosure()
    {
        var rootIds = new HashSet<OpaqueId128>();
        var anchorIds = new HashSet<OpaqueId128>();
        var expectedRoots = Enumerable.Range(0, Qa04ReferenceLoadV1.RegionalTileCount)
            .Select(static tile => Qa04TerrainRootMaterializerV1.RootId(checked((ushort)tile)))
            .ToHashSet();

        for (ushort tile = 0; tile < Qa04ReferenceLoadV1.RegionalTileCount; tile++)
        {
            var rootId = Qa04TerrainRootMaterializerV1.RootId(tile);
            var anchorId = Qa04TerrainRootMaterializerV1.AnchorId(tile);
            if (!rootIds.Add(rootId) || !anchorIds.Add(anchorId) || rootId == anchorId)
                throw new InvalidDataException("qa04.terrain.preflight-root-anchor-id-collision");

            var root = Qa04TerrainRootMaterializerV1.CreateRoot(tile, Qa04SpatialTileScopeAuthorityV1.ScopeRef(tile));
            if (root.RecordId != rootId || root.Payload is not SpatialTerrainRootPayloadV2 payload ||
                payload.ScopeRef != Qa04SpatialTileScopeAuthorityV1.ScopeRef(tile) ||
                payload.RootBrickRef.PartitionId.Value != SpatialTerrainGeometryRecordSchemaV2.PartitionId ||
                payload.RootBrickRef.RecordId != anchorId ||
                payload.ConnectivityRefs.Any(reference =>
                    reference.PartitionId.Value != SpatialTerrainGeometryRecordSchemaV2.PartitionId ||
                    !expectedRoots.Contains(reference.RecordId)))
                throw new InvalidDataException("qa04.terrain.preflight-root-closure");
        }

        if ((ulong)rootIds.Count != Qa04TerrainRootMaterializerV1.CanonicalRootCount ||
            (ulong)anchorIds.Count != Qa04TerrainRootMaterializerV1.CanonicalAnchorCount ||
            rootIds.Overlaps(anchorIds))
            throw new InvalidDataException("qa04.terrain.preflight-root-anchor-count");

        return (rootIds.Count, anchorIds.Count);
    }
}
