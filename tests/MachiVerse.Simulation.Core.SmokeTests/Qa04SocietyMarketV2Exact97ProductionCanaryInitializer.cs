using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04SocietyMarketV2Exact97ProductionCanaryInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var resident = Qa04ReferenceWorldMaterializerV1.MaterializeResidentIdentityLifecycle(1);
        var baseAuthorities = Qa04ReducedWorldTypedAuthorityV1.BindAll97(resident);

        const string scopeId = SpatialScopeRegistryPayloadV1.PartitionId;
        const string voidId = SpatialVoidGeometryPayloadV1.PartitionId;
        const string marketId = SocietyMarketTransactionRecordSchemaV2.PartitionId;
        var scopeRecordId = Id("00000000000000000000000000045401");
        var voidRecordId = Id("00000000000000000000000000045402");
        var scopeRef = new PartitionRecordRefV1(scopeId, scopeRecordId);
        var voidRef = new PartitionRecordRefV1(voidId, voidRecordId);

        var scopeAuthority = CreateScopeAuthority(resident, scopeRecordId, voidRef);
        var voidAuthority = CreateVoidAuthority(resident, voidRecordId, voidRef);

        var marketState = Qa04MarketMaterializerV1.CreateMarketState(
            0,
            _ => scopeRef,
            out _);
        var marketOrder = Qa04MarketMaterializerV1.CreateOrder(0, out _);
        var marketPrior = resident.WorldState.Partitions.Get(marketId).Header;
        var marketAuthority = SocietyMarketTransactionSnapshotAuthorityV2.CreateCanonical(
            new SocietyMarketTransactionPartitionStateV2([marketState, marketOrder]),
            marketPrior.Revision,
            marketPrior.BasisStep,
            marketPrior.DetailLevel);

        var replacementHeaders = new Dictionary<string, PartitionStateHeaderV1>(StringComparer.Ordinal)
        {
            [scopeId] = scopeAuthority.Header,
            [voidId] = voidAuthority.Header,
            [marketId] = marketAuthority.Header,
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

        var replaced = new HashSet<string>(StringComparer.Ordinal) { scopeId, voidId, marketId };
        var migratedAuthorities = baseAuthorities.CanonicalAuthorities
            .Where(authority => !replaced.Contains(authority.PartitionId.Value))
            .Concat<IDomainPartitionSnapshotAuthorityV1>([scopeAuthority, voidAuthority, marketAuthority])
            .ToArray();
        var exact97 = new DomainPartitionSnapshotAuthoritySetV1(migratedWorld, migratedAuthorities);
        Require(exact97.CanonicalAuthorities.Count == StandardDomainPartitionRegistry.StandardPartitionCount,
            "Market v2 canary must coexist with the other authorities in exact-97 production material.");
        Require(exact97.Get(marketId).RecordSchema == SocietyMarketTransactionRecordSchemaV2.RecordSchema,
            "Exact-97 authority set must preserve Market record schema v2.");

        var providers = StandardDomainSnapshotOwnerCompositionV1.CreateAllProviders();
        var sections = DomainPartitionSnapshotProductionProviderV1.CreateAll97(exact97, providers);
        Require(sections.Count == StandardDomainPartitionRegistry.StandardPartitionCount,
            "Market migration-aware production serialization must emit exactly 97 Domain sections.");
        var marketSection = sections.Single(section => section.SectionId == marketId);
        Require(marketSection.LogicalItemCount == 2 &&
                marketSection.LogicalContentDigest.SequenceEqual(marketAuthority.Header.CanonicalDigest),
            "Market v2 production section must remain bound to the frozen migrated authority.");

        var recoveredMarket = new SocietyMarketTransactionRecoveredReferenceSourceV2(marketSection.Fragments);
        Require(recoveredMarket.RecordSchema == SocietyMarketTransactionRecordSchemaV2.RecordSchema &&
                recoveredMarket.IsMarketState(marketState.RecordId) &&
                !recoveredMarket.IsMarketState(marketOrder.RecordId),
            "Market v2 recovered source must preserve schema and heterogeneous target kinds.");

        var recoveredAll97 = DomainSnapshotReferenceResolverV1.FromRecoveredSections(sections);
        Require(recoveredAll97.TryGetRecordSchema(
                    new PartitionRecordRefV1(marketId, marketState.RecordId),
                    out var recoveredSchema) &&
                recoveredSchema == SocietyMarketTransactionRecordSchemaV2.RecordSchema,
            "Recovery phase 1 must preserve Market v2 through the complete 97-section set.");

        var standardProvider = providers.Single(provider => provider.SectionId == marketId);
        var resolvedProvider = DomainSnapshotRecordSchemaMigrationProviderRegistryV1.Resolve(
            marketAuthority,
            standardProvider);
        Require(resolvedProvider is SocietyMarketTransactionSnapshotSectionProviderV2,
            "Actual Market v2 authority must select the registered v2 production provider.");
        var verifier = resolvedProvider.CreateSemanticVerifier(marketAuthority.Header);
        var semantic = verifier.VerifyWithContext?.Invoke(
            marketSection.Fragments,
            new SnapshotSectionSemanticVerificationContextV1(recoveredAll97))
            ?? throw new InvalidOperationException("Market v2 verifier must support recovered all-97 reference context.");
        Require(semantic.LogicalItemCount == marketSection.LogicalItemCount &&
                semantic.LogicalContentDigest.SequenceEqual(marketSection.LogicalContentDigest),
            "Recovery phase 2 must reconstruct and rehash Market v2 with target-kind closure intact.");

        Require(StandardDomainPartitionRegistry.Get(marketId).RecordSchema.Version == new SchemaVersionV1(1, 0),
            "Market v2 canary must not mutate the global standard registry from v1.");
    }

    private static DomainPartitionSnapshotAuthorityV1<SpatialScopeRegistryPayloadV1> CreateScopeAuthority(
        Qa04ResidentIdentityLifecycleMaterializationV1 resident,
        OpaqueId128 recordId,
        PartitionRecordRefV1 geometryRef)
    {
        var identity = StandardDomainPartitionRegistry.Get(SpatialScopeRegistryPayloadV1.PartitionId);
        var prior = resident.WorldState.Partitions.Get(identity.PartitionId.Value).Header;
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
        Qa04ResidentIdentityLifecycleMaterializationV1 resident,
        OpaqueId128 recordId,
        PartitionRecordRefV1 selfRef)
    {
        var identity = StandardDomainPartitionRegistry.Get(SpatialVoidGeometryPayloadV1.PartitionId);
        var prior = resident.WorldState.Partitions.Get(identity.PartitionId.Value).Header;
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
