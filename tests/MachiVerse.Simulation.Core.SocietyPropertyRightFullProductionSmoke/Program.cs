using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void ExpectInvalid(Action action, string message)
{
    try
    {
        action();
    }
    catch (Exception exception) when (exception is InvalidDataException or ArgumentException or ArgumentOutOfRangeException)
    {
        return;
    }
    throw new InvalidOperationException(message);
}

static DomainRecordEnvelopeV1<SocietyPropertyRightPayloadV1> CopyRight(
    DomainRecordEnvelopeV1<SocietyPropertyRightPayloadV1> record,
    SocietyPropertyRightPayloadV1 payload)
    => new(record.RecordId, record.RecordSchema, record.Revision, record.CreatedStep, record.RetiredStep,
        record.DetailLevel, record.LineageRef, payload);

static Qa04PhysicalD0RecordMaterialV1 CopyPhysical(
    Qa04PhysicalD0RecordMaterialV1 material,
    PhysicalPresencePayloadV1 payload)
{
    var presence = new DomainRecordEnvelopeV1<PhysicalPresencePayloadV1>(
        material.Presence.RecordId,
        material.Presence.RecordSchema,
        material.Presence.Revision,
        material.Presence.CreatedStep,
        material.Presence.RetiredStep,
        material.Presence.DetailLevel,
        material.Presence.LineageRef,
        payload);
    return new Qa04PhysicalD0RecordMaterialV1(material.PhysicalOrdinal, presence, material.Occupancy, material.CollisionShape);
}

static DomainRecordEnvelopeV1<SpatialWorldFramePayloadV1> CopyFrame(
    DomainRecordEnvelopeV1<SpatialWorldFramePayloadV1> record,
    SpatialWorldFramePayloadV1 payload)
    => new(record.RecordId, record.RecordSchema, record.Revision, record.CreatedStep, record.RetiredStep,
        record.DetailLevel, record.LineageRef, payload);

Console.WriteLine("Validating approved Society PropertyRight authority (50,000 records) and Physical D0 support...");
Qa04SocietyPropertyRightCanonicalAuthorityV1.ValidateCanonicalContract();
var materialization = Qa04SocietyPropertyRightCanonicalAuthorityV1.MaterializeCanonical();
var support = materialization.Support;
var rights = materialization.RecordsByOrdinal;
var physical = support.PhysicalRecordsByOrdinal;

Require(materialization.MaterializedRecordCount == 50_000 && rights.Count == 50_000,
    "PropertyRight production materialization count drifted.");
Require(support.TileFrames.ItemCount == 4_096 && support.TileScopes.ItemCount == 4_096,
    "Physical D0 PropertyRight support must materialize exactly 4,096 TileFrames and TileScopes.");
Require(support.Presences.ItemCount == 50_000 && physical.Count == 50_000,
    "Physical D0 PropertyRight support must materialize exactly 50,000 actual Presence records.");
Require(physical.Select(static value => value.Presence.Payload.SubjectRef).Distinct().Count() == 50_000,
    "Physical D0 PropertyRight support must bind 50,000 distinct actual Residents.");
Require(rights.Select(static value => value.Payload.AssetRef).Distinct().Count() == 50_000 &&
        rights.Select(static value => value.Payload.HolderRef).Distinct().Count() == 50_000,
    "PropertyRight must bind exactly 50,000 distinct assets and holders.");
Require(rights.All(static value =>
        value.Payload.RightKind.Value == "perf.ownership" && value.Payload.SharePpm == 1_000_000 &&
        value.Payload.EffectiveFrom == 0 && value.Payload.EffectiveUntil is null && value.Payload.ClaimRef is null),
    "PropertyRight approved benchmark payload drifted.");

var terrainSdf = physical.Count(static value => value.CollisionShape.Payload is PhysicalTerrainSdfRefShapePayloadV2);
Require(terrainSdf == 500,
    "First 50,000 Physical records must include exactly 500 canonical terrain-SDF shapes.");
Qa04PhysicalD0PropertyAssetSupportCanonicalAuthorityV1.ValidatePhysicalPopulation(physical);
Qa04SocietyPropertyRightCanonicalAuthorityV1.ValidateInvariants(rights);
Qa04SocietyPropertyRightCanonicalAuthorityV1.ValidateSecondaryIndexes(materialization.PropertyRights);
var recovered = Qa04SocietyPropertyRightSnapshotRecoveryEvidenceV1.Verify(materialization);
Require(recovered == 50_000,
    "PropertyRight Snapshot/recovery must semantically recover all 50,000 records.");

