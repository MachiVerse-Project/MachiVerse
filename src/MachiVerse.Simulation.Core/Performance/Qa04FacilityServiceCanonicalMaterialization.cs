using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed class Qa04FacilityServiceCanonicalMaterializationV1
{
    internal Qa04FacilityServiceCanonicalMaterializationV1(
        Qa04PhysicalD0PropertyAssetSupportMaterializationV1 physicalAuthority,
        Qa04InfrastructureServiceQueueCanonicalMaterializationV1 serviceAuthority,
        DomainPartitionStateV1<BuiltStructurePayloadV1> builtStructures,
        IReadOnlyList<DomainRecordEnvelopeV1<BuiltStructurePayloadV1>> builtStructureRecordsByOrdinal,
        DomainPartitionStateV1<InfrastructureFacilityServicePayloadV1> facilityServices,
        IReadOnlyList<DomainRecordEnvelopeV1<InfrastructureFacilityServicePayloadV1>> facilityServiceRecordsByOrdinal,
        IReadOnlyList<PartitionRecordRefV1> canonicalServicePool,
        IDomainRecordSchemaResolverV1 references)
    {
        PhysicalAuthority = physicalAuthority;
        ServiceAuthority = serviceAuthority;
        BuiltStructures = builtStructures;
        BuiltStructureRecordsByOrdinal = builtStructureRecordsByOrdinal;
        FacilityServices = facilityServices;
        FacilityServiceRecordsByOrdinal = facilityServiceRecordsByOrdinal;
        CanonicalServicePool = canonicalServicePool;
        References = references;
    }

    public Qa04PhysicalD0PropertyAssetSupportMaterializationV1 PhysicalAuthority { get; }
    public Qa04InfrastructureServiceQueueCanonicalMaterializationV1 ServiceAuthority { get; }
    public DomainPartitionStateV1<BuiltStructurePayloadV1> BuiltStructures { get; }
    public IReadOnlyList<DomainRecordEnvelopeV1<BuiltStructurePayloadV1>> BuiltStructureRecordsByOrdinal { get; }
    public DomainPartitionStateV1<InfrastructureFacilityServicePayloadV1> FacilityServices { get; }
    public IReadOnlyList<DomainRecordEnvelopeV1<InfrastructureFacilityServicePayloadV1>> FacilityServiceRecordsByOrdinal { get; }
    public IReadOnlyList<PartitionRecordRefV1> CanonicalServicePool { get; }
    public IDomainRecordSchemaResolverV1 References { get; }
    public ulong MaterializedFacilityRecordCount => FacilityServices.ItemCount;
}

/// <summary>
/// Benchmark-only production authority for the 15,000 built.structure identities and matching
/// infrastructure.facility_service records approved under #240. PhysicalPresence identities are
/// never reused as facility identities: each BuiltStructure has its own deterministic RecordId and
/// is grounded by the already-approved first-50k Physical support authority.
/// </summary>
public static class Qa04FacilityServiceCanonicalAuthorityV1
{
    public const ulong CanonicalCount = 15_000;
    public const ulong InfrastructureStartOrdinal = 180_100;
    public const uint CapacityPerStep = 1;
    public const uint ActiveLoad = 0;
    public const uint AvailabilityPpm = 1_000_000;

    public static readonly StableToken StructureClass = new("perf.service-facility");
    public static readonly StableToken ServiceKind = new("perf.facility-service");
    public static readonly StableToken Active = new("active");

    private static readonly StableToken PhysicalReferenceClass = new("physical.d0-presence");
    private static readonly StableToken PhysicalBuiltDomain = new("physical_built");
    private static readonly StableToken StructureCreationKind = new("perf.service-facility-structure");

