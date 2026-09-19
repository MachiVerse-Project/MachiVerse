using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed class Qa04InfrastructureServiceQueueCanonicalMaterializationV1
{
    internal Qa04InfrastructureServiceQueueCanonicalMaterializationV1(
        DomainPartitionStateV1<InfrastructureTransportServicePayloadV1> transportServices,
        DomainPartitionStateV1<InfrastructureWaterServicePayloadV1> waterServices,
        DomainPartitionStateV1<InfrastructurePowerServicePayloadV1> powerServices,
        DomainPartitionStateV1<InfrastructureCommunicationServicePayloadV1> communicationServices,
        DomainPartitionStateV1<InfrastructureServiceQueuePayloadV1> serviceQueue,
        IDomainRecordSchemaResolverV1 references)
    {
        TransportServices = transportServices;
        WaterServices = waterServices;
        PowerServices = powerServices;
        CommunicationServices = communicationServices;
        ServiceQueue = serviceQueue;
        References = references;
    }

    public DomainPartitionStateV1<InfrastructureTransportServicePayloadV1> TransportServices { get; }
    public DomainPartitionStateV1<InfrastructureWaterServicePayloadV1> WaterServices { get; }
    public DomainPartitionStateV1<InfrastructurePowerServicePayloadV1> PowerServices { get; }
    public DomainPartitionStateV1<InfrastructureCommunicationServicePayloadV1> CommunicationServices { get; }
    public DomainPartitionStateV1<InfrastructureServiceQueuePayloadV1> ServiceQueue { get; }
    public IDomainRecordSchemaResolverV1 References { get; }

    public ulong MaterializedRecordCount => checked(
        TransportServices.ItemCount +
        WaterServices.ItemCount +
        PowerServices.ItemCount +
        CommunicationServices.ItemCount +
        ServiceQueue.ItemCount);
}

/// <summary>
/// Production-path materialization for the benchmark-only network-service / ServiceQueue authority
/// fixed by phase4-alpha11-infrastructure-service-queue-authority.md.
///
/// All service targets are actual infrastructure.network_topology /2.0 records produced by the
/// accepted topology materializer. TileScope and Resident targets are likewise seeded from their
/// production materializers. Snapshot/recovery evidence reuses the same resolver returned here.
/// </summary>
public static class Qa04InfrastructureServiceQueueCanonicalMaterializerV1
{
    public const ulong TransportServiceCount = 10_000;
    public const ulong WaterServiceCount = 10_000;
    public const ulong PowerServiceCount = 10_000;
    public const ulong CommunicationServiceCount = 10_000;
    public const ulong CanonicalServicePoolCount = 40_000;
    public const ulong ServiceQueueCount = 250_000;
    public const ulong CanonicalMaterializedCount = 290_000;
    public const ulong RequiredResidentAuthorityCount = 250_000;

    private static readonly StableToken ResidentReferenceClass = new("resident.persistent-identity");
    private static readonly StableToken TransportServiceKind = new("perf.transport-service");
    private static readonly StableToken Active = new("active");
    private static readonly StableToken Queued = new("queued");

