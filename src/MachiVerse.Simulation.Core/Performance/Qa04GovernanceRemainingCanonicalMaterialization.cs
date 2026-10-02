using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed class Qa04GovernanceRemainingCanonicalMaterializationV1
{
    internal Qa04GovernanceRemainingCanonicalMaterializationV1(
        DomainPartitionStateV1<GovernanceLawRulePayloadV1> lawRules,
        DomainPartitionStateV1<GovernanceTaxFiscalPayloadV1> taxFiscal,
        DomainPartitionStateV1<GovernanceDiplomacyPayloadV1> diplomacy,
        DomainPartitionStateV1<GovernanceSecurityIncidentPayloadV1> securityIncidents,
        DomainPartitionStateV1<GovernanceInvestigationPayloadV1> investigations,
        DomainPartitionStateV1<GovernanceJudicialCasePayloadV1> judicialCases,
        DomainPartitionStateV1<GovernanceEnforcementPayloadV1> enforcements,
        DomainPartitionStateV1<GovernanceMilitaryAuthorityPayloadV1> militaryAuthorities,
        DomainPartitionStateV1<GovernanceBorderControlPayloadV1> borderControls,
        DomainPartitionStateV1<GovernanceLineagePayloadV1> lineages,
        IReadOnlyList<DomainRecordEnvelopeV1<GovernanceLawRulePayloadV1>> lawRuleRecordsByOrdinal,
        IDomainRecordSchemaResolverV1 references)
    {
        LawRules = lawRules;
        TaxFiscal = taxFiscal;
        Diplomacy = diplomacy;
        SecurityIncidents = securityIncidents;
        Investigations = investigations;
        JudicialCases = judicialCases;
        Enforcements = enforcements;
        MilitaryAuthorities = militaryAuthorities;
        BorderControls = borderControls;
        Lineages = lineages;
        LawRuleRecordsByOrdinal = lawRuleRecordsByOrdinal;
        References = references;
    }

    public DomainPartitionStateV1<GovernanceLawRulePayloadV1> LawRules { get; }
    public DomainPartitionStateV1<GovernanceTaxFiscalPayloadV1> TaxFiscal { get; }
    public DomainPartitionStateV1<GovernanceDiplomacyPayloadV1> Diplomacy { get; }
    public DomainPartitionStateV1<GovernanceSecurityIncidentPayloadV1> SecurityIncidents { get; }
    public DomainPartitionStateV1<GovernanceInvestigationPayloadV1> Investigations { get; }
    public DomainPartitionStateV1<GovernanceJudicialCasePayloadV1> JudicialCases { get; }
    public DomainPartitionStateV1<GovernanceEnforcementPayloadV1> Enforcements { get; }
    public DomainPartitionStateV1<GovernanceMilitaryAuthorityPayloadV1> MilitaryAuthorities { get; }
    public DomainPartitionStateV1<GovernanceBorderControlPayloadV1> BorderControls { get; }
    public DomainPartitionStateV1<GovernanceLineagePayloadV1> Lineages { get; }
    public IReadOnlyList<DomainRecordEnvelopeV1<GovernanceLawRulePayloadV1>> LawRuleRecordsByOrdinal { get; }
    public IDomainRecordSchemaResolverV1 References { get; }

    public ulong MaterializedRecordCount => checked(
        LawRules.ItemCount + TaxFiscal.ItemCount + Diplomacy.ItemCount + SecurityIncidents.ItemCount +
        Investigations.ItemCount + JudicialCases.ItemCount + Enforcements.ItemCount +
        MilitaryAuthorities.ItemCount + BorderControls.ItemCount + Lineages.ItemCount);
}

/// <summary>
/// Production-path realization of phase4-alpha11-governance-remaining-authority.md.
/// Every outgoing reference must resolve to an already materialized canonical record or to a record
/// created earlier in this dependency-ordered materialization.
/// </summary>
public static class Qa04GovernanceRemainingCanonicalAuthorityV1
{
    public const ulong LawRuleCount = 30_000;
    public const ulong TaxFiscalCount = 50_000;
    public const ulong DiplomacyCount = 10_000;
    public const ulong SecurityIncidentCount = 45_000;
    public const ulong InvestigationCount = 30_000;
    public const ulong JudicialCaseCount = 25_000;
    public const ulong EnforcementCount = 30_000;
    public const ulong MilitaryAuthorityCount = 10_000;
    public const ulong BorderControlCount = 10_000;
    public const ulong LineageCount = 19_000;
    public const ulong CanonicalCount = 259_000;