    public static void ValidateCanonicalContract()
    {
        Qa04InfrastructureReferenceDecompositionV1.ValidateCanonicalContract();
        Qa04PhysicalD0PropertyAssetSupportCanonicalAuthorityV1.ValidateCanonicalContract();
        Qa04InfrastructureServiceQueueCanonicalMaterializerV1.ValidateCanonicalContract();

        var slice = Qa04InfrastructureReferenceDecompositionV1.Get("facility_service");
        if (slice.PartitionId.Value != InfrastructureFacilityServicePayloadV1.PartitionId ||
            slice.StartOrdinal != InfrastructureStartOrdinal || slice.Count != CanonicalCount || slice.UsesSpecializedIdentity)
            throw new InvalidDataException("qa04.infrastructure.facility-service-slice-drift");
        if (CanonicalCount > Qa04PhysicalD0PropertyAssetSupportCanonicalAuthorityV1.CanonicalPhysicalCount ||
            CanonicalCount > Qa04ReferenceWorldMaterializerV1.CanonicalResidentCount ||
            CapacityPerStep != 1 || ActiveLoad != 0 || AvailabilityPpm != 1_000_000)
            throw new InvalidDataException("qa04.infrastructure.facility-service-cardinality-or-genesis-drift");
        if (StandardDomainPartitionRegistry.Get(BuiltStructurePayloadV1.PartitionId).OwnerDomain != PhysicalBuiltDomain ||
            StandardDomainPartitionRegistry.Get(InfrastructureFacilityServicePayloadV1.PartitionId).OwnerDomain.Value != "infrastructure_information")
            throw new InvalidDataException("qa04.infrastructure.facility-service-owner-drift");
        if (BuiltStructureRecordId(0).IsZero || BuiltStructureRecordId(CanonicalCount - 1).IsZero ||
            BuiltStructureRecordId(0) == BuiltStructureRecordId(CanonicalCount - 1))
            throw new InvalidDataException("qa04.infrastructure.facility-service-structure-identity-drift");
    }

