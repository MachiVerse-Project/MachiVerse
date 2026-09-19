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
        Polity = RequireIdentity(polity, GovernancePolityPayloadV1.PartitionId);
        Institution = RequireIdentity(institution, GovernanceInstitutionPayloadV1.PartitionId);
        LawRule = RequireIdentity(lawRule, GovernanceLawRulePayloadV1.PartitionId);
        Jurisdiction = RequireIdentity(jurisdiction, GovernanceJurisdictionPayloadV1.PartitionId);
        TerritorialClaim = RequireIdentity(territorialClaim, GovernanceTerritorialClaimPayloadV1.PartitionId);
        EffectiveControl = RequireIdentity(effectiveControl, GovernanceEffectiveControlPayloadV1.PartitionId);
        PublicAuthority = RequireIdentity(publicAuthority, GovernancePublicAuthorityPayloadV1.PartitionId);
        TaxFiscal = RequireIdentity(taxFiscal, GovernanceTaxFiscalPayloadV1.PartitionId);
        PermissionLicense = RequireIdentity(permissionLicense, GovernancePermissionLicensePayloadV1.PartitionId);
        Diplomacy = RequireIdentity(diplomacy, GovernanceDiplomacyPayloadV1.PartitionId);
        SecurityIncident = RequireIdentity(securityIncident, GovernanceSecurityIncidentPayloadV1.PartitionId);
        Investigation = RequireIdentity(investigation, GovernanceInvestigationPayloadV1.PartitionId);
        JudicialCase = RequireIdentity(judicialCase, GovernanceJudicialCasePayloadV1.PartitionId);
        Enforcement = RequireIdentity(enforcement, GovernanceEnforcementPayloadV1.PartitionId);
        MilitaryAuthority = RequireIdentity(militaryAuthority, GovernanceMilitaryAuthorityPayloadV1.PartitionId);
        BorderControl = RequireIdentity(borderControl, GovernanceBorderControlPayloadV1.PartitionId);
        Lineage = RequireIdentity(lineage, GovernanceLineagePayloadV1.PartitionId);
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

    public static GovernanceSecurityDomainStateV1 CreateEmpty()
        => new(
            Empty<GovernancePolityPayloadV1>(GovernancePolityPayloadV1.PartitionId),
            Empty<GovernanceInstitutionPayloadV1>(GovernanceInstitutionPayloadV1.PartitionId),
            Empty<GovernanceLawRulePayloadV1>(GovernanceLawRulePayloadV1.PartitionId),
            Empty<GovernanceJurisdictionPayloadV1>(GovernanceJurisdictionPayloadV1.PartitionId),
            Empty<GovernanceTerritorialClaimPayloadV1>(GovernanceTerritorialClaimPayloadV1.PartitionId),
            Empty<GovernanceEffectiveControlPayloadV1>(GovernanceEffectiveControlPayloadV1.PartitionId),
            Empty<GovernancePublicAuthorityPayloadV1>(GovernancePublicAuthorityPayloadV1.PartitionId),
            Empty<GovernanceTaxFiscalPayloadV1>(GovernanceTaxFiscalPayloadV1.PartitionId),
            Empty<GovernancePermissionLicensePayloadV1>(GovernancePermissionLicensePayloadV1.PartitionId),
            Empty<GovernanceDiplomacyPayloadV1>(GovernanceDiplomacyPayloadV1.PartitionId),
            Empty<GovernanceSecurityIncidentPayloadV1>(GovernanceSecurityIncidentPayloadV1.PartitionId),
            Empty<GovernanceInvestigationPayloadV1>(GovernanceInvestigationPayloadV1.PartitionId),
            Empty<GovernanceJudicialCasePayloadV1>(GovernanceJudicialCasePayloadV1.PartitionId),
            Empty<GovernanceEnforcementPayloadV1>(GovernanceEnforcementPayloadV1.PartitionId),
            Empty<GovernanceMilitaryAuthorityPayloadV1>(GovernanceMilitaryAuthorityPayloadV1.PartitionId),
            Empty<GovernanceBorderControlPayloadV1>(GovernanceBorderControlPayloadV1.PartitionId),
            Empty<GovernanceLineagePayloadV1>(GovernanceLineagePayloadV1.PartitionId));

    public GovernanceSecurityDomainSnapshotMaterialV1 BindSnapshotMaterial(WorldStateV1 frozenState)
        => GovernanceSecurityDomainSnapshotMaterialV1.Bind(frozenState, this);

    private static DomainPartitionStateV1<TPayload> Empty<TPayload>(string partitionId)
        => new(StandardDomainPartitionRegistry.Get(partitionId), Array.Empty<DomainRecordEnvelopeV1<TPayload>>());

    private static DomainPartitionStateV1<TPayload> RequireIdentity<TPayload>(
        DomainPartitionStateV1<TPayload> partition,
        string partitionId)
    {
        ArgumentNullException.ThrowIfNull(partition);
        if (partition.Identity != StandardDomainPartitionRegistry.Get(partitionId))
            throw new InvalidDataException($"governance.runtime-state.partition-identity:{partitionId}");
        return partition;
    }
}

