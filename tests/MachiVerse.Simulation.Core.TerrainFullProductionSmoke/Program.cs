using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Configuration;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static WorldStateV1 BuildFrozenState(
    Qa04ResidentIdentityMaterializationV1 resident,
    EffectiveCoreConfig config,
    DomainRegistryStateV1 registry,
    DetailDirectoryV1 detail)
{
    var scheduler = OperationSchedulerSubstateV1.Canonicalize(
        new OperationSchedulerStateV1(
            nextSchedulableStep: resident.WorldState.Header.Step,
            freezeStep: null,
            scheduled: Array.Empty<ScheduledOperationRefV1>()),
        resident.WorldState.Header.Step);
    var operations = DurableOperationSubstateV1.Canonicalize(Array.Empty<DurableOperationStateV1>());
    var detailAuthority = DetailDirectorySubstateV1.Canonicalize(detail);
    return new WorldStateV1(
        new WorldStateHeaderV1(
            resident.WorldState.Header.WorldId,
            resident.WorldState.Header.Step,
            SHA256.HashData(Qa04ReferenceLoadV1.WorldSeed.ToBytes()),
            config.Generation,
            masterGeneration: 1,
            rateGeneration: 1),
        new OrderedPartitionDirectoryV1(
            resident.WorldState.Partitions.CanonicalEntries.Select(entry => new PartitionStateRefV1(entry.Header))),
        scheduler,
        operations,
        detailAuthority,
        registry.ToWorldSubstateRef(),
        config.Digest);
}

static WorldStateV1 ReplaceHeaders(
    WorldStateV1 source,
    PartitionStateHeaderV1 scopeHeader,
    PartitionStateHeaderV1 terrainHeader)
{
    var partitions = new OrderedPartitionDirectoryV1(
        source.Partitions.CanonicalEntries.Select(entry =>
        {
            if (entry.Header.PartitionId == scopeHeader.PartitionId)
                return new PartitionStateRefV1(scopeHeader);
            if (entry.Header.PartitionId == terrainHeader.PartitionId)
                return new PartitionStateRefV1(terrainHeader);
            return entry;
        }));
    return new WorldStateV1(
        source.Header,
        partitions,
        source.SchedulerState,
        source.OperationState,
        source.DetailState,
        source.DomainRegistryState,
        source.Diagnostic.ConfigDigest);
}

var resident = Qa04ReferenceWorldMaterializerV1.MaterializeResidentIdentityLifecycle(1);
var config = new CoreConfigCoordinator().LoadStartup(
    """
    [meta]
    format = "machiverse-config"
    schema_version = "1.0"
    component = "simulation-core"
    """);
var registry = StandardDomainRegistryAuthorityV1.Generation1;
var detail = new DetailDirectoryV1(
    Array.Empty<DetailRegionStateV1>(),
    Array.Empty<DetailTransitionCandidateV1>());

var baseFrozen = BuildFrozenState(resident, config, registry, detail);
var baseAuthorities = Qa04ReducedWorldTypedAuthorityV1.BindAll97(resident);

var scopeId = SpatialScopeRegistryPayloadV1.PartitionId;
var priorScopeHeader = baseFrozen.Partitions.Get(scopeId).Header;
var scopePartition = Qa04SpatialTileScopeAuthorityV1.MaterializeCanonical();
var scopeHeader = PartitionStateHeaderV1.CreateCanonical(
    scopePartition,
    priorScopeHeader.Revision,
    priorScopeHeader.BasisStep,
    priorScopeHeader.DetailLevel,
    static payload => payload.CanonicalDigest());
var scopeAuthority = new DomainPartitionSnapshotAuthorityV1<SpatialScopeRegistryPayloadV1>(
    scopePartition,
    scopeHeader,
    static payload => payload.CanonicalDigest());

var terrainId = SpatialTerrainGeometryRecordSchemaV2.PartitionId;
var priorTerrainHeader = baseFrozen.Partitions.Get(terrainId).Header;
var terrainAuthority = Qa04TerrainCanonicalStreamingSnapshotAuthorityV1.Create(priorTerrainHeader);
Require(terrainAuthority.ActualItemCount == Qa04TerrainCanonicalRecordSourceV1.CanonicalRecordCount,
    "Full Terrain production authority must expose exactly 508,192 canonical records.");
Require(terrainAuthority.Header.ItemCount == Qa04TerrainCanonicalRecordSourceV1.CanonicalRecordCount,
    "Full Terrain frozen header must bind exactly 508,192 canonical records.");

var frozen = ReplaceHeaders(baseFrozen, scopeHeader, terrainAuthority.Header);
var exact97 = new DomainPartitionSnapshotAuthoritySetV1(
    frozen,
    baseAuthorities.CanonicalAuthorities
        .Where(authority =>
            authority.PartitionId.Value != scopeId &&
            authority.PartitionId.Value != terrainId)
        .Append<IDomainPartitionSnapshotAuthorityV1>(scopeAuthority)
        .Append<IDomainPartitionSnapshotAuthorityV1>(terrainAuthority));

var coreCut = CoreSnapshotOwnerMaterialCutV1.Create(
    frozen,
    Array.Empty<DurableOperationStateV1>(),
    Array.Empty<ScheduledOperationRefV1>(),
    new IFrozenCoreSnapshotOwnerMaterialV1[]
    {
        FrozenDetailDirectorySnapshotOwnerV1.Freeze(frozen.Header.Step, detail),
        FrozenDomainRegistrySnapshotOwnerV1.Freeze(frozen.Header.Step, registry),
        FrozenCoreConfigSnapshotOwnerV1.Freeze(frozen.Header.Step, config),
    });