    public static OpaqueId128 BuiltStructureRecordId(ulong localOrdinal)
    {
        if (localOrdinal >= CanonicalCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));
        var physical = Qa04ReferenceLoadV1.Record(PhysicalReferenceClass, localOrdinal);
        return DerivedIdentity.DeriveEntityId(
            Qa04ReferenceLoadV1.WorldId,
            creationStep: 0,
            PhysicalBuiltDomain,
            physical.RecordId,
            StructureCreationKind,
            localOrdinal: 0);
    }

    public static PartitionRecordRefV1 BuiltStructureRef(ulong localOrdinal)
        => new(BuiltStructurePayloadV1.PartitionId, BuiltStructureRecordId(localOrdinal));

    public static PartitionRecordRefV1 FacilityServiceRef(ulong localOrdinal)
    {
        if (localOrdinal >= CanonicalCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));
        var binding = FacilityBinding(localOrdinal);
        return new PartitionRecordRefV1(InfrastructureFacilityServicePayloadV1.PartitionId, binding.Descriptor.RecordId);
    }

    public static Qa04FacilityServiceCanonicalMaterializationV1 MaterializeCanonical()
        => MaterializeCanonical(
            Qa04PhysicalD0PropertyAssetSupportCanonicalAuthorityV1.MaterializeCanonical(),
            Qa04InfrastructureServiceQueueCanonicalMaterializerV1.MaterializeCanonical());

    public static Qa04FacilityServiceCanonicalMaterializationV1 MaterializeCanonical(
        Qa04PhysicalD0PropertyAssetSupportMaterializationV1 physicalAuthority,
        Qa04InfrastructureServiceQueueCanonicalMaterializationV1 serviceAuthority)
    {
        ArgumentNullException.ThrowIfNull(physicalAuthority);
        ArgumentNullException.ThrowIfNull(serviceAuthority);
        ValidateCanonicalContract();

        if (physicalAuthority.Presences.ItemCount < CanonicalCount ||
            physicalAuthority.PhysicalRecordsByOrdinal.Count < checked((int)CanonicalCount) ||
            serviceAuthority.MaterializedRecordCount != Qa04InfrastructureServiceQueueCanonicalMaterializerV1.CanonicalMaterializedCount)
            throw new InvalidDataException("qa04.infrastructure.facility-service-upstream-count-drift");

        var resolver = new CanonicalReferenceResolver();
        SeedPhysicalReferences(physicalAuthority, resolver);
        var validator = new StandardDomainPayloadCodecValidatorV1();

        var builtIdentity = StandardDomainPartitionRegistry.Get(BuiltStructurePayloadV1.PartitionId);
        var builtRecords = new DomainRecordEnvelopeV1<BuiltStructurePayloadV1>[checked((int)CanonicalCount)];
        for (ulong f = 0; f < CanonicalCount; f++)
        {
            var physical = physicalAuthority.PhysicalRecordsByOrdinal[checked((int)f)];
            physical.ValidateRefClosure();
            var descriptor = Qa04ReferenceLoadV1.Record(PhysicalReferenceClass, f);
            if (physical.Presence.RecordId != descriptor.RecordId)
                throw new InvalidDataException("qa04.infrastructure.facility-service-physical-presence-id-drift");

            var payload = new BuiltStructurePayloadV1(
                Qa04SpatialTileScopeAuthorityV1.ScopeRef(descriptor.RegionalTileIndex),
                StructureClass,
                new[] { physical.Presence.Payload.ShapeRef },
                Array.Empty<PartitionRecordRefV1>(),
                IntegrityPpm: 1_000_000,
                Array.Empty<PartitionRecordRefV1>(),
                Active);
            validator.Validate(BuiltStructurePayloadV1.PartitionId, payload.ToStandardPayload(), resolver);

            var record = new DomainRecordEnvelopeV1<BuiltStructurePayloadV1>(
                BuiltStructureRecordId(f),
                builtIdentity.RecordSchema,
                revision: 1,
                createdStep: 0,
                retiredStep: null,
                DetailLevelV1.D0Entity,
                lineageRef: null,
                payload);
            builtRecords[checked((int)f)] = record;
            resolver.Add(new PartitionRecordRefV1(BuiltStructurePayloadV1.PartitionId, record.RecordId), record.RecordSchema);
        }
        ValidateBuiltStructureIdentityUniqueness(builtRecords);
        var builtPartition = new DomainPartitionStateV1<BuiltStructurePayloadV1>(builtIdentity, builtRecords);

        var facilityIdentity = StandardDomainPartitionRegistry.Get(InfrastructureFacilityServicePayloadV1.PartitionId);
        var facilityRecords = new DomainRecordEnvelopeV1<InfrastructureFacilityServicePayloadV1>[checked((int)CanonicalCount)];
        for (ulong f = 0; f < CanonicalCount; f++)
        {
            var binding = FacilityBinding(f);
            var payload = new InfrastructureFacilityServicePayloadV1(
                BuiltStructureRef(f),
                ServiceKind,
                CapacityPerStep,
                ActiveLoad,
                Array.Empty<PartitionRecordRefV1>(),
                AvailabilityPpm,
                Active);
            validator.Validate(InfrastructureFacilityServicePayloadV1.PartitionId, payload.ToStandardPayload(), resolver);

            facilityRecords[checked((int)f)] = new DomainRecordEnvelopeV1<InfrastructureFacilityServicePayloadV1>(
                binding.Descriptor.RecordId,
                facilityIdentity.RecordSchema,
                revision: 1,
                createdStep: 0,
                retiredStep: null,
                binding.Descriptor.DetailLevel,
                lineageRef: null,
                payload);
        }
        ValidateFacilityServiceIdentityUniqueness(facilityRecords);
        var facilityPartition = new DomainPartitionStateV1<InfrastructureFacilityServicePayloadV1>(facilityIdentity, facilityRecords);

        for (ulong f = 0; f < CanonicalCount; f++)
        {
            ValidateCanonicalBuiltStructureRecord(f, builtRecords[checked((int)f)], physicalAuthority, resolver);
            ValidateCanonicalFacilityServiceRecord(f, facilityRecords[checked((int)f)], resolver);
        }

        var pool = Qa04InfrastructureCanonicalServicePoolV1.BuildCanonical(serviceAuthority, facilityPartition);
        return new Qa04FacilityServiceCanonicalMaterializationV1(
            physicalAuthority,
            serviceAuthority,
            builtPartition,
            Array.AsReadOnly(builtRecords),
            facilityPartition,
            Array.AsReadOnly(facilityRecords),
            pool,
            resolver);
    }

    public static void ValidateCanonicalBuiltStructureRecord(
        ulong localOrdinal,
        DomainRecordEnvelopeV1<BuiltStructurePayloadV1> record,
        Qa04PhysicalD0PropertyAssetSupportMaterializationV1 physicalAuthority,
        IDomainRecordSchemaResolverV1 references)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(physicalAuthority);
        ArgumentNullException.ThrowIfNull(references);
        if (localOrdinal >= CanonicalCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));

        var physical = physicalAuthority.PhysicalRecordsByOrdinal[checked((int)localOrdinal)];
        physical.ValidateRefClosure();
        var descriptor = Qa04ReferenceLoadV1.Record(PhysicalReferenceClass, localOrdinal);
        var identity = StandardDomainPartitionRegistry.Get(BuiltStructurePayloadV1.PartitionId);
        if (record.RecordId != BuiltStructureRecordId(localOrdinal) || record.RecordSchema != identity.RecordSchema ||
            record.Revision != 1 || record.CreatedStep != 0 || record.RetiredStep is not null ||
            record.DetailLevel != DetailLevelV1.D0Entity || record.LineageRef is not null)
            throw new InvalidDataException("qa04.infrastructure.facility-service-structure-envelope-drift");

        var payload = record.Payload;
        if (payload.SpatialScope != Qa04SpatialTileScopeAuthorityV1.ScopeRef(descriptor.RegionalTileIndex) ||
            payload.StructureClass != StructureClass ||
            payload.GeometryParts.Count != 1 || payload.GeometryParts[0] != physical.Presence.Payload.ShapeRef ||
            payload.MaterialRefs.Count != 0 || payload.IntegrityPpm != 1_000_000 ||
            payload.SupportRefs.Count != 0 || payload.Lifecycle != Active)
            throw new InvalidDataException("qa04.infrastructure.facility-service-structure-payload-drift");

        new StandardDomainPayloadCodecValidatorV1().Validate(
            BuiltStructurePayloadV1.PartitionId, payload.ToStandardPayload(), references);
    }

    public static void ValidateCanonicalFacilityServiceRecord(
        ulong localOrdinal,
        DomainRecordEnvelopeV1<InfrastructureFacilityServicePayloadV1> record,
        IDomainRecordSchemaResolverV1 references)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(references);
        if (localOrdinal >= CanonicalCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));

        var binding = FacilityBinding(localOrdinal);
        var identity = StandardDomainPartitionRegistry.Get(InfrastructureFacilityServicePayloadV1.PartitionId);
        if (record.RecordId != binding.Descriptor.RecordId || record.RecordSchema != identity.RecordSchema ||
            record.Revision != 1 || record.CreatedStep != 0 || record.RetiredStep is not null ||
            record.DetailLevel != binding.Descriptor.DetailLevel || record.LineageRef is not null)
            throw new InvalidDataException("qa04.infrastructure.facility-service-envelope-drift");

        var payload = record.Payload;
        if (payload.FacilityRef != BuiltStructureRef(localOrdinal) ||
            payload.ServiceKind != ServiceKind || payload.CapacityPerStep != CapacityPerStep ||
            payload.ActiveLoad != ActiveLoad || payload.RequiredResourceRefs.Count != 0 ||
            payload.AvailabilityPpm != AvailabilityPpm || payload.Status != Active)
            throw new InvalidDataException("qa04.infrastructure.facility-service-payload-drift");

        new StandardDomainPayloadCodecValidatorV1().Validate(
            InfrastructureFacilityServicePayloadV1.PartitionId, payload.ToStandardPayload(), references);
    }

    public static void ValidateBuiltStructureIdentityUniqueness(IEnumerable<DomainRecordEnvelopeV1<BuiltStructurePayloadV1>> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var ids = new HashSet<OpaqueId128>();
        var shapes = new HashSet<PartitionRecordRefV1>();
        foreach (var record in records)
        {
            if (!ids.Add(record.RecordId))
                throw new InvalidDataException("qa04.infrastructure.facility-service-structure-id-duplicate");
            if (record.Payload.GeometryParts.Count != 1 || !shapes.Add(record.Payload.GeometryParts[0]))
                throw new InvalidDataException("qa04.infrastructure.facility-service-structure-shape-duplicate-or-noncanonical");
        }
    }

    public static void ValidateFacilityServiceIdentityUniqueness(IEnumerable<DomainRecordEnvelopeV1<InfrastructureFacilityServicePayloadV1>> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var ids = new HashSet<OpaqueId128>();
        var facilities = new HashSet<PartitionRecordRefV1>();
        foreach (var record in records)
        {
            if (!ids.Add(record.RecordId))
                throw new InvalidDataException("qa04.infrastructure.facility-service-id-duplicate");
            if (!facilities.Add(record.Payload.FacilityRef))
                throw new InvalidDataException("qa04.infrastructure.facility-service-facility-relation-duplicate");
        }
    }

    private static Qa04InfrastructureBindingV1 FacilityBinding(ulong localOrdinal)
    {
        if (localOrdinal >= CanonicalCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));
        var binding = Qa04InfrastructureReferenceDecompositionV1.Bind(checked(InfrastructureStartOrdinal + localOrdinal));
        if (binding.MaterialClass.Value != "facility_service" ||
            binding.PartitionId.Value != InfrastructureFacilityServicePayloadV1.PartitionId ||
            binding.LocalOrdinal != localOrdinal || binding.UsesSpecializedIdentity)
            throw new InvalidDataException("qa04.infrastructure.facility-service-binding-drift");
        return binding;
    }

    private static void SeedPhysicalReferences(
        Qa04PhysicalD0PropertyAssetSupportMaterializationV1 physicalAuthority,
        CanonicalReferenceResolver resolver)
    {
        foreach (var scope in physicalAuthority.TileScopes.RecordsCanonical)
            resolver.Add(new PartitionRecordRefV1(SpatialScopeRegistryPayloadV1.PartitionId, scope.RecordId), scope.RecordSchema);

        for (ulong f = 0; f < CanonicalCount; f++)
        {
            var physical = physicalAuthority.PhysicalRecordsByOrdinal[checked((int)f)];
            physical.ValidateRefClosure();
            resolver.Add(
                new PartitionRecordRefV1(PhysicalPresencePayloadV1.PartitionId, physical.Presence.RecordId),
                physical.Presence.RecordSchema);
            resolver.Add(physical.Presence.Payload.ShapeRef, physical.CollisionShape.RecordSchema);
        }
    }

    private sealed class CanonicalReferenceResolver : IDomainRecordSchemaResolverV1
    {
        private readonly Dictionary<PartitionRecordRefV1, SchemaRefV1> _records = new();

        public void Add(PartitionRecordRefV1 reference, SchemaRefV1 schema)
        {
            if (reference.RecordId.IsZero)
                throw new InvalidDataException("qa04.infrastructure.facility-service-reference-zero");
            if (!_records.TryAdd(reference, schema) && _records[reference] != schema)
                throw new InvalidDataException("qa04.infrastructure.facility-service-reference-schema-conflict");
        }

        public bool Exists(PartitionRecordRefV1 reference) => _records.ContainsKey(reference);
        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
            => _records.TryGetValue(reference, out schema);
    }
}

