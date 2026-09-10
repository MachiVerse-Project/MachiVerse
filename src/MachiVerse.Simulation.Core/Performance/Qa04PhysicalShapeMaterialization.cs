using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Materializes the collision-shape half of the perf.reference.v1 Physical D0 contract using the
/// production physical.occupancy /2.0 record model. Terrain root identity is supplied by the
/// canonical Terrain materializer so this class never invents a cross-partition Ref target.
/// </summary>
public static class Qa04PhysicalShapeMaterializerV1
{
    public const ulong CanonicalPhysicalCount = 500_000;

    private static readonly StableToken PhysicalReferenceClass = new("physical.d0-presence");
    private static readonly StableToken PerformanceDomain = new("performance");
    private static readonly StableToken ShapePurpose = new("perf.physical-shape");

    public static void ValidateCanonicalContract()
    {
        Qa04ReferenceLoadV1.ValidateCanonicalContract();
        PhysicalOccupancyRecordSchemaV2.ValidateCanonicalContract();

        var count = Qa04ReferenceLoadV1.RecordClasses
            .Single(entry => entry.ClassToken == PhysicalReferenceClass)
            .Count;
        if (count != CanonicalPhysicalCount)
            throw new InvalidDataException("qa04.materialization.physical-count-drift");

        if (ExpectedShapeCount(PhysicalOccupancyRecordSchemaV2.SphereShapeKind) != 250_000 ||
            ExpectedShapeCount(PhysicalOccupancyRecordSchemaV2.CapsuleShapeKind) != 100_000 ||
            ExpectedShapeCount(PhysicalOccupancyRecordSchemaV2.OrientedBoxShapeKind) != 100_000 ||
            ExpectedShapeCount(PhysicalOccupancyRecordSchemaV2.ConvexPolytopeShapeKind) != 40_000 ||
            ExpectedShapeCount(PhysicalOccupancyRecordSchemaV2.TriangleMeshStaticShapeKind) != 5_000 ||
            ExpectedShapeCount(PhysicalOccupancyRecordSchemaV2.TerrainSdfRefShapeKind) != 5_000)
            throw new InvalidDataException("qa04.materialization.physical-shape-mix-drift");
    }

    public static PhysicalOccupancyRecordMaterialV2 CreateShapeRecord(
        ulong physicalOrdinal,
        Func<ushort, PartitionRecordRefV1> terrainRootForTile)
    {
        ValidateCanonicalContract();
        ArgumentNullException.ThrowIfNull(terrainRootForTile);
        return CreateShapeRecordValidated(physicalOrdinal, terrainRootForTile);
    }

    internal static PhysicalOccupancyRecordMaterialV2 CreateShapeRecordValidated(
        ulong physicalOrdinal,
        Func<ushort, PartitionRecordRefV1> terrainRootForTile)
    {
        ArgumentNullException.ThrowIfNull(terrainRootForTile);
        var descriptor = Qa04ReferenceLoadV1.Record(PhysicalReferenceClass, physicalOrdinal);
        var recordId = DerivedIdentity.DeriveEntityId(
            Qa04ReferenceLoadV1.WorldId,
            0,
            PerformanceDomain,
            descriptor.RecordId,
            ShapePurpose,
            0);

        var payload = CreateShapePayload(descriptor, terrainRootForTile);
        return new PhysicalOccupancyRecordMaterialV2(
            recordId,
            revision: 1,
            createdStep: 0,
            retiredStep: null,
            detailLevel: DetailLevelV1.D0Entity,
            lineageRef: null,
            payload);
    }

    public static IEnumerable<PhysicalOccupancyRecordMaterialV2> MaterializeCanonicalShapes(
        Func<ushort, PartitionRecordRefV1> terrainRootForTile)
    {
        ValidateCanonicalContract();
        ArgumentNullException.ThrowIfNull(terrainRootForTile);
        for (ulong ordinal = 0; ordinal < CanonicalPhysicalCount; ordinal++)
            yield return CreateShapeRecordValidated(ordinal, terrainRootForTile);
    }

