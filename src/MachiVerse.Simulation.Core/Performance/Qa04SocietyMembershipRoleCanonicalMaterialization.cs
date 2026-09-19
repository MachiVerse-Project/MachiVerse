using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed class Qa04SocietyMembershipRoleCanonicalMaterializationV1
{
    internal Qa04SocietyMembershipRoleCanonicalMaterializationV1(
        DomainPartitionStateV1<SocietyOrganizationPayloadV1> organizations,
        DomainPartitionStateV1<SocietyMembershipRolePayloadV1> membershipRoles,
        IReadOnlyList<DomainRecordEnvelopeV1<SocietyMembershipRolePayloadV1>> recordsByOrdinal,
        IDomainRecordSchemaResolverV1 references)
    {
        Organizations = organizations;
        MembershipRoles = membershipRoles;
        RecordsByOrdinal = recordsByOrdinal;
        References = references;
    }

    public DomainPartitionStateV1<SocietyOrganizationPayloadV1> Organizations { get; }
    public DomainPartitionStateV1<SocietyMembershipRolePayloadV1> MembershipRoles { get; }
    public IReadOnlyList<DomainRecordEnvelopeV1<SocietyMembershipRolePayloadV1>> RecordsByOrdinal { get; }
    public IDomainRecordSchemaResolverV1 References { get; }
    public ulong MaterializedRecordCount => MembershipRoles.ItemCount;
}

/// <summary>
/// Production-path materialization for the approved perf.reference.v1 society.membership_role
/// 80,000-record package. It binds the already accepted 10,000 Organization authority to the first
/// 80,000 actual canonical Residents with exactly eight memberships per Organization.
/// </summary>
public static class Qa04SocietyMembershipRoleCanonicalAuthorityV1
{
    public const ulong CanonicalCount = 80_000;
    public const ulong OrganizationCount = 10_000;
    public const ulong MembershipsPerOrganization = 8;
    public const ulong JoinedStep = 0;

    public static readonly StableToken RoleToken = new("perf.member");
    public static readonly StableToken Active = new("active");

    public static void ValidateCanonicalContract()
    {
        Qa04SocietyGovernanceReferenceDecompositionV1.ValidateCanonicalContract();
        Qa04SocietyGovernanceCanonicalAuthorityV1.ValidateCanonicalContract();
        Qa04SocietyOrganizationResolvedMaterializerV1.ValidateCanonicalContract();
        Qa04ReferenceWorldMaterializerV1.ValidateCanonicalContract();

        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(SocietyMembershipRolePayloadV1.PartitionId);
        if (slice.StartOrdinal != 10_000 || slice.Count != CanonicalCount || slice.UsesSpecializedIdentity)
            throw new InvalidDataException("qa04.society.membership-role-slice-drift");
        if (OrganizationCount != Qa04SocietyOrganizationResolvedMaterializerV1.CanonicalCount ||
            checked(OrganizationCount * MembershipsPerOrganization) != CanonicalCount ||
            CanonicalCount > Qa04ReferenceWorldMaterializerV1.CanonicalResidentCount)
            throw new InvalidDataException("qa04.society.membership-role-cardinality-drift");

        var identity = StandardDomainPartitionRegistry.Get(SocietyMembershipRolePayloadV1.PartitionId);
        if (identity.OwnerDomain.Value != "society_economy")
            throw new InvalidDataException("qa04.society.membership-role-owner-drift");
    }

    public static Qa04SocietyMembershipRoleCanonicalMaterializationV1 MaterializeCanonical()
    {
        ValidateCanonicalContract();

        var organizationRecords = Qa04SocietyOrganizationResolvedMaterializerV1.MaterializeResolved(
                static _ => Qa04SocietyGovernanceCanonicalAuthorityV1.OrganizationClass)
            .ToArray();
        var organizationIdentity = StandardDomainPartitionRegistry.Get(SocietyOrganizationPayloadV1.PartitionId);
        var organizations = new DomainPartitionStateV1<SocietyOrganizationPayloadV1>(organizationIdentity, organizationRecords);
        if (organizations.ItemCount != OrganizationCount)
            throw new InvalidDataException("qa04.society.membership-role-organization-count-drift");

        var references = new CanonicalReferenceResolver();
        var organizationsById = organizationRecords.ToDictionary(static record => record.RecordId);
        foreach (var organization in organizationRecords)
            references.Add(
                new PartitionRecordRefV1(SocietyOrganizationPayloadV1.PartitionId, organization.RecordId),
                organization.RecordSchema);

        var residentRefs = new PartitionRecordRefV1[checked((int)CanonicalCount)];
        for (ulong i = 0; i < CanonicalCount; i++)
        {
            var resident = Qa04ReferenceWorldMaterializerV1.CreateResidentRecord(i);
            var reference = new PartitionRecordRefV1(ResidentIdentityLifecyclePayloadV1.PartitionId, resident.RecordId);
            references.Add(reference, resident.RecordSchema);
            residentRefs[checked((int)i)] = reference;
        }

        var validator = new StandardDomainPayloadCodecValidatorV1();
        var membershipIdentity = StandardDomainPartitionRegistry.Get(SocietyMembershipRolePayloadV1.PartitionId);
        var membershipSlice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(SocietyMembershipRolePayloadV1.PartitionId);
        var organizationSlice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(SocietyOrganizationPayloadV1.PartitionId);
        var records = new DomainRecordEnvelopeV1<SocietyMembershipRolePayloadV1>[checked((int)CanonicalCount)];
        var relations = new HashSet<(PartitionRecordRefV1 Organization, PartitionRecordRefV1 Member)>();

        for (ulong i = 0; i < CanonicalCount; i++)
        {
            var organizationOrdinal = i % OrganizationCount;
            var organizationBinding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(
                checked(organizationSlice.StartOrdinal + organizationOrdinal));
            if (!organizationsById.TryGetValue(organizationBinding.Descriptor.RecordId, out var organization))
                throw new InvalidDataException("qa04.society.membership-role-organization-authority-missing");

            var organizationRef = new PartitionRecordRefV1(SocietyOrganizationPayloadV1.PartitionId, organization.RecordId);
            var memberRef = residentRefs[checked((int)i)];
            if (!relations.Add((organizationRef, memberRef)))
                throw new InvalidDataException("qa04.society.membership-role-relation-duplicate");

            var payload = new SocietyMembershipRolePayloadV1(
                organizationRef,
                memberRef,
                new[] { RoleToken },
                Array.Empty<StableToken>(),
                JoinedStep,
                EndedStep: null,
                Active);
            validator.Validate(SocietyMembershipRolePayloadV1.PartitionId, payload.ToStandardPayload(), references);

            var binding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(checked(membershipSlice.StartOrdinal + i));
            if (binding.PartitionId.Value != SocietyMembershipRolePayloadV1.PartitionId ||
                binding.PartitionLocalOrdinal != i || binding.UsesSpecializedIdentity)
                throw new InvalidDataException("qa04.society.membership-role-descriptor-binding-drift");

            records[checked((int)i)] = new DomainRecordEnvelopeV1<SocietyMembershipRolePayloadV1>(
                binding.Descriptor.RecordId,
                membershipIdentity.RecordSchema,
                revision: 1,
                createdStep: 0,
                retiredStep: null,
                binding.Descriptor.DetailLevel,
                lineageRef: null,
                payload);
        }

        ValidateRelationUniqueness(records);
        var membershipRoles = new DomainPartitionStateV1<SocietyMembershipRolePayloadV1>(membershipIdentity, records);
        if (membershipRoles.ItemCount != CanonicalCount)
            throw new InvalidDataException("qa04.society.membership-role-partition-count-drift");

        return new Qa04SocietyMembershipRoleCanonicalMaterializationV1(
            organizations,
            membershipRoles,
            Array.AsReadOnly(records),
            references);
    }

