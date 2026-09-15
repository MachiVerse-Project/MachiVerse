using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains.GovernanceSecurity;

public sealed record GovernancePolityPayloadV1(
    IReadOnlyList<PartitionRecordRefV1> RelatedOrgRefs,
    StableToken Lifecycle,
    IReadOnlyList<PartitionRecordRefV1> InstitutionRefs,
    IReadOnlyList<PartitionRecordRefV1> JurisdictionRefs,
    IReadOnlyList<PartitionRecordRefV1> ClaimRefs,
    IReadOnlyList<PartitionRecordRefV1> ControlRefs,
    IReadOnlyList<PartitionRecordRefV1> RecognitionRefs,
    IReadOnlyList<PartitionRecordRefV1> FiscalRefs)
{
    public const string PartitionId = "governance.polity";
    public IReadOnlyDictionary<string, object?> ToStandardPayload() => GovernancePayloadFields.Map(
        ("related_org_refs", RelatedOrgRefs), ("lifecycle", Lifecycle.Value),
        ("institution_refs", InstitutionRefs), ("jurisdiction_refs", JurisdictionRefs),
        ("claim_refs", ClaimRefs), ("control_refs", ControlRefs),
        ("recognition_refs", RecognitionRefs), ("fiscal_refs", FiscalRefs));
    public byte[] CanonicalDigest() => GovernancePayloadFields.Digest(PartitionId, ToStandardPayload());
    public static GovernancePolityPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> v) => new(
        GovernancePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(v, PartitionId, "related_org_refs"),
        new StableToken(GovernancePayloadFields.Required<string>(v, PartitionId, "lifecycle")),
        GovernancePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(v, PartitionId, "institution_refs"),
        GovernancePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(v, PartitionId, "jurisdiction_refs"),
        GovernancePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(v, PartitionId, "claim_refs"),
        GovernancePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(v, PartitionId, "control_refs"),
        GovernancePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(v, PartitionId, "recognition_refs"),
        GovernancePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(v, PartitionId, "fiscal_refs"));
}

public sealed record GovernanceInstitutionPayloadV1(
    PartitionRecordRefV1 PolityRef, StableToken InstitutionKind,
    IReadOnlyList<PartitionRecordRefV1> OfficeRefs, StableToken DecisionMethod,
    PartitionRecordRefV1? SelectionRuleRef, StableToken Lifecycle)
{
    public const string PartitionId = "governance.institution";
    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var v = GovernancePayloadFields.Map(("polity_ref", PolityRef), ("institution_kind", InstitutionKind.Value),
            ("office_refs", OfficeRefs), ("decision_method", DecisionMethod.Value), ("lifecycle", Lifecycle.Value));
        GovernancePayloadFields.AddOptional(v, "selection_rule_ref", SelectionRuleRef); return v;
    }
    public byte[] CanonicalDigest() => GovernancePayloadFields.Digest(PartitionId, ToStandardPayload());
    public static GovernanceInstitutionPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> v) => new(
        GovernancePayloadFields.Required<PartitionRecordRefV1>(v, PartitionId, "polity_ref"),
        new StableToken(GovernancePayloadFields.Required<string>(v, PartitionId, "institution_kind")),
        GovernancePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(v, PartitionId, "office_refs"),
        new StableToken(GovernancePayloadFields.Required<string>(v, PartitionId, "decision_method")),
        GovernancePayloadFields.Optional<PartitionRecordRefV1>(v, "selection_rule_ref"),
        new StableToken(GovernancePayloadFields.Required<string>(v, PartitionId, "lifecycle")));
}

