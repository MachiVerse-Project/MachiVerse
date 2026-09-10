using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04PhysicalPresenceGenesisBindingV1(
    PartitionRecordRefV1 SubjectRef,
    PartitionRecordRefV1 FrameRef,
    Vec3Int64V1 Position,
    QuaternionQ30V1 Orientation,
    Vec3Int64V1 LinearVelocity,
    Vec3Int64V1 AngularRateUradPerSecond,
    PartitionRecordRefV1? ContainmentRef,
    StableToken PresenceMode);

public sealed record Qa04PhysicalTerrainRootBindingV1(
    PartitionRecordRefV1 TerrainRootRef,
    Vec3Int64V1 OccupancyAabbMin,
    Vec3Int64V1 OccupancyAabbMax);

public sealed record Qa04PhysicalD0RecordMaterialV1(
    ulong PhysicalOrdinal,
    DomainRecordEnvelopeV1<PhysicalPresencePayloadV1> Presence,
    PhysicalOccupancyRecordMaterialV2 Occupancy,
    PhysicalOccupancyRecordMaterialV2 CollisionShape)
{
    public void ValidateRefClosure()
    {
        if (Presence.RecordId.IsZero || Occupancy.RecordId.IsZero || CollisionShape.RecordId.IsZero)
            throw new InvalidDataException("qa04.materialization.physical-record-id-zero");
        if (Occupancy.RecordId == CollisionShape.RecordId ||
            Presence.RecordId == Occupancy.RecordId ||
            Presence.RecordId == CollisionShape.RecordId)
            throw new InvalidDataException("qa04.materialization.physical-record-id-collision");

        if (Presence.Payload.ShapeRef.PartitionId.Value != PhysicalOccupancyRecordSchemaV2.PartitionId ||
            Presence.Payload.ShapeRef.RecordId != CollisionShape.RecordId)
            throw new InvalidDataException("qa04.materialization.physical-shape-ref-closure");
        if (CollisionShape.Payload is not PhysicalCollisionShapePayloadV2)
            throw new InvalidDataException("qa04.materialization.physical-shape-kind");
        if (Occupancy.Payload is not PhysicalOccupancyStatePayloadV2 occupancy ||
            occupancy.PresenceRef.PartitionId.Value != PhysicalPresencePayloadV1.PartitionId ||
            occupancy.PresenceRef.RecordId != Presence.RecordId)
            throw new InvalidDataException("qa04.materialization.physical-presence-ref-closure");
    }
}

/// <summary>
/// Assembles one canonical perf.reference.v1 Physical D0 descriptor into the three authoritative
/// records required by the Alpha 1.1 contract. Subject/frame/presence-mode ownership and Terrain
/// root bounds are explicit inputs from their owning canonical materializers; this class never
/// invents those cross-partition authorities.
/// </summary>
public static class Qa04PhysicalD0MaterializerV1
{
    public const ulong CanonicalPhysicalCount = Qa04PhysicalShapeMaterializerV1.CanonicalPhysicalCount;

    private static readonly StableToken PhysicalReferenceClass = new("physical.d0-presence");
    private static readonly StableToken PerformanceDomain = new("performance");
    private static readonly StableToken OccupancyPurpose = new("perf.physical-occupancy");

    public static void ValidateCanonicalContract()
    {
        Qa04PhysicalShapeMaterializerV1.ValidateCanonicalContract();
        Qa04ReferenceWorldRefOwnershipContractV1.ValidateCanonicalContract();
        PhysicalOccupancyRecordSchemaV2.ValidateCanonicalContract();

        var closure = Qa04ReferenceWorldRefOwnershipContractV1.RefClosures.Single(
            static value => value.DependencyId.Value == "physical.presence.shape-ref-target");
        if (closure.SourcePartitionId.Value != PhysicalPresencePayloadV1.PartitionId ||
            closure.SourceFieldName != "shape_ref" ||
            closure.TargetPartitionId.Value != PhysicalOccupancyRecordSchemaV2.PartitionId ||
            closure.TargetRecordKind.Value != PhysicalOccupancyRecordSchemaV2.CollisionShapeKind)
            throw new InvalidDataException("qa04.materialization.physical-ref-ownership-drift");
    }

