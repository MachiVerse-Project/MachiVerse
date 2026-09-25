using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed class Qa04PhysicalD0FullReferenceWorldCanonicalMaterializationV1
{
    internal Qa04PhysicalD0FullReferenceWorldCanonicalMaterializationV1(
        DomainPartitionStateV1<SpatialWorldFramePayloadV1> tileFrames,
        PartitionStateHeaderV1 tileFrameHeader,
        DomainPartitionStateV1<PhysicalPresencePayloadV1> presences,
        PartitionStateHeaderV1 presenceHeader,
        PhysicalOccupancyPartitionStateV2 occupancy,
        PartitionStateHeaderV1 occupancyHeader,
        IDomainRecordSchemaResolverV1 references)
    {
        TileFrames = tileFrames ?? throw new ArgumentNullException(nameof(tileFrames));
        TileFrameHeader = tileFrameHeader ?? throw new ArgumentNullException(nameof(tileFrameHeader));
        Presences = presences ?? throw new ArgumentNullException(nameof(presences));
        PresenceHeader = presenceHeader ?? throw new ArgumentNullException(nameof(presenceHeader));
        Occupancy = occupancy ?? throw new ArgumentNullException(nameof(occupancy));
        OccupancyHeader = occupancyHeader ?? throw new ArgumentNullException(nameof(occupancyHeader));
        References = references ?? throw new ArgumentNullException(nameof(references));

        if (TileFrames.ItemCount != Qa04PhysicalD0FullReferenceWorldCanonicalAuthorityV1.CanonicalTileFrameCount ||
            Presences.ItemCount != Qa04PhysicalD0FullReferenceWorldCanonicalAuthorityV1.CanonicalPhysicalCount ||
            Occupancy.State.ItemCount != checked(Qa04PhysicalD0FullReferenceWorldCanonicalAuthorityV1.CanonicalPhysicalCount * 2UL) ||
            PresenceHeader.ItemCount != Qa04PhysicalD0FullReferenceWorldCanonicalAuthorityV1.CanonicalPhysicalCount ||
            OccupancyHeader.ItemCount != checked(Qa04PhysicalD0FullReferenceWorldCanonicalAuthorityV1.CanonicalPhysicalCount * 2UL) ||
            TileFrameHeader.ItemCount != Qa04PhysicalD0FullReferenceWorldCanonicalAuthorityV1.CanonicalTileFrameCount)
            throw new InvalidDataException("qa04.physical.full-reference-world-result-count-drift");
    }

    public DomainPartitionStateV1<SpatialWorldFramePayloadV1> TileFrames { get; }
    public PartitionStateHeaderV1 TileFrameHeader { get; }
    public DomainPartitionStateV1<PhysicalPresencePayloadV1> Presences { get; }
    public PartitionStateHeaderV1 PresenceHeader { get; }
    public PhysicalOccupancyPartitionStateV2 Occupancy { get; }
    public PartitionStateHeaderV1 OccupancyHeader { get; }
    public IDomainRecordSchemaResolverV1 References { get; }
}

/// <summary>
/// Full perf.reference.v1 Physical D0 genesis authority used by Gate 2 Step 13. This is a
/// benchmark-only extension of the previously approved first-50,000 PropertyRight support binding;
/// it does not define a general Physical subject ontology or universal frame semantics.
/// </summary>
public static class Qa04PhysicalD0FullReferenceWorldCanonicalAuthorityV1
{
    public const ulong CanonicalPhysicalCount = Qa04PhysicalD0MaterializerV1.CanonicalPhysicalCount;
    public const int CanonicalTileFrameCount = Qa04PhysicalD0PropertyAssetSupportCanonicalAuthorityV1.CanonicalTileFrameCount;

    private static readonly StableToken PhysicalReferenceClass = new("physical.d0-presence");
    private static readonly Vec3Int64V1 ZeroVector = new(0, 0, 0);
    private static readonly QuaternionQ30V1 IdentityOrientation = new(0, 0, 0, 1 << 30);

    public static void ValidateCanonicalContract()
    {
        Qa04ReferenceLoadV1.ValidateCanonicalContract();
        Qa04ReferenceWorldMaterializerV1.ValidateCanonicalContract();
        Qa04PhysicalD0PropertyAssetSupportCanonicalAuthorityV1.ValidateCanonicalContract();
        Qa04PhysicalD0MaterializerV1.ValidateCanonicalContract();
        Qa04PhysicalShapeMaterializerV1.ValidateCanonicalContract();
        Qa04TerrainCanonicalContentSourceV1.ValidateCanonicalContract();

        var descriptorCount = Qa04ReferenceLoadV1.RecordClasses
            .Single(entry => entry.ClassToken == PhysicalReferenceClass)
            .Count;
        if (CanonicalPhysicalCount != 500_000 ||
            descriptorCount != CanonicalPhysicalCount ||
            CanonicalPhysicalCount > Qa04ReferenceWorldMaterializerV1.CanonicalResidentCount ||
            CanonicalTileFrameCount != 4_096 ||
            Qa04ReferenceLoadV1.RegionalTileCount != CanonicalTileFrameCount ||
            Qa04PhysicalD0PropertyAssetSupportCanonicalAuthorityV1.PresenceMode.Value != "perf.free-moving")
            throw new InvalidDataException("qa04.physical.full-reference-world-contract-drift");
    }

