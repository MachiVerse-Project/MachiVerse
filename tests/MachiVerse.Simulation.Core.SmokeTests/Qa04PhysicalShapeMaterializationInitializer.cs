using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04PhysicalShapeMaterializationInitializer
{
    private static readonly StableToken SpatialTerrainPartition = new("spatial.terrain_geometry");

    [ModuleInitializer]
    internal static void Initialize()
    {
        Qa04PhysicalShapeMaterializerV1.ValidateCanonicalContract();

        Require(Qa04PhysicalShapeMaterializerV1.ExpectedShapeCount(PhysicalOccupancyRecordSchemaV2.SphereShapeKind) == 250_000,
            "QA-04 physical sphere count drifted.");
        Require(Qa04PhysicalShapeMaterializerV1.ExpectedShapeCount(PhysicalOccupancyRecordSchemaV2.CapsuleShapeKind) == 100_000,
            "QA-04 physical capsule count drifted.");
        Require(Qa04PhysicalShapeMaterializerV1.ExpectedShapeCount(PhysicalOccupancyRecordSchemaV2.OrientedBoxShapeKind) == 100_000,
            "QA-04 physical OBB count drifted.");
        Require(Qa04PhysicalShapeMaterializerV1.ExpectedShapeCount(PhysicalOccupancyRecordSchemaV2.ConvexPolytopeShapeKind) == 40_000,
            "QA-04 physical convex count drifted.");
        Require(Qa04PhysicalShapeMaterializerV1.ExpectedShapeCount(PhysicalOccupancyRecordSchemaV2.TriangleMeshStaticShapeKind) == 5_000,
            "QA-04 physical mesh count drifted.");
        Require(Qa04PhysicalShapeMaterializerV1.ExpectedShapeCount(PhysicalOccupancyRecordSchemaV2.TerrainSdfRefShapeKind) == 5_000,
            "QA-04 physical terrain SDF count drifted.");

        VerifyKind(0, PhysicalOccupancyRecordSchemaV2.SphereShapeKind);
        VerifyKind(49, PhysicalOccupancyRecordSchemaV2.SphereShapeKind);
        VerifyKind(50, PhysicalOccupancyRecordSchemaV2.CapsuleShapeKind);
        VerifyKind(69, PhysicalOccupancyRecordSchemaV2.CapsuleShapeKind);
        VerifyKind(70, PhysicalOccupancyRecordSchemaV2.OrientedBoxShapeKind);
        VerifyKind(89, PhysicalOccupancyRecordSchemaV2.OrientedBoxShapeKind);
        VerifyKind(90, PhysicalOccupancyRecordSchemaV2.ConvexPolytopeShapeKind);
        VerifyKind(97, PhysicalOccupancyRecordSchemaV2.ConvexPolytopeShapeKind);
        VerifyKind(98, PhysicalOccupancyRecordSchemaV2.TriangleMeshStaticShapeKind);
        VerifyKind(99, PhysicalOccupancyRecordSchemaV2.TerrainSdfRefShapeKind);

        var first = Qa04PhysicalShapeMaterializerV1.CreateShapeRecord(0, TerrainRoot);
        var repeat = Qa04PhysicalShapeMaterializerV1.CreateShapeRecord(0, TerrainRoot);
        Require(first.RecordId == repeat.RecordId,
            "QA-04 physical shape identity must be deterministic.");
        Require(first.RecordSchema == PhysicalOccupancyRecordSchemaV2.RecordSchema,
            "QA-04 physical shape must use occupancy schema v2.");
        Require(first.DetailLevel == DetailLevelV1.D0Entity && first.Revision == 1 && first.CreatedStep == 0,
            "QA-04 physical shape genesis envelope drifted.");

        VerifyCanonicalFiveHundredThousandShapeStream();
    }

    private static void VerifyCanonicalFiveHundredThousandShapeStream()
    {
        ulong total = 0;
        ulong spheres = 0;
        ulong capsules = 0;
        ulong boxes = 0;
        ulong convexes = 0;
        ulong meshes = 0;
        ulong terrain = 0;

        foreach (var record in Qa04PhysicalShapeMaterializerV1.MaterializeCanonicalShapes(TerrainRoot))
        {
            Require(record.RecordSchema == PhysicalOccupancyRecordSchemaV2.RecordSchema,
                "Every canonical Physical shape must use occupancy schema v2.");
            Require(record.DetailLevel == DetailLevelV1.D0Entity && record.Revision == 1 && record.CreatedStep == 0,
                "Every canonical Physical shape must retain the genesis record envelope.");
            var shape = record.Payload as PhysicalCollisionShapePayloadV2
                ?? throw new InvalidOperationException("Canonical Physical shape stream emitted a non-shape payload.");
            switch (shape.ShapeKind)
            {
                case PhysicalOccupancyRecordSchemaV2.SphereShapeKind: spheres++; break;
                case PhysicalOccupancyRecordSchemaV2.CapsuleShapeKind: capsules++; break;
                case PhysicalOccupancyRecordSchemaV2.OrientedBoxShapeKind: boxes++; break;
                case PhysicalOccupancyRecordSchemaV2.ConvexPolytopeShapeKind: convexes++; break;
                case PhysicalOccupancyRecordSchemaV2.TriangleMeshStaticShapeKind: meshes++; break;
                case PhysicalOccupancyRecordSchemaV2.TerrainSdfRefShapeKind: terrain++; break;
                default: throw new InvalidOperationException($"Unexpected canonical Physical shape kind: {shape.ShapeKind}");
            }
            total++;
        }

        Require(total == Qa04PhysicalShapeMaterializerV1.CanonicalPhysicalCount,
            "Canonical Physical shape stream must materialize exactly 500,000 records.");
        Require(spheres == 250_000 && capsules == 100_000 && boxes == 100_000 &&
                convexes == 40_000 && meshes == 5_000 && terrain == 5_000,
            "Canonical 500,000 Physical shape stream must preserve the exact shape mix.");
    }

    private static void VerifyKind(ulong ordinal, string expected)
    {
        var record = Qa04PhysicalShapeMaterializerV1.CreateShapeRecord(ordinal, TerrainRoot);
        var shape = record.Payload as PhysicalCollisionShapePayloadV2
            ?? throw new InvalidOperationException("QA-04 physical materializer emitted a non-shape payload.");
        Require(string.Equals(shape.ShapeKind, expected, StringComparison.Ordinal),
            $"QA-04 physical shape mix drifted at ordinal {ordinal}.");
    }

    private static PartitionRecordRefV1 TerrainRoot(ushort tile)
    {
        var id = HashSuite.Trunc128(HashSuite.DomainHash("mv.qa04-physical-shape-smoke-terrain-root.v1", writer =>
        {
            writer.WriteMapStart(1);
            writer.WriteUnsigned(0); writer.WriteUnsigned(tile);
        }));
        if (id.IsZero) throw new InvalidOperationException("Smoke terrain root id unexpectedly became ZERO.");
        return new PartitionRecordRefV1(SpatialTerrainPartition, id);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
