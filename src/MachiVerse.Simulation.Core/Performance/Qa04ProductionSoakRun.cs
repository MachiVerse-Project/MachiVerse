using System.Diagnostics;
using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Persistence;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04ProductionSoakRunResultV1(
    string SchemaVersion,
    string TestCaseId,
    bool ReleaseMode,
    long DurationSeconds,
    int CanonicalCycleCount,
    int SnapshotRecoveryCheckpointCount,
    int ParallelVerifierCheckpointCount,
    bool ParallelVerifierDigestMatched,
    double MaxPostWarmupMemoryGrowthPercent,
    int AcceptedOperationLoss,
    bool HiddenSolverIterationReduction,
    bool HistoryChainValid,
    bool NoUnrecoverableQueueDeadlock,
    string FinalStateDigest,
    bool Passed,
    IReadOnlyList<string> FailureCodes);

public static class Qa04ProductionSoakRunV1
{
    public const long ReleaseDurationSeconds = 86_400;
    private const int ReleaseWorkerCount = 16;
    private static readonly TimeSpan ProgressDeadlockLimit = TimeSpan.FromMinutes(30);

    public static async Task<Qa04ProductionSoakRunResultV1> RunAsync(
        string persistenceRoot,
        long requestedDurationSeconds,
        bool releaseMode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(persistenceRoot))
            throw new ArgumentException("persistenceRoot is required.", nameof(persistenceRoot));
        if (releaseMode && requestedDurationSeconds != ReleaseDurationSeconds)
            throw new InvalidDataException("qa04.soak.release-duration-must-be-86400");
        if (!releaseMode && requestedDurationSeconds is < 1 or > 60)
            throw new InvalidDataException("qa04.soak.contract-duration-out-of-range");

        Directory.CreateDirectory(Path.GetFullPath(persistenceRoot));
        if (!releaseMode)
            return await RunContractProbeAsync(
                persistenceRoot,
                requestedDurationSeconds,
                cancellationToken).ConfigureAwait(false);

        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var monitorStop = new CancellationTokenSource();
        var started = Stopwatch.StartNew();
        var lastProgressTimestamp = Stopwatch.GetTimestamp();
        var deadlockDetected = 0;
        var warm = 0;
        long baselineMemory = 0;
        long maxPostWarmupMemory = 0;

        void Progress()
            => Interlocked.Exchange(ref lastProgressTimestamp, Stopwatch.GetTimestamp());

