using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Runtime;

namespace MachiVerse.Simulation.Core.Performance;

public enum Qa04CrossDomainTransactionPersistentAuthorityDependencyKindV1 : byte
{
    AuthorityOwner = 1,
    StateSchema = 2,
    LifecycleSemantics = 3,
    SnapshotAuthority = 4,
    HistoryCommitBinding = 5,
    RecoveryReconstruction = 6,
    DetailGuardBinding = 7,
    BenchmarkTurnoverBinding = 8,
}

public sealed record Qa04CrossDomainTransactionPersistentAuthorityDependencyV1(
    StableToken DependencyId,
    Qa04CrossDomainTransactionPersistentAuthorityDependencyKindV1 Kind,
    StableToken FailureCode);

/// <summary>
/// Cross-domain transaction persistent authority is fully implemented: logical state/lifecycle,
/// benchmark genesis/turnover, SQLite durable ownership, transition-history atomic commit binding,
/// canonical Snapshot /2.0 authority, physical recovery, and TileScope-backed detail-guard binding.
/// Workload transaction creation remains a separate canonical workload dependency.
/// </summary>
public static class Qa04CrossDomainTransactionPersistentAuthorityDependencyContractV1
{
    public const string ParentWorldDependencyId = "transaction.active-cross-domain.persistent-authority";
    public const string ParentWorldFailureCode = "qa04.material.cross-domain-transaction-authority-undefined";
    public const string WorkloadCreationDependencyId = "workload.transaction.creation-binding";
    public const string WorkloadCreationFailureCode = "qa04.workload.transaction-creation-binding-undefined";

    private static readonly IReadOnlyList<Qa04CrossDomainTransactionPersistentAuthorityDependencyV1> BlockersValue =
        Array.Empty<Qa04CrossDomainTransactionPersistentAuthorityDependencyV1>();

    public static IReadOnlyList<Qa04CrossDomainTransactionPersistentAuthorityDependencyV1> Blockers => BlockersValue;

    public static IReadOnlyList<StableToken> FailureCodes
        => BlockersValue.Select(static blocker => blocker.FailureCode).ToArray();

    public static void ValidateCanonicalContract()
    {
        Qa04ReferenceWorldDependencyContractV1.ValidateCanonicalContract();
        Qa04CanonicalWorkloadDependencyContractV1.ValidateCanonicalContract();
        Qa04ReferenceScenariosV1.ValidateCanonicalContract();

        if (BlockersValue.Count != 0)
            throw new InvalidDataException("qa04.cross-domain-transaction.dependency-blocker-count-drift");

        ValidateEstablishedRuntimeBoundary();
        ValidateImplementedPersistentStateBoundary();
        ValidateImplementedBenchmarkTurnoverBoundary();
        ValidateImplementedDurableOwnerAndHistoryBoundary();
        ValidateImplementedSnapshotAndRecoveryBoundary();
        ValidateImplementedDetailGuardBoundary();
        ValidateParentWorldBlockerReleased();
        ValidateSeparateWorkloadBlocker();
    }

    private static void ValidateEstablishedRuntimeBoundary()
    {
        if (CrossDomainTransactionKindRegistryV1.StandardKindCount != 17 ||
            CrossDomainTransactionKindRegistryV1.Entries.Count != 17)
            throw new InvalidDataException("qa04.cross-domain-transaction.kind-registry-drift");
        if (Qa04ReferenceScenariosV1.ActiveCrossDomainTransactionTarget != 10_000 ||
            Qa04ReferenceScenariosV1.CrossDomainTransactionCreationEverySteps != 300)
            throw new InvalidDataException("qa04.cross-domain-transaction.reference-load-drift");
        if (DetailTransitionGuardV1.ActiveTransaction.Value != "detail.guard.active-transaction")
            throw new InvalidDataException("qa04.cross-domain-transaction.detail-guard-token-drift");

        var first = Qa04ReferenceScenariosV1.ActiveTransaction(0);
        if (first.TransactionId.IsZero || first.SubjectIds.Count != 2 || first.SubjectIds.Any(static id => id.IsZero))
            throw new InvalidDataException("qa04.cross-domain-transaction.reference-descriptor-drift");
    }

