using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class SpatialTerrainGeometryStreamingSemanticVerifierSmoke
{
    [ModuleInitializer]
    internal static void Run()
        => RunAsync().GetAwaiter().GetResult();

    private static async Task RunAsync()
    {
        var brickId = OpaqueId128.Parse("00000000000000000000000000000120");
        var rootId = OpaqueId128.Parse("00000000000000000000000000000110");
        var scopeId = OpaqueId128.Parse("00000000000000000000000000000130");
        var scopeRef = new PartitionRecordRefV1("spatial.scope_registry", scopeId);

        var brick = SpatialTerrainGeometryRecordMaterialV2.FromTerrainBrick(
            new TerrainBrickV1(
                brickId,
                3,
                new SpatialCellKeyV1(3, 0, 0, 0),
                64_000,
                Enumerable.Repeat(0, TerrainBrickV1.SdfSampleCount),
                Enumerable.Repeat((ushort)1, TerrainBrickV1.SurfaceMaterialCount),
                1),
            createdStep: 0,
            detailLevel: DetailLevelV1.D3BoundarySummary);
        var root = new SpatialTerrainGeometryRecordMaterialV2(
            rootId,
            1,
            0,
            null,
            DetailLevelV1.D3BoundarySummary,
            null,
            new SpatialTerrainRootPayloadV2(
                scopeRef,
                new PartitionRecordRefV1(SpatialTerrainGeometryRecordSchemaV2.PartitionId, brickId),
                1,
                [new StableToken("terrain.rock")],
                Array.Empty<PartitionRecordRefV1>(),
                null));

        var partition = new SpatialTerrainGeometryPartitionStateV2([root, brick]);
        var authority = SpatialTerrainGeometrySnapshotAuthorityV2.CreateCanonical(
            partition,
            1,
            0,
            DetailLevelV1.D3BoundarySummary);
        var provider = new SpatialTerrainGeometrySnapshotSectionProviderV2();
        var section = provider.Create(authority);
        var recovered = new SpatialTerrainGeometryStreamingRecoveredReferenceSourceV2(section.Fragments);
        var recoveredAsync = await SpatialTerrainGeometryStreamingRecoveredReferenceSourceV2.CreateAsync(
            Async(section.Fragments));
        var references = BuildResolver(rootId, brickId, scopeId, includeScope: true);

        Require(recoveredAsync.RecordIdsCanonical.SequenceEqual(recovered.RecordIdsCanonical) &&
                recoveredAsync.RootClosures.SequenceEqual(recovered.RootClosures),
            "Async Terrain phase-1 recovery index must match the synchronous bounded-memory source.");

        var legacyVerifier = provider.CreateSemanticVerifier(authority.Header);
        var legacy = legacyVerifier.VerifyWithContext!(
            section.Fragments,
            new SnapshotSectionSemanticVerificationContextV1(references));
        var streaming = SpatialTerrainGeometryStreamingSemanticVerifierV2.Verify(
            authority.Header,
            section.Fragments,
            recovered,
            references);
        var streamingAsync = await SpatialTerrainGeometryStreamingSemanticVerifierV2.VerifyAsync(
            authority.Header,
            Async(section.Fragments),
            recoveredAsync,
            references);

        Require(streaming.LogicalItemCount == legacy.LogicalItemCount &&
                streaming.LogicalContentDigest.AsSpan().SequenceEqual(legacy.LogicalContentDigest),
            "Streaming Terrain semantic verifier must reproduce the existing recovered semantic rehash exactly.");
        Require(streamingAsync.LogicalItemCount == streaming.LogicalItemCount &&
                streamingAsync.LogicalContentDigest.AsSpan().SequenceEqual(streaming.LogicalContentDigest),
            "Async Terrain semantic verifier must be byte-for-byte identical to the synchronous bounded-memory verifier.");
        Require(streamingAsync.LogicalContentDigest.AsSpan().SequenceEqual(authority.Header.CanonicalDigest),
            "Async Terrain semantic verifier must rehash to the frozen authority digest.");

        var missingScope = BuildResolver(rootId, brickId, scopeId, includeScope: false);
        var rejected = false;
        try
        {
            _ = await SpatialTerrainGeometryStreamingSemanticVerifierV2.VerifyAsync(
                authority.Header,
                Async(section.Fragments),
                recoveredAsync,
                missingScope);
        }
        catch (InvalidDataException ex) when (
            ex.Message == "domain.payload.digest-reference-missing:spatial.terrain_geometry:scope_ref")
        {
            rejected = true;
        }
        Require(rejected,
            "Async Terrain semantic verifier must fail closed when the recovered TileScope reference is missing.");
    }

    private static DomainSnapshotCompactReferenceResolverV1 BuildResolver(
        OpaqueId128 rootId,
        OpaqueId128 brickId,
        OpaqueId128 scopeId,
        bool includeScope)
    {
        var terrainId = SpatialTerrainGeometryRecordSchemaV2.PartitionId;
        var sources = StandardDomainPartitionRegistry.Entries
            .Select(identity =>
            {
                if (string.Equals(identity.PartitionId.Value, terrainId, StringComparison.Ordinal))
                {
                    return (IDomainPartitionSnapshotReferenceSourceV1)new Source(
                        identity.PartitionId,
                        SpatialTerrainGeometryRecordSchemaV2.RecordSchema,
                        new[] { rootId, brickId }.OrderBy(static id => id).ToArray());
                }
                if (string.Equals(identity.PartitionId.Value, "spatial.scope_registry", StringComparison.Ordinal))
                {
                    return new Source(
                        identity.PartitionId,
                        identity.RecordSchema,
                        includeScope ? new[] { scopeId } : Array.Empty<OpaqueId128>());
                }
                return new Source(identity.PartitionId, identity.RecordSchema, Array.Empty<OpaqueId128>());
            })
            .ToArray();
        return new DomainSnapshotCompactReferenceResolverV1(sources);
    }

    private static async IAsyncEnumerable<SnapshotSectionFragmentMaterialV1> Async(
        IEnumerable<SnapshotSectionFragmentMaterialV1> fragments)
    {
        foreach (var fragment in fragments)
        {
            await Task.Yield();
            yield return fragment;
        }
    }

    private sealed class Source : IDomainPartitionSnapshotReferenceSourceV1
    {
        public Source(StableToken partitionId, SchemaRefV1 recordSchema, IReadOnlyList<OpaqueId128> ids)
        {
            PartitionId = partitionId;
            RecordSchema = recordSchema;
            RecordIdsCanonical = ids;
        }

        public StableToken PartitionId { get; }
        public SchemaRefV1 RecordSchema { get; }
        public ulong ActualItemCount => checked((ulong)RecordIdsCanonical.Count);
        public IReadOnlyList<OpaqueId128> RecordIdsCanonical { get; }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
