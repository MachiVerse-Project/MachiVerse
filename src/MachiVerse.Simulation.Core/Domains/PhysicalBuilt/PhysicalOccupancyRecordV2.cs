using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains.PhysicalBuilt;

/// <summary>
/// Alpha 1.1 で確定した physical.occupancy の record schema v2。
/// StandardDomainPartitionRegistry の v1 baseline は変更せず、明示 migration target として扱う。
/// </summary>
public static class PhysicalOccupancyRecordSchemaV2
{
    public const string PartitionId = PhysicalOccupancyPayloadV1.PartitionId;
    public const string OccupancyKind = "occupancy";
    public const string CollisionShapeKind = "collision_shape";

    public const string SphereShapeKind = "sphere";
    public const string CapsuleShapeKind = "capsule";
    public const string OrientedBoxShapeKind = "oriented_box";
    public const string ConvexPolytopeShapeKind = "convex_polytope";
    public const string TriangleMeshStaticShapeKind = "triangle_mesh_static";
    public const string TerrainSdfRefShapeKind = "terrain_sdf_ref";

    public static SchemaRefV1 RecordSchema { get; } =
        new("domain.physical.occupancy.record", 2, 0);

    public static IReadOnlyList<string> RecordKinds { get; } = Array.AsReadOnly(new[]
    {
        CollisionShapeKind,
        OccupancyKind,
    });

    public static IReadOnlyList<string> ShapeKinds { get; } = Array.AsReadOnly(new[]
    {
        CapsuleShapeKind,
        ConvexPolytopeShapeKind,
        OrientedBoxShapeKind,
        SphereShapeKind,
        TerrainSdfRefShapeKind,
        TriangleMeshStaticShapeKind,
    });

    public static void ValidateCanonicalContract()
    {
        var current = StandardDomainPartitionRegistry.Get(PartitionId);
        if (current.RecordSchema.SchemaId != RecordSchema.SchemaId ||
            current.RecordSchema.Version != new SchemaVersionV1(1, 0) ||
            RecordSchema.Version != new SchemaVersionV1(2, 0))
            throw new InvalidDataException("physical.occupancy-v2.schema-version-contract");

        if (!RecordKinds.SequenceEqual(RecordKinds.OrderBy(static value => value, StringComparer.Ordinal), StringComparer.Ordinal) ||
            RecordKinds.Distinct(StringComparer.Ordinal).Count() != 2)
            throw new InvalidDataException("physical.occupancy-v2.record-kind-contract");
        if (!ShapeKinds.SequenceEqual(ShapeKinds.OrderBy(static value => value, StringComparer.Ordinal), StringComparer.Ordinal) ||
            ShapeKinds.Distinct(StringComparer.Ordinal).Count() != 6)
            throw new InvalidDataException("physical.occupancy-v2.shape-kind-contract");
    }
}

public abstract class PhysicalOccupancyRecordPayloadV2
{
    public abstract string RecordKind { get; }
}

public sealed class PhysicalOccupancyStatePayloadV2 : PhysicalOccupancyRecordPayloadV2
{
    public PhysicalOccupancyStatePayloadV2(
        PartitionRecordRefV1 presenceRef,
        Vec3Int64V1 aabbMin,
        Vec3Int64V1 aabbMax,
        IEnumerable<PartitionRecordRefV1> contactRefs,
        uint occupancyFlags,
        uint collisionLayer)
    {
        if (presenceRef.PartitionId.Value != PhysicalPresencePayloadV1.PartitionId)
            throw new InvalidDataException("physical.occupancy-v2.presence-owner");
        if (aabbMin.X > aabbMax.X || aabbMin.Y > aabbMax.Y || aabbMin.Z > aabbMax.Z)
            throw new InvalidDataException("physical.occupancy-v2.aabb-order");
        ArgumentNullException.ThrowIfNull(contactRefs);
        var contacts = contactRefs.ToArray();
        ValidateCanonicalRefs(contacts, "physical.occupancy-v2.contact-ref-order");

        PresenceRef = presenceRef;
        AabbMin = aabbMin;
        AabbMax = aabbMax;
        ContactRefs = Array.AsReadOnly(contacts);
        OccupancyFlags = occupancyFlags;
        CollisionLayer = collisionLayer;
    }

    public override string RecordKind => PhysicalOccupancyRecordSchemaV2.OccupancyKind;
    public PartitionRecordRefV1 PresenceRef { get; }
    public Vec3Int64V1 AabbMin { get; }
    public Vec3Int64V1 AabbMax { get; }
    public IReadOnlyList<PartitionRecordRefV1> ContactRefs { get; }
    public uint OccupancyFlags { get; }
    public uint CollisionLayer { get; }

