using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed class Qa04PhysicalD0PropertyAssetSupportMaterializationV1
{
    internal Qa04PhysicalD0PropertyAssetSupportMaterializationV1(
        DomainPartitionStateV1<SpatialScopeRegistryPayloadV1> tileScopes,
        DomainPartitionStateV1<SpatialWorldFramePayloadV1> tileFrames,
        IReadOnlyList<DomainRecordEnvelopeV1<SpatialWorldFramePayloadV1>> tileFrameRecordsByOrdinal,
        DomainPartitionStateV1<PhysicalPresencePayloadV1> presences,
        IReadOnlyList<Qa04PhysicalD0RecordMaterialV1> physicalRecordsByOrdinal,
        IReadOnlyList<Qa04PhysicalTerrainRootBindingV1> terrainBindingsByTile,
        IDomainRecordSchemaResolverV1 references)
    {
        TileScopes = tileScopes;
        TileFrames = tileFrames;
        TileFrameRecordsByOrdinal = tileFrameRecordsByOrdinal;
        Presences = presences;
        PhysicalRecordsByOrdinal = physicalRecordsByOrdinal;
        TerrainBindingsByTile = terrainBindingsByTile;
        References = references;
    }

    public DomainPartitionStateV1<SpatialScopeRegistryPayloadV1> TileScopes { get; }
    public DomainPartitionStateV1<SpatialWorldFramePayloadV1> TileFrames { get; }
    public IReadOnlyList<DomainRecordEnvelopeV1<SpatialWorldFramePayloadV1>> TileFrameRecordsByOrdinal { get; }
    public DomainPartitionStateV1<PhysicalPresencePayloadV1> Presences { get; }
    public IReadOnlyList<Qa04PhysicalD0RecordMaterialV1> PhysicalRecordsByOrdinal { get; }
    public IReadOnlyList<Qa04PhysicalTerrainRootBindingV1> TerrainBindingsByTile { get; }
    public IDomainRecordSchemaResolverV1 References { get; }
}

/// <summary>
/// Production support authority approved in #240 for the first 50,000 Physical D0 records consumed
/// by the PropertyRight benchmark slice. It does not define Physical ordinals 50,000..499,999 or a
/// general Spatial frame hierarchy.
/// </summary>
public static class Qa04PhysicalD0PropertyAssetSupportCanonicalAuthorityV1
{
    public const ulong CanonicalPhysicalCount = 50_000;
    public const int CanonicalTileFrameCount = Qa04SpatialTileScopeAuthorityV1.CanonicalScopeCount;

    public static readonly StableToken TileFrameKind = new("perf.world-aligned-tile-frame");
    public static readonly StableToken PresenceMode = new("perf.free-moving");

    private static readonly StableToken PhysicalReferenceClass = new("physical.d0-presence");
    private static readonly StableToken SpatialDomain = new("spatial");
    private static readonly StableToken TileFrameCreationKind = new("perf.tile-frame");
    private static readonly Vec3Int64V1 ZeroVector = new(0, 0, 0);
    private static readonly QuaternionQ30V1 IdentityOrientation = new(0, 0, 0, 1 << 30);

    public static void ValidateCanonicalContract()
    {
        Qa04ReferenceLoadV1.ValidateCanonicalContract();
        Qa04ReferenceWorldMaterializerV1.ValidateCanonicalContract();
        Qa04SpatialTileScopeAuthorityV1.ValidateCanonicalContract();
        Qa04TerrainRootMaterializerV1.ValidateCanonicalContract();
        Qa04TerrainCanonicalContentSourceV1.ValidateCanonicalContract();
        Qa04PhysicalD0MaterializerV1.ValidateCanonicalContract();

        if (CanonicalPhysicalCount != 50_000 || CanonicalPhysicalCount > 100_000 ||
            CanonicalPhysicalCount > Qa04ReferenceWorldMaterializerV1.CanonicalResidentCount ||
            CanonicalTileFrameCount != 4_096 || Qa04ReferenceLoadV1.RegionalTileCount != CanonicalTileFrameCount ||
            Qa04TerrainCanonicalContentSourceV1.TileWidthMm != 512_000 ||
            TileFrameKind.Value != "perf.world-aligned-tile-frame" || PresenceMode.Value != "perf.free-moving")
            throw new InvalidDataException("qa04.physical.property-asset-support-contract-drift");

        var physicalCount = Qa04ReferenceLoadV1.RecordClasses.Single(entry => entry.ClassToken == PhysicalReferenceClass).Count;
        if (physicalCount != Qa04PhysicalD0MaterializerV1.CanonicalPhysicalCount || CanonicalPhysicalCount > physicalCount)
            throw new InvalidDataException("qa04.physical.property-asset-support-physical-count-drift");
        if (StandardDomainPartitionRegistry.Get(SpatialWorldFramePayloadV1.PartitionId).OwnerDomain.Value != "spatial")
            throw new InvalidDataException("qa04.physical.property-asset-support-frame-owner-drift");
    }

