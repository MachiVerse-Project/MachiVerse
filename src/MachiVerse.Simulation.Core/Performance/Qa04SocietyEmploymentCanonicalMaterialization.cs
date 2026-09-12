using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed class Qa04SocietyEmploymentCanonicalMaterializationV1
{
    internal Qa04SocietyEmploymentCanonicalMaterializationV1(
        DomainPartitionStateV1<SocietyOrganizationPayloadV1> organizations,
        DomainPartitionStateV1<SocietyMembershipRolePayloadV1> membershipRoles,
        DomainPartitionStateV1<SocietyEmploymentPayloadV1> employments,
        IReadOnlyList<DomainRecordEnvelopeV1<SocietyMembershipRolePayloadV1>> membershipRecordsByOrdinal,
        IReadOnlyList<DomainRecordEnvelopeV1<SocietyEmploymentPayloadV1>> recordsByOrdinal,
        IDomainRecordSchemaResolverV1 references)
    {
        Organizations = organizations;
        MembershipRoles = membershipRoles;
        Employments = employments;
        MembershipRecordsByOrdinal = membershipRecordsByOrdinal;
        RecordsByOrdinal = recordsByOrdinal;
        References = references;
    }

    public DomainPartitionStateV1<SocietyOrganizationPayloadV1> Organizations { get; }
    public DomainPartitionStateV1<SocietyMembershipRolePayloadV1> MembershipRoles { get; }
    public DomainPartitionStateV1<SocietyEmploymentPayloadV1> Employments { get; }
    public IReadOnlyList<DomainRecordEnvelopeV1<SocietyMembershipRolePayloadV1>> MembershipRecordsByOrdinal { get; }
    public IReadOnlyList<DomainRecordEnvelopeV1<SocietyEmploymentPayloadV1>> RecordsByOrdinal { get; }
    public IDomainRecordSchemaResolverV1 References { get; }
    public ulong MaterializedRecordCount => Employments.ItemCount;
}

/// <summary>
/// Production-path materialization for the #240-approved perf.reference.v1 society.employment
/// 80,000-record package. It reuses actual Organization/Resident endpoints already proven by the
/// MembershipRole fixture, while keeping Employment record identity and payload authority independent.
/// </summary>
public static class Qa04SocietyEmploymentCanonicalAuthorityV1
{
    public const ulong CanonicalCount = 80_000;
    public const ulong OrganizationCount = 10_000;
    public const ulong EmploymentsPerOrganization = 8;
    public const ulong StartedStep = 0;
    public const long WageMicrounitPerPeriod = 1_000_000;
    public const ulong PayPeriodSteps = 1;

    public static readonly StableToken JobToken = new("perf.worker");
    public static readonly StableToken Active = new("active");

    public static void ValidateCanonicalContract()
    {
        Qa04SocietyGovernanceReferenceDecompositionV1.ValidateCanonicalContract();
        Qa04SocietyMembershipRoleCanonicalAuthorityV1.ValidateCanonicalContract();
        Qa04ReferenceWorldMaterializerV1.ValidateCanonicalContract();

        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(SocietyEmploymentPayloadV1.PartitionId);
        if (slice.StartOrdinal != 90_000 || slice.Count != CanonicalCount || slice.UsesSpecializedIdentity)
            throw new InvalidDataException("qa04.society.employment-slice-drift");
        if (OrganizationCount != Qa04SocietyMembershipRoleCanonicalAuthorityV1.OrganizationCount ||
            checked(OrganizationCount * EmploymentsPerOrganization) != CanonicalCount ||
            CanonicalCount != Qa04SocietyMembershipRoleCanonicalAuthorityV1.CanonicalCount ||
            CanonicalCount > Qa04ReferenceWorldMaterializerV1.CanonicalResidentCount)
            throw new InvalidDataException("qa04.society.employment-cardinality-drift");
        if (JobToken.Value != "perf.worker" || Active.Value != "active" ||
            StartedStep != 0 || WageMicrounitPerPeriod != 1_000_000 || PayPeriodSteps != 1)
            throw new InvalidDataException("qa04.society.employment-benchmark-authority-drift");

        var identity = StandardDomainPartitionRegistry.Get(SocietyEmploymentPayloadV1.PartitionId);
        if (identity.OwnerDomain.Value != "society_economy")
            throw new InvalidDataException("qa04.society.employment-owner-drift");

        var indexes = StandardSecondaryIndexRegistry.ForPartition(SocietyEmploymentPayloadV1.PartitionId);
        var expectedIndexes = new HashSet<string>(StringComparer.Ordinal)
        {
            "society.employment-by-employer",
            "society.employment-by-worker",
        };
        if (indexes.Count != 2 ||
            indexes.Any(index => index.Authority != IndexAuthorityV1.DerivedRebuildable) ||
            !expectedIndexes.SetEquals(indexes.Select(static index => index.IndexId.Value)))
            throw new InvalidDataException("qa04.society.employment-secondary-index-registration-drift");
    }