/// <summary>Carrier for the normative RuleAst fields. Nonempty encode/digest remains fail-closed until explicit RuleAst codecs are registered.</summary>
public sealed record GovernanceLawRulePayloadV1(
    PartitionRecordRefV1 JurisdictionRef, int Priority, uint Specificity,
    ulong EffectiveFrom, ulong? EffectiveUntil,
    ICanonicalDomainNestedValueV1 PredicateAst, ICanonicalDomainNestedValueV1 EffectAst,
    StableToken Status)
{
    public const string PartitionId = "governance.law_rule";
    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var v = GovernancePayloadFields.Map(("jurisdiction_ref", JurisdictionRef), ("priority", Priority),
            ("specificity", Specificity), ("effective_from", EffectiveFrom),
            ("predicate_ast", PredicateAst), ("effect_ast", EffectAst), ("status", Status.Value));
        GovernancePayloadFields.AddOptional(v, "effective_until", EffectiveUntil); return v;
    }
    public byte[] CanonicalDigest() => GovernancePayloadFields.Digest(PartitionId, ToStandardPayload());
    public static GovernanceLawRulePayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> v) => new(
        GovernancePayloadFields.Required<PartitionRecordRefV1>(v, PartitionId, "jurisdiction_ref"),
        GovernancePayloadFields.Required<int>(v, PartitionId, "priority"),
        GovernancePayloadFields.Required<uint>(v, PartitionId, "specificity"),
        GovernancePayloadFields.Required<ulong>(v, PartitionId, "effective_from"),
        GovernancePayloadFields.Optional<ulong>(v, "effective_until"),
        GovernancePayloadFields.Required<ICanonicalDomainNestedValueV1>(v, PartitionId, "predicate_ast"),
        GovernancePayloadFields.Required<ICanonicalDomainNestedValueV1>(v, PartitionId, "effect_ast"),
        new StableToken(GovernancePayloadFields.Required<string>(v, PartitionId, "status")));
}

public sealed record GovernanceJurisdictionPayloadV1(
    PartitionRecordRefV1 PolityRef, PartitionRecordRefV1 ScopeRef, StableToken JurisdictionKind,
    IReadOnlyList<StableToken> SubjectClasses, ulong EffectiveFrom, ulong? EffectiveUntil)
{
    public const string PartitionId = "governance.jurisdiction";
    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var v = GovernancePayloadFields.Map(("polity_ref", PolityRef), ("scope_ref", ScopeRef),
            ("jurisdiction_kind", JurisdictionKind.Value), ("subject_classes", GovernancePayloadFields.TokenValues(SubjectClasses)),
            ("effective_from", EffectiveFrom)); GovernancePayloadFields.AddOptional(v, "effective_until", EffectiveUntil); return v;
    }
    public byte[] CanonicalDigest() => GovernancePayloadFields.Digest(PartitionId, ToStandardPayload());
    public static GovernanceJurisdictionPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> v) => new(
        GovernancePayloadFields.Required<PartitionRecordRefV1>(v, PartitionId, "polity_ref"),
        GovernancePayloadFields.Required<PartitionRecordRefV1>(v, PartitionId, "scope_ref"),
        new StableToken(GovernancePayloadFields.Required<string>(v, PartitionId, "jurisdiction_kind")),
        GovernancePayloadFields.Tokens(v, PartitionId, "subject_classes"),
        GovernancePayloadFields.Required<ulong>(v, PartitionId, "effective_from"),
        GovernancePayloadFields.Optional<ulong>(v, "effective_until"));
}

public sealed record GovernanceTerritorialClaimPayloadV1(
    PartitionRecordRefV1 ClaimantPolityRef, PartitionRecordRefV1 ScopeRef, StableToken ClaimKind,
    uint StrengthPpm, ulong EffectiveFrom, ulong? EffectiveUntil, IReadOnlyList<PartitionRecordRefV1> BasisRefs)
{
    public const string PartitionId = "governance.territorial_claim";
    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var v = GovernancePayloadFields.Map(("claimant_polity_ref", ClaimantPolityRef), ("scope_ref", ScopeRef),
            ("claim_kind", ClaimKind.Value), ("strength_ppm", StrengthPpm), ("effective_from", EffectiveFrom), ("basis_refs", BasisRefs));
        GovernancePayloadFields.AddOptional(v, "effective_until", EffectiveUntil); return v;
    }
    public byte[] CanonicalDigest() => GovernancePayloadFields.Digest(PartitionId, ToStandardPayload());
    public static GovernanceTerritorialClaimPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> v) => new(
        GovernancePayloadFields.Required<PartitionRecordRefV1>(v, PartitionId, "claimant_polity_ref"), GovernancePayloadFields.Required<PartitionRecordRefV1>(v, PartitionId, "scope_ref"),
        new StableToken(GovernancePayloadFields.Required<string>(v, PartitionId, "claim_kind")), GovernancePayloadFields.Required<uint>(v, PartitionId, "strength_ppm"),
        GovernancePayloadFields.Required<ulong>(v, PartitionId, "effective_from"), GovernancePayloadFields.Optional<ulong>(v, "effective_until"),
        GovernancePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(v, PartitionId, "basis_refs"));
}

