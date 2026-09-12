using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04SocietyGovernanceCanonicalMaterializationSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04SocietyGovernanceCanonicalMaterializerV1.ValidateCanonicalContract();
        var materialization = Qa04SocietyGovernanceCanonicalMaterializerV1.MaterializeCanonical();

        Require(materialization.MaterializedRecordCount == 195_000,
            "Canonical Society/Governance authority proof must materialize exactly 195,000 records.");
        Require(materialization.Organizations.ItemCount == 10_000 &&
                materialization.Institutions.ItemCount == 5_000 &&
                materialization.ContractClaims.ItemCount == 60_000 &&
                materialization.InformationClaims.ItemCount == 25_000 &&
                materialization.PublicAuthorities.ItemCount == 25_000 &&
                materialization.PermissionLicenses.ItemCount == 70_000,
            "Canonical Society/Governance partition cardinality drifted.");

        RequireUnique(materialization.Organizations.RecordsCanonical.Select(static record => record.RecordId), 10_000, "Organization");
        RequireUnique(materialization.Institutions.RecordsCanonical.Select(static record => record.RecordId), 5_000, "Institution");
        RequireUnique(materialization.ContractClaims.RecordsCanonical.Select(static record => record.RecordId), 60_000, "ContractClaim");
        RequireUnique(materialization.InformationClaims.RecordsCanonical.Select(static record => record.RecordId), 25_000, "InformationClaim");
        RequireUnique(materialization.PublicAuthorities.RecordsCanonical.Select(static record => record.RecordId), 25_000, "PublicAuthority");
        RequireUnique(materialization.PermissionLicenses.RecordsCanonical.Select(static record => record.RecordId), 70_000, "PermissionLicense");

        var institutionLast = materialization.Institutions.RecordsCanonical
            .Single(record => record.RecordId == Qa04SocietyGovernanceCanonicalAuthorityV1.ResolveInstitutionRef(4_999).RecordId);
        Require(institutionLast.Payload.InstitutionKind == Qa04SocietyGovernanceCanonicalAuthorityV1.InstitutionKind &&
                institutionLast.Payload.DecisionMethod == Qa04SocietyGovernanceCanonicalAuthorityV1.DecisionMethod &&
                institutionLast.Payload.OfficeRefs.Count == 0,
            "Actual canonical Institution materialization drifted from decided benchmark authority.");

        var publicAuthorityLast = materialization.PublicAuthorities.RecordsCanonical
            .Single(record => record.RecordId == Qa04SocietyGovernanceCanonicalAuthorityV1.ResolvePublicAuthorityRef(24_999).RecordId);
        Require(publicAuthorityLast.Payload.InstitutionRef == Qa04SocietyGovernanceCanonicalAuthorityV1.ResolveInstitutionRef(24_999) &&
                publicAuthorityLast.Payload.AuthorityTokens.SequenceEqual(new[] { Qa04SocietyGovernanceCanonicalAuthorityV1.AuthorityToken }) &&
                publicAuthorityLast.Payload.EffectiveFrom == 0,
            "Actual canonical PublicAuthority materialization drifted from decided benchmark authority.");

        var permissionLast = materialization.PermissionLicenses.RecordsCanonical
            .Single(record => record.Payload.AuthorityRef == Qa04SocietyGovernanceCanonicalAuthorityV1.ResolvePublicAuthorityRef(69_999) &&
                              record.Payload.SubjectRef == Qa04SocietyGovernanceCanonicalAuthorityV1.ResolveResidentRef(69_999));
        Require(permissionLast.Payload.PermissionKind == Qa04SocietyGovernanceCanonicalAuthorityV1.PermissionKind &&
                permissionLast.Payload.EffectiveFrom == 0 && permissionLast.Payload.EffectiveUntil is null &&
                permissionLast.Payload.Status.Value == "active",
            "Actual canonical PermissionLicense materialization drifted from decided benchmark authority.");

        var contractFirst = materialization.ContractClaims.RecordsCanonical.First();
        Require(contractFirst.Payload.ContractKind == Qa04SocietyGovernanceCanonicalAuthorityV1.ContractKind &&
                contractFirst.Payload.PartyRefs.Count == 1,
            "Actual canonical ContractClaim materialization must use one decided actual Resident party.");

        var claimFirst = materialization.InformationClaims.RecordsCanonical.First();
        Require(claimFirst.Payload.ClaimToken == Qa04SocietyGovernanceCanonicalAuthorityV1.ClaimToken &&
                claimFirst.Payload.CreatedStep == 0 && claimFirst.Payload.Status.Value == "active",
            "Actual canonical InformationClaim materialization drifted from decided benchmark authority.");
    }

    private static void RequireUnique(IEnumerable<MachiVerse.Simulation.Core.Determinism.OpaqueId128> ids, int expected, string label)
    {
        var values = ids.ToArray();
        Require(values.Length == expected && values.Distinct().Count() == expected,
            $"{label} canonical materialization must retain unique descriptor RecordIds.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
