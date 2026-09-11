using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Runtime;

internal static class Qa04CanonicalWorkloadDependencyContractSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04CanonicalWorkloadDependencyContractV1.ValidateCanonicalContract();
        Qa04CanonicalTransactionKindBindingV1.ValidateCanonicalContract();

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

        Require(Qa04CanonicalTransactionKindBindingV1.DirectKindMappings.Count == 11,
            "QA-04 transaction direct production-kind mapping count drifted.");
        foreach (var transactionKind in Qa04ReferenceScenariosV1.TransactionKinds
                     .Where(static item => item.KindToken.Value != "other-registered-transactions"))
        {
            var productionKind = Qa04CanonicalTransactionKindBindingV1.ResolveProductionKind(transactionKind.KindToken);
            Require(productionKind.Value == $"transaction.{transactionKind.KindToken.Value}" &&
                    CrossDomainTransactionKindRegistryV1.Contains(productionKind),
                "QA-04 known transaction bucket must map to its registered production kind.");
        }

        var source = Qa04ReferenceScenariosV1.ActiveTransaction(0);
        var root = new CausalityRefV1(
            CausalityRefKindV1.Entity,
            source.SubjectIds[0].ToBytes(),
            0);
        var binding = Qa04CanonicalTransactionKindBindingV1.BindKnownDescriptor(
            source,
            basisStep: 300,
            root,
            stableLocalOrdinal: source.Ordinal,
            Array.Empty<TransactionParticipantCandidateV1>());
        Require(binding.ProductionKind.Value == "transaction.market-sale-delivery" &&
                binding.Candidate.TransactionKind == binding.ProductionKind &&
                binding.Candidate.BasisStep == 300 &&
                binding.Candidate.SubjectRefs.SequenceEqual(source.SubjectIds) &&
                binding.Candidate.Status == TransactionCandidateStatusV1.Invalid &&
                binding.Candidate.FailureCode?.Value == "transaction.participant-missing" &&
                !binding.Candidate.IsAuthoritative,
            "QA-04 known transaction bucket must enter the ordinary production assembler and fail closed without participant authority.");

        var other = Qa04ReferenceScenariosV1.ActiveTransaction(9_999);
        Require(other.KindToken.Value == "other-registered-transactions",
            "QA-04 transaction other-bucket selection boundary drifted.");
        RequireThrows<InvalidDataException>(
            () => Qa04CanonicalTransactionKindBindingV1.ResolveProductionKind(other.KindToken),
            "qa04.workload.tx-other-kind-allocation-undefined",
            "QA-04 other transaction bucket must remain fail-closed until production allocation is defined.");
    }

    private static void RequireThrows<T>(Action action, string expectedMessage, string failureMessage)
        where T : Exception
    {
        try
        {
            action();
        }
        catch (T ex) when (ex.Message == expectedMessage)
        {
            return;
        }
        throw new InvalidOperationException(failureMessage);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