public sealed record GovernanceEffectiveControlPayloadV1(
    PartitionRecordRefV1 ControllerRef, PartitionRecordRefV1 ScopeRef, uint ControlPpm,
    uint SecurityCapacityPpm, ulong EffectiveFrom, IReadOnlyList<PartitionRecordRefV1> BasisRefs)
{
    public const string PartitionId = "governance.effective_control";
    public IReadOnlyDictionary<string, object?> ToStandardPayload() => GovernancePayloadFields.Map(
        ("controller_ref", ControllerRef), ("scope_ref", ScopeRef), ("control_ppm", ControlPpm),
        ("security_capacity_ppm", SecurityCapacityPpm), ("effective_from", EffectiveFrom), ("basis_refs", BasisRefs));
    public byte[] CanonicalDigest() => GovernancePayloadFields.Digest(PartitionId, ToStandardPayload());
    public static GovernanceEffectiveControlPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> v) => new(
        GovernancePayloadFields.Required<PartitionRecordRefV1>(v, PartitionId, "controller_ref"), GovernancePayloadFields.Required<PartitionRecordRefV1>(v, PartitionId, "scope_ref"),
        GovernancePayloadFields.Required<uint>(v, PartitionId, "control_ppm"), GovernancePayloadFields.Required<uint>(v, PartitionId, "security_capacity_ppm"),
        GovernancePayloadFields.Required<ulong>(v, PartitionId, "effective_from"), GovernancePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(v, PartitionId, "basis_refs"));
}

public sealed record GovernancePublicAuthorityPayloadV1(
    PartitionRecordRefV1 InstitutionRef, PartitionRecordRefV1 HolderRef,
    IReadOnlyList<StableToken> AuthorityTokens, IReadOnlyList<PartitionRecordRefV1> ScopeRefs,
    ulong EffectiveFrom, ulong? EffectiveUntil, StableToken Status)
{
    public const string PartitionId = "governance.public_authority";
    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var v = GovernancePayloadFields.Map(("institution_ref", InstitutionRef), ("holder_ref", HolderRef),
            ("authority_tokens", GovernancePayloadFields.TokenValues(AuthorityTokens)), ("scope_refs", ScopeRefs),
            ("effective_from", EffectiveFrom), ("status", Status.Value)); GovernancePayloadFields.AddOptional(v, "effective_until", EffectiveUntil); return v;
    }
    public byte[] CanonicalDigest() => GovernancePayloadFields.Digest(PartitionId, ToStandardPayload());
    public static GovernancePublicAuthorityPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> v) => new(
        GovernancePayloadFields.Required<PartitionRecordRefV1>(v, PartitionId, "institution_ref"), GovernancePayloadFields.Required<PartitionRecordRefV1>(v, PartitionId, "holder_ref"),
        GovernancePayloadFields.Tokens(v, PartitionId, "authority_tokens"), GovernancePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(v, PartitionId, "scope_refs"),
        GovernancePayloadFields.Required<ulong>(v, PartitionId, "effective_from"), GovernancePayloadFields.Optional<ulong>(v, "effective_until"),
        new StableToken(GovernancePayloadFields.Required<string>(v, PartitionId, "status")));
}

public sealed record GovernanceTaxFiscalPayloadV1(
    PartitionRecordRefV1 PolityRef, StableToken TaxKind, StableToken TaxBaseToken, uint RatePpm,
    long? ClaimAmount, PartitionRecordRefV1? DebtorRef, ulong? DueStep, StableToken Status)
{
    public const string PartitionId = "governance.tax_fiscal";
    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var v = GovernancePayloadFields.Map(("polity_ref", PolityRef), ("tax_kind", TaxKind.Value), ("tax_base_token", TaxBaseToken.Value),
            ("rate_ppm", RatePpm), ("status", Status.Value)); GovernancePayloadFields.AddOptional(v, "claim_amount", ClaimAmount);
        GovernancePayloadFields.AddOptional(v, "debtor_ref", DebtorRef); GovernancePayloadFields.AddOptional(v, "due_step", DueStep); return v;
    }
    public byte[] CanonicalDigest() => GovernancePayloadFields.Digest(PartitionId, ToStandardPayload());
    public static GovernanceTaxFiscalPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> v) => new(
        GovernancePayloadFields.Required<PartitionRecordRefV1>(v, PartitionId, "polity_ref"), new StableToken(GovernancePayloadFields.Required<string>(v, PartitionId, "tax_kind")),
        new StableToken(GovernancePayloadFields.Required<string>(v, PartitionId, "tax_base_token")), GovernancePayloadFields.Required<uint>(v, PartitionId, "rate_ppm"),
        GovernancePayloadFields.Optional<long>(v, "claim_amount"), GovernancePayloadFields.Optional<PartitionRecordRefV1>(v, "debtor_ref"),
        GovernancePayloadFields.Optional<ulong>(v, "due_step"), new StableToken(GovernancePayloadFields.Required<string>(v, PartitionId, "status")));
}

