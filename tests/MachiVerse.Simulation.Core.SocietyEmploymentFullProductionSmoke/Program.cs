using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void ExpectInvalid(Action action, string message)
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

static DomainRecordEnvelopeV1<SocietyEmploymentPayloadV1> CopyWithPayload(
    DomainRecordEnvelopeV1<SocietyEmploymentPayloadV1> record,
    SocietyEmploymentPayloadV1 payload)
    => new(
        record.RecordId,
        record.RecordSchema,
        record.Revision,
        record.CreatedStep,
        record.RetiredStep,
        record.DetailLevel,
        record.LineageRef,
        payload);

Console.WriteLine("Validating approved Society Employment authority (80,000 records)...");
Qa04SocietyEmploymentCanonicalAuthorityV1.ValidateCanonicalContract();
var materialization = Qa04SocietyEmploymentCanonicalAuthorityV1.MaterializeCanonical();
var records = materialization.RecordsByOrdinal;

Require(materialization.MaterializedRecordCount == 80_000 &&
        materialization.Employments.ItemCount == 80_000 &&
        materialization.Organizations.ItemCount == 10_000 &&
        materialization.MembershipRoles.ItemCount == 80_000,
    "Society Employment production materialization counts drifted.");

Require(records.Select(static record => record.Payload.WorkerRef).Distinct().Count() == 80_000,
    "Society Employment must use 80,000 unique canonical Resident workers.");
var employerGroups = records.GroupBy(static record => record.Payload.EmployerRef).ToArray();
Require(employerGroups.Length == 10_000 && employerGroups.All(static group => group.Count() == 8),
    "Society Employment must assign exactly eight records to every Organization.");
Require(records.Select(static record => (record.Payload.EmployerRef, record.Payload.WorkerRef)).Distinct().Count() == 80_000,
    "Society Employment employer/worker relations must be unique.");
Require(records.All(static record =>
        record.Payload.JobToken.Value == "perf.worker" &&
        record.Payload.Status.Value == "active" &&
        record.Payload.StartedStep == 0 && record.Payload.EndedStep is null &&
        record.Payload.WageMicrounitPerPeriod == 1_000_000 &&
        record.Payload.PayPeriodSteps == 1 &&
        record.Payload.ObligationRefs.Count == 0),
    "Society Employment approved benchmark payload drifted.");

Qa04SocietyEmploymentCanonicalAuthorityV1.ValidateMembershipPairCoherence(
    records,
    materialization.MembershipRecordsByOrdinal);
Qa04SocietyEmploymentCanonicalAuthorityV1.ValidateSecondaryIndexes(materialization.Employments);

var recovered = Qa04SocietyEmploymentSnapshotRecoveryEvidenceV1.Verify(materialization);
Require(recovered == 80_000,
    "Society Employment Snapshot/recovery must semantically recover all 80,000 records.");

var first = records[0];
ExpectInvalid(() => new StandardDomainPayloadCodecValidatorV1().Validate(
        SocietyEmploymentPayloadV1.PartitionId,
        first.Payload.ToStandardPayload(),
        new EmptyReferenceResolver()),
    "Employment must fail closed without upstream Organization/Resident authority.");

ExpectInvalid(() => Qa04SocietyEmploymentCanonicalAuthorityV1.ValidateCanonicalRecord(
        0,
        CopyWithPayload(first, first.Payload with { EmployerRef = records[1].Payload.EmployerRef }),
        materialization.References),
    "Employment employer mapping drift must fail closed.");

ExpectInvalid(() => Qa04SocietyEmploymentCanonicalAuthorityV1.ValidateCanonicalRecord(
        0,
        CopyWithPayload(first, first.Payload with
        {
            WorkerRef = new PartitionRecordRefV1(SocietyOrganizationPayloadV1.PartitionId, first.Payload.WorkerRef.RecordId),
        }),
        materialization.References),
    "Employment worker Ref partition drift must fail closed.");

ExpectInvalid(() => Qa04SocietyEmploymentCanonicalAuthorityV1.ValidateCanonicalRecord(
        0,
        CopyWithPayload(first, first.Payload with { JobToken = new StableToken("invalid-job") }),
        materialization.References),
    "Employment job Token drift must fail closed.");
ExpectInvalid(() => Qa04SocietyEmploymentCanonicalAuthorityV1.ValidateCanonicalRecord(
        0,
        CopyWithPayload(first, first.Payload with { Status = new StableToken("inactive") }),
        materialization.References),
    "Employment status drift must fail closed.");
ExpectInvalid(() => Qa04SocietyEmploymentCanonicalAuthorityV1.ValidateCanonicalRecord(
        0,
        CopyWithPayload(first, first.Payload with { StartedStep = 1UL }),
        materialization.References),
    "Employment started_step drift must fail closed.");
ExpectInvalid(() => Qa04SocietyEmploymentCanonicalAuthorityV1.ValidateCanonicalRecord(
        0,
        CopyWithPayload(first, first.Payload with { EndedStep = 1UL }),
        materialization.References),
    "Employment ended_step drift must fail closed.");
ExpectInvalid(() => Qa04SocietyEmploymentCanonicalAuthorityV1.ValidateCanonicalRecord(
        0,
        CopyWithPayload(first, first.Payload with { WageMicrounitPerPeriod = 999_999L }),
        materialization.References),
    "Employment wage drift must fail closed.");
ExpectInvalid(() => Qa04SocietyEmploymentCanonicalAuthorityV1.ValidateCanonicalRecord(
        0,
        CopyWithPayload(first, first.Payload with { PayPeriodSteps = 2UL }),
        materialization.References),
    "Employment pay-period drift must fail closed.");
ExpectInvalid(() => Qa04SocietyEmploymentCanonicalAuthorityV1.ValidateCanonicalRecord(
        0,
        CopyWithPayload(first, first.Payload with { ObligationRefs = new[] { first.Payload.EmployerRef } }),
        materialization.References),
    "Employment obligation injection must fail closed.");

var duplicateRelations = records.ToArray();
duplicateRelations[1] = CopyWithPayload(duplicateRelations[1], duplicateRelations[1].Payload with
{
    EmployerRef = duplicateRelations[0].Payload.EmployerRef,
    WorkerRef = duplicateRelations[0].Payload.WorkerRef,
});
ExpectInvalid(() => Qa04SocietyEmploymentCanonicalAuthorityV1.ValidateUniqueness(duplicateRelations),
    "Duplicate Employment relation must fail closed at full population.");

var coherenceDrift = records.ToArray();
coherenceDrift[1] = CopyWithPayload(coherenceDrift[1], coherenceDrift[1].Payload with
{
    EmployerRef = coherenceDrift[2].Payload.EmployerRef,
});
ExpectInvalid(() => Qa04SocietyEmploymentCanonicalAuthorityV1.ValidateMembershipPairCoherence(
        coherenceDrift,
        materialization.MembershipRecordsByOrdinal),
    "Employment/MembershipRole pair-set drift must fail closed at full population.");

Console.WriteLine($"society-employment-full-production-pass records={materialization.MaterializedRecordCount} recovered={recovered} employers={employerGroups.Length} workers={records.Select(static record => record.Payload.WorkerRef).Distinct().Count()}");

sealed class EmptyReferenceResolver : IDomainRecordSchemaResolverV1
{
    public bool Exists(PartitionRecordRefV1 reference) => false;

    public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
    {
        schema = default;
        return false;
    }
}
