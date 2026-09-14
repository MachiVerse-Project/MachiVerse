using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04CanonicalOperationStepPreparationResultV1(
    Qa04CanonicalOperationDomainOutputBatchV1 DomainOutputs,
    StepCandidateV1 Candidate,
    PreparedStepWorldStateV1 PreparedState);

/// <summary>
/// Gate-2 Steps 3-5 bridge from the six actual typed mutation partition results to the ordinary
/// eight-domain StepCandidate/invariant/Prepare path. This boundary deliberately stops before the
/// SQLite COMMIT and therefore cannot publish State(S+1).
/// </summary>
public static class Qa04CanonicalOperationStepPreparationV1
{
    public static Qa04CanonicalOperationStepPreparationResultV1 Prepare(
        OpaqueId128 candidateId,
        WorldStateV1 basisState,
        FrozenStepInputV1 frozenInput,
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> orderedBindings,
        Qa04CanonicalOperationMutationBatchResultV1 mutationResult,
        IDomainRecordSchemaResolverV1 references,
        IEnumerable<InvariantResultV1>? invariantResults = null)
    {
        ArgumentNullException.ThrowIfNull(orderedBindings);
        ArgumentNullException.ThrowIfNull(mutationResult);
        ArgumentNullException.ThrowIfNull(references);

        RequireFrozenOperationMatch(frozenInput, orderedBindings, mutationResult);
        var partitionBatch = Qa04CanonicalOperationAuthoritativeStepPartitionBinderV1.Bind(
            basisState,
            orderedBindings,
            mutationResult,
            references);
        return Prepare(
            candidateId,
            basisState,
            frozenInput,
            partitionBatch,
            invariantResults);
    }

    public static Qa04CanonicalOperationStepPreparationResultV1 Prepare(
        OpaqueId128 candidateId,
        WorldStateV1 basisState,
        FrozenStepInputV1 frozenInput,
        Qa04CanonicalOperationPartitionCandidateBatchV1 partitionBatch,
        IEnumerable<InvariantResultV1>? invariantResults = null)
    {
        if (candidateId.IsZero)
            throw new ArgumentException("CandidateId ZERO is invalid.", nameof(candidateId));
        ArgumentNullException.ThrowIfNull(basisState);
        ArgumentNullException.ThrowIfNull(frozenInput);
        ArgumentNullException.ThrowIfNull(partitionBatch);

        if (frozenInput.WorldId != basisState.Header.WorldId ||
            frozenInput.BasisStep != basisState.Header.Step)
            throw new InvalidDataException("qa04.full-step.step-preparation-frozen-basis-drift");
        if (partitionBatch.BasisStep != basisState.Header.Step ||
            partitionBatch.TargetStep != checked(basisState.Header.Step + 1UL))
            throw new InvalidDataException("qa04.full-step.step-preparation-partition-step-drift");

        var domainOutputs = Qa04CanonicalOperationDomainOutputBinderV1.Bind(basisState, partitionBatch);
        var candidate = StepCandidateV1.Build(
            candidateId,
            basisState,
            frozenInput,
            domainOutputs.Outputs,
            conflictResolutions: Array.Empty<ConflictGroupResolutionV1>(),
            invariantResults: invariantResults ?? Array.Empty<InvariantResultV1>());

        if (candidate.DomainOutputs.Count != 8)
            throw new InvalidDataException("qa04.full-step.step-preparation-domain-count-drift");
        if (candidate.PartitionCandidates.Count != partitionBatch.Partitions.Count)
            throw new InvalidDataException("qa04.full-step.step-preparation-partition-count-drift");
        if (candidate.OrderedIntents.Count != 0 || candidate.ConflictResolutions.Count != 0)
            throw new InvalidDataException("qa04.full-step.step-preparation-unexpected-conflict-surface");
        if (candidate.IsPublishable)
            throw new InvalidDataException("qa04.full-step.step-preparation-premature-candidate-authority");

        var prepared = StepStateApplicationV1.Prepare(
            basisState,
            candidate,
            partitionBatch.Partitions.Select(static mutation => mutation.Material));
        if (prepared.IsPublishable)
            throw new InvalidDataException("qa04.full-step.step-preparation-premature-state-authority");
        if (prepared.BasisStep != basisState.Header.Step ||
            prepared.TargetStep != partitionBatch.TargetStep)
            throw new InvalidDataException("qa04.full-step.step-preparation-result-step-drift");

        return new Qa04CanonicalOperationStepPreparationResultV1(
            domainOutputs,
            candidate,
            prepared);
    }

    private static void RequireFrozenOperationMatch(
        FrozenStepInputV1 frozenInput,
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> orderedBindings,
        Qa04CanonicalOperationMutationBatchResultV1 mutationResult)
    {
        ArgumentNullException.ThrowIfNull(frozenInput);
        if (frozenInput.ScheduledOperations.Count != orderedBindings.Count ||
            mutationResult.AppliedOperationIds.Count != orderedBindings.Count)
            throw new InvalidDataException("qa04.full-step.step-preparation-operation-coverage-drift");

        for (var index = 0; index < orderedBindings.Count; index++)
        {
            var binding = orderedBindings[index]
                ?? throw new InvalidDataException("qa04.full-step.step-preparation-binding-null");
            var scheduled = frozenInput.ScheduledOperations[index];
            if (binding.ScheduledOperation.EffectiveStep != frozenInput.BasisStep ||
                scheduled.OperationId != binding.SourceDescriptor.OperationId ||
                scheduled.OperationId != mutationResult.AppliedOperationIds[index] ||
                scheduled.EffectiveStep != binding.ScheduledOperation.EffectiveStep ||
                !scheduled.OrderKey.ToDatabaseBytes().AsSpan().SequenceEqual(
                    binding.OrderKey.ToDatabaseBytes()))
            {
                throw new InvalidDataException("qa04.full-step.step-preparation-frozen-operation-drift");
            }
        }
    }
}
