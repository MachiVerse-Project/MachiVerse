using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed class Qa04InformationDeliveryCanonicalMaterializationV1
{
    internal Qa04InformationDeliveryCanonicalMaterializationV1(
        Qa04SocietyGovernanceCanonicalMaterializationV1 societyAuthority,
        Qa04InfrastructureServiceQueueCanonicalMaterializationV1 serviceAuthority,
        DomainPartitionStateV1<InformationDeliveryPayloadV1> partition,
        IReadOnlyList<DomainRecordEnvelopeV1<InformationDeliveryPayloadV1>> recordsByOrdinal,
        IReadOnlyList<InformationDeliveryV1> runtimeDeliveries,
        IDomainRecordSchemaResolverV1 references)
    {
        SocietyAuthority = societyAuthority;
        ServiceAuthority = serviceAuthority;
        Partition = partition;
        RecordsByOrdinal = recordsByOrdinal;
        RuntimeDeliveries = runtimeDeliveries;
        References = references;
    }

    public Qa04SocietyGovernanceCanonicalMaterializationV1 SocietyAuthority { get; }
    public Qa04InfrastructureServiceQueueCanonicalMaterializationV1 ServiceAuthority { get; }
    public DomainPartitionStateV1<InformationDeliveryPayloadV1> Partition { get; }
    public IReadOnlyList<DomainRecordEnvelopeV1<InformationDeliveryPayloadV1>> RecordsByOrdinal { get; }
    public IReadOnlyList<InformationDeliveryV1> RuntimeDeliveries { get; }
    public IDomainRecordSchemaResolverV1 References { get; }
    public ulong MaterializedRecordCount => Partition.ItemCount;
}

/// <summary>
/// Production-path materialization for the approved perf.reference.v1 information.delivery
/// 20,000-record package. All content, sender, recipient and channel references bind to existing
/// production authority; no synthetic placeholder identity participates in the package.
/// </summary>
public static class Qa04InformationDeliveryCanonicalAuthorityV1
{
    public const ulong CanonicalCount = 20_000;
    public const ulong CanonicalStartOrdinal = 445_100;
    public const ulong CommunicationServiceCount = 10_000;
    public const ulong EligibleStep = 0;
    public const int Priority = 0;

    public static readonly StableToken Queued = new("queued");

    private static readonly StableToken InfrastructureDomain = new("infrastructure_information");

    public static void ValidateCanonicalContract()
    {
        Qa04InfrastructureReferenceDecompositionV1.ValidateCanonicalContract();
        Qa04SocietyGovernanceCanonicalMaterializerV1.ValidateCanonicalContract();
        Qa04InfrastructureServiceQueueCanonicalMaterializerV1.ValidateCanonicalContract();
        Qa04ReferenceWorldMaterializerV1.ValidateCanonicalContract();

        var slice = Qa04InfrastructureReferenceDecompositionV1.Get("information_delivery");
        if (slice.PartitionId.Value != InformationDeliveryPayloadV1.PartitionId ||
            slice.StartOrdinal != CanonicalStartOrdinal ||
            slice.Count != CanonicalCount ||
            slice.UsesSpecializedIdentity)
            throw new InvalidDataException("qa04.information.delivery-slice-drift");

        if (Qa04SocietyInformationClaimResolvedMaterializerV1.CanonicalCount < CanonicalCount ||
            Qa04InfrastructureServiceQueueCanonicalMaterializerV1.CommunicationServiceCount != CommunicationServiceCount ||
            Qa04ReferenceWorldMaterializerV1.CanonicalResidentCount <= CanonicalCount ||
            EligibleStep != 0 || Priority != 0 || Queued.Value != "queued")
            throw new InvalidDataException("qa04.information.delivery-cardinality-or-genesis-drift");

        var identity = StandardDomainPartitionRegistry.Get(InformationDeliveryPayloadV1.PartitionId);
        if (identity.OwnerDomain != InfrastructureDomain)
            throw new InvalidDataException("qa04.information.delivery-owner-drift");

        var first = RecordId(0);
        var last = RecordId(CanonicalCount - 1);
        if (first.IsZero || last.IsZero || first == last)
            throw new InvalidDataException("qa04.information.delivery-identity-drift");
    }

