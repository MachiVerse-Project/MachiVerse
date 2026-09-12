using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Performance;

internal static class Qa04RemainingCanonicalAuthorityDependencySmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04SocietyOrganizationDependencyContractV1.ValidateCanonicalContract();
        Qa04SocietyContractClaimDependencyContractV1.ValidateCanonicalContract();
        Qa04SocietyInformationClaimDependencyContractV1.ValidateCanonicalContract();
        Qa04GovernanceInstitutionDependencyContractV1.ValidateCanonicalContract();
        Qa04GovernancePublicAuthorityDependencyContractV1.ValidateCanonicalContract();
        Qa04GovernancePermissionLicenseDependencyContractV1.ValidateCanonicalContract();

        Require(Qa04SocietyOrganizationDependencyContractV1.Blockers.Count == 0 &&
                Qa04SocietyContractClaimDependencyContractV1.Blockers.Count == 0 &&
                Qa04SocietyInformationClaimDependencyContractV1.Blockers.Count == 0 &&
                Qa04GovernanceInstitutionDependencyContractV1.Blockers.Count == 0 &&
                Qa04GovernancePublicAuthorityDependencyContractV1.Blockers.Count == 0 &&
                Qa04GovernancePermissionLicenseDependencyContractV1.Blockers.Count == 0,
            "Decided Society/Governance canonical authority dependencies must remain closed.");

        Qa04ParticipationControlModeDependencyContractV1.ValidateCanonicalContract();
        Require(Qa04ParticipationControlModeDependencyContractV1.Blockers.Count == 3,
            "Canonical participation.control_mode dependency count drifted.");

        Qa04InfrastructureServiceQueueDependencyContractV1.ValidateCanonicalContract();
        Require(Qa04InfrastructureServiceQueueDependencyContractV1.Blockers.Count == 0,
            "Canonical infrastructure.service_queue authority dependencies must remain closed.");

        Qa04DetailRegionAuthorityDependencyContractV1.ValidateCanonicalContract();
        Require(Qa04DetailRegionAuthorityDependencyContractV1.Blockers.Count == 2,
            "Canonical spatial.detail_regions dependency count drifted.");

        var failureCodes = new[]
        {
            Qa04ParticipationControlModeDependencyContractV1.Blockers.Select(static blocker => blocker.FailureCode.Value),
            Qa04InfrastructureServiceQueueDependencyContractV1.Blockers.Select(static blocker => blocker.FailureCode.Value),
            Qa04DetailRegionAuthorityDependencyContractV1.Blockers.Select(static blocker => blocker.FailureCode.Value),
        }
        .SelectMany(static codes => codes)
        .ToArray();

        Require(failureCodes.Length == 5,
            "Canonical remaining authority dependency total must be five.");
        Require(failureCodes.Distinct(StringComparer.Ordinal).Count() == failureCodes.Length,
            "Canonical remaining authority failure codes must remain unique.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
