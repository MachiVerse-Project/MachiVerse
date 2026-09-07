using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

internal static class Sim06StepCoordinatorSmoke
{
    internal static async Task RunAsync()
    {
        const ulong basisStep = 7;
        var worldId = OpaqueId128.Parse("00000000000000000000000000000600");
        var state = CreateWorldState(worldId, basisStep);

        var scheduledNow = new ScheduledOperationRefV1(
            OpaqueId128.Parse("00000000000000000000000000000601"),
            basisStep,
            new SameStepOrderKey(
                1,
                50,
                SHA256.HashData("sim06-scheduled-now"u8),
                0,
                OpaqueId128.Parse("00000000000000000000000000000611")));
        var scheduledFuture = new ScheduledOperationRefV1(
            OpaqueId128.Parse("00000000000000000000000000000602"),
            basisStep + 1,
            new SameStepOrderKey(
                1,
                50,
                SHA256.HashData("sim06-scheduled-future"u8),
                0,
                OpaqueId128.Parse("00000000000000000000000000000612")));
        var scheduler = new OperationSchedulerStateV1(basisStep, null, [scheduledFuture, scheduledNow]);
        var frozen = StepInputFreezerV1.Freeze(state, scheduler);
        Require(frozen.BasisStep == basisStep && frozen.ScheduledOperations.Count == 1,
            "Step freeze must capture only Operations assigned to the frozen transition.");
        Require(frozen.ScheduledOperations[0].OperationId == scheduledNow.OperationId,
            "Frozen input Operation mismatch.");
        Require(scheduler.FreezeStep == basisStep && scheduler.NextSchedulableStep == basisStep + 1,
            "Freezing external input must advance the admission barrier before calculation starts.");

        var plan = StandardDomainExecutionPlanV1.Create();
        Require(plan.Entries.Count == 8 && plan.Entries.Sum(static entry => entry.OwnedPartitions.Count) == 97,
            "Standard execution plan must cover exactly 8 domains / 97 partitions.");
        var planRanks = plan.Entries.Select(static entry => entry.DomainRank).ToArray();
        Require(planRanks.SequenceEqual(new ushort[] { 10, 20, 30, 40, 50, 60, 70, 80 }),
            "Standard execution plan domain ranks are not canonical.");

        var runtimes = plan.Entries
            .Reverse()
            .Select(static entry => (IDomainRuntimeV1)new EmptyDomainRuntime(entry.DomainToken))
            .ToArray();
        string? canonicalRuntimeOrder = null;
        foreach (var workerCount in new[] { 1, 4, 8, 16 })
        {
            var outputs = await DomainRuntimeExecutorV1.ExecuteAsync(plan, state, frozen, runtimes, workerCount);
            var order = string.Join('|', outputs.Select(static output => output.DomainToken.Value));
            canonicalRuntimeOrder ??= order;
            Require(order == canonicalRuntimeOrder,
                $"Domain output order changed with worker-count={workerCount}.");
        }

        var exclusiveScope = Scope("exclusive");
        var exclusive = new[]
        {
            Intent("00000000000000000000000000000621", -2, ConflictResolutionModeV1.ExclusiveFirstValid, exclusiveScope, "a"),
            Intent("00000000000000000000000000000622", -1, ConflictResolutionModeV1.ExclusiveFirstValid, exclusiveScope, "b"),
            Intent("00000000000000000000000000000623", 0, ConflictResolutionModeV1.ExclusiveFirstValid, exclusiveScope, "c"),
        };
        var expectedOrder = DeterministicIntentMergerV1.CanonicalOrder(exclusive, basisStep)
            .Select(static intent => intent.IntentId)
            .ToArray();
        var random = new Random(142006);
        for (var round = 0; round < 100; round++)
        {
            var permuted = exclusive.ToArray();
            for (var index = permuted.Length - 1; index > 0; index--)
            {
                var swap = random.Next(index + 1);
                (permuted[index], permuted[swap]) = (permuted[swap], permuted[index]);
            }
            var actual = DeterministicIntentMergerV1.CanonicalOrder(permuted, basisStep)
                .Select(static intent => intent.IntentId)
                .ToArray();
            Require(actual.SequenceEqual(expectedOrder),
                "Canonical intent order changed across arrival permutation.");
        }

        var exclusiveGroup = DeterministicIntentMergerV1.GroupByConflictScope(exclusive.Reverse(), basisStep).Single();
        var exclusiveResolution = DeterministicIntentMergerV1.ResolveExclusiveFirstValid(
            exclusiveGroup,
            static intent => intent.SemanticPriority >= -1);
        Require(exclusiveResolution.Outcomes[0].Disposition == MutationIntentDispositionV1.PreconditionFailed &&
                exclusiveResolution.Outcomes[1].Disposition == MutationIntentDispositionV1.Effective &&
                exclusiveResolution.Outcomes[2].Disposition == MutationIntentDispositionV1.ConflictLost,
            "exclusive_first_valid must choose the first valid candidate in SameStepOrderKey order.");

        var sequential = IntentsForMode("sequential", ConflictResolutionModeV1.Sequential);
        var sequentialGroup = DeterministicIntentMergerV1.GroupByConflictScope(sequential.Reverse(), basisStep).Single();
        var sequentialResolution = DeterministicIntentMergerV1.ResolveSequential(sequentialGroup);
        Require(sequentialResolution.Outcomes.All(static outcome => outcome.Disposition == MutationIntentDispositionV1.Effective),
            "Sequential conflict mode must preserve every candidate in canonical order.");
        Require(sequentialResolution.Outcomes.Select(static outcome => outcome.Intent.IntentId)
                .SequenceEqual(DeterministicIntentMergerV1.CanonicalOrder(sequential, basisStep).Select(static intent => intent.IntentId)),
            "Sequential conflict application order is not canonical.");

        var setScope = Scope("set-merge");
        var duplicateDigest = SHA256.HashData("same-member"u8);
        var setMerge = new[]
        {
            Intent("00000000000000000000000000000641", -1, ConflictResolutionModeV1.SetMerge, setScope, duplicateDigest),
            Intent("00000000000000000000000000000642", 0, ConflictResolutionModeV1.SetMerge, setScope, duplicateDigest),
            Intent("00000000000000000000000000000643", 1, ConflictResolutionModeV1.SetMerge, setScope, SHA256.HashData("other-member"u8)),
        };
        var setGroup = DeterministicIntentMergerV1.GroupByConflictScope(setMerge.Reverse(), basisStep).Single();
        var setResolution = DeterministicIntentMergerV1.ResolveSetMerge(
            setGroup,
            static intent => intent.SemanticPayloadDigest.AsMemory());
        Require(setResolution.Outcomes.Count(static outcome => outcome.Disposition == MutationIntentDispositionV1.Effective) == 2 &&
                setResolution.Outcomes.Count(static outcome => outcome.Disposition == MutationIntentDispositionV1.NormalizedDuplicate) == 1,
            "set_merge must normalize duplicate semantic identities deterministically.");

        var reduce = IntentsForMode("reduce", ConflictResolutionModeV1.DeterministicReduce);
        var reduceA = ResolveReduce(reduce);
        var reduceB = ResolveReduce(reduce.Reverse().ToArray());
        Require(reduceA.AggregateDigest is not null && reduceB.AggregateDigest is not null &&
                reduceA.AggregateDigest.SequenceEqual(reduceB.AggregateDigest),
            "deterministic_reduce must be arrival-order independent.");

        var custom = IntentsForMode("custom", ConflictResolutionModeV1.CustomDeterministic);
        var customA = ResolveCustom(custom);
        var customB = ResolveCustom(custom.Reverse().ToArray());
        Require(customA.Outcomes.Select(static outcome => outcome.Intent.IntentId)
                .SequenceEqual(customB.Outcomes.Select(static outcome => outcome.Intent.IntentId)),
            "custom_deterministic resolver must receive canonical candidate order.");

        var allIntents = exclusive
            .Concat(sequential)
            .Concat(setMerge)
            .Concat(reduce)
            .Concat(custom)
            .ToArray();
        var domainOutputs = plan.Entries.Select(entry =>
            entry.DomainToken.Value == "resident"
                ? new DomainCandidateOutputV1(entry.DomainToken, basisStep, allIntents)
                : new DomainCandidateOutputV1(entry.DomainToken, basisStep)).ToArray();
        var allResolutions = new[]
        {
            exclusiveResolution,
            sequentialResolution,
            setResolution,
            reduceA,
            customA,
        };
        var partition = new PartitionCandidateV1(
            new StableToken("resident.identity_lifecycle"),
            new StableToken("resident"),
            basisRevision: 1,
            basisStep,
            SHA256.HashData("sim06-change-set"u8));

        var diagnosticOnly = new InvariantResultV1(
            new StableToken("sim06.diagnostic"),
            InvariantSeverityV1.Diagnostic,
            InvariantOutcomeV1.Fail);
        var blocking = new InvariantResultV1(
            new StableToken("sim06.commit-blocking"),
            InvariantSeverityV1.CommitBlocking,
            InvariantOutcomeV1.Fail,
            diagnosticCode: new StableToken("simulation.invariant-failed"));
        var candidateA = StepCandidateV1.Build(
            OpaqueId128.Parse("00000000000000000000000000000690"),
            state,
            frozen,
            domainOutputs,
            allResolutions,
            [partition],
            [diagnosticOnly, blocking]);
        Require(candidateA.TargetStep == basisStep + 1 && !candidateA.CommitDecision.CanCommit &&
                !candidateA.CommitDecision.FatalAuthorityFailure && !candidateA.IsPublishable,
            "Commit-blocking invariant failure must abort the non-authoritative candidate.");

        var candidateB = StepCandidateV1.Build(
            OpaqueId128.Parse("00000000000000000000000000000691"),
            state,
            frozen,
            domainOutputs.Reverse(),
            allResolutions.Reverse(),
            [partition],
            [blocking, diagnosticOnly]);
        Require(candidateA.DiagnosticDigest.SequenceEqual(candidateB.DiagnosticDigest),
            "Candidate diagnostic must ignore candidate identity and input collection order.");

        var fatal = InvariantBarrierV1.Evaluate([
            new InvariantResultV1(
                new StableToken("sim06.fatal"),
                InvariantSeverityV1.FatalAuthority,
                InvariantOutcomeV1.Fail)
        ]);
        Require(!fatal.CanCommit && fatal.FatalAuthorityFailure,
            "Fatal authority invariant must be distinguished from an ordinary step abort.");
    }