    private static void ValidateImplementedPersistentStateBoundary()
    {
        if (!Enum.IsDefined(TransactionLifecycleV1.Active) ||
            !Enum.IsDefined(TransactionLifecycleV1.Committed) ||
            !Enum.IsDefined(TransactionLifecycleV1.Aborted))
            throw new InvalidDataException("qa04.cross-domain-transaction.lifecycle-state-drift");
        if (FailureCodes.Any(static code =>
                code.Value is "qa04.cross-domain-transaction.state-schema-undefined" or
                    "qa04.cross-domain-transaction.lifecycle-semantics-undefined"))
            throw new InvalidDataException("qa04.cross-domain-transaction.implemented-state-dependency-retained");
    }

    private static void ValidateImplementedBenchmarkTurnoverBoundary()
    {
        if (Qa04CrossDomainTransactionGenesisMaterializerV1.CanonicalActiveCount != 10_000 ||
            Qa04CrossDomainTransactionTurnoverMaterializerV1.CohortSize != 1_000 ||
            Qa04CrossDomainTransactionTurnoverMaterializerV1.TurnoverCadenceSteps != 300 ||
            Qa04CrossDomainTransactionTurnoverMaterializerV1.LifetimeSteps != 3_000)
            throw new InvalidDataException("qa04.cross-domain-transaction.turnover-contract-drift");
        if (FailureCodes.Any(static code => code.Value == "qa04.transaction.benchmark-turnover-binding-undefined"))
            throw new InvalidDataException("qa04.cross-domain-transaction.implemented-turnover-dependency-retained");
    }

    private static void ValidateImplementedDurableOwnerAndHistoryBoundary()
    {
        if (FailureCodes.Any(static code => code.Value is
                "qa04.cross-domain-transaction.authority-owner-undefined" or
                "qa04.cross-domain-transaction.history-commit-binding-undefined"))
            throw new InvalidDataException("qa04.cross-domain-transaction.implemented-durable-dependency-retained");
    }

    private static void ValidateImplementedSnapshotAndRecoveryBoundary()
    {
        if (FailureCodes.Any(static code => code.Value is
                "qa04.cross-domain-transaction.snapshot-authority-undefined" or
                "qa04.cross-domain-transaction.recovery-reconstruction-undefined"))
            throw new InvalidDataException("qa04.cross-domain-transaction.implemented-snapshot-recovery-dependency-retained");
    }

    private static void ValidateImplementedDetailGuardBoundary()
    {
        if (FailureCodes.Any(static code => code.Value == "qa04.cross-domain-transaction.detail-guard-binding-undefined"))
            throw new InvalidDataException("qa04.cross-domain-transaction.implemented-detail-guard-dependency-retained");
    }

    private static void ValidateParentWorldBlockerReleased()
    {
        if (Qa04ReferenceWorldDependencyContractV1.Blockers.Any(static blocker =>
                blocker.DependencyId.Value == ParentWorldDependencyId ||
                blocker.FailureCode.Value == ParentWorldFailureCode))
            throw new InvalidDataException("qa04.cross-domain-transaction.parent-world-blocker-retained");
    }

    private static void ValidateSeparateWorkloadBlocker()
    {
        var workload = Qa04CanonicalWorkloadDependencyContractV1.Blockers.SingleOrDefault(
            static blocker => blocker.DependencyId.Value == WorkloadCreationDependencyId)
            ?? throw new InvalidDataException("qa04.cross-domain-transaction.workload-blocker-missing");
        if (workload.Kind != Qa04CanonicalWorkloadDependencyKindV1.TransactionCreationBinding ||
            workload.FailureCode.Value != WorkloadCreationFailureCode)
            throw new InvalidDataException("qa04.cross-domain-transaction.workload-blocker-drift");
        if (FailureCodes.Any(code => code.Value == WorkloadCreationFailureCode))
            throw new InvalidDataException("qa04.cross-domain-transaction.persistence-workload-code-collision");
    }
}
