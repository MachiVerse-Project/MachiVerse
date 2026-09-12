using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static PartitionRecordRefV1 TerrainRoot(ushort tile)
{
    var id = HashSuite.Trunc128(HashSuite.DomainHash("mv.qa04-physical-shape-smoke-terrain-root.v1", writer =>
    {
        writer.WriteMapStart(1);
        writer.WriteUnsigned(0); writer.WriteUnsigned(tile);
    }));
    if (id.IsZero) throw new InvalidOperationException("Physical full materialization terrain root id unexpectedly became ZERO.");
    return new PartitionRecordRefV1("spatial.terrain_geometry", id);
}

Qa04PhysicalShapeMaterializerV1.ValidateCanonicalContract();

var seenIds = new HashSet<OpaqueId128>();
ulong total = 0;
ulong spheres = 0;
ulong capsules = 0;
ulong boxes = 0;
ulong convexes = 0;
ulong meshes = 0;
ulong terrain = 0;

Console.WriteLine($"Validating full canonical Physical shape materialization ({Qa04PhysicalShapeMaterializerV1.CanonicalPhysicalCount:N0} records)...");

foreach (var record in Qa04PhysicalShapeMaterializerV1.MaterializeCanonicalShapes(TerrainRoot))
{
    Require(record.RecordSchema == PhysicalOccupancyRecordSchemaV2.RecordSchema,
        $"Canonical Physical shape schema drifted at ordinal {total}.");
    Require(record.DetailLevel == DetailLevelV1.D0Entity && record.Revision == 1 &&
            record.CreatedStep == 0 && record.RetiredStep is null && record.LineageRef is null,
        $"Canonical Physical shape envelope drifted at ordinal {total}.");
    Require(seenIds.Add(record.RecordId),
        $"Canonical Physical shape stream emitted duplicate RecordId at ordinal {total}.");

    var shape = record.Payload as PhysicalCollisionShapePayloadV2
        ?? throw new InvalidOperationException($"Canonical Physical shape stream emitted a non-shape payload at ordinal {total}.");
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

Require(total == Qa04PhysicalShapeMaterializerV1.CanonicalPhysicalCount &&
        (ulong)seenIds.Count == Qa04PhysicalShapeMaterializerV1.CanonicalPhysicalCount,
    "Canonical Physical shape stream must materialize exactly 500,000 unique records.");
Require(spheres == 250_000 && capsules == 100_000 && boxes == 100_000 &&
        convexes == 40_000 && meshes == 5_000 && terrain == 5_000,
    "Canonical 500,000 Physical shape stream must preserve the exact shape mix.");

Console.WriteLine($"physical-shape-full-production-pass records={total} unique={seenIds.Count}");
