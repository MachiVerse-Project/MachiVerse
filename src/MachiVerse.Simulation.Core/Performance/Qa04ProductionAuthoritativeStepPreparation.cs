using System.Diagnostics;
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
    private const int CanonicalDomainCount = 8;
    private static readonly StableToken ProductionInvariant = new("qa04.production-authoritative-step");
    private static readonly IReadOnlyDictionary<string, int> CanonicalFamilyIndexByToken =
        Qa04ReferenceLoadV1.OperationFamilies
            .Select(static (family, index) => (Token: family.FamilyToken.Value, Index: index))
            .ToDictionary(static pair => pair.Token, static pair => pair.Index, StringComparer.Ordinal);

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

        ValidateCanonicalWorkloadAlignment(
            injectionStep,
            basisState.Header.Step,
            expectedDescriptors,
            orderedBindings,
            frozenInput,
            mutationResult);

        var plan = StandardDomainExecutionPlanV1.Create();
        var runtimeByDomain = IndexCanonicalDomainOutputs(
            plan,
            runtimeOutputs,
            "qa04.production-step.runtime-output-coverage-drift");
        ValidateRuntimeOutputs(runtimeByDomain, basisState.Header.Step);

        var partitionBatch = Qa04CanonicalOperationAuthoritativeStepPartitionBinderV1.Bind(
            basisState,
            orderedBindings,
            mutationResult,
            references,
            digestCache);
        var mutationOutputs = Qa04CanonicalOperationDomainOutputBinderV1.Bind(basisState, partitionBatch);
        var mutationByDomain = IndexCanonicalDomainOutputs(
            plan,
            mutationOutputs.Outputs,
            "qa04.production-step.merged-output-drift");
        var mergedOutputs = MergeCanonicalDomainOutputs(
            plan,
            basisState.Header.Step,
            runtimeByDomain,
            mutationByDomain);

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

        var diagnosticStarted = Stopwatch.GetTimestamp();
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
            mutationResult.AppliedOperationIds.Count != expectedOperationCount ||
            mutationResult.Changes.Count != expectedOperationCount)
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

        var prevalidatedPartitionInput = ValidateCanonicalWorkloadAlignmentAndBuildPartitionInput(
            injectionStep,
            basisState.Header.Step,
            expectedDescriptors,
            orderedBindings,
            frozenInput,
            mutationResult);

        var plan = StandardDomainExecutionPlanV1.Create();
        var runtimeByDomain = IndexCanonicalDomainOutputs(
            plan,
            runtimeOutputs,
            "qa04.production-step.runtime-output-coverage-drift");
        ValidateRuntimeOutputs(runtimeByDomain, basisState.Header.Step);
        EmitDiagnosticPhase(injectionStep, workerCount, "step-preparation-alignment", diagnosticStarted);

        diagnosticStarted = Stopwatch.GetTimestamp();
        var partitionBatch = await Qa04CanonicalOperationAuthoritativeStepPartitionBinderV1.BindParallelPrevalidatedAsync(
            basisState,
            mutationResult,
            references,
            workerCount,
            prevalidatedPartitionInput,
            digestCache,
            cancellationToken).ConfigureAwait(false);
        EmitDiagnosticPhase(injectionStep, workerCount, "step-preparation-partition-bind", diagnosticStarted);

        diagnosticStarted = Stopwatch.GetTimestamp();
        var mutationOutputs = Qa04CanonicalOperationDomainOutputBinderV1.Bind(basisState, partitionBatch);
        var mutationByDomain = IndexCanonicalDomainOutputs(
            plan,
            mutationOutputs.Outputs,
            "qa04.production-step.merged-output-drift");
        var mergedOutputs = MergeCanonicalDomainOutputs(
            plan,
            basisState.Header.Step,
            runtimeByDomain,
            mutationByDomain);
        EmitDiagnosticPhase(injectionStep, workerCount, "step-preparation-domain-output-merge", diagnosticStarted);

        diagnosticStarted = Stopwatch.GetTimestamp();
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
        EmitDiagnosticPhase(injectionStep, workerCount, "step-preparation-candidate-build", diagnosticStarted);

        diagnosticStarted = Stopwatch.GetTimestamp();
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
        EmitDiagnosticPhase(injectionStep, workerCount, "step-preparation-state-prepare", diagnosticStarted);

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

    private static DomainCandidateOutputV1[] IndexCanonicalDomainOutputs(
        StandardDomainExecutionPlanV1 plan,
        IReadOnlyList<DomainCandidateOutputV1> outputs,
        string driftCode)
    {
        if (plan.Entries.Count != CanonicalDomainCount || outputs.Count != CanonicalDomainCount)
            throw new InvalidDataException(driftCode);

        var indexed = new DomainCandidateOutputV1[CanonicalDomainCount];
        for (var outputIndex = 0; outputIndex < outputs.Count; outputIndex++)
        {
            var output = outputs[outputIndex]
                ?? throw new InvalidDataException(driftCode);
            var domainIndex = FindCanonicalDomainIndex(plan, output.DomainToken);
            if (domainIndex < 0 || indexed[domainIndex] is not null)
                throw new InvalidDataException(driftCode);

            indexed[domainIndex] = output;
        }

        for (var domainIndex = 0; domainIndex < indexed.Length; domainIndex++)
        {
            if (indexed[domainIndex] is null)
                throw new InvalidDataException(driftCode);
        }

        return indexed;
    }

    private static int FindCanonicalDomainIndex(
        StandardDomainExecutionPlanV1 plan,
        StableToken domainToken)
    {
        for (var domainIndex = 0; domainIndex < plan.Entries.Count; domainIndex++)
        {
            if (plan.Entries[domainIndex].DomainToken == domainToken)
                return domainIndex;
        }

        return -1;
    }

    private static void ValidateRuntimeOutputs(
        IReadOnlyList<DomainCandidateOutputV1> runtimeByDomain,
        ulong basisStep)
    {
        for (var domainIndex = 0; domainIndex < runtimeByDomain.Count; domainIndex++)
        {
            var output = runtimeByDomain[domainIndex];
            if (output.BasisStep != basisStep)
                throw new InvalidDataException("qa04.production-step.runtime-output-coverage-drift");
            if (output.LocalPartitionCandidates.Count != 0)
                throw new InvalidDataException("qa04.production-step.runtime-local-candidate-overlap");
        }
    }

    private static DomainCandidateOutputV1[] MergeCanonicalDomainOutputs(
        StandardDomainExecutionPlanV1 plan,
        ulong basisStep,
        IReadOnlyList<DomainCandidateOutputV1> runtimeByDomain,
        IReadOnlyList<DomainCandidateOutputV1> mutationByDomain)
    {
        if (plan.Entries.Count != CanonicalDomainCount ||
            runtimeByDomain.Count != CanonicalDomainCount ||
            mutationByDomain.Count != CanonicalDomainCount)
        {
            throw new InvalidDataException("qa04.production-step.merged-output-drift");
        }

        var mergedOutputs = new DomainCandidateOutputV1[CanonicalDomainCount];
        var localPartitionCandidateCount = 0;
        for (var domainIndex = 0; domainIndex < CanonicalDomainCount; domainIndex++)
        {
            var entry = plan.Entries[domainIndex];
            var runtime = runtimeByDomain[domainIndex];
            var mutation = mutationByDomain[domainIndex];
            if (runtime.DomainToken != entry.DomainToken || mutation.DomainToken != entry.DomainToken)
                throw new InvalidDataException("qa04.production-step.merged-output-drift");

            localPartitionCandidateCount = checked(
                localPartitionCandidateCount + mutation.LocalPartitionCandidates.Count);
            mergedOutputs[domainIndex] = new DomainCandidateOutputV1(
                entry.DomainToken,
                basisStep,
                intents: runtime.Intents,
                localPartitionCandidates: mutation.LocalPartitionCandidates);
        }

        if (localPartitionCandidateCount != 6)
            throw new InvalidDataException("qa04.production-step.merged-output-drift");

        return mergedOutputs;
    }

    private static void EmitDiagnosticPhase(
        ulong injectionStep,
        int workerCount,
        string phase,
        long startedTimestamp)
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("MACHIVERSE_QA04_DETAILED_PHASE_DIAGNOSTICS"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var intervalRaw = Environment.GetEnvironmentVariable("MACHIVERSE_GATE4_STEP3_PHASE_LOG_INTERVAL_TRANSITIONS");
        var interval = int.TryParse(intervalRaw, out var parsedInterval) && parsedInterval > 0
            ? parsedInterval
            : 1;
        if (injectionStep % checked((ulong)interval) != 0)
            return;

        Console.Error.WriteLine(
            $"QA04_PHASE workers={workerCount} injection_step={injectionStep} phase={phase} " +
            $"elapsed_ms={Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds:F1}");
    }

    private static void ValidateCanonicalWorkloadAlignment(
        ulong injectionStep,
        ulong basisStep,
        IReadOnlyList<Qa04OperationDescriptorV1> expectedDescriptors,
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> orderedBindings,
        FrozenStepInputV1 frozenInput,
        Qa04CanonicalOperationMutationBatchResultV1 mutationResult)
        => _ = ValidateCanonicalWorkloadAlignmentCore(
            injectionStep,
            basisStep,
            expectedDescriptors,
            orderedBindings,
            frozenInput,
            mutationResult,
            buildPartitionInput: false);

    private static Qa04CanonicalOperationAuthoritativeStepPartitionBinderV1.PrevalidatedInput
        ValidateCanonicalWorkloadAlignmentAndBuildPartitionInput(
            ulong injectionStep,
            ulong basisStep,
            IReadOnlyList<Qa04OperationDescriptorV1> expectedDescriptors,
            IReadOnlyList<Qa04CanonicalOperationBindingResultV1> orderedBindings,
            FrozenStepInputV1 frozenInput,
            Qa04CanonicalOperationMutationBatchResultV1 mutationResult)
        => ValidateCanonicalWorkloadAlignmentCore(
                injectionStep,
                basisStep,
                expectedDescriptors,
                orderedBindings,
                frozenInput,
                mutationResult,
                buildPartitionInput: true)
            ?? throw new InvalidDataException("qa04.production-step.partition-prevalidation-missing");

    private static Qa04CanonicalOperationAuthoritativeStepPartitionBinderV1.PrevalidatedInput?
        ValidateCanonicalWorkloadAlignmentCore(
            ulong injectionStep,
            ulong basisStep,
            IReadOnlyList<Qa04OperationDescriptorV1> expectedDescriptors,
            IReadOnlyList<Qa04CanonicalOperationBindingResultV1> orderedBindings,
            FrozenStepInputV1 frozenInput,
            Qa04CanonicalOperationMutationBatchResultV1 mutationResult,
            bool buildPartitionInput)
    {
        if (expectedDescriptors.Count != orderedBindings.Count ||
            frozenInput.ScheduledOperations.Count != orderedBindings.Count ||
            mutationResult.AppliedOperationIds.Count != orderedBindings.Count ||
            mutationResult.Changes.Count != orderedBindings.Count)
        {
            throw new InvalidDataException("qa04.production-step.workload-count-drift");
        }

        var seenOrdinals = new bool[expectedDescriptors.Count];
        var seenOperationIds = new HashSet<OpaqueId128>(orderedBindings.Count);
        Span<ulong> familyCounts = stackalloc ulong[6];
        if (CanonicalFamilyIndexByToken.Count != familyCounts.Length)
            throw new InvalidDataException("qa04.production-step.family-contract-drift");

        var partitionInputBuilder = buildPartitionInput
            ? new Qa04CanonicalOperationAuthoritativeStepPartitionBinderV1.PrevalidatedInputBuilder(orderedBindings.Count)
            : null;
        SameStepOrderKey? previousOrderKey = null;

        for (var index = 0; index < orderedBindings.Count; index++)
        {
            var binding = orderedBindings[index]
                ?? throw new InvalidDataException("qa04.production-step.operation-order-drift");
            var source = binding.SourceDescriptor
                ?? throw new InvalidDataException("qa04.production-step.operation-order-drift");
            if (binding.ScheduledOperation is null || binding.OrderKey is null)
                throw new InvalidDataException("qa04.production-step.operation-order-drift");

            var ordinal = source.FamilyOrdinal;
            if (ordinal >= checked((ulong)expectedDescriptors.Count))
                throw new InvalidDataException("qa04.production-step.operation-order-drift");
            var expectedIndex = checked((int)ordinal);
            if (seenOrdinals[expectedIndex])
                throw new InvalidDataException("qa04.production-step.operation-order-drift");
            seenOrdinals[expectedIndex] = true;

            var expected = expectedDescriptors[expectedIndex]
                ?? throw new InvalidDataException("qa04.production-step.expected-descriptor-drift");
            if (expected.InjectionStep != injectionStep ||
                expected.FamilyOrdinal != ordinal ||
                source.InjectionStep != injectionStep ||
                source.OperationId.IsZero ||
                source.OperationId != expected.OperationId ||
                source.FamilyToken != expected.FamilyToken ||
                !source.PayloadDigest.AsSpan().SequenceEqual(expected.PayloadDigest) ||
                binding.ScheduledOperation.EffectiveStep != basisStep ||
                !binding.OrderKey.CanonicallyEquals(binding.ScheduledOperation.OrderKey) ||
                !seenOperationIds.Add(source.OperationId) ||
                (previousOrderKey is not null && previousOrderKey.CompareTo(binding.OrderKey) >= 0))
            {
                throw new InvalidDataException("qa04.production-step.operation-order-drift");
            }
            previousOrderKey = binding.OrderKey;

            var frozen = frozenInput.ScheduledOperations[index];
            if (frozen.OperationId != source.OperationId ||
                mutationResult.AppliedOperationIds[index] != frozen.OperationId ||
                !frozen.OrderKey.CanonicallyEquals(binding.OrderKey))
            {
                throw new InvalidDataException("qa04.production-step.operation-order-drift");
            }

            if (!CanonicalFamilyIndexByToken.TryGetValue(expected.FamilyToken.Value, out var familyIndex))
                throw new InvalidDataException("qa04.production-step.family-coverage-drift");
            familyCounts[familyIndex] = checked(familyCounts[familyIndex] + 1UL);

            partitionInputBuilder?.Add(
                familyIndex,
                binding,
                mutationResult.Changes[index]);
        }

        if (mutationResult.AppliedCountByFamily.Count != CanonicalFamilyIndexByToken.Count)
            throw new InvalidDataException("qa04.production-step.family-coverage-drift");
        for (var familyIndex = 0; familyIndex < Qa04ReferenceLoadV1.OperationFamilies.Count; familyIndex++)
        {
            var token = Qa04ReferenceLoadV1.OperationFamilies[familyIndex].FamilyToken.Value;
            if (!mutationResult.AppliedCountByFamily.TryGetValue(token, out var actual) ||
                actual != familyCounts[familyIndex])
            {
                throw new InvalidDataException("qa04.production-step.family-coverage-drift");
            }
        }

        return partitionInputBuilder?.Build();
    }
}