    public static void ValidateCanonicalContract()
    {
        Qa04InfrastructureReferenceDecompositionV1.ValidateCanonicalContract();
        Qa04InfrastructureNetworkMaterializerV1.ValidateCanonicalContract();
        Qa04ReferenceWorldMaterializerV1.ValidateCanonicalContract();
        Qa04SpatialTileScopeAuthorityV1.ValidateCanonicalContract();

        RequireSlice("transport_service", InfrastructureTransportServicePayloadV1.PartitionId, 120_100, TransportServiceCount, false);
        RequireSlice("water_service", InfrastructureWaterServicePayloadV1.PartitionId, 130_100, WaterServiceCount, false);
        RequireSlice("power_service", InfrastructurePowerServicePayloadV1.PartitionId, 140_100, PowerServiceCount, false);
        RequireSlice("communication_service", InfrastructureCommunicationServicePayloadV1.PartitionId, 150_100, CommunicationServiceCount, false);
        RequireSlice("service_queue", InfrastructureServiceQueuePayloadV1.PartitionId, 195_100, ServiceQueueCount, true);

        if (CanonicalServicePoolCount != checked(
                TransportServiceCount + WaterServiceCount + PowerServiceCount + CommunicationServiceCount) ||
            CanonicalMaterializedCount != checked(CanonicalServicePoolCount + ServiceQueueCount) ||
            RequiredResidentAuthorityCount != ServiceQueueCount ||
            RequiredResidentAuthorityCount > Qa04ReferenceWorldMaterializerV1.CanonicalResidentCount)
            throw new InvalidDataException("qa04.infrastructure.service-queue-canonical-count-drift");
    }

    public static Qa04InfrastructureServiceQueueCanonicalMaterializationV1 MaterializeCanonical()
    {
        ValidateCanonicalContract();

        var resolver = new CanonicalReferenceResolver();
        SeedTileScopeAuthority(resolver);
        var actualNetworks = SeedTopologyAuthority(resolver);
        var residentRefs = SeedResidentAuthority(resolver);
        var validator = new StandardDomainPayloadCodecValidatorV1();
        var servicePool = new PartitionRecordRefV1[CanonicalServicePoolCount];

        var transport = MaterializeTransportServices(resolver, validator, actualNetworks, servicePool);
        var water = MaterializeWaterServices(resolver, validator, actualNetworks, servicePool);
        var power = MaterializePowerServices(resolver, validator, actualNetworks, servicePool);
        var communication = MaterializeCommunicationServices(resolver, validator, actualNetworks, servicePool);

        if (servicePool.Any(static reference => reference.RecordId.IsZero) || servicePool.Distinct().Count() != servicePool.Length)
            throw new InvalidDataException("qa04.infrastructure.service-pool-closure");

        var queue = MaterializeServiceQueue(resolver, validator, servicePool, residentRefs);
        var materialization = new Qa04InfrastructureServiceQueueCanonicalMaterializationV1(
            transport, water, power, communication, queue, resolver);
        if (materialization.MaterializedRecordCount != CanonicalMaterializedCount)
            throw new InvalidDataException("qa04.infrastructure.service-queue-materialized-total");
        return materialization;
    }

    private static DomainPartitionStateV1<InfrastructureTransportServicePayloadV1> MaterializeTransportServices(
        CanonicalReferenceResolver resolver,
        StandardDomainPayloadCodecValidatorV1 validator,
        IReadOnlyDictionary<OpaqueId128, InfrastructureNetworkPayloadV2> actualNetworks,
        PartitionRecordRefV1[] servicePool)
    {
        var records = new DomainRecordEnvelopeV1<InfrastructureTransportServicePayloadV1>[TransportServiceCount];
        for (ulong i = 0; i < TransportServiceCount; i++)
        {
            var networkOrdinal = checked((uint)(4UL * (i % 25UL)));
            var networkRef = RequireNetwork(actualNetworks, networkOrdinal, "transport");
            var networkLocalServiceOrdinal = checked((uint)(i / 25UL));
            var edgeOrdinal = checked(networkOrdinal * Qa04InfrastructureNetworkMaterializerV1.EdgesPerNetwork + networkLocalServiceOrdinal);
            var edgeRef = new PartitionRecordRefV1(
                InfrastructureNetworkTopologyRecordSchemaV2.PartitionId,
                Qa04ReferenceScenariosV1.InfrastructureEdgeId(checked((int)edgeOrdinal)));
            if (!resolver.Exists(edgeRef))
                throw new InvalidDataException("qa04.infrastructure.transport-service-route-edge-missing");

            var payload = new InfrastructureTransportServicePayloadV1(
                networkRef,
                TransportServiceKind,
                new[] { edgeRef },
                CapacityPerStep: 1_000,
                Load: 0,
                ScheduleRef: null,
                AvailabilityPpm: 1_000_000,
                Status: Active);
            records[i] = CreateDescriptorRecord(
                "transport_service", i, InfrastructureTransportServicePayloadV1.PartitionId, payload, validator, resolver);
            var reference = new PartitionRecordRefV1(InfrastructureTransportServicePayloadV1.PartitionId, records[i].RecordId);
            resolver.Add(reference, records[i].RecordSchema);
            servicePool[checked((int)(4UL * i))] = reference;
        }
        return Partition(InfrastructureTransportServicePayloadV1.PartitionId, records);
    }

