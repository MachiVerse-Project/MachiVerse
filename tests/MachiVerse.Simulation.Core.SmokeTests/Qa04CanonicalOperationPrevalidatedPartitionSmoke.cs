using System.Reflection;
using MachiVerse.Simulation.Core.Performance;

internal static class Qa04CanonicalOperationPrevalidatedPartitionSmoke
{
    internal static void Run()
    {
        var bindings = Qa04ReferenceLoadV1.OperationsForStep(0)
            .GroupBy(static descriptor => descriptor.FamilyToken.Value, StringComparer.Ordinal)
            .SelectMany(static group => group.Take(2))
            .Select(static descriptor => Qa04CanonicalOperationBindingV1.Bind(
                descriptor,
                schedulingPolicyGeneration: 1))
            .OrderBy(static binding => binding.OrderKey)
            .ToArray();
        Require(bindings.Length == 12,
            "Gate4 prevalidated partition smoke requires two bindings from every canonical family.");

        var effectiveStep = bindings[0].ScheduledOperation.EffectiveStep;
        Require(effectiveStep > 0 &&
                bindings.All(binding => binding.ScheduledOperation.EffectiveStep == effectiveStep),
            "Gate4 prevalidated partition smoke requires one common effective step.");

        var assembly = Qa04ProductionReferenceWorldAssemblerV1.AssembleCanonical(effectiveStep);
        var basisState = assembly.PartitionAuthorityState;
        var references = assembly.References;
        Require(basisState.Header.Step == effectiveStep,
            "Gate4 prevalidated partition smoke basis step drifted.");

        var mutation = Qa04CanonicalOperationMutationBatchV1.Apply(
            Qa04ReferenceLoadV1.WorldId,
            effectiveStep,
            bindings,
            assembly.MutationState,
            references);
        Require(mutation.AppliedOperationIds.Count == bindings.Length &&
                mutation.Changes.Count == bindings.Length &&
                mutation.AppliedCountByFamily.Count == 6 &&
                mutation.AppliedCountByFamily.Values.All(static count => count == 2),
            "Gate4 prevalidated partition smoke mutation coverage drifted.");

        const int workerCount = 4;
        var defensive = Qa04CanonicalOperationAuthoritativeStepPartitionBinderV1.BindParallelAsync(
            basisState,
            bindings,
            mutation,
            references,
            workerCount).GetAwaiter().GetResult();

        var binderType = typeof(Qa04CanonicalOperationAuthoritativeStepPartitionBinderV1);
        var builderType = binderType.GetNestedType(
                "PrevalidatedInputBuilder",
                BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "Gate4 prevalidated partition builder type missing.");
        var builderConstructor = builderType.GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: [typeof(int)],
                modifiers: null)
            ?? throw new InvalidOperationException(
                "Gate4 prevalidated partition builder constructor missing.");
        var builder = builderConstructor.Invoke([bindings.Length]);
        var add = builderType.GetMethod(
                "Add",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "Gate4 prevalidated partition builder Add method missing.");
        var build = builderType.GetMethod(
                "Build",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "Gate4 prevalidated partition builder Build method missing.");

