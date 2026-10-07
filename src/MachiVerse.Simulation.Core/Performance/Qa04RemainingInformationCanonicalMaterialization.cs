using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed class Qa04RemainingInformationCanonicalMaterializationV1
{
    internal Qa04RemainingInformationCanonicalMaterializationV1(
        Qa04SocietyGovernanceCanonicalMaterializationV1 societyAuthority,
        Qa04InfrastructureServiceQueueCanonicalMaterializationV1 serviceAuthority,
        DomainPartitionStateV1<InformationMediaDistributionPayloadV1> mediaDistribution,
        IReadOnlyList<DomainRecordEnvelopeV1<InformationMediaDistributionPayloadV1>> mediaRecordsByOrdinal,
        DomainPartitionStateV1<InformationRecordStorePayloadV1> recordStore,
        IReadOnlyList<DomainRecordEnvelopeV1<InformationRecordStorePayloadV1>> recordStoreRecordsByOrdinal,
        IDomainRecordSchemaResolverV1 references)
    {
        SocietyAuthority = societyAuthority;
        ServiceAuthority = serviceAuthority;
        MediaDistribution = mediaDistribution;
        MediaRecordsByOrdinal = mediaRecordsByOrdinal;
        RecordStore = recordStore;
        RecordStoreRecordsByOrdinal = recordStoreRecordsByOrdinal;
        References = references;
    }

    public Qa04SocietyGovernanceCanonicalMaterializationV1 SocietyAuthority { get; }
    public Qa04InfrastructureServiceQueueCanonicalMaterializationV1 ServiceAuthority { get; }
    public DomainPartitionStateV1<InformationMediaDistributionPayloadV1> MediaDistribution { get; }
    public IReadOnlyList<DomainRecordEnvelopeV1<InformationMediaDistributionPayloadV1>> MediaRecordsByOrdinal { get; }
    public DomainPartitionStateV1<InformationRecordStorePayloadV1> RecordStore { get; }
    public IReadOnlyList<DomainRecordEnvelopeV1<InformationRecordStorePayloadV1>> RecordStoreRecordsByOrdinal { get; }
    public IDomainRecordSchemaResolverV1 References { get; }
    public ulong MaterializedRecordCount => checked(MediaDistribution.ItemCount + RecordStore.ItemCount);
}

/// <summary>
/// Production-path materialization for the #240-approved remaining Information packages:
/// information.media_distribution 5,000 and information.record_store 10,000.
/// All references bind to existing production authority; no placeholder identity, digest,
/// publisher, channel, scope, legal authority, or predecessor relation is synthesized.
/// </summary>
public static class Qa04RemainingInformationCanonicalAuthorityV1
{
    public const ulong MediaDistributionCount = 5_000;
    public const ulong MediaDistributionStartOrdinal = 465_100;
    public const ulong RecordStoreCount = 10_000;
    public const ulong RecordStoreStartOrdinal = 470_100;
    public const ulong PublishedStep = 0;
    public const ulong ReachCount = 0;
    public const uint RecordVersion = 1;

    public static readonly StableToken Published = new("published");
    public static readonly StableToken InformationClaimRecordKind = new("perf.information-claim-record");

    private static readonly StableToken InfrastructureDomain = new("infrastructure_information");

