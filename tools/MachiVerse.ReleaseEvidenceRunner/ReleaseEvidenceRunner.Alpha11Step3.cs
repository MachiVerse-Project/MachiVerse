using System.Diagnostics;
using System.Text;
using System.Text.Json;

internal static partial class ReleaseEvidenceRunner
{
    private static double Alpha11TargetTickRateHz => Alpha11AcceptanceConfig.Current.TickRateHz;
    private static double Alpha11StepDeadlineMilliseconds => Alpha11AcceptanceConfig.Current.DeadlineMilliseconds;
    private static double Alpha11DeadlineMissRatioMax => Alpha11AcceptanceConfig.Current.DeadlineMissRatioMax;
    private static int[] Alpha11WorkerProfiles => Alpha11AcceptanceConfig.Current.WorkerCounts;
    private static int Alpha11RunsPerWorker => Alpha11AcceptanceConfig.Current.RunsPerWorker;
    private static int Alpha11RunCount => Alpha11AcceptanceConfig.Current.RunCount;

    /// <summary>
    /// Alpha 1.1 Gate 4 Step 3 runner. The historical reference plan still contains workers
    /// 1/4/8/16 so older evidence remains reproducible, but Alpha 1.1 release acceptance executes
    /// only workers 8 and 16, three independent runs each. Worker count is an execution-width
    /// setting, not proof of physical CPU core count; host topology is captured separately.
    /// </summary>
    internal static async Task<int> RunGate4Step3Alpha11Async(
        string repositoryRoot,
        string executionClass,
        string sourceCommit,
        string adapterExecutable,
        string planDirectory,
        string outputDirectory)
    {
        ValidateStageInputs(executionClass, sourceCommit, adapterExecutable, planDirectory);

        var manifestPath = Path.Combine(repositoryRoot, "tests", "performance-fixtures", "v1", "harness-manifest.json");
        if (!string.Equals(Program.Sha256File(manifestPath), Program.CanonicalQa04ManifestSha256, StringComparison.Ordinal))
            throw new InvalidDataException("Current QA-04 manifest is not the canonical release profile.");

        var benchmarkSummary = RequirePlanJson(planDirectory, "benchmark-summary.json");
        var runMatrixPath = Path.Combine(planDirectory, "reference-run-matrix.json");
        if (!File.Exists(runMatrixPath))
            throw new InvalidDataException("Missing materialized reference-run-matrix.json.");

        var sourceRuns = Program.ReadJson<BenchmarkRunDescriptor[]>(runMatrixPath, "QA-04 run matrix");
        var runs = SelectAlpha11Runs(sourceRuns);
        var host = CaptureAlpha11HostEvidence(executionClass);

        Directory.CreateDirectory(outputDirectory);
        var reportsDirectory = Path.Combine(outputDirectory, "reports");
        Directory.CreateDirectory(reportsDirectory);
        var observations = new List<BenchmarkRunObservation>(runs.Length);
        var orderedRuns = runs.OrderBy(static x => x.WorkerCount).ThenBy(static x => x.RunOrdinal).ToArray();
        var heartbeatSeconds = Step3HeartbeatSeconds();

        Console.Error.WriteLine(
            $"GATE4_STEP3_ALPHA11_START total_runs={orderedRuns.Length} target_tick_rate_hz={Alpha11TargetTickRateHz:F1} " +
            $"deadline_ms={Alpha11StepDeadlineMilliseconds:F1} max_deadline_miss_ratio={Alpha11DeadlineMissRatioMax:F4} " +
            $"host_logical_processors={host.ProcessVisibleLogicalProcessorCount} heartbeat_seconds={heartbeatSeconds}");

        var completedRuns = 0;
        foreach (var run in orderedRuns)
        {
            var matrixOrdinal = completedRuns + 1;
            Console.Error.WriteLine(
                $"GATE4_STEP3_ALPHA11_RUN_START matrix_run={matrixOrdinal}/{orderedRuns.Length} " +
                $"run_id={run.RunId} workers={run.WorkerCount} repetition={run.RunOrdinal}");

            var request = NewRequest(
                "benchmark-run",
                executionClass,
                run.RunId,
                sourceCommit,
                ReferenceProfile,
                benchmarkSummary,
                run);
            var invocation = await InvokeAdapterAsync(adapterExecutable, request);
            ValidateResponse(invocation.Response, request, "performance-benchmark-report-v1");
            var artifact = WriteResponseArtifact(outputDirectory, reportsDirectory, run.RunId, invocation.Response);
            observations.Add(ParseBenchmarkObservation(run, invocation.Response, artifact.Ref, artifact.Digest));

            completedRuns++;
            Console.Error.WriteLine(
                $"GATE4_STEP3_ALPHA11_RUN_COMPLETE matrix_run={completedRuns}/{orderedRuns.Length} " +
                $"run_id={run.RunId} workers={run.WorkerCount} repetition={run.RunOrdinal} " +
                $"elapsed_seconds={invocation.Elapsed.TotalSeconds:F1}");
        }

        var aggregate = EvaluateAlpha11ReferenceProfile(observations, executionClass, sourceCommit, host);
        var aggregatePath = Path.Combine(reportsDirectory, "perf.reference.v1.alpha11.aggregate.json");
        var aggregateDigest = Program.WriteJson(aggregatePath, aggregate);
        var referenceEvidence = new PerformanceReportEvidence
        {
            ProfileId = ReferenceProfile,
            SourceCommit = sourceCommit,
            ReportRef = RelativeRef(outputDirectory, aggregatePath),
            ReportDigest = aggregateDigest,
            Passed = aggregate.Passed,
            FailureCodes = aggregate.FailureCodes,
        };

        var evidence = new Gate4Step3BenchmarkEvidence
        {
            SchemaVersion = SchemaVersion,
            ExecutionClass = executionClass,
            SourceCommit = sourceCommit,
            Qa04ManifestSha256 = Program.CanonicalQa04ManifestSha256,
            AcceptanceProfile = Alpha11AcceptanceConfig.Current.AcceptanceProfile,
            AcceptanceConfigSha256 = Alpha11AcceptanceConfig.Current.Sha256,
            BenchmarkProfileId = ReferenceProfile,
            ReferenceProfile = referenceEvidence,
            DeterminismDigestSummary = aggregate.DeterminismDigestSummary,
            Passed = aggregate.Passed,
            FailureCodes = aggregate.FailureCodes,
        };
        var evidencePath = Path.Combine(outputDirectory, "gate4-step3-benchmark-evidence.json");
        Program.WriteJson(evidencePath, evidence);

        Console.WriteLine($"Gate4 Step3 Alpha 1.1 evidence: {evidencePath}");
        Console.WriteLine($"Execution class: {executionClass}");
        Console.WriteLine($"Source commit: {sourceCommit}");
        Console.WriteLine($"10Hz reference profiles: {(aggregate.Passed ? "PASS" : "FAIL")}");
        Console.WriteLine($"Determinism digest summary: {aggregate.DeterminismDigestSummary}");

        return aggregate.Passed ? 0 : 2;
    }