public sealed record GovernancePermissionLicensePayloadV1(
    PartitionRecordRefV1 SubjectRef, PartitionRecordRefV1 AuthorityRef, StableToken PermissionKind,
    IReadOnlyList<PartitionRecordRefV1> ScopeRefs, ulong EffectiveFrom, ulong? EffectiveUntil,
    StableToken Status, byte[] ConditionsDigest)
{
    public const string PartitionId = "governance.permission_license";
    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var v = GovernancePayloadFields.Map(("subject_ref", SubjectRef), ("authority_ref", AuthorityRef), ("permission_kind", PermissionKind.Value),
            ("scope_refs", ScopeRefs), ("effective_from", EffectiveFrom), ("status", Status.Value), ("conditions_digest", ConditionsDigest));
        GovernancePayloadFields.AddOptional(v, "effective_until", EffectiveUntil); return v;
    }
    public byte[] CanonicalDigest() => GovernancePayloadFields.Digest(PartitionId, ToStandardPayload());
    public static GovernancePermissionLicensePayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> v) => new(
        GovernancePayloadFields.Required<PartitionRecordRefV1>(v, PartitionId, "subject_ref"), GovernancePayloadFields.Required<PartitionRecordRefV1>(v, PartitionId, "authority_ref"),
        new StableToken(GovernancePayloadFields.Required<string>(v, PartitionId, "permission_kind")), GovernancePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(v, PartitionId, "scope_refs"),
        GovernancePayloadFields.Required<ulong>(v, PartitionId, "effective_from"), GovernancePayloadFields.Optional<ulong>(v, "effective_until"),
        new StableToken(GovernancePayloadFields.Required<string>(v, PartitionId, "status")), GovernancePayloadFields.Required<byte[]>(v, PartitionId, "conditions_digest").ToArray());
}

public sealed record GovernanceDiplomacyPayloadV1(
    IReadOnlyList<PartitionRecordRefV1> PartyRefs, StableToken RelationKind, StableToken Status,
    ulong EffectiveFrom, ulong? EffectiveUntil, IReadOnlyList<PartitionRecordRefV1> InstrumentRefs, byte[] TermsDigest)
{
    public const string PartitionId = "governance.diplomacy";
    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var v = GovernancePayloadFields.Map(("party_refs", PartyRefs), ("relation_kind", RelationKind.Value), ("status", Status.Value),
            ("effective_from", EffectiveFrom), ("instrument_refs", InstrumentRefs), ("terms_digest", TermsDigest));
        GovernancePayloadFields.AddOptional(v, "effective_until", EffectiveUntil); return v;
    }
    public byte[] CanonicalDigest() => GovernancePayloadFields.Digest(PartitionId, ToStandardPayload());
    public static GovernanceDiplomacyPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> v) => new(
        GovernancePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(v, PartitionId, "party_refs"), new StableToken(GovernancePayloadFields.Required<string>(v, PartitionId, "relation_kind")),
        new StableToken(GovernancePayloadFields.Required<string>(v, PartitionId, "status")), GovernancePayloadFields.Required<ulong>(v, PartitionId, "effective_from"),
        GovernancePayloadFields.Optional<ulong>(v, "effective_until"), GovernancePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(v, PartitionId, "instrument_refs"),
        GovernancePayloadFields.Required<byte[]>(v, PartitionId, "terms_digest").ToArray());
}