    private static DomainPartitionStateV1<InfrastructureWaterServicePayloadV1> MaterializeWaterServices(
        CanonicalReferenceResolver resolver,
        StandardDomainPayloadCodecValidatorV1 validator,
        IReadOnlyDictionary<OpaqueId128, InfrastructureNetworkPayloadV2> actualNetworks,
        PartitionRecordRefV1[] servicePool)
    {
        var records = new DomainRecordEnvelopeV1<InfrastructureWaterServicePayloadV1>[WaterServiceCount];
        for (ulong i = 0; i < WaterServiceCount; i++)
        {
            var networkOrdinal = checked((uint)(4UL * (i % 25UL) + 1UL));
            var payload = new InfrastructureWaterServicePayloadV1(
                RequireNetwork(actualNetworks, networkOrdinal, "water"),
                ScopeRefForNetwork(networkOrdinal),
                SupplyMlPerStep: 1_000_000,
                DemandMlPerStep: 0,
                PressureHeadMm: 1_000,
                QualityPpm: 1_000_000,
                AvailabilityPpm: 1_000_000,
                Status: Active);
            records[i] = CreateDescriptorRecord(
                "water_service", i, InfrastructureWaterServicePayloadV1.PartitionId, payload, validator, resolver);
            var reference = new PartitionRecordRefV1(InfrastructureWaterServicePayloadV1.PartitionId, records[i].RecordId);
            resolver.Add(reference, records[i].RecordSchema);
            servicePool[checked((int)(4UL * i + 1UL))] = reference;
        }
        return Partition(InfrastructureWaterServicePayloadV1.PartitionId, records);
    }

    private static DomainPartitionStateV1<InfrastructurePowerServicePayloadV1> MaterializePowerServices(
        CanonicalReferenceResolver resolver,
        StandardDomainPayloadCodecValidatorV1 validator,
        IReadOnlyDictionary<OpaqueId128, InfrastructureNetworkPayloadV2> actualNetworks,
        PartitionRecordRefV1[] servicePool)
    {
        var records = new DomainRecordEnvelopeV1<InfrastructurePowerServicePayloadV1>[PowerServiceCount];
        for (ulong i = 0; i < PowerServiceCount; i++)
        {
            var networkOrdinal = checked((uint)(4UL * (i % 25UL) + 2UL));
            var payload = new InfrastructurePowerServicePayloadV1(
                RequireNetwork(actualNetworks, networkOrdinal, "power"),
                ScopeRefForNetwork(networkOrdinal),
                GenerationMw: 1_000,
                DemandMw: 0,
                DeliveredMw: 0,
                AvailabilityPpm: 1_000_000,
                Status: Active);
            records[i] = CreateDescriptorRecord(
                "power_service", i, InfrastructurePowerServicePayloadV1.PartitionId, payload, validator, resolver);
            var reference = new PartitionRecordRefV1(InfrastructurePowerServicePayloadV1.PartitionId, records[i].RecordId);
            resolver.Add(reference, records[i].RecordSchema);
            servicePool[checked((int)(4UL * i + 2UL))] = reference;
        }
        return Partition(InfrastructurePowerServicePayloadV1.PartitionId, records);
    }

