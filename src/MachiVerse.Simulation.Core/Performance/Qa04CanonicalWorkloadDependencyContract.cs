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
/// Fail-closed audit of the normative bindings required to turn canonical perf.reference.v1
/// workload descriptors into production scheduler/domain/detail authority. All three binding
/// surfaces are now authority-complete: all six Operation families, transaction creation/turnover,
/// and all canonical DetailTransition requests.
/// </summary>
public static class Qa04CanonicalWorkloadDependencyContractV1
{
    private static readonly IReadOnlyList<Qa04CanonicalWorkloadDependencyV1> BlockersValue =
        Array.AsReadOnly(Array.Empty<Qa04CanonicalWorkloadDependencyV1>());

    public static IReadOnlyList<Qa04CanonicalWorkloadDependencyV1> Blockers => BlockersValue;

    public static IReadOnlyList<StableToken> FailureCodes
        => BlockersValue.Select(static blocker => blocker.FailureCode).ToArray();

    public static void ValidateCanonicalContract()
    {
        Qa04ReferenceLoadV1.ValidateCanonicalContract();
        Qa04ReferenceScenariosV1.ValidateCanonicalContract();

        if (BlockersValue.Count != 0)
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
            "governance-security",
            "infrastructure-service-delivery",
            "participation-control-resident-action",
            "physical-item-movement-work",
            "society-market-payment-contract",
        };
        if (!boundFamilies.SequenceEqual(expectedBoundFamilies, StringComparer.Ordinal))
            throw new InvalidDataException("qa04.workload.operation-bound-family-progress-drift");

        if (Qa04CanonicalOperationBindingV1.PendingAuthorityFamilies.Count != 0)
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
            Qa04TransactionCreationDependencyContractV1.Blockers.Count != 0 ||
            Qa04TransactionCreationDependencyContractV1.MissingParticipantAuthorityPartitions.Count != 0 ||
            Qa04TransactionCreationDependencyContractV1.AvailableParticipantAuthorityPartitions.Count != 8)
            throw new InvalidDataException("qa04.workload.transaction-binding-progress-drift");
    }

    private static void ValidateDetailTransitionBoundary()
    {
        Qa04CanonicalDetailTransitionBindingV1.ValidateCanonicalContract();
        Qa04DetailRegionAuthorityDependencyContractV1.ValidateCanonicalContract();

        if (Qa04ReferenceScenariosV1.DetailTransitionBatches(299).Count != 0)
            throw new InvalidDataException("qa04.workload.detail-transition-pre-cadence-drift");
        var batches = Qa04ReferenceScenariosV1.DetailTransitionBatches(300);
        if (batches.Count != 2 ||
            batches.Single(static batch => batch.TransitionKind.Value == "promotion").CandidateRecordCount != 30_000 ||
            batches.Single(static batch => batch.TransitionKind.Value == "demotion").CandidateRecordCount != 80_000)
            throw new InvalidDataException("qa04.workload.detail-transition-descriptor-drift");

        if (Qa04CanonicalDetailTransitionBindingV1.CanonicalCadenceCount != 89 ||
            Qa04CanonicalDetailTransitionBindingV1.CanonicalRequestCount != 1_424 ||
            Qa04DetailRegionAuthorityDependencyContractV1.CanonicalTileRegionCount != 4_096 ||
            Qa04DetailRegionAuthorityDependencyContractV1.CanonicalRequestedOverrideCount != 1_424 ||
            Qa04DetailRegionAuthorityDependencyContractV1.Blockers.Count != 0)
            throw new InvalidDataException("qa04.workload.detail-transition-binding-progress-drift");

        var firstCadence = Qa04CanonicalDetailTransitionBindingV1.RequirementsForStep(300);
        if (firstCadence.Count != 16 ||
            firstCadence.Count(static requirement => requirement.Direction == DetailTransitionDirectionV1.Promotion) != 6 ||
            firstCadence.Count(static requirement => requirement.Direction == DetailTransitionDirectionV1.Demotion) != 10)
            throw new InvalidDataException("qa04.workload.detail-transition-binding-cadence-progress-drift");
    }
}
