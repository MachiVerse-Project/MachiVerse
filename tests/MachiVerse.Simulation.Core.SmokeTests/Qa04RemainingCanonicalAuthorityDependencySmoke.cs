using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Performance;

internal static class Qa04RemainingCanonicalAuthorityDependencySmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04SocietyOrganizationDependencyContractV1.ValidateCanonicalContract();
        Require(Qa04SocietyOrganizationDependencyContractV1.Blockers.Count == 1,
            "Canonical society.organization dependency count drifted.");

        Qa04SocietyContractClaimDependencyContractV1.ValidateCanonicalContract();
        Require(Qa04SocietyContractClaimDependencyContractV1.Blockers.Count == 2,
            "Canonical society.contract_claim dependency count drifted.");

        Qa04SocietyInformationClaimDependencyContractV1.ValidateCanonicalContract();
        Require(Qa04SocietyInformationClaimDependencyContractV1.Blockers.Count == 3,
            "Canonical society.information_claim dependency count drifted.");

        Qa04GovernanceInstitutionDependencyContractV1.ValidateCanonicalContract();
        Require(Qa04GovernanceInstitutionDependencyContractV1.Blockers.Count == 3,
            "Canonical governance.institution dependency count drifted.");

        Qa04GovernancePublicAuthorityDependencyContractV1.ValidateCanonicalContract();
        Require(Qa04GovernancePublicAuthorityDependencyContractV1.Blockers.Count == 3,
            "Canonical governance.public_authority dependency count drifted.");

        Qa04GovernancePermissionLicenseDependencyContractV1.ValidateCanonicalContract();
        Require(Qa04GovernancePermissionLicenseDependencyContractV1.Blockers.Count == 3,
            "Canonical governance.permission_license dependency count drifted.");

        Qa04ParticipationControlModeDependencyContractV1.ValidateCanonicalContract();
        Require(Qa04ParticipationControlModeDependencyContractV1.Blockers.Count == 3,
            "Canonical participation.control_mode dependency count drifted.");

        Qa04InfrastructureServiceQueueDependencyContractV1.ValidateCanonicalContract();
        Require(Qa04InfrastructureServiceQueueDependencyContractV1.Blockers.Count == 3,
            "Canonical infrastructure.service_queue dependency count drifted.");

        Qa04DetailRegionAuthorityDependencyContractV1.ValidateCanonicalContract();
        Require(Qa04DetailRegionAuthorityDependencyContractV1.Blockers.Count == 2,
            "Canonical spatial.detail_regions dependency count drifted.");

        var failureCodes = new[]
        {
            Qa04SocietyOrganizationDependencyContractV1.Blockers.Select(static blocker => blocker.FailureCode.Value),
            Qa04SocietyContractClaimDependencyContractV1.Blockers.Select(static blocker => blocker.FailureCode.Value),
            Qa04SocietyInformationClaimDependencyContractV1.Blockers.Select(static blocker => blocker.FailureCode.Value),
            Qa04GovernanceInstitutionDependencyContractV1.Blockers.Select(static blocker => blocker.FailureCode.Value),
            Qa04GovernancePublicAuthorityDependencyContractV1.Blockers.Select(static blocker => blocker.FailureCode.Value),
            Qa04GovernancePermissionLicenseDependencyContractV1.Blockers.Select(static blocker => blocker.FailureCode.Value),
            Qa04ParticipationControlModeDependencyContractV1.Blockers.Select(static blocker => blocker.FailureCode.Value),
            Qa04InfrastructureServiceQueueDependencyContractV1.Blockers.Select(static blocker => blocker.FailureCode.Value),
            Qa04DetailRegionAuthorityDependencyContractV1.Blockers.Select(static blocker => blocker.FailureCode.Value),
        }
        .SelectMany(static codes => codes)
        .ToArray();

        Require(failureCodes.Length == 23,
            "Canonical remaining authority dependency total drifted.");
        Require(failureCodes.Distinct(StringComparer.Ordinal).Count() == failureCodes.Length,
            "Canonical remaining authority failure codes must remain unique.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