var firstPhysical = physical[0];
var secondPhysical = physical[1];
var firstRight = rights[0];
var secondRight = rights[1];

ExpectInvalid(() => new StandardDomainPayloadCodecValidatorV1().Validate(
        PhysicalPresencePayloadV1.PartitionId,
        firstPhysical.Presence.Payload.ToStandardPayload(),
        new EmptyReferenceResolver()),
    "Physical Presence must fail closed without upstream Resident/Frame/Shape authority.");
ExpectInvalid(() => Qa04PhysicalD0PropertyAssetSupportCanonicalAuthorityV1.ValidateCanonicalPhysicalRecord(
        0,
        CopyPhysical(firstPhysical, firstPhysical.Presence.Payload with { SubjectRef = secondPhysical.Presence.Payload.SubjectRef }),
        materialization.References,
        support.TerrainBindingsByTile),
    "Wrong Physical Resident mapping must fail closed.");
ExpectInvalid(() => Qa04PhysicalD0PropertyAssetSupportCanonicalAuthorityV1.ValidateCanonicalPhysicalRecord(
        0,
        CopyPhysical(firstPhysical, firstPhysical.Presence.Payload with
        {
            SubjectRef = new PartitionRecordRefV1(PhysicalPresencePayloadV1.PartitionId, firstPhysical.Presence.Payload.SubjectRef.RecordId),
        }),
        materialization.References,
        support.TerrainBindingsByTile),
    "Wrong Physical subject partition must fail closed.");
var firstDescriptor = Qa04ReferenceLoadV1.Record(new StableToken("physical.d0-presence"), 0);
var wrongFrameTile = checked((ushort)((firstDescriptor.RegionalTileIndex + 1) % Qa04ReferenceLoadV1.RegionalTileCount));
ExpectInvalid(() => Qa04PhysicalD0PropertyAssetSupportCanonicalAuthorityV1.ValidateCanonicalPhysicalRecord(
        0,
        CopyPhysical(firstPhysical, firstPhysical.Presence.Payload with
        {
            FrameRef = Qa04PhysicalD0PropertyAssetSupportCanonicalAuthorityV1.TileFrameRef(wrongFrameTile),
        }),
        materialization.References,
        support.TerrainBindingsByTile),
    "Wrong Physical TileFrame mapping must fail closed.");
var frame0 = support.TileFrameRecordsByOrdinal[0];
ExpectInvalid(() => Qa04PhysicalD0PropertyAssetSupportCanonicalAuthorityV1.ValidateCanonicalTileFrameRecord(
        0,
        CopyFrame(frame0, frame0.Payload with { ValidScope = Qa04SpatialTileScopeAuthorityV1.ScopeRef(1) }),
        materialization.References),
    "TileFrame scope mismatch must fail closed.");
ExpectInvalid(() => Qa04PhysicalD0PropertyAssetSupportCanonicalAuthorityV1.ValidateCanonicalPhysicalRecord(
        0,
        CopyPhysical(firstPhysical, firstPhysical.Presence.Payload with
        {
            Position = firstPhysical.Presence.Payload.Position with { X = firstPhysical.Presence.Payload.Position.X + 1 },
        }),
        materialization.References,
        support.TerrainBindingsByTile),
    "Physical position drift must fail closed.");
ExpectInvalid(() => Qa04PhysicalD0PropertyAssetSupportCanonicalAuthorityV1.ValidateCanonicalPhysicalRecord(
        0,
        CopyPhysical(firstPhysical, firstPhysical.Presence.Payload with { Orientation = new QuaternionQ30V1(1, 0, 0, 1 << 30) }),
        materialization.References,
        support.TerrainBindingsByTile),
    "Physical orientation drift must fail closed.");
ExpectInvalid(() => Qa04PhysicalD0PropertyAssetSupportCanonicalAuthorityV1.ValidateCanonicalPhysicalRecord(
        0,
        CopyPhysical(firstPhysical, firstPhysical.Presence.Payload with { LinearVelocity = new Vec3Int64V1(1, 0, 0) }),
        materialization.References,
        support.TerrainBindingsByTile),
    "Physical linear-motion drift must fail closed.");
ExpectInvalid(() => Qa04PhysicalD0PropertyAssetSupportCanonicalAuthorityV1.ValidateCanonicalPhysicalRecord(
        0,
        CopyPhysical(firstPhysical, firstPhysical.Presence.Payload with { AngularRateUradPerSecond = new Vec3Int64V1(0, 1, 0) }),
        materialization.References,
        support.TerrainBindingsByTile),
    "Physical angular-motion drift must fail closed.");
