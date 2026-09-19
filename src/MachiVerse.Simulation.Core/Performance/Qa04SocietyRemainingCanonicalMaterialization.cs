using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed class Qa04SocietyRemainingCanonicalMaterializationV1
{
    internal Qa04SocietyRemainingCanonicalMaterializationV1(
        Qa04FacilityServiceCanonicalMaterializationV1 facilityAuthority,
        DomainPartitionStateV1<SocietyOrganizationPayloadV1> organizations,
        DomainPartitionStateV1<SocietyBusinessProductionPayloadV1> businessProductions,
        IReadOnlyList<DomainRecordEnvelopeV1<SocietyBusinessProductionPayloadV1>> businessRecordsByOrdinal,
        DomainPartitionStateV1<SocietyLogisticsObligationPayloadV1> logisticsObligations,
        IReadOnlyList<DomainRecordEnvelopeV1<SocietyLogisticsObligationPayloadV1>> logisticsRecordsByOrdinal,
        DomainPartitionStateV1<SocietyHistoryLineagePayloadV1> historyLineages,
        IReadOnlyList<DomainRecordEnvelopeV1<SocietyHistoryLineagePayloadV1>> historyRecordsByOrdinal,
        IDomainRecordSchemaResolverV1 references)
    {
        FacilityAuthority = facilityAuthority;
        Organizations = organizations;
        BusinessProductions = businessProductions;
        BusinessRecordsByOrdinal = businessRecordsByOrdinal;
        LogisticsObligations = logisticsObligations;
        LogisticsRecordsByOrdinal = logisticsRecordsByOrdinal;
        HistoryLineages = historyLineages;
        HistoryRecordsByOrdinal = historyRecordsByOrdinal;
        References = references;
    }

    public Qa04FacilityServiceCanonicalMaterializationV1 FacilityAuthority { get; }
    public DomainPartitionStateV1<SocietyOrganizationPayloadV1> Organizations { get; }
    public DomainPartitionStateV1<SocietyBusinessProductionPayloadV1> BusinessProductions { get; }
    public IReadOnlyList<DomainRecordEnvelopeV1<SocietyBusinessProductionPayloadV1>> BusinessRecordsByOrdinal { get; }
    public DomainPartitionStateV1<SocietyLogisticsObligationPayloadV1> LogisticsObligations { get; }
    public IReadOnlyList<DomainRecordEnvelopeV1<SocietyLogisticsObligationPayloadV1>> LogisticsRecordsByOrdinal { get; }
    public DomainPartitionStateV1<SocietyHistoryLineagePayloadV1> HistoryLineages { get; }
    public IReadOnlyList<DomainRecordEnvelopeV1<SocietyHistoryLineagePayloadV1>> HistoryRecordsByOrdinal { get; }
    public IDomainRecordSchemaResolverV1 References { get; }
    public ulong MaterializedRecordCount => checked(BusinessProductions.ItemCount + LogisticsObligations.ItemCount + HistoryLineages.ItemCount);
}

/// <summary>
/// Benchmark-only production authority for the final 89,800 Society records in perf.reference.v1.
/// It deliberately reuses only already-proven Organization, Physical D0 and BuiltStructure records.
/// </summary>
public static class Qa04SocietyRemainingCanonicalAuthorityV1
{
    public const ulong BusinessCount = 30_000;
    public const ulong LogisticsCount = 40_000;
    public const ulong HistoryCount = 19_800;
    public const ulong CanonicalCount = BusinessCount + LogisticsCount + HistoryCount;

    public const ulong BusinessStartOrdinal = 1_400_200;
    public const ulong LogisticsStartOrdinal = 1_430_200;
    public const ulong HistoryStartOrdinal = 1_580_200;

    public static readonly StableToken RecipeToken = new("perf.reference-production-plan");
    public static readonly StableToken Planned = new("planned");
    public static readonly StableToken HistoryKind = new("perf.genesis-production");

