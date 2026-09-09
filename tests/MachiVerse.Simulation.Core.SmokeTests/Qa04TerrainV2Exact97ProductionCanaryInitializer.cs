using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04TerrainV2Exact97ProductionCanaryInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var resident = Qa04ReferenceWorldMaterializerV1.MaterializeResidentIdentityLifecycle(1);
        var baseAuthorities = Qa04ReducedWorldTypedAuthorityV1.BindAll97(resident);
        var terrainId = SpatialTerrainGeometryRecordSchemaV2.PartitionId;
        var priorTerrainHeader = resident.WorldState.Partitions.Get(terrainId).Header;

        var brickId = Id("00000000000000000000000000026200");
        var brick = SpatialTerrainGeometryRecordMaterialV2.FromTerrainBrick(
            new TerrainBrickV1(
                brickId,
                0,
                new SpatialCellKeyV1(0, 0, 0, 0),
                250,
                Enumerable.Repeat(0, TerrainBrickV1.SdfSampleCount),
                Enumerable.Repeat((ushort)1, TerrainBrickV1.SurfaceMaterialCount),
                1),
            createdStep: priorTerrainHeader.BasisStep,
            detailLevel: priorTerrainHeader.DetailLevel);
        var terrainAuthority = SpatialTerrainGeometrySnapshotAuthorityV2.CreateCanonical(
            new SpatialTerrainGeometryPartitionStateV2([brick]),
            revision: priorTerrainHeader.Revision,
            basisStep: priorTerrainHeader.BasisStep,
            detailLevel: priorTerrainHeader.DetailLevel);

        var migratedDirectory = new OrderedPartitionDirectoryV1(
            resident.WorldState.Partitions.CanonicalEntries.Select(entry =>
                entry.Header.PartitionId.Value == terrainId
                    ? new PartitionStateRefV1(terrainAuthority.Header)
                    : entry));
        var migratedWorld = new WorldStateV1(
            resident.WorldState.Header,
            migratedDirectory,
            resident.WorldState.SchedulerState,
            resident.WorldState.OperationState,
            resident.WorldState.DetailState,
            resident.WorldState.DomainRegistryState,
            resident.WorldState.Diagnostic.ConfigDigest);

        Require(!migratedWorld.Diagnostic.StateDigest.SequenceEqual(resident.WorldState.Diagnostic.StateDigest),
            "Replacing truthful empty Terrain with v2 canary material must change the WorldState digest.");
        Require(migratedWorld.Partitions.Get(terrainId).Header.ItemCount == 1,
            "Migrated frozen WorldState must bind the actual Terrain v2 record count.");

        var migratedAuthorities = baseAuthorities.CanonicalAuthorities
            .Where(authority => authority.PartitionId.Value != terrainId)
            .Append<IDomainPartitionSnapshotAuthorityV1>(terrainAuthority)
            .ToArray();
        var exact97 = new DomainPartitionSnapshotAuthoritySetV1(migratedWorld, migratedAuthorities);
        Require(exact97.CanonicalAuthorities.Count == StandardDomainPartitionRegistry.StandardPartitionCount,
            "Registered Terrain migration must coexist with the other 96 authorities in exact-97 production material.");
        Require(exact97.Get(terrainId).RecordSchema == SpatialTerrainGeometryRecordSchemaV2.RecordSchema,
            "Exact-97 authority set must preserve the actual Terrain v2 record schema.");

        var providers = StandardDomainSnapshotOwnerCompositionV1.CreateAllProviders();
        var sections = DomainPartitionSnapshotProductionProviderV1.CreateAll97(exact97, providers);
        Require(sections.Count == StandardDomainPartitionRegistry.StandardPartitionCount,
            "Migration-aware production serialization must still emit exactly 97 Domain sections.");
        var terrainSection = sections.Single(section => section.SectionId == terrainId);
        Require(terrainSection.LogicalItemCount == 1 &&
                terrainSection.LogicalContentDigest.SequenceEqual(terrainAuthority.Header.CanonicalDigest),
            "Terrain v2 production section must remain bound to the migrated frozen authority.");

        var recoveredTerrain = new SpatialTerrainGeometryRecoveredReferenceSourceV2(terrainSection.Fragments);
        Require(recoveredTerrain.RecordIdsCanonical.Count == 1 &&
                recoveredTerrain.RecordIdsCanonical[0] == brickId &&
                recoveredTerrain.RecordSchema == SpatialTerrainGeometryRecordSchemaV2.RecordSchema,
            "Terrain v2 production section must recover the exact migrated record identity/schema.");

        var recoveredAll97 = DomainSnapshotReferenceResolverV1.FromRecoveredSections(sections);
        var brickRef = new PartitionRecordRefV1(terrainId, brickId);
        Require(recoveredAll97.TryGetRecordSchema(brickRef, out var recoveredSchema) &&
                recoveredSchema == SpatialTerrainGeometryRecordSchemaV2.RecordSchema,
            "Recovery phase 1 must preserve Terrain v2 through the complete 97-section production set.");
        Require(recoveredAll97.RecordCount == checked(resident.MaterializedRecordCount + 1UL),
            "Exact-97 recovery must contain only the truthful Resident fixture plus one Terrain migration canary record.");

        var standardTerrainProvider = providers.Single(provider => provider.SectionId == terrainId);
        var resolvedProvider = DomainSnapshotRecordSchemaMigrationProviderRegistryV1.Resolve(
            terrainAuthority,
            standardTerrainProvider);
        Require(resolvedProvider is SpatialTerrainGeometrySnapshotSectionProviderV2,
            "Actual Terrain v2 authority must select the registered v2 production provider.");
        Require(StandardDomainPartitionRegistry.Get(terrainId).RecordSchema.Version == new SchemaVersionV1(1, 0),
            "The migration canary must not mutate the global standard registry from v1.");
    }

    private static OpaqueId128 Id(string value) => OpaqueId128.Parse(value);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
