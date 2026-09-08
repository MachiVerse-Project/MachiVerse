using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04RuntimeTargetSmoke
{
    internal static async Task RunAsync()
    {
        Require(Qa04DomainExecutionTargetV1.CanonicalWorkerCounts.SequenceEqual(new[] { 1, 4, 8, 16 }),
            "QA-04 runtime target worker-count set drifted.");

        var invalidWorkerRejected = false;
        try
        {
            _ = new Qa04DomainExecutionTargetV1(2, CreateProbeRuntimes(new ConcurrencyProbe(1)));
        }
        catch (InvalidDataException ex) when (ex.Message == "qa04.target.worker-count-not-canonical")
        {
            invalidWorkerRejected = true;
        }
        Require(invalidWorkerRejected, "QA-04 runtime target must reject non-canonical worker counts.");

        var workerOneProbe = new ConcurrencyProbe(1);
        var workerOneTarget = new Qa04DomainExecutionTargetV1(1, CreateProbeRuntimes(workerOneProbe));
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
        {
            var receipt = await workerOneTarget.ExecuteDomainsAsync(
                CreateWorldState(Qa04ReferenceLoadV1.WorldId, 0),
                new OperationSchedulerStateV1(0, null, Array.Empty<ScheduledOperationRefV1>()),
                cts.Token);
            Require(receipt.WorkerCount == 1 && receipt.BasisStep == 0 && receipt.DomainOutputs.Count == 8,
                "QA-04 worker=1 receipt mismatch.");
            Require(workerOneProbe.MaxConcurrency == 1,
                "QA-04 worker=1 must serialize domain execution.");
        }

        var workerFourProbe = new ConcurrencyProbe(4);
        var workerFourTarget = new Qa04DomainExecutionTargetV1(4, CreateProbeRuntimes(workerFourProbe));
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
        {
            var receipt = await workerFourTarget.ExecuteDomainsAsync(
                CreateWorldState(Qa04ReferenceLoadV1.WorldId, 0),
                new OperationSchedulerStateV1(0, null, Array.Empty<ScheduledOperationRefV1>()),
                cts.Token);
            Require(receipt.WorkerCount == 4 && receipt.DomainOutputs.Count == 8,
                "QA-04 worker=4 receipt mismatch.");
            Require(workerFourProbe.MaxConcurrency == 4,
                "QA-04 worker=4 did not reach actual domain execution concurrency.");
        }

        var wrongWorldRejected = false;
        try
        {
            var target = new Qa04DomainExecutionTargetV1(1, CreateProbeRuntimes(new ConcurrencyProbe(1)));
            await target.ExecuteDomainsAsync(
                CreateWorldState(OpaqueId128.Parse("00000000000000000000000000000001"), 0),
                new OperationSchedulerStateV1(0, null, Array.Empty<ScheduledOperationRefV1>()));
        }
        catch (InvalidDataException ex) when (ex.Message == "qa04.target.world-id-mismatch")
        {
            wrongWorldRejected = true;
        }
        Require(wrongWorldRejected, "QA-04 runtime target must reject a non-reference WorldId.");
    }

    private static IReadOnlyCollection<IDomainRuntimeV1> CreateProbeRuntimes(ConcurrencyProbe probe)
        => StandardDomainExecutionPlanV1.Create().Entries
            .Select(entry => (IDomainRuntimeV1)new ProbeDomainRuntime(entry.DomainToken, probe))
            .ToArray();

    private static WorldStateV1 CreateWorldState(OpaqueId128 worldId, ulong step)
    {
        var seedDigest = SHA256.HashData(Qa04ReferenceLoadV1.WorldSeed.ToBytes());
        var configDigest = SHA256.HashData("qa04-runtime-target-smoke-config"u8);
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
            seedDigest,
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
            configDigest);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class ProbeDomainRuntime(StableToken domainToken, ConcurrencyProbe probe) : IDomainRuntimeV1
    {
        public StableToken DomainToken { get; } = domainToken;

        public async ValueTask<DomainCandidateOutputV1> ExecuteAsync(
            DomainRuntimeContextV1 context,
            CancellationToken cancellationToken)
        {
            await probe.EnterAsync(cancellationToken);
            return new DomainCandidateOutputV1(DomainToken, context.FrozenInput.BasisStep);
        }
    }

    private sealed class ConcurrencyProbe(int releaseAt)
    {
        private readonly int _releaseAt = releaseAt;
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _active;
        private int _max;

        public int MaxConcurrency => Volatile.Read(ref _max);

        public async ValueTask EnterAsync(CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref _active);
            UpdateMax(active);
            if (active >= _releaseAt) _release.TrySetResult();
            try
            {
                await _release.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        private void UpdateMax(int observed)
        {
            while (true)
            {
                var current = Volatile.Read(ref _max);
                if (observed <= current) return;
                if (Interlocked.CompareExchange(ref _max, observed, current) == current) return;
            }
        }
    }
}
