using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed class Qa04InfrastructureDependencyCanonicalMaterializationV1
{
    internal Qa04InfrastructureDependencyCanonicalMaterializationV1(
        Qa04InfrastructureServiceQueueCanonicalMaterializationV1 serviceAuthority,
        DomainPartitionStateV1<InfrastructureDependencyPayloadV1> partition,
        IReadOnlyList<DomainRecordEnvelopeV1<InfrastructureDependencyPayloadV1>> recordsByOrdinal,
        IReadOnlyList<InfrastructureDependencyV1> runtimeDependencies)
    {
        ServiceAuthority = serviceAuthority;
        Partition = partition;
        RecordsByOrdinal = recordsByOrdinal;
        RuntimeDependencies = runtimeDependencies;
    }

    public Qa04InfrastructureServiceQueueCanonicalMaterializationV1 ServiceAuthority { get; }
    public DomainPartitionStateV1<InfrastructureDependencyPayloadV1> Partition { get; }
    public IReadOnlyList<DomainRecordEnvelopeV1<InfrastructureDependencyPayloadV1>> RecordsByOrdinal { get; }
    public IReadOnlyList<InfrastructureDependencyV1> RuntimeDependencies { get; }
    public IDomainRecordSchemaResolverV1 References => ServiceAuthority.References;
    public ulong MaterializedRecordCount => Partition.ItemCount;
}

/// <summary>
/// Production-path materialization for the approved perf.reference.v1 infrastructure.dependency
/// 20,000-record package. Persistent payload orientation is consumer/provider; runtime outage
/// orientation is deliberately provider->consumer.
/// </summary>
public static class Qa04InfrastructureDependencyCanonicalAuthorityV1
{
    public const ulong CanonicalCount = 20_000;
    public const ulong WaterDependencyCount = 10_000;
    public const ulong CommunicationDependencyCount = 10_000;
    public const uint MinimumServicePpm = 1_000_000;

    public static readonly StableToken DependencyKind = new("perf.power-supply");
    public static readonly StableToken Active = new("active");

    private static readonly StableToken InfrastructureDomain = new("infrastructure_information");
    private static readonly StableToken CreationKind = new("perf.infrastructure-dependency");

    public static void ValidateCanonicalContract()
    {
        Qa04InfrastructureReferenceDecompositionV1.ValidateCanonicalContract();
        Qa04InfrastructureServiceQueueCanonicalMaterializerV1.ValidateCanonicalContract();

        var slice = Qa04InfrastructureReferenceDecompositionV1.Get("dependency");
        if (slice.PartitionId.Value != InfrastructureDependencyPayloadV1.PartitionId ||
            slice.Count != CanonicalCount || slice.UsesSpecializedIdentity)
            throw new InvalidDataException("qa04.infrastructure.dependency-slice-drift");
        if (WaterDependencyCount + CommunicationDependencyCount != CanonicalCount ||
            WaterDependencyCount != Qa04InfrastructureServiceQueueCanonicalMaterializerV1.WaterServiceCount ||
            CommunicationDependencyCount != Qa04InfrastructureServiceQueueCanonicalMaterializerV1.CommunicationServiceCount ||
            WaterDependencyCount != Qa04InfrastructureServiceQueueCanonicalMaterializerV1.PowerServiceCount)
            throw new InvalidDataException("qa04.infrastructure.dependency-cardinality-drift");

        var identity = StandardDomainPartitionRegistry.Get(InfrastructureDependencyPayloadV1.PartitionId);
        if (identity.OwnerDomain != InfrastructureDomain)
            throw new InvalidDataException("qa04.infrastructure.dependency-owner-drift");

        var first = RecordId(0);
        var last = RecordId(CanonicalCount - 1);
        if (first.IsZero || last.IsZero || first == last)
            throw new InvalidDataException("qa04.infrastructure.dependency-identity-drift");
    }

