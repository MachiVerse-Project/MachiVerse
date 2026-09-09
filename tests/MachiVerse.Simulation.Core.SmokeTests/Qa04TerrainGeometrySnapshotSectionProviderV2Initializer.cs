using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04TerrainGeometrySnapshotSectionProviderV2Initializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        VerifySectionCreateAndSemanticRecovery();
        VerifyWrongAuthorityRejected();
    }

    private static void VerifySectionCreateAndSemanticRecovery()
    {
        var brickId = Id("00000000000000000000000000025300");
        var rootId = Id("00000000000000000000000000025301");
        var brick = SpatialTerrainGeometryRecordMaterialV2.FromTerrainBrick(
            new TerrainBrickV1(
                brickId,
                0,
                new SpatialCellKeyV1(0, 10, -11, 12),
                250,
                Enumerable.Repeat(-25, TerrainBrickV1.SdfSampleCount),
                Enumerable.Repeat((ushort)3, TerrainBrickV1.SurfaceMaterialCount),
                2),
            createdStep: 2,
            detailLevel: DetailLevelV1.D0Entity);
        var root = new SpatialTerrainGeometryRecordMaterialV2(
            rootId,
            revision: 3,
            createdStep: 2,
            retiredStep: null,
            detailLevel: DetailLevelV1.D0Entity,
            lineageRef: null,
            new SpatialTerrainRootPayloadV2(
                new PartitionRecordRefV1("spatial.scope_registry", Id("00000000000000000000000000025310")),
                new PartitionRecordRefV1(SpatialTerrainGeometryRecordSchemaV2.PartitionId, brickId),
                geometryRevision: 6,
                [new StableToken("rock")],
                Array.Empty<PartitionRecordRefV1>(),
                archiveAnchor: null));
        var partition = new SpatialTerrainGeometryPartitionStateV2([root, brick]);
        var authority = SpatialTerrainGeometrySnapshotAuthorityV2.CreateCanonical(
            partition,
            revision: 7,
            basisStep: 2,
            detailLevel: DetailLevelV1.D0Entity);
        var provider = new SpatialTerrainGeometrySnapshotSectionProviderV2();

        var section = provider.Create(authority);
        Require(section.SectionId == SpatialTerrainGeometryRecordSchemaV2.PartitionId,
            "terrain v2 provider must emit the standard terrain section id.");
        Require(section.SectionSchema == StandardDomainPartitionRegistry.Get(section.SectionId).PartitionSchema,
            "terrain v2 provider must retain the standard partition section schema.");
        Require(section.LogicalItemCount == 2 &&
                CryptographicOperations.FixedTimeEquals(section.LogicalContentDigest, authority.Header.CanonicalDigest),
            "terrain v2 provider must bind section count/digest to actual frozen authority.");
        Require(section.Fragments.Count == 1 && section.Fragments[0].ItemCount == 2,
            "small terrain v2 authority should materialize as one canonical fragment.");
        Require(section.Fragments[0].FirstRecordId is not null && section.Fragments[0].LastRecordId is not null,
            "non-empty terrain v2 fragment must expose its canonical record range.");

        var verifier = provider.CreateSemanticVerifier(authority.Header);
        var verified = verifier.Verify(section.Fragments);
        Require(verified.LogicalItemCount == section.LogicalItemCount &&
                CryptographicOperations.FixedTimeEquals(verified.LogicalContentDigest, section.LogicalContentDigest),
            "terrain v2 semantic recovery must reconstruct and rehash the exact frozen authority.");

        var contextVerified = verifier.VerifyWithContext?.Invoke(
            section.Fragments,
            new SnapshotSectionSemanticVerificationContextV1(null))
            ?? throw new InvalidOperationException("terrain v2 provider must expose context-aware verification.");
        Require(contextVerified.LogicalItemCount == verified.LogicalItemCount &&
                CryptographicOperations.FixedTimeEquals(contextVerified.LogicalContentDigest, verified.LogicalContentDigest),
            "terrain v2 context verifier must preserve standalone recovery semantics when no resolver is supplied.");
        Require(StandardDomainPartitionRegistry.Get(SpatialTerrainGeometryRecordSchemaV2.PartitionId).RecordSchema.Version == new SchemaVersionV1(1, 0),
            "terrain v2 standalone provider must not activate the production registry implicitly.");
    }

    private static void VerifyWrongAuthorityRejected()
    {
        var standard = StandardDomainPartitionRegistry.Get("spatial.void_geometry");
        var empty = new DomainPartitionStateV1<string>(
            standard,
            Array.Empty<DomainRecordEnvelopeV1<string>>());
        var header = PartitionStateHeaderV1.CreateCanonical(
            empty,
            revision: 1,
            basisStep: 0,
            detailLevel: DetailLevelV1.D0Entity,
            static payload => SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload)));
        var authority = new DomainPartitionSnapshotAuthorityV1<string>(
            empty,
            header,
            static payload => SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload)));
        ExpectReject(
            () => new SpatialTerrainGeometrySnapshotSectionProviderV2().Create(authority),
            "persistence.snapshot.terrain-v2-provider-authority-type");
    }

    private static OpaqueId128 Id(string value) => OpaqueId128.Parse(value);

    private static void ExpectReject(Action action, string code)
    {
        try
        {
            action();
            throw new InvalidOperationException($"Expected rejection containing '{code}'.");
        }
        catch (InvalidDataException ex) when (ex.Message.Contains(code, StringComparison.Ordinal))
        {
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
