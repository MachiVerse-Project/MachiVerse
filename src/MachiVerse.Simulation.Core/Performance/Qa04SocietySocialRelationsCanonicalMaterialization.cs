using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed class Qa04SocietySocialRelationsCanonicalMaterializationV1
{
    internal Qa04SocietySocialRelationsCanonicalMaterializationV1(
        DomainPartitionStateV1<SocietyOrganizationPayloadV1> organizations,
        DomainPartitionStateV1<SocietyMembershipRolePayloadV1> membershipRoles,
        DomainPartitionStateV1<SocietyEducationPayloadV1> educations,
        DomainPartitionStateV1<SocietyCulturePayloadV1> cultures,
        DomainPartitionStateV1<SocietyReputationPayloadV1> reputations,
        IReadOnlyList<DomainRecordEnvelopeV1<SocietyEducationPayloadV1>> educationRecordsByOrdinal,
        IReadOnlyList<DomainRecordEnvelopeV1<SocietyCulturePayloadV1>> cultureRecordsByOrdinal,
        IReadOnlyList<DomainRecordEnvelopeV1<SocietyReputationPayloadV1>> reputationRecordsByOrdinal,
        IDomainRecordSchemaResolverV1 references)
    {
        Organizations = organizations;
        MembershipRoles = membershipRoles;
        Educations = educations;
        Cultures = cultures;
        Reputations = reputations;
        EducationRecordsByOrdinal = educationRecordsByOrdinal;
        CultureRecordsByOrdinal = cultureRecordsByOrdinal;
        ReputationRecordsByOrdinal = reputationRecordsByOrdinal;
        References = references;
    }

    public DomainPartitionStateV1<SocietyOrganizationPayloadV1> Organizations { get; }
    public DomainPartitionStateV1<SocietyMembershipRolePayloadV1> MembershipRoles { get; }
    public DomainPartitionStateV1<SocietyEducationPayloadV1> Educations { get; }
    public DomainPartitionStateV1<SocietyCulturePayloadV1> Cultures { get; }
    public DomainPartitionStateV1<SocietyReputationPayloadV1> Reputations { get; }
    public IReadOnlyList<DomainRecordEnvelopeV1<SocietyEducationPayloadV1>> EducationRecordsByOrdinal { get; }
    public IReadOnlyList<DomainRecordEnvelopeV1<SocietyCulturePayloadV1>> CultureRecordsByOrdinal { get; }
    public IReadOnlyList<DomainRecordEnvelopeV1<SocietyReputationPayloadV1>> ReputationRecordsByOrdinal { get; }
    public IDomainRecordSchemaResolverV1 References { get; }

    public ulong MaterializedRecordCount
        => checked(Educations.ItemCount + Cultures.ItemCount + Reputations.ItemCount);
}

/// <summary>
/// Production-path materialization for the #240-approved perf.reference.v1 Society social-relations
/// package. It uses actual accepted Organization authority and canonical Resident identity authority
/// only; Education, Culture, and Reputation retain independent descriptor RecordIds and payloads.
/// </summary>
public static class Qa04SocietySocialRelationsCanonicalAuthorityV1
{
    public const ulong EducationCount = 25_000;
    public const ulong CultureCount = 30_000;
    public const ulong ReputationCount = 30_000;
    public const ulong CanonicalCount = EducationCount + CultureCount + ReputationCount;
    public const ulong OrganizationCount = 10_000;
    public const ulong GenesisStep = 0;
    public const uint FullPpm = 1_000_000;

    public static readonly StableToken EducationProgram = new("perf.education-program");
    public static readonly StableToken Active = new("active");
    private static readonly StableToken[] CultureTraits =
    {
        new("perf.culture-trait-0"),
        new("perf.culture-trait-1"),
        new("perf.culture-trait-2"),
    };
    private static readonly StableToken[] ReputationDimensions =
    {
        new("perf.reputation-dimension-0"),
        new("perf.reputation-dimension-1"),
        new("perf.reputation-dimension-2"),
    };

