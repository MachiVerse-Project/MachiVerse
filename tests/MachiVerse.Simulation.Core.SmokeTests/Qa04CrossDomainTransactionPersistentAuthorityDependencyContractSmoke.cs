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
        Require(Qa04CanonicalWorkloadDependencyContractV1.Blockers.Count == 3,
            "CrossDomainTransaction persistent authority release must not change workload blockers.");
        Require(Qa04CrossDomainTransactionPersistentAuthorityDependencyContractV1.FailureCodes.Count == 0,
            "Implemented CrossDomainTransaction persistent authority must expose no internal failure codes.");

        var parents = Qa04ReferenceWorldDependencyContractV1.Blockers
            .Where(blocker => blocker.DependencyId.Value == Qa04CrossDomainTransactionPersistentAuthorityDependencyContractV1.ParentWorldDependencyId ||
                              blocker.FailureCode.Value == Qa04CrossDomainTransactionPersistentAuthorityDependencyContractV1.ParentWorldFailureCode)
            .ToArray();
        Require(parents.Length == 0,
            "Implemented CrossDomainTransaction persistent authority must no longer retain a compatibility world blocker.");

        var workload = Qa04CanonicalWorkloadDependencyContractV1.Blockers.SingleOrDefault(
            blocker => blocker.DependencyId.Value == Qa04CrossDomainTransactionPersistentAuthorityDependencyContractV1.WorkloadCreationDependencyId);
        Require(workload is not null &&
                workload.Kind == Qa04CanonicalWorkloadDependencyKindV1.TransactionCreationBinding &&
                workload.FailureCode.Value == Qa04CrossDomainTransactionPersistentAuthorityDependencyContractV1.WorkloadCreationFailureCode,
            "Persistent active-set authority completion must not release workload transaction creation binding.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