    public const ulong LawRuleStart = 1_606_000;
    public const ulong TaxFiscalStart = 1_701_000;
    public const ulong DiplomacyStart = 1_821_000;
    public const ulong SecurityIncidentStart = 1_831_000;
    public const ulong InvestigationStart = 1_876_000;
    public const ulong JudicialCaseStart = 1_906_000;
    public const ulong EnforcementStart = 1_931_000;
    public const ulong MilitaryAuthorityStart = 1_961_000;
    public const ulong BorderControlStart = 1_971_000;
    public const ulong LineageStart = 1_981_000;

    private const ulong PolityCount = 1_000;
    private const ulong InstitutionCount = 5_000;
    private const ulong JurisdictionCount = 10_000;
    private const ulong PublicAuthorityCount = 25_000;
    private const ulong OrganizationCount = 10_000;
    private const ulong TileScopeCount = 4_096;
    private const ulong BuiltStructureCount = 15_000;

    public static readonly StableToken SubjectClassKey = new("perf.subject-class");
    public static readonly StableToken ReferenceSubject = new("perf.reference-subject");
    public static readonly StableToken ReferencePermit = new("perf.reference-permit");
    public static readonly StableToken Active = new("active");
    public static readonly StableToken TaxKind = new("perf.tax-policy");
    public static readonly StableToken TaxBase = new("perf.reference-tax-base");
    public static readonly StableToken DiplomacyKind = new("perf.diplomatic-relation");
    public static readonly StableToken SecurityIncidentKind = new("perf.security-incident");
    public static readonly StableToken Recognized = new("recognized");
    public static readonly StableToken Open = new("open");
    public static readonly StableToken CaseKind = new("perf.reference-case");
    public static readonly StableToken EnforcementKind = new("perf.reference-enforcement-order");
    public static readonly StableToken Issued = new("issued");
    public static readonly StableToken Mission = new("perf.reference-mission");
    public static readonly StableToken Genesis = new("perf.genesis");

    private static readonly byte[] DiplomacyTermsDigest = Convert.FromHexString(
        "9ff4bf863b8803c1a7d0c7f765802e590e8ea17b3db2fed8b17e95e0cea4d4d8");