    public static void ValidateCanonicalContract()
    {
        Qa04SocietyGovernanceReferenceDecompositionV1.ValidateCanonicalContract();
        Qa04SocietyOrganizationResolvedMaterializerV1.ValidateCanonicalContract();
        Qa04FacilityServiceCanonicalAuthorityV1.ValidateCanonicalContract();

        ValidateSlice(SocietyBusinessProductionPayloadV1.PartitionId, BusinessStartOrdinal, BusinessCount);
        ValidateSlice(SocietyLogisticsObligationPayloadV1.PartitionId, LogisticsStartOrdinal, LogisticsCount);
        ValidateSlice(SocietyHistoryLineagePayloadV1.PartitionId, HistoryStartOrdinal, HistoryCount);

        if (CanonicalCount != 89_800 ||
            BusinessCount > 50_000 || LogisticsCount > Qa04PhysicalD0PropertyAssetSupportCanonicalAuthorityV1.CanonicalPhysicalCount ||
            HistoryCount > BusinessCount || Qa04SocietyOrganizationResolvedMaterializerV1.CanonicalCount != 10_000 ||
            Qa04FacilityServiceCanonicalAuthorityV1.CanonicalCount != 15_000)
            throw new InvalidDataException("qa04.society.remaining-cardinality-drift");
    }

    public static PartitionRecordRefV1 OrganizationRef(ulong ordinal)
    {
        var local = ordinal % Qa04SocietyOrganizationResolvedMaterializerV1.CanonicalCount;
        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(SocietyOrganizationPayloadV1.PartitionId);
        var binding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(checked(slice.StartOrdinal + local));
        if (binding.PartitionId.Value != SocietyOrganizationPayloadV1.PartitionId || binding.PartitionLocalOrdinal != local || binding.UsesSpecializedIdentity)
            throw new InvalidDataException("qa04.society.remaining-organization-ref-drift");
        return new PartitionRecordRefV1(binding.PartitionId, binding.Descriptor.RecordId);
    }

    public static PartitionRecordRefV1 BusinessRef(ulong localOrdinal)
        => RefFor(SocietyBusinessProductionPayloadV1.PartitionId, BusinessStartOrdinal, BusinessCount, localOrdinal);

    public static PartitionRecordRefV1 LogisticsRef(ulong localOrdinal)
        => RefFor(SocietyLogisticsObligationPayloadV1.PartitionId, LogisticsStartOrdinal, LogisticsCount, localOrdinal);

    public static PartitionRecordRefV1 HistoryRef(ulong localOrdinal)
        => RefFor(SocietyHistoryLineagePayloadV1.PartitionId, HistoryStartOrdinal, HistoryCount, localOrdinal);

    public static Qa04SocietyRemainingCanonicalMaterializationV1 MaterializeCanonical()
        => MaterializeCanonical(Qa04FacilityServiceCanonicalAuthorityV1.MaterializeCanonical());