    public static Qa04SocietyEmploymentCanonicalMaterializationV1 MaterializeCanonical()
    {
        ValidateCanonicalContract();

        var membership = Qa04SocietyMembershipRoleCanonicalAuthorityV1.MaterializeCanonical();
        if (membership.Organizations.ItemCount != OrganizationCount ||
            membership.MembershipRoles.ItemCount != CanonicalCount ||
            membership.RecordsByOrdinal.Count != checked((int)CanonicalCount))
            throw new InvalidDataException("qa04.society.employment-upstream-authority-drift");

        var validator = new StandardDomainPayloadCodecValidatorV1();
        var identity = StandardDomainPartitionRegistry.Get(SocietyEmploymentPayloadV1.PartitionId);
        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(SocietyEmploymentPayloadV1.PartitionId);
        var records = new DomainRecordEnvelopeV1<SocietyEmploymentPayloadV1>[checked((int)CanonicalCount)];

        for (ulong i = 0; i < CanonicalCount; i++)
        {
            var membershipRecord = membership.RecordsByOrdinal[checked((int)i)];
            var payload = new SocietyEmploymentPayloadV1(
                membershipRecord.Payload.OrganizationRef,
                membershipRecord.Payload.MemberRef,
                JobToken,
                Active,
                StartedStep,
                EndedStep: null,
                WageMicrounitPerPeriod,
                PayPeriodSteps,
                Array.Empty<PartitionRecordRefV1>());
            validator.Validate(SocietyEmploymentPayloadV1.PartitionId, payload.ToStandardPayload(), membership.References);

            var binding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(checked(slice.StartOrdinal + i));
            if (binding.PartitionId.Value != SocietyEmploymentPayloadV1.PartitionId ||
                binding.PartitionLocalOrdinal != i || binding.UsesSpecializedIdentity ||
                binding.Descriptor.DetailLevel != DetailLevelV1.D2RegionalAggregate)
                throw new InvalidDataException("qa04.society.employment-descriptor-binding-drift");

            records[checked((int)i)] = new DomainRecordEnvelopeV1<SocietyEmploymentPayloadV1>(
                binding.Descriptor.RecordId,
                identity.RecordSchema,
                revision: 1,
                createdStep: 0,
                retiredStep: null,
                binding.Descriptor.DetailLevel,
                lineageRef: null,
                payload);
        }

        ValidateInvariants(records, membership.RecordsByOrdinal);
        var employments = new DomainPartitionStateV1<SocietyEmploymentPayloadV1>(identity, records);
        if (employments.ItemCount != CanonicalCount)
            throw new InvalidDataException("qa04.society.employment-partition-count-drift");
        ValidateSecondaryIndexes(employments);

        return new Qa04SocietyEmploymentCanonicalMaterializationV1(
            membership.Organizations,
            membership.MembershipRoles,
            employments,
            membership.RecordsByOrdinal,
            Array.AsReadOnly(records),
            membership.References);
    }