    public static void ValidateCanonicalContract()
    {
        Qa04SocietyGovernanceReferenceDecompositionV1.ValidateCanonicalContract();
        Qa04SocietyMembershipRoleCanonicalAuthorityV1.ValidateCanonicalContract();
        Qa04ReferenceWorldMaterializerV1.ValidateCanonicalContract();

        ValidateSlice(SocietyEducationPayloadV1.PartitionId, 1_470_200, EducationCount);
        ValidateSlice(SocietyCulturePayloadV1.PartitionId, 1_495_200, CultureCount);
        ValidateSlice(SocietyReputationPayloadV1.PartitionId, 1_525_200, ReputationCount);

        if (CanonicalCount != 85_000 ||
            OrganizationCount != Qa04SocietyMembershipRoleCanonicalAuthorityV1.OrganizationCount ||
            EducationCount > Qa04ReferenceWorldMaterializerV1.CanonicalResidentCount ||
            EducationProgram.Value != "perf.education-program" || Active.Value != "active" ||
            GenesisStep != 0 || FullPpm != 1_000_000 ||
            CultureTraits.Length != 3 || ReputationDimensions.Length != 3 ||
            CultureTraits.Select(static token => token.Value).Distinct(StringComparer.Ordinal).Count() != 3 ||
            ReputationDimensions.Select(static token => token.Value).Distinct(StringComparer.Ordinal).Count() != 3)
            throw new InvalidDataException("qa04.society.social-relations-contract-drift");

        foreach (var partitionId in new[]
                 {
                     SocietyEducationPayloadV1.PartitionId,
                     SocietyCulturePayloadV1.PartitionId,
                     SocietyReputationPayloadV1.PartitionId,
                 })
        {
            if (StandardDomainPartitionRegistry.Get(partitionId).OwnerDomain.Value != "society_economy")
                throw new InvalidDataException($"qa04.society.social-relations-owner-drift:{partitionId}");
        }

        ValidateIndexRegistrations(
            SocietyEducationPayloadV1.PartitionId,
            "society.education-by-learner",
            "society.education-by-provider");
        ValidateIndexRegistrations(
            SocietyCulturePayloadV1.PartitionId,
            "society.culture-by-subject",
            "society.culture-by-trait");
        ValidateIndexRegistrations(
            SocietyReputationPayloadV1.PartitionId,
            "society.reputation-by-subject",
            "society.reputation-by-dimension");
    }

    public static Qa04SocietySocialRelationsCanonicalMaterializationV1 MaterializeCanonical()
    {
        ValidateCanonicalContract();

        var upstream = Qa04SocietyMembershipRoleCanonicalAuthorityV1.MaterializeCanonical();
        if (upstream.Organizations.ItemCount != OrganizationCount ||
            upstream.MembershipRoles.ItemCount != Qa04SocietyMembershipRoleCanonicalAuthorityV1.CanonicalCount)
            throw new InvalidDataException("qa04.society.social-relations-upstream-authority-drift");

        var validator = new StandardDomainPayloadCodecValidatorV1();
        var educationRecords = MaterializeEducations(validator, upstream.References);
        var cultureRecords = MaterializeCultures(validator, upstream.References);
        var reputationRecords = MaterializeReputations(validator, upstream.References);

        var educations = new DomainPartitionStateV1<SocietyEducationPayloadV1>(
            StandardDomainPartitionRegistry.Get(SocietyEducationPayloadV1.PartitionId),
            educationRecords);
        var cultures = new DomainPartitionStateV1<SocietyCulturePayloadV1>(
            StandardDomainPartitionRegistry.Get(SocietyCulturePayloadV1.PartitionId),
            cultureRecords);
        var reputations = new DomainPartitionStateV1<SocietyReputationPayloadV1>(
            StandardDomainPartitionRegistry.Get(SocietyReputationPayloadV1.PartitionId),
            reputationRecords);

        ValidateEducationInvariants(educationRecords);
        ValidateCultureInvariants(cultureRecords);
        ValidateReputationInvariants(reputationRecords);

        for (ulong i = 0; i < EducationCount; i++)
            ValidateEducationRecord(i, educationRecords[checked((int)i)], upstream.References);
        for (ulong i = 0; i < CultureCount; i++)
            ValidateCultureRecord(i, cultureRecords[checked((int)i)], upstream.References);
        for (ulong i = 0; i < ReputationCount; i++)
            ValidateReputationRecord(i, reputationRecords[checked((int)i)], upstream.References);

        ValidateSecondaryIndexes(educations, cultures, reputations);

        var materialization = new Qa04SocietySocialRelationsCanonicalMaterializationV1(
            upstream.Organizations,
            upstream.MembershipRoles,
            educations,
            cultures,
            reputations,
            Array.AsReadOnly(educationRecords),
            Array.AsReadOnly(cultureRecords),
            Array.AsReadOnly(reputationRecords),
            upstream.References);

        if (materialization.MaterializedRecordCount != CanonicalCount)
            throw new InvalidDataException("qa04.society.social-relations-total-count-drift");
        return materialization;
    }