    public static Qa04SocietyRemainingCanonicalMaterializationV1 MaterializeCanonical(
        Qa04FacilityServiceCanonicalMaterializationV1 facilityAuthority)
    {
        ArgumentNullException.ThrowIfNull(facilityAuthority);
        ValidateCanonicalContract();

        if (facilityAuthority.PhysicalAuthority.PhysicalRecordsByOrdinal.Count < checked((int)LogisticsCount) ||
            facilityAuthority.BuiltStructures.ItemCount != Qa04FacilityServiceCanonicalAuthorityV1.CanonicalCount)
            throw new InvalidDataException("qa04.society.remaining-upstream-materialization-drift");

        var organizationRecords = Qa04SocietyOrganizationResolvedMaterializerV1
            .MaterializeResolved(static _ => Qa04SocietyGovernanceCanonicalAuthorityV1.OrganizationClass)
            .ToArray();
        if ((ulong)organizationRecords.Length != Qa04SocietyOrganizationResolvedMaterializerV1.CanonicalCount)
            throw new InvalidDataException("qa04.society.remaining-organization-count-drift");
        var organizations = new DomainPartitionStateV1<SocietyOrganizationPayloadV1>(
            StandardDomainPartitionRegistry.Get(SocietyOrganizationPayloadV1.PartitionId), organizationRecords);

        var resolver = new OverlayReferenceResolver(facilityAuthority.References);
        AddPartition(organizations, resolver);
        for (ulong ordinal = 0; ordinal < LogisticsCount; ordinal++)
        {
            var physical = facilityAuthority.PhysicalAuthority.PhysicalRecordsByOrdinal[checked((int)ordinal)];
            resolver.Add(
                new PartitionRecordRefV1(PhysicalPresencePayloadV1.PartitionId, physical.Presence.RecordId),
                physical.Presence.RecordSchema);
        }

        var validator = new StandardDomainPayloadCodecValidatorV1();
        var businessRecords = MaterializeBusinesses(resolver, validator);
        var businesses = new DomainPartitionStateV1<SocietyBusinessProductionPayloadV1>(
            StandardDomainPartitionRegistry.Get(SocietyBusinessProductionPayloadV1.PartitionId), businessRecords);
        AddPartition(businesses, resolver);

        var logisticsRecords = MaterializeLogistics(facilityAuthority, resolver, validator);
        var logistics = new DomainPartitionStateV1<SocietyLogisticsObligationPayloadV1>(
            StandardDomainPartitionRegistry.Get(SocietyLogisticsObligationPayloadV1.PartitionId), logisticsRecords);
        AddPartition(logistics, resolver);

        var historyRecords = MaterializeHistory(businessRecords, resolver, validator);
        var history = new DomainPartitionStateV1<SocietyHistoryLineagePayloadV1>(
            StandardDomainPartitionRegistry.Get(SocietyHistoryLineagePayloadV1.PartitionId), historyRecords);
        AddPartition(history, resolver);

        ValidateBusinessUniqueness(businessRecords);
        ValidateLogisticsUniqueness(logisticsRecords);
        ValidateHistoryUniqueness(historyRecords);

        for (ulong ordinal = 0; ordinal < BusinessCount; ordinal++)
            ValidateCanonicalBusinessRecord(ordinal, businessRecords[checked((int)ordinal)], resolver);
        for (ulong ordinal = 0; ordinal < LogisticsCount; ordinal++)
            ValidateCanonicalLogisticsRecord(ordinal, logisticsRecords[checked((int)ordinal)], facilityAuthority, resolver);
        for (ulong ordinal = 0; ordinal < HistoryCount; ordinal++)
            ValidateCanonicalHistoryRecord(ordinal, historyRecords[checked((int)ordinal)], businessRecords, resolver);

        var result = new Qa04SocietyRemainingCanonicalMaterializationV1(
            facilityAuthority,
            organizations,
            businesses,
            Array.AsReadOnly(businessRecords),
            logistics,
            Array.AsReadOnly(logisticsRecords),
            history,
            Array.AsReadOnly(historyRecords),
            resolver);
        if (result.MaterializedRecordCount != CanonicalCount)
            throw new InvalidDataException("qa04.society.remaining-materialized-count-drift");
        return result;
    }

