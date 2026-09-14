using MachiVerse.Simulation.Core.Determinism;
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
}
