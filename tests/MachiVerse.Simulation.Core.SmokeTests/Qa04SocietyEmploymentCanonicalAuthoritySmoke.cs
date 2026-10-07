using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04SocietyEmploymentCanonicalAuthoritySmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04SocietyEmploymentCanonicalAuthorityV1.ValidateCanonicalContract();
        var materialization = Qa04SocietyEmploymentCanonicalAuthorityV1.MaterializeCanonical();
        var records = materialization.RecordsByOrdinal;

        Require(materialization.MaterializedRecordCount == 80_000 &&
                materialization.Employments.ItemCount == 80_000 &&
                materialization.Organizations.ItemCount == 10_000 &&
                materialization.MembershipRoles.ItemCount == 80_000 &&
                records.Count == 80_000,
            "Canonical society.employment authority must materialize exactly 80,000 records over actual upstream authority.");

        Require(records.Select(static record => record.Payload.WorkerRef).Distinct().Count() == 80_000,
            "Canonical Employment must bind each Resident ordinal 0..79,999 exactly once.");
        var employers = records.GroupBy(static record => record.Payload.EmployerRef).ToArray();
        Require(employers.Length == 10_000 && employers.All(static group => group.Count() == 8),
            "Every canonical Organization must have exactly eight benchmark Employments.");
        Require(records.Select(static record => (record.Payload.EmployerRef, record.Payload.WorkerRef)).Distinct().Count() == 80_000,
            "Canonical Employment employer/worker relations must be unique.");

        Require(records.All(static record =>
                record.Payload.EmployerRef.PartitionId.Value == SocietyOrganizationPayloadV1.PartitionId &&
                record.Payload.WorkerRef.PartitionId.Value == ResidentIdentityLifecyclePayloadV1.PartitionId &&
                record.Payload.JobToken.Value == "perf.worker" &&
                record.Payload.Status.Value == "active" &&
                record.Payload.StartedStep == 0 && record.Payload.EndedStep is null &&
                record.Payload.WageMicrounitPerPeriod == 1_000_000 &&
                record.Payload.PayPeriodSteps == 1 &&
                record.Payload.ObligationRefs.Count == 0),
            "Canonical Employment genesis payload drifted.");

        Qa04SocietyEmploymentCanonicalAuthorityV1.ValidateMembershipPairCoherence(
            records,
            materialization.MembershipRecordsByOrdinal);
        Qa04SocietyEmploymentCanonicalAuthorityV1.ValidateSecondaryIndexes(materialization.Employments);

        var recovered = Qa04SocietyEmploymentSnapshotRecoveryEvidenceV1.Verify(materialization);
        Require(recovered == 80_000,
            "Canonical Employment Snapshot/recovery must semantically recover all 80,000 records.");

        var first = records[0];
        ExpectInvalid(() => new StandardDomainPayloadCodecValidatorV1().Validate(
                SocietyEmploymentPayloadV1.PartitionId,
                first.Payload.ToStandardPayload(),
                new EmptyReferenceResolver()),
            "Missing Organization/Resident authority must fail closed.");

        var wrongEmployer = first.Payload with { EmployerRef = records[1].Payload.EmployerRef };
        ExpectInvalid(() => Qa04SocietyEmploymentCanonicalAuthorityV1.ValidateCanonicalRecord(
                0, CopyWithPayload(first, wrongEmployer), materialization.References),
            "Wrong Employment employer mapping must fail closed.");

        var wrongWorkerPartition = first.Payload with
        {
            WorkerRef = new PartitionRecordRefV1(SocietyOrganizationPayloadV1.PartitionId, first.Payload.WorkerRef.RecordId),
        };
        ExpectInvalid(() => Qa04SocietyEmploymentCanonicalAuthorityV1.ValidateCanonicalRecord(
                0, CopyWithPayload(first, wrongWorkerPartition), materialization.References),
            "Wrong Employment worker partition must fail closed.");

        var badJob = first.Payload with { JobToken = new StableToken("invalid-job") };
        ExpectInvalid(() => Qa04SocietyEmploymentCanonicalAuthorityV1.ValidateCanonicalRecord(
                0, CopyWithPayload(first, badJob), materialization.References),
            "Employment job Token drift must fail closed.");

        var badStatus = first.Payload with { Status = new StableToken("inactive") };
        ExpectInvalid(() => Qa04SocietyEmploymentCanonicalAuthorityV1.ValidateCanonicalRecord(
                0, CopyWithPayload(first, badStatus), materialization.References),
            "Employment status drift must fail closed.");

        var lateStart = first.Payload with { StartedStep = 1UL };
        ExpectInvalid(() => Qa04SocietyEmploymentCanonicalAuthorityV1.ValidateCanonicalRecord(
                0, CopyWithPayload(first, lateStart), materialization.References),
            "Employment started_step drift must fail closed.");

        var ended = first.Payload with { EndedStep = 1UL };
        ExpectInvalid(() => Qa04SocietyEmploymentCanonicalAuthorityV1.ValidateCanonicalRecord(
                0, CopyWithPayload(first, ended), materialization.References),
            "Employment ended_step drift must fail closed.");

        var badWage = first.Payload with { WageMicrounitPerPeriod = 999_999L };
        ExpectInvalid(() => Qa04SocietyEmploymentCanonicalAuthorityV1.ValidateCanonicalRecord(
                0, CopyWithPayload(first, badWage), materialization.References),
            "Employment wage drift must fail closed.");

        var badPeriod = first.Payload with { PayPeriodSteps = 2UL };
        ExpectInvalid(() => Qa04SocietyEmploymentCanonicalAuthorityV1.ValidateCanonicalRecord(
                0, CopyWithPayload(first, badPeriod), materialization.References),
            "Employment pay-period drift must fail closed.");

        var injectedObligation = first.Payload with
        {
            ObligationRefs = new[] { first.Payload.EmployerRef },
        };
        ExpectInvalid(() => Qa04SocietyEmploymentCanonicalAuthorityV1.ValidateCanonicalRecord(
                0, CopyWithPayload(first, injectedObligation), materialization.References),
            "Employment obligation injection must fail closed.");

        var second = records[1];
        var duplicateRelation = CopyWithPayload(second, second.Payload with
        {
            EmployerRef = first.Payload.EmployerRef,
            WorkerRef = first.Payload.WorkerRef,
        });
        ExpectInvalid(() => Qa04SocietyEmploymentCanonicalAuthorityV1.ValidateUniqueness(
                new[] { first, duplicateRelation }),
            "Duplicate Employment relation must fail closed.");
        ExpectInvalid(() => Qa04SocietyEmploymentCanonicalAuthorityV1.ValidateUniqueness(
                new[] { first, first }),
            "Duplicate Employment RecordId must fail closed.");

        var coherenceDrift = CopyWithPayload(second, second.Payload with
        {
            EmployerRef = records[2].Payload.EmployerRef,
        });
        ExpectInvalid(() => Qa04SocietyEmploymentCanonicalAuthorityV1.ValidateMembershipPairCoherence(
                new[] { first, coherenceDrift },
                materialization.MembershipRecordsByOrdinal.Take(2)),
            "Employment/MembershipRole endpoint-pair drift must fail closed.");
    }

    private static DomainRecordEnvelopeV1<SocietyEmploymentPayloadV1> CopyWithPayload(
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
