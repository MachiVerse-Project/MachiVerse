using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Configuration;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04TerrainV2Exact103ProductionCanaryInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
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
        var terrainId = SpatialTerrainGeometryRecordSchemaV2.PartitionId;
        var priorTerrainHeader = baseFrozen.Partitions.Get(terrainId).Header;
        var brickId = Id("00000000000000000000000000026300");
        var brick = SpatialTerrainGeometryRecordMaterialV2.FromTerrainBrick(
            new TerrainBrickV1(
                brickId,
                0,
                new SpatialCellKeyV1(0, 1, 1, 1),
                250,
                Enumerable.Repeat(5, TerrainBrickV1.SdfSampleCount),
                Enumerable.Repeat((ushort)2, TerrainBrickV1.SurfaceMaterialCount),
                1),
            createdStep: priorTerrainHeader.BasisStep,
            detailLevel: priorTerrainHeader.DetailLevel);
        var terrainAuthority = SpatialTerrainGeometrySnapshotAuthorityV2.CreateCanonical(
            new SpatialTerrainGeometryPartitionStateV2([brick]),
            revision: priorTerrainHeader.Revision,
            basisStep: priorTerrainHeader.BasisStep,
            detailLevel: priorTerrainHeader.DetailLevel);
        var migratedFrozen = ReplaceTerrainHeader(baseFrozen, terrainAuthority.Header);

        var exact97 = new DomainPartitionSnapshotAuthoritySetV1(
            migratedFrozen,
            baseAuthorities.CanonicalAuthorities
                .Where(authority => authority.PartitionId.Value != terrainId)
                .Append<IDomainPartitionSnapshotAuthorityV1>(terrainAuthority));
        var coreCut = CoreSnapshotOwnerMaterialCutV1.Create(
            migratedFrozen,
            Array.Empty<DurableOperationStateV1>(),
            Array.Empty<ScheduledOperationRefV1>(),
            new IFrozenCoreSnapshotOwnerMaterialV1[]
            {
                FrozenDetailDirectorySnapshotOwnerV1.Freeze(migratedFrozen.Header.Step, detail),
                FrozenDomainRegistrySnapshotOwnerV1.Freeze(migratedFrozen.Header.Step, registry),
                FrozenCoreConfigSnapshotOwnerV1.Freeze(migratedFrozen.Header.Step, config),
            });
        var providers = StandardDomainSnapshotOwnerCompositionV1.CreateAllProviders();
        var sections = StandardSnapshotOwnerCompositionV1.CreateAll103(
            coreCut,
            exact97,
            providers);

        Require(sections.Count == 103,
            "Terrain v2 migration canary must compose the exact six-Core plus 97-Domain section set.");
        Require(sections.Count(section => StandardSnapshotSectionSetV1.IsCoreSection(section.SectionId)) == 6,
            "Terrain v2 migration canary must retain exactly six Core sections.");
        Require(sections.Count(section => StandardDomainPartitionRegistry.TryGet(section.SectionId, out _)) == 97,
            "Terrain v2 migration canary must retain exactly 97 Domain sections.");
        var terrainSection = sections.Single(section => section.SectionId == terrainId);
        Require(terrainSection.LogicalItemCount == 1 &&
                terrainSection.LogicalContentDigest.SequenceEqual(terrainAuthority.Header.CanonicalDigest),
            "Exact-103 Terrain section must remain bound to the migrated v2 authority.");

        var domainSections = sections
            .Where(section => StandardDomainPartitionRegistry.TryGet(section.SectionId, out _))
            .ToArray();
        var recoveredReferences = DomainSnapshotReferenceResolverV1.FromRecoveredSections(domainSections);
        Require(recoveredReferences.TryGetRecordSchema(
                new PartitionRecordRefV1(terrainId, brickId),
                out var recoveredSchema) &&
                recoveredSchema == SpatialTerrainGeometryRecordSchemaV2.RecordSchema,
            "Exact-103 recovery context must preserve the Terrain v2 record schema.");

        var semanticVerifiers = StandardSnapshotOwnerCompositionV1.CreateSemanticVerifierRegistry(
            coreCut,
            exact97,
            providers);
        semanticVerifiers.VerifyAll(
            sections,
            new SnapshotSectionSemanticVerificationContextV1(recoveredReferences));

        Require(StandardDomainPartitionRegistry.Get(terrainId).RecordSchema.Version == new SchemaVersionV1(1, 0),
            "Exact-103 migration canary must not mutate the standard registry from v1.");
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

    private static WorldStateV1 ReplaceTerrainHeader(
        WorldStateV1 source,
        PartitionStateHeaderV1 terrainHeader)
    {
        var terrainId = SpatialTerrainGeometryRecordSchemaV2.PartitionId;
        var partitions = new OrderedPartitionDirectoryV1(
            source.Partitions.CanonicalEntries.Select(entry =>
                entry.Header.PartitionId.Value == terrainId
                    ? new PartitionStateRefV1(terrainHeader)
                    : entry));
        return new WorldStateV1(
            source.Header,
            partitions,
            source.SchedulerState,
            source.OperationState,
            source.DetailState,
            source.DomainRegistryState,
            source.Diagnostic.ConfigDigest);
    }

    private static OpaqueId128 Id(string value) => OpaqueId128.Parse(value);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
