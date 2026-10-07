using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
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

Console.WriteLine("Validating approved Governance territorial foundation (40,000 records)...");
Qa04GovernanceTerritorialFoundationCanonicalAuthorityV1.ValidateCanonicalContract();
var materialization = Qa04GovernanceTerritorialFoundationCanonicalAuthorityV1.MaterializeCanonical();

Require(materialization.MaterializedRecordCount == 40_000,
    "Governance territorial foundation must materialize exactly 40,000 records.");
Require(materialization.Jurisdictions.ItemCount == 10_000 &&
        materialization.TerritorialClaims.ItemCount == 10_000 &&
        materialization.EffectiveControls.ItemCount == 20_000,
    "Governance territorial partition counts drifted.");
Require(materialization.Polities.ItemCount == 1_000 &&
        materialization.TileScopes.ItemCount == 4_096 &&
        materialization.PublicAuthorities.ItemCount == 25_000,
    "Governance territorial upstream production authority drifted.");

var jurisdictions = materialization.JurisdictionRecordsByOrdinal;
var jurisdictionScopes = jurisdictions.GroupBy(static record => record.Payload.ScopeRef).ToArray();
Require(jurisdictions.Select(static record => (record.Payload.PolityRef, record.Payload.ScopeRef)).Distinct().Count() == 10_000,
    "Jurisdiction polity/scope relations must be unique.");
Require(jurisdictions.GroupBy(static record => record.Payload.PolityRef).Count() == 1_000 &&
        jurisdictions.GroupBy(static record => record.Payload.PolityRef).All(static group => group.Count() == 10),
    "Jurisdiction must have exactly ten records per Polity.");
Require(jurisdictionScopes.Length == 4_096 &&
        jurisdictionScopes.Count(static group => group.Count() == 3) == 1_808 &&
        jurisdictionScopes.Count(static group => group.Count() == 2) == 2_288,
    "Jurisdiction TileScope distribution must be 1,808x3 + 2,288x2.");
Require(jurisdictions.All(static record =>
        record.Payload.JurisdictionKind.Value == "perf.regional-jurisdiction" &&
        record.Payload.SubjectClasses.Count == 1 && record.Payload.SubjectClasses[0].Value == "perf.subject" &&
        record.Payload.EffectiveFrom == 0 && record.Payload.EffectiveUntil is null),
    "Jurisdiction canonical payload semantics drifted.");

var claims = materialization.TerritorialClaimRecordsByOrdinal;
var claimScopes = claims.GroupBy(static record => record.Payload.ScopeRef).ToArray();
Require(claims.Select(static record => (record.Payload.ClaimantPolityRef, record.Payload.ScopeRef)).Distinct().Count() == 10_000,
    "TerritorialClaim polity/scope relations must be unique.");
Require(claims.GroupBy(static record => record.Payload.ClaimantPolityRef).Count() == 1_000 &&
        claims.GroupBy(static record => record.Payload.ClaimantPolityRef).All(static group => group.Count() == 10),
    "TerritorialClaim must have exactly ten records per Polity.");
Require(claimScopes.Length == 4_096 &&
        claimScopes.Count(static group => group.Count() == 3) == 1_808 &&
        claimScopes.Count(static group => group.Count() == 2) == 2_288,
    "TerritorialClaim TileScope distribution must be 1,808x3 + 2,288x2.");
Require(claims.All(static record =>
        record.Payload.ClaimKind.Value == "perf.territorial-claim" &&
        record.Payload.StrengthPpm == 1_000_000 &&
        record.Payload.EffectiveFrom == 0 && record.Payload.EffectiveUntil is null &&
        record.Payload.BasisRefs.Count == 0),
    "TerritorialClaim canonical payload semantics drifted.");

var controls = materialization.EffectiveControlRecordsByOrdinal;
var controlScopes = controls.GroupBy(static record => record.Payload.ScopeRef).ToArray();
Require(controls.Select(static record => record.Payload.ControllerRef).Distinct().Count() == 20_000,
    "EffectiveControl must use 20,000 unique PublicAuthority controllers.");
Require(controls.Select(static record => (record.Payload.ControllerRef, record.Payload.ScopeRef)).Distinct().Count() == 20_000,
    "EffectiveControl controller/scope relations must be unique.");
Require(controlScopes.Length == 4_096 &&
        controlScopes.Count(static group => group.Count() == 5) == 3_616 &&
        controlScopes.Count(static group => group.Count() == 4) == 480,
    "EffectiveControl scope distribution must be 3,616x5 + 480x4.");
Require(controlScopes.All(static group =>
        group.Sum(static record => (long)record.Payload.ControlPpm) == 1_000_000 &&
        group.Sum(static record => (long)record.Payload.SecurityCapacityPpm) == 1_000_000),
    "EffectiveControl aggregates must equal exactly 1,000,000 ppm per TileScope.");
Require(controls.All(static record => record.Payload.EffectiveFrom == 0 && record.Payload.BasisRefs.Count == 0),
    "EffectiveControl Step/basis semantics drifted.");