public sealed record GovernanceSecurityIncidentPayloadV1(
    StableToken IncidentKind, IReadOnlyList<PartitionRecordRefV1> SubjectRefs, PartitionRecordRefV1 ScopeRef,
    ulong OccurredStep, IReadOnlyList<PartitionRecordRefV1> FactEventRefs, StableToken Status, uint SeverityPpm)
{
    public const string PartitionId = "governance.security_incident";
    public IReadOnlyDictionary<string, object?> ToStandardPayload() => GovernancePayloadFields.Map(
        ("incident_kind", IncidentKind.Value), ("subject_refs", SubjectRefs), ("scope_ref", ScopeRef), ("occurred_step", OccurredStep),
        ("fact_event_refs", FactEventRefs), ("status", Status.Value), ("severity_ppm", SeverityPpm));
    public byte[] CanonicalDigest() => GovernancePayloadFields.Digest(PartitionId, ToStandardPayload());
    public static GovernanceSecurityIncidentPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> v) => new(
        new StableToken(GovernancePayloadFields.Required<string>(v, PartitionId, "incident_kind")), GovernancePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(v, PartitionId, "subject_refs"),
        GovernancePayloadFields.Required<PartitionRecordRefV1>(v, PartitionId, "scope_ref"), GovernancePayloadFields.Required<ulong>(v, PartitionId, "occurred_step"),
        GovernancePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(v, PartitionId, "fact_event_refs"), new StableToken(GovernancePayloadFields.Required<string>(v, PartitionId, "status")),
        GovernancePayloadFields.Required<uint>(v, PartitionId, "severity_ppm"));
}

public sealed record GovernanceInvestigationPayloadV1(
    PartitionRecordRefV1 IncidentRef, PartitionRecordRefV1 AuthorityRef, IReadOnlyList<PartitionRecordRefV1> InvestigatorRefs,
    IReadOnlyList<PartitionRecordRefV1> EvidenceRefs, IReadOnlyList<PartitionRecordRefV1> SuspectRefs,
    StableToken Status, ulong OpenedStep, ulong? ClosedStep)
{
    public const string PartitionId = "governance.investigation";
    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var v = GovernancePayloadFields.Map(("incident_ref", IncidentRef), ("authority_ref", AuthorityRef), ("investigator_refs", InvestigatorRefs),
            ("evidence_refs", EvidenceRefs), ("suspect_refs", SuspectRefs), ("status", Status.Value), ("opened_step", OpenedStep));
        GovernancePayloadFields.AddOptional(v, "closed_step", ClosedStep); return v;
    }
    public byte[] CanonicalDigest() => GovernancePayloadFields.Digest(PartitionId, ToStandardPayload());
    public static GovernanceInvestigationPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> v) => new(
        GovernancePayloadFields.Required<PartitionRecordRefV1>(v, PartitionId, "incident_ref"), GovernancePayloadFields.Required<PartitionRecordRefV1>(v, PartitionId, "authority_ref"),
        GovernancePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(v, PartitionId, "investigator_refs"), GovernancePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(v, PartitionId, "evidence_refs"),
        GovernancePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(v, PartitionId, "suspect_refs"), new StableToken(GovernancePayloadFields.Required<string>(v, PartitionId, "status")),
        GovernancePayloadFields.Required<ulong>(v, PartitionId, "opened_step"), GovernancePayloadFields.Optional<ulong>(v, "closed_step"));
}

public sealed record GovernanceJudicialCasePayloadV1(
    StableToken CaseKind, PartitionRecordRefV1 JurisdictionRef, IReadOnlyList<PartitionRecordRefV1> PartyRefs,
    IReadOnlyList<PartitionRecordRefV1> EvidenceRefs, IReadOnlyList<PartitionRecordRefV1> ChargeOrClaimRefs,
    StableToken Status, ulong OpenedStep, PartitionRecordRefV1? DecisionRef)
{
    public const string PartitionId = "governance.judicial_case";
    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var v = GovernancePayloadFields.Map(("case_kind", CaseKind.Value), ("jurisdiction_ref", JurisdictionRef), ("party_refs", PartyRefs),
            ("evidence_refs", EvidenceRefs), ("charge_or_claim_refs", ChargeOrClaimRefs), ("status", Status.Value), ("opened_step", OpenedStep));
        GovernancePayloadFields.AddOptional(v, "decision_ref", DecisionRef); return v;
    }
    public byte[] CanonicalDigest() => GovernancePayloadFields.Digest(PartitionId, ToStandardPayload());
    public static GovernanceJudicialCasePayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> v) => new(
        new StableToken(GovernancePayloadFields.Required<string>(v, PartitionId, "case_kind")), GovernancePayloadFields.Required<PartitionRecordRefV1>(v, PartitionId, "jurisdiction_ref"),
        GovernancePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(v, PartitionId, "party_refs"), GovernancePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(v, PartitionId, "evidence_refs"),
        GovernancePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(v, PartitionId, "charge_or_claim_refs"), new StableToken(GovernancePayloadFields.Required<string>(v, PartitionId, "status")),
        GovernancePayloadFields.Required<ulong>(v, PartitionId, "opened_step"), GovernancePayloadFields.Optional<PartitionRecordRefV1>(v, "decision_ref"));
}