    private static BenchmarkRunDescriptor[] SelectAlpha11Runs(BenchmarkRunDescriptor[] sourceRuns)
    {
        ArgumentNullException.ThrowIfNull(sourceRuns);
        if (sourceRuns.Select(static x => x.RunId).Distinct(StringComparer.Ordinal).Count() != sourceRuns.Length)
            throw new InvalidDataException("QA-04 source run matrix contains duplicate RunId values.");

        var selected = sourceRuns
            .Where(run => Alpha11WorkerProfiles.Contains(run.WorkerCount))
            .OrderBy(static run => run.WorkerCount)
            .ThenBy(static run => run.RunOrdinal)
            .ToArray();

        if (selected.Length != Alpha11RunCount)
            throw new InvalidDataException($"Alpha 1.1 Step3 matrix must contain {Alpha11RunCount} selected descriptors, found {selected.Length}.");

        foreach (var worker in Alpha11WorkerProfiles)
        {
            var workerRuns = selected.Where(run => run.WorkerCount == worker).ToArray();
            if (workerRuns.Length != Alpha11RunsPerWorker || !workerRuns.Select(static run => run.RunOrdinal).SequenceEqual(Enumerable.Range(1, Alpha11RunsPerWorker)))
                throw new InvalidDataException($"Alpha 1.1 Step3 worker {worker} repetition matrix is incomplete.");
        }

        foreach (var run in selected)
        {
            if (!string.Equals(run.BenchmarkProfileId, ReferenceProfile, StringComparison.Ordinal))
                throw new InvalidDataException($"Unexpected benchmark profile in run {run.RunId}: {run.BenchmarkProfileId}.");
            if (run.WarmupSteps != Alpha11AcceptanceConfig.Current.WarmupSteps || run.MeasurementSteps != Alpha11AcceptanceConfig.Current.MeasurementSteps)
                throw new InvalidDataException($"Run {run.RunId} does not preserve canonical warmup/measurement Steps.");
        }

        return selected;
    }

