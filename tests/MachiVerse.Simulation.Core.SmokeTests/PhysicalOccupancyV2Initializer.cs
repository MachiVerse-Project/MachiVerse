using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.WorldState;

internal static class PhysicalOccupancyV2Initializer
{
    [ModuleInitializer]
    internal static void Run()
    {
        VerifySchemaAndMigration();
        VerifyAllShapeArmsAndMixedState();
        VerifyFailClosedValidation();
    }

    private static void VerifySchemaAndMigration()
    {
        PhysicalOccupancyRecordSchemaV2.ValidateCanonicalContract();
        PhysicalOccupancyPartitionIdentityV2.ValidateCanonicalContract();
        StandardDomainRecordSchemaMigrationRegistryV1.ValidateCanonicalContract();

        var migration = StandardDomainRecordSchemaMigrationRegistryV1.Get(PhysicalOccupancyRecordSchemaV2.PartitionId);
        Require(migration.SourceRecordSchema.Version == new SchemaVersionV1(1, 0) &&
                migration.TargetRecordSchema == PhysicalOccupancyRecordSchemaV2.RecordSchema,
            "physical.occupancy must register exact 1.0 -> 2.0 migration.");

        var sourcePayload = new PhysicalOccupancyPayloadV1(
            Ref(PhysicalPresencePayloadV1.PartitionId, 0x10),
            new Vec3Int64V1(-100, -200, -300),
            new Vec3Int64V1(100, 200, 300),
            Array.Empty<PartitionRecordRefV1>(),
            7,
            3);
        var source = new DomainRecordEnvelopeV1<PhysicalOccupancyPayloadV1>(
            Id(0x20),
            migration.SourceRecordSchema,
            revision: 4,
            createdStep: 5,
            retiredStep: null,
            detailLevel: DetailLevelV1.D0Entity,
            lineageRef: Id(0x21),
            payload: sourcePayload);

        var migrated = PhysicalOccupancyRecordMaterialV2.MigrateOccupancy(source);
        Require(migrated.RecordId == source.RecordId &&
                migrated.Revision == source.Revision &&
                migrated.CreatedStep == source.CreatedStep &&
                migrated.RetiredStep == source.RetiredStep &&
                migrated.DetailLevel == source.DetailLevel &&
                migrated.LineageRef == source.LineageRef &&
                migrated.RecordSchema == migration.TargetRecordSchema,
            "physical.occupancy migration must preserve the common record envelope.");
        Require(migrated.Payload is PhysicalOccupancyStatePayloadV2 occupancy &&
                occupancy.PresenceRef == sourcePayload.PresenceRef &&
                occupancy.AabbMin == sourcePayload.AabbMin &&
                occupancy.AabbMax == sourcePayload.AabbMax &&
                occupancy.ContactRefs.SequenceEqual(sourcePayload.ContactRefs) &&
                occupancy.OccupancyFlags == sourcePayload.OccupancyFlags &&
                occupancy.CollisionLayer == sourcePayload.CollisionLayer,
            "physical.occupancy migration must be field-for-field lossless.");
    }