        var preparationType = typeof(Qa04ProductionAuthoritativeStepPreparationV1);
        var partitionFamilySlots = preparationType.GetField(
                "CanonicalPartitionFamilySlotByIndex",
                BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as int[]
            ?? throw new InvalidOperationException(
                "Gate4 production prevalidated partition family-slot mapping missing.");
        Require(partitionFamilySlots.Length == Qa04ReferenceLoadV1.OperationFamilies.Count,
            "Gate4 production prevalidated partition family-slot mapping coverage drifted.");
        var canonicalFamilyIndexByToken = Qa04ReferenceLoadV1.OperationFamilies
            .Select(static (family, index) => (Token: family.FamilyToken.Value, Index: index))
            .ToDictionary(static pair => pair.Token, static pair => pair.Index, StringComparer.Ordinal);

        var changesByOperation = mutation.Changes.ToDictionary(static change => change.OperationId);
        foreach (var binding in bindings)
        {
            var source = binding.SourceDescriptor;
            Require(changesByOperation.TryGetValue(source.OperationId, out var change),
                "Gate4 prevalidated partition smoke change lookup drifted.");
            Require(canonicalFamilyIndexByToken.TryGetValue(source.FamilyToken.Value, out var canonicalFamilyIndex),
                "Gate4 prevalidated partition smoke canonical family lookup drifted.");
            add.Invoke(
                builder,
                [partitionFamilySlots[canonicalFamilyIndex], binding, change!]);
        }

        var prevalidatedInput = build.Invoke(builder, parameters: null)
            ?? throw new InvalidOperationException(
                "Gate4 prevalidated partition builder returned null.");
        var bindPrevalidated = binderType.GetMethod(
                "BindParallelPrevalidatedAsync",
                BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "Gate4 prevalidated partition binder method missing.");
        var task = bindPrevalidated.Invoke(
                obj: null,
                parameters:
                [
                    basisState,
                    mutation,
                    references,
                    workerCount,
                    prevalidatedInput,
                    null,
                    CancellationToken.None,
                ]) as Task<Qa04CanonicalOperationPartitionCandidateBatchV1>
            ?? throw new InvalidOperationException(
                "Gate4 prevalidated partition binder returned an unexpected task type.");
        var prevalidated = task.GetAwaiter().GetResult();

        RequireSamePartitionBatch(defensive, prevalidated);
    }

    private static void RequireSamePartitionBatch(
        Qa04CanonicalOperationPartitionCandidateBatchV1 expected,
        Qa04CanonicalOperationPartitionCandidateBatchV1 actual)
    {
        Require(actual.BasisStep == expected.BasisStep &&
                actual.TargetStep == expected.TargetStep &&
                actual.Partitions.Count == expected.Partitions.Count,
            "Gate4 prevalidated partition batch authority drifted.");

        for (var index = 0; index < expected.Partitions.Count; index++)
        {
            var expectedPartition = expected.Partitions[index];
            var actualPartition = actual.Partitions[index];
            var expectedCandidate = expectedPartition.Candidate;
            var actualCandidate = actualPartition.Candidate;
            var expectedHeader = expectedPartition.Material.ResultingHeader;
            var actualHeader = actualPartition.Material.ResultingHeader;

            Require(actualCandidate.PartitionId == expectedCandidate.PartitionId &&
                    actualCandidate.OwnerDomain == expectedCandidate.OwnerDomain &&
                    actualCandidate.BasisRevision == expectedCandidate.BasisRevision &&
                    actualCandidate.CandidateRevision == expectedCandidate.CandidateRevision &&
                    actualCandidate.BasisStep == expectedCandidate.BasisStep &&
                    actualCandidate.TargetStep == expectedCandidate.TargetStep &&
                    actualCandidate.ChangeSetDigest.AsSpan().SequenceEqual(expectedCandidate.ChangeSetDigest),
                $"Gate4 prevalidated partition candidate drifted at index={index}.");
            Require(actualHeader.PartitionId == expectedHeader.PartitionId &&
                    actualHeader.OwnerDomain == expectedHeader.OwnerDomain &&
                    actualHeader.Schema == expectedHeader.Schema &&
                    actualHeader.Revision == expectedHeader.Revision &&
                    actualHeader.BasisStep == expectedHeader.BasisStep &&
                    actualHeader.DetailLevel == expectedHeader.DetailLevel &&
                    actualHeader.ItemCount == expectedHeader.ItemCount &&
                    actualHeader.DigestAlgorithm == expectedHeader.DigestAlgorithm &&
                    actualHeader.CanonicalDigest.AsSpan().SequenceEqual(expectedHeader.CanonicalDigest),
                $"Gate4 prevalidated partition resulting header drifted at index={index}.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
