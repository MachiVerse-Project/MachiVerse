using System.Security.Cryptography;
using System.Text.Json;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public static class Qa04ProcessTargetV1
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var line = await Console.In.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line))
                throw new InvalidDataException("qa04.target.request-line-required");

            var request = JsonSerializer.Deserialize<Qa04ProcessRequestV1>(line, Json)
                ?? throw new InvalidDataException("qa04.target.request-null");
            if (!string.Equals(request.SchemaVersion, "1.0", StringComparison.Ordinal))
                throw new InvalidDataException("qa04.target.schema-version-unsupported");

            object response = request.Command switch
            {
                "inspect" => Inspect(),
                "worker-probe" => await ProbeWorkerAsync(request.WorkerCount, cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidDataException("qa04.target.command-unsupported"),
            };

            await Console.Out.WriteLineAsync(JsonSerializer.Serialize(response, Json)).ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"QA-04 process target FAILED: {ex.Message}").ConfigureAwait(false);
            return 1;
        }
    }

    private static Qa04ProcessInspectionV1 Inspect()
    {
        Qa04ReferenceLoadV1.ValidateCanonicalContract();
        Qa04ReferenceScenariosV1.ValidateCanonicalContract();
        return new Qa04ProcessInspectionV1
        {
            SchemaVersion = "1.0",
            ProfileId = Qa04ReferenceLoadV1.BenchmarkProfileId,
            WorldId = Qa04ReferenceLoadV1.WorldId.ToString(),
            WorldSeedSha256 = Convert.ToHexString(SHA256.HashData(Qa04ReferenceLoadV1.WorldSeed.ToBytes())).ToLowerInvariant(),
            CanonicalWorkerCounts = Qa04DomainExecutionTargetV1.CanonicalWorkerCounts.ToArray(),
            StandardDomainCount = StandardDomainExecutionPlanV1.Create().Entries.Count,
            StandardPartitionCount = StandardDomainPartitionRegistry.StandardPartitionCount,
            ReferenceWorldMaterialized = false,
            AuthoritativeStepLoopAvailable = false,
            ReleaseEvidenceCapable = false,
            BlockingFailureCodes =
            [
                "qa04.target.reference-world-not-materialized",
                "qa04.target.authoritative-step-loop-not-assembled",
            ],
        };
    }

    private static async Task<Qa04ProcessWorkerProbeV1> ProbeWorkerAsync(
        int workerCount,
        CancellationToken cancellationToken)
    {
        var probe = new ProcessConcurrencyProbe(workerCount);
        var runtimes = StandardDomainExecutionPlanV1.Create().Entries
            .Select(entry => (IDomainRuntimeV1)new ProbeDomainRuntime(entry.DomainToken, probe))
            .ToArray();
        var target = new Qa04DomainExecutionTargetV1(workerCount, runtimes);
        var state = CreateProbeWorldState();
        var scheduler = new OperationSchedulerStateV1(0, null, Array.Empty<ScheduledOperationRefV1>());
        var receipt = await target.ExecuteDomainsAsync(state, scheduler, cancellationToken).ConfigureAwait(false);
        return new Qa04ProcessWorkerProbeV1
        {
            SchemaVersion = "1.0",
            ProfileId = Qa04ReferenceLoadV1.BenchmarkProfileId,
            WorkerCount = receipt.WorkerCount,
            DomainCount = receipt.DomainOutputs.Count,
            MaxObservedConcurrency = probe.MaxConcurrency,
            WorkerCountAppliedToDomainExecutor = probe.MaxConcurrency == workerCount,
            ReferenceWorldMaterialized = false,
            ReleaseEvidenceCapable = false,
            BlockingFailureCodes = ["qa04.target.reference-world-not-materialized"],
        };
    }

    private static WorldStateV1 CreateProbeWorldState()
    {
        var seedDigest = SHA256.HashData(Qa04ReferenceLoadV1.WorldSeed.ToBytes());
        var configDigest = SHA256.HashData("qa04-process-target-probe-config"u8);
        var partitions = StandardDomainPartitionRegistry.Entries.Select(entry => new PartitionStateRefV1(
            new PartitionStateHeaderV1(
                entry,
                revision: 1,
                basisStep: 0,
                detailLevel: DetailLevelV1.D0Entity,
                itemCount: 0,
                canonicalDigest: SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(entry.PartitionId.Value)))));
        return new WorldStateV1(
            new WorldStateHeaderV1(
                Qa04ReferenceLoadV1.WorldId,
                step: 0,
                worldSeedDigest: seedDigest,
                configGeneration: 1,
                masterGeneration: 1,
                rateGeneration: 1),
            new OrderedPartitionDirectoryV1(partitions),
            WorldStateV1.EmptySubstate("core.scheduler-state"),
            WorldStateV1.EmptySubstate("core.operation-state"),
            WorldStateV1.EmptySubstate("core.detail-state"),
            WorldStateV1.EmptySubstate("core.domain-registry-state"),
            configDigest);
    }

    private sealed class ProbeDomainRuntime(StableToken domainToken, ProcessConcurrencyProbe probe) : IDomainRuntimeV1
    {
        public StableToken DomainToken { get; } = domainToken;

        public async ValueTask<DomainCandidateOutputV1> ExecuteAsync(
            DomainRuntimeContextV1 context,
            CancellationToken cancellationToken)
        {
            await probe.EnterAsync(cancellationToken).ConfigureAwait(false);
            return new DomainCandidateOutputV1(DomainToken, context.FrozenInput.BasisStep);
        }
    }

    private sealed class ProcessConcurrencyProbe(int releaseAt)
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
                await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
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

public sealed class Qa04ProcessRequestV1
{
    public string SchemaVersion { get; set; } = "";
    public string Command { get; set; } = "";
    public int WorkerCount { get; set; }
}

public sealed class Qa04ProcessInspectionV1
{
    public string SchemaVersion { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public string WorldId { get; set; } = "";
    public string WorldSeedSha256 { get; set; } = "";
    public int[] CanonicalWorkerCounts { get; set; } = [];
    public int StandardDomainCount { get; set; }
    public int StandardPartitionCount { get; set; }
    public bool ReferenceWorldMaterialized { get; set; }
    public bool AuthoritativeStepLoopAvailable { get; set; }
    public bool ReleaseEvidenceCapable { get; set; }
    public string[] BlockingFailureCodes { get; set; } = [];
}

public sealed class Qa04ProcessWorkerProbeV1
{
    public string SchemaVersion { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public int WorkerCount { get; set; }
    public int DomainCount { get; set; }
    public int MaxObservedConcurrency { get; set; }
    public bool WorkerCountAppliedToDomainExecutor { get; set; }
    public bool ReferenceWorldMaterialized { get; set; }
    public bool ReleaseEvidenceCapable { get; set; }
    public string[] BlockingFailureCodes { get; set; } = [];
}
