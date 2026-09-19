using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04GovernanceTerritorialFoundationCanonicalAuthoritySmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04GovernanceTerritorialFoundationCanonicalAuthorityV1.ValidateCanonicalContract();
        var materialization = Qa04GovernanceTerritorialFoundationCanonicalAuthorityV1.MaterializeCanonical();

        Require(materialization.MaterializedRecordCount == 40_000 &&
                materialization.Jurisdictions.ItemCount == 10_000 &&
                materialization.TerritorialClaims.ItemCount == 10_000 &&
                materialization.EffectiveControls.ItemCount == 20_000,
            "Governance territorial foundation must materialize exactly 40,000 approved records.");
        Require(materialization.Polities.ItemCount == 1_000 &&
                materialization.TileScopes.ItemCount == 4_096 &&
                materialization.PublicAuthorities.ItemCount == 25_000,
            "Governance territorial foundation must retain actual upstream production authority.");

        var jurisdictions = materialization.JurisdictionRecordsByOrdinal;
        Require(jurisdictions.GroupBy(static record => record.Payload.PolityRef).Count() == 1_000 &&
                jurisdictions.GroupBy(static record => record.Payload.PolityRef).All(static group => group.Count() == 10),
            "Every Polity must have exactly ten canonical Jurisdictions.");
        Require(jurisdictions.Select(static record => (record.Payload.PolityRef, record.Payload.ScopeRef)).Distinct().Count() == 10_000,
            "Canonical Jurisdiction polity/scope relations must be unique.");
        Require(jurisdictions.All(static record =>
                record.Payload.JurisdictionKind.Value == "perf.regional-jurisdiction" &&
                record.Payload.SubjectClasses.Count == 1 && record.Payload.SubjectClasses[0].Value == "perf.subject" &&
                record.Payload.EffectiveFrom == 0 && record.Payload.EffectiveUntil is null),
            "Canonical Jurisdiction benchmark payload drifted.");

        var jurisdictionScopeCounts = jurisdictions.GroupBy(static record => record.Payload.ScopeRef).Select(static group => group.Count()).ToArray();
        Require(jurisdictionScopeCounts.Length == 4_096 &&
                jurisdictionScopeCounts.Count(static count => count == 3) == 1_808 &&
                jurisdictionScopeCounts.Count(static count => count == 2) == 2_288,
            "Canonical Jurisdiction TileScope distribution must be exact 1,808x3 + 2,288x2.");

        var claims = materialization.TerritorialClaimRecordsByOrdinal;
        Require(claims.Select(static record => (record.Payload.ClaimantPolityRef, record.Payload.ScopeRef)).Distinct().Count() == 10_000,
            "Canonical TerritorialClaim polity/scope relations must be unique.");
        Require(claims.All(static record =>
                record.Payload.ClaimKind.Value == "perf.territorial-claim" &&
                record.Payload.StrengthPpm == 1_000_000 &&
                record.Payload.EffectiveFrom == 0 && record.Payload.EffectiveUntil is null &&
                record.Payload.BasisRefs.Count == 0),
            "Canonical TerritorialClaim benchmark payload drifted.");

        var controls = materialization.EffectiveControlRecordsByOrdinal;
        Require(controls.Select(static record => record.Payload.ControllerRef).Distinct().Count() == 20_000,
            "Canonical EffectiveControl must use 20,000 unique PublicAuthority controllers.");
        Require(controls.Select(static record => (record.Payload.ControllerRef, record.Payload.ScopeRef)).Distinct().Count() == 20_000,
            "Canonical EffectiveControl controller/scope relations must be unique.");
        var controlScopes = controls.GroupBy(static record => record.Payload.ScopeRef).ToArray();
        Require(controlScopes.Length == 4_096 &&
                controlScopes.Count(static group => group.Count() == 5) == 3_616 &&
                controlScopes.Count(static group => group.Count() == 4) == 480,
            "Canonical EffectiveControl scope distribution must be exact 3,616x5 + 480x4.");
        Require(controlScopes.All(static group =>
                group.Sum(static record => (long)record.Payload.ControlPpm) == 1_000_000 &&
                group.Sum(static record => (long)record.Payload.SecurityCapacityPpm) == 1_000_000),
            "Each canonical EffectiveControl scope must aggregate to exactly 1,000,000 ppm.");

        Qa04GovernanceTerritorialFoundationCanonicalAuthorityV1.ValidateSecondaryIndexes(
            materialization.Jurisdictions,
            materialization.TerritorialClaims,
            materialization.EffectiveControls);

        var recovered = Qa04GovernanceTerritorialFoundationSnapshotRecoveryEvidenceV1.Verify(materialization);
        Require(recovered == 40_000,
            "Governance territorial foundation Snapshot/recovery must semantically recover all 40,000 records.");

        var firstJurisdiction = jurisdictions[0];
        ExpectInvalid(() => new StandardDomainPayloadCodecValidatorV1().Validate(
                GovernanceJurisdictionPayloadV1.PartitionId,
                firstJurisdiction.Payload.ToStandardPayload(),
                new EmptyReferenceResolver()),
            "Jurisdiction must fail closed when Polity/TileScope authority is unavailable.");

        var wrongScope = firstJurisdiction.Payload with
        {
            ScopeRef = Qa04SpatialTileScopeAuthorityV1.ScopeRef(1),
        };
        ExpectInvalid(() => Qa04GovernanceTerritorialFoundationCanonicalAuthorityV1.ValidateJurisdictionRecord(
                0, CopyWithPayload(firstJurisdiction, wrongScope), materialization.References),
            "Jurisdiction scope mapping drift must fail closed.");

        var wrongJurisdictionToken = firstJurisdiction.Payload with
        {
            JurisdictionKind = new StableToken("invalid-jurisdiction"),
        };
        ExpectInvalid(() => Qa04GovernanceTerritorialFoundationCanonicalAuthorityV1.ValidateJurisdictionRecord(
                0, CopyWithPayload(firstJurisdiction, wrongJurisdictionToken), materialization.References),
            "Jurisdiction Token drift must fail closed.");

        var wrongSubject = firstJurisdiction.Payload with
        {
            SubjectClasses = new[] { new StableToken("invalid-subject") },
        };
        ExpectInvalid(() => Qa04GovernanceTerritorialFoundationCanonicalAuthorityV1.ValidateJurisdictionRecord(
                0, CopyWithPayload(firstJurisdiction, wrongSubject), materialization.References),
            "Jurisdiction subject class drift must fail closed.");

        var expiredJurisdiction = firstJurisdiction.Payload with { EffectiveUntil = 1UL };
        ExpectInvalid(() => Qa04GovernanceTerritorialFoundationCanonicalAuthorityV1.ValidateJurisdictionRecord(
                0, CopyWithPayload(firstJurisdiction, expiredJurisdiction), materialization.References),
            "Jurisdiction genesis effective period drift must fail closed.");

        var firstClaim = claims[0];
        var weakClaim = firstClaim.Payload with { StrengthPpm = 999_999 };
        ExpectInvalid(() => Qa04GovernanceTerritorialFoundationCanonicalAuthorityV1.ValidateTerritorialClaimRecord(
                0, CopyWithPayload(firstClaim, weakClaim), materialization.References),
            "TerritorialClaim strength drift must fail closed.");

        var basedClaim = firstClaim.Payload with { BasisRefs = new[] { firstClaim.Payload.ClaimantPolityRef } };
        ExpectInvalid(() => Qa04GovernanceTerritorialFoundationCanonicalAuthorityV1.ValidateTerritorialClaimRecord(
                0, CopyWithPayload(firstClaim, basedClaim), materialization.References),
            "Unexpected TerritorialClaim genesis basis must fail closed.");

        var duplicateClaim = CopyWithPayload(claims[1], claims[1].Payload with
        {
            ClaimantPolityRef = firstClaim.Payload.ClaimantPolityRef,
            ScopeRef = firstClaim.Payload.ScopeRef,
        });
        ExpectInvalid(() => Qa04GovernanceTerritorialFoundationCanonicalAuthorityV1.ValidateTerritorialClaimInvariants(
                new[] { firstClaim, duplicateClaim }),
            "Duplicate TerritorialClaim relation must fail closed.");

        var firstControl = controls[0];
        var badControlShare = firstControl.Payload with { ControlPpm = 199_999 };
        ExpectInvalid(() => Qa04GovernanceTerritorialFoundationCanonicalAuthorityV1.ValidateEffectiveControlRecord(
                0, CopyWithPayload(firstControl, badControlShare), materialization.References),
            "EffectiveControl ppm drift must fail closed.");

        var badSecurityShare = firstControl.Payload with { SecurityCapacityPpm = 199_999 };
        ExpectInvalid(() => Qa04GovernanceTerritorialFoundationCanonicalAuthorityV1.ValidateEffectiveControlRecord(
                0, CopyWithPayload(firstControl, badSecurityShare), materialization.References),
            "EffectiveControl security capacity drift must fail closed.");

        var wrongController = firstControl.Payload with
        {
            ControllerRef = Qa04SocietyGovernanceCanonicalAuthorityV1.ResolvePublicAuthorityRef(1),
        };
        ExpectInvalid(() => Qa04GovernanceTerritorialFoundationCanonicalAuthorityV1.ValidateEffectiveControlRecord(
                0, CopyWithPayload(firstControl, wrongController), materialization.References),
            "EffectiveControl controller mapping drift must fail closed.");

        var duplicateControl = CopyWithPayload(controls[1], controls[1].Payload with
        {
            ControllerRef = firstControl.Payload.ControllerRef,
            ScopeRef = firstControl.Payload.ScopeRef,
        });
        ExpectInvalid(() => Qa04GovernanceTerritorialFoundationCanonicalAuthorityV1.ValidateEffectiveControlInvariants(
                new[] { firstControl, duplicateControl }),
            "Duplicate EffectiveControl relation must fail closed.");
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
