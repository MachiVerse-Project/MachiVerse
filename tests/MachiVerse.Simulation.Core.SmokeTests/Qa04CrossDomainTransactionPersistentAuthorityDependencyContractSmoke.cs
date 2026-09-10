using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Performance;

internal static class Qa04CrossDomainTransactionPersistentAuthorityDependencyContractSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04CrossDomainTransactionPersistentAuthorityDependencyContractV1.ValidateCanonicalContract();

        Require(Qa04CrossDomainTransactionPersistentAuthorityDependencyContractV1.Blockers.Count == 3,
            "QA-04 CrossDomainTransaction persistent authority dependency count drifted.");
        Require(Qa04ReferenceWorldDependencyContractV1.Blockers.Count == 6,
            "CrossDomainTransaction persistent authority subdependencies must not change the reference-world blocker count.");
        Require(Qa04CanonicalWorkloadDependencyContractV1.Blockers.Count == 3,
            "CrossDomainTransaction persistent authority subdependencies must not change the workload blocker count.");

        var expected = new[]
        {
            ("cross-domain-transaction.persistence.detail-guard-binding", Qa04CrossDomainTransactionPersistentAuthorityDependencyKindV1.DetailGuardBinding,
                "qa04.cross-domain-transaction.detail-guard-binding-undefined"),
            ("cross-domain-transaction.persistence.recovery-reconstruction", Qa04CrossDomainTransactionPersistentAuthorityDependencyKindV1.RecoveryReconstruction,
                "qa04.cross-domain-transaction.recovery-reconstruction-undefined"),
            ("cross-domain-transaction.persistence.snapshot-authority", Qa04CrossDomainTransactionPersistentAuthorityDependencyKindV1.SnapshotAuthority,
                "qa04.cross-domain-transaction.snapshot-authority-undefined"),
        };

        var actual = Qa04CrossDomainTransactionPersistentAuthorityDependencyContractV1.Blockers
            .Select(static blocker => (blocker.DependencyId.Value, blocker.Kind, blocker.FailureCode.Value))
            .ToArray();
        Require(actual.SequenceEqual(expected),
            "QA-04 CrossDomainTransaction persistent authority dependency identity/kind/failure-code drifted.");
        Require(Qa04CrossDomainTransactionPersistentAuthorityDependencyContractV1.FailureCodes.All(static code =>
                code.Value is not "qa04.cross-domain-transaction.state-schema-undefined" and
                    not "qa04.cross-domain-transaction.lifecycle-semantics-undefined" and
                    not "qa04.transaction.benchmark-turnover-binding-undefined" and
                    not "qa04.cross-domain-transaction.authority-owner-undefined" and
                    not "qa04.cross-domain-transaction.history-commit-binding-undefined"),
            "Implemented persistent state/lifecycle/turnover/durable-owner/history dependencies must not remain blocked.");

        var parents = Qa04ReferenceWorldDependencyContractV1.Blockers
            .Where(blocker => blocker.DependencyId.Value == Qa04CrossDomainTransactionPersistentAuthorityDependencyContractV1.ParentWorldDependencyId)
            .ToArray();
        Require(parents.Length == 1 &&
                parents[0].Kind == Qa04ReferenceDependencyBlockerKindV1.PersistentAuthority &&
                parents[0].PartitionId is null &&
                parents[0].FieldName is null &&
                parents[0].FailureCode.Value == Qa04CrossDomainTransactionPersistentAuthorityDependencyContractV1.ParentWorldFailureCode,
            "QA-04 CrossDomainTransaction must remain represented by exactly one compatibility world blocker.");

        var workload = Qa04CanonicalWorkloadDependencyContractV1.Blockers.SingleOrDefault(
            blocker => blocker.DependencyId.Value == Qa04CrossDomainTransactionPersistentAuthorityDependencyContractV1.WorkloadCreationDependencyId);
        Require(workload is not null &&
                workload.Kind == Qa04CanonicalWorkloadDependencyKindV1.TransactionCreationBinding &&
                workload.FailureCode.Value == Qa04CrossDomainTransactionPersistentAuthorityDependencyContractV1.WorkloadCreationFailureCode,
            "Persistent active-set authority and workload transaction creation must remain separate blockers.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