ExpectInvalid(() => Qa04PhysicalD0PropertyAssetSupportCanonicalAuthorityV1.ValidateCanonicalPhysicalRecord(
        0,
        CopyPhysical(firstPhysical, firstPhysical.Presence.Payload with { ContainmentRef = secondPhysical.Presence.Payload.SubjectRef }),
        materialization.References,
        support.TerrainBindingsByTile),
    "Physical containment injection must fail closed.");
ExpectInvalid(() => Qa04PhysicalD0PropertyAssetSupportCanonicalAuthorityV1.ValidateCanonicalPhysicalRecord(
        0,
        CopyPhysical(firstPhysical, firstPhysical.Presence.Payload with { PresenceMode = new StableToken("active") }),
        materialization.References,
        support.TerrainBindingsByTile),
    "Physical presence-mode drift must fail closed.");

var terrain0 = Qa04PhysicalD0PropertyAssetSupportCanonicalAuthorityV1.CreateCanonicalTerrainBinding(0);
ExpectInvalid(() => Qa04PhysicalD0PropertyAssetSupportCanonicalAuthorityV1.ValidateCanonicalTerrainBinding(
        0,
        terrain0 with
        {
            TerrainRootRef = new PartitionRecordRefV1(
                SpatialTerrainGeometryRecordSchemaV2.PartitionId,
                Qa04TerrainRootMaterializerV1.RootId(1)),
        }),
    "Wrong/missing Terrain root binding must fail closed.");
ExpectInvalid(() => Qa04PhysicalD0PropertyAssetSupportCanonicalAuthorityV1.ValidateCanonicalTerrainBinding(
        0,
        terrain0 with { OccupancyAabbMin = terrain0.OccupancyAabbMin with { X = terrain0.OccupancyAabbMin.X + 1 } }),
    "Terrain D3-anchor AABB drift must fail closed.");

var duplicatePresence = physical.ToArray();
var duplicatePresenceEnvelope = new DomainRecordEnvelopeV1<PhysicalPresencePayloadV1>(
    duplicatePresence[0].Presence.RecordId,
    duplicatePresence[1].Presence.RecordSchema,
    duplicatePresence[1].Presence.Revision,
    duplicatePresence[1].Presence.CreatedStep,
    duplicatePresence[1].Presence.RetiredStep,
    duplicatePresence[1].Presence.DetailLevel,
    duplicatePresence[1].Presence.LineageRef,
    duplicatePresence[1].Presence.Payload);
duplicatePresence[1] = new Qa04PhysicalD0RecordMaterialV1(
    duplicatePresence[1].PhysicalOrdinal,
    duplicatePresenceEnvelope,
    duplicatePresence[1].Occupancy,
    duplicatePresence[1].CollisionShape);
ExpectInvalid(() => Qa04PhysicalD0PropertyAssetSupportCanonicalAuthorityV1.ValidatePhysicalPopulation(duplicatePresence),
    "Duplicate Physical Presence RecordId must fail closed.");
ExpectInvalid(() => Qa04PhysicalD0PropertyAssetSupportCanonicalAuthorityV1.ValidatePhysicalPopulation(physical.Take(physical.Count - 1)),
    "Physical Presence population drift must fail closed.");

ExpectInvalid(() => new StandardDomainPayloadCodecValidatorV1().Validate(
        SocietyPropertyRightPayloadV1.PartitionId,
        firstRight.Payload.ToStandardPayload(),
        new EmptyReferenceResolver()),
    "PropertyRight must fail closed without actual Physical/Resident authority.");
ExpectInvalid(() => Qa04SocietyPropertyRightCanonicalAuthorityV1.ValidateCanonicalRecord(
        0,
        CopyRight(firstRight, firstRight.Payload with { AssetRef = secondRight.Payload.AssetRef }),
        support,
        materialization.References),
    "Wrong PropertyRight asset mapping must fail closed.");
ExpectInvalid(() => Qa04SocietyPropertyRightCanonicalAuthorityV1.ValidateCanonicalRecord(
        0,
        CopyRight(firstRight, firstRight.Payload with
        {
            AssetRef = new PartitionRecordRefV1(SocietyPropertyRightPayloadV1.PartitionId, firstRight.Payload.AssetRef.RecordId),
        }),
        support,
        materialization.References),
    "Wrong PropertyRight asset partition must fail closed.");
ExpectInvalid(() => Qa04SocietyPropertyRightCanonicalAuthorityV1.ValidateCanonicalRecord(
        0,
        CopyRight(firstRight, firstRight.Payload with { HolderRef = secondRight.Payload.HolderRef }),
        support,
        materialization.References),
    "Wrong PropertyRight holder mapping must fail closed.");
