using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

internal static class Sim06DependencyDagSmoke
{
    internal static async Task RunAsync()
    {
        const ulong basisStep = 31;
        var state = CreateWorldState(
            OpaqueId128.Parse("00000000000000000000000000000910"),
            basisStep);
        var scheduler = new OperationSchedulerStateV1(basisStep, null);
        var frozen = StepInputFreezerV1.Freeze(state, scheduler);

        var independent = StandardDomainExecutionPlanV1.Create();
        Require(independent.ExecutionWaves.Count == 1 && independent.ExecutionWaves[0].Count == 8,
            "Domains without same-Step edges must remain eligible for concurrent execution.");
        var independentWave = independent.ExecutionWaves[0]
            .Select(static entry => entry.DomainToken.Value)
            .ToArray();
        Require(independentWave.SequenceEqual(independentWave.OrderBy(static token => token, StringComparer.Ordinal)),
            "Ready domains must use DomainToken ASCII order, not DomainRank, for deterministic topological selection.");

        var participation = new StableToken("participation");
        var resident = new StableToken("resident");
        var plan = StandardDomainExecutionPlanV1.Create([
            new DomainSameStepDependencyV1(participation, resident),
        ]);
        Require(plan.ExecutionWaves.Count == 2,
            "A same-Step dependency must create a later execution wave for the consumer.");
        Require(plan.ExecutionWaves[0].Any(entry => entry.DomainToken == participation) &&
                plan.ExecutionWaves[1].Count == 1 &&
                plan.ExecutionWaves[1][0].DomainToken == resident,
            "Participation -> Resident dependency was not reflected in the execution DAG.");

        var runtimes = plan.Entries
            .Reverse()
            .Select(entry => (IDomainRuntimeV1)new DependencyCheckingRuntime(
                entry.DomainToken,
                entry.DomainToken == resident ? participation : null))
            .ToArray();

        string? canonicalOutputOrder = null;
        foreach (var workerCount in new[] { 1, 4, 8, 16 })
        {
            var outputs = await DomainRuntimeExecutorV1.ExecuteAsync(
                plan,
                state,
                frozen,
                runtimes,
                workerCount);
            var outputOrder = string.Join('|', outputs.Select(static output => output.DomainToken.Value));
            canonicalOutputOrder ??= outputOrder;
            Require(outputOrder == canonicalOutputOrder,
                $"Dependency-aware domain outputs changed with worker-count={workerCount}.");
        }

        RequireReject(
            () => StandardDomainExecutionPlanV1.Create([
                new DomainSameStepDependencyV1(participation, resident),
                new DomainSameStepDependencyV1(resident, participation),
            ]),
            "step-plan.same-step-dependency-cycle");
        RequireReject(
            () => StandardDomainExecutionPlanV1.Create([
                new DomainSameStepDependencyV1(participation, participation),
            ]),
            "step-plan.same-step-dependency-self-cycle");
    }

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
        return new WorldStateV1(
            new WorldStateHeaderV1(
                worldId,
                step,
                worldSeedDigest: zero,
                configGeneration: 1,
                masterGeneration: 1,
                rateGeneration: 1),
            new OrderedPartitionDirectoryV1(partitions),
            WorldStateV1.EmptySubstate("core.scheduler-state"),
            WorldStateV1.EmptySubstate("core.operation-state"),
            WorldStateV1.EmptySubstate("core.detail-state"),
            WorldStateV1.EmptySubstate("core.domain-registry-state"),
            zero);
    }

    private static void RequireReject(Func<StandardDomainExecutionPlanV1> action, string expectedMessage)
    {
        try
        {
            _ = action();
        }
        catch (InvalidDataException ex) when (ex.Message == expectedMessage)
        {
            return;
        }
        throw new InvalidOperationException($"Expected dependency-plan rejection: {expectedMessage}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class DependencyCheckingRuntime(
        StableToken domainToken,
        StableToken? expectedDependency) : IDomainRuntimeV1
    {
        public StableToken DomainToken { get; } = domainToken;

        public async ValueTask<DomainCandidateOutputV1> ExecuteAsync(
            DomainRuntimeContextV1 context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();

            if (expectedDependency is { } dependency)
            {
                if (!context.CompletedDependencyOutputs.TryGetValue(dependency, out var producer) ||
                    producer.DomainToken != dependency ||
                    producer.BasisStep != context.FrozenInput.BasisStep)
                    throw new InvalidOperationException("Declared same-Step producer output was not visible to the consumer wave.");
            }
            else if (context.CompletedDependencyOutputs.Count != 0)
            {
                throw new InvalidOperationException("Domain without declared same-Step dependencies received dependency outputs.");
            }

            return new DomainCandidateOutputV1(DomainToken, context.FrozenInput.BasisStep);
        }
    }
}
