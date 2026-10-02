using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains.InfrastructureInformation;

public sealed class InfrastructureInformationDomainStateV1
{
    public InfrastructureInformationDomainStateV1(
        DomainPartitionStateV1<InfrastructureNetworkTopologyPayloadV1> networkTopology,
        DomainPartitionStateV1<InfrastructureTransportServicePayloadV1> transportService,
        DomainPartitionStateV1<InfrastructureWaterServicePayloadV1> waterService,
        DomainPartitionStateV1<InfrastructurePowerServicePayloadV1> powerService,
        DomainPartitionStateV1<InfrastructureCommunicationServicePayloadV1> communicationService,
        DomainPartitionStateV1<InfrastructureDependencyPayloadV1> dependency,
        DomainPartitionStateV1<InfrastructureFacilityServicePayloadV1> facilityService,
        DomainPartitionStateV1<InfrastructureServiceQueuePayloadV1> serviceQueue,
        DomainPartitionStateV1<InformationDeliveryPayloadV1> delivery,
        DomainPartitionStateV1<InformationMediaDistributionPayloadV1> mediaDistribution,
        DomainPartitionStateV1<InformationRecordStorePayloadV1> recordStore,
        DomainPartitionStateV1<InformationAddressPlaceIndexPayloadV1> addressPlaceIndex,
        DomainPartitionStateV1<InfrastructureFailureRecoveryPayloadV1> failureRecovery,
        DomainPartitionStateV1<InfrastructureLineagePayloadV1> lineage)
    {
        NetworkTopology = RequireIdentity(networkTopology, InfrastructureNetworkTopologyPayloadV1.PartitionId);
        TransportService = RequireIdentity(transportService, InfrastructureTransportServicePayloadV1.PartitionId);
        WaterService = RequireIdentity(waterService, InfrastructureWaterServicePayloadV1.PartitionId);
        PowerService = RequireIdentity(powerService, InfrastructurePowerServicePayloadV1.PartitionId);
        CommunicationService = RequireIdentity(communicationService, InfrastructureCommunicationServicePayloadV1.PartitionId);
        Dependency = RequireIdentity(dependency, InfrastructureDependencyPayloadV1.PartitionId);
        FacilityService = RequireIdentity(facilityService, InfrastructureFacilityServicePayloadV1.PartitionId);
        ServiceQueue = RequireIdentity(serviceQueue, InfrastructureServiceQueuePayloadV1.PartitionId);
        Delivery = RequireIdentity(delivery, InformationDeliveryPayloadV1.PartitionId);
        MediaDistribution = RequireIdentity(mediaDistribution, InformationMediaDistributionPayloadV1.PartitionId);
        RecordStore = RequireIdentity(recordStore, InformationRecordStorePayloadV1.PartitionId);
        AddressPlaceIndex = RequireIdentity(addressPlaceIndex, InformationAddressPlaceIndexPayloadV1.PartitionId);
        FailureRecovery = RequireIdentity(failureRecovery, InfrastructureFailureRecoveryPayloadV1.PartitionId);
        Lineage = RequireIdentity(lineage, InfrastructureLineagePayloadV1.PartitionId);
    }

    public DomainPartitionStateV1<InfrastructureNetworkTopologyPayloadV1> NetworkTopology { get; }
    public DomainPartitionStateV1<InfrastructureTransportServicePayloadV1> TransportService { get; }
    public DomainPartitionStateV1<InfrastructureWaterServicePayloadV1> WaterService { get; }
    public DomainPartitionStateV1<InfrastructurePowerServicePayloadV1> PowerService { get; }
    public DomainPartitionStateV1<InfrastructureCommunicationServicePayloadV1> CommunicationService { get; }
    public DomainPartitionStateV1<InfrastructureDependencyPayloadV1> Dependency { get; }
    public DomainPartitionStateV1<InfrastructureFacilityServicePayloadV1> FacilityService { get; }
    public DomainPartitionStateV1<InfrastructureServiceQueuePayloadV1> ServiceQueue { get; }
    public DomainPartitionStateV1<InformationDeliveryPayloadV1> Delivery { get; }
    public DomainPartitionStateV1<InformationMediaDistributionPayloadV1> MediaDistribution { get; }
    public DomainPartitionStateV1<InformationRecordStorePayloadV1> RecordStore { get; }
    public DomainPartitionStateV1<InformationAddressPlaceIndexPayloadV1> AddressPlaceIndex { get; }
    public DomainPartitionStateV1<InfrastructureFailureRecoveryPayloadV1> FailureRecovery { get; }
    public DomainPartitionStateV1<InfrastructureLineagePayloadV1> Lineage { get; }

