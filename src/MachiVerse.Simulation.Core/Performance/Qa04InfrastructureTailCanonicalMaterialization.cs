using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed class Qa04InfrastructureTailCanonicalMaterializationV1
{
    internal Qa04InfrastructureTailCanonicalMaterializationV1(
        Qa04FacilityServiceCanonicalMaterializationV1 facilityAuthority,
        DomainPartitionStateV1<InformationAddressPlaceIndexPayloadV1> addressPlaceIndexes,
        IReadOnlyList<DomainRecordEnvelopeV1<InformationAddressPlaceIndexPayloadV1>> addressRecordsByOrdinal,
        DomainPartitionStateV1<InfrastructureFailureRecoveryPayloadV1> failureRecoveries,
        IReadOnlyList<DomainRecordEnvelopeV1<InfrastructureFailureRecoveryPayloadV1>> failureRecordsByOrdinal,
        DomainPartitionStateV1<InfrastructureLineagePayloadV1> lineages,
        IReadOnlyList<DomainRecordEnvelopeV1<InfrastructureLineagePayloadV1>> lineageRecordsByOrdinal,
        IDomainRecordSchemaResolverV1 references)
    {
        FacilityAuthority = facilityAuthority;
        AddressPlaceIndexes = addressPlaceIndexes;
        AddressRecordsByOrdinal = addressRecordsByOrdinal;
        FailureRecoveries = failureRecoveries;
        FailureRecordsByOrdinal = failureRecordsByOrdinal;
        Lineages = lineages;
        LineageRecordsByOrdinal = lineageRecordsByOrdinal;
        References = references;
    }

    public Qa04FacilityServiceCanonicalMaterializationV1 FacilityAuthority { get; }
    public DomainPartitionStateV1<InformationAddressPlaceIndexPayloadV1> AddressPlaceIndexes { get; }
    public IReadOnlyList<DomainRecordEnvelopeV1<InformationAddressPlaceIndexPayloadV1>> AddressRecordsByOrdinal { get; }
    public DomainPartitionStateV1<InfrastructureFailureRecoveryPayloadV1> FailureRecoveries { get; }
    public IReadOnlyList<DomainRecordEnvelopeV1<InfrastructureFailureRecoveryPayloadV1>> FailureRecordsByOrdinal { get; }
    public DomainPartitionStateV1<InfrastructureLineagePayloadV1> Lineages { get; }
    public IReadOnlyList<DomainRecordEnvelopeV1<InfrastructureLineagePayloadV1>> LineageRecordsByOrdinal { get; }
    public IDomainRecordSchemaResolverV1 References { get; }
    public ulong MaterializedTailRecordCount => checked(AddressPlaceIndexes.ItemCount + FailureRecoveries.ItemCount + Lineages.ItemCount);
}

/// <summary>
/// Benchmark-only authority for the final 19,900 Infrastructure/Information records in
/// perf.reference.v1. The mappings are intentionally deterministic and bounded to QA-04; they are
/// not a universal address, failure, or lineage model.
/// </summary>
public static class Qa04InfrastructureTailCanonicalAuthorityV1
{
    public const ulong AddressCount = 5_000;
    public const ulong FailureRecoveryCount = 10_000;
    public const ulong LineageCount = 4_900;
    public const ulong CanonicalCount = AddressCount + FailureRecoveryCount + LineageCount;

    public const ulong AddressStartOrdinal = 480_100;
    public const ulong FailureRecoveryStartOrdinal = 485_100;
    public const ulong LineageStartOrdinal = 495_100;

    public static readonly StableToken FailureKind = new("perf.benchmark-recovery-proof");
    public static readonly StableToken Restored = new("restored");
    public static readonly StableToken Genesis = new("perf.genesis");

    private static readonly StableToken PhysicalReferenceClass = new("physical.d0-presence");