    private static Alpha11BenchmarkAggregateArtifact EvaluateAlpha11ReferenceProfile(
        IReadOnlyList<BenchmarkRunObservation> observations,
        string executionClass,
        string sourceCommit,
        Alpha11HostEvidence host,
        JsonElement? persistenceCriteria = null)
    {
        var failures = new SortedSet<string>(StringComparer.Ordinal);
        var criteria = persistenceCriteria ?? ReadAlpha11PersistenceCriteria();
        var sqliteP95Limit = RequirePositiveCriterion(criteria, "sqliteCommitP95Ms");
        var sqliteP99Limit = RequirePositiveCriterion(criteria, "sqliteCommitP99Ms");
        var snapshotP95Limit = RequirePositiveCriterion(criteria, "snapshotCowBarrierP95Ms");
        if (observations.Count != Alpha11RunCount)
            failures.Add("alpha11-reference-run-count");

        foreach (var worker in Alpha11WorkerProfiles)
        {
            var profileRuns = observations
                .Where(run => run.WorkerCount == worker)
                .OrderBy(static run => run.RunOrdinal)
                .ToArray();
            if (profileRuns.Length != Alpha11RunsPerWorker || !profileRuns.Select(static run => run.RunOrdinal).SequenceEqual(Enumerable.Range(1, Alpha11RunsPerWorker)))
                failures.Add($"alpha11-worker-{worker}-run-matrix");
        }

        if (observations.Select(static x => x.RunId).Distinct(StringComparer.Ordinal).Count() != observations.Count ||
            observations.Any(x => !Alpha11WorkerProfiles.Contains(x.WorkerCount)))
            failures.Add("alpha11-reference-run-matrix");

        foreach (var observation in observations)
        {
            if (!observation.MeasurementEvidenceComplete) failures.Add("measurement-evidence-incomplete");
            if (!observation.TargetPassed)
                failures.Add("target-report-failed");
            foreach (var code in observation.TargetFailureCodes)
                failures.Add($"target:{code}");

            if (!string.Equals(observation.AcceptanceConfigSha256, Alpha11AcceptanceConfig.Current.Sha256, StringComparison.Ordinal))
                failures.Add("acceptance-config-mismatch");
            if (observation.StepSampleCount != Alpha11AcceptanceConfig.Current.MeasurementSteps)
                failures.Add("step-sample-count");
            if (!double.IsFinite(observation.StepDeadlineMilliseconds) || observation.StepDeadlineMilliseconds != Alpha11StepDeadlineMilliseconds)
                failures.Add("step-deadline-contract");
            if (observation.StepDeadlineMissCount < 0 || observation.StepDeadlineMissCount > observation.StepSampleCount)
                failures.Add("step-deadline-miss-count");
            if (!double.IsFinite(observation.StepDeadlineMissRatio) || observation.StepSampleCount <= 0 ||
                observation.StepDeadlineMissRatio != observation.StepDeadlineMissCount / (double)observation.StepSampleCount)
                failures.Add("step-deadline-miss-ratio-drift");
            if (observation.StepDeadlineMissRatio > Alpha11DeadlineMissRatioMax)
                failures.Add("step-deadline-miss-ratio");
            if (observation.CoreWorkingSetSampleCount <= 0 || observation.MaxMemoryBytes < 0)
                failures.Add("memory-sample-invalid");
            if (observation.MaxMemoryBytes > Alpha11AcceptanceConfig.Current.CoreSteadyTargetBytes)
                failures.Add("memory-target");
            if (observation.MaxMemoryBytes > Alpha11AcceptanceConfig.Current.CoreHardGuardBytes)
                failures.Add("memory-guard");
            if (observation.PersistenceMetricObserverFailureCount != 0)
                failures.Add("persistence-observer-failure");
            if (!double.IsFinite(observation.SqliteCommitP95Ms) || observation.SqliteCommitP95Ms < 0 || observation.SqliteCommitP95Ms > sqliteP95Limit)
                failures.Add("sqlite-commit-p95");
            if (!double.IsFinite(observation.SqliteCommitP99Ms) || observation.SqliteCommitP99Ms < 0 || observation.SqliteCommitP99Ms > sqliteP99Limit)
                failures.Add("sqlite-commit-p99");
            if (!double.IsFinite(observation.SnapshotCowBarrierP95Ms) || observation.SnapshotCowBarrierP95Ms < 0 || observation.SnapshotCowBarrierP95Ms > snapshotP95Limit)
                failures.Add("snapshot-cow-p95");
            if (!double.IsFinite(observation.StepP99Ms) || observation.StepP99Ms < 0 || observation.StepP99Ms > Alpha11StepDeadlineMilliseconds)
                failures.Add("step-p99");
            if (observation.AcceptedOperationLoss != 0)
                failures.Add("accepted-operation-loss");
            if (observation.HiddenSolverIterationReduction)
                failures.Add("hidden-solver-reduction");

            if (string.Equals(executionClass, "release", StringComparison.Ordinal))
            {
                if (!observation.CpuParallelismMeasured)
                    failures.Add("cpu-parallelism-unmeasured");
                if (observation.ConfiguredWorkerCount != observation.WorkerCount)
                    failures.Add("cpu-worker-configured-mismatch");
                if (observation.EffectiveWorkerCount != observation.WorkerCount)
                    failures.Add("cpu-worker-effective-mismatch");
                if (!observation.WorkerBudgetApplied)
                    failures.Add("cpu-worker-budget-not-applied");
                if (!observation.ParallelExecutionObserved)
                    failures.Add("cpu-parallelism-not-observed");
                if (observation.MaxObservedCpuConcurrency < 1 ||
                    observation.MaxObservedCpuConcurrency > observation.WorkerCount)
                    failures.Add("cpu-max-observed-invalid");
            }
        }

        if (string.Equals(executionClass, "release", StringComparison.Ordinal) &&
            host.ProcessVisibleLogicalProcessorCount < Alpha11WorkerProfiles.Max())
            failures.Add("cpu-profile-logical-processor-capacity");

        var stateDigests = observations
            .Select(static x => x.FinalStateDigest)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (stateDigests.Length != 1)
            failures.Add("state-digest-mismatch");
        foreach (var digest in stateDigests)
            Program.RequireLowerHex(digest, 64, "final_state_digest");

        var digestRows = observations.Where(static x => x.Determinism is not null).Select(static x => x.Determinism!).ToArray();
        if (observations.Any(x => x.Determinism is { } d && (d.RunId != x.RunId || d.FinalStateDigest != x.FinalStateDigest)))
            throw new InvalidDataException("Alpha 1.1 determinism evidence run/State identity drifted.");
        if (digestRows.Length != Alpha11RunCount) failures.Add("determinism-evidence-incomplete");
        var determinismDigest = digestRows.Length == Alpha11RunCount
            ? Qa04DeterminismEvidenceVerifier.ValidateAndSummarize(digestRows, Alpha11RunCount) : "";

        var profiles = Alpha11WorkerProfiles.Select(worker =>
        {
            var profileRuns = observations.Where(run => run.WorkerCount == worker).ToArray();
            return new Alpha11ExecutionProfileEvidence
            {
                WorkerCount = worker,
                RunCount = profileRuns.Length,
                WorstRunP99Milliseconds = profileRuns.Select(static run => run.StepP99Ms).DefaultIfEmpty(double.PositiveInfinity).Max(),
                TargetDeadlineMissRatioMax = Alpha11DeadlineMissRatioMax,
                WorstDeadlineMissRatio = profileRuns.Select(static run => run.StepDeadlineMissRatio).DefaultIfEmpty(double.PositiveInfinity).Max(),
                DeadlineMissRatioVerifiedByTarget = profileRuns.Length == Alpha11RunsPerWorker && profileRuns.All(run =>
                    run.StepSampleCount == Alpha11AcceptanceConfig.Current.MeasurementSteps &&
                    run.StepDeadlineMilliseconds == Alpha11StepDeadlineMilliseconds &&
                    run.StepDeadlineMissCount >= 0 && run.StepDeadlineMissCount <= run.StepSampleCount &&
                    double.IsFinite(run.StepDeadlineMissRatio) &&
                    run.StepDeadlineMissRatio == run.StepDeadlineMissCount / (double)run.StepSampleCount &&
                    run.StepDeadlineMissRatio <= Alpha11DeadlineMissRatioMax),
                AllRunsPassed = profileRuns.Length == Alpha11RunsPerWorker && profileRuns.All(static run => run.TargetPassed),
            };
        }).ToArray();

        return new Alpha11BenchmarkAggregateArtifact
        {
            ExecutionClass = executionClass,
            SourceCommit = sourceCommit,
            Qa04ManifestSha256 = Program.CanonicalQa04ManifestSha256,
            AcceptanceProfile = Alpha11AcceptanceConfig.Current.AcceptanceProfile,
            AcceptanceConfigSha256 = Alpha11AcceptanceConfig.Current.Sha256,
            TargetTickRateHz = Alpha11TargetTickRateHz,
            StepDeadlineMilliseconds = Alpha11StepDeadlineMilliseconds,
            DeadlineMissRatioMax = Alpha11DeadlineMissRatioMax,
            PacingWaitExcludedFromProcessingLatency = true,
            Host = host,
            Profiles = profiles,
            Runs = observations.OrderBy(static x => x.WorkerCount).ThenBy(static x => x.RunOrdinal).ToArray(),
            FinalStateDigest = stateDigests.Length == 1 ? stateDigests[0] : new string('0', 64),
            DeterminismDigestSummary = determinismDigest,
            Passed = failures.Count == 0,
            FailureCodes = failures.ToArray(),
        };
    }