public sealed record GovernanceEnforcementPayloadV1(
    PartitionRecordRefV1 AuthorityRef, StableToken OrderKind, IReadOnlyList<PartitionRecordRefV1> SubjectRefs,
    IReadOnlyList<PartitionRecordRefV1> TargetRefs, StableToken Status, ulong IssuedStep,
    ulong? EffectiveStep, IReadOnlyList<PartitionRecordRefV1> OutcomeEventRefs)
{
    public const string PartitionId = "governance.enforcement";
    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var v = GovernancePayloadFields.Map(("authority_ref", AuthorityRef), ("order_kind", OrderKind.Value), ("subject_refs", SubjectRefs),
            ("target_refs", TargetRefs), ("status", Status.Value), ("issued_step", IssuedStep), ("outcome_event_refs", OutcomeEventRefs));
        GovernancePayloadFields.AddOptional(v, "effective_step", EffectiveStep); return v;
    }
    public byte[] CanonicalDigest() => GovernancePayloadFields.Digest(PartitionId, ToStandardPayload());
    public static GovernanceEnforcementPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> v) => new(
        GovernancePayloadFields.Required<PartitionRecordRefV1>(v, PartitionId, "authority_ref"), new StableToken(GovernancePayloadFields.Required<string>(v, PartitionId, "order_kind")),
        GovernancePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(v, PartitionId, "subject_refs"), GovernancePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(v, PartitionId, "target_refs"),
        new StableToken(GovernancePayloadFields.Required<string>(v, PartitionId, "status")), GovernancePayloadFields.Required<ulong>(v, PartitionId, "issued_step"),
        GovernancePayloadFields.Optional<ulong>(v, "effective_step"), GovernancePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(v, PartitionId, "outcome_event_refs"));
}

public sealed record GovernanceMilitaryAuthorityPayloadV1(
    PartitionRecordRefV1 PolityRef, PartitionRecordRefV1 UnitOrOrgRef, PartitionRecordRefV1? CommandRef,
    StableToken MissionToken, IReadOnlyList<PartitionRecordRefV1> ObjectiveRefs,
    IReadOnlyList<PartitionRecordRefV1> AuthorityScopeRefs, StableToken Status, ulong IssuedStep)
{
    public const string PartitionId = "governance.military_authority";
    public IReadOnlyDictionary<string, object?> ToStandardPayload()
    {
        var v = GovernancePayloadFields.Map(("polity_ref", PolityRef), ("unit_or_org_ref", UnitOrOrgRef), ("mission_token", MissionToken.Value),
            ("objective_refs", ObjectiveRefs), ("authority_scope_refs", AuthorityScopeRefs), ("status", Status.Value), ("issued_step", IssuedStep));
        GovernancePayloadFields.AddOptional(v, "command_ref", CommandRef); return v;
    }
    public byte[] CanonicalDigest() => GovernancePayloadFields.Digest(PartitionId, ToStandardPayload());
    public static GovernanceMilitaryAuthorityPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> v) => new(
        GovernancePayloadFields.Required<PartitionRecordRefV1>(v, PartitionId, "polity_ref"), GovernancePayloadFields.Required<PartitionRecordRefV1>(v, PartitionId, "unit_or_org_ref"),
        GovernancePayloadFields.Optional<PartitionRecordRefV1>(v, "command_ref"), new StableToken(GovernancePayloadFields.Required<string>(v, PartitionId, "mission_token")),
        GovernancePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(v, PartitionId, "objective_refs"), GovernancePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(v, PartitionId, "authority_scope_refs"),
        new StableToken(GovernancePayloadFields.Required<string>(v, PartitionId, "status")), GovernancePayloadFields.Required<ulong>(v, PartitionId, "issued_step"));
}

