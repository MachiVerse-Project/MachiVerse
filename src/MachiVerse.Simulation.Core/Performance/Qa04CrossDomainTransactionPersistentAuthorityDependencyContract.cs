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
/// perf.reference.v1 の steady active CrossDomainTransaction 10,000件を、Step を跨ぐ
/// authoritative state として保持・Snapshot・recovery するために残っている正本依存を列挙する。
///
/// CrossDomainTransactionCandidateV1 は StepCandidate 内の非 authoritative candidate であり、
/// valid candidate や transition commit の存在だけを persistent active-set authority とみなしてはならない。
/// この契約は Qa04ReferenceWorldDependencyContractV1 の PersistentAuthority blocker 1件の
/// 下位診断契約であり、reference-world blocker 数や互換 failure code は変更しない。
/// workload 側の transaction creation binding とも別契約として扱う。
/// </summary>
public static class Qa04CrossDomainTransactionPersistentAuthorityDependencyContractV1
{
    public const string ParentWorldDependencyId = "transaction.active-cross-domain.persistent-authority";
    public const string ParentWorldFailureCode = "qa04.material.cross-domain-transaction-authority-undefined";
    public const string WorkloadCreationDependencyId = "workload.transaction.creation-binding";
    public const string WorkloadCreationFailureCode = "qa04.workload.transaction-creation-binding-undefined";

    private static readonly IReadOnlyList<Qa04CrossDomainTransactionPersistentAuthorityDependencyV1> BlockersValue = Array.AsReadOnly(new[]
    {
        Blocker(
            "cross-domain-transaction.persistence.authority-owner",
            Qa04CrossDomainTransactionPersistentAuthorityDependencyKindV1.AuthorityOwner,
            "qa04.cross-domain-transaction.authority-owner-undefined"),
        Blocker(
            "cross-domain-transaction.persistence.benchmark-turnover-binding",
            Qa04CrossDomainTransactionPersistentAuthorityDependencyKindV1.BenchmarkTurnoverBinding,
            "qa04.cross-domain-transaction.benchmark-turnover-binding-undefined"),
        Blocker(
            "cross-domain-transaction.persistence.detail-guard-binding",
            Qa04CrossDomainTransactionPersistentAuthorityDependencyKindV1.DetailGuardBinding,
            "qa04.cross-domain-transaction.detail-guard-binding-undefined"),
        Blocker(
            "cross-domain-transaction.persistence.history-commit-binding",
            Qa04CrossDomainTransactionPersistentAuthorityDependencyKindV1.HistoryCommitBinding,
            "qa04.cross-domain-transaction.history-commit-binding-undefined"),
        Blocker(
            "cross-domain-transaction.persistence.lifecycle-semantics",
            Qa04CrossDomainTransactionPersistentAuthorityDependencyKindV1.LifecycleSemantics,
            "qa04.cross-domain-transaction.lifecycle-semantics-undefined"),
        Blocker(
            "cross-domain-transaction.persistence.recovery-reconstruction",
            Qa04CrossDomainTransactionPersistentAuthorityDependencyKindV1.RecoveryReconstruction,
            "qa04.cross-domain-transaction.recovery-reconstruction-undefined"),
        Blocker(
            "cross-domain-transaction.persistence.snapshot-authority",
            Qa04CrossDomainTransactionPersistentAuthorityDependencyKindV1.SnapshotAuthority,
            "qa04.cross-domain-transaction.snapshot-authority-undefined"),
        Blocker(
            "cross-domain-transaction.persistence.state-schema",
            Qa04CrossDomainTransactionPersistentAuthorityDependencyKindV1.StateSchema,
            "qa04.cross-domain-transaction.state-schema-undefined"),
    }
    .OrderBy(static blocker => blocker.DependencyId.Value, StringComparer.Ordinal)
    .ToArray());

    public static IReadOnlyList<Qa04CrossDomainTransactionPersistentAuthorityDependencyV1> Blockers => BlockersValue;

    public static IReadOnlyList<StableToken> FailureCodes
        => BlockersValue.Select(static blocker => blocker.FailureCode).ToArray();

    public static void ValidateCanonicalContract()
    {
        Qa04ReferenceWorldDependencyContractV1.ValidateCanonicalContract();
        Qa04CanonicalWorkloadDependencyContractV1.ValidateCanonicalContract();
        Qa04ReferenceScenariosV1.ValidateCanonicalContract();

        if (BlockersValue.Count != 8)
            throw new InvalidDataException("qa04.cross-domain-transaction.dependency-blocker-count-drift");
        if (BlockersValue.Select(static blocker => blocker.DependencyId).Distinct().Count() != BlockersValue.Count)
            throw new InvalidDataException("qa04.cross-domain-transaction.dependency-blocker-id-duplicate");
        if (BlockersValue.Select(static blocker => blocker.FailureCode).Distinct().Count() != BlockersValue.Count)
            throw new InvalidDataException("qa04.cross-domain-transaction.dependency-blocker-code-duplicate");
        if (BlockersValue.Any(static blocker => !Enum.IsDefined(blocker.Kind)))
            throw new InvalidDataException("qa04.cross-domain-transaction.dependency-blocker-kind-invalid");

        var ordered = BlockersValue.Select(static blocker => blocker.DependencyId.Value).ToArray();
        if (!ordered.SequenceEqual(ordered.OrderBy(static value => value, StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException("qa04.cross-domain-transaction.dependency-blocker-order");

        ValidateEstablishedRuntimeBoundary();
        ValidateParentWorldBlocker();
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

    private static void ValidateParentWorldBlocker()
    {
        var parent = Qa04ReferenceWorldDependencyContractV1.Blockers.SingleOrDefault(
            static blocker => blocker.DependencyId.Value == ParentWorldDependencyId)
            ?? throw new InvalidDataException("qa04.cross-domain-transaction.parent-world-blocker-missing");

        if (parent.Kind != Qa04ReferenceDependencyBlockerKindV1.PersistentAuthority ||
            parent.PartitionId is not null ||
            parent.FieldName is not null ||
            parent.FailureCode.Value != ParentWorldFailureCode)
            throw new InvalidDataException("qa04.cross-domain-transaction.parent-world-blocker-drift");

        var worldCodes = Qa04ReferenceWorldDependencyContractV1.FailureCodes
            .Select(static code => code.Value)
            .ToHashSet(StringComparer.Ordinal);
        if (FailureCodes.Any(code => worldCodes.Contains(code.Value)))
            throw new InvalidDataException("qa04.cross-domain-transaction.subdependency-code-collides-with-world-blocker");
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

    private static Qa04CrossDomainTransactionPersistentAuthorityDependencyV1 Blocker(
        string dependencyId,
        Qa04CrossDomainTransactionPersistentAuthorityDependencyKindV1 kind,
        string failureCode)
        => new(new StableToken(dependencyId), kind, new StableToken(failureCode));
}
