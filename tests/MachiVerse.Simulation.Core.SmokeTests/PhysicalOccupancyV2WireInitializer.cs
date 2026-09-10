using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class PhysicalOccupancyV2WireInitializer
{
    [ModuleInitializer]
    internal static void Run()
    {
        var records = BuildRecords();
        foreach (var record in records)
        {
            var encoded = PhysicalOccupancyRecordWireCodecV2.Encode(record);
            var decoded = PhysicalOccupancyRecordWireCodecV2.Decode(encoded);
            var reencoded = PhysicalOccupancyRecordWireCodecV2.Encode(decoded);
            Require(encoded.AsSpan().SequenceEqual(reencoded),
                $"Physical occupancy v2 wire must be byte-canonical for {record.Payload.RecordKind}.");
            Require(decoded.RecordId == record.RecordId &&
                    decoded.RecordSchema == record.RecordSchema &&
                    decoded.Revision == record.Revision &&
                    decoded.CreatedStep == record.CreatedStep &&
                    decoded.RetiredStep == record.RetiredStep &&
                    decoded.DetailLevel == record.DetailLevel &&
                    decoded.LineageRef == record.LineageRef &&
                    decoded.Payload.GetType() == record.Payload.GetType(),
                "Physical occupancy v2 wire must preserve envelope and exact arm type.");
        }

        var malformed = PhysicalOccupancyRecordWireCodecV2.Encode(records[0]).Concat(new byte[] { 0x50, 0x00 }).ToArray();
        ExpectInvalid(() => PhysicalOccupancyRecordWireCodecV2.Decode(malformed));
    }

    private static PhysicalOccupancyRecordMaterialV2[] BuildRecords()
    {
        var half = new Vec3MmV1(300, 200, 500);
        return
        [
            Record(1, new PhysicalOccupancyStatePayloadV2(
                Ref(PhysicalPresencePayloadV1.PartitionId, 20),
                new Vec3Int64V1(-100, -200, -300),
                new Vec3Int64V1(100, 200, 300),
                Array.Empty<PartitionRecordRefV1>(), 0, 1)),
            Record(2, new PhysicalSphereShapePayloadV2(new Vec3MmV1(1, 2, 3), 301)),
            Record(3, new PhysicalCapsuleShapePayloadV2(
                new Vec3MmV1(0, -400, 0), new Vec3MmV1(0, 400, 0), 201)),
            Record(4, new PhysicalOrientedBoxShapePayloadV2(
                new Vec3MmV1(0, 0, 0), half, QuaternionQ30V1.Identity)),
            Record(5, new PhysicalConvexPolytopeShapePayloadV2(Corners(half))),
            Record(6, new PhysicalTriangleMeshStaticShapePayloadV2(
            [
                new StaticTriangleV1(0, new Vec3MmV1(-1000,-1000,0), new Vec3MmV1(1000,-1000,0), new Vec3MmV1(1000,1000,0)),
                new StaticTriangleV1(1, new Vec3MmV1(-1000,-1000,0), new Vec3MmV1(1000,1000,0), new Vec3MmV1(-1000,1000,0)),
            ])),
            Record(7, new PhysicalTerrainSdfRefShapePayloadV2(Ref("spatial.terrain_geometry", 21))),
        ];
    }

    private static IReadOnlyList<Vec3MmV1> Corners(Vec3MmV1 half)
        => Array.AsReadOnly(Enumerable.Range(0, 8).Select(bits => new Vec3MmV1(
            (bits & 1) == 0 ? -half.X : half.X,
            (bits & 2) == 0 ? -half.Y : half.Y,
            (bits & 4) == 0 ? -half.Z : half.Z)).ToArray());

    private static PhysicalOccupancyRecordMaterialV2 Record(byte suffix, PhysicalOccupancyRecordPayloadV2 payload)
        => new(Id(suffix), 2, 3, null, DetailLevelV1.D0Entity, Id((byte)(suffix + 100)), payload);

    private static PartitionRecordRefV1 Ref(string partition, byte suffix) => new(partition, Id(suffix));

    private static OpaqueId128 Id(byte suffix)
    {
        var bytes = new byte[16];
        bytes[^1] = suffix;
        return OpaqueId128.FromBytes(bytes);
    }

    private static void ExpectInvalid(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Expected strict Physical occupancy v2 wire rejection.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