    public static void ValidateEducationRecord(
        ulong localOrdinal,
        DomainRecordEnvelopeV1<SocietyEducationPayloadV1> record,
        IDomainRecordSchemaResolverV1 references)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(references);
        if (localOrdinal >= EducationCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));

        ValidateEnvelope(localOrdinal, SocietyEducationPayloadV1.PartitionId, record);
        var payload = record.Payload;
        if (payload.ProviderRef != ResolveOrganizationRef(localOrdinal % OrganizationCount) ||
            payload.LearnerRef != ResolveResidentRef(localOrdinal) ||
            payload.ProgramToken != EducationProgram || payload.Status != Active ||
            payload.ProgressPpm != 0 || payload.SkillRefs.Count != 0 ||
            payload.StartedStep != GenesisStep || payload.EndedStep is not null)
            throw new InvalidDataException("qa04.society.education-genesis-drift");

        new StandardDomainPayloadCodecValidatorV1().Validate(
            SocietyEducationPayloadV1.PartitionId,
            payload.ToStandardPayload(),
            references);
    }

    public static void ValidateCultureRecord(
        ulong localOrdinal,
        DomainRecordEnvelopeV1<SocietyCulturePayloadV1> record,
        IDomainRecordSchemaResolverV1 references)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(references);
        if (localOrdinal >= CultureCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));

        ValidateEnvelope(localOrdinal, SocietyCulturePayloadV1.PartitionId, record);
        var payload = record.Payload;
        if (payload.SubjectRef != ResolveOrganizationRef(localOrdinal % OrganizationCount) ||
            payload.TraitToken != CultureTrait(localOrdinal) ||
            payload.AffiliationPpm != FullPpm || payload.AdoptionStep != GenesisStep ||
            payload.SourceRefs.Count != 0 || payload.Status != Active)
            throw new InvalidDataException("qa04.society.culture-genesis-drift");

        new StandardDomainPayloadCodecValidatorV1().Validate(
            SocietyCulturePayloadV1.PartitionId,
            payload.ToStandardPayload(),
            references);
    }

    public static void ValidateReputationRecord(
        ulong localOrdinal,
        DomainRecordEnvelopeV1<SocietyReputationPayloadV1> record,
        IDomainRecordSchemaResolverV1 references)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(references);
        if (localOrdinal >= ReputationCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));

        ValidateEnvelope(localOrdinal, SocietyReputationPayloadV1.PartitionId, record);
        var payload = record.Payload;
        if (payload.SubjectRef != ResolveOrganizationRef(localOrdinal % OrganizationCount) ||
            payload.AudienceScopeRef is not null ||
            payload.DimensionToken != ReputationDimension(localOrdinal) ||
            payload.Score != 0 || payload.ConfidencePpm != 0 ||
            payload.EvidenceRefs.Count != 0 || payload.UpdatedStep != GenesisStep)
            throw new InvalidDataException("qa04.society.reputation-genesis-drift");

        new StandardDomainPayloadCodecValidatorV1().Validate(
            SocietyReputationPayloadV1.PartitionId,
            payload.ToStandardPayload(),
            references);
    }

    public static void ValidateEducationInvariants(
        IEnumerable<DomainRecordEnvelopeV1<SocietyEducationPayloadV1>> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var material = records.ToArray();
        if ((ulong)material.Length != EducationCount)
            throw new InvalidDataException("qa04.society.education-count-drift");

        ValidateEducationUniqueness(material);
        if ((ulong)material.Select(static record => record.Payload.LearnerRef).Distinct().Count() != EducationCount)
            throw new InvalidDataException("qa04.society.education-learner-cardinality-drift");

        var providers = material.GroupBy(static record => record.Payload.ProviderRef).ToArray();
        if ((ulong)providers.Length != OrganizationCount ||
            providers.Count(static group => group.Count() == 3) != 5_000 ||
            providers.Count(static group => group.Count() == 2) != 5_000 ||
            providers.Any(static group => group.Count() is not (2 or 3)))
            throw new InvalidDataException("qa04.society.education-provider-cardinality-drift");
    }

    public static void ValidateCultureInvariants(
        IEnumerable<DomainRecordEnvelopeV1<SocietyCulturePayloadV1>> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var material = records.ToArray();
        if ((ulong)material.Length != CultureCount)
            throw new InvalidDataException("qa04.society.culture-count-drift");

        ValidateCultureUniqueness(material);
        var subjects = material.GroupBy(static record => record.Payload.SubjectRef).ToArray();
        if ((ulong)subjects.Length != OrganizationCount || subjects.Any(static group => group.Count() != 3))
            throw new InvalidDataException("qa04.society.culture-subject-cardinality-drift");

        var traits = material.GroupBy(static record => record.Payload.TraitToken.Value, StringComparer.Ordinal).ToArray();
        if (traits.Length != 3 || traits.Any(static group => group.Count() != 10_000))
            throw new InvalidDataException("qa04.society.culture-trait-cardinality-drift");
    }

    public static void ValidateReputationInvariants(
        IEnumerable<DomainRecordEnvelopeV1<SocietyReputationPayloadV1>> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var material = records.ToArray();
        if ((ulong)material.Length != ReputationCount)
            throw new InvalidDataException("qa04.society.reputation-count-drift");

        ValidateReputationUniqueness(material);
        var subjects = material.GroupBy(static record => record.Payload.SubjectRef).ToArray();
        if ((ulong)subjects.Length != OrganizationCount || subjects.Any(static group => group.Count() != 3))
            throw new InvalidDataException("qa04.society.reputation-subject-cardinality-drift");

        var dimensions = material.GroupBy(static record => record.Payload.DimensionToken.Value, StringComparer.Ordinal).ToArray();
        if (dimensions.Length != 3 || dimensions.Any(static group => group.Count() != 10_000))
            throw new InvalidDataException("qa04.society.reputation-dimension-cardinality-drift");
    }

    public static void ValidateEducationUniqueness(
        IEnumerable<DomainRecordEnvelopeV1<SocietyEducationPayloadV1>> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var ids = new HashSet<OpaqueId128>();
        var relations = new HashSet<(PartitionRecordRefV1 Provider, PartitionRecordRefV1 Learner)>();
        foreach (var record in records)
        {
            if (!ids.Add(record.RecordId))
                throw new InvalidDataException("qa04.society.education-record-id-duplicate");
            if (!relations.Add((record.Payload.ProviderRef, record.Payload.LearnerRef)))
                throw new InvalidDataException("qa04.society.education-relation-duplicate");
        }
    }

    public static void ValidateCultureUniqueness(
        IEnumerable<DomainRecordEnvelopeV1<SocietyCulturePayloadV1>> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var ids = new HashSet<OpaqueId128>();
        var relations = new HashSet<(PartitionRecordRefV1 Subject, StableToken Trait)>();
        foreach (var record in records)
        {
            if (!ids.Add(record.RecordId))
                throw new InvalidDataException("qa04.society.culture-record-id-duplicate");
            if (!relations.Add((record.Payload.SubjectRef, record.Payload.TraitToken)))
                throw new InvalidDataException("qa04.society.culture-key-duplicate");
        }
    }

    public static void ValidateReputationUniqueness(
        IEnumerable<DomainRecordEnvelopeV1<SocietyReputationPayloadV1>> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var ids = new HashSet<OpaqueId128>();
        var relations = new HashSet<(PartitionRecordRefV1 Subject, StableToken Dimension)>();
        foreach (var record in records)
        {
            if (!ids.Add(record.RecordId))
                throw new InvalidDataException("qa04.society.reputation-record-id-duplicate");
            if (!relations.Add((record.Payload.SubjectRef, record.Payload.DimensionToken)))
                throw new InvalidDataException("qa04.society.reputation-key-duplicate");
        }
    }

    public static void ValidateSecondaryIndexes(
        DomainPartitionStateV1<SocietyEducationPayloadV1> educations,
        DomainPartitionStateV1<SocietyCulturePayloadV1> cultures,
        DomainPartitionStateV1<SocietyReputationPayloadV1> reputations)
    {
        ArgumentNullException.ThrowIfNull(educations);
        ArgumentNullException.ThrowIfNull(cultures);
        ArgumentNullException.ThrowIfNull(reputations);

        var educationByLearner = DerivedRecordIndexV1<OpaqueId128>.Rebuild(
            "society.education-by-learner",
            educations,
            static record => new[] { record.Payload.LearnerRef.RecordId });
        var educationByProvider = DerivedRecordIndexV1<OpaqueId128>.Rebuild(
            "society.education-by-provider",
            educations,
            static record => new[] { record.Payload.ProviderRef.RecordId });
        ValidateIndex(educationByLearner, checked((int)EducationCount), EducationCount,
            "qa04.society.education-by-learner-index-drift");
        ValidateIndex(educationByProvider, checked((int)OrganizationCount), EducationCount,
            "qa04.society.education-by-provider-index-drift");
        if (educationByLearner.CanonicalEntries.Any(static entry => entry.Value.Count != 1) ||
            educationByProvider.CanonicalEntries.Count(static entry => entry.Value.Count == 3) != 5_000 ||
            educationByProvider.CanonicalEntries.Count(static entry => entry.Value.Count == 2) != 5_000 ||
            educationByProvider.CanonicalEntries.Any(static entry => entry.Value.Count is not (2 or 3)))
            throw new InvalidDataException("qa04.society.education-secondary-index-cardinality-drift");

        var cultureBySubject = DerivedRecordIndexV1<OpaqueId128>.Rebuild(
            "society.culture-by-subject",
            cultures,
            static record => new[] { record.Payload.SubjectRef.RecordId });
        var cultureByTrait = DerivedRecordIndexV1<string>.Rebuild(
            "society.culture-by-trait",
            cultures,
            static record => new[] { record.Payload.TraitToken.Value },
            StringComparer.Ordinal);
        ValidateIndex(cultureBySubject, checked((int)OrganizationCount), CultureCount,
            "qa04.society.culture-by-subject-index-drift");
        ValidateIndex(cultureByTrait, 3, CultureCount,
            "qa04.society.culture-by-trait-index-drift");
        if (cultureBySubject.CanonicalEntries.Any(static entry => entry.Value.Count != 3) ||
            cultureByTrait.CanonicalEntries.Any(static entry => entry.Value.Count != 10_000))
            throw new InvalidDataException("qa04.society.culture-secondary-index-cardinality-drift");

        var reputationBySubject = DerivedRecordIndexV1<OpaqueId128>.Rebuild(
            "society.reputation-by-subject",
            reputations,
            static record => new[] { record.Payload.SubjectRef.RecordId });
        var reputationByDimension = DerivedRecordIndexV1<string>.Rebuild(
            "society.reputation-by-dimension",
            reputations,
            static record => new[] { record.Payload.DimensionToken.Value },
            StringComparer.Ordinal);
        ValidateIndex(reputationBySubject, checked((int)OrganizationCount), ReputationCount,
            "qa04.society.reputation-by-subject-index-drift");
        ValidateIndex(reputationByDimension, 3, ReputationCount,
            "qa04.society.reputation-by-dimension-index-drift");
        if (reputationBySubject.CanonicalEntries.Any(static entry => entry.Value.Count != 3) ||
            reputationByDimension.CanonicalEntries.Any(static entry => entry.Value.Count != 10_000))
            throw new InvalidDataException("qa04.society.reputation-secondary-index-cardinality-drift");
    }

    private static DomainRecordEnvelopeV1<SocietyEducationPayloadV1>[] MaterializeEducations(
        StandardDomainPayloadCodecValidatorV1 validator,
        IDomainRecordSchemaResolverV1 references)
    {
        var records = new DomainRecordEnvelopeV1<SocietyEducationPayloadV1>[checked((int)EducationCount)];
        for (ulong i = 0; i < EducationCount; i++)
        {
            var payload = new SocietyEducationPayloadV1(
                ResolveOrganizationRef(i % OrganizationCount),
                ResolveResidentRef(i),
                EducationProgram,
                Active,
                ProgressPpm: 0,
                Array.Empty<PartitionRecordRefV1>(),
                GenesisStep,
                EndedStep: null);
            validator.Validate(SocietyEducationPayloadV1.PartitionId, payload.ToStandardPayload(), references);
            records[checked((int)i)] = CreateEnvelope(i, SocietyEducationPayloadV1.PartitionId, payload);
        }
        return records;
    }

    private static DomainRecordEnvelopeV1<SocietyCulturePayloadV1>[] MaterializeCultures(
        StandardDomainPayloadCodecValidatorV1 validator,
        IDomainRecordSchemaResolverV1 references)
    {
        var records = new DomainRecordEnvelopeV1<SocietyCulturePayloadV1>[checked((int)CultureCount)];
        for (ulong i = 0; i < CultureCount; i++)
        {
            var payload = new SocietyCulturePayloadV1(
                ResolveOrganizationRef(i % OrganizationCount),
                CultureTrait(i),
                FullPpm,
                GenesisStep,
                Array.Empty<PartitionRecordRefV1>(),
                Active);
            validator.Validate(SocietyCulturePayloadV1.PartitionId, payload.ToStandardPayload(), references);
            records[checked((int)i)] = CreateEnvelope(i, SocietyCulturePayloadV1.PartitionId, payload);
        }
        return records;
    }

    private static DomainRecordEnvelopeV1<SocietyReputationPayloadV1>[] MaterializeReputations(
        StandardDomainPayloadCodecValidatorV1 validator,
        IDomainRecordSchemaResolverV1 references)
    {
        var records = new DomainRecordEnvelopeV1<SocietyReputationPayloadV1>[checked((int)ReputationCount)];
        for (ulong i = 0; i < ReputationCount; i++)
        {
            var payload = new SocietyReputationPayloadV1(
                ResolveOrganizationRef(i % OrganizationCount),
                AudienceScopeRef: null,
                ReputationDimension(i),
                Score: 0,
                ConfidencePpm: 0,
                Array.Empty<PartitionRecordRefV1>(),
                GenesisStep);
            validator.Validate(SocietyReputationPayloadV1.PartitionId, payload.ToStandardPayload(), references);
            records[checked((int)i)] = CreateEnvelope(i, SocietyReputationPayloadV1.PartitionId, payload);
        }
        return records;
    }

    private static StableToken CultureTrait(ulong localOrdinal)
    {
        if (localOrdinal >= CultureCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));
        return CultureTraits[checked((int)(localOrdinal / OrganizationCount))];
    }

    private static StableToken ReputationDimension(ulong localOrdinal)
    {
        if (localOrdinal >= ReputationCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));
        return ReputationDimensions[checked((int)(localOrdinal / OrganizationCount))];
    }

    private static PartitionRecordRefV1 ResolveOrganizationRef(ulong organizationOrdinal)
    {
        if (organizationOrdinal >= OrganizationCount) throw new ArgumentOutOfRangeException(nameof(organizationOrdinal));
        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(SocietyOrganizationPayloadV1.PartitionId);
        var binding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(checked(slice.StartOrdinal + organizationOrdinal));
        if (binding.PartitionId.Value != SocietyOrganizationPayloadV1.PartitionId ||
            binding.PartitionLocalOrdinal != organizationOrdinal || binding.UsesSpecializedIdentity)
            throw new InvalidDataException("qa04.society.social-relations-organization-binding-drift");
        return new PartitionRecordRefV1(SocietyOrganizationPayloadV1.PartitionId, binding.Descriptor.RecordId);
    }

    private static PartitionRecordRefV1 ResolveResidentRef(ulong residentOrdinal)
    {
        if (residentOrdinal >= EducationCount) throw new ArgumentOutOfRangeException(nameof(residentOrdinal));
        var resident = Qa04ReferenceWorldMaterializerV1.CreateResidentRecord(residentOrdinal);
        return new PartitionRecordRefV1(ResidentIdentityLifecyclePayloadV1.PartitionId, resident.RecordId);
    }

    private static DomainRecordEnvelopeV1<TPayload> CreateEnvelope<TPayload>(
        ulong localOrdinal,
        string partitionId,
        TPayload payload)
    {
        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(partitionId);
        var binding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(checked(slice.StartOrdinal + localOrdinal));
        if (binding.PartitionId.Value != partitionId ||
            binding.PartitionLocalOrdinal != localOrdinal || binding.UsesSpecializedIdentity ||
            binding.Descriptor.DetailLevel != DetailLevelV1.D2RegionalAggregate)
            throw new InvalidDataException($"qa04.society.social-relations-descriptor-binding-drift:{partitionId}");

        return new DomainRecordEnvelopeV1<TPayload>(
            binding.Descriptor.RecordId,
            StandardDomainPartitionRegistry.Get(partitionId).RecordSchema,
            revision: 1,
            createdStep: 0,
            retiredStep: null,
            binding.Descriptor.DetailLevel,
            lineageRef: null,
            payload);
    }

    private static void ValidateEnvelope<TPayload>(
        ulong localOrdinal,
        string partitionId,
        DomainRecordEnvelopeV1<TPayload> record)
    {
        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(partitionId);
        var binding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(checked(slice.StartOrdinal + localOrdinal));
        var identity = StandardDomainPartitionRegistry.Get(partitionId);
        if (record.RecordId != binding.Descriptor.RecordId ||
            record.RecordSchema != identity.RecordSchema ||
            record.Revision != 1 || record.CreatedStep != 0 || record.RetiredStep is not null ||
            record.DetailLevel != binding.Descriptor.DetailLevel || record.LineageRef is not null)
            throw new InvalidDataException($"qa04.society.social-relations-envelope-drift:{partitionId}");
    }

    private static void ValidateSlice(string partitionId, ulong startOrdinal, ulong count)
    {
        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(partitionId);
        if (slice.StartOrdinal != startOrdinal || slice.Count != count || slice.UsesSpecializedIdentity)
            throw new InvalidDataException($"qa04.society.social-relations-slice-drift:{partitionId}");
    }

    private static void ValidateIndexRegistrations(string partitionId, params string[] expectedIndexIds)
    {
        var indexes = StandardSecondaryIndexRegistry.ForPartition(partitionId);
        var expected = new HashSet<string>(expectedIndexIds, StringComparer.Ordinal);
        if (indexes.Count != expected.Count ||
            indexes.Any(static index => index.Authority != IndexAuthorityV1.DerivedRebuildable) ||
            !expected.SetEquals(indexes.Select(static index => index.IndexId.Value)))
            throw new InvalidDataException($"qa04.society.social-relations-secondary-index-registration-drift:{partitionId}");
    }

    private static void ValidateIndex<TKey>(
        DerivedRecordIndexV1<TKey> index,
        int expectedKeyCount,
        ulong expectedRecordCount,
        string error)
        where TKey : notnull
    {
        var entries = index.CanonicalEntries.ToArray();
        if (entries.Length != expectedKeyCount ||
            entries.Aggregate(0UL, static (sum, entry) => checked(sum + (ulong)entry.Value.Count)) != expectedRecordCount)
            throw new InvalidDataException(error);
    }
}