    public static InfrastructureInformationDomainStateV1 CreateEmpty()
        => new(
            Empty<InfrastructureNetworkTopologyPayloadV1>(InfrastructureNetworkTopologyPayloadV1.PartitionId),
            Empty<InfrastructureTransportServicePayloadV1>(InfrastructureTransportServicePayloadV1.PartitionId),
            Empty<InfrastructureWaterServicePayloadV1>(InfrastructureWaterServicePayloadV1.PartitionId),
            Empty<InfrastructurePowerServicePayloadV1>(InfrastructurePowerServicePayloadV1.PartitionId),
            Empty<InfrastructureCommunicationServicePayloadV1>(InfrastructureCommunicationServicePayloadV1.PartitionId),
            Empty<InfrastructureDependencyPayloadV1>(InfrastructureDependencyPayloadV1.PartitionId),
            Empty<InfrastructureFacilityServicePayloadV1>(InfrastructureFacilityServicePayloadV1.PartitionId),
            Empty<InfrastructureServiceQueuePayloadV1>(InfrastructureServiceQueuePayloadV1.PartitionId),
            Empty<InformationDeliveryPayloadV1>(InformationDeliveryPayloadV1.PartitionId),
            Empty<InformationMediaDistributionPayloadV1>(InformationMediaDistributionPayloadV1.PartitionId),
            Empty<InformationRecordStorePayloadV1>(InformationRecordStorePayloadV1.PartitionId),
            Empty<InformationAddressPlaceIndexPayloadV1>(InformationAddressPlaceIndexPayloadV1.PartitionId),
            Empty<InfrastructureFailureRecoveryPayloadV1>(InfrastructureFailureRecoveryPayloadV1.PartitionId),
            Empty<InfrastructureLineagePayloadV1>(InfrastructureLineagePayloadV1.PartitionId));

    public InfrastructureInformationDomainSnapshotMaterialV1 BindSnapshotMaterial(WorldStateV1 frozenState)
        => InfrastructureInformationDomainSnapshotMaterialV1.Bind(frozenState, this);

    private static DomainPartitionStateV1<TPayload> Empty<TPayload>(string partitionId)
        => new(StandardDomainPartitionRegistry.Get(partitionId), Array.Empty<DomainRecordEnvelopeV1<TPayload>>());

    private static DomainPartitionStateV1<TPayload> RequireIdentity<TPayload>(DomainPartitionStateV1<TPayload> partition, string partitionId)
    {
        ArgumentNullException.ThrowIfNull(partition);
        if (partition.Identity != StandardDomainPartitionRegistry.Get(partitionId))
            throw new InvalidDataException($"infrastructure.runtime-state.partition-identity:{partitionId}");
        return partition;
    }
}

public sealed class InfrastructureInformationDomainSnapshotMaterialV1
{
    private InfrastructureInformationDomainSnapshotMaterialV1(IEnumerable<IDomainPartitionSnapshotAuthorityV1> authorities)
    {
        var materialized = authorities?.ToArray() ?? throw new ArgumentNullException(nameof(authorities));
        if (materialized.Length != 14)
            throw new InvalidDataException("infrastructure.snapshot-material.authority-count");
        var byId = new Dictionary<string, IDomainPartitionSnapshotAuthorityV1>(StringComparer.Ordinal);
        foreach (var authority in materialized)
        {
            ArgumentNullException.ThrowIfNull(authority);
            authority.VerifyBoundAuthority();
            if (!string.Equals(authority.Identity.OwnerDomain.Value, "infrastructure_information", StringComparison.Ordinal))
                throw new InvalidDataException($"infrastructure.snapshot-material.foreign-owner:{authority.PartitionId.Value}");
            if (!byId.TryAdd(authority.PartitionId.Value, authority))
                throw new InvalidDataException($"infrastructure.snapshot-material.duplicate:{authority.PartitionId.Value}");
        }

        NetworkTopology = Require<InfrastructureNetworkTopologyPayloadV1>(byId, InfrastructureNetworkTopologyPayloadV1.PartitionId);
        TransportService = Require<InfrastructureTransportServicePayloadV1>(byId, InfrastructureTransportServicePayloadV1.PartitionId);
        WaterService = Require<InfrastructureWaterServicePayloadV1>(byId, InfrastructureWaterServicePayloadV1.PartitionId);
        PowerService = Require<InfrastructurePowerServicePayloadV1>(byId, InfrastructurePowerServicePayloadV1.PartitionId);
        CommunicationService = Require<InfrastructureCommunicationServicePayloadV1>(byId, InfrastructureCommunicationServicePayloadV1.PartitionId);
        Dependency = Require<InfrastructureDependencyPayloadV1>(byId, InfrastructureDependencyPayloadV1.PartitionId);
        FacilityService = Require<InfrastructureFacilityServicePayloadV1>(byId, InfrastructureFacilityServicePayloadV1.PartitionId);
        ServiceQueue = Require<InfrastructureServiceQueuePayloadV1>(byId, InfrastructureServiceQueuePayloadV1.PartitionId);
        Delivery = Require<InformationDeliveryPayloadV1>(byId, InformationDeliveryPayloadV1.PartitionId);
        MediaDistribution = Require<InformationMediaDistributionPayloadV1>(byId, InformationMediaDistributionPayloadV1.PartitionId);
        RecordStore = Require<InformationRecordStorePayloadV1>(byId, InformationRecordStorePayloadV1.PartitionId);
        AddressPlaceIndex = Require<InformationAddressPlaceIndexPayloadV1>(byId, InformationAddressPlaceIndexPayloadV1.PartitionId);
        FailureRecovery = Require<InfrastructureFailureRecoveryPayloadV1>(byId, InfrastructureFailureRecoveryPayloadV1.PartitionId);
        Lineage = Require<InfrastructureLineagePayloadV1>(byId, InfrastructureLineagePayloadV1.PartitionId);
        Authorities = Array.AsReadOnly(materialized.OrderBy(static value => value.PartitionId.Value, StringComparer.Ordinal).ToArray());
    }