ExpectInvalid(() => Qa04SocietyPropertyRightCanonicalAuthorityV1.ValidateCanonicalRecord(
        0,
        CopyRight(firstRight, firstRight.Payload with
        {
            HolderRef = new PartitionRecordRefV1(SocietyPropertyRightPayloadV1.PartitionId, firstRight.Payload.HolderRef.RecordId),
        }),
        support,
        materialization.References),
    "Wrong PropertyRight holder partition must fail closed.");
ExpectInvalid(() => Qa04SocietyPropertyRightCanonicalAuthorityV1.ValidateCanonicalRecord(
        0,
        CopyRight(firstRight, firstRight.Payload with { RightKind = new StableToken("active") }),
        support,
        materialization.References),
    "PropertyRight right-kind drift must fail closed.");
ExpectInvalid(() => Qa04SocietyPropertyRightCanonicalAuthorityV1.ValidateCanonicalRecord(
        0,
        CopyRight(firstRight, firstRight.Payload with { SharePpm = 999_999 }),
        support,
        materialization.References),
    "PropertyRight share drift must fail closed.");
ExpectInvalid(() => Qa04SocietyPropertyRightCanonicalAuthorityV1.ValidateCanonicalRecord(
        0,
        CopyRight(firstRight, firstRight.Payload with { EffectiveFrom = 1 }),
        support,
        materialization.References),
    "PropertyRight effective-from drift must fail closed.");
ExpectInvalid(() => Qa04SocietyPropertyRightCanonicalAuthorityV1.ValidateCanonicalRecord(
        0,
        CopyRight(firstRight, firstRight.Payload with { EffectiveUntil = 1 }),
        support,
        materialization.References),
    "PropertyRight effective-until injection must fail closed.");
ExpectInvalid(() => Qa04SocietyPropertyRightCanonicalAuthorityV1.ValidateCanonicalRecord(
        0,
        CopyRight(firstRight, firstRight.Payload with { ClaimRef = firstRight.Payload.HolderRef }),
        support,
        materialization.References),
    "PropertyRight claim injection must fail closed.");

var duplicateRelation = rights.ToArray();
duplicateRelation[1] = CopyRight(duplicateRelation[1], duplicateRelation[0].Payload);
ExpectInvalid(() => Qa04SocietyPropertyRightCanonicalAuthorityV1.ValidateInvariants(duplicateRelation),
    "Duplicate PropertyRight asset/holder/right relation must fail closed.");
var duplicateRightId = rights.ToArray();
duplicateRightId[1] = new DomainRecordEnvelopeV1<SocietyPropertyRightPayloadV1>(
    duplicateRightId[0].RecordId,
    duplicateRightId[1].RecordSchema,
    duplicateRightId[1].Revision,
    duplicateRightId[1].CreatedStep,
    duplicateRightId[1].RetiredStep,
    duplicateRightId[1].DetailLevel,
    duplicateRightId[1].LineageRef,
    duplicateRightId[1].Payload);
ExpectInvalid(() => Qa04SocietyPropertyRightCanonicalAuthorityV1.ValidateInvariants(duplicateRightId),
    "Duplicate PropertyRight RecordId must fail closed.");
var shortRights = rights.Take(rights.Count - 1).ToArray();
ExpectInvalid(() => Qa04SocietyPropertyRightCanonicalAuthorityV1.ValidateInvariants(shortRights),
    "PropertyRight population cardinality drift must fail closed.");
var shortPartition = new DomainPartitionStateV1<SocietyPropertyRightPayloadV1>(
    StandardDomainPartitionRegistry.Get(SocietyPropertyRightPayloadV1.PartitionId),
    shortRights);
ExpectInvalid(() => Qa04SocietyPropertyRightCanonicalAuthorityV1.ValidateSecondaryIndexes(shortPartition),
    "PropertyRight secondary-index cardinality drift must fail closed.");

Console.WriteLine($"society-property-right-full-production-pass records={materialization.MaterializedRecordCount} recovered={recovered} assets={rights.Select(static value => value.Payload.AssetRef).Distinct().Count()} holders={rights.Select(static value => value.Payload.HolderRef).Distinct().Count()} tileFrames={support.TileFrames.ItemCount} physical={support.Presences.ItemCount} terrainSdf={terrainSdf}");

sealed class EmptyReferenceResolver : IDomainRecordSchemaResolverV1
{
    public bool Exists(PartitionRecordRefV1 reference) => false;

    public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
    {
        schema = default;
        return false;
    }
}