public sealed class GovernanceSecurityDomainSnapshotMaterialV1
{
    private GovernanceSecurityDomainSnapshotMaterialV1(IEnumerable<IDomainPartitionSnapshotAuthorityV1> authorities)
    {
        var materialized = authorities?.ToArray() ?? throw new ArgumentNullException(nameof(authorities));
        if (materialized.Length != 17)
            throw new InvalidDataException("governance.snapshot-material.authority-count");

        var byId = new Dictionary<string, IDomainPartitionSnapshotAuthorityV1>(StringComparer.Ordinal);
        foreach (var authority in materialized)
        {
            ArgumentNullException.ThrowIfNull(authority);
            authority.VerifyBoundAuthority();
            if (!string.Equals(authority.Identity.OwnerDomain.Value, "governance_security", StringComparison.Ordinal))
                throw new InvalidDataException($"governance.snapshot-material.foreign-owner:{authority.PartitionId.Value}");
            if (!byId.TryAdd(authority.PartitionId.Value, authority))
                throw new InvalidDataException($"governance.snapshot-material.duplicate:{authority.PartitionId.Value}");
        }

        Polity = Require<GovernancePolityPayloadV1>(byId, GovernancePolityPayloadV1.PartitionId);
        Institution = Require<GovernanceInstitutionPayloadV1>(byId, GovernanceInstitutionPayloadV1.PartitionId);
        LawRule = Require<GovernanceLawRulePayloadV1>(byId, GovernanceLawRulePayloadV1.PartitionId);
        Jurisdiction = Require<GovernanceJurisdictionPayloadV1>(byId, GovernanceJurisdictionPayloadV1.PartitionId);
        TerritorialClaim = Require<GovernanceTerritorialClaimPayloadV1>(byId, GovernanceTerritorialClaimPayloadV1.PartitionId);
        EffectiveControl = Require<GovernanceEffectiveControlPayloadV1>(byId, GovernanceEffectiveControlPayloadV1.PartitionId);
        PublicAuthority = Require<GovernancePublicAuthorityPayloadV1>(byId, GovernancePublicAuthorityPayloadV1.PartitionId);
        TaxFiscal = Require<GovernanceTaxFiscalPayloadV1>(byId, GovernanceTaxFiscalPayloadV1.PartitionId);
        PermissionLicense = Require<GovernancePermissionLicensePayloadV1>(byId, GovernancePermissionLicensePayloadV1.PartitionId);
        Diplomacy = Require<GovernanceDiplomacyPayloadV1>(byId, GovernanceDiplomacyPayloadV1.PartitionId);
        SecurityIncident = Require<GovernanceSecurityIncidentPayloadV1>(byId, GovernanceSecurityIncidentPayloadV1.PartitionId);
        Investigation = Require<GovernanceInvestigationPayloadV1>(byId, GovernanceInvestigationPayloadV1.PartitionId);
        JudicialCase = Require<GovernanceJudicialCasePayloadV1>(byId, GovernanceJudicialCasePayloadV1.PartitionId);
        Enforcement = Require<GovernanceEnforcementPayloadV1>(byId, GovernanceEnforcementPayloadV1.PartitionId);
        MilitaryAuthority = Require<GovernanceMilitaryAuthorityPayloadV1>(byId, GovernanceMilitaryAuthorityPayloadV1.PartitionId);
        BorderControl = Require<GovernanceBorderControlPayloadV1>(byId, GovernanceBorderControlPayloadV1.PartitionId);
        Lineage = Require<GovernanceLineagePayloadV1>(byId, GovernanceLineagePayloadV1.PartitionId);
        Authorities = Array.AsReadOnly(materialized.OrderBy(static value => value.PartitionId.Value, StringComparer.Ordinal).ToArray());
    }