    public static ulong ExpectedShapeCount(string shapeKind)
        => shapeKind switch
        {
            PhysicalOccupancyRecordSchemaV2.SphereShapeKind => CanonicalPhysicalCount / 100 * 50,
            PhysicalOccupancyRecordSchemaV2.CapsuleShapeKind => CanonicalPhysicalCount / 100 * 20,
            PhysicalOccupancyRecordSchemaV2.OrientedBoxShapeKind => CanonicalPhysicalCount / 100 * 20,
            PhysicalOccupancyRecordSchemaV2.ConvexPolytopeShapeKind => CanonicalPhysicalCount / 100 * 8,
            PhysicalOccupancyRecordSchemaV2.TriangleMeshStaticShapeKind => CanonicalPhysicalCount / 100,
            PhysicalOccupancyRecordSchemaV2.TerrainSdfRefShapeKind => CanonicalPhysicalCount / 100,
            _ => throw new ArgumentOutOfRangeException(nameof(shapeKind)),
        };

    private static PhysicalCollisionShapePayloadV2 CreateShapePayload(
        Qa04ReferenceRecordV1 descriptor,
        Func<ushort, PartitionRecordRefV1> terrainRootForTile)
    {
        var bucket = descriptor.Ordinal % 100;
        if (bucket < 50)
            return new PhysicalSphereShapePayloadV2(
                new Vec3MmV1(0, 0, 0),
                checked(300L + (long)(descriptor.Ordinal % 201)));

        if (bucket < 70)
            return new PhysicalCapsuleShapePayloadV2(
                new Vec3MmV1(0, -400, 0),
                new Vec3MmV1(0, 400, 0),
                checked(200L + (long)(descriptor.Ordinal % 101)));

        var halfExtents = new Vec3MmV1(
            checked(300L + (long)(descriptor.Ordinal % 101)),
            checked(200L + (long)(descriptor.Ordinal % 101)),
            checked(500L + (long)(descriptor.Ordinal % 201)));

        if (bucket < 90)
            return new PhysicalOrientedBoxShapePayloadV2(
                new Vec3MmV1(0, 0, 0),
                halfExtents,
                global::MachiVerse.Simulation.Core.Domains.QuaternionQ30V1.Identity);

        if (bucket < 98)
            return new PhysicalConvexPolytopeShapePayloadV2(CreateBoxVertices(halfExtents));

        if (bucket == 98)
            return new PhysicalTriangleMeshStaticShapePayloadV2([
                new StaticTriangleV1(
                    0,
                    new Vec3MmV1(-1_000, -1_000, 0),
                    new Vec3MmV1(1_000, -1_000, 0),
                    new Vec3MmV1(1_000, 1_000, 0)),
                new StaticTriangleV1(
                    1,
                    new Vec3MmV1(-1_000, -1_000, 0),
                    new Vec3MmV1(1_000, 1_000, 0),
                    new Vec3MmV1(-1_000, 1_000, 0)),
            ]);

        var terrainRoot = terrainRootForTile(descriptor.RegionalTileIndex);
        if (terrainRoot.PartitionId.Value != "spatial.terrain_geometry" || terrainRoot.RecordId.IsZero)
            throw new InvalidDataException("qa04.materialization.physical-terrain-root-ref-invalid");
        return new PhysicalTerrainSdfRefShapePayloadV2(terrainRoot);
    }

    private static IReadOnlyList<Vec3MmV1> CreateBoxVertices(Vec3MmV1 half)
    {
        var values = new Vec3MmV1[8];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = new Vec3MmV1(
                (index & 1) == 0 ? -half.X : half.X,
                (index & 2) == 0 ? -half.Y : half.Y,
                (index & 4) == 0 ? -half.Z : half.Z);
        }
        return Array.AsReadOnly(values);
    }
}