    public static OpaqueId128 TileFrameId(ushort tileIndex)
    {
        if (tileIndex >= CanonicalTileFrameCount) throw new ArgumentOutOfRangeException(nameof(tileIndex));
        return DerivedIdentity.DeriveEntityId(
            Qa04ReferenceLoadV1.WorldId,
            creationStep: 0,
            SpatialDomain,
            Qa04SpatialTileScopeAuthorityV1.ScopeId(tileIndex),
            TileFrameCreationKind,
            localOrdinal: 0);
    }

    public static PartitionRecordRefV1 TileFrameRef(ushort tileIndex)
        => new(SpatialWorldFramePayloadV1.PartitionId, TileFrameId(tileIndex));

    public static DomainRecordEnvelopeV1<SpatialWorldFramePayloadV1> CreateCanonicalTileFrame(ushort tileIndex)
    {
        ValidateCanonicalContract();
        return CreateCanonicalTileFrameValidated(tileIndex);
    }

    public static Qa04PhysicalPresenceGenesisBindingV1 CreateCanonicalPresenceBinding(ulong physicalOrdinal)
    {
        ValidateCanonicalContract();
        return CreateCanonicalPresenceBindingValidated(physicalOrdinal);
    }

    public static Qa04PhysicalTerrainRootBindingV1 CreateCanonicalTerrainBinding(ushort tileIndex)
    {
        ValidateCanonicalContract();
        return CreateCanonicalTerrainBindingValidated(tileIndex);
    }