/// <summary>Exact 55,000-record service pool required by the canonical Infrastructure workload.</summary>
public static class Qa04InfrastructureCanonicalServicePoolV1
{
    public const ulong CanonicalCount = 55_000;

    private static readonly Lazy<IReadOnlyList<PartitionRecordRefV1>> ExpectedPool = new(CreateExpectedPool);

    public static IReadOnlyList<PartitionRecordRefV1> Expected => ExpectedPool.Value;

    public static PartitionRecordRefV1 Resolve(ulong ordinal)
    {
        if (Expected.Count != checked((int)CanonicalCount))
            throw new InvalidDataException("qa04.infrastructure.canonical-service-pool-count-drift");
        return Expected[checked((int)(ordinal % CanonicalCount))];
    }

    public static IReadOnlyList<PartitionRecordRefV1> BuildCanonical(
        Qa04InfrastructureServiceQueueCanonicalMaterializationV1 serviceAuthority,
        DomainPartitionStateV1<InfrastructureFacilityServicePayloadV1> facilityServices)
    {
        ArgumentNullException.ThrowIfNull(serviceAuthority);
        ArgumentNullException.ThrowIfNull(facilityServices);

        var actual = serviceAuthority.TransportServices.RecordsCanonical
            .Select(static record => new PartitionRecordRefV1(InfrastructureTransportServicePayloadV1.PartitionId, record.RecordId))
            .Concat(serviceAuthority.WaterServices.RecordsCanonical
                .Select(static record => new PartitionRecordRefV1(InfrastructureWaterServicePayloadV1.PartitionId, record.RecordId)))
            .Concat(serviceAuthority.PowerServices.RecordsCanonical
                .Select(static record => new PartitionRecordRefV1(InfrastructurePowerServicePayloadV1.PartitionId, record.RecordId)))
            .Concat(serviceAuthority.CommunicationServices.RecordsCanonical
                .Select(static record => new PartitionRecordRefV1(InfrastructureCommunicationServicePayloadV1.PartitionId, record.RecordId)))
            .Concat(facilityServices.RecordsCanonical
                .Select(static record => new PartitionRecordRefV1(InfrastructureFacilityServicePayloadV1.PartitionId, record.RecordId)))
            .ToArray();

        var ordered = Order(actual);
        if ((ulong)ordered.Length != CanonicalCount || ordered.Distinct().Count() != ordered.Length)
            throw new InvalidDataException("qa04.infrastructure.canonical-service-pool-actual-cardinality-drift");
        if (!ordered.SequenceEqual(Expected))
            throw new InvalidDataException("qa04.infrastructure.canonical-service-pool-actual-identity-drift");
        return Array.AsReadOnly(ordered);
    }