    private static DomainPartitionStateV1<InfrastructureCommunicationServicePayloadV1> MaterializeCommunicationServices(
        CanonicalReferenceResolver resolver,
        StandardDomainPayloadCodecValidatorV1 validator,
        IReadOnlyDictionary<OpaqueId128, InfrastructureNetworkPayloadV2> actualNetworks,
        PartitionRecordRefV1[] servicePool)
    {
        var records = new DomainRecordEnvelopeV1<InfrastructureCommunicationServicePayloadV1>[CommunicationServiceCount];
        for (ulong i = 0; i < CommunicationServiceCount; i++)
        {
            var networkOrdinal = checked((uint)(4UL * (i % 25UL) + 3UL));
            var poolIndex = checked(4UL * i + 3UL);
            var payload = new InfrastructureCommunicationServicePayloadV1(
                RequireNetwork(actualNetworks, networkOrdinal, "communication"),
                ScopeRefForNetwork(networkOrdinal),
                CapacityUnitsPerStep: 1_000,
                QueuedUnits: QueueRecordsForPoolIndex(poolIndex),
                LatencySteps: 1,
                AvailabilityPpm: 1_000_000,
                Status: Active);
            records[i] = CreateDescriptorRecord(
                "communication_service", i, InfrastructureCommunicationServicePayloadV1.PartitionId, payload, validator, resolver);
            var reference = new PartitionRecordRefV1(InfrastructureCommunicationServicePayloadV1.PartitionId, records[i].RecordId);
            resolver.Add(reference, records[i].RecordSchema);
            servicePool[checked((int)poolIndex)] = reference;
        }
        return Partition(InfrastructureCommunicationServicePayloadV1.PartitionId, records);
    }

    private static DomainPartitionStateV1<InfrastructureServiceQueuePayloadV1> MaterializeServiceQueue(
        CanonicalReferenceResolver resolver,
        StandardDomainPayloadCodecValidatorV1 validator,
        IReadOnlyList<PartitionRecordRefV1> servicePool,
        IReadOnlyList<PartitionRecordRefV1> residentRefs)
    {
        var identity = StandardDomainPartitionRegistry.Get(InfrastructureServiceQueuePayloadV1.PartitionId);
        var records = new DomainRecordEnvelopeV1<InfrastructureServiceQueuePayloadV1>[ServiceQueueCount];
        for (ulong q = 0; q < ServiceQueueCount; q++)
        {
            var binding = Qa04InfrastructureReferenceDecompositionV1.Bind(
                checked(Qa04InfrastructureReferenceDecompositionV1.ServiceQueueStartOrdinal + q));
            if (binding.MaterialClass.Value != "service_queue" ||
                binding.PartitionId.Value != InfrastructureServiceQueuePayloadV1.PartitionId ||
                !binding.UsesSpecializedIdentity)
                throw new InvalidDataException("qa04.infrastructure.service-queue-binding-drift");

            var payload = new InfrastructureServiceQueuePayloadV1(
                servicePool[checked((int)(q % CanonicalServicePoolCount))],
                residentRefs[checked((int)q)],
                EligibleStep: 0,
                SemanticPriority: 0,
                RequestedUnits: 1,
                AllocatedUnits: 0,
                Status: Queued);
            validator.Validate(InfrastructureServiceQueuePayloadV1.PartitionId, payload.ToStandardPayload(), resolver);

            var recordId = Qa04ReferenceScenariosV1.InfrastructureServiceRequestId(checked((int)q));
            var record = new DomainRecordEnvelopeV1<InfrastructureServiceQueuePayloadV1>(
                recordId,
                identity.RecordSchema,
                revision: 1,
                createdStep: 0,
                retiredStep: null,
                DetailLevelV1.D2RegionalAggregate,
                lineageRef: null,
                payload);
            records[q] = record;
            resolver.Add(new PartitionRecordRefV1(InfrastructureServiceQueuePayloadV1.PartitionId, recordId), record.RecordSchema);
        }
        return new DomainPartitionStateV1<InfrastructureServiceQueuePayloadV1>(identity, records);
    }

