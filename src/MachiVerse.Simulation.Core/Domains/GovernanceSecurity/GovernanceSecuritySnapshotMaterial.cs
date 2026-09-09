using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains.GovernanceSecurity;

public sealed class GovernanceSecurityDomainStateV1
{
    public GovernanceSecurityDomainStateV1(
        DomainPartitionStateV1<GovernancePolityPayloadV1> polity,
        DomainPartitionStateV1<GovernanceInstitutionPayloadV1> institution,
        DomainPartitionStateV1<GovernanceLawRulePayloadV1> lawRule,
        DomainPartitionStateV1<GovernanceJurisdictionPayloadV1> jurisdiction,
        DomainPartitionStateV1<GovernanceTerritorialClaimPayloadV1> territorialClaim,
        DomainPartitionStateV1<GovernanceEffectiveControlPayloadV1> effectiveControl,
        DomainPartitionStateV1<GovernancePublicAuthorityPayloadV1> publicAuthority,
        DomainPartitionStateV1<GovernanceTaxFiscalPayloadV1> taxFiscal,
        DomainPartitionStateV1<GovernancePermissionLicensePayloadV1> permissionLicense,
        DomainPartitionStateV1<GovernanceDiplomacyPayloadV1> diplomacy,
        DomainPartitionStateV1<GovernanceSecurityIncidentPayloadV1> securityIncident,
        DomainPartitionStateV1<GovernanceInvestigationPayloadV1> investigation,
        DomainPartitionStateV1<GovernanceJudicialCasePayloadV1> judicialCase,
        DomainPartitionStateV1<GovernanceEnforcementPayloadV1> enforcement,
        DomainPartitionStateV1<GovernanceMilitaryAuthorityPayloadV1> militaryAuthority,
        DomainPartitionStateV1<GovernanceBorderControlPayloadV1> borderControl,
        DomainPartitionStateV1<GovernanceLineagePayloadV1> lineage)
    {
        Polity = R(polity, GovernancePolityPayloadV1.PartitionId);
        Institution = R(institution, GovernanceInstitutionPayloadV1.PartitionId);
        LawRule = R(lawRule, GovernanceLawRulePayloadV1.PartitionId);
        Jurisdiction = R(jurisdiction, GovernanceJurisdictionPayloadV1.PartitionId);
        TerritorialClaim = R(territorialClaim, GovernanceTerritorialClaimPayloadV1.PartitionId);
        EffectiveControl = R(effectiveControl, GovernanceEffectiveControlPayloadV1.PartitionId);
        PublicAuthority = R(publicAuthority, GovernancePublicAuthorityPayloadV1.PartitionId);
        TaxFiscal = R(taxFiscal, GovernanceTaxFiscalPayloadV1.PartitionId);
        PermissionLicense = R(permissionLicense, GovernancePermissionLicensePayloadV1.PartitionId);
        Diplomacy = R(diplomacy, GovernanceDiplomacyPayloadV1.PartitionId);
        SecurityIncident = R(securityIncident, GovernanceSecurityIncidentPayloadV1.PartitionId);
        Investigation = R(investigation, GovernanceInvestigationPayloadV1.PartitionId);
        JudicialCase = R(judicialCase, GovernanceJudicialCasePayloadV1.PartitionId);
        Enforcement = R(enforcement, GovernanceEnforcementPayloadV1.PartitionId);
        MilitaryAuthority = R(militaryAuthority, GovernanceMilitaryAuthorityPayloadV1.PartitionId);
        BorderControl = R(borderControl, GovernanceBorderControlPayloadV1.PartitionId);
        Lineage = R(lineage, GovernanceLineagePayloadV1.PartitionId);
    }