    public static void ValidateCanonicalContract()
    {
        Qa04InfrastructureReferenceDecompositionV1.ValidateCanonicalContract();
        Qa04SocietyGovernanceCanonicalMaterializerV1.ValidateCanonicalContract();
        Qa04InfrastructureServiceQueueCanonicalMaterializerV1.ValidateCanonicalContract();

        var mediaSlice = Qa04InfrastructureReferenceDecompositionV1.Get("media_distribution");
        if (mediaSlice.PartitionId.Value != InformationMediaDistributionPayloadV1.PartitionId ||
            mediaSlice.StartOrdinal != MediaDistributionStartOrdinal ||
            mediaSlice.Count != MediaDistributionCount ||
            mediaSlice.UsesSpecializedIdentity)
            throw new InvalidDataException("qa04.information.media-distribution-slice-drift");

        var storeSlice = Qa04InfrastructureReferenceDecompositionV1.Get("record_store");
        if (storeSlice.PartitionId.Value != InformationRecordStorePayloadV1.PartitionId ||
            storeSlice.StartOrdinal != RecordStoreStartOrdinal ||
            storeSlice.Count != RecordStoreCount ||
            storeSlice.UsesSpecializedIdentity)
            throw new InvalidDataException("qa04.information.record-store-slice-drift");

        if (Qa04SocietyInformationClaimResolvedMaterializerV1.CanonicalCount < RecordStoreCount ||
            Qa04SocietyOrganizationResolvedMaterializerV1.CanonicalCount < MediaDistributionCount ||
            Qa04InfrastructureServiceQueueCanonicalMaterializerV1.CommunicationServiceCount < MediaDistributionCount ||
            PublishedStep != 0 || ReachCount != 0 || RecordVersion != 1 ||
            Published.Value != "published" || InformationClaimRecordKind.Value != "perf.information-claim-record")
            throw new InvalidDataException("qa04.information.remaining-cardinality-or-genesis-drift");

        if (StandardDomainPartitionRegistry.Get(InformationMediaDistributionPayloadV1.PartitionId).OwnerDomain != InfrastructureDomain ||
            StandardDomainPartitionRegistry.Get(InformationRecordStorePayloadV1.PartitionId).OwnerDomain != InfrastructureDomain)
            throw new InvalidDataException("qa04.information.remaining-owner-drift");

        if (MediaRecordId(0).IsZero || MediaRecordId(MediaDistributionCount - 1).IsZero ||
            MediaRecordId(0) == MediaRecordId(MediaDistributionCount - 1) ||
            RecordStoreRecordId(0).IsZero || RecordStoreRecordId(RecordStoreCount - 1).IsZero ||
            RecordStoreRecordId(0) == RecordStoreRecordId(RecordStoreCount - 1))
            throw new InvalidDataException("qa04.information.remaining-identity-drift");
    }

    public static OpaqueId128 MediaRecordId(ulong localOrdinal) => MediaBinding(localOrdinal).Descriptor.RecordId;
    public static OpaqueId128 RecordStoreRecordId(ulong localOrdinal) => RecordStoreBinding(localOrdinal).Descriptor.RecordId;

    public static Qa04RemainingInformationCanonicalMaterializationV1 MaterializeCanonical()
        => MaterializeCanonical(
            Qa04SocietyGovernanceCanonicalMaterializerV1.MaterializeCanonical(),
            Qa04InfrastructureServiceQueueCanonicalMaterializerV1.MaterializeCanonical());