        var sampler = Task.Run(async () =>
        {
            using var process = Process.GetCurrentProcess();
            try
            {
                while (!monitorStop.Token.IsCancellationRequested)
                {
                    process.Refresh();
                    var workingSet = process.WorkingSet64;
                    if (Volatile.Read(ref warm) != 0)
                        UpdateMaximum(ref maxPostWarmupMemory, workingSet);
                    await Task.Delay(TimeSpan.FromSeconds(10), monitorStop.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (monitorStop.IsCancellationRequested)
            {
            }
        });

        var watchdog = Task.Run(async () =>
        {
            try
            {
                while (!monitorStop.Token.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), monitorStop.Token).ConfigureAwait(false);
                    var last = Volatile.Read(ref lastProgressTimestamp);
                    if (Stopwatch.GetElapsedTime(last) <= ProgressDeadlockLimit)
                        continue;
                    Interlocked.Exchange(ref deadlockDetected, 1);
                    runCancellation.Cancel();
                    return;
                }
            }
            catch (OperationCanceledException) when (monitorStop.IsCancellationRequested)
            {
            }
        });

        var cycleCount = 0;
        var snapshotCheckpointCount = 0;
        var parallelCheckpointCount = 0;
        var acceptedOperationLoss = 0;
        var hiddenReduction = false;
        var historyChainValid = true;
        var parallelMatched = true;
        string? canonicalFinalDigest = null;
        var failures = new SortedSet<string>(StringComparer.Ordinal);

        try
        {
            while (started.Elapsed.TotalSeconds < requestedDurationSeconds)
            {
                runCancellation.Token.ThrowIfCancellationRequested();
                var cycleRoot = Path.Combine(persistenceRoot, $"cycle-{cycleCount + 1:000000}");
                var observer = new Qa04ParallelStateDigestObserverV1(Progress);
                Progress();
                Qa04ProductionReferenceRunResultV1 run;
                try
                {
                    run = await Qa04ProductionReferenceRunV1.RunCanonicalAsync(
                        ReleaseWorkerCount,
                        cycleRoot,
                        runCancellation.Token,
                        observer).ConfigureAwait(false);
                }
                finally
                {
                    Progress();
                }

                await observer.CompleteAsync(runCancellation.Token).ConfigureAwait(false);
                parallelCheckpointCount = checked(parallelCheckpointCount + observer.CheckpointCount);
                if (observer.CheckpointCount == 0 || observer.MismatchCount != 0)
                {
                    parallelMatched = false;
                    failures.Add("parallel-verifier-digest");
                }

                if (!run.SnapshotDrainCompleted ||
                    run.SnapshotSectionCount != 103 ||
                    run.SnapshotChunkCount <= 0 ||
                    run.SnapshotRecoveredStateDigest.Length != 64 ||
                    run.SnapshotDigest.Length != 64 ||
                    run.SnapshotPhysicalManifestDigest.Length != 64)
                    failures.Add("snapshot-recovery-checkpoint");
                snapshotCheckpointCount++;

                acceptedOperationLoss = checked(acceptedOperationLoss + run.AcceptedOperationLoss);
                hiddenReduction |= run.HiddenSolverIterationReduction;
                if (run.AcceptedOperationLoss != 0)
                    failures.Add("accepted-operation-loss");
                if (run.HiddenSolverIterationReduction)
                    failures.Add("hidden-solver-reduction");

                if (canonicalFinalDigest is null)
                    canonicalFinalDigest = run.FinalStateDigest;
                else if (!string.Equals(canonicalFinalDigest, run.FinalStateDigest, StringComparison.Ordinal))
                {
                    parallelMatched = false;
                    failures.Add("cycle-final-state-digest-mismatch");
                }

                var paths = PersistenceLayout.Resolve(cycleRoot, Qa04ReferenceLoadV1.WorldId, 1);
                await using (var reopened = await SqlitePersistenceStore
                    .OpenOrCreateAsync(paths, runCancellation.Token)
                    .ConfigureAwait(false))
                {
                    await reopened.ValidateQuickCheckAsync(runCancellation.Token).ConfigureAwait(false);
                    _ = await reopened.ValidateHistoryHashChainStructureAsync(runCancellation.Token).ConfigureAwait(false);
                    var recovery = await reopened.ReadRecoveryHeadAsync(runCancellation.Token).ConfigureAwait(false);
                    if (recovery.FinalizedStep != run.FinalizedStep)
                        throw new InvalidDataException("qa04.soak.reopened-recovery-step-drift");
                }

                cycleCount++;
                if (cycleCount == 1)
                {
                    using var process = Process.GetCurrentProcess();
                    process.Refresh();
                    baselineMemory = process.WorkingSet64;
                    maxPostWarmupMemory = baselineMemory;
                    Volatile.Write(ref warm, 1);
                }
                Progress();

                try
                {
                    Directory.Delete(cycleRoot, recursive: true);
                }
                catch (IOException)
                {
                    failures.Add("cycle-persistence-cleanup");
                }
            }
        }
        catch (OperationCanceledException) when (
            Volatile.Read(ref deadlockDetected) != 0 &&
            !cancellationToken.IsCancellationRequested)
        {
            failures.Add("unrecoverable-queue-deadlock");
            throw new InvalidDataException("qa04.soak.progress-deadlock");
        }
        finally
        {
            monitorStop.Cancel();
            await Task.WhenAll(sampler, watchdog).ConfigureAwait(false);
        }

        var growthPercent = baselineMemory <= 0
            ? 100.0
            : Math.Max(
                0.0,
                (maxPostWarmupMemory - baselineMemory) * 100.0 / baselineMemory);
        if (growthPercent > 10.0)
            failures.Add("post-warmup-memory-growth");
        if (cycleCount == 0)
            failures.Add("canonical-cycle-missing");
        if (snapshotCheckpointCount != cycleCount)
            failures.Add("snapshot-checkpoint-count");
        if (parallelCheckpointCount == 0)
            failures.Add("parallel-verifier-checkpoint-missing");

        var durationSeconds = (long)Math.Floor(started.Elapsed.TotalSeconds);
        if (durationSeconds < ReleaseDurationSeconds)
            failures.Add("soak-duration-short");

        var passed = failures.Count == 0 &&
            acceptedOperationLoss == 0 &&
            !hiddenReduction &&
            historyChainValid &&
            parallelMatched &&
            Volatile.Read(ref deadlockDetected) == 0;

        return new Qa04ProductionSoakRunResultV1(
            "1.0",
            "performance.soak.24h",
            ReleaseMode: true,
            DurationSeconds: durationSeconds,
            CanonicalCycleCount: cycleCount,
            SnapshotRecoveryCheckpointCount: snapshotCheckpointCount,
            ParallelVerifierCheckpointCount: parallelCheckpointCount,
            ParallelVerifierDigestMatched: parallelMatched,
            MaxPostWarmupMemoryGrowthPercent: growthPercent,
            AcceptedOperationLoss: acceptedOperationLoss,
            HiddenSolverIterationReduction: hiddenReduction,
            HistoryChainValid: historyChainValid,
            NoUnrecoverableQueueDeadlock: Volatile.Read(ref deadlockDetected) == 0,
            FinalStateDigest: canonicalFinalDigest ?? new string('0', 64),
            Passed: passed,
            FailureCodes: failures.ToArray());
    }

    private static async Task<Qa04ProductionSoakRunResultV1> RunContractProbeAsync(
        string persistenceRoot,
        long requestedDurationSeconds,
        CancellationToken cancellationToken)
    {
        var probeRoot = Path.Combine(persistenceRoot, "contract-probe");
        var started = Stopwatch.StartNew();
        var probe = await Qa04ProductionReferenceConnectionProbeV1.RunAsync(
            workerCount: 1,
            persistenceRoot: probeRoot,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var remaining = TimeSpan.FromSeconds(requestedDurationSeconds) - started.Elapsed;
        if (remaining > TimeSpan.Zero)
            await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);

        return new Qa04ProductionSoakRunResultV1(
            "1.0",
            "performance.soak.24h",
            ReleaseMode: false,
            DurationSeconds: (long)Math.Floor(started.Elapsed.TotalSeconds),
            CanonicalCycleCount: 0,
            SnapshotRecoveryCheckpointCount: 0,
            ParallelVerifierCheckpointCount: 0,
            ParallelVerifierDigestMatched: false,
            MaxPostWarmupMemoryGrowthPercent: 0,
            AcceptedOperationLoss: 0,
            HiddenSolverIterationReduction: false,
            HistoryChainValid: probe.RealSqliteCommitObserved,
            NoUnrecoverableQueueDeadlock: true,
            FinalStateDigest: probe.FinalStateDigest,
            Passed: false,
            FailureCodes: ["qa04.soak.contract-smoke-not-release"]);
    }

    private static void UpdateMaximum(ref long location, long value)
    {
        while (true)
        {
            var current = Volatile.Read(ref location);
            if (value <= current) return;
            if (Interlocked.CompareExchange(ref location, value, current) == current) return;
        }
    }
}