    private static JsonElement ReadAlpha11PersistenceCriteria()
    {
        var root = Program.FindRepositoryRoot(Directory.GetCurrentDirectory());
        var path = Path.Combine(root, "tests", "performance-fixtures", "v1", "harness-manifest.json");
        if (Program.Sha256File(path) != Program.CanonicalQa04ManifestSha256)
            throw new InvalidDataException("QA-04 persistence criteria manifest digest mismatch.");
        return Program.ReadJsonElement(path).GetProperty("passCriteria");
    }

    private static double RequirePositiveCriterion(JsonElement criteria, string name)
    {
        var value = GetDouble(criteria, name);
        if (!double.IsFinite(value) || value <= 0)
            throw new InvalidDataException($"QA-04 persistence criterion is invalid: {name}.");
        return value;
    }

    private static void ValidateAlpha11Step3Aggregate(
        Gate4Step3BenchmarkEvidence evidence, string evidenceDirectory, string sourceCommit, string executionClass)
    {
        var policy = Alpha11AcceptanceConfig.Current;
        if (evidence.AcceptanceProfile != policy.AcceptanceProfile || evidence.AcceptanceConfigSha256 != policy.Sha256)
            throw new InvalidDataException("Gate4 Step3 acceptance profile/Config mismatch; stale evidence is rejected.");
        var path = ResolveArtifactPath(evidenceDirectory, evidence.ReferenceProfile.ReportRef, "Alpha 1.1 aggregate");
        var aggregate = Program.ReadJson<Alpha11BenchmarkAggregateArtifact>(path, "Alpha 1.1 aggregate");
        if (aggregate.AcceptanceProfile != policy.AcceptanceProfile || aggregate.AcceptanceConfigSha256 != policy.Sha256 ||
            aggregate.SchemaVersion != SchemaVersion || aggregate.ProfileId != ReferenceProfile ||
            aggregate.SourceCommit != sourceCommit || aggregate.ExecutionClass != executionClass ||
            aggregate.Qa04ManifestSha256 != Program.CanonicalQa04ManifestSha256 ||
            aggregate.TargetTickRateHz != policy.TickRateHz || aggregate.StepDeadlineMilliseconds != policy.DeadlineMilliseconds ||
            aggregate.DeadlineMissRatioMax != policy.DeadlineMissRatioMax || !aggregate.PacingWaitExcludedFromProcessingLatency ||
            !aggregate.Passed || aggregate.FailureCodes.Length != 0 || evidence.ReferenceProfile.FailureCodes.Length != 0)
            throw new InvalidDataException("Gate4 Step3 Alpha 1.1 aggregate contract mismatch.");

        // PASSフラグだけを信用せず、digestで束縛された個別レポートから再検証する。
        var observations = new List<BenchmarkRunObservation>();
        foreach (var run in aggregate.Runs)
        {
            VerifyArtifactDigest(evidenceDirectory, run.ReportRef, run.ReportDigest, run.RunId);
            var response = Program.ReadJson<Qa04AdapterResponse>(
                ResolveArtifactPath(evidenceDirectory, run.ReportRef, run.RunId), run.RunId);
            var descriptor = new BenchmarkRunDescriptor
            {
                RunId = run.RunId, WorkerCount = run.WorkerCount, RunOrdinal = run.RunOrdinal,
                BenchmarkProfileId = ReferenceProfile, WarmupSteps = policy.WarmupSteps, MeasurementSteps = policy.MeasurementSteps,
            };
            ValidateResponse(response, NewRequest("benchmark-run", executionClass, run.RunId, sourceCommit,
                ReferenceProfile, JsonSerializer.SerializeToElement(new { }), descriptor), "performance-benchmark-report-v1");
            var parsed = ParseBenchmarkObservation(descriptor, response, run.ReportRef, run.ReportDigest);
            if (JsonSerializer.Serialize(parsed, Program.Json) != JsonSerializer.Serialize(run, Program.Json))
                throw new InvalidDataException($"Gate4 Step3 aggregate observation differs from its raw report: {run.RunId}.");
            observations.Add(parsed);
        }
        var verified = EvaluateAlpha11ReferenceProfile(observations, executionClass, sourceCommit, aggregate.Host);
        if (!verified.Passed || verified.DeterminismDigestSummary != evidence.DeterminismDigestSummary ||
            JsonSerializer.Serialize(verified, Program.Json) != JsonSerializer.Serialize(aggregate, Program.Json))
            throw new InvalidDataException("Gate4 Step3 Alpha 1.1 aggregate failed independent revalidation.");
    }