    public static Qa04RemainingInformationCanonicalMaterializationV1 MaterializeCanonical(
        Qa04SocietyGovernanceCanonicalMaterializationV1 societyAuthority,
        Qa04InfrastructureServiceQueueCanonicalMaterializationV1 serviceAuthority)
    {
        ArgumentNullException.ThrowIfNull(societyAuthority);
        ArgumentNullException.ThrowIfNull(serviceAuthority);
        ValidateCanonicalContract();

        if (societyAuthority.InformationClaims.ItemCount < RecordStoreCount ||
            societyAuthority.Organizations.ItemCount < MediaDistributionCount ||
            serviceAuthority.CommunicationServices.ItemCount < MediaDistributionCount)
            throw new InvalidDataException("qa04.information.remaining-upstream-authority-count-drift");

        var pools = AuthorityPools.Create(societyAuthority, serviceAuthority);
        var references = pools.References;
        var validator = new StandardDomainPayloadCodecValidatorV1();

        var mediaIdentity = StandardDomainPartitionRegistry.Get(InformationMediaDistributionPayloadV1.PartitionId);
        var mediaRecords = new DomainRecordEnvelopeV1<InformationMediaDistributionPayloadV1>[checked((int)MediaDistributionCount)];
        for (ulong m = 0; m < MediaDistributionCount; m++)
        {
            var expected = ExpectedMedia(m, pools);
            var binding = MediaBinding(m);
            var payload = new InformationMediaDistributionPayloadV1(
                expected.ClaimRef,
                expected.PublisherRef,
                new[] { expected.ChannelRef },
                new[] { expected.AudienceScopeRef },
                PublishedStep,
                ReachCount,
                Published);

            validator.Validate(InformationMediaDistributionPayloadV1.PartitionId, payload.ToStandardPayload(), references);
            var recordId = binding.Descriptor.RecordId;
            if (recordId == expected.ClaimRef.RecordId || recordId == expected.PublisherRef.RecordId ||
                recordId == expected.ChannelRef.RecordId || recordId == expected.AudienceScopeRef.RecordId)
                throw new InvalidDataException("qa04.information.media-distribution-record-id-reused");

            mediaRecords[checked((int)m)] = new DomainRecordEnvelopeV1<InformationMediaDistributionPayloadV1>(
                recordId,
                mediaIdentity.RecordSchema,
                revision: 1,
                createdStep: 0,
                retiredStep: null,
                binding.Descriptor.DetailLevel,
                lineageRef: null,
                payload);
        }

        ValidateMediaIdentityUniqueness(mediaRecords);
        var mediaPartition = new DomainPartitionStateV1<InformationMediaDistributionPayloadV1>(mediaIdentity, mediaRecords);
        if (mediaPartition.ItemCount != MediaDistributionCount)
            throw new InvalidDataException("qa04.information.media-distribution-partition-count-drift");
        ValidateMediaSecondaryIndexRebuild(mediaPartition);

        var storeIdentity = StandardDomainPartitionRegistry.Get(InformationRecordStorePayloadV1.PartitionId);
        var storeRecords = new DomainRecordEnvelopeV1<InformationRecordStorePayloadV1>[checked((int)RecordStoreCount)];
        for (ulong r = 0; r < RecordStoreCount; r++)
        {
            var expected = ExpectedRecordStore(r, pools);
            var binding = RecordStoreBinding(r);
            var payload = new InformationRecordStorePayloadV1(
                InformationClaimRecordKind,
                AuthorityRef: null,
                new[] { expected.SubjectRef },
                expected.ContentDigest.ToArray(),
                RecordVersion,
                expected.CreatedStep,
                Available: true,
                SupersedesRef: null);

            validator.Validate(InformationRecordStorePayloadV1.PartitionId, payload.ToStandardPayload(), references);
            var recordId = binding.Descriptor.RecordId;
            if (recordId == expected.SubjectRef.RecordId)
                throw new InvalidDataException("qa04.information.record-store-record-id-reused");

            storeRecords[checked((int)r)] = new DomainRecordEnvelopeV1<InformationRecordStorePayloadV1>(
                recordId,
                storeIdentity.RecordSchema,
                revision: 1,
                createdStep: 0,
                retiredStep: null,
                binding.Descriptor.DetailLevel,
                lineageRef: null,
                payload);
        }

        ValidateRecordStoreIdentityUniqueness(storeRecords);
        var storePartition = new DomainPartitionStateV1<InformationRecordStorePayloadV1>(storeIdentity, storeRecords);
        if (storePartition.ItemCount != RecordStoreCount)
            throw new InvalidDataException("qa04.information.record-store-partition-count-drift");
        ValidateRecordStoreSecondaryIndexRebuild(storePartition);

        return new Qa04RemainingInformationCanonicalMaterializationV1(
            societyAuthority,
            serviceAuthority,
            mediaPartition,
            Array.AsReadOnly(mediaRecords),
            storePartition,
            Array.AsReadOnly(storeRecords),
            references);
    }

