using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Environment;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Domains.Participation;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed class Qa04ProductionReferenceWorldAssemblyV1
{
    internal Qa04ProductionReferenceWorldAssemblyV1(
        WorldStateV1 partitionAuthorityState,
        Qa04CanonicalOperationMutationStateV1 mutationState,
        IReadOnlyList<CrossDomainTransactionStateV1> activeTransactions,
        IReadOnlyList<PartitionStateHeaderV1> environmentD1Headers,
        IDomainRecordSchemaResolverV1 references,
        Qa04ProductionReferenceWorldStateValidationV1 validation,
        IReadOnlyList<IDomainPartitionSnapshotAuthorityV1> basisDomainAuthorities,
        IReadOnlyDictionary<string, IReadOnlyList<OpaqueId128>>? canonicalTransactionRecordPools = null,
        DetailDirectoryV1? initialDetailDirectory = null)
    {
        PartitionAuthorityState = partitionAuthorityState ?? throw new ArgumentNullException(nameof(partitionAuthorityState));
        MutationState = mutationState ?? throw new ArgumentNullException(nameof(mutationState));
        ActiveTransactions = activeTransactions ?? throw new ArgumentNullException(nameof(activeTransactions));
        EnvironmentD1Headers = environmentD1Headers ?? throw new ArgumentNullException(nameof(environmentD1Headers));
        References = references ?? throw new ArgumentNullException(nameof(references));
        Validation = validation ?? throw new ArgumentNullException(nameof(validation));
        BasisDomainAuthorities = basisDomainAuthorities ?? throw new ArgumentNullException(nameof(basisDomainAuthorities));
        CanonicalTransactionRecordPools =
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, IReadOnlyList<OpaqueId128>>(
                (canonicalTransactionRecordPools ?? new Dictionary<string, IReadOnlyList<OpaqueId128>>(StringComparer.Ordinal))
                .ToDictionary(
                    static pair => pair.Key,
                    static pair => (IReadOnlyList<OpaqueId128>)Array.AsReadOnly(pair.Value.ToArray()),
                    StringComparer.Ordinal));
        InitialDetailDirectory = initialDetailDirectory;
        if (BasisDomainAuthorities.Count != StandardDomainPartitionRegistry.StandardPartitionCount)
            throw new InvalidDataException("qa04.production-reference-world.snapshot-authority-count-not-97");
    }

    public WorldStateV1 PartitionAuthorityState { get; }
    public Qa04CanonicalOperationMutationStateV1 MutationState { get; }
    public IReadOnlyList<CrossDomainTransactionStateV1> ActiveTransactions { get; }
    public IReadOnlyList<PartitionStateHeaderV1> EnvironmentD1Headers { get; }
    public IDomainRecordSchemaResolverV1 References { get; }
    public Qa04ProductionReferenceWorldStateValidationV1 Validation { get; }
    public IReadOnlyList<IDomainPartitionSnapshotAuthorityV1> BasisDomainAuthorities { get; }
    internal IReadOnlyDictionary<string, IReadOnlyList<OpaqueId128>> CanonicalTransactionRecordPools { get; }
    internal DetailDirectoryV1? InitialDetailDirectory { get; }
}

/// <summary>
/// Gate-2 Step-13 production reference-world assembler. Every non-empty base partition header is
/// derived from its actual canonical production materializer. Environment D1 remains separate detail
/// authority over the same thirteen partition identities. The returned six mutation targets are the
/// same full typed states represented by State(S), not touched-record-only fixtures.
/// </summary>
public static class Qa04ProductionReferenceWorldAssemblerV1
{
    public const ulong CanonicalBasisStep = 1;