Qa04GovernanceTerritorialFoundationCanonicalAuthorityV1.ValidateSecondaryIndexes(
    materialization.Jurisdictions,
    materialization.TerritorialClaims,
    materialization.EffectiveControls);

var recovered = Qa04GovernanceTerritorialFoundationSnapshotRecoveryEvidenceV1.Verify(materialization);
Require(recovered == 40_000,
    "Governance territorial Snapshot/recovery must semantically rehash all 40,000 records.");

var firstJurisdiction = jurisdictions[0];
var wrongJurisdictionScope = firstJurisdiction.Payload with
{
    ScopeRef = Qa04SpatialTileScopeAuthorityV1.ScopeRef(1),
};
ExpectInvalid(() => Qa04GovernanceTerritorialFoundationCanonicalAuthorityV1.ValidateJurisdictionRecord(
        0,
        CopyWithPayload(firstJurisdiction, wrongJurisdictionScope),
        materialization.References),
    "Wrong Jurisdiction scope mapping must fail closed.");

var badJurisdictionToken = firstJurisdiction.Payload with
{
    JurisdictionKind = new StableToken("invalid-jurisdiction"),
};
ExpectInvalid(() => Qa04GovernanceTerritorialFoundationCanonicalAuthorityV1.ValidateJurisdictionRecord(
        0,
        CopyWithPayload(firstJurisdiction, badJurisdictionToken),
        materialization.References),
    "Wrong Jurisdiction Token must fail closed.");

var badSubjectClasses = firstJurisdiction.Payload with
{
    SubjectClasses = new[] { new StableToken("invalid-subject") },
};
ExpectInvalid(() => Qa04GovernanceTerritorialFoundationCanonicalAuthorityV1.ValidateJurisdictionRecord(
        0,
        CopyWithPayload(firstJurisdiction, badSubjectClasses),
        materialization.References),
    "Wrong Jurisdiction subject class must fail closed.");

var firstClaim = claims[0];
var badClaimStrength = firstClaim.Payload with { StrengthPpm = 999_999 };
ExpectInvalid(() => Qa04GovernanceTerritorialFoundationCanonicalAuthorityV1.ValidateTerritorialClaimRecord(
        0,
        CopyWithPayload(firstClaim, badClaimStrength),
        materialization.References),
    "TerritorialClaim strength drift must fail closed.");
var injectedClaimBasis = firstClaim.Payload with
{
    BasisRefs = new[] { firstClaim.Payload.ClaimantPolityRef },
};
ExpectInvalid(() => Qa04GovernanceTerritorialFoundationCanonicalAuthorityV1.ValidateTerritorialClaimRecord(
        0,
        CopyWithPayload(firstClaim, injectedClaimBasis),
        materialization.References),
    "TerritorialClaim basis injection must fail closed.");

var duplicateClaims = claims.ToArray();
duplicateClaims[1] = CopyWithPayload(duplicateClaims[1], duplicateClaims[1].Payload with
{
    ClaimantPolityRef = duplicateClaims[0].Payload.ClaimantPolityRef,
    ScopeRef = duplicateClaims[0].Payload.ScopeRef,
});
ExpectInvalid(() => Qa04GovernanceTerritorialFoundationCanonicalAuthorityV1.ValidateTerritorialClaimInvariants(duplicateClaims),
    "Duplicate TerritorialClaim relation must fail closed at full population.");

var firstControl = controls[0];
var badControlShare = firstControl.Payload with { ControlPpm = 199_999 };
ExpectInvalid(() => Qa04GovernanceTerritorialFoundationCanonicalAuthorityV1.ValidateEffectiveControlRecord(
        0,
        CopyWithPayload(firstControl, badControlShare),
        materialization.References),
    "EffectiveControl ppm drift must fail closed.");
var wrongController = firstControl.Payload with
{
    ControllerRef = Qa04SocietyGovernanceCanonicalAuthorityV1.ResolvePublicAuthorityRef(1),
};
ExpectInvalid(() => Qa04GovernanceTerritorialFoundationCanonicalAuthorityV1.ValidateEffectiveControlRecord(
        0,
        CopyWithPayload(firstControl, wrongController),
        materialization.References),
    "EffectiveControl controller mapping drift must fail closed.");

var duplicateControls = controls.ToArray();
duplicateControls[1] = CopyWithPayload(duplicateControls[1], duplicateControls[1].Payload with
{
    ControllerRef = duplicateControls[0].Payload.ControllerRef,
    ScopeRef = duplicateControls[0].Payload.ScopeRef,
});
ExpectInvalid(() => Qa04GovernanceTerritorialFoundationCanonicalAuthorityV1.ValidateEffectiveControlInvariants(duplicateControls),
    "Duplicate EffectiveControl relation must fail closed at full population.");

Console.WriteLine($"governance-territorial-full-production-pass records={materialization.MaterializedRecordCount} recovered={recovered} jurisdictions={materialization.Jurisdictions.ItemCount} claims={materialization.TerritorialClaims.ItemCount} controls={materialization.EffectiveControls.ItemCount}");
