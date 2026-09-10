using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04InfrastructureNetworkV2Exact97ProductionCanaryInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var resident = Qa04ReferenceWorldMaterializerV1.MaterializeResidentIdentityLifecycle(1);
        var baseAuthorities = Qa04ReducedWorldTypedAuthorityV1.BindAll97(resident);

        const string scopeId = SpatialScopeRegistryPayloadV1.PartitionId;
        const string voidId = SpatialVoidGeometryPayloadV1.PartitionId;
        const string infrastructureId = InfrastructureNetworkTopologyRecordSchemaV2.PartitionId;
        var scopeRecordId = Id("00000000000000000000000000046401");
        var voidRecordId = Id("00000000000000000000000000046402");
        var networkId = Id("00000000000000000000000000046410");
        var nodeAId = Id("00000000000000000000000000046420");
        var nodeBId = Id("00000000000000000000000000046421");
        var edgeId = Id("00000000000000000000000000046430");
        var scopeRef = new PartitionRecordRefV1(scopeId, scopeRecordId);
        var voidRef = new PartitionRecordRefV1(voidId, voidRecordId);
        var networkRef = new PartitionRecordRefV1(infrastructureId, networkId);
        var nodeARef = new PartitionRecordRefV1(infrastructureId, nodeAId);
        var nodeBRef = new PartitionRecordRefV1(infrastructureId, nodeBId);
        var edgeRef = new PartitionRecordRefV1(infrastructureId, edgeId);

        var scopeAuthority = CreateScopeAuthority(resident.WorldState, scopeRecordId, voidRef);
        var voidAuthority = CreateVoidAuthority(resident.WorldState, voidRecordId, voidRef);

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

        var prior = resident.WorldState.Partitions.Get(infrastructureId).Header;
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
        var migratedDirectory = new OrderedPartitionDirectoryV1(
            resident.WorldState.Partitions.CanonicalEntries.Select(entry =>
                replacementHeaders.TryGetValue(entry.Header.PartitionId.Value, out var replacement)
                    ? new PartitionStateRefV1(replacement)
                    : entry));
        var migratedWorld = new WorldStateV1(
            resident.WorldState.Header,
            migratedDirectory,
            resident.WorldState.SchedulerState,
            resident.WorldState.OperationState,
            resident.WorldState.DetailState,
            resident.WorldState.DomainRegistryState,
            resident.WorldState.Diagnostic.ConfigDigest);

        var replaced = new HashSet<string>(StringComparer.Ordinal) { scopeId, voidId, infrastructureId };
        var migratedAuthorities = baseAuthorities.CanonicalAuthorities
            .Where(authority => !replaced.Contains(authority.PartitionId.Value))
            .Concat<IDomainPartitionSnapshotAuthorityV1>([scopeAuthority, voidAuthority, infrastructureAuthority])
            .ToArray();
        var exact97 = new DomainPartitionSnapshotAuthoritySetV1(migratedWorld, migratedAuthorities);
        Require(exact97.CanonicalAuthorities.Count == StandardDomainPartitionRegistry.StandardPartitionCount,
            "Infrastructure v2 canary must coexist with the other authorities in exact-97 production material.");
        Require(exact97.Get(infrastructureId).RecordSchema == InfrastructureNetworkTopologyRecordSchemaV2.RecordSchema,
            "Exact-97 authority set must preserve Infrastructure record schema v2.");

        var providers = StandardDomainSnapshotOwnerCompositionV1.CreateAllProviders();
        var sections = DomainPartitionSnapshotProductionProviderV1.CreateAll97(exact97, providers);
        Require(sections.Count == StandardDomainPartitionRegistry.StandardPartitionCount,
            "Infrastructure migration-aware production serialization must emit exactly 97 Domain sections.");
        var infrastructureSection = sections.Single(section => section.SectionId == infrastructureId);
        Require(infrastructureSection.LogicalItemCount == 4 &&
                infrastructureSection.LogicalContentDigest.SequenceEqual(infrastructureAuthority.Header.CanonicalDigest),
            "Infrastructure v2 production section must remain bound to the frozen migrated authority.");

        var recoveredInfrastructure = new InfrastructureNetworkTopologyRecoveredReferenceSourceV2(infrastructureSection.Fragments);
        Require(recoveredInfrastructure.RecordSchema == InfrastructureNetworkTopologyRecordSchemaV2.RecordSchema &&
                recoveredInfrastructure.TryGetRecordKind(networkId, out var networkKind) &&
                networkKind == InfrastructureNetworkTopologyRecordSchemaV2.NetworkKind &&
                recoveredInfrastructure.TryGetRecordKind(nodeAId, out var nodeKind) &&
                nodeKind == InfrastructureNetworkTopologyRecordSchemaV2.NodeKind &&
                recoveredInfrastructure.TryGetRecordKind(edgeId, out var edgeKind) &&
                edgeKind == InfrastructureNetworkTopologyRecordSchemaV2.EdgeKind,
            "Infrastructure v2 recovered source must preserve schema and heterogeneous target kinds.");

        var recoveredAll97 = DomainSnapshotReferenceResolverV1.FromRecoveredSections(sections);
        Require(recoveredAll97.TryGetRecordSchema(networkRef, out var recoveredSchema) &&
                recoveredSchema == InfrastructureNetworkTopologyRecordSchemaV2.RecordSchema,
            "Recovery phase 1 must preserve Infrastructure v2 through the complete 97-section set.");

        var standardProvider = providers.Single(provider => provider.SectionId == infrastructureId);
        var resolvedProvider = DomainSnapshotRecordSchemaMigrationProviderRegistryV1.Resolve(
            infrastructureAuthority,
            standardProvider);
        Require(resolvedProvider is InfrastructureNetworkTopologySnapshotSectionProviderV2,
            "Actual Infrastructure v2 authority must select the registered v2 production provider.");
        var verifier = resolvedProvider.CreateSemanticVerifier(infrastructureAuthority.Header);
        var semantic = verifier.VerifyWithContext?.Invoke(
            infrastructureSection.Fragments,
            new SnapshotSectionSemanticVerificationContextV1(recoveredAll97))
            ?? throw new InvalidOperationException("Infrastructure v2 verifier must support recovered all-97 reference context.");
        Require(semantic.LogicalItemCount == infrastructureSection.LogicalItemCount &&
                semantic.LogicalContentDigest.SequenceEqual(infrastructureSection.LogicalContentDigest),
            "Recovery phase 2 must reconstruct and rehash Infrastructure v2 with target-kind closure intact.");

        Require(StandardDomainPartitionRegistry.Get(infrastructureId).RecordSchema.Version == new SchemaVersionV1(1, 0),
            "Infrastructure v2 canary must not mutate the global standard registry from v1.");
    }

    private static InfrastructureNetworkTopologyRecordMaterialV2 Material(
        OpaqueId128 recordId,
        InfrastructureNetworkTopologyRecordPayloadV2 payload)
        => new(recordId, 1, 0, null, DetailLevelV1.D2RegionalAggregate, null, payload);

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

    private static OpaqueId128 Id(string value) => OpaqueId128.Parse(value);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