    public DomainPartitionSnapshotAuthorityV1<GovernancePolityPayloadV1> Polity { get; }
    public DomainPartitionSnapshotAuthorityV1<GovernanceInstitutionPayloadV1> Institution { get; }
    public DomainPartitionSnapshotAuthorityV1<GovernanceLawRulePayloadV1> LawRule { get; }
    public DomainPartitionSnapshotAuthorityV1<GovernanceJurisdictionPayloadV1> Jurisdiction { get; }
    public DomainPartitionSnapshotAuthorityV1<GovernanceTerritorialClaimPayloadV1> TerritorialClaim { get; }
    public DomainPartitionSnapshotAuthorityV1<GovernanceEffectiveControlPayloadV1> EffectiveControl { get; }
    public DomainPartitionSnapshotAuthorityV1<GovernancePublicAuthorityPayloadV1> PublicAuthority { get; }
    public DomainPartitionSnapshotAuthorityV1<GovernanceTaxFiscalPayloadV1> TaxFiscal { get; }
    public DomainPartitionSnapshotAuthorityV1<GovernancePermissionLicensePayloadV1> PermissionLicense { get; }
    public DomainPartitionSnapshotAuthorityV1<GovernanceDiplomacyPayloadV1> Diplomacy { get; }
    public DomainPartitionSnapshotAuthorityV1<GovernanceSecurityIncidentPayloadV1> SecurityIncident { get; }
    public DomainPartitionSnapshotAuthorityV1<GovernanceInvestigationPayloadV1> Investigation { get; }
    public DomainPartitionSnapshotAuthorityV1<GovernanceJudicialCasePayloadV1> JudicialCase { get; }
    public DomainPartitionSnapshotAuthorityV1<GovernanceEnforcementPayloadV1> Enforcement { get; }
    public DomainPartitionSnapshotAuthorityV1<GovernanceMilitaryAuthorityPayloadV1> MilitaryAuthority { get; }
    public DomainPartitionSnapshotAuthorityV1<GovernanceBorderControlPayloadV1> BorderControl { get; }
    public DomainPartitionSnapshotAuthorityV1<GovernanceLineagePayloadV1> Lineage { get; }
    public IReadOnlyList<IDomainPartitionSnapshotAuthorityV1> Authorities { get; }

    public static GovernanceSecurityDomainSnapshotMaterialV1 Bind(
        WorldStateV1 frozenState,
        GovernanceSecurityDomainStateV1 state)
    {
        ArgumentNullException.ThrowIfNull(frozenState);
        ArgumentNullException.ThrowIfNull(state);
        return new GovernanceSecurityDomainSnapshotMaterialV1(
        [
            Bind(frozenState, state.Polity, GovernancePolityPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.Institution, GovernanceInstitutionPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.LawRule, GovernanceLawRulePayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.Jurisdiction, GovernanceJurisdictionPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.TerritorialClaim, GovernanceTerritorialClaimPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.EffectiveControl, GovernanceEffectiveControlPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.PublicAuthority, GovernancePublicAuthorityPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.TaxFiscal, GovernanceTaxFiscalPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.PermissionLicense, GovernancePermissionLicensePayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.Diplomacy, GovernanceDiplomacyPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.SecurityIncident, GovernanceSecurityIncidentPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.Investigation, GovernanceInvestigationPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.JudicialCase, GovernanceJudicialCasePayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.Enforcement, GovernanceEnforcementPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.MilitaryAuthority, GovernanceMilitaryAuthorityPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.BorderControl, GovernanceBorderControlPayloadV1.PartitionId, static value => value.CanonicalDigest()),
            Bind(frozenState, state.Lineage, GovernanceLineagePayloadV1.PartitionId, static value => value.CanonicalDigest()),
        ]);
    }

    private static DomainPartitionSnapshotAuthorityV1<TPayload> Bind<TPayload>(
        WorldStateV1 frozenState,
        DomainPartitionStateV1<TPayload> partition,
        string partitionId,
        Func<TPayload, byte[]> digest)
    {
        if (partition.Identity != StandardDomainPartitionRegistry.Get(partitionId))
            throw new InvalidDataException($"governance.snapshot-material.partition-identity:{partitionId}");
        return new DomainPartitionSnapshotAuthorityV1<TPayload>(
            partition,
            frozenState.Partitions.Get(partitionId).Header,
            digest);
    }

    private static DomainPartitionSnapshotAuthorityV1<TPayload> Require<TPayload>(
        IReadOnlyDictionary<string, IDomainPartitionSnapshotAuthorityV1> byId,
        string partitionId)
    {
        if (!byId.TryGetValue(partitionId, out var authority))
            throw new InvalidDataException($"governance.snapshot-material.missing:{partitionId}");
        return authority as DomainPartitionSnapshotAuthorityV1<TPayload>
            ?? throw new InvalidDataException($"governance.snapshot-material.payload-type:{partitionId}");
    }
}