    public DomainPartitionStateV1<GovernancePolityPayloadV1> Polity { get; }
    public DomainPartitionStateV1<GovernanceInstitutionPayloadV1> Institution { get; }
    public DomainPartitionStateV1<GovernanceLawRulePayloadV1> LawRule { get; }
    public DomainPartitionStateV1<GovernanceJurisdictionPayloadV1> Jurisdiction { get; }
    public DomainPartitionStateV1<GovernanceTerritorialClaimPayloadV1> TerritorialClaim { get; }
    public DomainPartitionStateV1<GovernanceEffectiveControlPayloadV1> EffectiveControl { get; }
    public DomainPartitionStateV1<GovernancePublicAuthorityPayloadV1> PublicAuthority { get; }
    public DomainPartitionStateV1<GovernanceTaxFiscalPayloadV1> TaxFiscal { get; }
    public DomainPartitionStateV1<GovernancePermissionLicensePayloadV1> PermissionLicense { get; }
    public DomainPartitionStateV1<GovernanceDiplomacyPayloadV1> Diplomacy { get; }
    public DomainPartitionStateV1<GovernanceSecurityIncidentPayloadV1> SecurityIncident { get; }
    public DomainPartitionStateV1<GovernanceInvestigationPayloadV1> Investigation { get; }
    public DomainPartitionStateV1<GovernanceJudicialCasePayloadV1> JudicialCase { get; }
    public DomainPartitionStateV1<GovernanceEnforcementPayloadV1> Enforcement { get; }
    public DomainPartitionStateV1<GovernanceMilitaryAuthorityPayloadV1> MilitaryAuthority { get; }
    public DomainPartitionStateV1<GovernanceBorderControlPayloadV1> BorderControl { get; }
    public DomainPartitionStateV1<GovernanceLineagePayloadV1> Lineage { get; }

    public static GovernanceSecurityDomainStateV1 CreateEmpty() => new(
        E<GovernancePolityPayloadV1>(GovernancePolityPayloadV1.PartitionId), E<GovernanceInstitutionPayloadV1>(GovernanceInstitutionPayloadV1.PartitionId),
        E<GovernanceLawRulePayloadV1>(GovernanceLawRulePayloadV1.PartitionId), E<GovernanceJurisdictionPayloadV1>(GovernanceJurisdictionPayloadV1.PartitionId),
        E<GovernanceTerritorialClaimPayloadV1>(GovernanceTerritorialClaimPayloadV1.PartitionId), E<GovernanceEffectiveControlPayloadV1>(GovernanceEffectiveControlPayloadV1.PartitionId),
        E<GovernancePublicAuthorityPayloadV1>(GovernancePublicAuthorityPayloadV1.PartitionId), E<GovernanceTaxFiscalPayloadV1>(GovernanceTaxFiscalPayloadV1.PartitionId),
        E<GovernancePermissionLicensePayloadV1>(GovernancePermissionLicensePayloadV1.PartitionId), E<GovernanceDiplomacyPayloadV1>(GovernanceDiplomacyPayloadV1.PartitionId),
        E<GovernanceSecurityIncidentPayloadV1>(GovernanceSecurityIncidentPayloadV1.PartitionId), E<GovernanceInvestigationPayloadV1>(GovernanceInvestigationPayloadV1.PartitionId),
        E<GovernanceJudicialCasePayloadV1>(GovernanceJudicialCasePayloadV1.PartitionId), E<GovernanceEnforcementPayloadV1>(GovernanceEnforcementPayloadV1.PartitionId),
        E<GovernanceMilitaryAuthorityPayloadV1>(GovernanceMilitaryAuthorityPayloadV1.PartitionId), E<GovernanceBorderControlPayloadV1>(GovernanceBorderControlPayloadV1.PartitionId),
        E<GovernanceLineagePayloadV1>(GovernanceLineagePayloadV1.PartitionId));

    public GovernanceSecurityDomainSnapshotMaterialV1 BindSnapshotMaterial(WorldStateV1 frozenState) => GovernanceSecurityDomainSnapshotMaterialV1.Bind(frozenState, this);
    private static DomainPartitionStateV1<T> E<T>(string id) => new(StandardDomainPartitionRegistry.Get(id), Array.Empty<DomainRecordEnvelopeV1<T>>());
    private static DomainPartitionStateV1<T> R<T>(DomainPartitionStateV1<T> p, string id) { ArgumentNullException.ThrowIfNull(p); if (p.Identity != StandardDomainPartitionRegistry.Get(id)) throw new InvalidDataException($"governance.runtime-state.partition-identity:{id}"); return p; }
}

