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
/// Machine-readable audit for the remaining perf.reference.v1 transaction-creation binding gap.
///
/// The canonical workload fixes the initial ACTIVE count at 10,000, terminalizes and replaces
/// exactly 1,000 slots at every nonzero 300-Step cadence boundary, and allocates the initial 200
/// "other registered transactions" by descriptor TransactionId order round-robin across six
/// registered production kinds. Qa04CrossDomainTransactionGenesisMaterializerV1 and
/// Qa04CrossDomainTransactionTurnoverMaterializerV1 already implement those rules through the
/// production assembler/state path. The only remaining creation dependency is actual participant
/// record authority for the canonical owner partitions; missing pools remain fail-closed.
/// </summary>
public static class Qa04TransactionCreationDependencyContractV1
{
    public const int CanonicalBenchmarkBucketCount = 12;
    public const int DirectProductionKindMappingCount = 11;
    public const ulong CanonicalInitialActiveCount = 10_000;
    public const ulong CanonicalReplacementCountPerCadence = 1_000;
    public const int CanonicalOtherInitialCount = 200;

    private static readonly StableToken OtherRegisteredBucket = new("other-registered-transactions");

    private static readonly IReadOnlyList<Qa04TransactionCreationDependencyV1> BlockersValue =
        Array.AsReadOnly(new[]
        {
            Blocker(
                "workload.transaction.participant-authority-binding",
                Qa04TransactionCreationDependencyKindV1.ParticipantAuthorityBinding,
                "qa04.workload.tx-participant-authority-undefined"),
        });

    public static IReadOnlyList<Qa04TransactionCreationDependencyV1> Blockers => BlockersValue;

    public static IReadOnlyList<StableToken> DirectlyMappedProductionKinds { get; } = Array.AsReadOnly(
        Qa04ReferenceScenariosV1.TransactionKinds
            .Where(item => item.KindToken != OtherRegisteredBucket)
            .Select(item => new StableToken($"transaction.{item.KindToken.Value}"))
            .OrderBy(static kind => kind.Value, StringComparer.Ordinal)
            .ToArray());

    public static IReadOnlyList<StableToken> OtherRegisteredProductionKinds { get; } = Array.AsReadOnly(new[]
    {
        new StableToken("transaction.demolition"),
        new StableToken("transaction.birth"),
        new StableToken("transaction.death"),
        new StableToken("transaction.disease-transmission"),
        new StableToken("transaction.public-record"),
        new StableToken("transaction.military-operation"),
    });

    public static void ValidateCanonicalContract()
    {
        Qa04ReferenceScenariosV1.ValidateCanonicalContract();
        Qa04CrossDomainTransactionGenesisMaterializerV1.ValidateCanonicalContract();
        Qa04CrossDomainTransactionTurnoverMaterializerV1.ValidateCanonicalContract();

        if (Qa04ReferenceScenariosV1.CrossDomainTransactionCreationEverySteps != 300)
            throw new InvalidDataException("qa04.workload.transaction-creation-cadence-drift");
        if (Qa04ReferenceScenariosV1.TransactionKinds.Count != CanonicalBenchmarkBucketCount)
            throw new InvalidDataException("qa04.workload.transaction-bucket-count-drift");
        if (Qa04ReferenceScenariosV1.TransactionKinds.Sum(static item => (int)item.SharePermille) != 1_000)
            throw new InvalidDataException("qa04.workload.transaction-bucket-share-drift");
        if (Qa04ReferenceScenariosV1.ActiveCrossDomainTransactionTarget != CanonicalInitialActiveCount ||
            Qa04CrossDomainTransactionGenesisMaterializerV1.CanonicalActiveCount != CanonicalInitialActiveCount)
            throw new InvalidDataException("qa04.workload.transaction-active-count-drift");
        if (Qa04CrossDomainTransactionTurnoverMaterializerV1.TurnoverCadenceSteps != 300 ||
            Qa04CrossDomainTransactionTurnoverMaterializerV1.CohortSize != CanonicalReplacementCountPerCadence ||
            Qa04CrossDomainTransactionTurnoverMaterializerV1.CohortCount != 10 ||
            Qa04CrossDomainTransactionTurnoverMaterializerV1.LifetimeSteps != 3_000)
            throw new InvalidDataException("qa04.workload.transaction-turnover-cardinality-drift");

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

        if (OtherRegisteredProductionKinds.Count != 6 ||
            OtherRegisteredProductionKinds.Distinct().Count() != 6 ||
            OtherRegisteredProductionKinds.Any(static kind => !CrossDomainTransactionKindRegistryV1.Contains(kind)))
            throw new InvalidDataException("qa04.workload.transaction-other-kind-allocation-drift");
        if (CrossDomainTransactionKindRegistryV1.Contains(new StableToken("transaction.other-registered-transactions")))
            throw new InvalidDataException("qa04.workload.transaction-other-bucket-must-not-be-production-kind");

        var otherDescriptors = Enumerable.Range(0, checked((int)CanonicalInitialActiveCount))
            .Select(static slot => Qa04ReferenceScenariosV1.ActiveTransaction(checked((ulong)slot)))
            .Where(descriptor => descriptor.KindToken == OtherRegisteredBucket)
            .OrderBy(static descriptor => descriptor.TransactionId)
            .ToArray();
        if (otherDescriptors.Length != CanonicalOtherInitialCount)
            throw new InvalidDataException("qa04.workload.transaction-other-initial-count-drift");

        var allocationCounts = new int[OtherRegisteredProductionKinds.Count];
        for (var ordinal = 0; ordinal < otherDescriptors.Length; ordinal++)
            allocationCounts[ordinal % OtherRegisteredProductionKinds.Count]++;
        if (!allocationCounts.SequenceEqual(new[] { 34, 34, 33, 33, 33, 33 }))
            throw new InvalidDataException("qa04.workload.transaction-other-round-robin-drift");

        if (BlockersValue.Count != 1 ||
            BlockersValue[0].Kind != Qa04TransactionCreationDependencyKindV1.ParticipantAuthorityBinding ||
            BlockersValue.Select(static blocker => blocker.DependencyId).Distinct().Count() != BlockersValue.Count ||
            BlockersValue.Select(static blocker => blocker.FailureCode).Distinct().Count() != BlockersValue.Count)
            throw new InvalidDataException("qa04.workload.transaction-dependency-contract-drift");
    }

    private static Qa04TransactionCreationDependencyV1 Blocker(
        string dependencyId,
        Qa04TransactionCreationDependencyKindV1 kind,
        string failureCode)
        => new(new StableToken(dependencyId), kind, new StableToken(failureCode));
}
