using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04SocietySocialRelationsCanonicalAuthoritySmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04SocietySocialRelationsCanonicalAuthorityV1.ValidateCanonicalContract();
        var materialization = Qa04SocietySocialRelationsCanonicalAuthorityV1.MaterializeCanonical();

        Require(materialization.MaterializedRecordCount == 85_000 &&
                materialization.Organizations.ItemCount == 10_000 &&
                materialization.Educations.ItemCount == 25_000 &&
                materialization.Cultures.ItemCount == 30_000 &&
                materialization.Reputations.ItemCount == 30_000,
            "Canonical Society social-relations authority must materialize exactly 85,000 approved records.");

        var education = materialization.EducationRecordsByOrdinal;
        Require(education.Select(static record => record.Payload.LearnerRef).Distinct().Count() == 25_000,
            "Education must use 25,000 unique canonical Resident learners.");
        var providers = education.GroupBy(static record => record.Payload.ProviderRef).ToArray();
        Require(providers.Length == 10_000 &&
                providers.Count(static group => group.Count() == 3) == 5_000 &&
                providers.Count(static group => group.Count() == 2) == 5_000,
            "Education provider distribution must be exact 5,000x3 + 5,000x2.");
        Require(education.All(static record =>
                record.Payload.ProviderRef.PartitionId.Value == SocietyOrganizationPayloadV1.PartitionId &&
                record.Payload.LearnerRef.PartitionId.Value == ResidentIdentityLifecyclePayloadV1.PartitionId &&
                record.Payload.ProgramToken.Value == "perf.education-program" &&
                record.Payload.Status.Value == "active" && record.Payload.ProgressPpm == 0 &&
                record.Payload.SkillRefs.Count == 0 && record.Payload.StartedStep == 0 &&
                record.Payload.EndedStep is null),
            "Education approved genesis payload drifted.");

        var culture = materialization.CultureRecordsByOrdinal;
        Require(culture.GroupBy(static record => record.Payload.SubjectRef).Count() == 10_000 &&
                culture.GroupBy(static record => record.Payload.SubjectRef).All(static group => group.Count() == 3),
            "Culture must bind exactly three approved traits per Organization.");
        Require(culture.GroupBy(static record => record.Payload.TraitToken.Value).Count() == 3 &&
                culture.GroupBy(static record => record.Payload.TraitToken.Value).All(static group => group.Count() == 10_000),
            "Culture must use each approved trait exactly 10,000 times.");
        Require(culture.All(static record =>
                record.Payload.SubjectRef.PartitionId.Value == SocietyOrganizationPayloadV1.PartitionId &&
                record.Payload.AffiliationPpm == 1_000_000 && record.Payload.AdoptionStep == 0 &&
                record.Payload.SourceRefs.Count == 0 && record.Payload.Status.Value == "active"),
            "Culture approved genesis payload drifted.");

        var reputation = materialization.ReputationRecordsByOrdinal;
        Require(reputation.GroupBy(static record => record.Payload.SubjectRef).Count() == 10_000 &&
                reputation.GroupBy(static record => record.Payload.SubjectRef).All(static group => group.Count() == 3),
            "Reputation must bind exactly three approved dimensions per Organization.");
        Require(reputation.GroupBy(static record => record.Payload.DimensionToken.Value).Count() == 3 &&
                reputation.GroupBy(static record => record.Payload.DimensionToken.Value).All(static group => group.Count() == 10_000),
            "Reputation must use each approved dimension exactly 10,000 times.");
        Require(reputation.All(static record =>
                record.Payload.SubjectRef.PartitionId.Value == SocietyOrganizationPayloadV1.PartitionId &&
                record.Payload.AudienceScopeRef is null && record.Payload.Score == 0 &&
                record.Payload.ConfidencePpm == 0 && record.Payload.EvidenceRefs.Count == 0 &&
                record.Payload.UpdatedStep == 0),
            "Reputation approved genesis payload drifted.");

        Qa04SocietySocialRelationsCanonicalAuthorityV1.ValidateSecondaryIndexes(
            materialization.Educations,
            materialization.Cultures,
            materialization.Reputations);
        var recovered = Qa04SocietySocialRelationsSnapshotRecoveryEvidenceV1.Verify(materialization);
        Require(recovered == 85_000,
            "Society social-relations Snapshot/recovery must semantically recover all 85,000 records.");

        var firstEducation = education[0];
        ExpectInvalid(() => new StandardDomainPayloadCodecValidatorV1().Validate(
                SocietyEducationPayloadV1.PartitionId,
                firstEducation.Payload.ToStandardPayload(),
                new EmptyReferenceResolver()),
            "Education must fail closed without actual Organization/Resident authority.");
        ExpectInvalid(() => Qa04SocietySocialRelationsCanonicalAuthorityV1.ValidateEducationRecord(
                0,
                CopyWithPayload(firstEducation, firstEducation.Payload with { ProgramToken = new StableToken("invalid-program") }),
                materialization.References),
            "Education Token drift must fail closed.");
        ExpectInvalid(() => Qa04SocietySocialRelationsCanonicalAuthorityV1.ValidateEducationRecord(
                0,
                CopyWithPayload(firstEducation, firstEducation.Payload with { ProgressPpm = 1 }),
                materialization.References),
            "Education progress drift must fail closed.");

        var firstCulture = culture[0];
        ExpectInvalid(() => Qa04SocietySocialRelationsCanonicalAuthorityV1.ValidateCultureRecord(
                0,
                CopyWithPayload(firstCulture, firstCulture.Payload with { TraitToken = new StableToken("invalid-trait") }),
                materialization.References),
            "Culture Token drift must fail closed.");
        ExpectInvalid(() => Qa04SocietySocialRelationsCanonicalAuthorityV1.ValidateCultureRecord(
                0,
                CopyWithPayload(firstCulture, firstCulture.Payload with { SourceRefs = new[] { firstCulture.Payload.SubjectRef } }),
                materialization.References),
            "Culture source injection must fail closed.");

        var firstReputation = reputation[0];
        ExpectInvalid(() => Qa04SocietySocialRelationsCanonicalAuthorityV1.ValidateReputationRecord(
                0,
                CopyWithPayload(firstReputation, firstReputation.Payload with { DimensionToken = new StableToken("invalid-dimension") }),
                materialization.References),
            "Reputation dimension drift must fail closed.");
        ExpectInvalid(() => Qa04SocietySocialRelationsCanonicalAuthorityV1.ValidateReputationRecord(
                0,
                CopyWithPayload(firstReputation, firstReputation.Payload with { ConfidencePpm = 1 }),
                materialization.References),
            "Reputation confidence drift must fail closed.");

        var duplicateEducation = CopyWithPayload(education[1], education[1].Payload with
        {
            ProviderRef = education[0].Payload.ProviderRef,
            LearnerRef = education[0].Payload.LearnerRef,
        });
        ExpectInvalid(() => Qa04SocietySocialRelationsCanonicalAuthorityV1.ValidateEducationUniqueness(
                new[] { education[0], duplicateEducation }),
            "Duplicate Education relation must fail closed.");

        var duplicateCulture = CopyWithPayload(culture[1], culture[1].Payload with
        {
            SubjectRef = culture[0].Payload.SubjectRef,
            TraitToken = culture[0].Payload.TraitToken,
        });
        ExpectInvalid(() => Qa04SocietySocialRelationsCanonicalAuthorityV1.ValidateCultureUniqueness(
                new[] { culture[0], duplicateCulture }),
            "Duplicate Culture key must fail closed.");

        var duplicateReputation = CopyWithPayload(reputation[1], reputation[1].Payload with
        {
            SubjectRef = reputation[0].Payload.SubjectRef,
            DimensionToken = reputation[0].Payload.DimensionToken,
        });
        ExpectInvalid(() => Qa04SocietySocialRelationsCanonicalAuthorityV1.ValidateReputationUniqueness(
                new[] { reputation[0], duplicateReputation }),
            "Duplicate Reputation key must fail closed.");
    }

    private static DomainRecordEnvelopeV1<TPayload> CopyWithPayload<TPayload>(
        DomainRecordEnvelopeV1<TPayload> record,
        TPayload payload)
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