    public static OpaqueId128 RecordId(ulong localOrdinal)
        => Binding(localOrdinal).Descriptor.RecordId;

    public static Qa04InformationDeliveryCanonicalMaterializationV1 MaterializeCanonical()
        => MaterializeCanonical(
            Qa04SocietyGovernanceCanonicalMaterializerV1.MaterializeCanonical(),
            Qa04InfrastructureServiceQueueCanonicalMaterializerV1.MaterializeCanonical());

    public static Qa04InformationDeliveryCanonicalMaterializationV1 MaterializeCanonical(
        Qa04SocietyGovernanceCanonicalMaterializationV1 societyAuthority,
        Qa04InfrastructureServiceQueueCanonicalMaterializationV1 serviceAuthority)
    {
        ArgumentNullException.ThrowIfNull(societyAuthority);
        ArgumentNullException.ThrowIfNull(serviceAuthority);
        ValidateCanonicalContract();

        if (societyAuthority.InformationClaims.ItemCount != Qa04SocietyInformationClaimResolvedMaterializerV1.CanonicalCount ||
            serviceAuthority.CommunicationServices.ItemCount != CommunicationServiceCount)
            throw new InvalidDataException("qa04.information.delivery-upstream-authority-count-drift");

        var pools = AuthorityPools.Create(societyAuthority, serviceAuthority);
        var references = new CompositeReferenceResolver(societyAuthority.References, serviceAuthority.References);
        var validator = new StandardDomainPayloadCodecValidatorV1();
        var identity = StandardDomainPartitionRegistry.Get(InformationDeliveryPayloadV1.PartitionId);
        var records = new DomainRecordEnvelopeV1<InformationDeliveryPayloadV1>[checked((int)CanonicalCount)];
        var runtime = new InformationDeliveryV1[checked((int)CanonicalCount)];
        var ids = new HashSet<OpaqueId128>();

        for (ulong d = 0; d < CanonicalCount; d++)
        {
            var expected = Expected(d, pools);
            var binding = Binding(d);
            var payload = new InformationDeliveryPayloadV1(
                expected.ContentRef,
                expected.SenderRef,
                new[] { expected.RecipientRef },
                expected.ChannelRef,
                EligibleStep,
                DeliveredStep: null,
                Priority,
                Queued,
                expected.ContentDigest.ToArray());

            validator.Validate(InformationDeliveryPayloadV1.PartitionId, payload.ToStandardPayload(), references);

            var recordId = binding.Descriptor.RecordId;
            if (!ids.Add(recordId) ||
                recordId == expected.ContentRef.RecordId ||
                recordId == expected.SenderRef.RecordId ||
                recordId == expected.RecipientRef.RecordId ||
                recordId == expected.ChannelRef.RecordId)
                throw new InvalidDataException("qa04.information.delivery-record-id-duplicate-or-reused");

            var record = new DomainRecordEnvelopeV1<InformationDeliveryPayloadV1>(
                recordId,
                identity.RecordSchema,
                revision: 1,
                createdStep: 0,
                retiredStep: null,
                binding.Descriptor.DetailLevel,
                lineageRef: null,
                payload);
            records[checked((int)d)] = record;

            var runtimeDelivery = new InformationDeliveryV1(
                recordId,
                expected.SenderRef.RecordId,
                expected.RecipientRef.RecordId,
                expected.ClaimToken,
                EligibleStep,
                Priority,
                InformationDeliveryStatusV1.Queued);
            ValidateRuntimeCompatibility(record, expected, runtimeDelivery);
            runtime[checked((int)d)] = runtimeDelivery;
        }

        ValidateIdentityUniqueness(records);
        var partition = new DomainPartitionStateV1<InformationDeliveryPayloadV1>(identity, records);
        if (partition.ItemCount != CanonicalCount)
            throw new InvalidDataException("qa04.information.delivery-partition-count-drift");
        ValidateSecondaryIndexRebuild(partition);

        return new Qa04InformationDeliveryCanonicalMaterializationV1(
            societyAuthority,
            serviceAuthority,
            partition,
            Array.AsReadOnly(records),
            Array.AsReadOnly(runtime),
            references);
    }