public sealed class GovernanceSecurityDomainSnapshotMaterialV1
{
    private GovernanceSecurityDomainSnapshotMaterialV1(IEnumerable<IDomainPartitionSnapshotAuthorityV1> authorities)
    {
        var a = authorities?.ToArray() ?? throw new ArgumentNullException(nameof(authorities));
        if (a.Length != 17) throw new InvalidDataException("governance.snapshot-material.authority-count");
        foreach (var x in a) { ArgumentNullException.ThrowIfNull(x); x.VerifyBoundAuthority(); if (x.Identity.OwnerDomain.Value != "governance_security") throw new InvalidDataException($"governance.snapshot-material.foreign-owner:{x.PartitionId.Value}"); }
        Authorities = Array.AsReadOnly(a.OrderBy(x => x.PartitionId.Value, StringComparer.Ordinal).ToArray());
    }
    public IReadOnlyList<IDomainPartitionSnapshotAuthorityV1> Authorities { get; }

    public static GovernanceSecurityDomainSnapshotMaterialV1 Bind(WorldStateV1 frozen, GovernanceSecurityDomainStateV1 s)
    {
        ArgumentNullException.ThrowIfNull(frozen); ArgumentNullException.ThrowIfNull(s);
        return new GovernanceSecurityDomainSnapshotMaterialV1(new IDomainPartitionSnapshotAuthorityV1[]
        {
            B(frozen,s.Polity,GovernancePolityPayloadV1.PartitionId,x=>x.CanonicalDigest()), B(frozen,s.Institution,GovernanceInstitutionPayloadV1.PartitionId,x=>x.CanonicalDigest()),
            B(frozen,s.LawRule,GovernanceLawRulePayloadV1.PartitionId,x=>x.CanonicalDigest()), B(frozen,s.Jurisdiction,GovernanceJurisdictionPayloadV1.PartitionId,x=>x.CanonicalDigest()),
            B(frozen,s.TerritorialClaim,GovernanceTerritorialClaimPayloadV1.PartitionId,x=>x.CanonicalDigest()), B(frozen,s.EffectiveControl,GovernanceEffectiveControlPayloadV1.PartitionId,x=>x.CanonicalDigest()),
            B(frozen,s.PublicAuthority,GovernancePublicAuthorityPayloadV1.PartitionId,x=>x.CanonicalDigest()), B(frozen,s.TaxFiscal,GovernanceTaxFiscalPayloadV1.PartitionId,x=>x.CanonicalDigest()),
            B(frozen,s.PermissionLicense,GovernancePermissionLicensePayloadV1.PartitionId,x=>x.CanonicalDigest()), B(frozen,s.Diplomacy,GovernanceDiplomacyPayloadV1.PartitionId,x=>x.CanonicalDigest()),
            B(frozen,s.SecurityIncident,GovernanceSecurityIncidentPayloadV1.PartitionId,x=>x.CanonicalDigest()), B(frozen,s.Investigation,GovernanceInvestigationPayloadV1.PartitionId,x=>x.CanonicalDigest()),
            B(frozen,s.JudicialCase,GovernanceJudicialCasePayloadV1.PartitionId,x=>x.CanonicalDigest()), B(frozen,s.Enforcement,GovernanceEnforcementPayloadV1.PartitionId,x=>x.CanonicalDigest()),
            B(frozen,s.MilitaryAuthority,GovernanceMilitaryAuthorityPayloadV1.PartitionId,x=>x.CanonicalDigest()), B(frozen,s.BorderControl,GovernanceBorderControlPayloadV1.PartitionId,x=>x.CanonicalDigest()),
            B(frozen,s.Lineage,GovernanceLineagePayloadV1.PartitionId,x=>x.CanonicalDigest())
        });
    }
    private static DomainPartitionSnapshotAuthorityV1<T> B<T>(WorldStateV1 f, DomainPartitionStateV1<T> p, string id, Func<T,byte[]> d)
        => new(p, f.Partitions.Get(id).Header, d);
}