    public static Qa04PhysicalPresenceGenesisBindingV1 CreateCanonicalPresenceBinding(ulong physicalOrdinal)
    {
        ValidateCanonicalContract();
        return CreateCanonicalPresenceBindingValidated(physicalOrdinal);
    }

    public static Qa04PhysicalD0FullReferenceWorldCanonicalMaterializationV1 MaterializeCanonical()
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
            var frame = Qa04PhysicalD0PropertyAssetSupportCanonicalAuthorityV1.CreateCanonicalTileFrame(tile);
            Qa04PhysicalD0PropertyAssetSupportCanonicalAuthorityV1.ValidateCanonicalTileFrameRecord(tile, frame, references);
            frameRecords[tile] = frame;
            references.Add(new PartitionRecordRefV1(SpatialWorldFramePayloadV1.PartitionId, frame.RecordId), frame.RecordSchema);
        }
        var tileFrames = new DomainPartitionStateV1<SpatialWorldFramePayloadV1>(frameIdentity, frameRecords);
        var tileFrameHeader = PartitionStateHeaderV1.CreateCanonical(
            tileFrames,
            revision: 1,
            basisStep: 0,
            detailLevel: DetailLevelV1.D2RegionalAggregate,
            static payload => payload.CanonicalDigest());

        for (ulong ordinal = 0; ordinal < CanonicalPhysicalCount; ordinal++)
        {
            var resident = Qa04ReferenceWorldMaterializerV1.CreateResidentRecord(ordinal);
            references.Add(
                new PartitionRecordRefV1(ResidentIdentityLifecyclePayloadV1.PartitionId, resident.RecordId),
                resident.RecordSchema);
        }

        var terrainBindings = new Qa04PhysicalTerrainRootBindingV1[CanonicalTileFrameCount];
        for (ushort tile = 0; tile < CanonicalTileFrameCount; tile++)
        {
            var terrain = Qa04PhysicalD0PropertyAssetSupportCanonicalAuthorityV1.CreateCanonicalTerrainBinding(tile);
            Qa04PhysicalD0PropertyAssetSupportCanonicalAuthorityV1.ValidateCanonicalTerrainBinding(tile, terrain);
            terrainBindings[tile] = terrain;
            references.Add(terrain.TerrainRootRef, SpatialTerrainGeometryRecordSchemaV2.RecordSchema);
        }

        var physical = Qa04PhysicalD0PartitionMaterializerV1.MaterializeCanonical(
            CreateCanonicalPresenceBindingValidated,
            tile => terrainBindings[tile]);

        var shapeCounts = new Dictionary<string, ulong>(StringComparer.Ordinal);
        foreach (var record in physical.Occupancy.RecordSet.RecordsCanonical)
        {
            references.Add(
                new PartitionRecordRefV1(PhysicalOccupancyRecordSchemaV2.PartitionId, record.RecordId),
                record.RecordSchema);
            if (record.Payload is PhysicalCollisionShapePayloadV2 shape)
                shapeCounts[shape.ShapeKind] = checked(shapeCounts.GetValueOrDefault(shape.ShapeKind) + 1UL);
        }

        var validator = new StandardDomainPayloadCodecValidatorV1();
        var subjects = new HashSet<PartitionRecordRefV1>();
        foreach (var record in physical.Presence.RecordsCanonical)
        {
            references.Add(
                new PartitionRecordRefV1(PhysicalPresencePayloadV1.PartitionId, record.RecordId),
                record.RecordSchema);
            if (!subjects.Add(record.Payload.SubjectRef))
                throw new InvalidDataException("qa04.physical.full-reference-world-subject-duplicate");
            validator.Validate(
                PhysicalPresencePayloadV1.PartitionId,
                record.Payload.ToStandardPayload(),
                references);
        }

        var firstFiftyThousand = Qa04PhysicalD0PropertyAssetSupportCanonicalAuthorityV1.MaterializeCanonical();
        foreach (var approved in firstFiftyThousand.Presences.RecordsCanonical)
        {
            if (!physical.Presence.TryGet(approved.RecordId, out var full) || full is null ||
                full.RecordSchema != approved.RecordSchema ||
                full.Revision != approved.Revision ||
                full.CreatedStep != approved.CreatedStep ||
                full.RetiredStep != approved.RetiredStep ||
                full.DetailLevel != approved.DetailLevel ||
                full.LineageRef != approved.LineageRef ||
                full.Payload != approved.Payload)
                throw new InvalidDataException("qa04.physical.full-reference-world-first-50000-compatibility-drift");
        }

        RequireShapeCount(shapeCounts, PhysicalOccupancyRecordSchemaV2.SphereShapeKind, 250_000);
        RequireShapeCount(shapeCounts, PhysicalOccupancyRecordSchemaV2.CapsuleShapeKind, 100_000);
        RequireShapeCount(shapeCounts, PhysicalOccupancyRecordSchemaV2.OrientedBoxShapeKind, 100_000);
        RequireShapeCount(shapeCounts, PhysicalOccupancyRecordSchemaV2.ConvexPolytopeShapeKind, 40_000);
        RequireShapeCount(shapeCounts, PhysicalOccupancyRecordSchemaV2.TriangleMeshStaticShapeKind, 5_000);
        RequireShapeCount(shapeCounts, PhysicalOccupancyRecordSchemaV2.TerrainSdfRefShapeKind, 5_000);
        if (subjects.Count != checked((int)CanonicalPhysicalCount) ||
            shapeCounts.Count != 6 ||
            shapeCounts.Values.Aggregate(0UL, static (sum, value) => checked(sum + value)) != CanonicalPhysicalCount)
            throw new InvalidDataException("qa04.physical.full-reference-world-population-drift");

        var presenceHeader = PartitionStateHeaderV1.CreateCanonical(
            physical.Presence,
            revision: 1,
            basisStep: 0,
            detailLevel: DetailLevelV1.D0Entity,
            payload => StandardDomainPayloadCanonicalDigestV1.Compute(
                PhysicalPresencePayloadV1.PartitionId,
                payload.ToStandardPayload(),
                references: references));
        var occupancyHeader = PartitionStateHeaderV1.CreateCanonical(
            physical.Occupancy.State,
            revision: 1,
            basisStep: 0,
            detailLevel: DetailLevelV1.D0Entity,
            payload => PhysicalOccupancyPayloadCanonicalDigestV2.Compute(payload, references));

        return new Qa04PhysicalD0FullReferenceWorldCanonicalMaterializationV1(
            tileFrames,
            tileFrameHeader,
            physical.Presence,
            presenceHeader,
            physical.Occupancy,
            occupancyHeader,
            references);
    }

    private static Qa04PhysicalPresenceGenesisBindingV1 CreateCanonicalPresenceBindingValidated(ulong physicalOrdinal)
    {
        if (physicalOrdinal >= CanonicalPhysicalCount) throw new ArgumentOutOfRangeException(nameof(physicalOrdinal));

        var descriptor = Qa04ReferenceLoadV1.Record(PhysicalReferenceClass, physicalOrdinal);
        var resident = Qa04ReferenceWorldMaterializerV1.CreateResidentRecord(physicalOrdinal);
        var (u, v) = Qa04ReferenceLoadV1.PositionWithinTile(descriptor.RecordId, step: 0);
        var row = descriptor.RegionalTileIndex / Qa04ReferenceLoadV1.RegionalTileColumns;
        var column = descriptor.RegionalTileIndex % Qa04ReferenceLoadV1.RegionalTileColumns;
        var width = Qa04TerrainCanonicalContentSourceV1.TileWidthMm;
        var x = checked((long)column * width + checked((long)Math.Floor(u * width)));
        var y = checked((long)row * width + checked((long)Math.Floor(v * width)));
        var z = Qa04TerrainCanonicalContentSourceV1.HeightMm(x, y);

        return new Qa04PhysicalPresenceGenesisBindingV1(
            new PartitionRecordRefV1(ResidentIdentityLifecyclePayloadV1.PartitionId, resident.RecordId),
            Qa04PhysicalD0PropertyAssetSupportCanonicalAuthorityV1.TileFrameRef(descriptor.RegionalTileIndex),
            new Vec3Int64V1(x, y, z),
            IdentityOrientation,
            ZeroVector,
            ZeroVector,
            ContainmentRef: null,
            Qa04PhysicalD0PropertyAssetSupportCanonicalAuthorityV1.PresenceMode);
    }

    private static void RequireShapeCount(IReadOnlyDictionary<string, ulong> counts, string shapeKind, ulong expected)
    {
        if (!counts.TryGetValue(shapeKind, out var actual) || actual != expected)
            throw new InvalidDataException($"qa04.physical.full-reference-world-shape-count:{shapeKind}");
    }

    private sealed class CanonicalReferenceResolver : IDomainRecordSchemaResolverV1
    {
        private readonly Dictionary<PartitionRecordRefV1, SchemaRefV1> _records = new();

        public void Add(PartitionRecordRefV1 reference, SchemaRefV1 schema)
        {
            if (reference.RecordId.IsZero)
                throw new InvalidDataException("qa04.physical.full-reference-world-reference-zero");
            if (!_records.TryAdd(reference, schema) && _records[reference] != schema)
                throw new InvalidDataException("qa04.physical.full-reference-world-reference-schema-conflict");
        }

        public bool Exists(PartitionRecordRefV1 reference) => _records.ContainsKey(reference);

        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
            => _records.TryGetValue(reference, out schema);
    }
}
