using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04TerrainGeometryPayloadDigestV2Initializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        VerifyBrickDigestAndPartitionHeader();
        VerifyRootCanonicalOrdering();
        VerifyReferenceValidation();
    }

    private static void VerifyBrickDigestAndPartitionHeader()
    {
        var brickId = Id("00000000000000000000000000024700");
        var sdf = Enumerable.Range(0, TerrainBrickV1.SdfSampleCount).Select(static i => i - 100).ToArray();
        var material = Enumerable.Range(0, TerrainBrickV1.SurfaceMaterialCount).Select(static i => (ushort)(i % 31)).ToArray();
        var brickRecord = SpatialTerrainGeometryRecordMaterialV2.FromTerrainBrick(
            new TerrainBrickV1(
                brickId,
                2,
                new SpatialCellKeyV1(3, -7, 8, -9),
                1_000,
                sdf,
                material,
                5),
            createdStep: 4,
            detailLevel: DetailLevelV1.D1LocalAggregate);
        var digestA = SpatialTerrainGeometryPayloadCanonicalDigestV2.Compute(brickRecord.Payload);
        var digestB = SpatialTerrainGeometryPayloadCanonicalDigestV2.Compute(brickRecord.Payload);
        Require(digestA.Length == 32 && CryptographicOperations.FixedTimeEquals(digestA, digestB),
            "terrain v2 payload digest must be stable for identical semantic material.");

        var changedSdf = sdf.ToArray();
        changedSdf[^1]++;
        var changed = new SpatialTerrainBrickPayloadV2(
            2,
            new SpatialCellKeyV1(3, -7, 8, -9),
            1_000,
            changedSdf,
            material);
        var changedDigest = SpatialTerrainGeometryPayloadCanonicalDigestV2.Compute(changed);
        Require(!CryptographicOperations.FixedTimeEquals(digestA, changedDigest),
            "terrain v2 payload digest must cover every SDF sample.");

        var rootId = Id("00000000000000000000000000024701");
        var rootRecord = new SpatialTerrainGeometryRecordMaterialV2(
            rootId,
            2,
            4,
            null,
            DetailLevelV1.D1LocalAggregate,
            null,
            new SpatialTerrainRootPayloadV2(
                new PartitionRecordRefV1("spatial.scope_registry", Id("00000000000000000000000000024710")),
                new PartitionRecordRefV1(SpatialTerrainGeometryRecordSchemaV2.PartitionId, brickId),
                9,
                [new StableToken("rock"), new StableToken("soil")],
                Array.Empty<PartitionRecordRefV1>(),
                null));
        var state = new SpatialTerrainGeometryPartitionStateV2([rootRecord, brickRecord]);
        var header = PartitionStateHeaderV1.CreateCanonical(
            state.State,
            revision: 7,
            basisStep: 4,
            detailLevel: DetailLevelV1.D1LocalAggregate,
            SpatialTerrainGeometryPayloadCanonicalDigestV2.Compute);
        var repeated = PartitionStateHeaderV1.CreateCanonical(
            state.State,
            revision: 7,
            basisStep: 4,
            detailLevel: DetailLevelV1.D1LocalAggregate,
            SpatialTerrainGeometryPayloadCanonicalDigestV2.Compute);
        var standard = StandardDomainPartitionRegistry.Get(SpatialTerrainGeometryRecordSchemaV2.PartitionId);
        Require(header.Schema == standard.PartitionSchema && header.ItemCount == 2,
            "terrain v2 record schema must not change the partition-header schema/count contract.");
        Require(CryptographicOperations.FixedTimeEquals(header.CanonicalDigest, repeated.CanonicalDigest),
            "terrain v2 partition canonical digest must be reproducible.");
    }

    private static void VerifyRootCanonicalOrdering()
    {
        var rootBrick = new PartitionRecordRefV1(
            SpatialTerrainGeometryRecordSchemaV2.PartitionId,
            Id("00000000000000000000000000024800"));
        var scope = new PartitionRecordRefV1(
            "spatial.scope_registry",
            Id("00000000000000000000000000024801"));

        var unsortedTokens = new SpatialTerrainRootPayloadV2(
            scope,
            rootBrick,
            1,
            [new StableToken("soil"), new StableToken("rock")],
            Array.Empty<PartitionRecordRefV1>(),
            null);
        ExpectReject(
            () => SpatialTerrainGeometryPayloadCanonicalDigestV2.Compute(unsortedTokens),
            "domain.payload.digest-terrain-v2-token-order:surface_classes");

        var high = new PartitionRecordRefV1("spatial.void_geometry", Id("00000000000000000000000000024803"));
        var low = new PartitionRecordRefV1("spatial.containment_topology", Id("00000000000000000000000000024802"));
        var unsortedRefs = new SpatialTerrainRootPayloadV2(
            scope,
            rootBrick,
            1,
            Array.Empty<StableToken>(),
            [high, low],
            null);
        ExpectReject(
            () => SpatialTerrainGeometryPayloadCanonicalDigestV2.Compute(unsortedRefs),
            "domain.payload.digest-terrain-v2-ref-order:connectivity_refs");
    }

    private static void VerifyReferenceValidation()
    {
        var root = new SpatialTerrainRootPayloadV2(
            new PartitionRecordRefV1("spatial.scope_registry", Id("00000000000000000000000000024810")),
            new PartitionRecordRefV1(
                SpatialTerrainGeometryRecordSchemaV2.PartitionId,
                Id("00000000000000000000000000024811")),
            1,
            Array.Empty<StableToken>(),
            Array.Empty<PartitionRecordRefV1>(),
            null);
        ExpectReject(
            () => SpatialTerrainGeometryPayloadCanonicalDigestV2.Compute(root, new RejectAllReferences()),
            "domain.payload.digest-reference-missing:spatial.terrain_geometry:scope_ref");
    }

    private sealed class RejectAllReferences : IDomainRecordReferenceResolverV1
    {
        public bool Exists(PartitionRecordRefV1 reference) => false;
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