    private static DomainRecordEnvelopeV1<TPayload> CreateDescriptorRecord<TPayload>(
        string materialClass,
        ulong localOrdinal,
        string partitionId,
        TPayload payload,
        StandardDomainPayloadCodecValidatorV1 validator,
        CanonicalReferenceResolver resolver)
        where TPayload : notnull
    {
        var slice = Qa04InfrastructureReferenceDecompositionV1.Get(materialClass);
        var binding = Qa04InfrastructureReferenceDecompositionV1.Bind(checked(slice.StartOrdinal + localOrdinal));
        if (binding.MaterialClass.Value != materialClass || binding.PartitionId.Value != partitionId || binding.UsesSpecializedIdentity)
            throw new InvalidDataException($"qa04.infrastructure.{materialClass}-binding-drift");

        var standardPayload = payload switch
        {
            InfrastructureTransportServicePayloadV1 value => value.ToStandardPayload(),
            InfrastructureWaterServicePayloadV1 value => value.ToStandardPayload(),
            InfrastructurePowerServicePayloadV1 value => value.ToStandardPayload(),
            InfrastructureCommunicationServicePayloadV1 value => value.ToStandardPayload(),
            _ => throw new InvalidOperationException($"Unsupported canonical Infrastructure service payload: {typeof(TPayload).Name}"),
        };
        validator.Validate(partitionId, standardPayload, resolver);
        var identity = StandardDomainPartitionRegistry.Get(partitionId);
        return new DomainRecordEnvelopeV1<TPayload>(
            binding.Descriptor.RecordId,
            identity.RecordSchema,
            revision: 1,
            createdStep: 0,
            retiredStep: null,
            DetailLevelV1.D2RegionalAggregate,
            lineageRef: null,
            payload);
    }

    private static IReadOnlyDictionary<OpaqueId128, InfrastructureNetworkPayloadV2> SeedTopologyAuthority(
        CanonicalReferenceResolver resolver)
    {
        var networks = new Dictionary<OpaqueId128, InfrastructureNetworkPayloadV2>();
        ulong count = 0;
        foreach (var record in Qa04InfrastructureNetworkMaterializerV1.MaterializeCanonicalTopology(
                     Qa04SpatialTileScopeAuthorityV1.ScopeRef))
        {
            resolver.Add(
                new PartitionRecordRefV1(InfrastructureNetworkTopologyRecordSchemaV2.PartitionId, record.RecordId),
                record.RecordSchema);
            if (record.Payload is InfrastructureNetworkPayloadV2 network && !networks.TryAdd(record.RecordId, network))
                throw new InvalidDataException("qa04.infrastructure.network-authority-duplicate");
            count++;
        }
        if (count != Qa04InfrastructureNetworkMaterializerV1.CanonicalTopologyRecordCount ||
            networks.Count != Qa04InfrastructureNetworkMaterializerV1.Networks)
            throw new InvalidDataException("qa04.infrastructure.network-authority-count");
        return networks;
    }

    private static PartitionRecordRefV1[] SeedResidentAuthority(CanonicalReferenceResolver resolver)
    {
        var materialization = Qa04ReferenceWorldMaterializerV1.MaterializeResidentIdentityLifecycle(RequiredResidentAuthorityCount);
        foreach (var record in materialization.Partition.RecordsCanonical)
        {
            resolver.Add(
                new PartitionRecordRefV1(ResidentIdentityLifecyclePayloadV1.PartitionId, record.RecordId),
                record.RecordSchema);
        }

        var refs = new PartitionRecordRefV1[RequiredResidentAuthorityCount];
        for (ulong ordinal = 0; ordinal < RequiredResidentAuthorityCount; ordinal++)
        {
            var descriptor = Qa04ReferenceLoadV1.Record(ResidentReferenceClass, ordinal);
            refs[ordinal] = new PartitionRecordRefV1(ResidentIdentityLifecyclePayloadV1.PartitionId, descriptor.RecordId);
            if (!resolver.Exists(refs[ordinal]))
                throw new InvalidDataException("qa04.infrastructure.service-queue-resident-authority-missing");
        }
        return refs;
    }