    private static ConflictGroupResolutionV1 ResolveReduce(IReadOnlyCollection<MutationIntentCandidateV1> intents)
    {
        var group = DeterministicIntentMergerV1.GroupByConflictScope(intents, 7).Single();
        return DeterministicIntentMergerV1.ResolveDeterministicReduce(
            group,
            static ordered => HashSuite.DomainHash("mv.state-diagnostic.v1", writer =>
            {
                writer.WriteArrayStart((ulong)ordered.Count);
                foreach (var intent in ordered)
                    writer.WriteBytes(intent.IntentId.ToBytes());
            }));
    }

    private static ConflictGroupResolutionV1 ResolveCustom(IReadOnlyCollection<MutationIntentCandidateV1> intents)
    {
        var group = DeterministicIntentMergerV1.GroupByConflictScope(intents, 7).Single();
        return DeterministicIntentMergerV1.ResolveCustomDeterministic(
            group,
            static canonical => new ConflictGroupResolutionV1(
                canonical,
                canonical.OrderedCandidates.Select(static candidate =>
                    new ResolvedMutationIntentV1(candidate, MutationIntentDispositionV1.Effective))));
    }

    private static MutationIntentCandidateV1[] IntentsForMode(string resource, ConflictResolutionModeV1 mode)
    {
        var scope = Scope(resource);
        var prefix = resource switch
        {
            "sequential" => 0x30,
            "reduce" => 0x50,
            "custom" => 0x60,
            _ => throw new ArgumentOutOfRangeException(nameof(resource)),
        };
        return Enumerable.Range(0, 3)
            .Select(index => Intent(
                $"{prefix + index + 1:x32}",
                index - 1,
                mode,
                scope,
                resource + index))
            .ToArray();
    }