    public static void ValidateCanonicalRecord(
        ulong localOrdinal,
        DomainRecordEnvelopeV1<SocietyMembershipRolePayloadV1> record,
        IDomainRecordSchemaResolverV1 references)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(references);
        if (localOrdinal >= CanonicalCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));

        var membershipSlice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(SocietyMembershipRolePayloadV1.PartitionId);
        var binding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(checked(membershipSlice.StartOrdinal + localOrdinal));
        var organizationSlice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(SocietyOrganizationPayloadV1.PartitionId);
        var organizationBinding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(
            checked(organizationSlice.StartOrdinal + (localOrdinal % OrganizationCount)));
        var resident = Qa04ReferenceWorldMaterializerV1.CreateResidentRecord(localOrdinal);
        var expectedOrganization = new PartitionRecordRefV1(SocietyOrganizationPayloadV1.PartitionId, organizationBinding.Descriptor.RecordId);
        var expectedMember = new PartitionRecordRefV1(ResidentIdentityLifecyclePayloadV1.PartitionId, resident.RecordId);
        var identity = StandardDomainPartitionRegistry.Get(SocietyMembershipRolePayloadV1.PartitionId);

        if (record.RecordId != binding.Descriptor.RecordId ||
            record.RecordSchema != identity.RecordSchema ||
            record.Revision != 1 || record.CreatedStep != 0 || record.RetiredStep is not null ||
            record.DetailLevel != binding.Descriptor.DetailLevel || record.LineageRef is not null)
            throw new InvalidDataException("qa04.society.membership-role-envelope-drift");

        var payload = record.Payload;
        if (payload.OrganizationRef != expectedOrganization ||
            payload.MemberRef != expectedMember ||
            payload.RoleTokens.Count != 1 || payload.RoleTokens[0] != RoleToken ||
            payload.AuthorityTokens.Count != 0 ||
            payload.JoinedStep != JoinedStep || payload.EndedStep is not null ||
            payload.Status != Active)
            throw new InvalidDataException("qa04.society.membership-role-genesis-drift");

        new StandardDomainPayloadCodecValidatorV1().Validate(
            SocietyMembershipRolePayloadV1.PartitionId,
            payload.ToStandardPayload(),
            references);
    }

    public static void ValidateRelationUniqueness(
        IEnumerable<DomainRecordEnvelopeV1<SocietyMembershipRolePayloadV1>> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var ids = new HashSet<OpaqueId128>();
        var relations = new HashSet<(PartitionRecordRefV1 Organization, PartitionRecordRefV1 Member)>();
        foreach (var record in records)
        {
            if (!ids.Add(record.RecordId))
                throw new InvalidDataException("qa04.society.membership-role-record-id-duplicate");
            if (!relations.Add((record.Payload.OrganizationRef, record.Payload.MemberRef)))
                throw new InvalidDataException("qa04.society.membership-role-relation-duplicate");
        }
    }

    private sealed class CanonicalReferenceResolver : IDomainRecordSchemaResolverV1
    {
        private readonly Dictionary<PartitionRecordRefV1, SchemaRefV1> _records = new();

        public void Add(PartitionRecordRefV1 reference, SchemaRefV1 schema)
        {
            if (reference.RecordId.IsZero)
                throw new InvalidDataException("qa04.society.membership-role-reference-zero");
            if (!_records.TryAdd(reference, schema) && _records[reference] != schema)
                throw new InvalidDataException("qa04.society.membership-role-reference-schema-conflict");
        }

        public bool Exists(PartitionRecordRefV1 reference) => _records.ContainsKey(reference);

        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
            => _records.TryGetValue(reference, out schema);
    }
}
