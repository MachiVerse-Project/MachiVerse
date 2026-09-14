using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Gate-2 Step 13 preparation boundary. It requires the completed production reference-world
/// authority contract and the full steady perf.reference.v1 workload, preserves the actual
/// eight-domain runtime outputs, overlays only the six typed mutation partition candidates owned by
/// those domains, and then enters the ordinary StepCandidate / Prepare authority path.
///
/// This boundary does not cross SQLite COMMIT or publish State(S+1); finalization remains owned by
/// Qa04CanonicalOperationStepFinalizationV1.
/// </summary>
public static class Qa04ProductionAuthoritativeStepPreparationV1
{
    private static readonly StableToken ProductionInvariant = new("qa04.production-authoritative-step");

    public static Qa04CanonicalOperationStepPreparationResultV1 Prepare(
        OpaqueId128 candidateId,
        WorldStateV1 basisState,
        FrozenStepInputV1 frozenInput,
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> orderedBindings,
        Qa04CanonicalOperationMutationBatchResultV1 mutationResult,
        IDomainRecordSchemaResolverV1 references,
        IReadOnlyList<DomainCandidateOutputV1> runtimeOutputs)
    {
        ArgumentNullException.ThrowIfNull(basisState);
        ArgumentNullException.ThrowIfNull(frozenInput);
        ArgumentNullException.ThrowIfNull(orderedBindings);
        ArgumentNullException.ThrowIfNull(mutationResult);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(runtimeOutputs);

        Qa04ReferenceWorldDependencyContractV1.ValidateCanonicalContract();
        Qa04ReferenceWorldMaterialContractV1.RequireAllProductionMaterializersAvailable();
        Qa04CanonicalOperationBindingV1.ValidateCanonicalContract();

        var expectedOperationCount = checked((int)Qa04ReferenceLoadV1.OperationCountForStep(0));
        if (expectedOperationCount != checked((int)Qa04ReferenceLoadV1.SteadyOperationsPerStep) ||
            orderedBindings.Count != expectedOperationCount ||
            frozenInput.ScheduledOperations.Count != expectedOperationCount ||
            mutationResult.AppliedOperationIds.Count != expectedOperationCount)
        {
            throw new InvalidDataException("qa04.production-step.workload-count-drift");
        }
        if (basisState.Header.WorldId != Qa04ReferenceLoadV1.WorldId ||
            frozenInput.WorldId != basisState.Header.WorldId ||
            frozenInput.BasisStep != basisState.Header.Step ||
            mutationResult.EffectiveStep != basisState.Header.Step)
        {
            throw new InvalidDataException("qa04.production-step.basis-drift");
        }

        var expectedFamilyCounts = Qa04ReferenceLoadV1.OperationFamilies.ToDictionary(
            static family => family.FamilyToken.Value,
            static family => checked(Qa04ReferenceLoadV1.SteadyOperationsPerStep * family.SharePermille / 1_000UL),
            StringComparer.Ordinal);
        if (mutationResult.AppliedCountByFamily.Count != expectedFamilyCounts.Count ||
            expectedFamilyCounts.Any(pair =>
                !mutationResult.AppliedCountByFamily.TryGetValue(pair.Key, out var actual) || actual != pair.Value))
        {
            throw new InvalidDataException("qa04.production-step.family-coverage-drift");
        }

        for (var index = 0; index < orderedBindings.Count; index++)
        {
            var binding = orderedBindings[index];
            var frozen = frozenInput.ScheduledOperations[index];
            if (binding.SourceDescriptor.InjectionStep != 0 ||
                binding.ScheduledOperation.EffectiveStep != basisState.Header.Step ||
                frozen.OperationId != binding.SourceDescriptor.OperationId ||
                mutationResult.AppliedOperationIds[index] != frozen.OperationId ||
                !frozen.OrderKey.ToDatabaseBytes().AsSpan().SequenceEqual(binding.OrderKey.ToDatabaseBytes()))
            {
                throw new InvalidDataException("qa04.production-step.operation-order-drift");
            }
        }

        var plan = StandardDomainExecutionPlanV1.Create();
        var runtimeByDomain = runtimeOutputs.ToDictionary(static output => output.DomainToken);
        if (runtimeOutputs.Count != plan.Entries.Count ||
            runtimeByDomain.Count != plan.Entries.Count ||
            plan.Entries.Any(entry => !runtimeByDomain.ContainsKey(entry.DomainToken)) ||
            runtimeOutputs.Any(output => output.BasisStep != basisState.Header.Step))
        {
            throw new InvalidDataException("qa04.production-step.runtime-output-coverage-drift");
        }

        // The current production workload mutations are applied by the six approved typed handlers.
        // A runtime-owned partition candidate for the same Step would introduce a second mutation
        // authority and is therefore rejected rather than merged heuristically.
        if (runtimeOutputs.Any(static output => output.LocalPartitionCandidates.Count != 0))
            throw new InvalidDataException("qa04.production-step.runtime-local-candidate-overlap");

        var partitionBatch = Qa04CanonicalOperationAuthoritativeStepPartitionBinderV1.Bind(
            basisState,
            orderedBindings,
            mutationResult,
            references);
        var mutationOutputs = Qa04CanonicalOperationDomainOutputBinderV1.Bind(basisState, partitionBatch);
        var mutationByDomain = mutationOutputs.Outputs.ToDictionary(static output => output.DomainToken);

        var mergedOutputs = plan.Entries
            .Select(entry =>
            {
                var runtime = runtimeByDomain[entry.DomainToken];
                var mutation = mutationByDomain[entry.DomainToken];
                return new DomainCandidateOutputV1(
                    entry.DomainToken,
                    basisState.Header.Step,
                    intents: runtime.Intents,
                    localPartitionCandidates: mutation.LocalPartitionCandidates);
            })
            .ToArray();

        if (mergedOutputs.Length != 8 ||
            mergedOutputs.Sum(static output => output.LocalPartitionCandidates.Count) != 6)
        {
            throw new InvalidDataException("qa04.production-step.merged-output-drift");
        }

        var candidate = StepCandidateV1.Build(
            candidateId,
            basisState,
            frozenInput,
            mergedOutputs,
            conflictResolutions: Array.Empty<ConflictGroupResolutionV1>(),
            invariantResults:
            [
                new InvariantResultV1(
                    ProductionInvariant,
                    InvariantSeverityV1.CommitBlocking,
                    InvariantOutcomeV1.Pass),
            ]);
        if (!candidate.CommitDecision.CanCommit || candidate.IsPublishable ||
            candidate.PartitionCandidates.Count != 6)
        {
            throw new InvalidDataException("qa04.production-step.candidate-authority-drift");
        }

        var prepared = StepStateApplicationV1.Prepare(
            basisState,
            candidate,
            partitionBatch.Partitions.Select(static partition => partition.Material));
        if (prepared.IsPublishable ||
            prepared.BasisStep != basisState.Header.Step ||
            prepared.TargetStep != checked(basisState.Header.Step + 1UL))
        {
            throw new InvalidDataException("qa04.production-step.prepared-authority-drift");
        }

        return new Qa04CanonicalOperationStepPreparationResultV1(
            new Qa04CanonicalOperationDomainOutputBatchV1(
                basisState.Header.Step,
                checked(basisState.Header.Step + 1UL),
                Array.AsReadOnly(mergedOutputs)),
            candidate,
            prepared)
        {
            BasisState = basisState,
        };
    }
}
