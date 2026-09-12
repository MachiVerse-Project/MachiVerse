using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04TerrainV2RecoveredReferenceResolverInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var brickId = Id("00000000000000000000000000026100");
        var rootId = Id("00000000000000000000000000026101");
        var terrainSection = BuildTerrainV2Section(brickId, rootId);

        var sections = new List<CanonicalSnapshotSectionMaterialV1>(StandardDomainPartitionRegistry.StandardPartitionCount);
        foreach (var identity in StandardDomainPartitionRegistry.Entries)
        {
            sections.Add(identity.PartitionId.Value == SpatialTerrainGeometryRecordSchemaV2.PartitionId
                ? terrainSection
                : BuildEmptyV1Section(identity));
        }

        var resolver = DomainSnapshotReferenceResolverV1.FromRecoveredSections(sections);
        Require(resolver.RecordCount == 2,
            "recovered all-97 resolver must contain the two actual Terrain v2 records and no header-only records.");
        Require(resolver.TryGetRecordSchema(
                new PartitionRecordRefV1(SpatialTerrainGeometryRecordSchemaV2.PartitionId, brickId),
                out var brickSchema) &&
                brickSchema == SpatialTerrainGeometryRecordSchemaV2.RecordSchema,
            "recovery phase 1 must preserve the actual Terrain v2 record schema.");
        Require(resolver.TryGetRecordSchema(
                new PartitionRecordRefV1(SpatialTerrainGeometryRecordSchemaV2.PartitionId, rootId),
                out var rootSchema) &&
                rootSchema == SpatialTerrainGeometryRecordSchemaV2.RecordSchema,
            "recovery phase 1 must expose both Terrain v2 record arms through the all-97 resolver.");
    }

    private static CanonicalSnapshotSectionMaterialV1 BuildTerrainV2Section(
        OpaqueId128 brickId,
        OpaqueId128 rootId)
    {
        var brick = SpatialTerrainGeometryRecordMaterialV2.FromTerrainBrick(
            new TerrainBrickV1(
                brickId,
                0,
                new SpatialCellKeyV1(0, 1, 2, 3),
                250,
                Enumerable.Repeat(-10, TerrainBrickV1.SdfSampleCount),
                Enumerable.Repeat((ushort)4, TerrainBrickV1.SurfaceMaterialCount),
                2),
            createdStep: 1,
            detailLevel: DetailLevelV1.D0Entity);
        var root = new SpatialTerrainGeometryRecordMaterialV2(
            rootId,
            revision: 2,
            createdStep: 1,
            retiredStep: null,
            detailLevel: DetailLevelV1.D0Entity,
            lineageRef: null,
            new SpatialTerrainRootPayloadV2(
                new PartitionRecordRefV1("spatial.scope_registry", Id("00000000000000000000000000026110")),
                new PartitionRecordRefV1(SpatialTerrainGeometryRecordSchemaV2.PartitionId, brickId),
                geometryRevision: 3,
                [new StableToken("rock")],
                Array.Empty<PartitionRecordRefV1>(),
                archiveAnchor: null));
        var authority = SpatialTerrainGeometrySnapshotAuthorityV2.CreateCanonical(
            new SpatialTerrainGeometryPartitionStateV2([root, brick]),
            revision: 3,
            basisStep: 1,
            detailLevel: DetailLevelV1.D0Entity);
        return new SpatialTerrainGeometrySnapshotSectionProviderV2().Create(authority);
    }

    private static CanonicalSnapshotSectionMaterialV1 BuildEmptyV1Section(DomainPartitionIdentityV1 identity)
    {
        var state = new DomainPartitionStateV1<byte[]>(
            identity,
            Array.Empty<DomainRecordEnvelopeV1<byte[]>>());
        var header = PartitionStateHeaderV1.CreateCanonical(
            state,
            revision: 1,
            basisStep: 0,
            detailLevel: DetailLevelV1.D0Entity,
            static payload => SHA256.HashData(payload));
        var authority = new DomainPartitionSnapshotAuthorityV1<byte[]>(
            state,
            header,
            static payload => SHA256.HashData(payload));
        var fragmentPayload = DomainPartitionSnapshotWireCodecV1.EncodeFragment(
            authority,
            Array.Empty<DomainRecordEnvelopeV1<byte[]>>(),
            static _ => new Dictionary<string, object?>(StringComparer.Ordinal));
        return new CanonicalSnapshotSectionMaterialV1(
            identity.PartitionId.Value,
            identity.PartitionSchema,
            0,
            header.CanonicalDigest.ToArray(),
            Array.AsReadOnly(new[]
            {
                new SnapshotSectionFragmentMaterialV1(
                    identity.PartitionId.Value,
                    0,
                    1,
                    null,
                    null,
                    0,
                    fragmentPayload),
            }));
    }

    private static OpaqueId128 Id(string value) => OpaqueId128.Parse(value);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
