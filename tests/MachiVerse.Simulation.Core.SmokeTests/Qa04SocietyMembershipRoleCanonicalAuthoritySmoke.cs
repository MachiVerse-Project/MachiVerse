using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04SocietyMembershipRoleCanonicalAuthoritySmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04SocietyMembershipRoleCanonicalAuthorityV1.ValidateCanonicalContract();
        var materialization = Qa04SocietyMembershipRoleCanonicalAuthorityV1.MaterializeCanonical();
        var records = materialization.RecordsByOrdinal;

        Require(materialization.MaterializedRecordCount == 80_000 &&
                materialization.MembershipRoles.ItemCount == 80_000 &&
                materialization.Organizations.ItemCount == 10_000 &&
                records.Count == 80_000,
            "Canonical society.membership_role authority must materialize exactly 80,000 records over 10,000 Organizations.");

        Require(records.Select(static record => record.Payload.MemberRef).Distinct().Count() == 80_000,
            "Canonical MembershipRole must bind each Resident ordinal 0..79,999 exactly once.");
        var organizations = records.GroupBy(static record => record.Payload.OrganizationRef).ToArray();
        Require(organizations.Length == 10_000 && organizations.All(static group => group.Count() == 8),
            "Every canonical Organization must have exactly eight benchmark memberships.");
        Require(records.Select(static record => (record.Payload.OrganizationRef, record.Payload.MemberRef)).Distinct().Count() == 80_000,
            "Canonical MembershipRole organization/member relations must be unique.");

        Require(records.All(static record =>
                record.Payload.OrganizationRef.PartitionId.Value == SocietyOrganizationPayloadV1.PartitionId &&
                record.Payload.MemberRef.PartitionId.Value == ResidentIdentityLifecyclePayloadV1.PartitionId &&
                record.Payload.RoleTokens.Count == 1 && record.Payload.RoleTokens[0].Value == "perf.member" &&
                record.Payload.AuthorityTokens.Count == 0 &&
                record.Payload.JoinedStep == 0 &&
                record.Payload.EndedStep is null &&
                record.Payload.Status.Value == "active"),
            "Canonical MembershipRole genesis payload drifted.");

        var recovered = Qa04SocietyMembershipRoleSnapshotRecoveryEvidenceV1.Verify(materialization);
        Require(recovered == 80_000,
            "Canonical MembershipRole Snapshot/recovery must semantically recover all 80,000 records.");

        var first = records[0];
        ExpectInvalid(() => new StandardDomainPayloadCodecValidatorV1().Validate(
                SocietyMembershipRolePayloadV1.PartitionId,
                first.Payload.ToStandardPayload(),
                new EmptyReferenceResolver()),
            "Missing Organization/Resident authority must fail closed.");

        var missingOrganization = first.Payload with
        {
            OrganizationRef = new PartitionRecordRefV1(SocietyOrganizationPayloadV1.PartitionId, OpaqueId128.Parse("ffffffffffffffffffffffffffffffff")),
        };
        ExpectInvalid(() => Qa04SocietyMembershipRoleCanonicalAuthorityV1.ValidateCanonicalRecord(
                0, CopyWithPayload(first, missingOrganization), materialization.References),
            "Wrong Organization target must fail closed.");

        var wrongMemberPartition = first.Payload with
        {
            MemberRef = new PartitionRecordRefV1(SocietyOrganizationPayloadV1.PartitionId, first.Payload.MemberRef.RecordId),
        };
        ExpectInvalid(() => Qa04SocietyMembershipRoleCanonicalAuthorityV1.ValidateCanonicalRecord(
                0, CopyWithPayload(first, wrongMemberPartition), materialization.References),
            "Wrong member target partition must fail closed.");

        var badRole = first.Payload with { RoleTokens = new[] { new StableToken("invalid-role") } };
        ExpectInvalid(() => Qa04SocietyMembershipRoleCanonicalAuthorityV1.ValidateCanonicalRecord(
                0, CopyWithPayload(first, badRole), materialization.References),
            "MembershipRole role Token drift must fail closed.");

        var badAuthority = first.Payload with { AuthorityTokens = new[] { new StableToken("unexpected-authority") } };
        ExpectInvalid(() => Qa04SocietyMembershipRoleCanonicalAuthorityV1.ValidateCanonicalRecord(
                0, CopyWithPayload(first, badAuthority), materialization.References),
            "MembershipRole authority list drift must fail closed.");

        var ended = first.Payload with { EndedStep = 1UL };
        ExpectInvalid(() => Qa04SocietyMembershipRoleCanonicalAuthorityV1.ValidateCanonicalRecord(
                0, CopyWithPayload(first, ended), materialization.References),
            "MembershipRole ended_step genesis drift must fail closed.");

        var badStatus = first.Payload with { Status = new StableToken("inactive") };
        ExpectInvalid(() => Qa04SocietyMembershipRoleCanonicalAuthorityV1.ValidateCanonicalRecord(
                0, CopyWithPayload(first, badStatus), materialization.References),
            "MembershipRole status drift must fail closed.");

        var second = records[1];
        var duplicateRelation = CopyWithPayload(second, second.Payload with
        {
            OrganizationRef = first.Payload.OrganizationRef,
            MemberRef = first.Payload.MemberRef,
        });
        ExpectInvalid(() => Qa04SocietyMembershipRoleCanonicalAuthorityV1.ValidateRelationUniqueness(
                new[] { first, duplicateRelation }),
            "Duplicate MembershipRole relation must fail closed.");
        ExpectInvalid(() => Qa04SocietyMembershipRoleCanonicalAuthorityV1.ValidateRelationUniqueness(
                new[] { first, first }),
            "Duplicate MembershipRole RecordId must fail closed.");
    }

    private static DomainRecordEnvelopeV1<SocietyMembershipRolePayloadV1> CopyWithPayload(
        DomainRecordEnvelopeV1<SocietyMembershipRolePayloadV1> record,
        SocietyMembershipRolePayloadV1 payload)
        => new(
            record.RecordId,
            record.RecordSchema,
            record.Revision,
            record.CreatedStep,
            record.RetiredStep,
            record.DetailLevel,
            record.LineageRef,
            payload);

    private static void ExpectInvalid(Action action, string message)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or ArgumentOutOfRangeException)
        {
            return;
        }
        throw new InvalidOperationException(message);
    }

    private sealed class EmptyReferenceResolver : IDomainRecordSchemaResolverV1
    {
        public bool Exists(PartitionRecordRefV1 reference) => false;

        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
        {
            schema = default;
            return false;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
