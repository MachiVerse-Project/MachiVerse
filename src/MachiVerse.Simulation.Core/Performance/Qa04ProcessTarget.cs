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
                "resident-materialize-probe" => MaterializeResident(request.RecordCount),
                "authoritative-step-probe" => await Qa04AuthoritativeStepBridgeV1.RunReducedAsync(
                    request.WorkerCount,
                    request.RecordCount,
                    request.PersistenceRoot,
                    cancellationToken).ConfigureAwait(false),
                "core-substate-two-step-probe" => await Qa04CoreSubstateAuthorityBridgeV1.RunTwoStepAsync(
                    request.WorkerCount,
                    request.RecordCount,
                    request.PersistenceRoot,
                    cancellationToken).ConfigureAwait(false),
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
        Qa04ReferenceWorldMaterializerV1.ValidateCanonicalContract();
        return new Qa04ProcessInspectionV1
        {
            SchemaVersion = "1.0",
            ProfileId = Qa04ReferenceLoadV1.BenchmarkProfileId,
            WorldId = Qa04ReferenceLoadV1.WorldId.ToString(),
            WorldSeedSha256 = Convert.ToHexString(SHA256.HashData(Qa04ReferenceLoadV1.WorldSeed.ToBytes())).ToLowerInvariant(),
            CanonicalWorkerCounts = Qa04DomainExecutionTargetV1.CanonicalWorkerCounts.ToArray(),
            StandardDomainCount = StandardDomainExecutionPlanV1.Create().Entries.Count,
            StandardPartitionCount = StandardDomainPartitionRegistry.StandardPartitionCount,
            ResidentIdentityMaterializationAvailable = true,
            CanonicalResidentCount = Qa04ReferenceWorldMaterializerV1.CanonicalResidentCount,
            InitialResidentLifecycle = Qa04ReferenceWorldMaterializerV1.InitialResidentLifecycle.Value,
            AuthoritativeStepStructuralBridgeAvailable = true,
            CoreSubstateTwoStepBridgeAvailable = true,
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

    private static Qa04ProcessResidentMaterializationV1 MaterializeResident(ulong recordCount)
    {
        var result = Qa04ReferenceWorldMaterializerV1.MaterializeResidentIdentityLifecycle(recordCount);
        return new Qa04ProcessResidentMaterializationV1
        {
            SchemaVersion = "1.0",
            ProfileId = Qa04ReferenceLoadV1.BenchmarkProfileId,
            PartitionId = result.PartitionHeader.PartitionId.Value,
            RequestedRecordCount = recordCount,
            MaterializedRecordCount = result.MaterializedRecordCount,
            CanonicalResidentCount = Qa04ReferenceWorldMaterializerV1.CanonicalResidentCount,
            D0Count = result.D0Count,
            D1Count = result.D1Count,
            D2Count = result.D2Count,
            D3Count = result.D3Count,
            InitialLifecycle = Qa04ReferenceWorldMaterializerV1.InitialResidentLifecycle.Value,
            BirthStepSpecified = false,
            LineageGeneration = Qa04ReferenceWorldMaterializerV1.InitialResidentLineageGeneration,
            ProfileToken = Qa04ReferenceWorldMaterializerV1.ResidentProfileToken.Value,
            PartitionDigest = Convert.ToHexString(result.PartitionHeader.CanonicalDigest).ToLowerInvariant(),
            StateDigest = Convert.ToHexString(result.WorldState.Diagnostic.StateDigest).ToLowerInvariant(),
            CanonicalResidentPopulationComplete = result.CanonicalResidentPopulationComplete,
            ReferenceWorldMaterialized = false,
            ReleaseEvidenceCapable = false,
            BlockingFailureCodes =
            [
                "qa04.target.reference-world-other-partitions-not-materialized",
                "qa04.target.authoritative-step-loop-not-assembled",
            ],
        };
    }

    private static async Task<Qa04ProcessWorkerProbeV1> ProbeWorkerAsync(
        int workerCount,
        CancellationToken cancellationToken)
    {
        var plan = StandardDomainExecutionPlanV1.Create();
        var expectedConcurrency = Math.Min(workerCount, plan.Entries.Count);
        var probe = new ProcessConcurrencyProbe(expectedConcurrency);
        var runtimes = plan.Entries
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
            WorkerCountAppliedToDomainExecutor = receipt.WorkerCount == workerCount && probe.MaxConcurrency == expectedConcurrency,
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
    public ulong RecordCount { get; set; }
    public string PersistenceRoot { get; set; } = "";
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
    public bool ResidentIdentityMaterializationAvailable { get; set; }
    public ulong CanonicalResidentCount { get; set; }
    public string InitialResidentLifecycle { get; set; } = "";
    public bool AuthoritativeStepStructuralBridgeAvailable { get; set; }
    public bool CoreSubstateTwoStepBridgeAvailable { get; set; }
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

public sealed class Qa04ProcessResidentMaterializationV1
{
    public string SchemaVersion { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public string PartitionId { get; set; } = "";
    public ulong RequestedRecordCount { get; set; }
    public ulong MaterializedRecordCount { get; set; }
    public ulong CanonicalResidentCount { get; set; }
    public ulong D0Count { get; set; }
    public ulong D1Count { get; set; }
    public ulong D2Count { get; set; }
    public ulong D3Count { get; set; }
    public string InitialLifecycle { get; set; } = "";
    public bool BirthStepSpecified { get; set; }
    public uint LineageGeneration { get; set; }
    public string ProfileToken { get; set; } = "";
    public string PartitionDigest { get; set; } = "";
    public string StateDigest { get; set; } = "";
    public bool CanonicalResidentPopulationComplete { get; set; }
    public bool ReferenceWorldMaterialized { get; set; }
    public bool ReleaseEvidenceCapable { get; set; }
    public string[] BlockingFailureCodes { get; set; } = [];
}