    public DomainPartitionSnapshotAuthorityV1<InfrastructureNetworkTopologyPayloadV1> NetworkTopology { get; }
    public DomainPartitionSnapshotAuthorityV1<InfrastructureTransportServicePayloadV1> TransportService { get; }
    public DomainPartitionSnapshotAuthorityV1<InfrastructureWaterServicePayloadV1> WaterService { get; }
    public DomainPartitionSnapshotAuthorityV1<InfrastructurePowerServicePayloadV1> PowerService { get; }
    public DomainPartitionSnapshotAuthorityV1<InfrastructureCommunicationServicePayloadV1> CommunicationService { get; }
    public DomainPartitionSnapshotAuthorityV1<InfrastructureDependencyPayloadV1> Dependency { get; }
    public DomainPartitionSnapshotAuthorityV1<InfrastructureFacilityServicePayloadV1> FacilityService { get; }
    public DomainPartitionSnapshotAuthorityV1<InfrastructureServiceQueuePayloadV1> ServiceQueue { get; }
    public DomainPartitionSnapshotAuthorityV1<InformationDeliveryPayloadV1> Delivery { get; }
    public DomainPartitionSnapshotAuthorityV1<InformationMediaDistributionPayloadV1> MediaDistribution { get; }
    public DomainPartitionSnapshotAuthorityV1<InformationRecordStorePayloadV1> RecordStore { get; }
    public DomainPartitionSnapshotAuthorityV1<InformationAddressPlaceIndexPayloadV1> AddressPlaceIndex { get; }
    public DomainPartitionSnapshotAuthorityV1<InfrastructureFailureRecoveryPayloadV1> FailureRecovery { get; }
    public DomainPartitionSnapshotAuthorityV1<InfrastructureLineagePayloadV1> Lineage { get; }
    public IReadOnlyList<IDomainPartitionSnapshotAuthorityV1> Authorities { get; }

    public static InfrastructureInformationDomainSnapshotMaterialV1 Bind(WorldStateV1 frozenState, InfrastructureInformationDomainStateV1 state)
    {
        ArgumentNullException.ThrowIfNull(frozenState);
        ArgumentNullException.ThrowIfNull(state);
        return new InfrastructureInformationDomainSnapshotMaterialV1(
        [
            Bind(frozenState, state.NetworkTopology, InfrastructureNetworkTopologyPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.TransportService, InfrastructureTransportServicePayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.WaterService, InfrastructureWaterServicePayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.PowerService, InfrastructurePowerServicePayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.CommunicationService, InfrastructureCommunicationServicePayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.Dependency, InfrastructureDependencyPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.FacilityService, InfrastructureFacilityServicePayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.ServiceQueue, InfrastructureServiceQueuePayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.Delivery, InformationDeliveryPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.MediaDistribution, InformationMediaDistributionPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.RecordStore, InformationRecordStorePayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.AddressPlaceIndex, InformationAddressPlaceIndexPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.FailureRecovery, InfrastructureFailureRecoveryPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.Lineage, InfrastructureLineagePayloadV1.PartitionId, static value => value.CanonicalDigest()),
        ]);
    }

    private static DomainPartitionSnapshotAuthorityV1<TPayload> Bind<TPayload>(WorldStateV1 frozenState, DomainPartitionStateV1<TPayload> partition, string partitionId, Func<TPayload, byte[]> digest)
    {
        if (partition.Identity != StandardDomainPartitionRegistry.Get(partitionId))
            throw new InvalidDataException($"infrastructure.snapshot-material.partition-identity:{partitionId}");
        return new DomainPartitionSnapshotAuthorityV1<TPayload>(partition, frozenState.Partitions.Get(partitionId).Header, digest);
    }

    private static DomainPartitionSnapshotAuthorityV1<TPayload> Require<TPayload>(IReadOnlyDictionary<string, IDomainPartitionSnapshotAuthorityV1> byId, string partitionId)
    {
        if (!byId.TryGetValue(partitionId, out var authority))
            throw new InvalidDataException($"infrastructure.snapshot-material.missing:{partitionId}");
        return authority as DomainPartitionSnapshotAuthorityV1<TPayload>
            ?? throw new InvalidDataException($"infrastructure.snapshot-material.payload-type:{partitionId}");
    }
}