    private static void SeedTileScopeAuthority(CanonicalReferenceResolver resolver)
    {
        foreach (var record in Qa04SpatialTileScopeAuthorityV1.MaterializeCanonical().RecordsCanonical)
        {
            resolver.Add(
                new PartitionRecordRefV1(SpatialScopeRegistryPayloadV1.PartitionId, record.RecordId),
                record.RecordSchema);
        }
    }

    private static PartitionRecordRefV1 RequireNetwork(
        IReadOnlyDictionary<OpaqueId128, InfrastructureNetworkPayloadV2> actualNetworks,
        uint networkOrdinal,
        string expectedKind)
    {
        var id = Qa04InfrastructureNetworkMaterializerV1.NetworkId(networkOrdinal);
        if (!actualNetworks.TryGetValue(id, out var network) || network.NetworkKind.Value != expectedKind)
            throw new InvalidDataException($"qa04.infrastructure.service-network-kind:{expectedKind}");
        return new PartitionRecordRefV1(InfrastructureNetworkTopologyRecordSchemaV2.PartitionId, id);
    }

    private static PartitionRecordRefV1 ScopeRefForNetwork(uint networkOrdinal)
    {
        var tile = checked((ushort)(((ulong)networkOrdinal * Qa04ReferenceLoadV1.RegionalTileCount) /
                                    Qa04InfrastructureNetworkMaterializerV1.Networks));
        return Qa04SpatialTileScopeAuthorityV1.ScopeRef(tile);
    }

    private static ulong QueueRecordsForPoolIndex(ulong poolIndex)
    {
        if (poolIndex >= CanonicalServicePoolCount) throw new ArgumentOutOfRangeException(nameof(poolIndex));
        var quotient = ServiceQueueCount / CanonicalServicePoolCount;
        var remainder = ServiceQueueCount % CanonicalServicePoolCount;
        return checked(quotient + (poolIndex < remainder ? 1UL : 0UL));
    }

    private static DomainPartitionStateV1<TPayload> Partition<TPayload>(
        string partitionId,
        IEnumerable<DomainRecordEnvelopeV1<TPayload>> records)
        => new(StandardDomainPartitionRegistry.Get(partitionId), records);

    private static void RequireSlice(
        string materialClass,
        string partitionId,
        ulong startOrdinal,
        ulong count,
        bool specialized)
    {
        var slice = Qa04InfrastructureReferenceDecompositionV1.Get(materialClass);
        if (slice.PartitionId.Value != partitionId || slice.StartOrdinal != startOrdinal ||
            slice.Count != count || slice.UsesSpecializedIdentity != specialized)
            throw new InvalidDataException($"qa04.infrastructure.{materialClass}-slice-drift");
    }

    private sealed class CanonicalReferenceResolver : IDomainRecordSchemaResolverV1
    {
        private readonly Dictionary<PartitionRecordRefV1, SchemaRefV1> _records = new();

        public void Add(PartitionRecordRefV1 reference, SchemaRefV1 schema)
        {
            if (reference.RecordId.IsZero)
                throw new InvalidDataException("qa04.infrastructure.canonical-reference-zero");
            if (!_records.TryAdd(reference, schema) && _records[reference] != schema)
                throw new InvalidDataException("qa04.infrastructure.canonical-reference-schema-conflict");
        }

        public bool Exists(PartitionRecordRefV1 reference) => _records.ContainsKey(reference);

        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
            => _records.TryGetValue(reference, out schema);
    }
}
