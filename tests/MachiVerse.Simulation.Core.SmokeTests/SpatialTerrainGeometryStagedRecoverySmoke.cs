using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Configuration;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

internal static class SpatialTerrainGeometryStagedRecoverySmoke
{
    internal static async Task RunAsync()
    {
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
        var terrainPartition = new SpatialTerrainGeometryPartitionStateV2(
            Qa04TerrainRootMaterializerV1.MaterializeCanonical(
                Qa04SpatialTileScopeAuthorityV1.ScopeRef));
        var terrainAuthority = SpatialTerrainGeometrySnapshotAuthorityV2.CreateCanonical(
            terrainPartition,
            priorTerrainHeader.Revision,
            priorTerrainHeader.BasisStep,
            priorTerrainHeader.DetailLevel);

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
        var materialized = StandardSnapshotOwnerCompositionV1.CreateAll103(
            coreCut,
            exact97,
            providers);
        var terrainSection = materialized.Single(section => section.SectionId == terrainId);
        var terrainFragmentSource = new SpatialTerrainGeometryStreamingFragmentSourceV2(
            terrainAuthority.Header,
            terrainAuthority.ActualItemCount,
            () => terrainAuthority.Partition.RecordSet.RecordsCanonical);
        var streaming = materialized
            .Select(section => string.Equals(section.SectionId, terrainId, StringComparison.Ordinal)
                ? new CanonicalSnapshotStreamingSectionV1(
                    section.SectionId,
                    section.SectionSchema,
                    section.LogicalItemCount,
                    section.LogicalContentDigest,
                    terrainFragmentSource.EnumerateFragments)
                : CanonicalSnapshotStreamingSectionV1.FromMaterialized(section))
            .ToArray();

        Require(streaming.Length == SnapshotManifestValidation.StandardRequiredSectionCount,
            "Staged Terrain recovery smoke must preserve exact-103 section cardinality.");
        Require(scopeAuthority.ActualItemCount == Qa04SpatialTileScopeAuthorityV1.CanonicalScopeCount,
            "Staged Terrain recovery smoke must use the canonical 4,096 TileScope authority.");
        Require(terrainAuthority.ActualItemCount ==
                Qa04TerrainRootMaterializerV1.CanonicalRootCount +
                Qa04TerrainRootMaterializerV1.CanonicalAnchorCount,
            "Staged Terrain recovery smoke must use all canonical roots and D3 anchors.");
        Require(terrainSection.LogicalContentDigest.SequenceEqual(terrainAuthority.Header.CanonicalDigest),
            "Materialized exact-103 Terrain metadata must bind the canonical reduced Terrain authority.");

        var historyDigest = SHA256.HashData("terrain-staged-recovery-history"u8);
        var continuity = SHA256.HashData("terrain-staged-recovery-continuity"u8);
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
            "machiverse-terrain-staged-recovery-" + Guid.NewGuid().ToString("N"));
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

            Require(staged.Chunks.Count > 0 && staged.Chunks.All(static chunk => chunk.Compression == SnapshotCompression.Zstd),
                "Streaming production manifest drain must stage actual Zstd MVCHNK01 chunks.");
            Require(File.Exists(physical.StagingManifestPath),
                "Streaming production manifest drain must durably write manifest.pb.");

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
                "Staged recovery must recover all 4,096 TileScope identities from the physical Snapshot.");
            Require(recovered.Terrain.ActualItemCount == terrainAuthority.ActualItemCount,
                "Staged recovery must recover the exact canonical reduced Terrain record count.");
            Require(recovered.Terrain.RootClosures.Count == Qa04TerrainRootMaterializerV1.CanonicalRootCount,
                "Staged recovery must retain all canonical Terrain root closure relations.");
            Require(recovered.Terrain.TryGetKind(
                    Qa04TerrainRootMaterializerV1.RootId(0),
                    out var rootKind) &&
                    rootKind == SpatialTerrainGeometryRecoveredRecordKindV2.Root,
                "Staged recovery must preserve canonical Terrain root identity/kind.");
            Require(recovered.Terrain.TryGetKind(
                    Qa04TerrainRootMaterializerV1.AnchorId(0),
                    out var anchorKind) &&
                    anchorKind == SpatialTerrainGeometryRecoveredRecordKindV2.Brick,
                "Staged recovery must preserve canonical Terrain D3 anchor identity/kind.");
            Require(recovered.SemanticVerification.LogicalItemCount == terrainAuthority.ActualItemCount &&
                    recovered.SemanticVerification.LogicalContentDigest.SequenceEqual(
                        terrainAuthority.Header.CanonicalDigest),
                "Staged async Terrain recovery must reproduce the frozen canonical semantic digest.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static WorldStateV1 BuildFrozenState(
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

    private static WorldStateV1 ReplaceHeaders(
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

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