    public static void ValidateCanonicalRecord(
        ulong localOrdinal,
        DomainRecordEnvelopeV1<SocietyEmploymentPayloadV1> record,
        IDomainRecordSchemaResolverV1 references)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(references);
        if (localOrdinal >= CanonicalCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));

        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(SocietyEmploymentPayloadV1.PartitionId);
        var binding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(checked(slice.StartOrdinal + localOrdinal));
        var organizationSlice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(SocietyOrganizationPayloadV1.PartitionId);
        var organizationBinding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(
            checked(organizationSlice.StartOrdinal + (localOrdinal % OrganizationCount)));
        var resident = Qa04ReferenceWorldMaterializerV1.CreateResidentRecord(localOrdinal);
        var expectedEmployer = new PartitionRecordRefV1(SocietyOrganizationPayloadV1.PartitionId, organizationBinding.Descriptor.RecordId);
        var expectedWorker = new PartitionRecordRefV1(ResidentIdentityLifecyclePayloadV1.PartitionId, resident.RecordId);
        var identity = StandardDomainPartitionRegistry.Get(SocietyEmploymentPayloadV1.PartitionId);

        if (record.RecordId != binding.Descriptor.RecordId ||
            record.RecordSchema != identity.RecordSchema ||
            record.Revision != 1 || record.CreatedStep != 0 || record.RetiredStep is not null ||
            record.DetailLevel != binding.Descriptor.DetailLevel || record.LineageRef is not null)
            throw new InvalidDataException("qa04.society.employment-envelope-drift");

        var payload = record.Payload;
        if (payload.EmployerRef != expectedEmployer ||
            payload.WorkerRef != expectedWorker ||
            payload.JobToken != JobToken || payload.Status != Active ||
            payload.StartedStep != StartedStep || payload.EndedStep is not null ||
            payload.WageMicrounitPerPeriod != WageMicrounitPerPeriod ||
            payload.PayPeriodSteps != PayPeriodSteps ||
            payload.ObligationRefs.Count != 0)
            throw new InvalidDataException("qa04.society.employment-genesis-drift");

        new StandardDomainPayloadCodecValidatorV1().Validate(
            SocietyEmploymentPayloadV1.PartitionId,
            payload.ToStandardPayload(),
            references);
    }

    public static void ValidateInvariants(
        IEnumerable<DomainRecordEnvelopeV1<SocietyEmploymentPayloadV1>> employments,
        IEnumerable<DomainRecordEnvelopeV1<SocietyMembershipRolePayloadV1>> memberships)
    {
        ArgumentNullException.ThrowIfNull(employments);
        ArgumentNullException.ThrowIfNull(memberships);
        var employmentRecords = employments.ToArray();
        var membershipRecords = memberships.ToArray();
        if ((ulong)employmentRecords.Length != CanonicalCount || (ulong)membershipRecords.Length != CanonicalCount)
            throw new InvalidDataException("qa04.society.employment-count-drift");

        ValidateUniqueness(employmentRecords);

        var employerGroups = employmentRecords.GroupBy(static record => record.Payload.EmployerRef).ToArray();
        if ((ulong)employerGroups.Length != OrganizationCount ||
            employerGroups.Any(static group => (ulong)group.Count() != EmploymentsPerOrganization))
            throw new InvalidDataException("qa04.society.employment-employer-cardinality-drift");
        if ((ulong)employmentRecords.Select(static record => record.Payload.WorkerRef).Distinct().Count() != CanonicalCount)
            throw new InvalidDataException("qa04.society.employment-worker-cardinality-drift");

        ValidateMembershipPairCoherence(employmentRecords, membershipRecords);
    }

    public static void ValidateUniqueness(IEnumerable<DomainRecordEnvelopeV1<SocietyEmploymentPayloadV1>> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var ids = new HashSet<OpaqueId128>();
        var relations = new HashSet<(PartitionRecordRefV1 Employer, PartitionRecordRefV1 Worker)>();
        foreach (var record in records)
        {
            if (!ids.Add(record.RecordId))
                throw new InvalidDataException("qa04.society.employment-record-id-duplicate");
            if (!relations.Add((record.Payload.EmployerRef, record.Payload.WorkerRef)))
                throw new InvalidDataException("qa04.society.employment-relation-duplicate");
        }
    }

    public static void ValidateMembershipPairCoherence(
        IEnumerable<DomainRecordEnvelopeV1<SocietyEmploymentPayloadV1>> employments,
        IEnumerable<DomainRecordEnvelopeV1<SocietyMembershipRolePayloadV1>> memberships)
    {
        ArgumentNullException.ThrowIfNull(employments);
        ArgumentNullException.ThrowIfNull(memberships);
        var employmentPairs = employments
            .Select(static record => (record.Payload.EmployerRef, record.Payload.WorkerRef))
            .ToHashSet();
        var membershipPairs = memberships
            .Select(static record => (record.Payload.OrganizationRef, record.Payload.MemberRef))
            .ToHashSet();
        if (employmentPairs.Count != membershipPairs.Count || !employmentPairs.SetEquals(membershipPairs))
            throw new InvalidDataException("qa04.society.employment-membership-pair-coherence-drift");
    }

    public static void ValidateSecondaryIndexes(DomainPartitionStateV1<SocietyEmploymentPayloadV1> employments)
    {
        ArgumentNullException.ThrowIfNull(employments);
        var byEmployer = DerivedRecordIndexV1<OpaqueId128>.Rebuild(
            "society.employment-by-employer",
            employments,
            static record => new[] { record.Payload.EmployerRef.RecordId });
        var byWorker = DerivedRecordIndexV1<OpaqueId128>.Rebuild(
            "society.employment-by-worker",
            employments,
            static record => new[] { record.Payload.WorkerRef.RecordId });

        ValidateIndex(byEmployer, checked((int)OrganizationCount), CanonicalCount,
            "qa04.society.employment-by-employer-index-drift");
        ValidateIndex(byWorker, checked((int)CanonicalCount), CanonicalCount,
            "qa04.society.employment-by-worker-index-drift");

        if (byEmployer.CanonicalEntries.Any(static entry => (ulong)entry.Value.Count != EmploymentsPerOrganization) ||
            byWorker.CanonicalEntries.Any(static entry => entry.Value.Count != 1))
            throw new InvalidDataException("qa04.society.employment-secondary-index-cardinality-drift");
    }

    private static void ValidateIndex(
        DerivedRecordIndexV1<OpaqueId128> index,
        int expectedKeyCount,
        ulong expectedRecordCount,
        string error)
    {
        var entries = index.CanonicalEntries.ToArray();
        if (entries.Length != expectedKeyCount ||
            entries.Aggregate(0UL, static (sum, entry) => checked(sum + (ulong)entry.Value.Count)) != expectedRecordCount)
            throw new InvalidDataException(error);
    }
}