var providers = StandardDomainSnapshotOwnerCompositionV1.CreateAllProviders();
var streaming = StandardSnapshotStreamingOwnerCompositionV1.CreateAll103WithTerrainV2(
    coreCut,
    exact97,
    providers);
var terrainSection = streaming.Single(section => section.SectionId == terrainId);
Require(streaming.Count == 103,
    "Full Terrain production canary must preserve exact-103 section cardinality.");
Require(terrainSection.LogicalItemCount == Qa04TerrainCanonicalRecordSourceV1.CanonicalRecordCount &&
        terrainSection.LogicalContentDigest.SequenceEqual(terrainAuthority.Header.CanonicalDigest),
    "Full Terrain exact-103 section metadata must bind the streaming production authority.");

var historyDigest = SHA256.HashData("terrain-full-production-history"u8);
var continuity = SHA256.HashData("terrain-full-production-continuity"u8);
var snapshotId = RunningSnapshotCoordinatorV1.DeriveSnapshotId(
    frozen.Header.WorldId,
    frozen.Header.Step,
    historyAnchorSequence: 1,
    historyDigest,
    continuity);
var cut = new RunningSnapshotCutV1(
    frozen,
    snapshotId,
    new HistoryAnchor(1, historyDigest),
    continuity,
    Array.Empty<DurableOperationStateV1>(),
    Array.Empty<ScheduledOperationRefV1>());

var root = Path.Combine(
    Path.GetTempPath(),
    "machiverse-terrain-full-production-" + Guid.NewGuid().ToString("N"));
try
{
    var world = PersistenceLayout.Resolve(root, frozen.Header.WorldId, generation: 1);
    PersistenceLayout.EnsureGenerationDirectories(world);
    var physical = SnapshotPhysicalStaging.Prepare(world, snapshotId);
    var codec = new ZstdSnapshotChunkCompressionCodecV1();

    var staged = await CanonicalSnapshotProductionManifestDrainV1.StageRunningCutStreamingAsync(
        cut,
        physical,
        streaming,
        config,
        Qa04ReferenceLoadV1.WorldSeed,
        zstdCodec: codec);

    Require(staged.Chunks.Count > 0,
        "Full Terrain production Snapshot must emit physical chunks.");
    Require(staged.Chunks.All(static chunk => chunk.Compression == SnapshotCompression.Zstd),
        "Full Terrain production Snapshot must use canonical Zstd compression.");
    Require(File.Exists(physical.StagingManifestPath),
        "Full Terrain production Snapshot must durably stage manifest.pb.");

    var recovered = await SpatialTerrainGeometryStagedRecoveryV2.VerifyAsync(
        physical,
        streaming,
        terrainAuthority.Header,
        frozen,
        cut,
        staged.SnapshotDigest,
        staged.PhysicalManifestDigest,
        CanonicalSnapshotProductionPhysicalDrainV1.ProductionDecoders(codec));

    Require(recovered.ScopeRegistry.ActualItemCount == Qa04SpatialTileScopeAuthorityV1.CanonicalScopeCount,
        "Full Terrain recovery must recover all canonical TileScope identities.");
    Require(recovered.Terrain.ActualItemCount == Qa04TerrainCanonicalRecordSourceV1.CanonicalRecordCount,
        "Full Terrain recovery must recover all 508,192 canonical Terrain records.");
    Require(checked((ulong)recovered.Terrain.RootClosures.Count) == Qa04TerrainRootMaterializerV1.CanonicalRootCount,
        "Full Terrain recovery must recover all 4,096 root closure relations.");

    var expectedIds = terrainAuthority.RecordIdsCanonical;
    var actualIds = recovered.Terrain.RecordIdsCanonical;
    Require(actualIds.Count == expectedIds.Count,
        "Full Terrain recovery RecordId cardinality must match the frozen authority.");
    for (var index = 0; index < expectedIds.Count; index++)
    {
        if (actualIds[index] != expectedIds[index])
            throw new InvalidOperationException($"Full Terrain recovery RecordId mismatch at canonical index {index}.");
    }

    for (ushort tile = 0; tile < Qa04ReferenceLoadV1.RegionalTileCount; tile++)
    {
        Require(recovered.Terrain.TryGetKind(
                Qa04TerrainRootMaterializerV1.RootId(tile),
                out var rootKind) &&
                rootKind == SpatialTerrainGeometryRecoveredRecordKindV2.Root,
            $"Full Terrain recovery must preserve Terrain root kind for tile {tile}.");
        Require(recovered.Terrain.TryGetKind(
                Qa04TerrainRootMaterializerV1.AnchorId(tile),
                out var anchorKind) &&
                anchorKind == SpatialTerrainGeometryRecoveredRecordKindV2.Brick,
            $"Full Terrain recovery must preserve D3 anchor brick kind for tile {tile}.");
    }

    Require(recovered.SemanticVerification.LogicalItemCount == terrainAuthority.ActualItemCount &&
            recovered.SemanticVerification.LogicalContentDigest.SequenceEqual(
                terrainAuthority.Header.CanonicalDigest),
        "Full Terrain recovery must reproduce the frozen 508,192-record semantic digest.");

    Console.WriteLine(
        $"terrain-full-production-pass records={recovered.Terrain.ActualItemCount} " +
        $"scopes={recovered.ScopeRegistry.ActualItemCount} roots={recovered.Terrain.RootClosures.Count} " +
        $"chunks={staged.Chunks.Count}");
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
}
