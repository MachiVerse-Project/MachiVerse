using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Domains;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04CanonicalOperationPostCommitVerificationV1(
    ulong ResultingStep,
    int PartitionCount,
    int ChangedPartitionCount,
    int TerminalOperationCount,
    byte[] StateDigest);

/// <summary>
/// Gate-2 Steps 9-12 post-COMMIT authority verifier. It does not create authority: it only accepts
/// the already-published State(S+1) when its full partition diagnostic, the six typed mutation
/// results, the scheduler substate, and the complete durable Operation catalog all agree with the
/// committed SQLite/runtime authority. When persistent CrossDomainTransaction state is supplied,
/// the Operation core substate is verified through the canonical core.operation-state /2.0
/// authority rather than the operation-only v1 authority. Any drift fails closed.
/// </summary>
public static class Qa04CanonicalOperationPostCommitVerifierV1
{
    public static Qa04CanonicalOperationPostCommitVerificationV1 Verify(
        Qa04CanonicalOperationStepPreparationResultV1 step5Preparation,
        PreparedStepWorldStateV1 finalPrepared,
        DurableStepReceiptV1 receipt,
        WorldStateV1 publishedState,
        OperationSchedulerStateV1 scheduler,
        IReadOnlyList<DurableOperationStateV1> durableOperationCatalog,
        IReadOnlyCollection<CrossDomainTransactionStateV1>? crossDomainTransactions = null)
    {
        ArgumentNullException.ThrowIfNull(step5Preparation);
        ArgumentNullException.ThrowIfNull(finalPrepared);
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(publishedState);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(durableOperationCatalog);

        var step5Candidate = step5Preparation.Candidate;
        if (!receipt.IsPublishable ||
            receipt.CandidateId != finalPrepared.CandidateId ||
            receipt.BasisStep != finalPrepared.BasisStep ||
            receipt.ResultingStep != finalPrepared.TargetStep ||
            publishedState.Header.WorldId != step5Candidate.WorldId ||
            publishedState.Header.Step != receipt.ResultingStep)
        {
            throw new InvalidDataException("qa04.full-step.post-commit-step-authority-drift");
        }

        if (!CryptographicOperations.FixedTimeEquals(
                finalPrepared.ResultingState.Diagnostic.StateDigest,
                publishedState.Diagnostic.StateDigest))
            throw new InvalidDataException("qa04.full-step.post-commit-state-digest-drift");

        var partitions = publishedState.Partitions.CanonicalEntries.ToArray();
        if (partitions.Length != StandardDomainPartitionRegistry.StandardPartitionCount)
            throw new InvalidDataException("qa04.full-step.post-commit-partition-count-drift");
        var diagnosticByPartition = publishedState.Diagnostic.PartitionDigests
            .ToDictionary(static item => item.Key, static item => item.Value, StringComparer.Ordinal);
        if (diagnosticByPartition.Count != partitions.Length)
            throw new InvalidDataException("qa04.full-step.post-commit-partition-diagnostic-coverage-drift");
        foreach (var partition in partitions)
        {
            if (!diagnosticByPartition.TryGetValue(partition.Header.PartitionId.Value, out var diagnostic) ||
                !CryptographicOperations.FixedTimeEquals(diagnostic, partition.Header.CanonicalDigest))
                throw new InvalidDataException($"qa04.full-step.post-commit-partition-digest-drift:{partition.Header.PartitionId.Value}");
        }

        if (step5Candidate.PartitionCandidates.Count != 6)
            throw new InvalidDataException("qa04.full-step.post-commit-changed-partition-count-drift");
        foreach (var candidate in step5Candidate.PartitionCandidates)
        {
            var typedResult = step5Preparation.PreparedState.ResultingState.Partitions.Get(candidate.PartitionId.Value).Header;
            var published = publishedState.Partitions.Get(candidate.PartitionId.Value).Header;
            if (published.PartitionId != typedResult.PartitionId ||
                published.OwnerDomain != typedResult.OwnerDomain ||
                published.Revision != typedResult.Revision ||
                published.BasisStep != typedResult.BasisStep ||
                published.ItemCount != typedResult.ItemCount ||
                published.DigestAlgorithm != typedResult.DigestAlgorithm ||
                !CryptographicOperations.FixedTimeEquals(published.CanonicalDigest, typedResult.CanonicalDigest))
            {
                throw new InvalidDataException($"qa04.full-step.post-commit-typed-partition-drift:{candidate.PartitionId.Value}");
            }
        }

        var schedulerAuthority = OperationSchedulerSubstateV1.Canonicalize(scheduler, publishedState.Header.Step);
        RequireSubstateMatch(
            schedulerAuthority,
            publishedState.SchedulerState,
            "qa04.full-step.post-commit-scheduler-substate-drift");

        var operationAuthority = crossDomainTransactions is null
            ? DurableOperationSubstateV1.Canonicalize(durableOperationCatalog)
            : CoreOperationStateSubstateV2.Canonicalize(
                durableOperationCatalog,
                crossDomainTransactions,
                publishedState.Header.Step);
        RequireSubstateMatch(
            operationAuthority,
            publishedState.OperationState,
            "qa04.full-step.post-commit-operation-substate-drift");

        var durableById = durableOperationCatalog.ToDictionary(static operation => operation.OperationId);
        foreach (var scheduled in step5Candidate.FrozenInput.ScheduledOperations)
        {
            if (!durableById.TryGetValue(scheduled.OperationId, out var durable) ||
                durable.Lifecycle != DurableOperationLifecycleV1.TerminalDurable ||
                durable.EffectiveStep != step5Candidate.BasisStep ||
                durable.TerminalSequence != receipt.HistorySequence ||
                durable.TerminalStatus is null ||
                durable.ResultCode is null)
            {
                throw new InvalidDataException("qa04.full-step.post-commit-terminal-operation-drift");
            }
        }

        // Reconstruct WorldState from the published authority surfaces so the state diagnostic is
        // independently recomputed from all 97 partition digests plus all four core substates.
        var reconstructed = new WorldStateV1(
            new WorldStateHeaderV1(
                publishedState.Header.WorldId,
                publishedState.Header.Step,
                publishedState.Header.WorldSeedDigest,
                publishedState.Header.ConfigGeneration,
                publishedState.Header.MasterGeneration,
                publishedState.Header.RateGeneration,
                publishedState.Header.PreviousStateDigest),
            new OrderedPartitionDirectoryV1(partitions),
            CopySubstate(publishedState.SchedulerState),
            CopySubstate(publishedState.OperationState),
            CopySubstate(publishedState.DetailState),
            CopySubstate(publishedState.DomainRegistryState),
            publishedState.Diagnostic.ConfigDigest);
        if (!CryptographicOperations.FixedTimeEquals(
                reconstructed.Diagnostic.StateDigest,
                publishedState.Diagnostic.StateDigest))
            throw new InvalidDataException("qa04.full-step.post-commit-semantic-rehash-drift");

        return new Qa04CanonicalOperationPostCommitVerificationV1(
            publishedState.Header.Step,
            partitions.Length,
            step5Candidate.PartitionCandidates.Count,
            step5Candidate.FrozenInput.ScheduledOperations.Count,
            publishedState.Diagnostic.StateDigest.ToArray());
    }

    private static WorldSubstateRefV1 CopySubstate(WorldSubstateRefV1 value)
        => new(value.Schema, value.CanonicalDigest.ToArray());

    private static void RequireSubstateMatch(
        WorldSubstateRefV1 expected,
        WorldSubstateRefV1 actual,
        string error)
    {
        if (expected.Schema != actual.Schema ||
            !CryptographicOperations.FixedTimeEquals(expected.CanonicalDigest, actual.CanonicalDigest))
            throw new InvalidDataException(error);
    }
}
