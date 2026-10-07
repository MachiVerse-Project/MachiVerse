using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Configuration;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04InfrastructureNetworkV2Exact103ProductionCanaryInitializer
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

        const string scopeId = SpatialScopeRegistryPayloadV1.PartitionId;
        const string voidId = SpatialVoidGeometryPayloadV1.PartitionId;
        const string infrastructureId = InfrastructureNetworkTopologyRecordSchemaV2.PartitionId;
        var scopeRecordId = Id("00000000000000000000000000046501");
        var voidRecordId = Id("00000000000000000000000000046502");
        var networkId = Id("00000000000000000000000000046510");
        var nodeAId = Id("00000000000000000000000000046520");
        var nodeBId = Id("00000000000000000000000000046521");
        var edgeId = Id("00000000000000000000000000046530");
        var scopeRef = new PartitionRecordRefV1(scopeId, scopeRecordId);
        var voidRef = new PartitionRecordRefV1(voidId, voidRecordId);
        var networkRef = new PartitionRecordRefV1(infrastructureId, networkId);
        var nodeARef = new PartitionRecordRefV1(infrastructureId, nodeAId);
        var nodeBRef = new PartitionRecordRefV1(infrastructureId, nodeBId);
        var edgeRef = new PartitionRecordRefV1(infrastructureId, edgeId);

        var scopeAuthority = CreateScopeAuthority(baseFrozen, scopeRecordId, voidRef);
        var voidAuthority = CreateVoidAuthority(baseFrozen, voidRecordId, voidRef);
        var nodeRefs = new[] { nodeARef, nodeBRef }
            .OrderBy(static reference => reference.RecordId)
            .ToArray();
        var records = new InfrastructureNetworkTopologyRecordMaterialV2[]
        {
            Material(networkId, new InfrastructureNetworkPayloadV2(
                new StableToken("transport"),
                nodeRefs,
                new[] { edgeRef },
                Array.Empty<PartitionRecordRefV1>(),
                new[] { scopeRef },
                new StableToken("active"),
                1)),
            Material(nodeAId, new InfrastructureNetworkNodePayloadV2(
                networkRef, new StableToken("junction"), scopeRef, 1_000, 1_000_000, new StableToken("active"))),
            Material(nodeBId, new InfrastructureNetworkNodePayloadV2(
                networkRef, new StableToken("junction"), scopeRef, 1_001, 1_000_000, new StableToken("active"))),
            Material(edgeId, new InfrastructureNetworkEdgePayloadV2(
                networkRef, nodeARef, nodeBRef, new StableToken("link"), 1, 100, 1_000_000, new StableToken("active"))),
        };
        InfrastructureNetworkTopologyReferenceClosureV2.Validate(records);
        var prior = baseFrozen.Partitions.Get(infrastructureId).Header;
        var infrastructureAuthority = InfrastructureNetworkTopologySnapshotAuthorityV2.CreateCanonical(
            new InfrastructureNetworkTopologyPartitionStateV2(records),
            prior.Revision,
            prior.BasisStep,
            prior.DetailLevel);

        var replacementHeaders = new Dictionary<string, PartitionStateHeaderV1>(StringComparer.Ordinal)
        {
            [scopeId] = scopeAuthority.Header,
            [voidId] = voidAuthority.Header,
            [infrastructureId] = infrastructureAuthority.Header,
        };
        var migratedFrozen = ReplaceHeaders(baseFrozen, replacementHeaders);
        var replaced = new HashSet<string>(StringComparer.Ordinal) { scopeId, voidId, infrastructureId };
        var exact97 = new DomainPartitionSnapshotAuthoritySetV1(
            migratedFrozen,
            baseAuthorities.CanonicalAuthorities
                .Where(authority => !replaced.Contains(authority.PartitionId.Value))
                .Concat<IDomainPartitionSnapshotAuthorityV1>([scopeAuthority, voidAuthority, infrastructureAuthority]));

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
        var sections = StandardSnapshotOwnerCompositionV1.CreateAll103(coreCut, exact97, providers);

        Require(sections.Count == 103,
            "Infrastructure v2 migration canary must compose exact six-Core plus 97-Domain sections.");
        Require(sections.Count(section => StandardSnapshotSectionSetV1.IsCoreSection(section.SectionId)) == 6,
            "Infrastructure v2 migration canary must retain exactly six Core sections.");
        Require(sections.Count(section => StandardDomainPartitionRegistry.TryGet(section.SectionId, out _)) == 97,
            "Infrastructure v2 migration canary must retain exactly 97 Domain sections.");
        var infrastructureSection = sections.Single(section => section.SectionId == infrastructureId);
        Require(infrastructureSection.LogicalItemCount == 4 &&
                infrastructureSection.LogicalContentDigest.SequenceEqual(infrastructureAuthority.Header.CanonicalDigest),
            "Exact-103 Infrastructure section must remain bound to the migrated v2 authority.");

        var recoveredInfrastructure = new InfrastructureNetworkTopologyRecoveredReferenceSourceV2(infrastructureSection.Fragments);
        Require(recoveredInfrastructure.RecordSchema == InfrastructureNetworkTopologyRecordSchemaV2.RecordSchema &&
                recoveredInfrastructure.TryGetRecordKind(networkId, out var networkKind) &&
                networkKind == InfrastructureNetworkTopologyRecordSchemaV2.NetworkKind &&
                recoveredInfrastructure.TryGetRecordKind(nodeAId, out var nodeKind) &&
                nodeKind == InfrastructureNetworkTopologyRecordSchemaV2.NodeKind &&
                recoveredInfrastructure.TryGetRecordKind(edgeId, out var edgeKind) &&
                edgeKind == InfrastructureNetworkTopologyRecordSchemaV2.EdgeKind,
            "Exact-103 Infrastructure recovered source must preserve heterogeneous target kinds.");

        var domainSections = sections
            .Where(section => StandardDomainPartitionRegistry.TryGet(section.SectionId, out _))
            .ToArray();
        var recoveredReferences = DomainSnapshotReferenceResolverV1.FromRecoveredSections(domainSections);
        Require(recoveredReferences.TryGetRecordSchema(networkRef, out var recoveredSchema) &&
                recoveredSchema == InfrastructureNetworkTopologyRecordSchemaV2.RecordSchema,
            "Exact-103 recovery context must preserve the Infrastructure v2 record schema.");

        var semanticVerifiers = StandardSnapshotOwnerCompositionV1.CreateSemanticVerifierRegistry(
            coreCut,
            exact97,
            providers);
        semanticVerifiers.VerifyAll(
            sections,
            new SnapshotSectionSemanticVerificationContextV1(recoveredReferences));

        Require(StandardDomainPartitionRegistry.Get(infrastructureId).RecordSchema.Version == new SchemaVersionV1(1, 0),
            "Exact-103 Infrastructure migration canary must not mutate the standard registry from v1.");
    }

    private static InfrastructureNetworkTopologyRecordMaterialV2 Material(
        OpaqueId128 recordId,
        InfrastructureNetworkTopologyRecordPayloadV2 payload)
        => new(recordId, 1, 0, null, DetailLevelV1.D2RegionalAggregate, null, payload);

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

    private static DomainPartitionSnapshotAuthorityV1<SpatialScopeRegistryPayloadV1> CreateScopeAuthority(
        WorldStateV1 world,
        OpaqueId128 recordId,
        PartitionRecordRefV1 geometryRef)
    {
        var identity = StandardDomainPartitionRegistry.Get(SpatialScopeRegistryPayloadV1.PartitionId);
        var prior = world.Partitions.Get(identity.PartitionId.Value).Header;
        var payload = new SpatialScopeRegistryPayloadV1(
            new StableToken("qa04.canary.scope"),
            geometryRef,
            null,
            prior.BasisStep,
            null,
            0);
        var record = new DomainRecordEnvelopeV1<SpatialScopeRegistryPayloadV1>(
            recordId, identity.RecordSchema, 1, prior.BasisStep, null, prior.DetailLevel, null, payload);
        var partition = new DomainPartitionStateV1<SpatialScopeRegistryPayloadV1>(identity, [record]);
        var header = PartitionStateHeaderV1.CreateCanonical(
            partition, prior.Revision, prior.BasisStep, prior.DetailLevel, static value => value.CanonicalDigest());
        return new DomainPartitionSnapshotAuthorityV1<SpatialScopeRegistryPayloadV1>(
            partition, header, static value => value.CanonicalDigest());
    }

    private static DomainPartitionSnapshotAuthorityV1<SpatialVoidGeometryPayloadV1> CreateVoidAuthority(
        WorldStateV1 world,
        OpaqueId128 recordId,
        PartitionRecordRefV1 selfRef)
    {
        var identity = StandardDomainPartitionRegistry.Get(SpatialVoidGeometryPayloadV1.PartitionId);
        var prior = world.Partitions.Get(identity.PartitionId.Value).Header;
        var payload = new SpatialVoidGeometryPayloadV1(
            selfRef,
            Array.Empty<PartitionRecordRefV1>(),
            Array.Empty<PartitionRecordRefV1>(),
            new StableToken("qa04.canary"),
            new StableToken("active"),
            1);
        var record = new DomainRecordEnvelopeV1<SpatialVoidGeometryPayloadV1>(
            recordId, identity.RecordSchema, 1, prior.BasisStep, null, prior.DetailLevel, null, payload);
        var partition = new DomainPartitionStateV1<SpatialVoidGeometryPayloadV1>(identity, [record]);
        var header = PartitionStateHeaderV1.CreateCanonical(
            partition, prior.Revision, prior.BasisStep, prior.DetailLevel, static value => value.CanonicalDigest());
        return new DomainPartitionSnapshotAuthorityV1<SpatialVoidGeometryPayloadV1>(
            partition, header, static value => value.CanonicalDigest());
    }

    private static WorldStateV1 ReplaceHeaders(
        WorldStateV1 source,
        IReadOnlyDictionary<string, PartitionStateHeaderV1> replacements)
    {
        var partitions = new OrderedPartitionDirectoryV1(
            source.Partitions.CanonicalEntries.Select(entry =>
                replacements.TryGetValue(entry.Header.PartitionId.Value, out var replacement)
                    ? new PartitionStateRefV1(replacement)
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
