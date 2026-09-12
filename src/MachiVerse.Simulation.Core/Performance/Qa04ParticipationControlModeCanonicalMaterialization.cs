using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Participation;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed class Qa04ParticipationControlModeCanonicalMaterializationV1
{
    internal Qa04ParticipationControlModeCanonicalMaterializationV1(
        DomainPartitionStateV1<ParticipationControlModePayloadV1> partition,
        IDomainRecordSchemaResolverV1 references,
        IReadOnlyList<OpaqueId128> transactionParticipantRecordIds)
    {
        Partition = partition;
        References = references;
        TransactionParticipantRecordIds = transactionParticipantRecordIds;
    }

    public DomainPartitionStateV1<ParticipationControlModePayloadV1> Partition { get; }
    public IDomainRecordSchemaResolverV1 References { get; }
    public IReadOnlyList<OpaqueId128> TransactionParticipantRecordIds { get; }
    public ulong MaterializedRecordCount => Partition.ItemCount;
}

/// <summary>
/// Production-path materialization for the approved perf.reference.v1 participation.control_mode
/// authority. Every canonical Resident receives exactly one Step-0 autonomous record with no active
/// binding. Record identity, mode-token spelling, generation, and per-record DetailLevel are fixed by
/// phase4-alpha11-participation-control-mode-authority-audit.md.
/// </summary>
public static class Qa04ParticipationControlModeCanonicalAuthorityV1
{
    public const ulong CanonicalCount = 1_000_000;
    public const ulong InitialEffectiveFrom = 0;
    public const uint InitialInputAuthorityGeneration = 0;

    public static readonly StableToken Autonomous = new("autonomous");
    public static readonly StableToken DiverControlAvailable = new("diver-control-available");
    public static readonly StableToken DiverAbsentPolicy = new("diver-absent-policy");
    public static readonly StableToken BoundResidentDeceased = new("bound-resident-deceased");

    private static readonly StableToken ParticipationDomain = new("participation");
    private static readonly StableToken CreationKind = new("perf.control-mode");

    public static void ValidateCanonicalContract()
    {
        Qa04ReferenceLoadV1.ValidateCanonicalContract();
        Qa04ReferenceWorldMaterializerV1.ValidateCanonicalContract();

        var referenceClass = Qa04ReferenceLoadV1.RecordClasses.SingleOrDefault(
            static item => item.ClassToken.Value == ParticipationControlModePayloadV1.PartitionId)
            ?? throw new InvalidDataException("qa04.participation.control-mode-reference-class-missing");
        if (referenceClass.Count != CanonicalCount ||
            CanonicalCount != Qa04ReferenceWorldMaterializerV1.CanonicalResidentCount)
            throw new InvalidDataException("qa04.participation.control-mode-count-drift");

        var identity = StandardDomainPartitionRegistry.Get(ParticipationControlModePayloadV1.PartitionId);
        if (identity.OwnerDomain.Value != ParticipationDomain.Value)
            throw new InvalidDataException("qa04.participation.control-mode-owner-drift");

        var expectedTokens = new[]
        {
            Autonomous.Value,
            DiverControlAvailable.Value,
            DiverAbsentPolicy.Value,
            BoundResidentDeceased.Value,
        };
        var actualTokens = Enum.GetValues<ResidentControlModeV1>()
            .Select(ModeToken)
            .Select(static token => token.Value)
            .ToArray();
        if (!actualTokens.SequenceEqual(expectedTokens, StringComparer.Ordinal) ||
            actualTokens.Distinct(StringComparer.Ordinal).Count() != 4)
            throw new InvalidDataException("qa04.participation.control-mode-token-vocabulary-drift");

        var firstId = RecordId(0);
        var lastId = RecordId(CanonicalCount - 1);
        if (firstId.IsZero || lastId.IsZero || firstId == lastId)
            throw new InvalidDataException("qa04.participation.control-mode-record-identity-drift");
    }

    public static OpaqueId128 RecordId(ulong residentOrdinal)
    {
        if (residentOrdinal >= CanonicalCount) throw new ArgumentOutOfRangeException(nameof(residentOrdinal));
        return DerivedIdentity.DeriveEntityId(
            Qa04ReferenceLoadV1.WorldId,
            creationStep: 0,
            ParticipationDomain,
            OpaqueId128.Zero,
            CreationKind,
            residentOrdinal);
    }