    public static void ValidateCanonicalRecord(
        ulong localOrdinal,
        DomainRecordEnvelopeV1<InformationDeliveryPayloadV1> record,
        Qa04InformationDeliveryCanonicalMaterializationV1 materialization)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(materialization);
        if (localOrdinal >= CanonicalCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));

        var expected = Expected(localOrdinal, AuthorityPools.Create(materialization.SocietyAuthority, materialization.ServiceAuthority));
        var binding = Binding(localOrdinal);
        var identity = StandardDomainPartitionRegistry.Get(InformationDeliveryPayloadV1.PartitionId);
        if (record.RecordId != binding.Descriptor.RecordId ||
            record.RecordSchema != identity.RecordSchema ||
            record.Revision != 1 || record.CreatedStep != 0 || record.RetiredStep is not null ||
            record.DetailLevel != binding.Descriptor.DetailLevel || record.LineageRef is not null)
            throw new InvalidDataException("qa04.information.delivery-envelope-drift");

        var payload = record.Payload;
        if (payload.ContentRef != expected.ContentRef ||
            payload.SenderRef != expected.SenderRef ||
            payload.RecipientRefs.Count != 1 ||
            payload.RecipientRefs[0] != expected.RecipientRef ||
            payload.SenderRef == payload.RecipientRefs[0] ||
            payload.ChannelRef != expected.ChannelRef ||
            payload.EligibleStep != EligibleStep ||
            payload.DeliveredStep is not null ||
            payload.Priority != Priority ||
            payload.Status != Queued ||
            !CryptographicOperations.FixedTimeEquals(payload.ContentDigest, expected.ContentDigest))
            throw new InvalidDataException("qa04.information.delivery-genesis-drift");

        new StandardDomainPayloadCodecValidatorV1().Validate(
            InformationDeliveryPayloadV1.PartitionId,
            payload.ToStandardPayload(),
            materialization.References);
    }

    public static void ValidateIdentityUniqueness(
        IEnumerable<DomainRecordEnvelopeV1<InformationDeliveryPayloadV1>> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var ids = new HashSet<OpaqueId128>();
        foreach (var record in records)
            if (!ids.Add(record.RecordId))
                throw new InvalidDataException("qa04.information.delivery-record-id-duplicate");
    }

    public static void ValidateSecondaryIndexRebuild(DomainPartitionStateV1<InformationDeliveryPayloadV1> partition)
    {
        ArgumentNullException.ThrowIfNull(partition);
        var registrations = StandardSecondaryIndexRegistry.ForPartition(InformationDeliveryPayloadV1.PartitionId);
        if (registrations.Count != 2 ||
            registrations.All(static entry => entry.IndexId.Value != "information.delivery-by-recipient") ||
            registrations.All(static entry => entry.IndexId.Value != "information.delivery-by-status"))
            throw new InvalidDataException("qa04.information.delivery-secondary-index-registration-drift");

        var recipientIndex = DerivedRecordIndexV1<OpaqueId128>.Rebuild(
            "information.delivery-by-recipient",
            partition,
            static record => record.Payload.RecipientRefs.Select(static reference => reference.RecordId));
        var recipientEntries = recipientIndex.CanonicalEntries.ToArray();
        if (recipientEntries.Length != CanonicalCount ||
            recipientEntries.Any(static entry => entry.Value.Count != 1))
            throw new InvalidDataException("qa04.information.delivery-recipient-index-rebuild-drift");

        var statusIndex = DerivedRecordIndexV1<string>.Rebuild(
            "information.delivery-by-status",
            partition,
            static record => new[] { record.Payload.Status.Value },
            StringComparer.Ordinal);
        var statusEntries = statusIndex.CanonicalEntries.ToArray();
        if (statusEntries.Length != 1 ||
            !string.Equals(statusEntries[0].Key, Queued.Value, StringComparison.Ordinal) ||
            statusEntries[0].Value.Count != CanonicalCount)
            throw new InvalidDataException("qa04.information.delivery-status-index-rebuild-drift");
    }

    private static void ValidateRuntimeCompatibility(
        DomainRecordEnvelopeV1<InformationDeliveryPayloadV1> record,
        ExpectedAuthorityV1 expected,
        InformationDeliveryV1 runtime)
    {
        runtime.Validate();
        if (runtime.DeliveryId != record.RecordId ||
            runtime.SourceRef != record.Payload.SenderRef.RecordId ||
            runtime.DestinationRef != record.Payload.RecipientRefs[0].RecordId ||
            runtime.ClaimRef != expected.ClaimToken ||
            runtime.EligibleStep != record.Payload.EligibleStep ||
            runtime.Priority != record.Payload.Priority ||
            runtime.Status != InformationDeliveryStatusV1.Queued)
            throw new InvalidDataException("qa04.information.delivery-runtime-binding-drift");

        var contentRefBefore = record.Payload.ContentRef;
        var digestBefore = record.Payload.ContentDigest.ToArray();
        var delivered = runtime.MarkDelivered();
        if (delivered.Status != InformationDeliveryStatusV1.Delivered ||
            delivered.DeliveryId != runtime.DeliveryId ||
            delivered.SourceRef != runtime.SourceRef ||
            delivered.DestinationRef != runtime.DestinationRef ||
            delivered.ClaimRef != runtime.ClaimRef ||
            delivered.EligibleStep != runtime.EligibleStep ||
            delivered.Priority != runtime.Priority ||
            record.Payload.ContentRef != contentRefBefore ||
            !CryptographicOperations.FixedTimeEquals(record.Payload.ContentDigest, digestBefore))
            throw new InvalidDataException("qa04.information.delivery-runtime-transition-content-identity-drift");
    }

    private static Qa04InfrastructureBindingV1 Binding(ulong localOrdinal)
    {
        if (localOrdinal >= CanonicalCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));
        var binding = Qa04InfrastructureReferenceDecompositionV1.Bind(checked(CanonicalStartOrdinal + localOrdinal));
        if (binding.MaterialClass.Value != "information_delivery" ||
            binding.PartitionId.Value != InformationDeliveryPayloadV1.PartitionId ||
            binding.LocalOrdinal != localOrdinal || binding.UsesSpecializedIdentity)
            throw new InvalidDataException("qa04.information.delivery-binding-drift");
        return binding;
    }

    private static ExpectedAuthorityV1 Expected(ulong localOrdinal, AuthorityPools pools)
    {
        if (localOrdinal >= CanonicalCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));
        var claim = pools.RequireClaim(localOrdinal);
        var recipientRecord = Qa04ReferenceWorldMaterializerV1.CreateResidentRecord(checked(localOrdinal + 1));
        var recipientRef = new PartitionRecordRefV1(ResidentIdentityLifecyclePayloadV1.PartitionId, recipientRecord.RecordId);
        var channel = pools.RequireCommunication(localOrdinal % CommunicationServiceCount);
        var contentRef = new PartitionRecordRefV1(SocietyInformationClaimPayloadV1.PartitionId, claim.RecordId);
        var channelRef = new PartitionRecordRefV1(InfrastructureCommunicationServicePayloadV1.PartitionId, channel.RecordId);

        if (!pools.References.Exists(recipientRef))
            throw new InvalidDataException("qa04.information.delivery-recipient-authority-missing");
        if (claim.Payload.ClaimantRef == recipientRef)
            throw new InvalidDataException("qa04.information.delivery-self-delivery");
        if (claim.Payload.ContentDigest.Length == 0)
            throw new InvalidDataException("qa04.information.delivery-content-digest-empty");

        return new ExpectedAuthorityV1(
            contentRef,
            claim.Payload.ClaimantRef,
            recipientRef,
            channelRef,
            claim.Payload.ClaimToken,
            claim.Payload.ContentDigest);
    }

    private sealed record ExpectedAuthorityV1(
        PartitionRecordRefV1 ContentRef,
        PartitionRecordRefV1 SenderRef,
        PartitionRecordRefV1 RecipientRef,
        PartitionRecordRefV1 ChannelRef,
        StableToken ClaimToken,
        byte[] ContentDigest);

    private sealed class AuthorityPools
    {
        private readonly IReadOnlyDictionary<OpaqueId128, DomainRecordEnvelopeV1<SocietyInformationClaimPayloadV1>> _claims;
        private readonly IReadOnlyDictionary<OpaqueId128, DomainRecordEnvelopeV1<InfrastructureCommunicationServicePayloadV1>> _communications;

        private AuthorityPools(
            IReadOnlyDictionary<OpaqueId128, DomainRecordEnvelopeV1<SocietyInformationClaimPayloadV1>> claims,
            IReadOnlyDictionary<OpaqueId128, DomainRecordEnvelopeV1<InfrastructureCommunicationServicePayloadV1>> communications,
            IDomainRecordSchemaResolverV1 references)
        {
            _claims = claims;
            _communications = communications;
            References = references;
        }

        public IDomainRecordSchemaResolverV1 References { get; }

        public static AuthorityPools Create(
            Qa04SocietyGovernanceCanonicalMaterializationV1 societyAuthority,
            Qa04InfrastructureServiceQueueCanonicalMaterializationV1 serviceAuthority)
            => new(
                societyAuthority.InformationClaims.RecordsCanonical.ToDictionary(static record => record.RecordId),
                serviceAuthority.CommunicationServices.RecordsCanonical.ToDictionary(static record => record.RecordId),
                new CompositeReferenceResolver(societyAuthority.References, serviceAuthority.References));

        public DomainRecordEnvelopeV1<SocietyInformationClaimPayloadV1> RequireClaim(ulong ordinal)
        {
            var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(SocietyInformationClaimPayloadV1.PartitionId);
            if (ordinal >= slice.Count) throw new ArgumentOutOfRangeException(nameof(ordinal));
            var binding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(checked(slice.StartOrdinal + ordinal));
            return _claims.TryGetValue(binding.Descriptor.RecordId, out var record)
                ? record
                : throw new InvalidDataException("qa04.information.delivery-information-claim-missing");
        }

        public DomainRecordEnvelopeV1<InfrastructureCommunicationServicePayloadV1> RequireCommunication(ulong ordinal)
        {
            var slice = Qa04InfrastructureReferenceDecompositionV1.Get("communication_service");
            if (ordinal >= slice.Count) throw new ArgumentOutOfRangeException(nameof(ordinal));
            var binding = Qa04InfrastructureReferenceDecompositionV1.Bind(checked(slice.StartOrdinal + ordinal));
            return _communications.TryGetValue(binding.Descriptor.RecordId, out var record)
                ? record
                : throw new InvalidDataException("qa04.information.delivery-communication-service-missing");
        }
    }

    private sealed class CompositeReferenceResolver : IDomainRecordSchemaResolverV1
    {
        private readonly IDomainRecordSchemaResolverV1 _first;
        private readonly IDomainRecordSchemaResolverV1 _second;

        public CompositeReferenceResolver(IDomainRecordSchemaResolverV1 first, IDomainRecordSchemaResolverV1 second)
        {
            _first = first ?? throw new ArgumentNullException(nameof(first));
            _second = second ?? throw new ArgumentNullException(nameof(second));
        }

        public bool Exists(PartitionRecordRefV1 reference) => _first.Exists(reference) || _second.Exists(reference);

        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
        {
            var firstFound = _first.TryGetRecordSchema(reference, out var firstSchema);
            var secondFound = _second.TryGetRecordSchema(reference, out var secondSchema);
            if (firstFound && secondFound && firstSchema != secondSchema)
                throw new InvalidDataException("qa04.information.delivery-reference-schema-conflict");
            if (firstFound)
            {
                schema = firstSchema;
                return true;
            }
            if (secondFound)
            {
                schema = secondSchema;
                return true;
            }
            schema = default;
            return false;
        }
    }
}