    public static Qa04PhysicalD0PropertyAssetSupportMaterializationV1 MaterializeCanonical()
    {
        ValidateCanonicalContract();
        var references = new CanonicalReferenceResolver();

        var tileScopes = Qa04SpatialTileScopeAuthorityV1.MaterializeCanonical();
        foreach (var scope in tileScopes.RecordsCanonical)
            references.Add(new PartitionRecordRefV1(SpatialScopeRegistryPayloadV1.PartitionId, scope.RecordId), scope.RecordSchema);

        var frameIdentity = StandardDomainPartitionRegistry.Get(SpatialWorldFramePayloadV1.PartitionId);
        var frameRecords = new DomainRecordEnvelopeV1<SpatialWorldFramePayloadV1>[CanonicalTileFrameCount];
        for (ushort tile = 0; tile < CanonicalTileFrameCount; tile++)
        {
            var frame = CreateCanonicalTileFrameValidated(tile);
            ValidateCanonicalTileFrameRecord(tile, frame, references);
            frameRecords[tile] = frame;
            references.Add(new PartitionRecordRefV1(SpatialWorldFramePayloadV1.PartitionId, frame.RecordId), frame.RecordSchema);
        }
        var tileFrames = new DomainPartitionStateV1<SpatialWorldFramePayloadV1>(frameIdentity, frameRecords);
        if (tileFrames.ItemCount != CanonicalTileFrameCount)
            throw new InvalidDataException("qa04.physical.property-asset-support-frame-count-drift");

        for (ulong ordinal = 0; ordinal < CanonicalPhysicalCount; ordinal++)
        {
            var resident = Qa04ReferenceWorldMaterializerV1.CreateResidentRecord(ordinal);
            references.Add(new PartitionRecordRefV1(ResidentIdentityLifecyclePayloadV1.PartitionId, resident.RecordId), resident.RecordSchema);
        }

        var terrainBindings = new Qa04PhysicalTerrainRootBindingV1[CanonicalTileFrameCount];
        for (ushort tile = 0; tile < CanonicalTileFrameCount; tile++)
        {
            var terrain = CreateCanonicalTerrainBindingValidated(tile);
            terrainBindings[tile] = terrain;
            references.Add(terrain.TerrainRootRef, SpatialTerrainGeometryRecordSchemaV2.RecordSchema);
        }

        var presenceBindings = new Qa04PhysicalPresenceGenesisBindingV1[checked((int)CanonicalPhysicalCount)];
        for (ulong ordinal = 0; ordinal < CanonicalPhysicalCount; ordinal++)
            presenceBindings[checked((int)ordinal)] = CreateCanonicalPresenceBindingValidated(ordinal);

        var physical = Qa04PhysicalD0MaterializerV1.Materialize(
            CanonicalPhysicalCount,
            ordinal => presenceBindings[checked((int)ordinal)],
            tile => terrainBindings[tile]).ToArray();

        foreach (var material in physical)
        {
            references.Add(new PartitionRecordRefV1(PhysicalPresencePayloadV1.PartitionId, material.Presence.RecordId), material.Presence.RecordSchema);
            references.Add(new PartitionRecordRefV1(PhysicalOccupancyRecordSchemaV2.PartitionId, material.Occupancy.RecordId), material.Occupancy.RecordSchema);
            references.Add(new PartitionRecordRefV1(PhysicalOccupancyRecordSchemaV2.PartitionId, material.CollisionShape.RecordId), material.CollisionShape.RecordSchema);
        }

        for (ulong ordinal = 0; ordinal < CanonicalPhysicalCount; ordinal++)
            ValidateCanonicalPhysicalRecord(ordinal, physical[checked((int)ordinal)], references, terrainBindings);
        ValidatePhysicalPopulation(physical);

        var presences = new DomainPartitionStateV1<PhysicalPresencePayloadV1>(
            StandardDomainPartitionRegistry.Get(PhysicalPresencePayloadV1.PartitionId),
            physical.Select(static material => material.Presence));
        if (presences.ItemCount != CanonicalPhysicalCount)
            throw new InvalidDataException("qa04.physical.property-asset-support-presence-count-drift");

        return new Qa04PhysicalD0PropertyAssetSupportMaterializationV1(
            tileScopes, tileFrames, Array.AsReadOnly(frameRecords), presences,
            Array.AsReadOnly(physical), Array.AsReadOnly(terrainBindings), references);
    }

    public static void ValidateCanonicalTileFrameRecord(
        ushort tileIndex,
        DomainRecordEnvelopeV1<SpatialWorldFramePayloadV1> record,
        IDomainRecordSchemaResolverV1 references)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(references);
        if (tileIndex >= CanonicalTileFrameCount) throw new ArgumentOutOfRangeException(nameof(tileIndex));

        var identity = StandardDomainPartitionRegistry.Get(SpatialWorldFramePayloadV1.PartitionId);
        if (record.RecordId != TileFrameId(tileIndex) || record.RecordSchema != identity.RecordSchema ||
            record.Revision != 1 || record.CreatedStep != 0 || record.RetiredStep is not null ||
            record.DetailLevel != DetailLevelV1.D2RegionalAggregate || record.LineageRef is not null)
            throw new InvalidDataException("qa04.physical.property-asset-support-frame-envelope-drift");

        var payload = record.Payload;
        if (payload.FrameKind != TileFrameKind || payload.ParentFrame is not null ||
            payload.Translation != ZeroVector || payload.Rotation != IdentityOrientation ||
            payload.ValidScope != Qa04SpatialTileScopeAuthorityV1.ScopeRef(tileIndex) || payload.TransformRevision != 1)
            throw new InvalidDataException("qa04.physical.property-asset-support-frame-payload-drift");

