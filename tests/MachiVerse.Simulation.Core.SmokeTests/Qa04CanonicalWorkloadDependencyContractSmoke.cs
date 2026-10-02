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

        Require(Qa04CanonicalWorkloadDependencyContractV1.Blockers.Count == 0,
            "QA-04 canonical workload dependency blockers must be fully closed.");
        Require(Qa04CanonicalWorkloadDependencyContractV1.FailureCodes.Count == 0,
            "QA-04 canonical workload dependency failure-code surface must be empty after closure.");
        Require(Qa04CanonicalOperationBindingV1.BoundFamilies.Count == 6 &&
                Qa04CanonicalOperationBindingV1.PendingAuthorityFamilies.Count == 0,
            "QA-04 canonical workload must bind all six Operation families.");

        var worldCodes = Qa04ReferenceWorldDependencyContractV1.FailureCodes
            .Select(static code => code.Value)
            .ToHashSet(StringComparer.Ordinal);
        Require(Qa04CanonicalWorkloadDependencyContractV1.FailureCodes
                .Select(static code => code.Value)
                .All(code => !worldCodes.Contains(code)),
            "QA-04 workload blockers must remain distinct from reference-world blockers.");

        Require(Qa04TransactionCreationDependencyContractV1.Blockers.Count == 0 &&
                Qa04TransactionCreationDependencyContractV1.MissingParticipantAuthorityPartitions.Count == 0 &&
                Qa04TransactionCreationDependencyContractV1.AvailableParticipantAuthorityPartitions.Count == 8,
            "QA-04 transaction creation must remain fully authority-bound.");

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
            descriptor: source,
            basisStep: 300,
            rootCausalityRef: root,
            stableLocalOrdinal: source.Ordinal,
            participants: Array.Empty<TransactionParticipantCandidateV1>());
        Require(binding.ProductionKind.Value == "transaction.market-sale-delivery" &&
                binding.Candidate.TransactionKind == binding.ProductionKind &&
                binding.Candidate.BasisStep == 300 &&
                binding.Candidate.SubjectRefs.SequenceEqual(source.SubjectIds.Order()) &&
                binding.Candidate.Status == TransactionCandidateStatusV1.Invalid &&
                binding.Candidate.FailureCode?.Value == "transaction.participant-missing" &&
                !binding.Candidate.IsAuthoritative,
            "QA-04 known transaction bucket must enter the ordinary production assembler and fail closed without participant authority supplied to that call.");

        var other = Qa04ReferenceScenariosV1.ActiveTransaction(9_999);
        Require(other.KindToken.Value == "other-registered-transactions",
            "QA-04 transaction other-bucket selection boundary drifted.");
        RequireThrows<InvalidDataException>(
            () => Qa04CanonicalTransactionKindBindingV1.ResolveProductionKind(other.KindToken),
            "qa04.workload.tx-other-kind-allocation-undefined",
            "QA-04 generic other-bucket resolver must remain fail-closed; canonical allocation is owned by transaction materialization.");
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
