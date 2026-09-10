using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04PhysicalOccupancyV2Exact97ProductionCanaryInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var resident = Qa04ReferenceWorldMaterializerV1.MaterializeResidentIdentityLifecycle(1);
        var baseAuthorities = Qa04ReducedWorldTypedAuthorityV1.BindAll97(resident);
        var occupancyId = PhysicalOccupancyRecordSchemaV2.PartitionId;
        var priorHeader = resident.WorldState.Partitions.Get(occupancyId).Header;

        var shapeId = Id("00000000000000000000000000025200");
        var shape = new PhysicalOccupancyRecordMaterialV2(
            shapeId,
            revision: 1,
            createdStep: priorHeader.BasisStep,
            retiredStep: null,
            detailLevel: priorHeader.DetailLevel,
            lineageRef: null,
            payload: new PhysicalSphereShapePayloadV2(new Vec3MmV1(0, 0, 0), 350));
        var occupancyAuthority = PhysicalOccupancySnapshotAuthorityV2.CreateCanonical(
            new PhysicalOccupancyPartitionStateV2([shape]),
            revision: priorHeader.Revision,
            basisStep: priorHeader.BasisStep,
            detailLevel: priorHeader.DetailLevel);

        var migratedDirectory = new OrderedPartitionDirectoryV1(
            resident.WorldState.Partitions.CanonicalEntries.Select(entry =>
                entry.Header.PartitionId.Value == occupancyId
                    ? new PartitionStateRefV1(occupancyAuthority.Header)
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
            "Replacing truthful empty Physical occupancy with v2 canary material must change the WorldState digest.");
        Require(migratedWorld.Partitions.Get(occupancyId).Header.ItemCount == 1,
            "Migrated frozen WorldState must bind the actual Physical occupancy v2 record count.");

        var migratedAuthorities = baseAuthorities.CanonicalAuthorities
            .Where(authority => authority.PartitionId.Value != occupancyId)
            .Append<IDomainPartitionSnapshotAuthorityV1>(occupancyAuthority)
            .ToArray();
        var exact97 = new DomainPartitionSnapshotAuthoritySetV1(migratedWorld, migratedAuthorities);
        Require(exact97.CanonicalAuthorities.Count == StandardDomainPartitionRegistry.StandardPartitionCount,
            "Registered Physical occupancy migration must coexist with the other 96 authorities in exact-97 production material.");
        Require(exact97.Get(occupancyId).RecordSchema == PhysicalOccupancyRecordSchemaV2.RecordSchema,
            "Exact-97 authority set must preserve the actual Physical occupancy v2 record schema.");

        var providers = StandardDomainSnapshotOwnerCompositionV1.CreateAllProviders();
        var sections = DomainPartitionSnapshotProductionProviderV1.CreateAll97(exact97, providers);
        Require(sections.Count == StandardDomainPartitionRegistry.StandardPartitionCount,
            "Migration-aware production serialization must still emit exactly 97 Domain sections.");
        var occupancySection = sections.Single(section => section.SectionId == occupancyId);
        Require(occupancySection.LogicalItemCount == 1 &&
                occupancySection.LogicalContentDigest.SequenceEqual(occupancyAuthority.Header.CanonicalDigest),
            "Physical occupancy v2 production section must remain bound to the migrated frozen authority.");

        var recoveredOccupancy = new PhysicalOccupancyRecoveredReferenceSourceV2(occupancySection.Fragments);
        Require(recoveredOccupancy.RecordIdsCanonical.Count == 1 &&
                recoveredOccupancy.RecordIdsCanonical[0] == shapeId &&
                recoveredOccupancy.RecordSchema == PhysicalOccupancyRecordSchemaV2.RecordSchema,
            "Physical occupancy v2 production section must recover the exact migrated record identity/schema.");

        var recoveredAll97 = DomainSnapshotReferenceResolverV1.FromRecoveredSections(sections);
        var shapeRef = new PartitionRecordRefV1(occupancyId, shapeId);
        Require(recoveredAll97.TryGetRecordSchema(shapeRef, out var recoveredSchema) &&
                recoveredSchema == PhysicalOccupancyRecordSchemaV2.RecordSchema,
            "Recovery phase 1 must preserve Physical occupancy v2 through the complete 97-section production set.");
        Require(recoveredAll97.RecordCount == checked(resident.MaterializedRecordCount + 1UL),
            "Exact-97 recovery must contain only the truthful Resident fixture plus one Physical occupancy migration canary record.");

        var standardProvider = providers.Single(provider => provider.SectionId == occupancyId);
        var resolvedProvider = DomainSnapshotRecordSchemaMigrationProviderRegistryV1.Resolve(
            occupancyAuthority,
            standardProvider);
        Require(resolvedProvider is PhysicalOccupancySnapshotSectionProviderV2,
            "Actual Physical occupancy v2 authority must select the registered v2 production provider.");

        var semanticVerifier = resolvedProvider.CreateSemanticVerifier(occupancyAuthority.Header);
        var semanticResult = semanticVerifier.VerifyWithContext?.Invoke(
            occupancySection.Fragments,
            new SnapshotSectionSemanticVerificationContextV1(recoveredAll97))
            ?? throw new InvalidOperationException("Physical occupancy v2 production verifier must support recovered all-97 reference context.");
        Require(semanticResult.LogicalItemCount == occupancySection.LogicalItemCount &&
                semanticResult.LogicalContentDigest.SequenceEqual(occupancySection.LogicalContentDigest),
            "Recovery phase 2 must reconstruct and rehash Physical occupancy v2 against the recovered all-97 resolver.");

        Require(StandardDomainPartitionRegistry.Get(occupancyId).RecordSchema.Version == new SchemaVersionV1(1, 0),
            "The Physical occupancy migration canary must not mutate the global standard registry from v1.");
    }

    private static OpaqueId128 Id(string value) => OpaqueId128.Parse(value);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