    public static Qa04PhysicalD0RecordMaterialV1 Create(
        ulong physicalOrdinal,
        Qa04PhysicalPresenceGenesisBindingV1 presenceBinding,
        Func<ushort, Qa04PhysicalTerrainRootBindingV1> terrainRootForTile)
    {
        ValidateCanonicalContract();
        ArgumentNullException.ThrowIfNull(presenceBinding);
        ArgumentNullException.ThrowIfNull(terrainRootForTile);

        var descriptor = Qa04ReferenceLoadV1.Record(PhysicalReferenceClass, physicalOrdinal);
        if (descriptor.DetailLevel != DetailLevelV1.D0Entity)
            throw new InvalidDataException("qa04.materialization.physical-detail-level");

        var terrainBinding = terrainRootForTile(descriptor.RegionalTileIndex)
            ?? throw new InvalidDataException("qa04.materialization.physical-terrain-binding-missing");
        ValidateTerrainBinding(terrainBinding);

        var shape = Qa04PhysicalShapeMaterializerV1.CreateShapeRecord(
            physicalOrdinal,
            _ => terrainBinding.TerrainRootRef);
        var shapeRef = new PartitionRecordRefV1(PhysicalOccupancyRecordSchemaV2.PartitionId, shape.RecordId);

        var presencePayload = new PhysicalPresencePayloadV1(
            presenceBinding.SubjectRef,
            presenceBinding.FrameRef,
            presenceBinding.Position,
            presenceBinding.Orientation,
            presenceBinding.LinearVelocity,
            presenceBinding.AngularRateUradPerSecond,
            shapeRef,
            presenceBinding.ContainmentRef,
            presenceBinding.PresenceMode);
        var presenceIdentity = StandardDomainPartitionRegistry.Get(PhysicalPresencePayloadV1.PartitionId);
        var presence = new DomainRecordEnvelopeV1<PhysicalPresencePayloadV1>(
            descriptor.RecordId,
            presenceIdentity.RecordSchema,
            revision: 1,
            createdStep: 0,
            retiredStep: null,
            detailLevel: DetailLevelV1.D0Entity,
            lineageRef: null,
            payload: presencePayload);

        var occupancyId = DerivedIdentity.DeriveEntityId(
            Qa04ReferenceLoadV1.WorldId,
            0,
            PerformanceDomain,
            descriptor.RecordId,
            OccupancyPurpose,
            0);
        var bounds = OccupancyBounds(shape.Payload, terrainBinding, presenceBinding.Position);
        var occupancyPayload = new PhysicalOccupancyStatePayloadV2(
            new PartitionRecordRefV1(PhysicalPresencePayloadV1.PartitionId, descriptor.RecordId),
            bounds.Min,
            bounds.Max,
            Array.Empty<PartitionRecordRefV1>(),
            occupancyFlags: 0,
            collisionLayer: 1);
        var occupancy = new PhysicalOccupancyRecordMaterialV2(
            occupancyId,
            revision: 1,
            createdStep: 0,
            retiredStep: null,
            detailLevel: DetailLevelV1.D0Entity,
            lineageRef: null,
            occupancyPayload);

        var result = new Qa04PhysicalD0RecordMaterialV1(physicalOrdinal, presence, occupancy, shape);
        result.ValidateRefClosure();
        return result;
    }

    public static IEnumerable<Qa04PhysicalD0RecordMaterialV1> Materialize(
        ulong count,
        Func<ulong, Qa04PhysicalPresenceGenesisBindingV1> presenceBindingForOrdinal,
        Func<ushort, Qa04PhysicalTerrainRootBindingV1> terrainRootForTile)
    {
        if (count is 0 or > CanonicalPhysicalCount)
            throw new ArgumentOutOfRangeException(nameof(count));
        ArgumentNullException.ThrowIfNull(presenceBindingForOrdinal);
        ArgumentNullException.ThrowIfNull(terrainRootForTile);

        for (ulong ordinal = 0; ordinal < count; ordinal++)
        {
            var binding = presenceBindingForOrdinal(ordinal)
                ?? throw new InvalidDataException("qa04.materialization.physical-presence-binding-missing");
            yield return Create(ordinal, binding, terrainRootForTile);
        }
    }

    private static (Vec3Int64V1 Min, Vec3Int64V1 Max) OccupancyBounds(
        PhysicalOccupancyRecordPayloadV2 payload,
        Qa04PhysicalTerrainRootBindingV1 terrain,
        Vec3Int64V1 presencePosition)
    {
        if (payload is PhysicalTerrainSdfRefShapePayloadV2)
            return (terrain.OccupancyAabbMin, terrain.OccupancyAabbMax);

        var local = LocalBounds(payload);
        return (Translate(local.Min, presencePosition), Translate(local.Max, presencePosition));
    }