    private static void ValidateCanonicalRefs(IReadOnlyList<PartitionRecordRefV1> refs, string failureCode)
    {
        for (var index = 1; index < refs.Count; index++)
        {
            if (CompareRef(refs[index - 1], refs[index]) >= 0)
                throw new InvalidDataException(failureCode);
        }
    }

    private static int CompareRef(PartitionRecordRefV1 left, PartitionRecordRefV1 right)
    {
        var partition = string.CompareOrdinal(left.PartitionId.Value, right.PartitionId.Value);
        return partition != 0 ? partition : left.RecordId.CompareTo(right.RecordId);
    }
}

public abstract class PhysicalCollisionShapePayloadV2 : PhysicalOccupancyRecordPayloadV2
{
    public sealed override string RecordKind => PhysicalOccupancyRecordSchemaV2.CollisionShapeKind;
    public abstract string ShapeKind { get; }
}

public sealed class PhysicalSphereShapePayloadV2 : PhysicalCollisionShapePayloadV2
{
    public PhysicalSphereShapePayloadV2(Vec3MmV1 centerMm, long radiusMm)
    {
        var value = new SphereColliderV1(centerMm, radiusMm);
        value.Validate();
        CenterMm = centerMm;
        RadiusMm = radiusMm;
    }

    public override string ShapeKind => PhysicalOccupancyRecordSchemaV2.SphereShapeKind;
    public Vec3MmV1 CenterMm { get; }
    public long RadiusMm { get; }
    public SphereColliderV1 ToRuntime() => new(CenterMm, RadiusMm);
}

public sealed class PhysicalCapsuleShapePayloadV2 : PhysicalCollisionShapePayloadV2
{
    public PhysicalCapsuleShapePayloadV2(Vec3MmV1 segmentStartMm, Vec3MmV1 segmentEndMm, long radiusMm)
    {
        var value = new CapsuleColliderV1(segmentStartMm, segmentEndMm, radiusMm);
        value.Validate();
        SegmentStartMm = segmentStartMm;
        SegmentEndMm = segmentEndMm;
        RadiusMm = radiusMm;
    }

    public override string ShapeKind => PhysicalOccupancyRecordSchemaV2.CapsuleShapeKind;
    public Vec3MmV1 SegmentStartMm { get; }
    public Vec3MmV1 SegmentEndMm { get; }
    public long RadiusMm { get; }
    public CapsuleColliderV1 ToRuntime() => new(SegmentStartMm, SegmentEndMm, RadiusMm);
}

public sealed class PhysicalOrientedBoxShapePayloadV2 : PhysicalCollisionShapePayloadV2
{
    public PhysicalOrientedBoxShapePayloadV2(
        Vec3MmV1 centerMm,
        Vec3MmV1 halfExtentsMm,
        global::MachiVerse.Simulation.Core.Domains.QuaternionQ30V1 orientation)
    {
        orientation.ValidateCanonical();
        var value = new OrientedBoxColliderV1(centerMm, halfExtentsMm, orientation);
        value.Validate();
        CenterMm = centerMm;
        HalfExtentsMm = halfExtentsMm;
        Orientation = orientation;
    }

    public override string ShapeKind => PhysicalOccupancyRecordSchemaV2.OrientedBoxShapeKind;
    public Vec3MmV1 CenterMm { get; }
    public Vec3MmV1 HalfExtentsMm { get; }
    public global::MachiVerse.Simulation.Core.Domains.QuaternionQ30V1 Orientation { get; }
    public OrientedBoxColliderV1 ToRuntime() => new(CenterMm, HalfExtentsMm, Orientation);
}

public sealed class PhysicalConvexPolytopeShapePayloadV2 : PhysicalCollisionShapePayloadV2
{
    public PhysicalConvexPolytopeShapePayloadV2(IEnumerable<Vec3MmV1> verticesMm)
    {
        ArgumentNullException.ThrowIfNull(verticesMm);
        var vertices = verticesMm.ToArray();
        if (vertices.Length is < 1 or > 256)
            throw new InvalidDataException("physical.occupancy-v2.convex-vertex-count");
        if (vertices.Distinct().Count() != vertices.Length)
            throw new InvalidDataException("physical.occupancy-v2.convex-vertex-duplicate");
        _ = new ConvexPolytopeV1(vertices);
        VerticesMm = Array.AsReadOnly(vertices);
    }

    public override string ShapeKind => PhysicalOccupancyRecordSchemaV2.ConvexPolytopeShapeKind;
    public IReadOnlyList<Vec3MmV1> VerticesMm { get; }
    public ConvexPolytopeV1 ToRuntime() => new(VerticesMm);
}

