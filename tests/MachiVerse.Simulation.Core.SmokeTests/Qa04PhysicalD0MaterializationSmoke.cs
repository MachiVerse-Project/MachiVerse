using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04PhysicalD0MaterializationSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04PhysicalD0MaterializerV1.ValidateCanonicalContract();
        VerifySphereClosureAndBounds();
        VerifyTerrainBindingBounds();
        VerifyBatchMaterialization();
        VerifyPartitionMaterialization();
        VerifyInvalidTerrainBindingFailsClosed();
    }

    private static void VerifySphereClosureAndBounds()
    {
        var material = Qa04PhysicalD0MaterializerV1.Create(0, PresenceBinding(0), TerrainBinding);
        material.ValidateRefClosure();

        Require(material.Presence.RecordId == Qa04ReferenceLoadV1.Record(new StableToken("physical.d0-presence"), 0).RecordId,
            "Physical presence must preserve the canonical descriptor record id.");
        Require(material.CollisionShape.Payload is PhysicalSphereShapePayloadV2 sphere && sphere.RadiusMm == 300,
            "Physical ordinal 0 must materialize the canonical 300 mm sphere.");
        Require(material.Occupancy.Payload is PhysicalOccupancyStatePayloadV2,
            "Physical D0 material must include an occupancy arm.");
        var occupancy = (PhysicalOccupancyStatePayloadV2)material.Occupancy.Payload;
        Require(occupancy.AabbMin == new Vec3Int64V1(700, 1700, 2700) &&
                occupancy.AabbMax == new Vec3Int64V1(1300, 2300, 3300),
            "Sphere occupancy AABB must be exact local bounds translated by presence position.");
        Require(occupancy.ContactRefs.Count == 0 && occupancy.OccupancyFlags == 0 && occupancy.CollisionLayer == 1,
            "Physical genesis occupancy flags must match the Alpha 1.1 contract.");
    }

    private static void VerifyTerrainBindingBounds()
    {
        var material = Qa04PhysicalD0MaterializerV1.Create(99, PresenceBinding(99), TerrainBinding);
        Require(material.CollisionShape.Payload is PhysicalTerrainSdfRefShapePayloadV2 terrain &&
                terrain.TerrainRootRef == TerrainBinding(0).TerrainRootRef,
            "Physical ordinal 99 must retain the supplied canonical Terrain root Ref.");

        var occupancy = (PhysicalOccupancyStatePayloadV2)material.Occupancy.Payload;
        Require(occupancy.AabbMin == new Vec3Int64V1(500, 1500, 2500) &&
                occupancy.AabbMax == new Vec3Int64V1(1500, 2500, 3500),
            "Terrain-backed occupancy must use supplied root bounds translated by presence position.");
    }

    private static void VerifyBatchMaterialization()
    {
        var records = Qa04PhysicalD0MaterializerV1.Materialize(100, PresenceBinding, TerrainBinding).ToArray();
        Require(records.Length == 100,
            "Physical D0 batch materializer must preserve requested descriptor count.");
        Require(records.Select(static value => value.Presence.RecordId).Distinct().Count() == 100,
            "Physical D0 presence ids must be unique across canonical descriptors.");
        Require(records.Select(static value => value.Occupancy.RecordId).Distinct().Count() == 100,
            "Physical D0 occupancy ids must be unique across canonical descriptors.");
        Require(records.Select(static value => value.CollisionShape.RecordId).Distinct().Count() == 100,
            "Physical D0 collision-shape ids must be unique across canonical descriptors.");
        Require(records.All(static value => value.Presence.Payload.ShapeRef.RecordId == value.CollisionShape.RecordId),
            "Every Physical D0 presence must close to its own collision-shape record.");
    }

    private static void VerifyPartitionMaterialization()
    {
        var slice = Qa04PhysicalD0PartitionMaterializerV1.Materialize(100, PresenceBinding, TerrainBinding);
        Require(slice.DescriptorCount == 100 && !slice.IsCanonicalCount,
            "Reduced Physical partition materialization must retain its descriptor count without claiming canonical completion.");
        Require(slice.Presence.ItemCount == 100,
            "Physical presence partition must contain exactly one record per descriptor.");
        Require(slice.Occupancy.State.ItemCount == 200,
            "Physical occupancy v2 partition must contain occupancy plus collision-shape for every descriptor.");
        Require(slice.Occupancy.RecordSet.RecordsCanonical.Count(
                    static record => record.Payload is PhysicalOccupancyStatePayloadV2) == 100 &&
                slice.Occupancy.RecordSet.RecordsCanonical.Count(
                    static record => record.Payload is PhysicalCollisionShapePayloadV2) == 100,
            "Physical occupancy v2 mixed state must retain exact one-to-one arm counts.");
    }

    private static void VerifyInvalidTerrainBindingFailsClosed()
    {
        try
        {
            _ = Qa04PhysicalD0MaterializerV1.Create(
                99,
                PresenceBinding(99),
                _ => new Qa04PhysicalTerrainRootBindingV1(
                    new PartitionRecordRefV1("spatial.world_frame", Id(0x44)),
                    new Vec3Int64V1(-1, -1, -1),
                    new Vec3Int64V1(1, 1, 1)));
        }
        catch (InvalidDataException ex) when (ex.Message.Contains("physical-terrain-root-ref-invalid", StringComparison.Ordinal))
        {
            return;
        }

        throw new InvalidOperationException("Invalid Physical Terrain root ownership must fail closed.");
    }

    private static Qa04PhysicalPresenceGenesisBindingV1 PresenceBinding(ulong ordinal)
        => new(
            new PartitionRecordRefV1("resident.identity_lifecycle", Id(checked((byte)(1 + ordinal % 200)))),
            new PartitionRecordRefV1("spatial.world_frame", Id(0xF0)),
            new Vec3Int64V1(1000, 2000, 3000),
            new QuaternionQ30V1(0, 0, 0, 1 << 30),
            new Vec3Int64V1(0, 0, 0),
            new Vec3Int64V1(0, 0, 0),
            null,
            new StableToken("active"));

    private static Qa04PhysicalTerrainRootBindingV1 TerrainBinding(ushort _)
        => new(
            new PartitionRecordRefV1("spatial.terrain_geometry", Id(0xE0)),
            new Vec3Int64V1(-500, -500, -500),
            new Vec3Int64V1(500, 500, 500));

    private static OpaqueId128 Id(byte suffix)
    {
        var bytes = new byte[16];
        bytes[^1] = suffix;
        return OpaqueId128.FromBytes(bytes);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