public sealed record GovernanceBorderControlPayloadV1(
    PartitionRecordRefV1 JurisdictionRef, PartitionRecordRefV1 BoundaryRef,
    IReadOnlyList<PartitionRecordRefV1> CheckpointRefs, IReadOnlyList<PartitionRecordRefV1> MovementRuleRefs,
    StableToken Status, uint CapacityPerStep)
{
    public const string PartitionId = "governance.border_control";
    public IReadOnlyDictionary<string, object?> ToStandardPayload() => GovernancePayloadFields.Map(
        ("jurisdiction_ref", JurisdictionRef), ("boundary_ref", BoundaryRef), ("checkpoint_refs", CheckpointRefs),
        ("movement_rule_refs", MovementRuleRefs), ("status", Status.Value), ("capacity_per_step", CapacityPerStep));
    public byte[] CanonicalDigest() => GovernancePayloadFields.Digest(PartitionId, ToStandardPayload());
    public static GovernanceBorderControlPayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> v) => new(
        GovernancePayloadFields.Required<PartitionRecordRefV1>(v, PartitionId, "jurisdiction_ref"), GovernancePayloadFields.Required<PartitionRecordRefV1>(v, PartitionId, "boundary_ref"),
        GovernancePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(v, PartitionId, "checkpoint_refs"), GovernancePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(v, PartitionId, "movement_rule_refs"),
        new StableToken(GovernancePayloadFields.Required<string>(v, PartitionId, "status")), GovernancePayloadFields.Required<uint>(v, PartitionId, "capacity_per_step"));
}

public sealed record GovernanceLineagePayloadV1(
    PartitionRecordRefV1 SubjectRef, IReadOnlyList<PartitionRecordRefV1> PredecessorRefs,
    StableToken SuccessionKind, ulong EffectiveStep, byte[] CausalityDigest)
{
    public const string PartitionId = "governance.lineage";
    public IReadOnlyDictionary<string, object?> ToStandardPayload() => GovernancePayloadFields.Map(
        ("subject_ref", SubjectRef), ("predecessor_refs", PredecessorRefs), ("succession_kind", SuccessionKind.Value),
        ("effective_step", EffectiveStep), ("causality_digest", CausalityDigest));
    public byte[] CanonicalDigest() => GovernancePayloadFields.Digest(PartitionId, ToStandardPayload());
    public static GovernanceLineagePayloadV1 FromStandardPayload(IReadOnlyDictionary<string, object?> v) => new(
        GovernancePayloadFields.Required<PartitionRecordRefV1>(v, PartitionId, "subject_ref"), GovernancePayloadFields.Required<IReadOnlyList<PartitionRecordRefV1>>(v, PartitionId, "predecessor_refs"),
        new StableToken(GovernancePayloadFields.Required<string>(v, PartitionId, "succession_kind")), GovernancePayloadFields.Required<ulong>(v, PartitionId, "effective_step"),
        GovernancePayloadFields.Required<byte[]>(v, PartitionId, "causality_digest").ToArray());
}