        new StandardDomainPayloadCodecValidatorV1().Validate(
            SpatialWorldFramePayloadV1.PartitionId, payload.ToStandardPayload(), references);
    }

    public static void ValidateCanonicalTerrainBinding(ushort tileIndex, Qa04PhysicalTerrainRootBindingV1 binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (tileIndex >= CanonicalTileFrameCount) throw new ArgumentOutOfRangeException(nameof(tileIndex));
        if (binding != CreateCanonicalTerrainBindingValidated(tileIndex))
            throw new InvalidDataException("qa04.physical.property-asset-support-terrain-binding-drift");
    }

    public static void ValidateCanonicalPhysicalRecord(
        ulong physicalOrdinal,
        Qa04PhysicalD0RecordMaterialV1 material,
        IDomainRecordSchemaResolverV1 references,
        IReadOnlyList<Qa04PhysicalTerrainRootBindingV1> terrainBindings)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(terrainBindings);
        if (physicalOrdinal >= CanonicalPhysicalCount) throw new ArgumentOutOfRangeException(nameof(physicalOrdinal));
        if (terrainBindings.Count != CanonicalTileFrameCount)
            throw new InvalidDataException("qa04.physical.property-asset-support-terrain-binding-count-drift");
        if (material.PhysicalOrdinal != physicalOrdinal)
            throw new InvalidDataException("qa04.physical.property-asset-support-ordinal-drift");

        material.ValidateRefClosure();
        var descriptor = Qa04ReferenceLoadV1.Record(PhysicalReferenceClass, physicalOrdinal);
        var expected = CreateCanonicalPresenceBindingValidated(physicalOrdinal);
        var presence = material.Presence;
        if (presence.RecordId != descriptor.RecordId ||
            presence.RecordSchema != StandardDomainPartitionRegistry.Get(PhysicalPresencePayloadV1.PartitionId).RecordSchema ||
            presence.Revision != 1 || presence.CreatedStep != 0 || presence.RetiredStep is not null ||
            presence.DetailLevel != DetailLevelV1.D0Entity || presence.LineageRef is not null)
            throw new InvalidDataException("qa04.physical.property-asset-support-presence-envelope-drift");

        var payload = presence.Payload;
        if (payload.SubjectRef != expected.SubjectRef || payload.FrameRef != expected.FrameRef ||
            payload.Position != expected.Position || payload.Orientation != expected.Orientation ||
            payload.LinearVelocity != expected.LinearVelocity ||
            payload.AngularRateUradPerSecond != expected.AngularRateUradPerSecond ||
            payload.ContainmentRef != expected.ContainmentRef || payload.PresenceMode != expected.PresenceMode)
            throw new InvalidDataException("qa04.physical.property-asset-support-presence-payload-drift");

        if (material.Occupancy.RecordSchema != PhysicalOccupancyRecordSchemaV2.RecordSchema ||
            material.CollisionShape.RecordSchema != PhysicalOccupancyRecordSchemaV2.RecordSchema ||
            material.Occupancy.Revision != 1 || material.CollisionShape.Revision != 1 ||
            material.Occupancy.CreatedStep != 0 || material.CollisionShape.CreatedStep != 0 ||
            material.Occupancy.RetiredStep is not null || material.CollisionShape.RetiredStep is not null ||
            material.Occupancy.DetailLevel != DetailLevelV1.D0Entity || material.CollisionShape.DetailLevel != DetailLevelV1.D0Entity)
            throw new InvalidDataException("qa04.physical.property-asset-support-occupancy-envelope-drift");

        new StandardDomainPayloadCodecValidatorV1().Validate(
            PhysicalPresencePayloadV1.PartitionId, payload.ToStandardPayload(), references);

        if (material.CollisionShape.Payload is PhysicalTerrainSdfRefShapePayloadV2 terrainShape)
        {
            var expectedTerrain = terrainBindings[descriptor.RegionalTileIndex];
            if (terrainShape.TerrainRootRef != expectedTerrain.TerrainRootRef ||
                material.Occupancy.Payload is not PhysicalOccupancyStatePayloadV2 occupancy ||
                occupancy.AabbMin != expectedTerrain.OccupancyAabbMin || occupancy.AabbMax != expectedTerrain.OccupancyAabbMax)
                throw new InvalidDataException("qa04.physical.property-asset-support-terrain-sdf-drift");
        }
    }

    public static void ValidatePhysicalPopulation(IEnumerable<Qa04PhysicalD0RecordMaterialV1> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var material = records.ToArray();
        if ((ulong)material.Length != CanonicalPhysicalCount)
            throw new InvalidDataException("qa04.physical.property-asset-support-population-drift");

        var ids = new HashSet<OpaqueId128>();
        var subjects = new HashSet<PartitionRecordRefV1>();
        var shapeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < material.Length; index++)
        {
            var record = material[index];
            if (record.PhysicalOrdinal != checked((ulong)index))
                throw new InvalidDataException("qa04.physical.property-asset-support-population-order-drift");
            if (!ids.Add(record.Presence.RecordId))
                throw new InvalidDataException("qa04.physical.property-asset-support-presence-id-duplicate");
            if (!subjects.Add(record.Presence.Payload.SubjectRef))
                throw new InvalidDataException("qa04.physical.property-asset-support-subject-duplicate");
            var kind = ShapeKind(record.CollisionShape.Payload);
            shapeCounts[kind] = shapeCounts.GetValueOrDefault(kind) + 1;
        }

        RequireShapeCount(shapeCounts, PhysicalOccupancyRecordSchemaV2.SphereShapeKind, 25_000);
        RequireShapeCount(shapeCounts, PhysicalOccupancyRecordSchemaV2.CapsuleShapeKind, 10_000);
        RequireShapeCount(shapeCounts, PhysicalOccupancyRecordSchemaV2.OrientedBoxShapeKind, 10_000);
        RequireShapeCount(shapeCounts, PhysicalOccupancyRecordSchemaV2.ConvexPolytopeShapeKind, 4_000);
        RequireShapeCount(shapeCounts, PhysicalOccupancyRecordSchemaV2.TriangleMeshStaticShapeKind, 500);
        RequireShapeCount(shapeCounts, PhysicalOccupancyRecordSchemaV2.TerrainSdfRefShapeKind, 500);
        if (shapeCounts.Values.Sum() != material.Length || shapeCounts.Count != 6)
            throw new InvalidDataException("qa04.physical.property-asset-support-shape-mix-drift");
    }

    private static DomainRecordEnvelopeV1<SpatialWorldFramePayloadV1> CreateCanonicalTileFrameValidated(ushort tileIndex)
    {
        if (tileIndex >= CanonicalTileFrameCount) throw new ArgumentOutOfRangeException(nameof(tileIndex));
        var identity = StandardDomainPartitionRegistry.Get(SpatialWorldFramePayloadV1.PartitionId);
        return new DomainRecordEnvelopeV1<SpatialWorldFramePayloadV1>(
            TileFrameId(tileIndex), identity.RecordSchema, revision: 1, createdStep: 0, retiredStep: null,
            DetailLevelV1.D2RegionalAggregate, lineageRef: null,
            new SpatialWorldFramePayloadV1(
                TileFrameKind, ParentFrame: null, ZeroVector, IdentityOrientation,
                Qa04SpatialTileScopeAuthorityV1.ScopeRef(tileIndex), TransformRevision: 1));
    }

    private static Qa04PhysicalPresenceGenesisBindingV1 CreateCanonicalPresenceBindingValidated(ulong physicalOrdinal)
    {
        if (physicalOrdinal >= CanonicalPhysicalCount) throw new ArgumentOutOfRangeException(nameof(physicalOrdinal));
        var descriptor = Qa04ReferenceLoadV1.Record(PhysicalReferenceClass, physicalOrdinal);
        var resident = Qa04ReferenceWorldMaterializerV1.CreateResidentRecord(physicalOrdinal);
        if (resident.DetailLevel != DetailLevelV1.D0Entity)
            throw new InvalidDataException("qa04.physical.property-asset-support-resident-detail-drift");

        var (u, v) = Qa04ReferenceLoadV1.PositionWithinTile(descriptor.RecordId, step: 0);
        var row = descriptor.RegionalTileIndex / Qa04ReferenceLoadV1.RegionalTileColumns;
        var column = descriptor.RegionalTileIndex % Qa04ReferenceLoadV1.RegionalTileColumns;
        var width = Qa04TerrainCanonicalContentSourceV1.TileWidthMm;
        var x = checked((long)column * width + checked((long)Math.Floor(u * width)));
        var y = checked((long)row * width + checked((long)Math.Floor(v * width)));
        var z = Qa04TerrainCanonicalContentSourceV1.HeightMm(x, y);

        return new Qa04PhysicalPresenceGenesisBindingV1(
            new PartitionRecordRefV1(ResidentIdentityLifecyclePayloadV1.PartitionId, resident.RecordId),
            TileFrameRef(descriptor.RegionalTileIndex),
            new Vec3Int64V1(x, y, z), IdentityOrientation, ZeroVector, ZeroVector,
            ContainmentRef: null, PresenceMode);
    }

    private static Qa04PhysicalTerrainRootBindingV1 CreateCanonicalTerrainBindingValidated(ushort tileIndex)
    {
        if (tileIndex >= CanonicalTileFrameCount) throw new ArgumentOutOfRangeException(nameof(tileIndex));
        var tile = Qa04TerrainRootMaterializerV1.MaterializeTile(tileIndex, Qa04SpatialTileScopeAuthorityV1.ScopeRef);
        if (tile.Root.RecordId != Qa04TerrainRootMaterializerV1.RootId(tileIndex) ||
            tile.Root.Payload is not SpatialTerrainRootPayloadV2 root ||
            root.ScopeRef != Qa04SpatialTileScopeAuthorityV1.ScopeRef(tileIndex) ||
            tile.Anchor.RecordId != Qa04TerrainRootMaterializerV1.AnchorId(tileIndex) ||
            tile.Anchor.Payload is not SpatialTerrainBrickPayloadV2 anchor || anchor.Level != 3)
            throw new InvalidDataException("qa04.physical.property-asset-support-terrain-authority-drift");

        var spacing = checked((long)anchor.SampleSpacingMm);
        var min = new Vec3Int64V1(
            checked((long)anchor.CellOrigin.X * spacing),
            checked((long)anchor.CellOrigin.Y * spacing),
            checked((long)anchor.CellOrigin.Z * spacing));
        var width = checked((long)TerrainBrickV1.CellsPerAxis * spacing);
        var max = new Vec3Int64V1(checked(min.X + width), checked(min.Y + width), checked(min.Z + width));
        return new Qa04PhysicalTerrainRootBindingV1(
            new PartitionRecordRefV1(SpatialTerrainGeometryRecordSchemaV2.PartitionId, tile.Root.RecordId), min, max);
    }

    private static string ShapeKind(PhysicalOccupancyRecordPayloadV2 payload)
        => payload switch
        {
            PhysicalSphereShapePayloadV2 => PhysicalOccupancyRecordSchemaV2.SphereShapeKind,
            PhysicalCapsuleShapePayloadV2 => PhysicalOccupancyRecordSchemaV2.CapsuleShapeKind,
            PhysicalOrientedBoxShapePayloadV2 => PhysicalOccupancyRecordSchemaV2.OrientedBoxShapeKind,
            PhysicalConvexPolytopeShapePayloadV2 => PhysicalOccupancyRecordSchemaV2.ConvexPolytopeShapeKind,
            PhysicalTriangleMeshStaticShapePayloadV2 => PhysicalOccupancyRecordSchemaV2.TriangleMeshStaticShapeKind,
            PhysicalTerrainSdfRefShapePayloadV2 => PhysicalOccupancyRecordSchemaV2.TerrainSdfRefShapeKind,
            _ => throw new InvalidDataException("qa04.physical.property-asset-support-shape-kind-unsupported"),
        };

    private static void RequireShapeCount(IReadOnlyDictionary<string, int> counts, string shapeKind, int expected)
    {
        if (!counts.TryGetValue(shapeKind, out var actual) || actual != expected)
            throw new InvalidDataException($"qa04.physical.property-asset-support-shape-count:{shapeKind}");
    }

    private sealed class CanonicalReferenceResolver : IDomainRecordSchemaResolverV1
    {
        private readonly Dictionary<PartitionRecordRefV1, SchemaRefV1> _records = new();

        public void Add(PartitionRecordRefV1 reference, SchemaRefV1 schema)
        {
            if (reference.RecordId.IsZero)
                throw new InvalidDataException("qa04.physical.property-asset-support-reference-zero");
            if (!_records.TryAdd(reference, schema) && _records[reference] != schema)
                throw new InvalidDataException("qa04.physical.property-asset-support-reference-schema-conflict");
        }

        public bool Exists(PartitionRecordRefV1 reference) => _records.ContainsKey(reference);
        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
            => _records.TryGetValue(reference, out schema);
    }
}
