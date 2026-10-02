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

static DomainRecordEnvelopeV1<TPayload> CopyWithPayload<TPayload>(
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

Console.WriteLine("Validating approved Society social-relations authority (85,000 records)...");
Qa04SocietySocialRelationsCanonicalAuthorityV1.ValidateCanonicalContract();
var materialization = Qa04SocietySocialRelationsCanonicalAuthorityV1.MaterializeCanonical();

Require(materialization.MaterializedRecordCount == 85_000 &&
        materialization.Organizations.ItemCount == 10_000 &&
        materialization.Educations.ItemCount == 25_000 &&
        materialization.Cultures.ItemCount == 30_000 &&
        materialization.Reputations.ItemCount == 30_000,
    "Society social-relations production materialization counts drifted.");

var education = materialization.EducationRecordsByOrdinal;
Require(education.Count == 25_000 &&
        education.Select(static record => record.Payload.LearnerRef).Distinct().Count() == 25_000 &&
        education.Select(static record => (record.Payload.ProviderRef, record.Payload.LearnerRef)).Distinct().Count() == 25_000,
    "Education learner/reference relation cardinality drifted.");
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
    "Education approved benchmark payload drifted.");

var culture = materialization.CultureRecordsByOrdinal;
Require(culture.Count == 30_000 &&
        culture.Select(static record => (record.Payload.SubjectRef, record.Payload.TraitToken)).Distinct().Count() == 30_000,
    "Culture subject/trait keys must be unique.");
var cultureSubjects = culture.GroupBy(static record => record.Payload.SubjectRef).ToArray();
var cultureTraits = culture.GroupBy(static record => record.Payload.TraitToken.Value).ToArray();
Require(cultureSubjects.Length == 10_000 && cultureSubjects.All(static group => group.Count() == 3) &&
        cultureTraits.Length == 3 && cultureTraits.All(static group => group.Count() == 10_000),
    "Culture must provide exactly three approved traits per Organization and 10,000 uses per trait.");
Require(culture.All(static record =>
        record.Payload.SubjectRef.PartitionId.Value == SocietyOrganizationPayloadV1.PartitionId &&
        record.Payload.TraitToken.Value is "perf.culture-trait-0" or "perf.culture-trait-1" or "perf.culture-trait-2" &&
        record.Payload.AffiliationPpm == 1_000_000 && record.Payload.AdoptionStep == 0 &&
        record.Payload.SourceRefs.Count == 0 && record.Payload.Status.Value == "active"),
    "Culture approved benchmark payload drifted.");

var reputation = materialization.ReputationRecordsByOrdinal;
Require(reputation.Count == 30_000 &&
        reputation.Select(static record => (record.Payload.SubjectRef, record.Payload.DimensionToken)).Distinct().Count() == 30_000,
    "Reputation subject/dimension keys must be unique.");
var reputationSubjects = reputation.GroupBy(static record => record.Payload.SubjectRef).ToArray();
var reputationDimensions = reputation.GroupBy(static record => record.Payload.DimensionToken.Value).ToArray();
Require(reputationSubjects.Length == 10_000 && reputationSubjects.All(static group => group.Count() == 3) &&
        reputationDimensions.Length == 3 && reputationDimensions.All(static group => group.Count() == 10_000),
    "Reputation must provide exactly three approved dimensions per Organization and 10,000 uses per dimension.");
Require(reputation.All(static record =>
        record.Payload.SubjectRef.PartitionId.Value == SocietyOrganizationPayloadV1.PartitionId &&
        record.Payload.AudienceScopeRef is null &&
        record.Payload.DimensionToken.Value is "perf.reputation-dimension-0" or "perf.reputation-dimension-1" or "perf.reputation-dimension-2" &&
        record.Payload.Score == 0 && record.Payload.ConfidencePpm == 0 &&
        record.Payload.EvidenceRefs.Count == 0 && record.Payload.UpdatedStep == 0),
    "Reputation approved benchmark payload drifted.");

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
    "Education must fail closed without upstream authority.");
ExpectInvalid(() => Qa04SocietySocialRelationsCanonicalAuthorityV1.ValidateEducationRecord(
        0, CopyWithPayload(firstEducation, firstEducation.Payload with { ProviderRef = education[1].Payload.ProviderRef }), materialization.References),
    "Education wrong provider mapping must fail closed.");
ExpectInvalid(() => Qa04SocietySocialRelationsCanonicalAuthorityV1.ValidateEducationRecord(
        0, CopyWithPayload(firstEducation, firstEducation.Payload with { LearnerRef = education[1].Payload.LearnerRef }), materialization.References),
    "Education wrong learner mapping must fail closed.");
ExpectInvalid(() => Qa04SocietySocialRelationsCanonicalAuthorityV1.ValidateEducationRecord(
        0, CopyWithPayload(firstEducation, firstEducation.Payload with { ProgramToken = new StableToken("invalid-program") }), materialization.References),
    "Education program Token drift must fail closed.");