    private static void VerifyAllShapeArmsAndMixedState()
    {
        var half = new Vec3MmV1(320, 220, 520);
        var records = new PhysicalOccupancyRecordMaterialV2[]
        {
            Record(0x30, new PhysicalOccupancyStatePayloadV2(
                Ref(PhysicalPresencePayloadV1.PartitionId, 0x31),
                new Vec3Int64V1(-500, -500, -500),
                new Vec3Int64V1(500, 500, 500),
                Array.Empty<PartitionRecordRefV1>(),
                0,
                1)),
            Record(0x40, new PhysicalSphereShapePayloadV2(new Vec3MmV1(0, 0, 0), 350)),
            Record(0x41, new PhysicalCapsuleShapePayloadV2(
                new Vec3MmV1(0, -400, 0),
                new Vec3MmV1(0, 400, 0),
                225)),
            Record(0x42, new PhysicalOrientedBoxShapePayloadV2(
                new Vec3MmV1(0, 0, 0),
                half,
                QuaternionQ30V1.Identity)),
            Record(0x43, new PhysicalConvexPolytopeShapePayloadV2(Corners(half))),
            Record(0x44, new PhysicalTriangleMeshStaticShapePayloadV2(new[]
            {
                new StaticTriangleV1(0,
                    new Vec3MmV1(-1000, -1000, 0),
                    new Vec3MmV1(1000, -1000, 0),
                    new Vec3MmV1(1000, 1000, 0)),
                new StaticTriangleV1(1,
                    new Vec3MmV1(-1000, -1000, 0),
                    new Vec3MmV1(1000, 1000, 0),
                    new Vec3MmV1(-1000, 1000, 0)),
            })),
            Record(0x45, new PhysicalTerrainSdfRefShapePayloadV2(
                Ref("spatial.terrain_geometry", 0x99))),
        };

        var state = new PhysicalOccupancyPartitionStateV2(records.Reverse());
        Require(state.State.ItemCount == 7 && state.RecordSet.RecordsCanonical.Count == 7,
            "physical.occupancy v2 mixed state must retain occupancy plus all six shape arms.");
        Require(state.RecordSet.RecordsCanonical
                .Select(static record => record.RecordId)
                .SequenceEqual(records.Select(static record => record.RecordId).OrderBy(static id => id)),
            "physical.occupancy v2 mixed state must canonicalize only by record id.");

        Require(((PhysicalSphereShapePayloadV2)records[1].Payload).ToRuntime().RadiusMm == 350,
            "sphere v2 must reconstruct runtime collider exactly.");
        Require(((PhysicalCapsuleShapePayloadV2)records[2].Payload).ToRuntime().RadiusMm == 225,
            "capsule v2 must reconstruct runtime collider exactly.");
        Require(((PhysicalOrientedBoxShapePayloadV2)records[3].Payload).ToRuntime().Orientation == QuaternionQ30V1.Identity,
            "oriented-box v2 must preserve canonical quaternion.");
        Require(((PhysicalConvexPolytopeShapePayloadV2)records[4].Payload).ToRuntime().VerticesCanonical.Count == 8,
            "convex v2 must preserve authoritative vertex index order.");
        Require(((PhysicalTriangleMeshStaticShapePayloadV2)records[5].Payload).ToRuntime().TrianglesCanonical.Count == 2,
            "static-mesh v2 must preserve canonical triangle count.");
    }

    private static void VerifyFailClosedValidation()
    {
        ExpectInvalid(() => new PhysicalOccupancyStatePayloadV2(
            Ref(PhysicalPresencePayloadV1.PartitionId, 1),
            new Vec3Int64V1(1, 0, 0),
            new Vec3Int64V1(0, 0, 0),
            Array.Empty<PartitionRecordRefV1>(), 0, 1));

        ExpectInvalid(() => new PhysicalSphereShapePayloadV2(new Vec3MmV1(0, 0, 0), 0));
        ExpectInvalid(() => new PhysicalCapsuleShapePayloadV2(
            new Vec3MmV1(0, 0, 0), new Vec3MmV1(0, 0, 0), 1));
        ExpectInvalid(() => new PhysicalConvexPolytopeShapePayloadV2(new[]
        {
            new Vec3MmV1(1, 2, 3), new Vec3MmV1(1, 2, 3),
        }));
        ExpectInvalid(() => new PhysicalTriangleMeshStaticShapePayloadV2(new[]
        {
            new StaticTriangleV1(1, new Vec3MmV1(0,0,0), new Vec3MmV1(1,0,0), new Vec3MmV1(0,1,0)),
            new StaticTriangleV1(0, new Vec3MmV1(0,0,0), new Vec3MmV1(1,0,0), new Vec3MmV1(0,1,0)),
        }));
        ExpectInvalid(() => new PhysicalTerrainSdfRefShapePayloadV2(Ref("spatial.world_frame", 1)));
    }

    private static IReadOnlyList<Vec3MmV1> Corners(Vec3MmV1 half)
    {
        var result = new List<Vec3MmV1>(8);
        for (var bits = 0; bits < 8; bits++)
        {
            result.Add(new Vec3MmV1(
                (bits & 1) == 0 ? -half.X : half.X,
                (bits & 2) == 0 ? -half.Y : half.Y,
                (bits & 4) == 0 ? -half.Z : half.Z));
        }
        return Array.AsReadOnly(result.ToArray());
    }

    private static PhysicalOccupancyRecordMaterialV2 Record(byte suffix, PhysicalOccupancyRecordPayloadV2 payload)
        => new(Id(suffix), 1, 0, null, DetailLevelV1.D0Entity, null, payload);

    private static PartitionRecordRefV1 Ref(string partitionId, byte suffix) => new(partitionId, Id(suffix));

    private static OpaqueId128 Id(byte suffix)
    {
        var bytes = new byte[16];
        bytes[^1] = suffix;
        return OpaqueId128.FromBytes(bytes);
    }

    private static void ExpectInvalid(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        {
            return;
        }
        throw new InvalidOperationException("Expected Physical occupancy v2 validation failure.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