public sealed class PhysicalTriangleMeshStaticShapePayloadV2 : PhysicalCollisionShapePayloadV2
{
    public PhysicalTriangleMeshStaticShapePayloadV2(IEnumerable<StaticTriangleV1> triangles)
    {
        ArgumentNullException.ThrowIfNull(triangles);
        var values = triangles.ToArray();
        if (values.Length is < 1 or > 65_535)
            throw new InvalidDataException("physical.occupancy-v2.mesh-triangle-count");
        for (var index = 1; index < values.Length; index++)
        {
            if (values[index - 1].CanonicalIndex >= values[index].CanonicalIndex)
                throw new InvalidDataException("physical.occupancy-v2.mesh-triangle-order");
        }
        _ = new TriangleMeshStaticV1(values);
        Triangles = Array.AsReadOnly(values);
    }

    public override string ShapeKind => PhysicalOccupancyRecordSchemaV2.TriangleMeshStaticShapeKind;
    public IReadOnlyList<StaticTriangleV1> Triangles { get; }
    public TriangleMeshStaticV1 ToRuntime() => new(Triangles);
}

public sealed class PhysicalTerrainSdfRefShapePayloadV2 : PhysicalCollisionShapePayloadV2
{
    public PhysicalTerrainSdfRefShapePayloadV2(PartitionRecordRefV1 terrainRootRef)
    {
        if (terrainRootRef.PartitionId.Value != "spatial.terrain_geometry")
            throw new InvalidDataException("physical.occupancy-v2.terrain-root-owner");
        TerrainRootRef = terrainRootRef;
    }

    public override string ShapeKind => PhysicalOccupancyRecordSchemaV2.TerrainSdfRefShapeKind;
    public PartitionRecordRefV1 TerrainRootRef { get; }
}

/// <summary>
/// 共通 Domain record envelope を保ったまま occupancy/collision_shape を同一 v2 schema で保持する。
/// </summary>
public sealed class PhysicalOccupancyRecordMaterialV2
{
    public PhysicalOccupancyRecordMaterialV2(
        OpaqueId128 recordId,
        ulong revision,
        ulong createdStep,
        ulong? retiredStep,
        DetailLevelV1 detailLevel,
        OpaqueId128? lineageRef,
        PhysicalOccupancyRecordPayloadV2 payload)
    {
        if (recordId.IsZero) throw new ArgumentException("Record id ZERO is invalid.", nameof(recordId));
        if (revision == 0) throw new ArgumentOutOfRangeException(nameof(revision));
        if (retiredStep is { } retired && retired < createdStep)
            throw new ArgumentOutOfRangeException(nameof(retiredStep));
        if (lineageRef is { IsZero: true })
            throw new ArgumentException("Lineage id ZERO is invalid.", nameof(lineageRef));
        if (!Enum.IsDefined(detailLevel)) throw new ArgumentOutOfRangeException(nameof(detailLevel));
        ArgumentNullException.ThrowIfNull(payload);

        RecordId = recordId;
        Revision = revision;
        CreatedStep = createdStep;
        RetiredStep = retiredStep;
        DetailLevel = detailLevel;
        LineageRef = lineageRef;
        Payload = payload;
    }

    public OpaqueId128 RecordId { get; }
    public SchemaRefV1 RecordSchema => PhysicalOccupancyRecordSchemaV2.RecordSchema;
    public ulong Revision { get; }
    public ulong CreatedStep { get; }
    public ulong? RetiredStep { get; }
    public DetailLevelV1 DetailLevel { get; }
    public OpaqueId128? LineageRef { get; }
    public PhysicalOccupancyRecordPayloadV2 Payload { get; }

    public static PhysicalOccupancyRecordMaterialV2 MigrateOccupancy(
        DomainRecordEnvelopeV1<PhysicalOccupancyPayloadV1> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var standard = StandardDomainPartitionRegistry.Get(PhysicalOccupancyRecordSchemaV2.PartitionId);
        if (source.RecordSchema != standard.RecordSchema)
            throw new InvalidDataException("physical.occupancy-v2.migration-source-schema");

        var payload = source.Payload;
        return new PhysicalOccupancyRecordMaterialV2(
            source.RecordId,
            source.Revision,
            source.CreatedStep,
            source.RetiredStep,
            source.DetailLevel,
            source.LineageRef,
            new PhysicalOccupancyStatePayloadV2(
                payload.PresenceRef,
                payload.AabbMin,
                payload.AabbMax,
                payload.ContactRefs,
                payload.OccupancyFlags,
                payload.CollisionLayer));
    }
}