    public static void ValidateCanonicalContract()
    {
        Qa04InfrastructureReferenceDecompositionV1.ValidateCanonicalContract();
        Qa04FacilityServiceCanonicalAuthorityV1.ValidateCanonicalContract();

        ValidateSlice("address_place_index", InformationAddressPlaceIndexPayloadV1.PartitionId, AddressStartOrdinal, AddressCount);
        ValidateSlice("failure_recovery", InfrastructureFailureRecoveryPayloadV1.PartitionId, FailureRecoveryStartOrdinal, FailureRecoveryCount);
        ValidateSlice("lineage", InfrastructureLineagePayloadV1.PartitionId, LineageStartOrdinal, LineageCount);

        if (CanonicalCount != 19_900 ||
            AddressStartOrdinal + AddressCount != FailureRecoveryStartOrdinal ||
            FailureRecoveryStartOrdinal + FailureRecoveryCount != LineageStartOrdinal ||
            LineageStartOrdinal + LineageCount != Qa04InfrastructureReferenceDecompositionV1.CanonicalCount)
            throw new InvalidDataException("qa04.infrastructure.tail-cardinality-or-boundary-drift");
        if (AddressCount > Qa04FacilityServiceCanonicalAuthorityV1.CanonicalCount ||
            FailureRecoveryCount > Qa04InfrastructureCanonicalServicePoolV1.CanonicalCount ||
            LineageCount > Qa04FacilityServiceCanonicalAuthorityV1.CanonicalCount)
            throw new InvalidDataException("qa04.infrastructure.tail-upstream-cardinality-drift");
    }