    public static void ValidateCanonicalBusinessRecord(
        ulong localOrdinal,
        DomainRecordEnvelopeV1<SocietyBusinessProductionPayloadV1> record,
        IDomainRecordSchemaResolverV1 references)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(references);
        var binding = Binding(SocietyBusinessProductionPayloadV1.PartitionId, BusinessStartOrdinal, BusinessCount, localOrdinal);
        ValidateEnvelope(record, binding, SocietyBusinessProductionPayloadV1.PartitionId, "qa04.society.business-production-envelope-drift");
        var payload = record.Payload;
        if (payload.OrganizationRef != OrganizationRef(localOrdinal) || payload.RecipeToken != RecipeToken ||
            payload.PlannedQuantity != 1 || payload.CompletedQuantity != 0 || payload.InputRefs.Count != 0 || payload.OutputRefs.Count != 0 ||
            payload.WorkRequired != 1 || payload.EnergyRequiredMj != 0 || payload.Status != Planned)
            throw new InvalidDataException("qa04.society.business-production-payload-drift");
        new StandardDomainPayloadCodecValidatorV1().Validate(
            SocietyBusinessProductionPayloadV1.PartitionId, payload.ToStandardPayload(), references);
    }

    public static void ValidateCanonicalLogisticsRecord(
        ulong localOrdinal,
        DomainRecordEnvelopeV1<SocietyLogisticsObligationPayloadV1> record,
        Qa04FacilityServiceCanonicalMaterializationV1 facilityAuthority,
        IDomainRecordSchemaResolverV1 references)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(facilityAuthority);
        ArgumentNullException.ThrowIfNull(references);
        var binding = Binding(SocietyLogisticsObligationPayloadV1.PartitionId, LogisticsStartOrdinal, LogisticsCount, localOrdinal);
        ValidateEnvelope(record, binding, SocietyLogisticsObligationPayloadV1.PartitionId, "qa04.society.logistics-envelope-drift");

        var physical = facilityAuthority.PhysicalAuthority.PhysicalRecordsByOrdinal[checked((int)localOrdinal)];
        var cargo = new PartitionRecordRefV1(PhysicalPresencePayloadV1.PartitionId, physical.Presence.RecordId);
        var origin = Qa04FacilityServiceCanonicalAuthorityV1.BuiltStructureRef(localOrdinal % Qa04FacilityServiceCanonicalAuthorityV1.CanonicalCount);
        var destination = Qa04FacilityServiceCanonicalAuthorityV1.BuiltStructureRef((localOrdinal + 1) % Qa04FacilityServiceCanonicalAuthorityV1.CanonicalCount);
        var payload = record.Payload;
        if (payload.ShipperRef != OrganizationRef(localOrdinal) || payload.ConsigneeRef != OrganizationRef(localOrdinal + 1) ||
            payload.CargoRefs.Count != 1 || payload.CargoRefs[0] != cargo || payload.Quantity != 1 ||
            payload.OriginRef != origin || payload.DestinationRef != destination || payload.OriginRef == payload.DestinationRef ||
            payload.DueStep != 30 || payload.Status != Planned || payload.CarrierRef != OrganizationRef(localOrdinal + 2))
            throw new InvalidDataException("qa04.society.logistics-payload-drift");
        new StandardDomainPayloadCodecValidatorV1().Validate(
            SocietyLogisticsObligationPayloadV1.PartitionId, payload.ToStandardPayload(), references);
    }

    public static void ValidateCanonicalHistoryRecord(
        ulong localOrdinal,
        DomainRecordEnvelopeV1<SocietyHistoryLineagePayloadV1> record,
        IReadOnlyList<DomainRecordEnvelopeV1<SocietyBusinessProductionPayloadV1>> businessRecords,
        IDomainRecordSchemaResolverV1 references)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(businessRecords);
        ArgumentNullException.ThrowIfNull(references);
        var binding = Binding(SocietyHistoryLineagePayloadV1.PartitionId, HistoryStartOrdinal, HistoryCount, localOrdinal);
        ValidateEnvelope(record, binding, SocietyHistoryLineagePayloadV1.PartitionId, "qa04.society.history-lineage-envelope-drift");
        if (businessRecords.Count < checked((int)HistoryCount))
            throw new InvalidDataException("qa04.society.history-lineage-business-source-count");
        var expectedDigest = businessRecords[checked((int)localOrdinal)].Payload.CanonicalDigest();
        var payload = record.Payload;
        if (payload.SubjectRef != BusinessRef(localOrdinal) || payload.HistoryKind != HistoryKind || payload.ParentRefs.Count != 0 ||
            payload.BasisStep != 0 || payload.CausalityDigest.Length != 32 ||
            !CryptographicOperations.FixedTimeEquals(payload.CausalityDigest, expectedDigest))
            throw new InvalidDataException("qa04.society.history-lineage-payload-drift");
        new StandardDomainPayloadCodecValidatorV1().Validate(
            SocietyHistoryLineagePayloadV1.PartitionId, payload.ToStandardPayload(), references);
    }

    public static void ValidateBusinessUniqueness(IEnumerable<DomainRecordEnvelopeV1<SocietyBusinessProductionPayloadV1>> records)
    {
        var ids = new HashSet<OpaqueId128>();
        foreach (var record in records)
            if (!ids.Add(record.RecordId)) throw new InvalidDataException("qa04.society.business-production-id-duplicate");
    }

    public static void ValidateLogisticsUniqueness(IEnumerable<DomainRecordEnvelopeV1<SocietyLogisticsObligationPayloadV1>> records)
    {
        var ids = new HashSet<OpaqueId128>();
        var cargo = new HashSet<PartitionRecordRefV1>();
        foreach (var record in records)
        {
            if (!ids.Add(record.RecordId)) throw new InvalidDataException("qa04.society.logistics-id-duplicate");
            if (record.Payload.CargoRefs.Count != 1 || !cargo.Add(record.Payload.CargoRefs[0]))
                throw new InvalidDataException("qa04.society.logistics-cargo-relation-duplicate");
        }
    }

    public static void ValidateHistoryUniqueness(IEnumerable<DomainRecordEnvelopeV1<SocietyHistoryLineagePayloadV1>> records)
    {
        var ids = new HashSet<OpaqueId128>();
        var subjects = new HashSet<PartitionRecordRefV1>();
        foreach (var record in records)
            if (!ids.Add(record.RecordId) || !subjects.Add(record.Payload.SubjectRef))
                throw new InvalidDataException("qa04.society.history-lineage-duplicate");
    }

    private static DomainRecordEnvelopeV1<SocietyBusinessProductionPayloadV1>[] MaterializeBusinesses(
        IDomainRecordSchemaResolverV1 resolver,
        StandardDomainPayloadCodecValidatorV1 validator)
    {
        var identity = StandardDomainPartitionRegistry.Get(SocietyBusinessProductionPayloadV1.PartitionId);
        var records = new DomainRecordEnvelopeV1<SocietyBusinessProductionPayloadV1>[checked((int)BusinessCount)];
        for (ulong ordinal = 0; ordinal < BusinessCount; ordinal++)
        {
            var binding = Binding(SocietyBusinessProductionPayloadV1.PartitionId, BusinessStartOrdinal, BusinessCount, ordinal);
            var payload = new SocietyBusinessProductionPayloadV1(
                OrganizationRef(ordinal), RecipeToken, PlannedQuantity: 1, CompletedQuantity: 0,
                InputRefs: Array.Empty<PartitionRecordRefV1>(), OutputRefs: Array.Empty<PartitionRecordRefV1>(),
                WorkRequired: 1, EnergyRequiredMj: 0, Planned);
            validator.Validate(SocietyBusinessProductionPayloadV1.PartitionId, payload.ToStandardPayload(), resolver);
            records[checked((int)ordinal)] = Envelope(binding, identity, payload);
        }
        return records;
    }

    private static DomainRecordEnvelopeV1<SocietyLogisticsObligationPayloadV1>[] MaterializeLogistics(
        Qa04FacilityServiceCanonicalMaterializationV1 facilityAuthority,
        IDomainRecordSchemaResolverV1 resolver,
        StandardDomainPayloadCodecValidatorV1 validator)
    {
        var identity = StandardDomainPartitionRegistry.Get(SocietyLogisticsObligationPayloadV1.PartitionId);
        var records = new DomainRecordEnvelopeV1<SocietyLogisticsObligationPayloadV1>[checked((int)LogisticsCount)];
        for (ulong ordinal = 0; ordinal < LogisticsCount; ordinal++)
        {
            var binding = Binding(SocietyLogisticsObligationPayloadV1.PartitionId, LogisticsStartOrdinal, LogisticsCount, ordinal);
            var physical = facilityAuthority.PhysicalAuthority.PhysicalRecordsByOrdinal[checked((int)ordinal)];
            var cargo = new PartitionRecordRefV1(PhysicalPresencePayloadV1.PartitionId, physical.Presence.RecordId);
            var payload = new SocietyLogisticsObligationPayloadV1(
                OrganizationRef(ordinal),
                OrganizationRef(ordinal + 1),
                new[] { cargo },
                Quantity: 1,
                Qa04FacilityServiceCanonicalAuthorityV1.BuiltStructureRef(ordinal % Qa04FacilityServiceCanonicalAuthorityV1.CanonicalCount),
                Qa04FacilityServiceCanonicalAuthorityV1.BuiltStructureRef((ordinal + 1) % Qa04FacilityServiceCanonicalAuthorityV1.CanonicalCount),
                DueStep: 30,
                Planned,
                CarrierRef: OrganizationRef(ordinal + 2));
            validator.Validate(SocietyLogisticsObligationPayloadV1.PartitionId, payload.ToStandardPayload(), resolver);
            records[checked((int)ordinal)] = Envelope(binding, identity, payload);
        }
        return records;
    }

    private static DomainRecordEnvelopeV1<SocietyHistoryLineagePayloadV1>[] MaterializeHistory(
        IReadOnlyList<DomainRecordEnvelopeV1<SocietyBusinessProductionPayloadV1>> businessRecords,
        IDomainRecordSchemaResolverV1 resolver,
        StandardDomainPayloadCodecValidatorV1 validator)
    {
        var identity = StandardDomainPartitionRegistry.Get(SocietyHistoryLineagePayloadV1.PartitionId);
        var records = new DomainRecordEnvelopeV1<SocietyHistoryLineagePayloadV1>[checked((int)HistoryCount)];
        for (ulong ordinal = 0; ordinal < HistoryCount; ordinal++)
        {
            var binding = Binding(SocietyHistoryLineagePayloadV1.PartitionId, HistoryStartOrdinal, HistoryCount, ordinal);
            var payload = new SocietyHistoryLineagePayloadV1(
                BusinessRef(ordinal), HistoryKind, Array.Empty<PartitionRecordRefV1>(), BasisStep: 0,
                businessRecords[checked((int)ordinal)].Payload.CanonicalDigest());
            validator.Validate(SocietyHistoryLineagePayloadV1.PartitionId, payload.ToStandardPayload(), resolver);
            records[checked((int)ordinal)] = Envelope(binding, identity, payload);
        }
        return records;
    }

    private static Qa04SocietyGovernanceBindingV1 Binding(string partitionId, ulong start, ulong count, ulong localOrdinal)
    {
        if (localOrdinal >= count) throw new ArgumentOutOfRangeException(nameof(localOrdinal));
        var binding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(checked(start + localOrdinal));
        if (binding.PartitionId.Value != partitionId || binding.PartitionLocalOrdinal != localOrdinal || binding.UsesSpecializedIdentity)
            throw new InvalidDataException($"qa04.society.remaining-binding-drift:{partitionId}");
        return binding;
    }

    private static PartitionRecordRefV1 RefFor(string partitionId, ulong start, ulong count, ulong localOrdinal)
    {
        var binding = Binding(partitionId, start, count, localOrdinal);
        return new PartitionRecordRefV1(binding.PartitionId, binding.Descriptor.RecordId);
    }

    private static void ValidateSlice(string partitionId, ulong start, ulong count)
    {
        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(partitionId);
        if (slice.StartOrdinal != start || slice.Count != count || slice.UsesSpecializedIdentity)
            throw new InvalidDataException($"qa04.society.remaining-slice-drift:{partitionId}");
    }

    private static void ValidateEnvelope<TPayload>(
        DomainRecordEnvelopeV1<TPayload> record,
        Qa04SocietyGovernanceBindingV1 binding,
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
        Qa04SocietyGovernanceBindingV1 binding,
        DomainPartitionIdentityV1 identity,
        TPayload payload)
        => new(binding.Descriptor.RecordId, identity.RecordSchema, 1, 0, null, binding.Descriptor.DetailLevel, null, payload);

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
            if (reference.RecordId.IsZero) throw new InvalidDataException("qa04.society.remaining-reference-zero");
            if (!_records.TryAdd(reference, schema) && _records[reference] != schema)
                throw new InvalidDataException("qa04.society.remaining-reference-schema-conflict");
        }

        public bool Exists(PartitionRecordRefV1 reference) => _records.ContainsKey(reference) || _parent.Exists(reference);
        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
            => _records.TryGetValue(reference, out schema) || _parent.TryGetRecordSchema(reference, out schema);
    }
}
