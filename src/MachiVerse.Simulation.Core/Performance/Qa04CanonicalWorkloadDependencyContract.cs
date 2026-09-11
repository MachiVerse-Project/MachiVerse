using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Runtime;

namespace MachiVerse.Simulation.Core.Performance;

public enum Qa04CanonicalWorkloadDependencyKindV1 : byte
{
    OperationAuthorityBinding = 1,
    TransactionCreationBinding = 2,
    DetailTransitionBinding = 3,
}

public sealed record Qa04CanonicalWorkloadDependencyV1(
    StableToken DependencyId,
    Qa04CanonicalWorkloadDependencyKindV1 Kind,
    StableToken FailureCode);

/// <summary>
/// Fail-closed audit of the remaining normative bindings required to turn the already-canonical
/// perf.reference.v1 workload descriptors into production scheduler/domain/detail authority.
///
/// This contract is intentionally separate from Qa04ReferenceWorldDependencyContractV1: the world
/// contract owns initial authoritative material, while this contract owns workload-to-runtime
/// binding. It does not invent SameStepOrderKey fields, domain payloads, transaction participant
/// material, or detail-transition identities that are not fixed by the existing design.
///
/// The parent blockers remain active until each full workload surface is authoritative. The
/// operation binding currently closes four of six families. Transaction creation already has the
/// exact 10,000 ACTIVE genesis set, 200-entry other-kind allocation, and 1,000-per-300-Step
/// production turnover path; its only remaining sub-blocker is actual participant record authority.
/// The detail binding closes the exact cadence/request mapping while still requiring actual canonical
/// DetailRegion authority.
/// </summary>
public static class Qa04CanonicalWorkloadDependencyContractV1
{
    private static readonly IReadOnlyList<Qa04CanonicalWorkloadDependencyV1> BlockersValue = Array.AsReadOnly(new[]
    {
        Blocker(
            "workload.operation.authority-binding",
            Qa04CanonicalWorkloadDependencyKindV1.OperationAuthorityBinding,
            "qa04.workload.operation-authority-binding-undefined"),
        Blocker(
            "workload.transaction.creation-binding",
            Qa04CanonicalWorkloadDependencyKindV1.TransactionCreationBinding,
            "qa04.workload.transaction-creation-binding-undefined"),
        Blocker(
            "workload.detail-transition.request-binding",
            Qa04CanonicalWorkloadDependencyKindV1.DetailTransitionBinding,
            "qa04.workload.detail-transition-request-binding-undefined"),
    }
    .OrderBy(static blocker => blocker.DependencyId.Value, StringComparer.Ordinal)
    .ToArray());

    public static IReadOnlyList<Qa04CanonicalWorkloadDependencyV1> Blockers => BlockersValue;

    public static IReadOnlyList<StableToken> FailureCodes
        => BlockersValue.Select(static blocker => blocker.FailureCode).ToArray();