    public static StableToken AddressToken(ulong localOrdinal)
    {
        if (localOrdinal >= AddressCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));
        return new StableToken($"perf.address.{localOrdinal:D5}");
    }

    public static PartitionRecordRefV1 AddressRef(ulong localOrdinal)
        => Binding("address_place_index", AddressStartOrdinal, AddressCount, localOrdinal).DescriptorRef();

    public static PartitionRecordRefV1 FailureRecoveryRef(ulong localOrdinal)
        => Binding("failure_recovery", FailureRecoveryStartOrdinal, FailureRecoveryCount, localOrdinal).DescriptorRef();

    public static PartitionRecordRefV1 LineageRef(ulong localOrdinal)
        => Binding("lineage", LineageStartOrdinal, LineageCount, localOrdinal).DescriptorRef();

    public static Qa04InfrastructureTailCanonicalMaterializationV1 MaterializeCanonical()
        => MaterializeCanonical(Qa04FacilityServiceCanonicalAuthorityV1.MaterializeCanonical());

    public static Qa04InfrastructureTailCanonicalMaterializationV1 MaterializeCanonical(
        Qa04FacilityServiceCanonicalMaterializationV1 facilityAuthority)
    {
        ArgumentNullException.ThrowIfNull(facilityAuthority);
        ValidateCanonicalContract();

        if (facilityAuthority.BuiltStructures.ItemCount != Qa04FacilityServiceCanonicalAuthorityV1.CanonicalCount ||
            facilityAuthority.FacilityServices.ItemCount != Qa04FacilityServiceCanonicalAuthorityV1.CanonicalCount ||
            facilityAuthority.CanonicalServicePool.Count != checked((int)Qa04InfrastructureCanonicalServicePoolV1.CanonicalCount))
            throw new InvalidDataException("qa04.infrastructure.tail-upstream-materialization-drift");

        var resolver = new OverlayReferenceResolver(facilityAuthority.References);
        foreach (var serviceRef in facilityAuthority.CanonicalServicePool)
            resolver.Add(serviceRef, StandardDomainPartitionRegistry.Get(serviceRef.PartitionId.Value).RecordSchema);
        foreach (var facilityRecord in facilityAuthority.FacilityServiceRecordsByOrdinal)
            resolver.Add(
                new PartitionRecordRefV1(InfrastructureFacilityServicePayloadV1.PartitionId, facilityRecord.RecordId),
                facilityRecord.RecordSchema);

        var validator = new StandardDomainPayloadCodecValidatorV1();
        var addressRecords = MaterializeAddresses(facilityAuthority, resolver, validator);
        var addressState = new DomainPartitionStateV1<InformationAddressPlaceIndexPayloadV1>(
            StandardDomainPartitionRegistry.Get(InformationAddressPlaceIndexPayloadV1.PartitionId), addressRecords);
        AddPartition(addressState, resolver);

        var failureRecords = MaterializeFailureRecoveries(facilityAuthority, resolver, validator);
        var failureState = new DomainPartitionStateV1<InfrastructureFailureRecoveryPayloadV1>(
            StandardDomainPartitionRegistry.Get(InfrastructureFailureRecoveryPayloadV1.PartitionId), failureRecords);
        AddPartition(failureState, resolver);

        var lineageRecords = MaterializeLineages(facilityAuthority, resolver, validator);
        var lineageState = new DomainPartitionStateV1<InfrastructureLineagePayloadV1>(
            StandardDomainPartitionRegistry.Get(InfrastructureLineagePayloadV1.PartitionId), lineageRecords);
        AddPartition(lineageState, resolver);

        ValidateAddressUniqueness(addressRecords);
        ValidateFailureRecoveryUniqueness(failureRecords);
        ValidateLineageUniqueness(lineageRecords);

        for (ulong ordinal = 0; ordinal < AddressCount; ordinal++)
            ValidateCanonicalAddressRecord(ordinal, addressRecords[checked((int)ordinal)], facilityAuthority, resolver);
        for (ulong ordinal = 0; ordinal < FailureRecoveryCount; ordinal++)
            ValidateCanonicalFailureRecoveryRecord(ordinal, failureRecords[checked((int)ordinal)], facilityAuthority, resolver);
        for (ulong ordinal = 0; ordinal < LineageCount; ordinal++)
            ValidateCanonicalLineageRecord(ordinal, lineageRecords[checked((int)ordinal)], facilityAuthority, resolver);

        return new Qa04InfrastructureTailCanonicalMaterializationV1(
            facilityAuthority,
            addressState,
            Array.AsReadOnly(addressRecords),
            failureState,
            Array.AsReadOnly(failureRecords),
            lineageState,
            Array.AsReadOnly(lineageRecords),
            resolver);
    }

    public static void ValidateCanonicalAddressRecord(
        ulong localOrdinal,
        DomainRecordEnvelopeV1<InformationAddressPlaceIndexPayloadV1> record,
        Qa04FacilityServiceCanonicalMaterializationV1 facilityAuthority,
        IDomainRecordSchemaResolverV1 references)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(facilityAuthority);
        ArgumentNullException.ThrowIfNull(references);
        if (localOrdinal >= AddressCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));

        var binding = Binding("address_place_index", AddressStartOrdinal, AddressCount, localOrdinal);
        ValidateEnvelope(record, binding, InformationAddressPlaceIndexPayloadV1.PartitionId,
            "qa04.infrastructure.address-place-index-envelope-drift");
        var physicalDescriptor = Qa04ReferenceLoadV1.Record(PhysicalReferenceClass, localOrdinal);
        var expectedScope = Qa04SpatialTileScopeAuthorityV1.ScopeRef(physicalDescriptor.RegionalTileIndex);
        var payload = record.Payload;
        if (payload.PlaceRef != Qa04FacilityServiceCanonicalAuthorityV1.BuiltStructureRef(localOrdinal) ||
            payload.AddressToken != AddressToken(localOrdinal) || payload.ScopeRef != expectedScope ||
            payload.ValidFrom != 0 || payload.ValidUntil is not null || payload.Aliases.Count != 0)
            throw new InvalidDataException("qa04.infrastructure.address-place-index-payload-drift");

        new StandardDomainPayloadCodecValidatorV1().Validate(
            InformationAddressPlaceIndexPayloadV1.PartitionId, payload.ToStandardPayload(), references);
    }

    public static void ValidateCanonicalFailureRecoveryRecord(
        ulong localOrdinal,
        DomainRecordEnvelopeV1<InfrastructureFailureRecoveryPayloadV1> record,
        Qa04FacilityServiceCanonicalMaterializationV1 facilityAuthority,
        IDomainRecordSchemaResolverV1 references)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(facilityAuthority);
        ArgumentNullException.ThrowIfNull(references);
        if (localOrdinal >= FailureRecoveryCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));

        var binding = Binding("failure_recovery", FailureRecoveryStartOrdinal, FailureRecoveryCount, localOrdinal);
        ValidateEnvelope(record, binding, InfrastructureFailureRecoveryPayloadV1.PartitionId,
            "qa04.infrastructure.failure-recovery-envelope-drift");
        var payload = record.Payload;
        if (payload.SubjectRef != facilityAuthority.CanonicalServicePool[checked((int)localOrdinal)] ||
            payload.FailureKind != FailureKind || payload.SeverityPpm != 0 || payload.StartedStep != 0 ||
            payload.RecoveryProgressPpm != 1_000_000 || payload.ExpectedRestoreStep is not null ||
            payload.DependencyRefs.Count != 0 || payload.Status != Restored)
            throw new InvalidDataException("qa04.infrastructure.failure-recovery-payload-drift");

        new StandardDomainPayloadCodecValidatorV1().Validate(
            InfrastructureFailureRecoveryPayloadV1.PartitionId, payload.ToStandardPayload(), references);
    }

    public static void ValidateCanonicalLineageRecord(
        ulong localOrdinal,
        DomainRecordEnvelopeV1<InfrastructureLineagePayloadV1> record,
        Qa04FacilityServiceCanonicalMaterializationV1 facilityAuthority,
        IDomainRecordSchemaResolverV1 references)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(facilityAuthority);
        ArgumentNullException.ThrowIfNull(references);
        if (localOrdinal >= LineageCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));

        var binding = Binding("lineage", LineageStartOrdinal, LineageCount, localOrdinal);
        ValidateEnvelope(record, binding, InfrastructureLineagePayloadV1.PartitionId,
            "qa04.infrastructure.lineage-envelope-drift");
        var expectedSubject = Qa04FacilityServiceCanonicalAuthorityV1.FacilityServiceRef(localOrdinal);
        var expectedDigest = facilityAuthority.FacilityServiceRecordsByOrdinal[checked((int)localOrdinal)].Payload.CanonicalDigest();
        var payload = record.Payload;
        if (payload.SubjectRef != expectedSubject || payload.PredecessorRefs.Count != 0 ||
            payload.ChangeKind != Genesis || payload.EffectiveStep != 0 || payload.SourceDigest.Length != 32 ||
            !CryptographicOperations.FixedTimeEquals(payload.SourceDigest, expectedDigest))
            throw new InvalidDataException("qa04.infrastructure.lineage-payload-drift");

        new StandardDomainPayloadCodecValidatorV1().Validate(
            InfrastructureLineagePayloadV1.PartitionId, payload.ToStandardPayload(), references);
    }

    public static void ValidateAddressUniqueness(IEnumerable<DomainRecordEnvelopeV1<InformationAddressPlaceIndexPayloadV1>> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var ids = new HashSet<OpaqueId128>();
        var places = new HashSet<PartitionRecordRefV1>();
        var tokens = new HashSet<StableToken>();
        foreach (var record in records)
        {
            if (!ids.Add(record.RecordId) || !places.Add(record.Payload.PlaceRef) || !tokens.Add(record.Payload.AddressToken))
                throw new InvalidDataException("qa04.infrastructure.address-place-index-duplicate");
        }
    }

    public static void ValidateFailureRecoveryUniqueness(IEnumerable<DomainRecordEnvelopeV1<InfrastructureFailureRecoveryPayloadV1>> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var ids = new HashSet<OpaqueId128>();
        var subjects = new HashSet<PartitionRecordRefV1>();
        foreach (var record in records)
        {
            if (!ids.Add(record.RecordId) || !subjects.Add(record.Payload.SubjectRef))
                throw new InvalidDataException("qa04.infrastructure.failure-recovery-duplicate");
        }
    }

    public static void ValidateLineageUniqueness(IEnumerable<DomainRecordEnvelopeV1<InfrastructureLineagePayloadV1>> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var ids = new HashSet<OpaqueId128>();
        var subjects = new HashSet<PartitionRecordRefV1>();
        foreach (var record in records)
        {
            if (!ids.Add(record.RecordId) || !subjects.Add(record.Payload.SubjectRef))
                throw new InvalidDataException("qa04.infrastructure.lineage-duplicate");
        }
    }

    private static DomainRecordEnvelopeV1<InformationAddressPlaceIndexPayloadV1>[] MaterializeAddresses(
        Qa04FacilityServiceCanonicalMaterializationV1 facilityAuthority,
        IDomainRecordSchemaResolverV1 resolver,
        StandardDomainPayloadCodecValidatorV1 validator)
    {
        var identity = StandardDomainPartitionRegistry.Get(InformationAddressPlaceIndexPayloadV1.PartitionId);
        var records = new DomainRecordEnvelopeV1<InformationAddressPlaceIndexPayloadV1>[checked((int)AddressCount)];
        for (ulong ordinal = 0; ordinal < AddressCount; ordinal++)
        {
            var binding = Binding("address_place_index", AddressStartOrdinal, AddressCount, ordinal);
            var physicalDescriptor = Qa04ReferenceLoadV1.Record(PhysicalReferenceClass, ordinal);
            var payload = new InformationAddressPlaceIndexPayloadV1(
                Qa04FacilityServiceCanonicalAuthorityV1.BuiltStructureRef(ordinal),
                AddressToken(ordinal),
                Qa04SpatialTileScopeAuthorityV1.ScopeRef(physicalDescriptor.RegionalTileIndex),
                ValidFrom: 0,
                ValidUntil: null,
                Aliases: Array.Empty<StableToken>());
            validator.Validate(InformationAddressPlaceIndexPayloadV1.PartitionId, payload.ToStandardPayload(), resolver);
            records[checked((int)ordinal)] = Envelope(binding, identity, payload);
        }
        return records;
    }

    private static DomainRecordEnvelopeV1<InfrastructureFailureRecoveryPayloadV1>[] MaterializeFailureRecoveries(
        Qa04FacilityServiceCanonicalMaterializationV1 facilityAuthority,
        IDomainRecordSchemaResolverV1 resolver,
        StandardDomainPayloadCodecValidatorV1 validator)
    {
        var identity = StandardDomainPartitionRegistry.Get(InfrastructureFailureRecoveryPayloadV1.PartitionId);
        var records = new DomainRecordEnvelopeV1<InfrastructureFailureRecoveryPayloadV1>[checked((int)FailureRecoveryCount)];
        for (ulong ordinal = 0; ordinal < FailureRecoveryCount; ordinal++)
        {
            var binding = Binding("failure_recovery", FailureRecoveryStartOrdinal, FailureRecoveryCount, ordinal);
            var payload = new InfrastructureFailureRecoveryPayloadV1(
                facilityAuthority.CanonicalServicePool[checked((int)ordinal)],
                FailureKind,
                SeverityPpm: 0,
                StartedStep: 0,
                RecoveryProgressPpm: 1_000_000,
                ExpectedRestoreStep: null,
                DependencyRefs: Array.Empty<PartitionRecordRefV1>(),
                Restored);
            validator.Validate(InfrastructureFailureRecoveryPayloadV1.PartitionId, payload.ToStandardPayload(), resolver);
            records[checked((int)ordinal)] = Envelope(binding, identity, payload);
        }
        return records;
    }

    private static DomainRecordEnvelopeV1<InfrastructureLineagePayloadV1>[] MaterializeLineages(
        Qa04FacilityServiceCanonicalMaterializationV1 facilityAuthority,
        IDomainRecordSchemaResolverV1 resolver,
        StandardDomainPayloadCodecValidatorV1 validator)
    {
        var identity = StandardDomainPartitionRegistry.Get(InfrastructureLineagePayloadV1.PartitionId);
        var records = new DomainRecordEnvelopeV1<InfrastructureLineagePayloadV1>[checked((int)LineageCount)];
        for (ulong ordinal = 0; ordinal < LineageCount; ordinal++)
        {
            var binding = Binding("lineage", LineageStartOrdinal, LineageCount, ordinal);
            var payload = new InfrastructureLineagePayloadV1(
                Qa04FacilityServiceCanonicalAuthorityV1.FacilityServiceRef(ordinal),
                Array.Empty<PartitionRecordRefV1>(),
                Genesis,
                EffectiveStep: 0,
                facilityAuthority.FacilityServiceRecordsByOrdinal[checked((int)ordinal)].Payload.CanonicalDigest());
            validator.Validate(InfrastructureLineagePayloadV1.PartitionId, payload.ToStandardPayload(), resolver);
            records[checked((int)ordinal)] = Envelope(binding, identity, payload);
        }
        return records;
    }

    private static Qa04InfrastructureBindingV1 Binding(
        string expectedMaterialClass,
        ulong startOrdinal,
        ulong count,
        ulong localOrdinal)
    {
        if (localOrdinal >= count) throw new ArgumentOutOfRangeException(nameof(localOrdinal));
        var binding = Qa04InfrastructureReferenceDecompositionV1.Bind(checked(startOrdinal + localOrdinal));
        if (binding.MaterialClass.Value != expectedMaterialClass || binding.LocalOrdinal != localOrdinal || binding.UsesSpecializedIdentity)
            throw new InvalidDataException($"qa04.infrastructure.tail-binding-drift:{expectedMaterialClass}");
        return binding;
    }

    private static void ValidateSlice(string materialClass, string partitionId, ulong startOrdinal, ulong count)
    {
        var slice = Qa04InfrastructureReferenceDecompositionV1.Get(materialClass);
        if (slice.PartitionId.Value != partitionId || slice.StartOrdinal != startOrdinal || slice.Count != count || slice.UsesSpecializedIdentity)
            throw new InvalidDataException($"qa04.infrastructure.tail-slice-drift:{materialClass}");
    }

    private static void ValidateEnvelope<TPayload>(
        DomainRecordEnvelopeV1<TPayload> record,
        Qa04InfrastructureBindingV1 binding,
        string partitionId,
        string failureCode)
    {
        var identity = StandardDomainPartitionRegistry.Get(partitionId);
        if (record.RecordId != binding.Descriptor.RecordId || record.RecordSchema != identity.RecordSchema ||
            record.Revision != 1 || record.CreatedStep != 0 || record.RetiredStep is not null ||
            record.DetailLevel != binding.Descriptor.DetailLevel || record.LineageRef is not null)
            throw new InvalidDataException(failureCode);
    }

    private static DomainRecordEnvelopeV1<TPayload> Envelope<TPayload>(
        Qa04InfrastructureBindingV1 binding,
        DomainPartitionIdentityV1 identity,
        TPayload payload)
        => new(
            binding.Descriptor.RecordId,
            identity.RecordSchema,
            revision: 1,
            createdStep: 0,
            retiredStep: null,
            binding.Descriptor.DetailLevel,
            lineageRef: null,
            payload);

    private static void AddPartition<TPayload>(DomainPartitionStateV1<TPayload> partition, OverlayReferenceResolver resolver)
    {
        foreach (var record in partition.RecordsCanonical)
            resolver.Add(new PartitionRecordRefV1(partition.Identity.PartitionId.Value, record.RecordId), record.RecordSchema);
    }

    private sealed class OverlayReferenceResolver : IDomainRecordSchemaResolverV1
    {
        private readonly IDomainRecordSchemaResolverV1 _parent;
        private readonly Dictionary<PartitionRecordRefV1, SchemaRefV1> _records = new();

        public OverlayReferenceResolver(IDomainRecordSchemaResolverV1 parent)
            => _parent = parent ?? throw new ArgumentNullException(nameof(parent));

        public void Add(PartitionRecordRefV1 reference, SchemaRefV1 schema)
        {
            if (reference.RecordId.IsZero)
                throw new InvalidDataException("qa04.infrastructure.tail-reference-zero");
            if (!_records.TryAdd(reference, schema) && _records[reference] != schema)
                throw new InvalidDataException("qa04.infrastructure.tail-reference-schema-conflict");
        }

        public bool Exists(PartitionRecordRefV1 reference)
            => _records.ContainsKey(reference) || _parent.Exists(reference);

        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
            => _records.TryGetValue(reference, out schema) || _parent.TryGetRecordSchema(reference, out schema);
    }

    private static PartitionRecordRefV1 DescriptorRef(this Qa04InfrastructureBindingV1 binding)
        => new(binding.PartitionId.Value, binding.Descriptor.RecordId);
}