    public static StableToken ModeToken(ResidentControlModeV1 mode) => mode switch
    {
        ResidentControlModeV1.Autonomous => Autonomous,
        ResidentControlModeV1.DiverControlAvailable => DiverControlAvailable,
        ResidentControlModeV1.DiverAbsentPolicy => DiverAbsentPolicy,
        ResidentControlModeV1.BoundResidentDeceased => BoundResidentDeceased,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    public static Qa04ParticipationControlModeCanonicalMaterializationV1 MaterializeCanonical()
    {
        ValidateCanonicalContract();

        var identity = StandardDomainPartitionRegistry.Get(ParticipationControlModePayloadV1.PartitionId);
        var references = new CanonicalReferenceResolver();
        var validator = new StandardDomainPayloadCodecValidatorV1();
        var records = new DomainRecordEnvelopeV1<ParticipationControlModePayloadV1>[checked((int)CanonicalCount)];
        var residentRefs = new HashSet<PartitionRecordRefV1>();
        var recordIds = new HashSet<OpaqueId128>();

        for (ulong ordinal = 0; ordinal < CanonicalCount; ordinal++)
        {
            var resident = Qa04ReferenceWorldMaterializerV1.CreateResidentRecord(ordinal);
            var residentRef = new PartitionRecordRefV1(ResidentIdentityLifecyclePayloadV1.PartitionId, resident.RecordId);
            references.Add(residentRef, resident.RecordSchema);
            if (!residentRefs.Add(residentRef))
                throw new InvalidDataException("qa04.participation.control-mode-resident-duplicate");

            var payload = new ParticipationControlModePayloadV1(
                residentRef,
                BindingRef: null,
                Autonomous,
                InitialEffectiveFrom,
                InitialInputAuthorityGeneration);
            validator.Validate(ParticipationControlModePayloadV1.PartitionId, payload.ToStandardPayload(), references);

            var recordId = RecordId(ordinal);
            if (!recordIds.Add(recordId) || recordId == resident.RecordId)
                throw new InvalidDataException("qa04.participation.control-mode-record-id-duplicate-or-reused");

            var record = new DomainRecordEnvelopeV1<ParticipationControlModePayloadV1>(
                recordId,
                identity.RecordSchema,
                revision: 1,
                createdStep: 0,
                retiredStep: null,
                detailLevel: Qa04ReferenceLoadV1.ResidentDetailLevel(ordinal),
                lineageRef: null,
                payload);
            ValidateCanonicalRecord(ordinal, record, references);
            records[checked((int)ordinal)] = record;
            references.Add(
                new PartitionRecordRefV1(ParticipationControlModePayloadV1.PartitionId, recordId),
                record.RecordSchema);
        }

        if (residentRefs.Count != checked((int)CanonicalCount) || recordIds.Count != checked((int)CanonicalCount))
            throw new InvalidDataException("qa04.participation.control-mode-uniqueness-drift");

        var partition = new DomainPartitionStateV1<ParticipationControlModePayloadV1>(identity, records);
        if (partition.ItemCount != CanonicalCount)
            throw new InvalidDataException("qa04.participation.control-mode-partition-count-drift");

        ValidateResidentUniqueness(partition.RecordsCanonical);
        var participantIds = partition.RecordsCanonical.Select(static record => record.RecordId).ToArray();
        OpaqueId128? previous = null;
        foreach (var id in participantIds)
        {
            if (previous is { } prior && prior.CompareTo(id) >= 0)
                throw new InvalidDataException("qa04.participation.control-mode-participant-pool-order");
            previous = id;
        }

        return new Qa04ParticipationControlModeCanonicalMaterializationV1(
            partition,
            references,
            Array.AsReadOnly(participantIds));
    }

    public static void ValidateCanonicalRecord(
        ulong residentOrdinal,
        DomainRecordEnvelopeV1<ParticipationControlModePayloadV1> record,
        IDomainRecordSchemaResolverV1 references)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(references);
        if (residentOrdinal >= CanonicalCount) throw new ArgumentOutOfRangeException(nameof(residentOrdinal));

        var expectedResident = Qa04ReferenceWorldMaterializerV1.CreateResidentRecord(residentOrdinal);
        var expectedResidentRef = new PartitionRecordRefV1(
            ResidentIdentityLifecyclePayloadV1.PartitionId,
            expectedResident.RecordId);
        var identity = StandardDomainPartitionRegistry.Get(ParticipationControlModePayloadV1.PartitionId);

        if (record.RecordId != RecordId(residentOrdinal) ||
            record.RecordSchema != identity.RecordSchema ||
            record.Revision != 1 ||
            record.CreatedStep != 0 ||
            record.RetiredStep is not null ||
            record.DetailLevel != Qa04ReferenceLoadV1.ResidentDetailLevel(residentOrdinal) ||
            record.LineageRef is not null)
            throw new InvalidDataException("qa04.participation.control-mode-envelope-drift");

        var payload = record.Payload;
        if (payload.ResidentRef != expectedResidentRef ||
            payload.BindingRef is not null ||
            payload.Mode != Autonomous ||
            payload.EffectiveFrom != InitialEffectiveFrom ||
            payload.InputAuthorityGeneration != InitialInputAuthorityGeneration)
            throw new InvalidDataException("qa04.participation.control-mode-genesis-drift");

        new StandardDomainPayloadCodecValidatorV1().Validate(
            ParticipationControlModePayloadV1.PartitionId,
            payload.ToStandardPayload(),
            references);
    }

    public static void ValidateResidentUniqueness(
        IEnumerable<DomainRecordEnvelopeV1<ParticipationControlModePayloadV1>> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var residents = new HashSet<PartitionRecordRefV1>();
        var recordIds = new HashSet<OpaqueId128>();
        foreach (var record in records)
        {
            if (!recordIds.Add(record.RecordId))
                throw new InvalidDataException("qa04.participation.control-mode-record-id-duplicate");
            if (!residents.Add(record.Payload.ResidentRef))
                throw new InvalidDataException("qa04.participation.control-mode-resident-duplicate");
        }
    }

    private sealed class CanonicalReferenceResolver : IDomainRecordSchemaResolverV1
    {
        private readonly Dictionary<PartitionRecordRefV1, SchemaRefV1> _records = new();

        public void Add(PartitionRecordRefV1 reference, SchemaRefV1 schema)
        {
            if (reference.RecordId.IsZero)
                throw new InvalidDataException("qa04.participation.control-mode-reference-zero");
            if (!_records.TryAdd(reference, schema) && _records[reference] != schema)
                throw new InvalidDataException("qa04.participation.control-mode-reference-schema-conflict");
        }

        public bool Exists(PartitionRecordRefV1 reference) => _records.ContainsKey(reference);

        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
            => _records.TryGetValue(reference, out schema);
    }
}