    private static MutationIntentCandidateV1 Intent(
        string id,
        int priority,
        ConflictResolutionModeV1 mode,
        ConflictScopeV1 scope,
        string payload)
        => Intent(id, priority, mode, scope, SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(payload)));

    private static MutationIntentCandidateV1 Intent(
        string id,
        int priority,
        ConflictResolutionModeV1 mode,
        ConflictScopeV1 scope,
        byte[] payloadDigest)
        => new(
            OpaqueId128.Parse(id),
            phase: 1,
            sourceDomain: new StableToken("resident"),
            targetDomain: new StableToken("resident"),
            targetPartitionId: new StableToken("resident.identity_lifecycle"),
            basisStep: 7,
            mutationKind: new StableToken("resident.identity-update"),
            targetScope: scope,
            semanticPriority: priority,
            resolutionMode: mode,
            semanticPayloadDigest: payloadDigest);

    private static ConflictScopeV1 Scope(string resource)
        => new(
            new StableToken("resident"),
            new StableToken("resident-record"),
            OpaqueId128.Parse("000000000000000000000000000006ff").ToBytes(),
            new StableToken(resource));

    private static WorldStateV1 CreateWorldState(OpaqueId128 worldId, ulong step)
    {
        var zero = new byte[32];
        var partitions = StandardDomainPartitionRegistry.Entries.Select(entry => new PartitionStateRefV1(
            new PartitionStateHeaderV1(
                entry,
                revision: 1,
                basisStep: step,
                detailLevel: DetailLevelV1.D0Entity,
                itemCount: 0,
                canonicalDigest: SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(entry.PartitionId.Value)))));
        var header = new WorldStateHeaderV1(
            worldId,
            step,
            worldSeedDigest: zero,
            configGeneration: 1,
            masterGeneration: 1,
            rateGeneration: 1);
        return new WorldStateV1(
            header,
            new OrderedPartitionDirectoryV1(partitions),
            WorldStateV1.EmptySubstate("core.scheduler-state"),
            WorldStateV1.EmptySubstate("core.operation-state"),
            WorldStateV1.EmptySubstate("core.detail-state"),
            WorldStateV1.EmptySubstate("core.domain-registry-state"),
            zero);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class EmptyDomainRuntime(StableToken domainToken) : IDomainRuntimeV1
    {
        public StableToken DomainToken { get; } = domainToken;

        public async ValueTask<DomainCandidateOutputV1> ExecuteAsync(
            DomainRuntimeContextV1 context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            return new DomainCandidateOutputV1(DomainToken, context.FrozenInput.BasisStep);
        }
    }
}