    private static IReadOnlyList<PartitionRecordRefV1> CreateExpectedPool()
    {
        Qa04InfrastructureReferenceDecompositionV1.ValidateCanonicalContract();
        var materialClasses = new[]
        {
            "transport_service",
            "water_service",
            "power_service",
            "communication_service",
            "facility_service",
        };
        var refs = new List<PartitionRecordRefV1>(checked((int)CanonicalCount));
        foreach (var materialClass in materialClasses)
        {
            var slice = Qa04InfrastructureReferenceDecompositionV1.Get(materialClass);
            for (ulong i = 0; i < slice.Count; i++)
            {
                var binding = Qa04InfrastructureReferenceDecompositionV1.Bind(checked(slice.StartOrdinal + i));
                refs.Add(new PartitionRecordRefV1(binding.PartitionId, binding.Descriptor.RecordId));
            }
        }
        var ordered = Order(refs);
        if ((ulong)ordered.Length != CanonicalCount || ordered.Distinct().Count() != ordered.Length)
            throw new InvalidDataException("qa04.infrastructure.canonical-service-pool-expected-cardinality-drift");
        return Array.AsReadOnly(ordered);
    }

    private static PartitionRecordRefV1[] Order(IEnumerable<PartitionRecordRefV1> source)
        => source
            .OrderBy(static reference => reference.PartitionId.Value, StringComparer.Ordinal)
            .ThenBy(static reference => Convert.ToHexString(reference.RecordId.ToBytes()), StringComparer.Ordinal)
            .ToArray();
}