ExpectInvalid(() => Qa04SocietySocialRelationsCanonicalAuthorityV1.ValidateEducationRecord(
        0, CopyWithPayload(firstEducation, firstEducation.Payload with { Status = new StableToken("inactive") }), materialization.References),
    "Education status drift must fail closed.");
ExpectInvalid(() => Qa04SocietySocialRelationsCanonicalAuthorityV1.ValidateEducationRecord(
        0, CopyWithPayload(firstEducation, firstEducation.Payload with { ProgressPpm = 1 }), materialization.References),
    "Education progress drift must fail closed.");
ExpectInvalid(() => Qa04SocietySocialRelationsCanonicalAuthorityV1.ValidateEducationRecord(
        0, CopyWithPayload(firstEducation, firstEducation.Payload with { SkillRefs = new[] { firstEducation.Payload.ProviderRef } }), materialization.References),
    "Education skill authority injection must fail closed.");
ExpectInvalid(() => Qa04SocietySocialRelationsCanonicalAuthorityV1.ValidateEducationRecord(
        0, CopyWithPayload(firstEducation, firstEducation.Payload with { StartedStep = 1 }), materialization.References),
    "Education started_step drift must fail closed.");
ExpectInvalid(() => Qa04SocietySocialRelationsCanonicalAuthorityV1.ValidateEducationRecord(
        0, CopyWithPayload(firstEducation, firstEducation.Payload with { EndedStep = 1 }), materialization.References),
    "Education ended_step drift must fail closed.");

var duplicateEducationRelation = education.ToArray();
duplicateEducationRelation[1] = CopyWithPayload(duplicateEducationRelation[1], duplicateEducationRelation[1].Payload with
{
    ProviderRef = duplicateEducationRelation[0].Payload.ProviderRef,
    LearnerRef = duplicateEducationRelation[0].Payload.LearnerRef,
});
ExpectInvalid(() => Qa04SocietySocialRelationsCanonicalAuthorityV1.ValidateEducationUniqueness(duplicateEducationRelation),
    "Duplicate Education relation must fail closed at full population.");
var duplicateEducationId = education.ToArray();
duplicateEducationId[1] = duplicateEducationId[0];
ExpectInvalid(() => Qa04SocietySocialRelationsCanonicalAuthorityV1.ValidateEducationUniqueness(duplicateEducationId),
    "Duplicate Education RecordId must fail closed at full population.");

var firstCulture = culture[0];
ExpectInvalid(() => new StandardDomainPayloadCodecValidatorV1().Validate(
        SocietyCulturePayloadV1.PartitionId,
        firstCulture.Payload.ToStandardPayload(),
        new EmptyReferenceResolver()),
    "Culture must fail closed without Organization authority.");
ExpectInvalid(() => Qa04SocietySocialRelationsCanonicalAuthorityV1.ValidateCultureRecord(
        0, CopyWithPayload(firstCulture, firstCulture.Payload with { SubjectRef = culture[1].Payload.SubjectRef }), materialization.References),
    "Culture wrong subject mapping must fail closed.");
ExpectInvalid(() => Qa04SocietySocialRelationsCanonicalAuthorityV1.ValidateCultureRecord(
        0, CopyWithPayload(firstCulture, firstCulture.Payload with { TraitToken = new StableToken("invalid-trait") }), materialization.References),
    "Culture trait Token drift must fail closed.");
ExpectInvalid(() => Qa04SocietySocialRelationsCanonicalAuthorityV1.ValidateCultureRecord(
        0, CopyWithPayload(firstCulture, firstCulture.Payload with { AffiliationPpm = 999_999 }), materialization.References),
    "Culture affiliation drift must fail closed.");
ExpectInvalid(() => Qa04SocietySocialRelationsCanonicalAuthorityV1.ValidateCultureRecord(
        0, CopyWithPayload(firstCulture, firstCulture.Payload with { AdoptionStep = 1 }), materialization.References),
    "Culture adoption_step drift must fail closed.");
ExpectInvalid(() => Qa04SocietySocialRelationsCanonicalAuthorityV1.ValidateCultureRecord(
        0, CopyWithPayload(firstCulture, firstCulture.Payload with { SourceRefs = new[] { firstCulture.Payload.SubjectRef } }), materialization.References),
    "Culture source injection must fail closed.");
ExpectInvalid(() => Qa04SocietySocialRelationsCanonicalAuthorityV1.ValidateCultureRecord(
        0, CopyWithPayload(firstCulture, firstCulture.Payload with { Status = new StableToken("inactive") }), materialization.References),
    "Culture status drift must fail closed.");