    public static void ValidateCanonicalContract()
    {
        Qa04SocietyGovernanceReferenceDecompositionV1.ValidateCanonicalContract();
        Qa04GovernanceTerritorialFoundationCanonicalAuthorityV1.ValidateCanonicalContract();
        Qa04FacilityServiceCanonicalAuthorityV1.ValidateCanonicalContract();

        ValidateSlice(GovernanceLawRulePayloadV1.PartitionId, LawRuleStart, LawRuleCount);
        ValidateSlice(GovernanceTaxFiscalPayloadV1.PartitionId, TaxFiscalStart, TaxFiscalCount);
        ValidateSlice(GovernanceDiplomacyPayloadV1.PartitionId, DiplomacyStart, DiplomacyCount);
        ValidateSlice(GovernanceSecurityIncidentPayloadV1.PartitionId, SecurityIncidentStart, SecurityIncidentCount);
        ValidateSlice(GovernanceInvestigationPayloadV1.PartitionId, InvestigationStart, InvestigationCount);
        ValidateSlice(GovernanceJudicialCasePayloadV1.PartitionId, JudicialCaseStart, JudicialCaseCount);
        ValidateSlice(GovernanceEnforcementPayloadV1.PartitionId, EnforcementStart, EnforcementCount);
        ValidateSlice(GovernanceMilitaryAuthorityPayloadV1.PartitionId, MilitaryAuthorityStart, MilitaryAuthorityCount);
        ValidateSlice(GovernanceBorderControlPayloadV1.PartitionId, BorderControlStart, BorderControlCount);
        ValidateSlice(GovernanceLineagePayloadV1.PartitionId, LineageStart, LineageCount);

        if (LawRuleCount + TaxFiscalCount + DiplomacyCount + SecurityIncidentCount + InvestigationCount +
            JudicialCaseCount + EnforcementCount + MilitaryAuthorityCount + BorderControlCount + LineageCount != CanonicalCount ||
            DiplomacyTermsDigest.Length != 32 ||
            !CryptographicOperations.FixedTimeEquals(
                DiplomacyTermsDigest,
                SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("perf.reference.v1/governance/diplomacy/terms/v1"))))
            throw new InvalidDataException("qa04.governance.remaining-contract-drift");
    }

    public static Qa04GovernanceRemainingCanonicalMaterializationV1 MaterializeCanonical()
    {
        ValidateCanonicalContract();

        var territorial = Qa04GovernanceTerritorialFoundationCanonicalAuthorityV1.MaterializeCanonical();
        var facility = Qa04FacilityServiceCanonicalAuthorityV1.MaterializeCanonical();
        var resolver = new CompositeReferenceResolver(territorial.References, facility.References);
        resolver.AddAll(GovernanceJurisdictionPayloadV1.PartitionId, territorial.JurisdictionRecordsByOrdinal);
        resolver.AddAll(BuiltStructurePayloadV1.PartitionId, facility.BuiltStructureRecordsByOrdinal);
        RequireUpstreamReferences(resolver);

        var validator = new StandardDomainPayloadCodecValidatorV1();
        var lawRules = MaterializeLawRules(validator, resolver);
        resolver.AddAll(GovernanceLawRulePayloadV1.PartitionId, lawRules);
        var taxFiscal = MaterializeTaxFiscal(validator, resolver);
        resolver.AddAll(GovernanceTaxFiscalPayloadV1.PartitionId, taxFiscal);
        var diplomacy = MaterializeDiplomacy(validator, resolver);
        resolver.AddAll(GovernanceDiplomacyPayloadV1.PartitionId, diplomacy);
        var incidents = MaterializeSecurityIncidents(validator, resolver);
        resolver.AddAll(GovernanceSecurityIncidentPayloadV1.PartitionId, incidents);
        var investigations = MaterializeInvestigations(validator, resolver);
        resolver.AddAll(GovernanceInvestigationPayloadV1.PartitionId, investigations);
        var judicialCases = MaterializeJudicialCases(validator, resolver);
        resolver.AddAll(GovernanceJudicialCasePayloadV1.PartitionId, judicialCases);
        var enforcements = MaterializeEnforcements(validator, resolver);
        resolver.AddAll(GovernanceEnforcementPayloadV1.PartitionId, enforcements);
        var military = MaterializeMilitaryAuthorities(validator, resolver);
        resolver.AddAll(GovernanceMilitaryAuthorityPayloadV1.PartitionId, military);
        var borders = MaterializeBorderControls(validator, resolver, facility.BuiltStructureRecordsByOrdinal);
        resolver.AddAll(GovernanceBorderControlPayloadV1.PartitionId, borders);
        var lineages = MaterializeLineages(validator, resolver, lawRules);
        resolver.AddAll(GovernanceLineagePayloadV1.PartitionId, lineages);

        EnsureUniqueIds(lawRules.Select(static record => record.RecordId)
            .Concat(taxFiscal.Select(static record => record.RecordId))
            .Concat(diplomacy.Select(static record => record.RecordId))
            .Concat(incidents.Select(static record => record.RecordId))
            .Concat(investigations.Select(static record => record.RecordId))
            .Concat(judicialCases.Select(static record => record.RecordId))
            .Concat(enforcements.Select(static record => record.RecordId))
            .Concat(military.Select(static record => record.RecordId))
            .Concat(borders.Select(static record => record.RecordId))
            .Concat(lineages.Select(static record => record.RecordId)));

        var result = new Qa04GovernanceRemainingCanonicalMaterializationV1(
            Partition(GovernanceLawRulePayloadV1.PartitionId, lawRules),
            Partition(GovernanceTaxFiscalPayloadV1.PartitionId, taxFiscal),
            Partition(GovernanceDiplomacyPayloadV1.PartitionId, diplomacy),
            Partition(GovernanceSecurityIncidentPayloadV1.PartitionId, incidents),
            Partition(GovernanceInvestigationPayloadV1.PartitionId, investigations),
            Partition(GovernanceJudicialCasePayloadV1.PartitionId, judicialCases),
            Partition(GovernanceEnforcementPayloadV1.PartitionId, enforcements),
            Partition(GovernanceMilitaryAuthorityPayloadV1.PartitionId, military),
            Partition(GovernanceBorderControlPayloadV1.PartitionId, borders),
            Partition(GovernanceLineagePayloadV1.PartitionId, lineages),
            Array.AsReadOnly(lawRules),
            resolver);
        if (result.MaterializedRecordCount != CanonicalCount)
            throw new InvalidDataException("qa04.governance.remaining-total-count-drift");
        return result;
    }

    private static DomainRecordEnvelopeV1<GovernanceLawRulePayloadV1>[] MaterializeLawRules(StandardDomainPayloadCodecValidatorV1 validator, CompositeReferenceResolver resolver)
    {
        var predicate = new GovernanceRulePredicateAstNestedValueV1(new LawPredicateNodeV1(
            LawPredicateNodeKindV1.FactEquals, Array.Empty<LawPredicateNodeV1>(), Key: SubjectClassKey, TokenValue: ReferenceSubject));
        var effect = new GovernanceRuleEffectAstNestedValueV1(new LawEffectV1(LawEffectKindV1.Permit, ReferencePermit));
        var records = new DomainRecordEnvelopeV1<GovernanceLawRulePayloadV1>[checked((int)LawRuleCount)];
        for (ulong ordinal = 0; ordinal < LawRuleCount; ordinal++)
        {
            var payload = new GovernanceLawRulePayloadV1(
                ExistingRef(GovernanceJurisdictionPayloadV1.PartitionId, ordinal % JurisdictionCount, resolver),
                checked((int)(ordinal / JurisdictionCount)), 1, 0, null, predicate, effect, Active);
            records[checked((int)ordinal)] = BuildValidated(validator, resolver, GovernanceLawRulePayloadV1.PartitionId, LawRuleStart, ordinal, payload, payload.ToStandardPayload());
        }
        return records;
    }

    private static DomainRecordEnvelopeV1<GovernanceTaxFiscalPayloadV1>[] MaterializeTaxFiscal(StandardDomainPayloadCodecValidatorV1 validator, CompositeReferenceResolver resolver)
    {
        var records = new DomainRecordEnvelopeV1<GovernanceTaxFiscalPayloadV1>[checked((int)TaxFiscalCount)];
        for (ulong ordinal = 0; ordinal < TaxFiscalCount; ordinal++)
        {
            var payload = new GovernanceTaxFiscalPayloadV1(
                ExistingRef(GovernancePolityPayloadV1.PartitionId, ordinal % PolityCount, resolver),
                TaxKind, TaxBase, checked((uint)(100_000 + 10_000 * (ordinal % 5))), null, null, null, Active);
            records[checked((int)ordinal)] = BuildValidated(validator, resolver, GovernanceTaxFiscalPayloadV1.PartitionId, TaxFiscalStart, ordinal, payload, payload.ToStandardPayload());
        }
        return records;
    }

    private static DomainRecordEnvelopeV1<GovernanceDiplomacyPayloadV1>[] MaterializeDiplomacy(StandardDomainPayloadCodecValidatorV1 validator, CompositeReferenceResolver resolver)
    {
        var records = new DomainRecordEnvelopeV1<GovernanceDiplomacyPayloadV1>[checked((int)DiplomacyCount)];
        for (ulong ordinal = 0; ordinal < DiplomacyCount; ordinal++)
        {
            var p0 = ordinal % PolityCount;
            var p1 = (p0 + 1 + ordinal / PolityCount) % PolityCount;
            if (p0 == p1) throw new InvalidDataException("qa04.governance.diplomacy-self-party");
            var payload = new GovernanceDiplomacyPayloadV1(
                SortRefs(ExistingRef(GovernancePolityPayloadV1.PartitionId, p0, resolver), ExistingRef(GovernancePolityPayloadV1.PartitionId, p1, resolver)),
                DiplomacyKind, Active, 0, null,
                new[] { GeneratedRef(GovernanceLawRulePayloadV1.PartitionId, LawRuleStart, ordinal % LawRuleCount, resolver) },
                DiplomacyTermsDigest.ToArray());
            records[checked((int)ordinal)] = BuildValidated(validator, resolver, GovernanceDiplomacyPayloadV1.PartitionId, DiplomacyStart, ordinal, payload, payload.ToStandardPayload());
        }
        return records;
    }

    private static DomainRecordEnvelopeV1<GovernanceSecurityIncidentPayloadV1>[] MaterializeSecurityIncidents(StandardDomainPayloadCodecValidatorV1 validator, CompositeReferenceResolver resolver)
    {
        var records = new DomainRecordEnvelopeV1<GovernanceSecurityIncidentPayloadV1>[checked((int)SecurityIncidentCount)];
        for (ulong ordinal = 0; ordinal < SecurityIncidentCount; ordinal++)
        {
            var scope = Qa04SpatialTileScopeAuthorityV1.ScopeRef(checked((ushort)(ordinal % TileScopeCount)));
            if (!resolver.Exists(scope)) throw new InvalidDataException("qa04.governance.remaining-tile-scope-missing");
            var payload = new GovernanceSecurityIncidentPayloadV1(
                SecurityIncidentKind,
                new[] { ExistingRef(SocietyOrganizationPayloadV1.PartitionId, ordinal % OrganizationCount, resolver) },
                scope, 0, Array.Empty<PartitionRecordRefV1>(), Recognized,
                checked((uint)(100_000 + 100_000 * (ordinal % 9))));
            records[checked((int)ordinal)] = BuildValidated(validator, resolver, GovernanceSecurityIncidentPayloadV1.PartitionId, SecurityIncidentStart, ordinal, payload, payload.ToStandardPayload());
        }
        return records;
    }

    private static DomainRecordEnvelopeV1<GovernanceInvestigationPayloadV1>[] MaterializeInvestigations(StandardDomainPayloadCodecValidatorV1 validator, CompositeReferenceResolver resolver)
    {
        var records = new DomainRecordEnvelopeV1<GovernanceInvestigationPayloadV1>[checked((int)InvestigationCount)];
        for (ulong ordinal = 0; ordinal < InvestigationCount; ordinal++)
        {
            var payload = new GovernanceInvestigationPayloadV1(
                GeneratedRef(GovernanceSecurityIncidentPayloadV1.PartitionId, SecurityIncidentStart, ordinal % SecurityIncidentCount, resolver),
                ExistingRef(GovernancePublicAuthorityPayloadV1.PartitionId, ordinal % PublicAuthorityCount, resolver),
                new[] { ExistingRef(GovernanceInstitutionPayloadV1.PartitionId, ordinal % InstitutionCount, resolver) },
                Array.Empty<PartitionRecordRefV1>(),
                new[] { ExistingRef(SocietyOrganizationPayloadV1.PartitionId, ordinal % OrganizationCount, resolver) },
                Open, 0, null);
            records[checked((int)ordinal)] = BuildValidated(validator, resolver, GovernanceInvestigationPayloadV1.PartitionId, InvestigationStart, ordinal, payload, payload.ToStandardPayload());
        }
        return records;
    }

    private static DomainRecordEnvelopeV1<GovernanceJudicialCasePayloadV1>[] MaterializeJudicialCases(StandardDomainPayloadCodecValidatorV1 validator, CompositeReferenceResolver resolver)
    {
        var records = new DomainRecordEnvelopeV1<GovernanceJudicialCasePayloadV1>[checked((int)JudicialCaseCount)];
        for (ulong ordinal = 0; ordinal < JudicialCaseCount; ordinal++)
        {
            var parties = SortRefs(
                ExistingRef(SocietyOrganizationPayloadV1.PartitionId, ordinal % OrganizationCount, resolver),
                ExistingRef(SocietyOrganizationPayloadV1.PartitionId, (ordinal + 1) % OrganizationCount, resolver));
            if (parties[0] == parties[1]) throw new InvalidDataException("qa04.governance.judicial-party-collapse");
            var payload = new GovernanceJudicialCasePayloadV1(
                CaseKind, ExistingRef(GovernanceJurisdictionPayloadV1.PartitionId, ordinal % JurisdictionCount, resolver),
                parties, Array.Empty<PartitionRecordRefV1>(),
                new[] { GeneratedRef(GovernanceLawRulePayloadV1.PartitionId, LawRuleStart, ordinal % LawRuleCount, resolver) },
                Open, 0, null);
            records[checked((int)ordinal)] = BuildValidated(validator, resolver, GovernanceJudicialCasePayloadV1.PartitionId, JudicialCaseStart, ordinal, payload, payload.ToStandardPayload());
        }
        return records;
    }

    private static DomainRecordEnvelopeV1<GovernanceEnforcementPayloadV1>[] MaterializeEnforcements(StandardDomainPayloadCodecValidatorV1 validator, CompositeReferenceResolver resolver)
    {
        var records = new DomainRecordEnvelopeV1<GovernanceEnforcementPayloadV1>[checked((int)EnforcementCount)];
        for (ulong ordinal = 0; ordinal < EnforcementCount; ordinal++)
        {
            var subject = ExistingRef(SocietyOrganizationPayloadV1.PartitionId, ordinal % OrganizationCount, resolver);
            var target = ExistingRef(SocietyOrganizationPayloadV1.PartitionId, (ordinal + 1) % OrganizationCount, resolver);
            if (subject == target) throw new InvalidDataException("qa04.governance.enforcement-target-collapse");
            var payload = new GovernanceEnforcementPayloadV1(
                ExistingRef(GovernancePublicAuthorityPayloadV1.PartitionId, ordinal % PublicAuthorityCount, resolver),
                EnforcementKind, new[] { subject }, new[] { target }, Issued, 0, 0, Array.Empty<PartitionRecordRefV1>());
            records[checked((int)ordinal)] = BuildValidated(validator, resolver, GovernanceEnforcementPayloadV1.PartitionId, EnforcementStart, ordinal, payload, payload.ToStandardPayload());
        }
        return records;
    }

    private static DomainRecordEnvelopeV1<GovernanceMilitaryAuthorityPayloadV1>[] MaterializeMilitaryAuthorities(StandardDomainPayloadCodecValidatorV1 validator, CompositeReferenceResolver resolver)
    {
        var records = new DomainRecordEnvelopeV1<GovernanceMilitaryAuthorityPayloadV1>[checked((int)MilitaryAuthorityCount)];
        for (ulong ordinal = 0; ordinal < MilitaryAuthorityCount; ordinal++)
        {
            var jurisdiction = ExistingRef(GovernanceJurisdictionPayloadV1.PartitionId, ordinal % JurisdictionCount, resolver);
            var payload = new GovernanceMilitaryAuthorityPayloadV1(
                ExistingRef(GovernancePolityPayloadV1.PartitionId, ordinal % PolityCount, resolver),
                ExistingRef(SocietyOrganizationPayloadV1.PartitionId, ordinal % OrganizationCount, resolver),
                ExistingRef(GovernancePublicAuthorityPayloadV1.PartitionId, ordinal % PublicAuthorityCount, resolver),
                Mission, new[] { jurisdiction }, new[] { jurisdiction }, Active, 0);
            records[checked((int)ordinal)] = BuildValidated(validator, resolver, GovernanceMilitaryAuthorityPayloadV1.PartitionId, MilitaryAuthorityStart, ordinal, payload, payload.ToStandardPayload());
        }
        return records;
    }

    private static DomainRecordEnvelopeV1<GovernanceBorderControlPayloadV1>[] MaterializeBorderControls(
        StandardDomainPayloadCodecValidatorV1 validator, CompositeReferenceResolver resolver,
        IReadOnlyList<DomainRecordEnvelopeV1<BuiltStructurePayloadV1>> builtStructures)
    {
        if ((ulong)builtStructures.Count < BuiltStructureCount)
            throw new InvalidDataException("qa04.governance.border-built-structure-count");
        var records = new DomainRecordEnvelopeV1<GovernanceBorderControlPayloadV1>[checked((int)BorderControlCount)];
        for (ulong ordinal = 0; ordinal < BorderControlCount; ordinal++)
        {
            var checkpointRecord = builtStructures[checked((int)(ordinal % BuiltStructureCount))];
            var checkpoint = new PartitionRecordRefV1(BuiltStructurePayloadV1.PartitionId, checkpointRecord.RecordId);
            var boundary = Qa04SpatialTileScopeAuthorityV1.ScopeRef(checked((ushort)(ordinal % TileScopeCount)));
            if (!resolver.Exists(checkpoint) || !resolver.Exists(boundary))
                throw new InvalidDataException("qa04.governance.border-anchor-missing");
            var payload = new GovernanceBorderControlPayloadV1(
                ExistingRef(GovernanceJurisdictionPayloadV1.PartitionId, ordinal, resolver), boundary,
                new[] { checkpoint },
                new[] { GeneratedRef(GovernanceLawRulePayloadV1.PartitionId, LawRuleStart, ordinal % LawRuleCount, resolver) },
                Active, checked((uint)(1 + ordinal % 1_000)));
            records[checked((int)ordinal)] = BuildValidated(validator, resolver, GovernanceBorderControlPayloadV1.PartitionId, BorderControlStart, ordinal, payload, payload.ToStandardPayload());
        }
        return records;
    }

    private static DomainRecordEnvelopeV1<GovernanceLineagePayloadV1>[] MaterializeLineages(
        StandardDomainPayloadCodecValidatorV1 validator, CompositeReferenceResolver resolver,
        IReadOnlyList<DomainRecordEnvelopeV1<GovernanceLawRulePayloadV1>> lawRules)
    {
        var records = new DomainRecordEnvelopeV1<GovernanceLineagePayloadV1>[checked((int)LineageCount)];
        for (ulong ordinal = 0; ordinal < LineageCount; ordinal++)
        {
            var source = lawRules[checked((int)ordinal)];
            var payload = new GovernanceLineagePayloadV1(
                new PartitionRecordRefV1(GovernanceLawRulePayloadV1.PartitionId, source.RecordId),
                Array.Empty<PartitionRecordRefV1>(), Genesis, 0, source.Payload.CanonicalDigest());
            records[checked((int)ordinal)] = BuildValidated(validator, resolver, GovernanceLineagePayloadV1.PartitionId, LineageStart, ordinal, payload, payload.ToStandardPayload());
        }
        return records;
    }

    private static DomainRecordEnvelopeV1<TPayload> BuildValidated<TPayload>(
        StandardDomainPayloadCodecValidatorV1 validator, IDomainRecordSchemaResolverV1 resolver,
        string partitionId, ulong expectedStart, ulong localOrdinal, TPayload payload,
        IReadOnlyDictionary<string, object?> standardPayload)
    {
        validator.Validate(partitionId, standardPayload, resolver);
        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(partitionId);
        if (slice.StartOrdinal != expectedStart || localOrdinal >= slice.Count)
            throw new InvalidDataException($"qa04.governance.remaining-binding-range:{partitionId}");
        var binding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(checked(expectedStart + localOrdinal));
        if (binding.PartitionId.Value != partitionId || binding.PartitionLocalOrdinal != localOrdinal ||
            binding.UsesSpecializedIdentity || binding.Descriptor.DetailLevel != DetailLevelV1.D2RegionalAggregate)
            throw new InvalidDataException($"qa04.governance.remaining-binding-drift:{partitionId}");
        return new DomainRecordEnvelopeV1<TPayload>(
            binding.Descriptor.RecordId, StandardDomainPartitionRegistry.Get(partitionId).RecordSchema,
            revision: 1, createdStep: 0, retiredStep: null, binding.Descriptor.DetailLevel, lineageRef: null, payload);
    }

    private static PartitionRecordRefV1 ExistingRef(string partitionId, ulong localOrdinal, IDomainRecordSchemaResolverV1 resolver)
    {
        var reference = DescriptorRef(partitionId, localOrdinal);
        if (!resolver.Exists(reference))
            throw new InvalidDataException($"qa04.governance.remaining-upstream-reference-missing:{partitionId}:{localOrdinal}");
        return reference;
    }

    private static PartitionRecordRefV1 GeneratedRef(string partitionId, ulong expectedStart, ulong localOrdinal, IDomainRecordSchemaResolverV1 resolver)
    {
        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(partitionId);
        if (slice.StartOrdinal != expectedStart) throw new InvalidDataException($"qa04.governance.remaining-slice-start-drift:{partitionId}");
        var reference = DescriptorRef(partitionId, localOrdinal);
        if (!resolver.Exists(reference))
            throw new InvalidDataException($"qa04.governance.remaining-generated-reference-missing:{partitionId}:{localOrdinal}");
        return reference;
    }

    private static PartitionRecordRefV1 DescriptorRef(string partitionId, ulong localOrdinal)
    {
        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(partitionId);
        if (localOrdinal >= slice.Count) throw new ArgumentOutOfRangeException(nameof(localOrdinal));
        var binding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(checked(slice.StartOrdinal + localOrdinal));
        if (binding.PartitionId.Value != partitionId || binding.PartitionLocalOrdinal != localOrdinal || binding.UsesSpecializedIdentity)
            throw new InvalidDataException($"qa04.governance.remaining-descriptor-ref-drift:{partitionId}");
        return new PartitionRecordRefV1(partitionId, binding.Descriptor.RecordId);
    }

    private static void RequireUpstreamReferences(IDomainRecordSchemaResolverV1 resolver)
    {
        _ = ExistingRef(GovernancePolityPayloadV1.PartitionId, 0, resolver);
        _ = ExistingRef(GovernancePolityPayloadV1.PartitionId, PolityCount - 1, resolver);
        _ = ExistingRef(GovernanceInstitutionPayloadV1.PartitionId, 0, resolver);
        _ = ExistingRef(GovernanceInstitutionPayloadV1.PartitionId, InstitutionCount - 1, resolver);
        _ = ExistingRef(GovernanceJurisdictionPayloadV1.PartitionId, 0, resolver);
        _ = ExistingRef(GovernanceJurisdictionPayloadV1.PartitionId, JurisdictionCount - 1, resolver);
        _ = ExistingRef(GovernancePublicAuthorityPayloadV1.PartitionId, 0, resolver);
        _ = ExistingRef(GovernancePublicAuthorityPayloadV1.PartitionId, PublicAuthorityCount - 1, resolver);
        _ = ExistingRef(SocietyOrganizationPayloadV1.PartitionId, 0, resolver);
        _ = ExistingRef(SocietyOrganizationPayloadV1.PartitionId, OrganizationCount - 1, resolver);
        for (ushort scope = 0; scope < TileScopeCount; scope++)
        {
            if (!resolver.Exists(Qa04SpatialTileScopeAuthorityV1.ScopeRef(scope)))
                throw new InvalidDataException("qa04.governance.remaining-tile-scope-missing");
        }
    }

    private static IReadOnlyList<PartitionRecordRefV1> SortRefs(PartitionRecordRefV1 first, PartitionRecordRefV1 second)
        => new[] { first, second }
            .OrderBy(static value => value.PartitionId.Value, StringComparer.Ordinal)
            .ThenBy(static value => value.RecordId)
            .ToArray();

    private static DomainPartitionStateV1<TPayload> Partition<TPayload>(string partitionId, IReadOnlyList<DomainRecordEnvelopeV1<TPayload>> records)
        => new(StandardDomainPartitionRegistry.Get(partitionId), records);

    private static void ValidateSlice(string partitionId, ulong expectedStart, ulong expectedCount)
    {
        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(partitionId);
        if (slice.StartOrdinal != expectedStart || slice.Count != expectedCount || slice.UsesSpecializedIdentity)
            throw new InvalidDataException($"qa04.governance.remaining-slice-drift:{partitionId}");
        if (StandardDomainPartitionRegistry.Get(partitionId).OwnerDomain.Value != "governance_security")
            throw new InvalidDataException($"qa04.governance.remaining-owner-drift:{partitionId}");
    }

    private static void EnsureUniqueIds(IEnumerable<OpaqueId128> ids)
    {
        var set = new HashSet<OpaqueId128>();
        ulong count = 0;
        foreach (var id in ids)
        {
            if (!set.Add(id)) throw new InvalidDataException("qa04.governance.remaining-record-id-duplicate");
            count++;
        }
        if (count != CanonicalCount) throw new InvalidDataException("qa04.governance.remaining-unique-id-count");
    }

    private sealed class CompositeReferenceResolver : IDomainRecordSchemaResolverV1
    {
        private readonly IDomainRecordSchemaResolverV1[] _upstream;
        private readonly Dictionary<PartitionRecordRefV1, SchemaRefV1> _own = new();

        public CompositeReferenceResolver(params IDomainRecordSchemaResolverV1[] upstream)
            => _upstream = upstream ?? throw new ArgumentNullException(nameof(upstream));

        public void AddAll<TPayload>(string partitionId, IEnumerable<DomainRecordEnvelopeV1<TPayload>> records)
        {
            foreach (var record in records)
            {
                var reference = new PartitionRecordRefV1(partitionId, record.RecordId);
                if (!_own.TryAdd(reference, record.RecordSchema) && _own[reference] != record.RecordSchema)
                    throw new InvalidDataException("qa04.governance.remaining-reference-schema-conflict");
            }
        }

        public bool Exists(PartitionRecordRefV1 reference)
            => _own.ContainsKey(reference) || _upstream.Any(value => value.Exists(reference));

        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
        {
            if (_own.TryGetValue(reference, out schema)) return true;
            foreach (var upstream in _upstream)
                if (upstream.TryGetRecordSchema(reference, out schema)) return true;
            schema = default;
            return false;
        }
    }
}
