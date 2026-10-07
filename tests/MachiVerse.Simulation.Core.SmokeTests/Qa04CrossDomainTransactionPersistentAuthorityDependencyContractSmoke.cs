using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Performance;

internal static class Qa04CrossDomainTransactionPersistentAuthorityDependencyContractSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04CrossDomainTransactionPersistentAuthorityDependencyContractV1.ValidateCanonicalContract();

        Require(Qa04CrossDomainTransactionPersistentAuthorityDependencyContractV1.Blockers.Count == 0,
            "QA-04 CrossDomainTransaction persistent authority must have no remaining internal blockers.");
        Require(Qa04CanonicalWorkloadDependencyContractV1.Blockers.Count == 0,
            "CrossDomainTransaction completion plus FacilityService authority must leave no canonical workload blockers.");
        Require(Qa04CrossDomainTransactionPersistentAuthorityDependencyContractV1.FailureCodes.Count == 0,
            "Implemented CrossDomainTransaction persistent authority must expose no internal failure codes.");

        var parents = Qa04ReferenceWorldDependencyContractV1.Blockers
            .Where(blocker => blocker.DependencyId.Value == Qa04CrossDomainTransactionPersistentAuthorityDependencyContractV1.ParentWorldDependencyId ||
                              blocker.FailureCode.Value == Qa04CrossDomainTransactionPersistentAuthorityDependencyContractV1.ParentWorldFailureCode)
            .ToArray();
        Require(parents.Length == 0,
            "Implemented CrossDomainTransaction persistent authority must no longer retain a compatibility world blocker.");

        var workload = Qa04CanonicalWorkloadDependencyContractV1.Blockers
            .Where(blocker => blocker.DependencyId.Value == Qa04CrossDomainTransactionPersistentAuthorityDependencyContractV1.WorkloadCreationDependencyId ||
                              blocker.FailureCode.Value == Qa04CrossDomainTransactionPersistentAuthorityDependencyContractV1.WorkloadCreationFailureCode)
            .ToArray();
        Require(workload.Length == 0 &&
                Qa04TransactionCreationDependencyContractV1.Blockers.Count == 0 &&
                Qa04TransactionCreationDependencyContractV1.MissingParticipantAuthorityPartitions.Count == 0,
            "Transaction creation workload blocker must remain released after Participation authority materialization.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