    public static Qa04ProductionReferenceWorldAssemblyV1 AssembleCanonical(ulong basisStep = CanonicalBasisStep)
    {
        if (basisStep != CanonicalBasisStep)
            throw new ArgumentOutOfRangeException(nameof(basisStep), "perf.reference.v1 production closure is fixed at State(S)=1.");

        Qa04ReferenceWorldDependencyContractV1.ValidateCanonicalContract();
        Qa04ReferenceWorldMaterialContractV1.RequireAllProductionMaterializersAvailable();

        var resident = Qa04ReferenceWorldMaterializerV1.MaterializeCanonicalResidentIdentityLifecycle();
        var snapshotAuthorities = Qa04ProductionDomainSnapshotAuthorityBuilderV1.CreateInitial(resident);
        var headers = resident.WorldState.Partitions.CanonicalEntries.ToDictionary(
            static entry => entry.Header.PartitionId.Value,
            static entry => entry.Header,
            StringComparer.Ordinal);

        var scopes = Qa04SpatialTileScopeAuthorityV1.MaterializeCanonical();
        var scopeResolver = new ExactReferenceResolver();
        foreach (var record in scopes.RecordsCanonical)
            scopeResolver.Add(new PartitionRecordRefV1(SpatialScopeRegistryPayloadV1.PartitionId, record.RecordId), record.RecordSchema);
        Replace(headers, Header(scopes, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));

        var participation = Qa04ParticipationControlModeCanonicalAuthorityV1.MaterializeCanonical();
        Replace(headers, Header(participation.Partition, DetailLevelV1.D0Entity, static payload => payload.CanonicalDigest()));

        var physical = Qa04PhysicalD0FullReferenceWorldCanonicalAuthorityV1.MaterializeCanonical();
        Replace(headers, physical.TileFrameHeader);
        Replace(headers, physical.PresenceHeader);
        Replace(headers, physical.OccupancyHeader);

        var detailRegions = Qa04DetailRegionCanonicalAuthorityV1.MaterializeCanonical();
        Replace(headers, Header(detailRegions.Partition, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));

        var terrainTemplate = headers[SpatialTerrainGeometryRecordSchemaV2.PartitionId];
        var terrain = Qa04TerrainCanonicalStreamingSnapshotAuthorityV1.Create(terrainTemplate);
        Replace(headers, terrain.Header);

        var environmentD0 = Qa04EnvironmentCanonicalD0FullEvidenceBuilderV1.Build();
        foreach (var header in environmentD0.PartitionHeaders) Replace(headers, header);
        var environmentReferences = new Qa04EnvironmentCanonicalD0ReferenceResolverV1();
        environmentReferences.ValidateFullD0Coverage();

        var environmentD1 = Qa04EnvironmentCanonicalD1FullEvidenceBuilderV1.Build();
        var environmentD1Headers = Array.AsReadOnly(environmentD1.PartitionHeaders.ToArray());

        var facility = Qa04FacilityServiceCanonicalAuthorityV1.MaterializeCanonical();
        Replace(headers, Header(facility.BuiltStructures, DetailLevelV1.D0Entity, static payload => payload.CanonicalDigest()));
        Replace(headers, Header(facility.FacilityServices, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));
        var service = facility.ServiceAuthority;
        Replace(headers, Header(service.TransportServices, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));
        Replace(headers, Header(service.WaterServices, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));
        Replace(headers, Header(service.PowerServices, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));
        Replace(headers, Header(service.CommunicationServices, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));
        Replace(headers, Header(service.ServiceQueue, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));

        var topologyRecords = Qa04InfrastructureNetworkMaterializerV1
            .MaterializeCanonicalTopology(Qa04SpatialTileScopeAuthorityV1.ScopeRef)
            .ToArray();
        var topologyState = new InfrastructureNetworkTopologyPartitionStateV2(topologyRecords);
        var topologyReferences = new ExactReferenceResolver(scopeResolver);
        foreach (var organization in Qa04SocietyOrganizationResolvedMaterializerV1.MaterializeResolved(
                     static _ => Qa04SocietyGovernanceCanonicalAuthorityV1.OrganizationClass))
        {
            topologyReferences.Add(
                new PartitionRecordRefV1(SocietyOrganizationPayloadV1.PartitionId, organization.RecordId),
                organization.RecordSchema);
        }
        foreach (var record in topologyState.RecordSet.RecordsCanonical)
            topologyReferences.Add(
                new PartitionRecordRefV1(InfrastructureNetworkTopologyRecordSchemaV2.PartitionId, record.RecordId),
                record.RecordSchema);
        Replace(headers, PartitionStateHeaderV1.CreateCanonical(
            topologyState.State,
            revision: 1,
            basisStep: 0,
            detailLevel: DetailLevelV1.D2RegionalAggregate,
            payload => InfrastructureNetworkTopologyPayloadCanonicalDigestV2.Compute(payload, topologyReferences)));

        var infrastructureDependency = Qa04InfrastructureDependencyCanonicalAuthorityV1.MaterializeCanonical();
        Replace(headers, Header(infrastructureDependency.Partition, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));

        var informationDelivery = Qa04InformationDeliveryCanonicalAuthorityV1.MaterializeCanonical();
        Replace(headers, Header(informationDelivery.Partition, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));

        var remainingInformation = Qa04RemainingInformationCanonicalAuthorityV1.MaterializeCanonical();
        Replace(headers, Header(remainingInformation.MediaDistribution, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));
        Replace(headers, Header(remainingInformation.RecordStore, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));

        var infrastructureTail = Qa04InfrastructureTailCanonicalAuthorityV1.MaterializeCanonical(facility);
        Replace(headers, Header(infrastructureTail.AddressPlaceIndexes, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));
        Replace(headers, Header(infrastructureTail.FailureRecoveries, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));
        Replace(headers, Header(infrastructureTail.Lineages, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));

        var society = BuildSocietyGovernance(headers, facility, participation.References, scopeResolver);

        var initialDetailDirectory = new DetailDirectoryV1(
            detailRegions.RegionsByTile,
            Array.Empty<DetailTransitionCandidateV1>());
        var partitionState = BuildWorldState(
            resident.WorldState,
            headers,
            initialDetailDirectory,
            basisStep);

        snapshotAuthorities.Replace(scopes, headers[SpatialScopeRegistryPayloadV1.PartitionId], static payload => payload.CanonicalDigest());
        snapshotAuthorities.Replace(participation.Partition, headers[ParticipationControlModePayloadV1.PartitionId], static payload => payload.CanonicalDigest());
        snapshotAuthorities.Replace(physical.TileFrames, physical.TileFrameHeader, static payload => payload.CanonicalDigest());
        snapshotAuthorities.Replace(physical.Presences, physical.PresenceHeader, static payload => payload.CanonicalDigest());
        snapshotAuthorities.Replace(new PhysicalOccupancySnapshotAuthorityV2(physical.Occupancy, physical.OccupancyHeader));
        snapshotAuthorities.Replace(detailRegions.Partition, headers[detailRegions.Partition.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
        snapshotAuthorities.Replace(terrain);
        foreach (var authority in environmentD0.Materialization.State.BindSnapshotMaterial(partitionState).Authorities)
            snapshotAuthorities.Replace(authority);
        snapshotAuthorities.Replace(facility.BuiltStructures, headers[facility.BuiltStructures.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
        snapshotAuthorities.Replace(facility.FacilityServices, headers[facility.FacilityServices.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
        snapshotAuthorities.Replace(service.TransportServices, headers[service.TransportServices.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
        snapshotAuthorities.Replace(service.WaterServices, headers[service.WaterServices.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
        snapshotAuthorities.Replace(service.PowerServices, headers[service.PowerServices.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
        snapshotAuthorities.Replace(service.CommunicationServices, headers[service.CommunicationServices.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
        snapshotAuthorities.Replace(service.ServiceQueue, headers[InfrastructureServiceQueuePayloadV1.PartitionId], static payload => payload.CanonicalDigest());
        snapshotAuthorities.Replace(new InfrastructureNetworkTopologySnapshotAuthorityV2(
            topologyState,
            headers[InfrastructureNetworkTopologyRecordSchemaV2.PartitionId]));
        snapshotAuthorities.Replace(infrastructureDependency.Partition, headers[infrastructureDependency.Partition.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
        snapshotAuthorities.Replace(informationDelivery.Partition, headers[informationDelivery.Partition.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
        snapshotAuthorities.Replace(remainingInformation.MediaDistribution, headers[remainingInformation.MediaDistribution.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
        snapshotAuthorities.Replace(remainingInformation.RecordStore, headers[remainingInformation.RecordStore.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
        snapshotAuthorities.Replace(infrastructureTail.AddressPlaceIndexes, headers[infrastructureTail.AddressPlaceIndexes.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
        snapshotAuthorities.Replace(infrastructureTail.FailureRecoveries, headers[infrastructureTail.FailureRecoveries.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
        snapshotAuthorities.Replace(infrastructureTail.Lineages, headers[infrastructureTail.Lineages.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
        society.ApplySnapshotAuthorities(snapshotAuthorities);
        var basisDomainAuthorities = snapshotAuthorities.Build(partitionState).CanonicalAuthorities;

        var mutationReferences = new CompositeReferenceResolver(
            service.References,
            facility.References,
            participation.References,
            physical.References,
            society.GovernanceReferences,
            environmentReferences,
            society.MarketReferences);

        foreach (var serviceRef in facility.CanonicalServicePool)
        {
            if (!mutationReferences.Exists(serviceRef))
                throw new InvalidDataException(
                    $"qa04.production-reference-world.canonical-service-pool-reference-missing:{serviceRef.PartitionId.Value}");
        }

        var mutationState = new Qa04CanonicalOperationMutationStateV1(
            service.ServiceQueue,
            participation.Partition,
            Empty<ResidentBehaviorStatePayloadV1>(ResidentBehaviorStatePayloadV1.PartitionId),
            physical.Presences,
            society.MarketState,
            society.SecurityIncidents,
            environmentD0.Materialization.State.Hazard);

        var pools = new Dictionary<string, IReadOnlyList<OpaqueId128>>(StringComparer.Ordinal)
        {
            [SpatialScopeRegistryPayloadV1.PartitionId] = RecordIds(scopes),
            [EnvironmentHazardPayloadV1.PartitionId] = RecordIds(environmentD0.Materialization.State.Hazard),
            [PhysicalPresencePayloadV1.PartitionId] = RecordIds(physical.Presences),
            [ParticipationControlModePayloadV1.PartitionId] = RecordIds(participation.Partition),
            [ResidentIdentityLifecyclePayloadV1.PartitionId] = RecordIds(resident.Partition),
            [SocietyContractClaimPayloadV1.PartitionId] = society.ContractClaimPool,
            [GovernancePermissionLicensePayloadV1.PartitionId] = society.PermissionLicensePool,
            [InfrastructureServiceQueuePayloadV1.PartitionId] = RecordIds(service.ServiceQueue),
        };
        var transactionBindings = Qa04CrossDomainTransactionGenesisMaterializerV1.Materialize(pools);
        var activeTransactions = Array.AsReadOnly(transactionBindings.Select(static binding => binding.State).ToArray());

        var validation = Qa04ProductionReferenceWorldStateContractV1.Validate(
            partitionState,
            activeTransactions,
            environmentD1Headers);

        return new Qa04ProductionReferenceWorldAssemblyV1(
            partitionState,
            mutationState,
            activeTransactions,
            environmentD1Headers,
            mutationReferences,
            validation,
            basisDomainAuthorities,
            pools,
            initialDetailDirectory);
    }

    private static SocietyGovernanceBuildResult BuildSocietyGovernance(
        IDictionary<string, PartitionStateHeaderV1> headers,
        Qa04FacilityServiceCanonicalMaterializationV1 facility,
        IDomainRecordSchemaResolverV1 residentReferences,
        IDomainRecordSchemaResolverV1 scopeReferences)
    {
        var canonical = Qa04SocietyGovernanceCanonicalMaterializerV1.MaterializeCanonical();
        Replace(headers, Header(canonical.Organizations, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));
        Replace(headers, Header(canonical.Institutions, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));
        Replace(headers, Header(canonical.ContractClaims, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));
        Replace(headers, Header(canonical.InformationClaims, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));
        Replace(headers, Header(canonical.PublicAuthorities, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));
        Replace(headers, Header(canonical.PermissionLicenses, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));
        var contractPool = RecordIds(canonical.ContractClaims);
        var permissionPool = RecordIds(canonical.PermissionLicenses);

        var memberships = Qa04SocietyMembershipRoleCanonicalAuthorityV1.MaterializeCanonical();
        Replace(headers, Header(memberships.MembershipRoles, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));

        var employments = Qa04SocietyEmploymentCanonicalAuthorityV1.MaterializeCanonical();
        Replace(headers, Header(employments.Employments, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));

        var households = Qa04SocietyHouseholdMaterializerV1.MaterializeCanonicalPartition();
        Replace(headers, Header(households, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));

        var propertyRights = Qa04SocietyPropertyRightCanonicalAuthorityV1.MaterializeCanonical();
        Replace(headers, Header(propertyRights.PropertyRights, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));

        var currencies = Qa04SocietyCurrencyMoneyCanonicalAuthorityV1.MaterializeCanonical();
        Replace(headers, Header(currencies.Currencies, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));

        var accounts = Qa04SocietyFinanceAccountCanonicalAuthorityV1.MaterializeCanonical();
        Replace(headers, Header(accounts.Accounts, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));

        var marketRecords = Qa04MarketMaterializerV1.MaterializeCanonical(Qa04SpatialTileScopeAuthorityV1.ScopeRef).ToArray();
        var marketState = new SocietyMarketTransactionPartitionStateV2(marketRecords);
        var marketLocalReferences = new ExactReferenceResolver();
        foreach (var record in marketState.RecordSet.RecordsCanonical)
        {
            if (record.Payload is SocietyMarketStatePayloadV2)
                marketLocalReferences.Add(
                    new PartitionRecordRefV1(SocietyMarketTransactionRecordSchemaV2.PartitionId, record.RecordId),
                    record.RecordSchema);
        }
        var marketReferences = new CompositeReferenceResolver(residentReferences, scopeReferences, marketLocalReferences);
        Replace(headers, PartitionStateHeaderV1.CreateCanonical(
            marketState.State,
            revision: 1,
            basisStep: 0,
            detailLevel: DetailLevelV1.D2RegionalAggregate,
            payload => SocietyMarketTransactionPayloadCanonicalDigestV2.Compute(payload, marketReferences)));

        var remainingSociety = Qa04SocietyRemainingCanonicalAuthorityV1.MaterializeCanonical(facility);
        Replace(headers, Header(remainingSociety.BusinessProductions, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));
        Replace(headers, Header(remainingSociety.LogisticsObligations, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));
        Replace(headers, Header(remainingSociety.HistoryLineages, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));

        var socialRelations = Qa04SocietySocialRelationsCanonicalAuthorityV1.MaterializeCanonical();
        Replace(headers, Header(socialRelations.Educations, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));
        Replace(headers, Header(socialRelations.Cultures, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));
        Replace(headers, Header(socialRelations.Reputations, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));

        var polities = Qa04GovernancePolityMaterializerV1.MaterializeCanonicalPartition();
        Replace(headers, Header(polities, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));

        var territorial = Qa04GovernanceTerritorialFoundationCanonicalAuthorityV1.MaterializeCanonical();
        Replace(headers, Header(territorial.Jurisdictions, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));
        Replace(headers, Header(territorial.TerritorialClaims, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));
        Replace(headers, Header(territorial.EffectiveControls, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));

        var governance = Qa04GovernanceRemainingCanonicalAuthorityV1.MaterializeCanonical();
        Replace(headers, Header(governance.LawRules, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));
        Replace(headers, Header(governance.TaxFiscal, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));
        Replace(headers, Header(governance.Diplomacy, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));
        Replace(headers, Header(governance.SecurityIncidents, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));
        Replace(headers, Header(governance.Investigations, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));
        Replace(headers, Header(governance.JudicialCases, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));
        Replace(headers, Header(governance.Enforcements, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));
        Replace(headers, Header(governance.MilitaryAuthorities, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));
        Replace(headers, Header(governance.BorderControls, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));
        Replace(headers, Header(governance.Lineages, DetailLevelV1.D2RegionalAggregate, static payload => payload.CanonicalDigest()));

        void ApplySnapshotAuthorities(Qa04ProductionDomainSnapshotAuthorityBuilderV1 builder)
        {
            builder.Replace(canonical.Organizations, headers[canonical.Organizations.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
            builder.Replace(canonical.Institutions, headers[canonical.Institutions.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
            builder.Replace(canonical.ContractClaims, headers[canonical.ContractClaims.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
            builder.Replace(canonical.InformationClaims, headers[canonical.InformationClaims.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
            builder.Replace(canonical.PublicAuthorities, headers[canonical.PublicAuthorities.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
            builder.Replace(canonical.PermissionLicenses, headers[canonical.PermissionLicenses.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
            builder.Replace(memberships.MembershipRoles, headers[memberships.MembershipRoles.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
            builder.Replace(employments.Employments, headers[employments.Employments.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
            builder.Replace(households, headers[households.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
            builder.Replace(propertyRights.PropertyRights, headers[propertyRights.PropertyRights.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
            builder.Replace(currencies.Currencies, headers[currencies.Currencies.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
            builder.Replace(accounts.Accounts, headers[accounts.Accounts.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
            builder.Replace(new SocietyMarketTransactionSnapshotAuthorityV2(
                marketState,
                headers[SocietyMarketTransactionRecordSchemaV2.PartitionId]));
            builder.Replace(remainingSociety.BusinessProductions, headers[remainingSociety.BusinessProductions.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
            builder.Replace(remainingSociety.LogisticsObligations, headers[remainingSociety.LogisticsObligations.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
            builder.Replace(remainingSociety.HistoryLineages, headers[remainingSociety.HistoryLineages.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
            builder.Replace(socialRelations.Educations, headers[socialRelations.Educations.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
            builder.Replace(socialRelations.Cultures, headers[socialRelations.Cultures.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
            builder.Replace(socialRelations.Reputations, headers[socialRelations.Reputations.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
            builder.Replace(polities, headers[polities.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
            builder.Replace(territorial.Jurisdictions, headers[territorial.Jurisdictions.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
            builder.Replace(territorial.TerritorialClaims, headers[territorial.TerritorialClaims.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
            builder.Replace(territorial.EffectiveControls, headers[territorial.EffectiveControls.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
            builder.Replace(governance.LawRules, headers[governance.LawRules.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
            builder.Replace(governance.TaxFiscal, headers[governance.TaxFiscal.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
            builder.Replace(governance.Diplomacy, headers[governance.Diplomacy.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
            builder.Replace(governance.SecurityIncidents, headers[governance.SecurityIncidents.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
            builder.Replace(governance.Investigations, headers[governance.Investigations.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
            builder.Replace(governance.JudicialCases, headers[governance.JudicialCases.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
            builder.Replace(governance.Enforcements, headers[governance.Enforcements.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
            builder.Replace(governance.MilitaryAuthorities, headers[governance.MilitaryAuthorities.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
            builder.Replace(governance.BorderControls, headers[governance.BorderControls.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
            builder.Replace(governance.Lineages, headers[governance.Lineages.Identity.PartitionId.Value], static payload => payload.CanonicalDigest());
        }

        return new SocietyGovernanceBuildResult(
            marketState,
            marketReferences,
            governance.SecurityIncidents,
            governance.References,
            contractPool,
            permissionPool,
            ApplySnapshotAuthorities);
    }

    private static WorldStateV1 BuildWorldState(
        WorldStateV1 template,
        IReadOnlyDictionary<string, PartitionStateHeaderV1> headers,
        DetailDirectoryV1 detailDirectory,
        ulong basisStep)
    {
        if (headers.Count != StandardDomainPartitionRegistry.StandardPartitionCount)
            throw new InvalidDataException("qa04.production-reference-world.assembly-header-count");
        ArgumentNullException.ThrowIfNull(detailDirectory);

        var partitionRefs = StandardDomainPartitionRegistry.Entries
            .Select(identity => new PartitionStateRefV1(headers[identity.PartitionId.Value]))
            .ToArray();

        return new WorldStateV1(
            new WorldStateHeaderV1(
                template.Header.WorldId,
                basisStep,
                template.Header.WorldSeedDigest,
                template.Header.ConfigGeneration,
                template.Header.MasterGeneration,
                template.Header.RateGeneration),
            new OrderedPartitionDirectoryV1(partitionRefs),
            template.SchedulerState,
            template.OperationState,
            DetailDirectorySubstateV1.Canonicalize(detailDirectory),
            StandardDomainRegistryAuthorityV1.Generation1.ToWorldSubstateRef(),
            template.Diagnostic.ConfigDigest);
    }

    private static PartitionStateHeaderV1 Header<TPayload>(
        DomainPartitionStateV1<TPayload> partition,
        DetailLevelV1 detailLevel,
        Func<TPayload, byte[]> payloadDigest)
        => PartitionStateHeaderV1.CreateCanonical(
            partition,
            revision: 1,
            basisStep: 0,
            detailLevel,
            payloadDigest);

    private static void Replace(IDictionary<string, PartitionStateHeaderV1> headers, PartitionStateHeaderV1 header)
    {
        var id = header.PartitionId.Value;
        if (!headers.ContainsKey(id))
            throw new InvalidDataException($"qa04.production-reference-world.assembly-unknown-partition:{id}");
        if (header.CanonicalDigest.Length != 32 || header.CanonicalDigest.All(static value => value == 0))
            throw new InvalidDataException($"qa04.production-reference-world.assembly-zero-digest:{id}");
        headers[id] = header;
    }

    private static DomainPartitionStateV1<TPayload> Empty<TPayload>(string partitionId)
        => new(
            StandardDomainPartitionRegistry.Get(partitionId),
            Array.Empty<DomainRecordEnvelopeV1<TPayload>>());

    private static IReadOnlyList<OpaqueId128> RecordIds<TPayload>(DomainPartitionStateV1<TPayload> partition)
        => Array.AsReadOnly(partition.RecordsCanonical.Select(static record => record.RecordId).ToArray());

    private sealed record SocietyGovernanceBuildResult(
        SocietyMarketTransactionPartitionStateV2 MarketState,
        IDomainRecordSchemaResolverV1 MarketReferences,
        DomainPartitionStateV1<GovernanceSecurityIncidentPayloadV1> SecurityIncidents,
        IDomainRecordSchemaResolverV1 GovernanceReferences,
        IReadOnlyList<OpaqueId128> ContractClaimPool,
        IReadOnlyList<OpaqueId128> PermissionLicensePool,
        Action<Qa04ProductionDomainSnapshotAuthorityBuilderV1> ApplySnapshotAuthorities);

    private sealed class CompositeReferenceResolver : IDomainRecordSchemaResolverV1
    {
        private readonly IReadOnlyList<IDomainRecordSchemaResolverV1> _resolvers;

        public CompositeReferenceResolver(params IDomainRecordSchemaResolverV1[] resolvers)
        {
            if (resolvers is null || resolvers.Length == 0 || resolvers.Any(static resolver => resolver is null))
                throw new ArgumentException("At least one resolver is required.", nameof(resolvers));
            _resolvers = Array.AsReadOnly(resolvers.ToArray());
        }

        public bool Exists(PartitionRecordRefV1 reference)
            => _resolvers.Any(resolver => resolver.Exists(reference));

        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
        {
            foreach (var resolver in _resolvers)
            {
                if (resolver.TryGetRecordSchema(reference, out schema))
                    return true;
            }
            schema = default!;
            return false;
        }
    }

    private sealed class ExactReferenceResolver : IDomainRecordSchemaResolverV1
    {
        private readonly Dictionary<PartitionRecordRefV1, SchemaRefV1> _records = new();
        private readonly IReadOnlyList<IDomainRecordSchemaResolverV1> _fallbacks;

        public ExactReferenceResolver(params IDomainRecordSchemaResolverV1[] fallbacks)
        {
            _fallbacks = Array.AsReadOnly((fallbacks ?? Array.Empty<IDomainRecordSchemaResolverV1>()).ToArray());
        }

        public void Add(PartitionRecordRefV1 reference, SchemaRefV1 schema)
        {
            if (reference.RecordId.IsZero)
                throw new InvalidDataException("qa04.production-reference-world.assembly-reference-zero");
            if (!_records.TryAdd(reference, schema) && _records[reference] != schema)
                throw new InvalidDataException("qa04.production-reference-world.assembly-reference-schema-conflict");
        }

        public bool Exists(PartitionRecordRefV1 reference)
            => _records.ContainsKey(reference) || _fallbacks.Any(resolver => resolver.Exists(reference));

        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
        {
            if (_records.TryGetValue(reference, out schema)) return true;
            foreach (var fallback in _fallbacks)
            {
                if (fallback.TryGetRecordSchema(reference, out schema)) return true;
            }
            schema = default!;
            return false;
        }
    }
}