    public static void ValidateCanonicalContract()
    {
        Qa04ReferenceLoadV1.ValidateCanonicalContract();
        Qa04ReferenceScenariosV1.ValidateCanonicalContract();

        if (BlockersValue.Count != 3)
            throw new InvalidDataException("qa04.workload.dependency-blocker-count-drift");
        if (BlockersValue.Select(static blocker => blocker.DependencyId).Distinct().Count() != BlockersValue.Count)
            throw new InvalidDataException("qa04.workload.dependency-blocker-id-duplicate");
        if (BlockersValue.Select(static blocker => blocker.FailureCode).Distinct().Count() != BlockersValue.Count)
            throw new InvalidDataException("qa04.workload.dependency-blocker-code-duplicate");
        if (BlockersValue.Any(static blocker => !Enum.IsDefined(blocker.Kind)))
            throw new InvalidDataException("qa04.workload.dependency-blocker-kind-invalid");

        var ordered = BlockersValue.Select(static blocker => blocker.DependencyId.Value).ToArray();
        if (!ordered.SequenceEqual(ordered.OrderBy(static value => value, StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException("qa04.workload.dependency-blocker-order");

        ValidateOperationDescriptorBoundary();
        ValidateTransactionKindBoundary();
        ValidateDetailTransitionBoundary();
    }

    private static void ValidateOperationDescriptorBoundary()
    {
        Qa04CanonicalOperationBindingV1.ValidateCanonicalContract();

        var expectedFamilies = new[]
        {
            "participation-control-resident-action",
            "physical-item-movement-work",
            "society-market-payment-contract",
            "infrastructure-service-delivery",
            "governance-security",
            "environment-spatial-admin-synthetic",
        };
        if (!Qa04ReferenceLoadV1.OperationFamilies.Select(static family => family.FamilyToken.Value)
                .SequenceEqual(expectedFamilies, StringComparer.Ordinal))
            throw new InvalidDataException("qa04.workload.operation-family-set-drift");

        var boundFamilies = Qa04CanonicalOperationBindingV1.BoundFamilies
            .Select(static family => family.Value)
            .OrderBy(static family => family, StringComparer.Ordinal)
            .ToArray();
        var expectedBoundFamilies = new[]
        {
            "environment-spatial-admin-synthetic",
            "participation-control-resident-action",
            "physical-item-movement-work",
            "society-market-payment-contract",
        };
        if (!boundFamilies.SequenceEqual(expectedBoundFamilies, StringComparer.Ordinal))
            throw new InvalidDataException("qa04.workload.operation-bound-family-progress-drift");

        var pendingFamilies = Qa04CanonicalOperationBindingV1.PendingAuthorityFamilies
            .Select(static family => family.Value)
            .OrderBy(static family => family, StringComparer.Ordinal)
            .ToArray();
        var expectedPendingFamilies = new[]
        {
            "governance-security",
            "infrastructure-service-delivery",
        };
        if (!pendingFamilies.SequenceEqual(expectedPendingFamilies, StringComparer.Ordinal))
            throw new InvalidDataException("qa04.workload.operation-pending-family-progress-drift");

        var steady = Qa04ReferenceLoadV1.OperationsForStep(1).ToArray();
        if (steady.Length != 5_000 ||
            steady.Any(static descriptor => descriptor.OperationId.IsZero || descriptor.PayloadDigest.Length != 32))
            throw new InvalidDataException("qa04.workload.operation-descriptor-boundary-drift");
    }

    private static void ValidateTransactionKindBoundary()
    {
        Qa04TransactionCreationDependencyContractV1.ValidateCanonicalContract();
        Qa04CanonicalTransactionKindBindingV1.ValidateCanonicalContract();

        if (Qa04TransactionCreationDependencyContractV1.DirectlyMappedProductionKinds.Count != 11 ||
            Qa04CanonicalTransactionKindBindingV1.DirectKindMappings.Count != 11 ||
            Qa04TransactionCreationDependencyContractV1.OtherRegisteredProductionKinds.Count != 6 ||
            Qa04TransactionCreationDependencyContractV1.CanonicalInitialActiveCount != 10_000 ||
            Qa04TransactionCreationDependencyContractV1.CanonicalReplacementCountPerCadence != 1_000 ||
            Qa04TransactionCreationDependencyContractV1.CanonicalOtherInitialCount != 200 ||
            Qa04TransactionCreationDependencyContractV1.Blockers.Count != 1 ||
            Qa04TransactionCreationDependencyContractV1.Blockers[0].Kind !=
                Qa04TransactionCreationDependencyKindV1.ParticipantAuthorityBinding)
            throw new InvalidDataException("qa04.workload.transaction-binding-progress-drift");
    }

    private static void ValidateDetailTransitionBoundary()
    {
        Qa04CanonicalDetailTransitionBindingV1.ValidateCanonicalContract();

        if (Qa04ReferenceScenariosV1.DetailTransitionBatches(299).Count != 0)
            throw new InvalidDataException("qa04.workload.detail-transition-pre-cadence-drift");
        var batches = Qa04ReferenceScenariosV1.DetailTransitionBatches(300);
        if (batches.Count != 2 ||
            batches.Single(static batch => batch.TransitionKind.Value == "promotion").CandidateRecordCount != 30_000 ||
            batches.Single(static batch => batch.TransitionKind.Value == "demotion").CandidateRecordCount != 80_000)
            throw new InvalidDataException("qa04.workload.detail-transition-descriptor-drift");

        if (Qa04CanonicalDetailTransitionBindingV1.CanonicalCadenceCount != 89 ||
            Qa04CanonicalDetailTransitionBindingV1.CanonicalRequestCount != 1_424)
            throw new InvalidDataException("qa04.workload.detail-transition-binding-progress-drift");

        var firstCadence = Qa04CanonicalDetailTransitionBindingV1.RequirementsForStep(300);
        if (firstCadence.Count != 16 ||
            firstCadence.Count(static requirement => requirement.Direction == DetailTransitionDirectionV1.Promotion) != 6 ||
            firstCadence.Count(static requirement => requirement.Direction == DetailTransitionDirectionV1.Demotion) != 10)
            throw new InvalidDataException("qa04.workload.detail-transition-binding-cadence-progress-drift");
    }

    private static Qa04CanonicalWorkloadDependencyV1 Blocker(
        string dependencyId,
        Qa04CanonicalWorkloadDependencyKindV1 kind,
        string failureCode)
        => new(new StableToken(dependencyId), kind, new StableToken(failureCode));
}