public static class GovernanceSecurityDomainSnapshotProviderV1
{
    public static IReadOnlyList<IDomainPartitionSnapshotSectionProviderV1> CreateAll()
    {
        IDomainPartitionSnapshotSectionProviderV1[] providers =
        {
            P<GovernancePolityPayloadV1>(GovernancePolityPayloadV1.PartitionId, x => x.ToStandardPayload(), GovernancePolityPayloadV1.FromStandardPayload, x => x.CanonicalDigest()),
            P<GovernanceInstitutionPayloadV1>(GovernanceInstitutionPayloadV1.PartitionId, x => x.ToStandardPayload(), GovernanceInstitutionPayloadV1.FromStandardPayload, x => x.CanonicalDigest()),
            P<GovernanceLawRulePayloadV1>(GovernanceLawRulePayloadV1.PartitionId, x => x.ToStandardPayload(), GovernanceLawRulePayloadV1.FromStandardPayload, x => x.CanonicalDigest()),
            P<GovernanceJurisdictionPayloadV1>(GovernanceJurisdictionPayloadV1.PartitionId, x => x.ToStandardPayload(), GovernanceJurisdictionPayloadV1.FromStandardPayload, x => x.CanonicalDigest()),
            P<GovernanceTerritorialClaimPayloadV1>(GovernanceTerritorialClaimPayloadV1.PartitionId, x => x.ToStandardPayload(), GovernanceTerritorialClaimPayloadV1.FromStandardPayload, x => x.CanonicalDigest()),
            P<GovernanceEffectiveControlPayloadV1>(GovernanceEffectiveControlPayloadV1.PartitionId, x => x.ToStandardPayload(), GovernanceEffectiveControlPayloadV1.FromStandardPayload, x => x.CanonicalDigest()),
            P<GovernancePublicAuthorityPayloadV1>(GovernancePublicAuthorityPayloadV1.PartitionId, x => x.ToStandardPayload(), GovernancePublicAuthorityPayloadV1.FromStandardPayload, x => x.CanonicalDigest()),
            P<GovernanceTaxFiscalPayloadV1>(GovernanceTaxFiscalPayloadV1.PartitionId, x => x.ToStandardPayload(), GovernanceTaxFiscalPayloadV1.FromStandardPayload, x => x.CanonicalDigest()),
            P<GovernancePermissionLicensePayloadV1>(GovernancePermissionLicensePayloadV1.PartitionId, x => x.ToStandardPayload(), GovernancePermissionLicensePayloadV1.FromStandardPayload, x => x.CanonicalDigest()),
            P<GovernanceDiplomacyPayloadV1>(GovernanceDiplomacyPayloadV1.PartitionId, x => x.ToStandardPayload(), GovernanceDiplomacyPayloadV1.FromStandardPayload, x => x.CanonicalDigest()),
            P<GovernanceSecurityIncidentPayloadV1>(GovernanceSecurityIncidentPayloadV1.PartitionId, x => x.ToStandardPayload(), GovernanceSecurityIncidentPayloadV1.FromStandardPayload, x => x.CanonicalDigest()),
            P<GovernanceInvestigationPayloadV1>(GovernanceInvestigationPayloadV1.PartitionId, x => x.ToStandardPayload(), GovernanceInvestigationPayloadV1.FromStandardPayload, x => x.CanonicalDigest()),
            P<GovernanceJudicialCasePayloadV1>(GovernanceJudicialCasePayloadV1.PartitionId, x => x.ToStandardPayload(), GovernanceJudicialCasePayloadV1.FromStandardPayload, x => x.CanonicalDigest()),
            P<GovernanceEnforcementPayloadV1>(GovernanceEnforcementPayloadV1.PartitionId, x => x.ToStandardPayload(), GovernanceEnforcementPayloadV1.FromStandardPayload, x => x.CanonicalDigest()),
            P<GovernanceMilitaryAuthorityPayloadV1>(GovernanceMilitaryAuthorityPayloadV1.PartitionId, x => x.ToStandardPayload(), GovernanceMilitaryAuthorityPayloadV1.FromStandardPayload, x => x.CanonicalDigest()),
            P<GovernanceBorderControlPayloadV1>(GovernanceBorderControlPayloadV1.PartitionId, x => x.ToStandardPayload(), GovernanceBorderControlPayloadV1.FromStandardPayload, x => x.CanonicalDigest()),
            P<GovernanceLineagePayloadV1>(GovernanceLineagePayloadV1.PartitionId, x => x.ToStandardPayload(), GovernanceLineagePayloadV1.FromStandardPayload, x => x.CanonicalDigest()),
        };
        return Array.AsReadOnly(providers.OrderBy(x => x.SectionId, StringComparer.Ordinal).ToArray());
    }
    private static IDomainPartitionSnapshotSectionProviderV1 P<T>(string id, Func<T,IReadOnlyDictionary<string,object?>> to, Func<IReadOnlyDictionary<string,object?>,T> from, Func<T,byte[]> digest)
        => new DomainPartitionSnapshotSectionProviderV1<T>(id, to, from, digest, StandardDomainNestedSnapshotCodecRegistryV1.Default);
}

internal static class GovernancePayloadFields
{
    public static Dictionary<string, object?> Map(params (string Name, object? Value)[] values) => values.ToDictionary(x => x.Name, x => x.Value, StringComparer.Ordinal);
    public static void AddOptional<T>(Dictionary<string, object?> values, string field, T? value) where T : struct { if (value is { } p) values[field] = p; }
    public static T Required<T>(IReadOnlyDictionary<string, object?> v, string p, string f) => v.TryGetValue(f, out var x) && x is T t ? t : throw new InvalidDataException($"governance.snapshot-payload.required:{p}:{f}");
    public static T? Optional<T>(IReadOnlyDictionary<string, object?> v, string f) where T : struct => v.TryGetValue(f, out var x) && x is not null ? (T)x : null;
    public static IReadOnlyList<string> TokenValues(IReadOnlyList<StableToken> v) => Array.AsReadOnly(v.Select(x => x.Value).ToArray());
    public static IReadOnlyList<StableToken> Tokens(IReadOnlyDictionary<string, object?> v, string p, string f) => Array.AsReadOnly(Required<IReadOnlyList<string>>(v,p,f).Select(x => new StableToken(x)).ToArray());
    public static byte[] Digest(string id, IReadOnlyDictionary<string, object?> p) => StandardDomainPayloadCanonicalDigestV1.Compute(id, p);
}