    private static Alpha11HostEvidence CaptureAlpha11HostEvidence(string executionClass)
    {
        var visibleLogical = Environment.ProcessorCount;
        long affinityMask = 0;
        var affinityLogical = 0;
        var affinityCaptured = false;

        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var process = Process.GetCurrentProcess();
                affinityMask = process.ProcessorAffinity.ToInt64();
                affinityLogical = CountSetBits(unchecked((ulong)affinityMask));
                affinityCaptured = true;
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
            {
                if (string.Equals(executionClass, "release", StringComparison.Ordinal))
                    throw new InvalidDataException("Alpha 1.1 Step3 could not capture process CPU affinity on the release runner.", ex);
            }
        }

        return new Alpha11HostEvidence
        {
            ProcessVisibleLogicalProcessorCount = visibleLogical,
            ProcessorAffinityCaptured = affinityCaptured,
            ProcessorAffinityMaskHex = affinityCaptured ? $"0x{unchecked((ulong)affinityMask):x}" : "unavailable",
            ProcessorAffinityLogicalProcessorCount = affinityLogical,
            RuntimeDescription = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            OperatingSystemDescription = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
        };
    }

    private static int CountSetBits(ulong value)
    {
        var count = 0;
        while (value != 0)
        {
            count += (int)(value & 1UL);
            value >>= 1;
        }
        return count;
    }

    private sealed class Alpha11BenchmarkAggregateArtifact
    {
        public string SchemaVersion { get; set; } = ReleaseEvidenceRunner.SchemaVersion;
        public string ProfileId { get; set; } = ReferenceProfile;
        public string ExecutionClass { get; set; } = "";
        public string SourceCommit { get; set; } = "";
        public string Qa04ManifestSha256 { get; set; } = "";
        public string AcceptanceProfile { get; set; } = "";
        public string AcceptanceConfigSha256 { get; set; } = "";
        public double TargetTickRateHz { get; set; }
        public double StepDeadlineMilliseconds { get; set; }
        public double DeadlineMissRatioMax { get; set; }
        public bool PacingWaitExcludedFromProcessingLatency { get; set; }
        public Alpha11HostEvidence Host { get; set; } = new();
        public Alpha11ExecutionProfileEvidence[] Profiles { get; set; } = [];
        public BenchmarkRunObservation[] Runs { get; set; } = [];
        public string FinalStateDigest { get; set; } = "";
        public string DeterminismDigestSummary { get; set; } = "";
        public bool Passed { get; set; }
        public string[] FailureCodes { get; set; } = [];
    }

    private sealed class Alpha11ExecutionProfileEvidence
    {
        public int WorkerCount { get; set; }
        public int RunCount { get; set; }
        public double WorstRunP99Milliseconds { get; set; }
        public double WorstDeadlineMissRatio { get; set; }
        public double TargetDeadlineMissRatioMax { get; set; }
        public bool DeadlineMissRatioVerifiedByTarget { get; set; }
        public bool AllRunsPassed { get; set; }
    }

    private sealed class Alpha11HostEvidence
    {
        public int ProcessVisibleLogicalProcessorCount { get; set; }
        public bool ProcessorAffinityCaptured { get; set; }
        public string ProcessorAffinityMaskHex { get; set; } = "";
        public int ProcessorAffinityLogicalProcessorCount { get; set; }
        public string RuntimeDescription { get; set; } = "";
        public string OperatingSystemDescription { get; set; } = "";
        public string Architecture { get; set; } = "";
    }
}
