using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Environment;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Domains.Participation;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04TerrainV2SpatialOwnerCompositionInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var residentMaterialization = Qa04ReferenceWorldMaterializerV1.MaterializeResidentIdentityLifecycle(1);
        var terrainId = SpatialTerrainGeometryRecordSchemaV2.PartitionId;
        var priorTerrainHeader = residentMaterialization.WorldState.Partitions.Get(terrainId).Header;
        var terrainPartition = new SpatialTerrainGeometryPartitionStateV2(
        [
            SpatialTerrainGeometryRecordMaterialV2.FromTerrainBrick(
                new TerrainBrickV1(
                    Id("00000000000000000000000000026400"),
                    0,
                    new SpatialCellKeyV1(0, 2, 2, 2),
                    250,
                    Enumerable.Repeat(7, TerrainBrickV1.SdfSampleCount),
                    Enumerable.Repeat((ushort)3, TerrainBrickV1.SurfaceMaterialCount),
                    1),
                createdStep: priorTerrainHeader.BasisStep,
                detailLevel: priorTerrainHeader.DetailLevel),
        ]);
        var terrainHeader = SpatialTerrainGeometrySnapshotAuthorityV2.CreateCanonical(
            terrainPartition,
            priorTerrainHeader.Revision,
            priorTerrainHeader.BasisStep,
            priorTerrainHeader.DetailLevel).Header;
        var migratedWorld = ReplaceTerrainHeader(residentMaterialization.WorldState, terrainHeader);

        var resident = CreateResidentState(residentMaterialization).BindSnapshotMaterial(migratedWorld);
        var participation = ParticipationDomainStateV1.CreateEmpty().BindSnapshotMaterial(migratedWorld);
        var physical = PhysicalBuiltDomainStateV1.CreateEmpty().BindSnapshotMaterial(migratedWorld);
        var spatial = SpatialDomainStateV2.CreateWithTerrain(terrainPartition).BindSnapshotMaterial(migratedWorld);
        var environment = EnvironmentDomainStateV1.CreateEmpty().BindSnapshotMaterial(migratedWorld);
        var society = SocietyEconomyDomainStateV1.CreateEmpty().BindSnapshotMaterial(migratedWorld);
        var infrastructure = InfrastructureInformationDomainStateV1.CreateEmpty().BindSnapshotMaterial(migratedWorld);
        var governance = GovernanceSecurityDomainStateV1.CreateEmpty().BindSnapshotMaterial(migratedWorld);

        Require(spatial.Authorities.Count == 8 &&
                spatial.TerrainGeometry.RecordSchema == SpatialTerrainGeometryRecordSchemaV2.RecordSchema,
            "Spatial v2 owner material must contain seven standard authorities plus exact Terrain v2 authority.");

        var exact97 = StandardDomainSnapshotOwnerCompositionV1.CreateAuthoritySet(
            migratedWorld,
            resident,
            participation,
            physical,
            spatial,
            environment,
            society,
            infrastructure,
            governance);
        Require(exact97.CanonicalAuthorities.Count == StandardDomainPartitionRegistry.StandardPartitionCount,
            "Eight owner materials including Spatial v2 must compose to exact-97 authorities.");
        Require(exact97.CanonicalAuthorities.Sum(static authority => authority.ActualItemCount) == 2UL,
            "Owner composition fixture must contain only one Resident record and one Terrain v2 canary record.");
        Require(exact97.Get(terrainId) is SpatialTerrainGeometrySnapshotAuthorityV2,
            "Exact-97 owner composition must retain the specialized Terrain v2 authority.");

        var sections = DomainPartitionSnapshotProductionProviderV1.CreateAll97(
            exact97,
            StandardDomainSnapshotOwnerCompositionV1.CreateAllProviders());
        var terrainSection = sections.Single(section => section.SectionId == terrainId);
        Require(terrainSection.LogicalItemCount == 1 &&
                terrainSection.LogicalContentDigest.SequenceEqual(terrainHeader.CanonicalDigest),
            "Owner-composed Terrain v2 authority must serialize through the production v2 provider.");
    }

    private static WorldStateV1 ReplaceTerrainHeader(
        WorldStateV1 source,
        PartitionStateHeaderV1 terrainHeader)
        => new(
            source.Header,
            new OrderedPartitionDirectoryV1(
                source.Partitions.CanonicalEntries.Select(entry =>
                    entry.Header.PartitionId.Value == SpatialTerrainGeometryRecordSchemaV2.PartitionId
                        ? new PartitionStateRefV1(terrainHeader)
                        : entry)),
            source.SchedulerState,
            source.OperationState,
            source.DetailState,
            source.DomainRegistryState,
            source.Diagnostic.ConfigDigest);

    private static ResidentDomainStateV1 CreateResidentState(Qa04ResidentIdentityMaterializationV1 materialized)
        => new(
            materialized.Partition,
            Empty<ResidentBodyHealthPayloadV1>(ResidentBodyHealthPayloadV1.PartitionId),
            Empty<ResidentPhysiologyPayloadV1>(ResidentPhysiologyPayloadV1.PartitionId),
            Empty<ResidentPerceptionPayloadV1>(ResidentPerceptionPayloadV1.PartitionId),
            Empty<ResidentKnowledgeBeliefPayloadV1>(ResidentKnowledgeBeliefPayloadV1.PartitionId),
            Empty<ResidentMemoryPayloadV1>(ResidentMemoryPayloadV1.PartitionId),
            Empty<ResidentPsychologyPayloadV1>(ResidentPsychologyPayloadV1.PartitionId),
            Empty<ResidentGoalPlanPayloadV1>(ResidentGoalPlanPayloadV1.PartitionId),
            Empty<ResidentSkillAptitudePayloadV1>(ResidentSkillAptitudePayloadV1.PartitionId),
            Empty<ResidentRelationshipPayloadV1>(ResidentRelationshipPayloadV1.PartitionId),
            Empty<ResidentFamilyLineagePayloadV1>(ResidentFamilyLineagePayloadV1.PartitionId),
            Empty<ResidentBehaviorStatePayloadV1>(ResidentBehaviorStatePayloadV1.PartitionId),
            Empty<ResidentLineagePayloadV1>(ResidentLineagePayloadV1.PartitionId));

    private static DomainPartitionStateV1<TPayload> Empty<TPayload>(string partitionId)
        => new(
            StandardDomainPartitionRegistry.Get(partitionId),
            Array.Empty<DomainRecordEnvelopeV1<TPayload>>());

    private static OpaqueId128 Id(string value) => OpaqueId128.Parse(value);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
