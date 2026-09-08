using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04RuntimeTargetSmoke
{
    internal static async Task RunAsync()
    {
        VerifyResidentMaterialization();
        await VerifyStructuralAuthorityPathAsync();

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

    private static async Task VerifyStructuralAuthorityPathAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "machiverse-qa04-structural-" + Guid.NewGuid().ToString("N"));
        try
        {
            var probe = await Qa04AuthoritativeStepBridgeV1.RunReducedAsync(
                workerCount: 4,
                residentRecordCount: 32,
                persistenceRoot: root);
            Require(probe.BasisStep == 0 && probe.ResultingStep == 1,
                "QA-04 structural Step bridge did not advance the durable transition from 0 to 1.");
            Require(probe.WorkerCount == 4 && probe.ResidentRecordCount == 32 && probe.DomainOutputCount == 8,
                "QA-04 structural Step bridge execution receipt mismatch.");
            Require(probe.PartitionCandidateCount == 1,
                "QA-04 structural Step bridge must carry the Resident partition candidate through StepCandidate.");
            Require(!probe.CandidatePublishableBeforeCommit && probe.DurableReceiptPublishable,
                "QA-04 structural Step authority boundary drifted.");
            Require(probe.SchedulerReopenedAfterCommit && probe.RealSqliteCommitObserved,
                "QA-04 structural Step bridge did not cross the SQLite COMMIT boundary.");
            Require(probe.CandidateDiagnosticDigest.Length == 64 && probe.CandidateDiagnosticDigest.Any(c => c != '0') &&
                    probe.ResultingContinuityToken.Length == 64 && probe.ResultingContinuityToken.Any(c => c != '0'),
                "QA-04 structural Step digests must be non-zero SHA-256 values.");
            Require(!probe.ReferenceWorldMaterialized && !probe.AuthoritativeStepLoopAvailable && !probe.ReleaseEvidenceCapable,
                "QA-04 reduced structural Step must remain unable to impersonate release execution.");
            Require(probe.BlockingFailureCodes.Contains(
                    "qa04.target.resulting-world-state-materialization-not-assembled",
                    StringComparer.Ordinal),
                "QA-04 structural Step must expose the remaining State(S+1) materialization boundary.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void VerifyResidentMaterialization()
    {
        Qa04ReferenceWorldMaterializerV1.ValidateCanonicalContract();
        Require(Qa04ReferenceWorldMaterializerV1.CanonicalResidentCount == 1_000_000,
            "QA-04 canonical Resident materialization count drifted.");
        Require(Qa04ReferenceWorldMaterializerV1.InitialResidentLifecycle.Value == "alive" &&
                Qa04ReferenceWorldMaterializerV1.ResidentProfileToken.Value == "perf.reference.v1" &&
                Qa04ReferenceWorldMaterializerV1.InitialResidentLineageGeneration == 1,
            "QA-04 Resident genesis payload contract drifted.");

        var first = Qa04ReferenceWorldMaterializerV1.CreateResidentRecord(0);
        Require(first.RecordId == Qa04ReferenceLoadV1.Record(new StableToken("resident.persistent-identity"), 0).RecordId,
            "QA-04 Resident materializer must preserve the canonical derived RecordId.");
        Require(first.Payload.ResidentId == first.RecordId &&
                first.Payload.Lifecycle.Value == "alive" &&
                first.Payload.BirthStep is null && first.Payload.DeathStep is null &&
                first.Payload.ParentRefs.Count == 0 &&
                first.Payload.LineageGeneration == 1 &&
                first.Payload.ProfileToken.Value == "perf.reference.v1",
            "QA-04 Resident genesis payload mismatch.");

        Require(Qa04ReferenceWorldMaterializerV1.CreateResidentRecord(99_999).DetailLevel == DetailLevelV1.D0Entity,
            "QA-04 materialized Resident D0 upper boundary drifted.");
        Require(Qa04ReferenceWorldMaterializerV1.CreateResidentRecord(100_000).DetailLevel == DetailLevelV1.D1LocalAggregate,
            "QA-04 materialized Resident D1 lower boundary drifted.");
        Require(Qa04ReferenceWorldMaterializerV1.CreateResidentRecord(400_000).DetailLevel == DetailLevelV1.D2RegionalAggregate,
            "QA-04 materialized Resident D2 lower boundary drifted.");
        Require(Qa04ReferenceWorldMaterializerV1.CreateResidentRecord(800_000).DetailLevel == DetailLevelV1.D3BoundarySummary,
            "QA-04 materialized Resident D3 lower boundary drifted.");

        var materializedA = Qa04ReferenceWorldMaterializerV1.MaterializeResidentIdentityLifecycle(128);
        var materializedB = Qa04ReferenceWorldMaterializerV1.MaterializeResidentIdentityLifecycle(128);
        Require(materializedA.MaterializedRecordCount == 128 &&
                materializedA.Partition.ItemCount == 128 &&
                materializedA.Partition.RecordsCanonical.Count() == 128 &&
                materializedA.PartitionHeader.ItemCount == 128 &&
                materializedA.WorldState.Partitions.Get("resident.identity_lifecycle").Header.ItemCount == 128,
            "QA-04 Resident materialization must derive item counts from real records.");
        Require(materializedA.D0Count == 128 && materializedA.D1Count == 0 &&
                materializedA.D2Count == 0 && materializedA.D3Count == 0,
            "QA-04 partial Resident detail-count receipt mismatch.");
        Require(!materializedA.CanonicalResidentPopulationComplete,
            "QA-04 reduced structural materialization must not impersonate the full Resident population.");
        Require(materializedA.PartitionHeader.CanonicalDigest.SequenceEqual(materializedB.PartitionHeader.CanonicalDigest) &&
                materializedA.WorldState.Diagnostic.StateDigest.SequenceEqual(materializedB.WorldState.Diagnostic.StateDigest),
            "QA-04 Resident materialization digest must be deterministic.");

        var invalidCountRejected = false;
        try
        {
            _ = Qa04ReferenceWorldMaterializerV1.MaterializeResidentIdentityLifecycle(0);
        }
        catch (ArgumentOutOfRangeException)
        {
            invalidCountRejected = true;
        }
        Require(invalidCountRejected,
            "QA-04 Resident materializer must reject an empty population request.");
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
