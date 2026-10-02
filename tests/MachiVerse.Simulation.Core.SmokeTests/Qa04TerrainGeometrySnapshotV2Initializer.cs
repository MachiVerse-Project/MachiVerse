using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04TerrainGeometrySnapshotV2Initializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        VerifyAuthorityAndFragmentRoundTrip();
        VerifyFragmentRejectsUnknownField();
        VerifyFragmentRejectsFutureRecord();
    }

    private static void VerifyAuthorityAndFragmentRoundTrip()
    {
        var brickId = Id("00000000000000000000000000025000");
        var rootId = Id("00000000000000000000000000025001");
        var brick = SpatialTerrainGeometryRecordMaterialV2.FromTerrainBrick(
            new TerrainBrickV1(
                brickId,
                1,
                new SpatialCellKeyV1(1, -2, 3, 4),
                1_000,
                Enumerable.Range(0, TerrainBrickV1.SdfSampleCount).Select(static x => x - 250),
                Enumerable.Range(0, TerrainBrickV1.SurfaceMaterialCount).Select(static x => (ushort)(x % 17)),
                3),
            createdStep: 5,
            detailLevel: DetailLevelV1.D1LocalAggregate);
        var root = new SpatialTerrainGeometryRecordMaterialV2(
            rootId,
            revision: 4,
            createdStep: 5,
            retiredStep: null,
            detailLevel: DetailLevelV1.D1LocalAggregate,
            lineageRef: null,
            new SpatialTerrainRootPayloadV2(
                new PartitionRecordRefV1("spatial.scope_registry", Id("00000000000000000000000000025010")),
                new PartitionRecordRefV1(SpatialTerrainGeometryRecordSchemaV2.PartitionId, brickId),
                geometryRevision: 8,
                [new StableToken("rock")],
                Array.Empty<PartitionRecordRefV1>(),
                archiveAnchor: null));

        var partition = new SpatialTerrainGeometryPartitionStateV2([root, brick]);
        var authority = SpatialTerrainGeometrySnapshotAuthorityV2.CreateCanonical(
            partition,
            revision: 9,
            basisStep: 5,
            detailLevel: DetailLevelV1.D1LocalAggregate);
        authority.VerifyBoundAuthority();

        Require(authority.RecordSchema == SpatialTerrainGeometryRecordSchemaV2.RecordSchema,
            "terrain v2 snapshot authority must expose record schema 2.0.");
        Require(authority.Header.Schema == StandardDomainPartitionRegistry.Get(SpatialTerrainGeometryRecordSchemaV2.PartitionId).PartitionSchema,
            "terrain v2 snapshot authority must retain the existing partition schema in the header.");
        Require(authority.ActualItemCount == 2,
            "terrain v2 snapshot authority must bind the actual root + brick item count.");

        var encoded = SpatialTerrainGeometrySnapshotFragmentWireV2.Encode(
            authority.Header,
            authority.Partition.RecordSet.RecordsCanonical);
        var decoded = SpatialTerrainGeometrySnapshotFragmentWireV2.Decode(encoded);
        Require(decoded.Records.Count == 2,
            "terrain v2 fragment round-trip must preserve both records.");
        Require(SameHeader(authority.Header, decoded.Header),
            "terrain v2 fragment round-trip must preserve the frozen partition header.");
        Require(decoded.Records[0].RecordId.CompareTo(decoded.Records[1].RecordId) < 0,
            "terrain v2 fragment recovery must preserve canonical record-id order.");
        Require(SpatialTerrainGeometrySnapshotFragmentWireV2.Encode(decoded.Header, decoded.Records).SequenceEqual(encoded),
            "terrain v2 fragment wire must be canonical after decode/re-encode.");

        var restoredPartition = new SpatialTerrainGeometryPartitionStateV2(decoded.Records);
        var restoredAuthority = new SpatialTerrainGeometrySnapshotAuthorityV2(restoredPartition, decoded.Header);
        restoredAuthority.VerifyBoundAuthority();
        Require(CryptographicOperations.FixedTimeEquals(
                restoredAuthority.Header.CanonicalDigest,
                authority.Header.CanonicalDigest),
            "terrain v2 recovered authority must rehash to the frozen partition digest.");
        Require(StandardDomainPartitionRegistry.Get(SpatialTerrainGeometryRecordSchemaV2.PartitionId).RecordSchema.Version == new SchemaVersionV1(1, 0),
            "terrain v2 snapshot foundation must not flip the production standard registry.");
    }

    private static void VerifyFragmentRejectsUnknownField()
    {
        var (authority, records) = MinimalAuthority();
        var encoded = SpatialTerrainGeometrySnapshotFragmentWireV2.Encode(authority.Header, records);
        ExpectReject(
            () => SpatialTerrainGeometrySnapshotFragmentWireV2.Decode(encoded.Concat(new byte[] { 0x1a, 0x00 }).ToArray()),
            "persistence.snapshot.terrain-v2-fragment:field-unknown");
    }

    private static void VerifyFragmentRejectsFutureRecord()
    {
        var (authority, _) = MinimalAuthority();
        var future = SpatialTerrainGeometryRecordMaterialV2.FromTerrainBrick(
            new TerrainBrickV1(
                Id("00000000000000000000000000025200"),
                0,
                new SpatialCellKeyV1(0, 0, 0, 0),
                250,
                Enumerable.Repeat(0, TerrainBrickV1.SdfSampleCount),
                Enumerable.Repeat((ushort)0, TerrainBrickV1.SurfaceMaterialCount),
                1),
            createdStep: authority.Header.BasisStep + 1,
            detailLevel: DetailLevelV1.D0Entity);
        ExpectReject(
            () => SpatialTerrainGeometrySnapshotFragmentWireV2.Encode(authority.Header, [future]),
            "persistence.snapshot.terrain-v2-fragment:record-step-after-basis");
    }

    private static (SpatialTerrainGeometrySnapshotAuthorityV2 Authority, IReadOnlyList<SpatialTerrainGeometryRecordMaterialV2> Records) MinimalAuthority()
    {
        var brick = SpatialTerrainGeometryRecordMaterialV2.FromTerrainBrick(
            new TerrainBrickV1(
                Id("00000000000000000000000000025100"),
                0,
                new SpatialCellKeyV1(0, 0, 0, 0),
                250,
                Enumerable.Repeat(0, TerrainBrickV1.SdfSampleCount),
                Enumerable.Repeat((ushort)0, TerrainBrickV1.SurfaceMaterialCount),
                1),
            createdStep: 1,
            detailLevel: DetailLevelV1.D0Entity);
        var partition = new SpatialTerrainGeometryPartitionStateV2([brick]);
        var authority = SpatialTerrainGeometrySnapshotAuthorityV2.CreateCanonical(
            partition,
            revision: 1,
            basisStep: 1,
            detailLevel: DetailLevelV1.D0Entity);
        return (authority, authority.Partition.RecordSet.RecordsCanonical);
    }

    private static bool SameHeader(PartitionStateHeaderV1 left, PartitionStateHeaderV1 right)
        => left.PartitionId == right.PartitionId &&
           left.OwnerDomain == right.OwnerDomain &&
           left.Schema == right.Schema &&
           left.Revision == right.Revision &&
           left.BasisStep == right.BasisStep &&
           left.DetailLevel == right.DetailLevel &&
           left.ItemCount == right.ItemCount &&
           CryptographicOperations.FixedTimeEquals(left.CanonicalDigest, right.CanonicalDigest);

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