    public static OpaqueId128 RecordId(ulong localOrdinal)
    {
        if (localOrdinal >= CanonicalCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));
        return DerivedIdentity.DeriveEntityId(
            Qa04ReferenceLoadV1.WorldId,
            creationStep: 0,
            InfrastructureDomain,
            OpaqueId128.Zero,
            CreationKind,
            localOrdinal);
    }

    public static Qa04InfrastructureDependencyCanonicalMaterializationV1 MaterializeCanonical()
        => MaterializeCanonical(Qa04InfrastructureServiceQueueCanonicalMaterializerV1.MaterializeCanonical());

    public static Qa04InfrastructureDependencyCanonicalMaterializationV1 MaterializeCanonical(
        Qa04InfrastructureServiceQueueCanonicalMaterializationV1 serviceAuthority)
    {
        ArgumentNullException.ThrowIfNull(serviceAuthority);
        ValidateCanonicalContract();
        if (serviceAuthority.MaterializedRecordCount != Qa04InfrastructureServiceQueueCanonicalMaterializerV1.CanonicalMaterializedCount ||
            serviceAuthority.WaterServices.ItemCount != WaterDependencyCount ||
            serviceAuthority.PowerServices.ItemCount != WaterDependencyCount ||
            serviceAuthority.CommunicationServices.ItemCount != CommunicationDependencyCount)
            throw new InvalidDataException("qa04.infrastructure.dependency-service-authority-count-drift");

        var pools = ServicePools.Create(serviceAuthority);
        var validator = new StandardDomainPayloadCodecValidatorV1();
        var identity = StandardDomainPartitionRegistry.Get(InfrastructureDependencyPayloadV1.PartitionId);
        var records = new DomainRecordEnvelopeV1<InfrastructureDependencyPayloadV1>[checked((int)CanonicalCount)];
        var runtime = new InfrastructureDependencyV1[checked((int)CanonicalCount)];
        var ids = new HashSet<OpaqueId128>();

        for (ulong d = 0; d < CanonicalCount; d++)
        {
            var expected = ExpectedEndpoints(d, pools);
            var payload = new InfrastructureDependencyPayloadV1(
                expected.ConsumerRef,
                expected.ProviderRef,
                DependencyKind,
                MinimumServicePpm,
                DegradationCurveRef: null,
                Array.Empty<PartitionRecordRefV1>(),
                Active);
            validator.Validate(InfrastructureDependencyPayloadV1.PartitionId, payload.ToStandardPayload(), serviceAuthority.References);

            var recordId = RecordId(d);
            if (!ids.Add(recordId) || recordId == expected.ConsumerRef.RecordId || recordId == expected.ProviderRef.RecordId)
                throw new InvalidDataException("qa04.infrastructure.dependency-record-id-duplicate-or-reused");

            var record = new DomainRecordEnvelopeV1<InfrastructureDependencyPayloadV1>(
                recordId,
                identity.RecordSchema,
                revision: 1,
                createdStep: 0,
                retiredStep: null,
                expected.ConsumerDetailLevel,
                lineageRef: null,
                payload);
            records[checked((int)d)] = record;

            // Runtime orientation is provider -> consumer, never persistent consumer -> provider.
            runtime[checked((int)d)] = new InfrastructureDependencyV1(
                expected.ProviderRef.RecordId,
                expected.ConsumerRef.RecordId);
        }

        ValidateIdentityUniqueness(records);
        _ = DeterministicOutageCascadeV1.Propagate(Array.Empty<OpaqueId128>(), runtime);

        var partition = new DomainPartitionStateV1<InfrastructureDependencyPayloadV1>(identity, records);
        if (partition.ItemCount != CanonicalCount)
            throw new InvalidDataException("qa04.infrastructure.dependency-partition-count-drift");

        return new Qa04InfrastructureDependencyCanonicalMaterializationV1(
            serviceAuthority,
            partition,
            Array.AsReadOnly(records),
            Array.AsReadOnly(runtime));
    }

    public static void ValidateCanonicalRecord(
        ulong localOrdinal,
        DomainRecordEnvelopeV1<InfrastructureDependencyPayloadV1> record,
        Qa04InfrastructureServiceQueueCanonicalMaterializationV1 serviceAuthority)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(serviceAuthority);
        if (localOrdinal >= CanonicalCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));

        var expected = ExpectedEndpoints(localOrdinal, ServicePools.Create(serviceAuthority));
        var identity = StandardDomainPartitionRegistry.Get(InfrastructureDependencyPayloadV1.PartitionId);
        if (record.RecordId != RecordId(localOrdinal) ||
            record.RecordSchema != identity.RecordSchema ||
            record.Revision != 1 || record.CreatedStep != 0 || record.RetiredStep is not null ||
            record.DetailLevel != expected.ConsumerDetailLevel || record.LineageRef is not null)
            throw new InvalidDataException("qa04.infrastructure.dependency-envelope-drift");

        var payload = record.Payload;
        if (payload.ConsumerRef != expected.ConsumerRef ||
            payload.ProviderRef != expected.ProviderRef ||
            payload.ConsumerRef == payload.ProviderRef ||
            payload.DependencyKind != DependencyKind ||
            payload.MinimumServicePpm != MinimumServicePpm ||
            payload.DegradationCurveRef is not null ||
            payload.FallbackRefs.Count != 0 ||
            payload.Status != Active)
            throw new InvalidDataException("qa04.infrastructure.dependency-genesis-drift");

        new StandardDomainPayloadCodecValidatorV1().Validate(
            InfrastructureDependencyPayloadV1.PartitionId,
            payload.ToStandardPayload(),
            serviceAuthority.References);
    }

    public static void ValidateIdentityUniqueness(
        IEnumerable<DomainRecordEnvelopeV1<InfrastructureDependencyPayloadV1>> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var ids = new HashSet<OpaqueId128>();
        foreach (var record in records)
            if (!ids.Add(record.RecordId))
                throw new InvalidDataException("qa04.infrastructure.dependency-record-id-duplicate");
    }

    private static ExpectedEndpointV1 ExpectedEndpoints(ulong localOrdinal, ServicePools pools)
    {
        var provider = pools.RequirePower(localOrdinal % WaterDependencyCount);
        if (localOrdinal < WaterDependencyCount)
        {
            var consumer = pools.RequireWater(localOrdinal);
            return new ExpectedEndpointV1(
                new PartitionRecordRefV1(InfrastructureWaterServicePayloadV1.PartitionId, consumer.RecordId),
                new PartitionRecordRefV1(InfrastructurePowerServicePayloadV1.PartitionId, provider.RecordId),
                consumer.DetailLevel);
        }

        var communicationOrdinal = checked(localOrdinal - WaterDependencyCount);
        var communication = pools.RequireCommunication(communicationOrdinal);
        return new ExpectedEndpointV1(
            new PartitionRecordRefV1(InfrastructureCommunicationServicePayloadV1.PartitionId, communication.RecordId),
            new PartitionRecordRefV1(InfrastructurePowerServicePayloadV1.PartitionId, provider.RecordId),
            communication.DetailLevel);
    }

    private sealed record ExpectedEndpointV1(
        PartitionRecordRefV1 ConsumerRef,
        PartitionRecordRefV1 ProviderRef,
        DetailLevelV1 ConsumerDetailLevel);

    private sealed class ServicePools
    {
        private readonly IReadOnlyDictionary<OpaqueId128, DomainRecordEnvelopeV1<InfrastructureWaterServicePayloadV1>> _water;
        private readonly IReadOnlyDictionary<OpaqueId128, DomainRecordEnvelopeV1<InfrastructurePowerServicePayloadV1>> _power;
        private readonly IReadOnlyDictionary<OpaqueId128, DomainRecordEnvelopeV1<InfrastructureCommunicationServicePayloadV1>> _communication;

        private ServicePools(
            IReadOnlyDictionary<OpaqueId128, DomainRecordEnvelopeV1<InfrastructureWaterServicePayloadV1>> water,
            IReadOnlyDictionary<OpaqueId128, DomainRecordEnvelopeV1<InfrastructurePowerServicePayloadV1>> power,
            IReadOnlyDictionary<OpaqueId128, DomainRecordEnvelopeV1<InfrastructureCommunicationServicePayloadV1>> communication)
        {
            _water = water;
            _power = power;
            _communication = communication;
        }

        public static ServicePools Create(Qa04InfrastructureServiceQueueCanonicalMaterializationV1 authority)
            => new(
                authority.WaterServices.RecordsCanonical.ToDictionary(static record => record.RecordId),
                authority.PowerServices.RecordsCanonical.ToDictionary(static record => record.RecordId),
                authority.CommunicationServices.RecordsCanonical.ToDictionary(static record => record.RecordId));

        public DomainRecordEnvelopeV1<InfrastructureWaterServicePayloadV1> RequireWater(ulong ordinal)
            => Require("water_service", ordinal, _water, "water");

        public DomainRecordEnvelopeV1<InfrastructurePowerServicePayloadV1> RequirePower(ulong ordinal)
            => Require("power_service", ordinal, _power, "power");

        public DomainRecordEnvelopeV1<InfrastructureCommunicationServicePayloadV1> RequireCommunication(ulong ordinal)
            => Require("communication_service", ordinal, _communication, "communication");

        private static DomainRecordEnvelopeV1<TPayload> Require<TPayload>(
            string materialClass,
            ulong ordinal,
            IReadOnlyDictionary<OpaqueId128, DomainRecordEnvelopeV1<TPayload>> records,
            string label)
        {
            var slice = Qa04InfrastructureReferenceDecompositionV1.Get(materialClass);
            if (ordinal >= slice.Count) throw new ArgumentOutOfRangeException(nameof(ordinal));
            var expectedId = Qa04InfrastructureReferenceDecompositionV1.Bind(checked(slice.StartOrdinal + ordinal)).Descriptor.RecordId;
            return records.TryGetValue(expectedId, out var record)
                ? record
                : throw new InvalidDataException($"qa04.infrastructure.dependency-{label}-service-missing");
        }
    }
}