    public static void ValidateCanonicalMediaRecord(
        ulong localOrdinal,
        DomainRecordEnvelopeV1<InformationMediaDistributionPayloadV1> record,
        Qa04RemainingInformationCanonicalMaterializationV1 materialization)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(materialization);
        if (localOrdinal >= MediaDistributionCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));

        var expected = ExpectedMedia(localOrdinal, AuthorityPools.Create(materialization.SocietyAuthority, materialization.ServiceAuthority));
        var binding = MediaBinding(localOrdinal);
        var identity = StandardDomainPartitionRegistry.Get(InformationMediaDistributionPayloadV1.PartitionId);
        if (record.RecordId != binding.Descriptor.RecordId || record.RecordSchema != identity.RecordSchema ||
            record.Revision != 1 || record.CreatedStep != 0 || record.RetiredStep is not null ||
            record.DetailLevel != binding.Descriptor.DetailLevel || record.LineageRef is not null)
            throw new InvalidDataException("qa04.information.media-distribution-envelope-drift");

        var payload = record.Payload;
        if (payload.ClaimRef != expected.ClaimRef || payload.PublisherRef != expected.PublisherRef ||
            payload.ChannelRefs.Count != 1 || payload.ChannelRefs[0] != expected.ChannelRef ||
            payload.AudienceScopeRefs.Count != 1 || payload.AudienceScopeRefs[0] != expected.AudienceScopeRef ||
            payload.PublishedStep != PublishedStep || payload.ReachCount != ReachCount || payload.Status != Published)
            throw new InvalidDataException("qa04.information.media-distribution-genesis-drift");

        new StandardDomainPayloadCodecValidatorV1().Validate(
            InformationMediaDistributionPayloadV1.PartitionId,
            payload.ToStandardPayload(),
            materialization.References);
    }

    public static void ValidateCanonicalRecordStoreRecord(
        ulong localOrdinal,
        DomainRecordEnvelopeV1<InformationRecordStorePayloadV1> record,
        Qa04RemainingInformationCanonicalMaterializationV1 materialization)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(materialization);
        if (localOrdinal >= RecordStoreCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));

        var expected = ExpectedRecordStore(localOrdinal, AuthorityPools.Create(materialization.SocietyAuthority, materialization.ServiceAuthority));
        var binding = RecordStoreBinding(localOrdinal);
        var identity = StandardDomainPartitionRegistry.Get(InformationRecordStorePayloadV1.PartitionId);
        if (record.RecordId != binding.Descriptor.RecordId || record.RecordSchema != identity.RecordSchema ||
            record.Revision != 1 || record.CreatedStep != 0 || record.RetiredStep is not null ||
            record.DetailLevel != binding.Descriptor.DetailLevel || record.LineageRef is not null)
            throw new InvalidDataException("qa04.information.record-store-envelope-drift");

        var payload = record.Payload;
        if (payload.RecordKind != InformationClaimRecordKind || payload.AuthorityRef is not null ||
            payload.SubjectRefs.Count != 1 || payload.SubjectRefs[0] != expected.SubjectRef ||
            !CryptographicOperations.FixedTimeEquals(payload.ContentDigest, expected.ContentDigest) ||
            payload.Version != RecordVersion || payload.CreatedStep != expected.CreatedStep ||
            !payload.Available || payload.SupersedesRef is not null)
            throw new InvalidDataException("qa04.information.record-store-genesis-drift");

        new StandardDomainPayloadCodecValidatorV1().Validate(
            InformationRecordStorePayloadV1.PartitionId,
            payload.ToStandardPayload(),
            materialization.References);
    }

    public static void ValidateMediaIdentityUniqueness(
        IEnumerable<DomainRecordEnvelopeV1<InformationMediaDistributionPayloadV1>> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var ids = new HashSet<OpaqueId128>();
        var claims = new HashSet<PartitionRecordRefV1>();
        var publishers = new HashSet<PartitionRecordRefV1>();
        foreach (var record in records)
        {
            if (!ids.Add(record.RecordId))
                throw new InvalidDataException("qa04.information.media-distribution-record-id-duplicate");
            if (!claims.Add(record.Payload.ClaimRef))
                throw new InvalidDataException("qa04.information.media-distribution-claim-relation-duplicate");
            if (!publishers.Add(record.Payload.PublisherRef))
                throw new InvalidDataException("qa04.information.media-distribution-publisher-relation-duplicate");
        }
    }

    public static void ValidateRecordStoreIdentityUniqueness(
        IEnumerable<DomainRecordEnvelopeV1<InformationRecordStorePayloadV1>> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var ids = new HashSet<OpaqueId128>();
        var subjects = new HashSet<PartitionRecordRefV1>();
        foreach (var record in records)
        {
            if (!ids.Add(record.RecordId))
                throw new InvalidDataException("qa04.information.record-store-record-id-duplicate");
            if (record.Payload.SubjectRefs.Count != 1 || !subjects.Add(record.Payload.SubjectRefs[0]))
                throw new InvalidDataException("qa04.information.record-store-subject-relation-duplicate-or-noncanonical");
        }
    }

    public static void ValidateMediaSecondaryIndexRebuild(
        DomainPartitionStateV1<InformationMediaDistributionPayloadV1> partition)
    {
        ArgumentNullException.ThrowIfNull(partition);
        var registrations = StandardSecondaryIndexRegistry.ForPartition(InformationMediaDistributionPayloadV1.PartitionId);
        if (registrations.Count != 2 ||
            registrations.All(static entry => entry.IndexId.Value != "information.media-by-claim") ||
            registrations.All(static entry => entry.IndexId.Value != "information.media-by-publisher"))
            throw new InvalidDataException("qa04.information.media-distribution-secondary-index-registration-drift");

        var claimIndex = DerivedRecordIndexV1<OpaqueId128>.Rebuild(
            "information.media-by-claim",
            partition,
            static record => new[] { record.Payload.ClaimRef.RecordId });
        var claimEntries = claimIndex.CanonicalEntries.ToArray();
        if (claimEntries.Length != checked((int)MediaDistributionCount) ||
            claimEntries.Any(static entry => entry.Value.Count != 1))
            throw new InvalidDataException("qa04.information.media-distribution-claim-index-rebuild-drift");

        var publisherIndex = DerivedRecordIndexV1<OpaqueId128>.Rebuild(
            "information.media-by-publisher",
            partition,
            static record => new[] { record.Payload.PublisherRef.RecordId });
        var publisherEntries = publisherIndex.CanonicalEntries.ToArray();
        if (publisherEntries.Length != checked((int)MediaDistributionCount) ||
            publisherEntries.Any(static entry => entry.Value.Count != 1))
            throw new InvalidDataException("qa04.information.media-distribution-publisher-index-rebuild-drift");
    }

    public static void ValidateRecordStoreSecondaryIndexRebuild(
        DomainPartitionStateV1<InformationRecordStorePayloadV1> partition)
    {
        ArgumentNullException.ThrowIfNull(partition);
        var registrations = StandardSecondaryIndexRegistry.ForPartition(InformationRecordStorePayloadV1.PartitionId);
        if (registrations.Count != 2 ||
            registrations.All(static entry => entry.IndexId.Value != "information.record-by-subject") ||
            registrations.All(static entry => entry.IndexId.Value != "information.record-by-kind"))
            throw new InvalidDataException("qa04.information.record-store-secondary-index-registration-drift");

        var subjectIndex = DerivedRecordIndexV1<OpaqueId128>.Rebuild(
            "information.record-by-subject",
            partition,
            static record => record.Payload.SubjectRefs.Select(static reference => reference.RecordId));
        var subjectEntries = subjectIndex.CanonicalEntries.ToArray();
        if (subjectEntries.Length != checked((int)RecordStoreCount) ||
            subjectEntries.Any(static entry => entry.Value.Count != 1))
            throw new InvalidDataException("qa04.information.record-store-subject-index-rebuild-drift");

        var kindIndex = DerivedRecordIndexV1<string>.Rebuild(
            "information.record-by-kind",
            partition,
            static record => new[] { record.Payload.RecordKind.Value },
            StringComparer.Ordinal);
        var kindEntries = kindIndex.CanonicalEntries.ToArray();
        if (kindEntries.Length != 1 ||
            !string.Equals(kindEntries[0].Key, InformationClaimRecordKind.Value, StringComparison.Ordinal) ||
            kindEntries[0].Value.Count != checked((int)RecordStoreCount))
            throw new InvalidDataException("qa04.information.record-store-kind-index-rebuild-drift");
    }

    private static Qa04InfrastructureBindingV1 MediaBinding(ulong localOrdinal)
    {
        if (localOrdinal >= MediaDistributionCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));
        var binding = Qa04InfrastructureReferenceDecompositionV1.Bind(checked(MediaDistributionStartOrdinal + localOrdinal));
        if (binding.MaterialClass.Value != "media_distribution" ||
            binding.PartitionId.Value != InformationMediaDistributionPayloadV1.PartitionId ||
            binding.LocalOrdinal != localOrdinal || binding.UsesSpecializedIdentity)
            throw new InvalidDataException("qa04.information.media-distribution-binding-drift");
        return binding;
    }

    private static Qa04InfrastructureBindingV1 RecordStoreBinding(ulong localOrdinal)
    {
        if (localOrdinal >= RecordStoreCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));
        var binding = Qa04InfrastructureReferenceDecompositionV1.Bind(checked(RecordStoreStartOrdinal + localOrdinal));
        if (binding.MaterialClass.Value != "record_store" ||
            binding.PartitionId.Value != InformationRecordStorePayloadV1.PartitionId ||
            binding.LocalOrdinal != localOrdinal || binding.UsesSpecializedIdentity)
            throw new InvalidDataException("qa04.information.record-store-binding-drift");
        return binding;
    }

    private static ExpectedMediaAuthorityV1 ExpectedMedia(ulong localOrdinal, AuthorityPools pools)
    {
        if (localOrdinal >= MediaDistributionCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));
        var claim = pools.RequireClaim(localOrdinal);
        var publisher = pools.RequireOrganization(localOrdinal);
        var channel = pools.RequireCommunication(localOrdinal);
        var claimRef = new PartitionRecordRefV1(SocietyInformationClaimPayloadV1.PartitionId, claim.RecordId);
        var publisherRef = new PartitionRecordRefV1(SocietyOrganizationPayloadV1.PartitionId, publisher.RecordId);
        var channelRef = new PartitionRecordRefV1(InfrastructureCommunicationServicePayloadV1.PartitionId, channel.RecordId);
        var audienceScopeRef = channel.Payload.ServiceScopeRef;
        if (!pools.References.Exists(audienceScopeRef))
            throw new InvalidDataException("qa04.information.media-distribution-audience-scope-missing");
        return new ExpectedMediaAuthorityV1(claimRef, publisherRef, channelRef, audienceScopeRef);
    }

    private static ExpectedRecordStoreAuthorityV1 ExpectedRecordStore(ulong localOrdinal, AuthorityPools pools)
    {
        if (localOrdinal >= RecordStoreCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));
        var claim = pools.RequireClaim(localOrdinal);
        if (claim.Payload.ContentDigest.Length == 0)
            throw new InvalidDataException("qa04.information.record-store-content-digest-empty");
        return new ExpectedRecordStoreAuthorityV1(
            new PartitionRecordRefV1(SocietyInformationClaimPayloadV1.PartitionId, claim.RecordId),
            claim.Payload.ContentDigest,
            claim.Payload.CreatedStep);
    }

    private sealed record ExpectedMediaAuthorityV1(
        PartitionRecordRefV1 ClaimRef,
        PartitionRecordRefV1 PublisherRef,
        PartitionRecordRefV1 ChannelRef,
        PartitionRecordRefV1 AudienceScopeRef);

    private sealed record ExpectedRecordStoreAuthorityV1(
        PartitionRecordRefV1 SubjectRef,
        byte[] ContentDigest,
        ulong CreatedStep);

    private sealed class AuthorityPools
    {
        private readonly IReadOnlyDictionary<OpaqueId128, DomainRecordEnvelopeV1<SocietyInformationClaimPayloadV1>> _claims;
        private readonly IReadOnlyDictionary<OpaqueId128, DomainRecordEnvelopeV1<SocietyOrganizationPayloadV1>> _organizations;
        private readonly IReadOnlyDictionary<OpaqueId128, DomainRecordEnvelopeV1<InfrastructureCommunicationServicePayloadV1>> _communications;

        private AuthorityPools(
            IReadOnlyDictionary<OpaqueId128, DomainRecordEnvelopeV1<SocietyInformationClaimPayloadV1>> claims,
            IReadOnlyDictionary<OpaqueId128, DomainRecordEnvelopeV1<SocietyOrganizationPayloadV1>> organizations,
            IReadOnlyDictionary<OpaqueId128, DomainRecordEnvelopeV1<InfrastructureCommunicationServicePayloadV1>> communications,
            IDomainRecordSchemaResolverV1 references)
        {
            _claims = claims;
            _organizations = organizations;
            _communications = communications;
            References = references;
        }

        public IDomainRecordSchemaResolverV1 References { get; }

        public static AuthorityPools Create(
            Qa04SocietyGovernanceCanonicalMaterializationV1 societyAuthority,
            Qa04InfrastructureServiceQueueCanonicalMaterializationV1 serviceAuthority)
            => new(
                societyAuthority.InformationClaims.RecordsCanonical.ToDictionary(static record => record.RecordId),
                societyAuthority.Organizations.RecordsCanonical.ToDictionary(static record => record.RecordId),
                serviceAuthority.CommunicationServices.RecordsCanonical.ToDictionary(static record => record.RecordId),
                new CompositeReferenceResolver(societyAuthority.References, serviceAuthority.References));

        public DomainRecordEnvelopeV1<SocietyInformationClaimPayloadV1> RequireClaim(ulong ordinal)
        {
            var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(SocietyInformationClaimPayloadV1.PartitionId);
            if (ordinal >= slice.Count) throw new ArgumentOutOfRangeException(nameof(ordinal));
            var binding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(checked(slice.StartOrdinal + ordinal));
            return _claims.TryGetValue(binding.Descriptor.RecordId, out var record)
                ? record
                : throw new InvalidDataException("qa04.information.remaining-information-claim-missing");
        }

        public DomainRecordEnvelopeV1<SocietyOrganizationPayloadV1> RequireOrganization(ulong ordinal)
        {
            var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(SocietyOrganizationPayloadV1.PartitionId);
            if (ordinal >= slice.Count) throw new ArgumentOutOfRangeException(nameof(ordinal));
            var binding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(checked(slice.StartOrdinal + ordinal));
            return _organizations.TryGetValue(binding.Descriptor.RecordId, out var record)
                ? record
                : throw new InvalidDataException("qa04.information.media-distribution-organization-missing");
        }

        public DomainRecordEnvelopeV1<InfrastructureCommunicationServicePayloadV1> RequireCommunication(ulong ordinal)
        {
            var slice = Qa04InfrastructureReferenceDecompositionV1.Get("communication_service");
            if (ordinal >= slice.Count) throw new ArgumentOutOfRangeException(nameof(ordinal));
            var binding = Qa04InfrastructureReferenceDecompositionV1.Bind(checked(slice.StartOrdinal + ordinal));
            return _communications.TryGetValue(binding.Descriptor.RecordId, out var record)
                ? record
                : throw new InvalidDataException("qa04.information.media-distribution-communication-service-missing");
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
                throw new InvalidDataException("qa04.information.remaining-reference-schema-conflict");
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
