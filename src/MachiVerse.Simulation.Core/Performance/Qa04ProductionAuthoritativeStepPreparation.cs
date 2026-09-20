using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Production QA-04 authoritative-Step preparation boundary. It requires the completed production
/// reference-world authority contract and the exact canonical perf.reference.v1 workload for the
/// current basis Step, preserves the actual eight-domain runtime outputs, overlays only the six
/// typed mutation partition candidates owned by those domains, and then enters the ordinary
/// StepCandidate / Prepare authority path.
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
        IReadOnlyList<DomainCandidateOutputV1> runtimeOutputs,
        Qa04ProductionStep2CanonicalDigestCacheV1? digestCache = null)
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

        if (basisState.Header.Step == 0)
            throw new InvalidDataException("qa04.production-step.basis-step-zero");
        var injectionStep = checked(basisState.Header.Step - 1UL);
        var expectedDescriptors = Qa04ReferenceLoadV1.OperationsForStep(injectionStep).ToArray();
        var expectedOperationCount = expectedDescriptors.Length;
        if (orderedBindings.Count != expectedOperationCount ||
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

        var expectedFamilyCounts = expectedDescriptors
            .GroupBy(static descriptor => descriptor.FamilyToken.Value, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => checked((ulong)group.LongCount()),
                StringComparer.Ordinal);
        if (mutationResult.AppliedCountByFamily.Count != expectedFamilyCounts.Count ||
            expectedFamilyCounts.Any(pair =>
                !mutationResult.AppliedCountByFamily.TryGetValue(pair.Key, out var actual) || actual != pair.Value))
        {
            throw new InvalidDataException("qa04.production-step.family-coverage-drift");
        }

        var expectedByOperationId = expectedDescriptors.ToDictionary(static descriptor => descriptor.OperationId);
        var observedOperationIds = new HashSet<OpaqueId128>();
        for (var index = 0; index < orderedBindings.Count; index++)
        {
            var binding = orderedBindings[index];
            var frozen = frozenInput.ScheduledOperations[index];
            if (!expectedByOperationId.TryGetValue(binding.SourceDescriptor.OperationId, out var expected) ||
                !observedOperationIds.Add(binding.SourceDescriptor.OperationId) ||
                binding.SourceDescriptor.InjectionStep != injectionStep ||
                binding.SourceDescriptor.FamilyToken != expected.FamilyToken ||
                binding.SourceDescriptor.FamilyOrdinal != expected.FamilyOrdinal ||
                !binding.SourceDescriptor.PayloadDigest.AsSpan().SequenceEqual(expected.PayloadDigest) ||
                binding.ScheduledOperation.EffectiveStep != basisState.Header.Step ||
                frozen.OperationId != binding.SourceDescriptor.OperationId ||
                mutationResult.AppliedOperationIds[index] != frozen.OperationId ||
                !frozen.OrderKey.ToDatabaseBytes().AsSpan().SequenceEqual(binding.OrderKey.ToDatabaseBytes()))
            {
                throw new InvalidDataException("qa04.production-step.operation-order-drift");
            }
        }
        if (observedOperationIds.Count != expectedByOperationId.Count)
            throw new InvalidDataException("qa04.production-step.operation-set-drift");

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
            references,
            digestCache);
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
            PartitionBatch = partitionBatch,
        };
    }

    public static Task<Qa04CanonicalOperationStepPreparationResultV1> PrepareParallelAsync(
        OpaqueId128 candidateId,
        WorldStateV1 basisState,
        FrozenStepInputV1 frozenInput,
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> orderedBindings,
        Qa04CanonicalOperationMutationBatchResultV1 mutationResult,
        IDomainRecordSchemaResolverV1 references,
        IReadOnlyList<DomainCandidateOutputV1> runtimeOutputs,
        int workerCount,
        Qa04ProductionStep2CanonicalDigestCacheV1? digestCache = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(basisState);
        if (basisState.Header.Step == 0)
            throw new InvalidDataException("qa04.production-step.basis-step-zero");

        var injectionStep = checked(basisState.Header.Step - 1UL);
        var expectedDescriptors = Qa04ReferenceLoadV1.OperationsForStep(injectionStep).ToArray();
        return PrepareParallelCoreAsync(
            candidateId,
            basisState,
            frozenInput,
            orderedBindings,
            mutationResult,
            references,
            runtimeOutputs,
            expectedDescriptors,
            workerCount,
            digestCache,
            cancellationToken);
    }

    internal static Task<Qa04CanonicalOperationStepPreparationResultV1> PrepareParallelWithExpectedDescriptorsAsync(
        OpaqueId128 candidateId,
        WorldStateV1 basisState,
        FrozenStepInputV1 frozenInput,
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> orderedBindings,
        Qa04CanonicalOperationMutationBatchResultV1 mutationResult,
        IDomainRecordSchemaResolverV1 references,
        IReadOnlyList<DomainCandidateOutputV1> runtimeOutputs,
        IReadOnlyList<Qa04OperationDescriptorV1> expectedDescriptors,
        int workerCount,
        Qa04ProductionStep2CanonicalDigestCacheV1? digestCache = null,
        CancellationToken cancellationToken = default)
        => PrepareParallelCoreAsync(
            candidateId,
            basisState,
            frozenInput,
            orderedBindings,
            mutationResult,
            references,
            runtimeOutputs,
            expectedDescriptors,
            workerCount,
            digestCache,
            cancellationToken);

    private static async Task<Qa04CanonicalOperationStepPreparationResultV1> PrepareParallelCoreAsync(
        OpaqueId128 candidateId,
        WorldStateV1 basisState,
        FrozenStepInputV1 frozenInput,
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> orderedBindings,
        Qa04CanonicalOperationMutationBatchResultV1 mutationResult,
        IDomainRecordSchemaResolverV1 references,
        IReadOnlyList<DomainCandidateOutputV1> runtimeOutputs,
        IReadOnlyList<Qa04OperationDescriptorV1> expectedDescriptors,
        int workerCount,
        Qa04ProductionStep2CanonicalDigestCacheV1? digestCache,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(basisState);
        ArgumentNullException.ThrowIfNull(frozenInput);
        ArgumentNullException.ThrowIfNull(orderedBindings);
        ArgumentNullException.ThrowIfNull(mutationResult);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(runtimeOutputs);
        ArgumentNullException.ThrowIfNull(expectedDescriptors);

        Qa04ReferenceWorldDependencyContractV1.ValidateCanonicalContract();
        Qa04ReferenceWorldMaterialContractV1.RequireAllProductionMaterializersAvailable();
        Qa04CanonicalOperationBindingV1.ValidateCanonicalContract();

        if (basisState.Header.Step == 0)
            throw new InvalidDataException("qa04.production-step.basis-step-zero");
        var injectionStep = checked(basisState.Header.Step - 1UL);
        var expectedOperationCount = expectedDescriptors.Count;
        if (expectedOperationCount != checked((int)Qa04ReferenceLoadV1.OperationCountForStep(injectionStep)) ||
            expectedDescriptors.Any(descriptor => descriptor.InjectionStep != injectionStep))
        {
            throw new InvalidDataException("qa04.production-step.expected-descriptor-drift");
        }
        if (orderedBindings.Count != expectedOperationCount ||
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

        var expectedFamilyCounts = expectedDescriptors
            .GroupBy(static descriptor => descriptor.FamilyToken.Value, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => checked((ulong)group.LongCount()),
                StringComparer.Ordinal);
        if (mutationResult.AppliedCountByFamily.Count != expectedFamilyCounts.Count ||
            expectedFamilyCounts.Any(pair =>
                !mutationResult.AppliedCountByFamily.TryGetValue(pair.Key, out var actual) || actual != pair.Value))
        {
            throw new InvalidDataException("qa04.production-step.family-coverage-drift");
        }

        var expectedByOperationId = expectedDescriptors.ToDictionary(static descriptor => descriptor.OperationId);
        var observedOperationIds = new HashSet<OpaqueId128>();
        for (var index = 0; index < orderedBindings.Count; index++)
        {
            var binding = orderedBindings[index];
            var frozen = frozenInput.ScheduledOperations[index];
            if (!expectedByOperationId.TryGetValue(binding.SourceDescriptor.OperationId, out var expected) ||
                !observedOperationIds.Add(binding.SourceDescriptor.OperationId) ||
                binding.SourceDescriptor.InjectionStep != injectionStep ||
                binding.SourceDescriptor.FamilyToken != expected.FamilyToken ||
                binding.SourceDescriptor.FamilyOrdinal != expected.FamilyOrdinal ||
                !binding.SourceDescriptor.PayloadDigest.AsSpan().SequenceEqual(expected.PayloadDigest) ||
                binding.ScheduledOperation.EffectiveStep != basisState.Header.Step ||
                frozen.OperationId != binding.SourceDescriptor.OperationId ||
                mutationResult.AppliedOperationIds[index] != frozen.OperationId ||
                !frozen.OrderKey.ToDatabaseBytes().AsSpan().SequenceEqual(binding.OrderKey.ToDatabaseBytes()))
            {
                throw new InvalidDataException("qa04.production-step.operation-order-drift");
            }
        }
        if (observedOperationIds.Count != expectedByOperationId.Count)
            throw new InvalidDataException("qa04.production-step.operation-set-drift");

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

        var partitionBatch = await Qa04CanonicalOperationAuthoritativeStepPartitionBinderV1.BindParallelAsync(
            basisState,
            orderedBindings,
            mutationResult,
            references,
            workerCount,
            digestCache,
            cancellationToken).ConfigureAwait(false);
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
            PartitionBatch = partitionBatch,
        };
    }
}