var duplicateCultureKey = culture.ToArray();
duplicateCultureKey[1] = CopyWithPayload(duplicateCultureKey[1], duplicateCultureKey[1].Payload with
{
    SubjectRef = duplicateCultureKey[0].Payload.SubjectRef,
    TraitToken = duplicateCultureKey[0].Payload.TraitToken,
});
ExpectInvalid(() => Qa04SocietySocialRelationsCanonicalAuthorityV1.ValidateCultureUniqueness(duplicateCultureKey),
    "Duplicate Culture key must fail closed at full population.");
var duplicateCultureId = culture.ToArray();
duplicateCultureId[1] = duplicateCultureId[0];
ExpectInvalid(() => Qa04SocietySocialRelationsCanonicalAuthorityV1.ValidateCultureUniqueness(duplicateCultureId),
    "Duplicate Culture RecordId must fail closed at full population.");

var firstReputation = reputation[0];
ExpectInvalid(() => new StandardDomainPayloadCodecValidatorV1().Validate(
        SocietyReputationPayloadV1.PartitionId,
        firstReputation.Payload.ToStandardPayload(),
        new EmptyReferenceResolver()),
    "Reputation must fail closed without Organization authority.");
ExpectInvalid(() => Qa04SocietySocialRelationsCanonicalAuthorityV1.ValidateReputationRecord(
        0, CopyWithPayload(firstReputation, firstReputation.Payload with { SubjectRef = reputation[1].Payload.SubjectRef }), materialization.References),
    "Reputation wrong subject mapping must fail closed.");
ExpectInvalid(() => Qa04SocietySocialRelationsCanonicalAuthorityV1.ValidateReputationRecord(
        0, CopyWithPayload(firstReputation, firstReputation.Payload with { AudienceScopeRef = firstReputation.Payload.SubjectRef }), materialization.References),
    "Reputation audience authority injection must fail closed.");
ExpectInvalid(() => Qa04SocietySocialRelationsCanonicalAuthorityV1.ValidateReputationRecord(
        0, CopyWithPayload(firstReputation, firstReputation.Payload with { DimensionToken = new StableToken("invalid-dimension") }), materialization.References),
    "Reputation dimension Token drift must fail closed.");
ExpectInvalid(() => Qa04SocietySocialRelationsCanonicalAuthorityV1.ValidateReputationRecord(
        0, CopyWithPayload(firstReputation, firstReputation.Payload with { Score = 1 }), materialization.References),
    "Reputation score drift must fail closed.");
ExpectInvalid(() => Qa04SocietySocialRelationsCanonicalAuthorityV1.ValidateReputationRecord(
        0, CopyWithPayload(firstReputation, firstReputation.Payload with { ConfidencePpm = 1 }), materialization.References),
    "Reputation confidence drift must fail closed.");
ExpectInvalid(() => Qa04SocietySocialRelationsCanonicalAuthorityV1.ValidateReputationRecord(
        0, CopyWithPayload(firstReputation, firstReputation.Payload with { EvidenceRefs = new[] { firstReputation.Payload.SubjectRef } }), materialization.References),
    "Reputation evidence injection must fail closed.");
ExpectInvalid(() => Qa04SocietySocialRelationsCanonicalAuthorityV1.ValidateReputationRecord(
        0, CopyWithPayload(firstReputation, firstReputation.Payload with { UpdatedStep = 1 }), materialization.References),
    "Reputation updated_step drift must fail closed.");

var duplicateReputationKey = reputation.ToArray();
duplicateReputationKey[1] = CopyWithPayload(duplicateReputationKey[1], duplicateReputationKey[1].Payload with
{
    SubjectRef = duplicateReputationKey[0].Payload.SubjectRef,
    DimensionToken = duplicateReputationKey[0].Payload.DimensionToken,
});
ExpectInvalid(() => Qa04SocietySocialRelationsCanonicalAuthorityV1.ValidateReputationUniqueness(duplicateReputationKey),
    "Duplicate Reputation key must fail closed at full population.");
var duplicateReputationId = reputation.ToArray();
duplicateReputationId[1] = duplicateReputationId[0];
ExpectInvalid(() => Qa04SocietySocialRelationsCanonicalAuthorityV1.ValidateReputationUniqueness(duplicateReputationId),
    "Duplicate Reputation RecordId must fail closed at full population.");

Console.WriteLine($"society-social-relations-full-production-pass records={materialization.MaterializedRecordCount} recovered={recovered} education={materialization.Educations.ItemCount} culture={materialization.Cultures.ItemCount} reputation={materialization.Reputations.ItemCount} organizations={materialization.Organizations.ItemCount} learners={education.Select(static record => record.Payload.LearnerRef).Distinct().Count()}");

sealed class EmptyReferenceResolver : IDomainRecordSchemaResolverV1
{
    public bool Exists(PartitionRecordRefV1 reference) => false;
    public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
    {
        schema = default;
        return false;
    }
}