    private static (Vec3Int64V1 Min, Vec3Int64V1 Max) LocalBounds(PhysicalOccupancyRecordPayloadV2 payload)
        => payload switch
        {
            PhysicalSphereShapePayloadV2 sphere => Expand(sphere.CenterMm, sphere.RadiusMm),
            PhysicalCapsuleShapePayloadV2 capsule => CapsuleBounds(capsule),
            PhysicalOrientedBoxShapePayloadV2 box => new(
                new Vec3Int64V1(
                    checked(box.CenterMm.X - box.HalfExtentsMm.X),
                    checked(box.CenterMm.Y - box.HalfExtentsMm.Y),
                    checked(box.CenterMm.Z - box.HalfExtentsMm.Z)),
                new Vec3Int64V1(
                    checked(box.CenterMm.X + box.HalfExtentsMm.X),
                    checked(box.CenterMm.Y + box.HalfExtentsMm.Y),
                    checked(box.CenterMm.Z + box.HalfExtentsMm.Z))),
            PhysicalConvexPolytopeShapePayloadV2 convex => VertexBounds(convex.VerticesMm),
            PhysicalTriangleMeshStaticShapePayloadV2 mesh => TriangleBounds(mesh.Triangles),
            _ => throw new InvalidDataException("qa04.materialization.physical-shape-payload-unsupported"),
        };

    private static (Vec3Int64V1 Min, Vec3Int64V1 Max) Expand(Vec3MmV1 center, long radius)
        => new(
            new Vec3Int64V1(checked(center.X - radius), checked(center.Y - radius), checked(center.Z - radius)),
            new Vec3Int64V1(checked(center.X + radius), checked(center.Y + radius), checked(center.Z + radius)));

    private static (Vec3Int64V1 Min, Vec3Int64V1 Max) CapsuleBounds(PhysicalCapsuleShapePayloadV2 capsule)
        => new(
            new Vec3Int64V1(
                checked(Math.Min(capsule.SegmentStartMm.X, capsule.SegmentEndMm.X) - capsule.RadiusMm),
                checked(Math.Min(capsule.SegmentStartMm.Y, capsule.SegmentEndMm.Y) - capsule.RadiusMm),
                checked(Math.Min(capsule.SegmentStartMm.Z, capsule.SegmentEndMm.Z) - capsule.RadiusMm)),
            new Vec3Int64V1(
                checked(Math.Max(capsule.SegmentStartMm.X, capsule.SegmentEndMm.X) + capsule.RadiusMm),
                checked(Math.Max(capsule.SegmentStartMm.Y, capsule.SegmentEndMm.Y) + capsule.RadiusMm),
                checked(Math.Max(capsule.SegmentStartMm.Z, capsule.SegmentEndMm.Z) + capsule.RadiusMm)));

    private static (Vec3Int64V1 Min, Vec3Int64V1 Max) VertexBounds(IReadOnlyList<Vec3MmV1> vertices)
    {
        if (vertices.Count == 0) throw new InvalidDataException("qa04.materialization.physical-empty-vertices");
        var minX = vertices[0].X; var minY = vertices[0].Y; var minZ = vertices[0].Z;
        var maxX = minX; var maxY = minY; var maxZ = minZ;
        foreach (var vertex in vertices.Skip(1))
        {
            minX = Math.Min(minX, vertex.X); minY = Math.Min(minY, vertex.Y); minZ = Math.Min(minZ, vertex.Z);
            maxX = Math.Max(maxX, vertex.X); maxY = Math.Max(maxY, vertex.Y); maxZ = Math.Max(maxZ, vertex.Z);
        }
        return (new Vec3Int64V1(minX, minY, minZ), new Vec3Int64V1(maxX, maxY, maxZ));
    }

    private static (Vec3Int64V1 Min, Vec3Int64V1 Max) TriangleBounds(IReadOnlyList<StaticTriangleV1> triangles)
    {
        if (triangles.Count == 0) throw new InvalidDataException("qa04.materialization.physical-empty-triangles");
        return VertexBounds(triangles.SelectMany(static triangle => new[] { triangle.A, triangle.B, triangle.C }).ToArray());
    }

    private static Vec3Int64V1 Translate(Vec3Int64V1 local, Vec3Int64V1 position)
        => new(
            checked(local.X + position.X),
            checked(local.Y + position.Y),
            checked(local.Z + position.Z));

    private static void ValidateTerrainBinding(Qa04PhysicalTerrainRootBindingV1 binding)
    {
        if (binding.TerrainRootRef.PartitionId.Value != "spatial.terrain_geometry" ||
            binding.TerrainRootRef.RecordId.IsZero)
            throw new InvalidDataException("qa04.materialization.physical-terrain-root-ref-invalid");
        if (binding.OccupancyAabbMin.X > binding.OccupancyAabbMax.X ||
            binding.OccupancyAabbMin.Y > binding.OccupancyAabbMax.Y ||
            binding.OccupancyAabbMin.Z > binding.OccupancyAabbMax.Z)
            throw new InvalidDataException("qa04.materialization.physical-terrain-aabb-order");
    }
}
