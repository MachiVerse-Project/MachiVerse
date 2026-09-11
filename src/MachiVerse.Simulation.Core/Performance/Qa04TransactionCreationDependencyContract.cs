using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Runtime;

namespace MachiVerse.Simulation.Core.Performance;

public enum Qa04TransactionCreationDependencyKindV1 : byte
{
    CreationCardinality = 1,
    OtherRegisteredKindAllocation = 2,
    ParticipantAuthorityBinding = 3,
}

public sealed record Qa04TransactionCreationDependencyV1(
    StableToken DependencyId,
    Qa04TransactionCreationDependencyKindV1 Kind,
    StableToken FailureCode);

/// <summary>
/// Machine-readable audit for the remaining perf.reference.v1 transaction-creation binding gaps.
///
/// The benchmark profile fixes the 300-Step cadence and 12-bucket creation mix, and eleven named
/// buckets already map directly to registered production transaction kinds. It does not fix the
/// exact number of creations per cadence, the concrete production kind represented by the 2%
/// "other registered transactions" bucket, or the actual participant partition/effect authority
/// needed by CrossDomainTransactionAssemblerV1. Those values remain fail-closed here rather than
/// being synthesized by the benchmark harness.
/// </summary>
public static class Qa04TransactionCreationDependencyContractV1
{
    public const int CanonicalBenchmarkBucketCount = 12;
    public const int DirectProductionKindMappingCount = 11;

    private static readonly StableToken OtherRegisteredBucket = new("other-registered-transactions");

    private static readonly IReadOnlyList<Qa04TransactionCreationDependencyV1> BlockersValue =
        Array.AsReadOnly(new[]
        {
            Blocker(
                "workload.transaction.creation-cardinality",
                Qa04TransactionCreationDependencyKindV1.CreationCardinality,
                "qa04.workload.tx-creation-cardinality-undefined"),
            Blocker(
                "workload.transaction.other-registered-kind-allocation",
                Qa04TransactionCreationDependencyKindV1.OtherRegisteredKindAllocation,
                "qa04.workload.tx-other-kind-allocation-undefined"),
            Blocker(
                "workload.transaction.participant-authority-binding",
                Qa04TransactionCreationDependencyKindV1.ParticipantAuthorityBinding,
                "qa04.workload.tx-participant-authority-undefined"),
        }
        .OrderBy(static blocker => blocker.DependencyId.Value, StringComparer.Ordinal)
        .ToArray());

    public static IReadOnlyList<Qa04TransactionCreationDependencyV1> Blockers => BlockersValue;

    public static IReadOnlyList<StableToken> DirectlyMappedProductionKinds { get; } = Array.AsReadOnly(
        Qa04ReferenceScenariosV1.TransactionKinds
            .Where(item => item.KindToken != OtherRegisteredBucket)
            .Select(item => new StableToken($"transaction.{item.KindToken.Value}"))
            .OrderBy(static kind => kind.Value, StringComparer.Ordinal)
            .ToArray());

    public static void ValidateCanonicalContract()
    {
        Qa04ReferenceScenariosV1.ValidateCanonicalContract();

        if (Qa04ReferenceScenariosV1.CrossDomainTransactionCreationEverySteps != 300)
            throw new InvalidDataException("qa04.workload.transaction-creation-cadence-drift");
        if (Qa04ReferenceScenariosV1.TransactionKinds.Count != CanonicalBenchmarkBucketCount)
            throw new InvalidDataException("qa04.workload.transaction-bucket-count-drift");
        if (Qa04ReferenceScenariosV1.TransactionKinds.Sum(static item => (int)item.SharePermille) != 1_000)
            throw new InvalidDataException("qa04.workload.transaction-bucket-share-drift");

        var other = Qa04ReferenceScenariosV1.TransactionKinds.SingleOrDefault(item => item.KindToken == OtherRegisteredBucket)
            ?? throw new InvalidDataException("qa04.workload.transaction-other-bucket-missing");
        if (other.SharePermille != 20)
            throw new InvalidDataException("qa04.workload.transaction-other-bucket-share-drift");

        if (DirectlyMappedProductionKinds.Count != DirectProductionKindMappingCount ||
            DirectlyMappedProductionKinds.Distinct().Count() != DirectlyMappedProductionKinds.Count)
            throw new InvalidDataException("qa04.workload.transaction-direct-kind-mapping-count-drift");
        foreach (var productionKind in DirectlyMappedProductionKinds)
        {
            if (!CrossDomainTransactionKindRegistryV1.Contains(productionKind))
                throw new InvalidDataException($"qa04.workload.transaction-kind-unregistered:{productionKind.Value}");
        }
        if (CrossDomainTransactionKindRegistryV1.Contains(new StableToken("transaction.other-registered-transactions")))
            throw new InvalidDataException("qa04.workload.transaction-other-bucket-must-require-explicit-allocation");

        if (BlockersValue.Count != 3 ||
            BlockersValue.Select(static blocker => blocker.DependencyId).Distinct().Count() != 3 ||
            BlockersValue.Select(static blocker => blocker.FailureCode).Distinct().Count() != 3 ||
            BlockersValue.Any(static blocker => !Enum.IsDefined(blocker.Kind)))
            throw new InvalidDataException("qa04.workload.transaction-dependency-contract-drift");

        var ordered = BlockersValue.Select(static blocker => blocker.DependencyId.Value).ToArray();
        if (!ordered.SequenceEqual(ordered.OrderBy(static value => value, StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException("qa04.workload.transaction-dependency-order-drift");
    }

    private static Qa04TransactionCreationDependencyV1 Blocker(
        string dependencyId,
        Qa04TransactionCreationDependencyKindV1 kind,
        string failureCode)
        => new(new StableToken(dependencyId), kind, new StableToken(failureCode));
}
