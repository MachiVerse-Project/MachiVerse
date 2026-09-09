using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Performance;

internal static class Qa04CanonicalWorkloadDependencyContractSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04CanonicalWorkloadDependencyContractV1.ValidateCanonicalContract();

        Require(Qa04CanonicalWorkloadDependencyContractV1.Blockers.Count == 3,
            "QA-04 canonical workload dependency count drifted.");

        var expected = new[]
        {
            ("workload.detail-transition.request-binding", Qa04CanonicalWorkloadDependencyKindV1.DetailTransitionBinding,
                "qa04.workload.detail-transition-request-binding-undefined"),
            ("workload.operation.authority-binding", Qa04CanonicalWorkloadDependencyKindV1.OperationAuthorityBinding,
                "qa04.workload.operation-authority-binding-undefined"),
            ("workload.transaction.creation-binding", Qa04CanonicalWorkloadDependencyKindV1.TransactionCreationBinding,
                "qa04.workload.transaction-creation-binding-undefined"),
        };

        var actual = Qa04CanonicalWorkloadDependencyContractV1.Blockers
            .Select(static blocker => (blocker.DependencyId.Value, blocker.Kind, blocker.FailureCode.Value))
            .ToArray();
        Require(actual.SequenceEqual(expected),
            "QA-04 canonical workload dependency identity/kind/failure-code drifted.");

        var worldCodes = Qa04ReferenceWorldDependencyContractV1.FailureCodes
            .Select(static code => code.Value)
            .ToHashSet(StringComparer.Ordinal);
        Require(Qa04CanonicalWorkloadDependencyContractV1.FailureCodes
                .Select(static code => code.Value)
                .All(code => !worldCodes.Contains(code)),
            "QA-04 workload blockers must remain distinct from reference-world blockers.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
