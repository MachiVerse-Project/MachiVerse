using System.Diagnostics;
using System.Text;
using System.Text.Json;

internal static partial class ReleaseEvidenceRunner
{
    private const double Alpha11TargetTickRateHz = 10d;
    private const double Alpha11StepDeadlineMilliseconds = 100d;
    private const double Alpha11DeadlineMissRatioMax = 0.01d;
    private static readonly int[] Alpha11WorkerProfiles = [8, 16];

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

        if (selected.Length != Alpha11WorkerProfiles.Length * 3)
            throw new InvalidDataException($"Alpha 1.1 Step3 matrix must contain 6 selected descriptors, found {selected.Length}.");

        foreach (var worker in Alpha11WorkerProfiles)
        {
            var workerRuns = selected.Where(run => run.WorkerCount == worker).ToArray();
            if (workerRuns.Length != 3 || !workerRuns.Select(static run => run.RunOrdinal).SequenceEqual([1, 2, 3]))
                throw new InvalidDataException($"Alpha 1.1 Step3 worker {worker} repetition matrix is incomplete.");
        }

        foreach (var run in selected)
        {
            if (!string.Equals(run.BenchmarkProfileId, ReferenceProfile, StringComparison.Ordinal))
                throw new InvalidDataException($"Unexpected benchmark profile in run {run.RunId}: {run.BenchmarkProfileId}.");
            if (run.WarmupSteps != 9000 || run.MeasurementSteps != 18000)
                throw new InvalidDataException($"Run {run.RunId} does not preserve canonical warmup/measurement Steps.");
        }

        return selected;
    }

    private static Alpha11BenchmarkAggregateArtifact EvaluateAlpha11ReferenceProfile(
        IReadOnlyList<BenchmarkRunObservation> observations,
        string executionClass,
        string sourceCommit,
        Alpha11HostEvidence host)
    {
        var failures = new SortedSet<string>(StringComparer.Ordinal);
        if (observations.Count != 6)
            failures.Add("alpha11-reference-run-count");

        foreach (var worker in Alpha11WorkerProfiles)
        {
            var profileRuns = observations
                .Where(run => run.WorkerCount == worker)
                .OrderBy(static run => run.RunOrdinal)
                .ToArray();
            if (profileRuns.Length != 3 || !profileRuns.Select(static run => run.RunOrdinal).SequenceEqual([1, 2, 3]))
                failures.Add($"alpha11-worker-{worker}-run-matrix");
        }

        foreach (var observation in observations)
        {
            if (!observation.TargetPassed)
                failures.Add("target-report-failed");
            foreach (var code in observation.TargetFailureCodes)
                failures.Add($"target:{code}");

            if (observation.StepP99Ms > Alpha11StepDeadlineMilliseconds)
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
            host.ProcessVisibleLogicalProcessorCount < 16)
            failures.Add("cpu-profile-logical-processor-capacity");

        var stateDigests = observations
            .Select(static x => x.FinalStateDigest)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (stateDigests.Length != 1)
            failures.Add("state-digest-mismatch");
        foreach (var digest in stateDigests)
            Program.RequireLowerHex(digest, 64, "final_state_digest");

        var determinismMaterial = string.Join("\n", observations
            .OrderBy(static x => x.WorkerCount)
            .ThenBy(static x => x.RunOrdinal)
            .Select(static x => $"{x.RunId}\0{x.FinalStateDigest}"));
        var determinismDigest = Program.Sha256Hex(Encoding.UTF8.GetBytes(determinismMaterial));

        var profiles = Alpha11WorkerProfiles.Select(worker =>
        {
            var profileRuns = observations.Where(run => run.WorkerCount == worker).ToArray();
            return new Alpha11ExecutionProfileEvidence
            {
                WorkerCount = worker,
                RunCount = profileRuns.Length,
                WorstRunP99Milliseconds = profileRuns.Select(static run => run.StepP99Ms).DefaultIfEmpty(double.PositiveInfinity).Max(),
                TargetDeadlineMissRatioMax = Alpha11DeadlineMissRatioMax,
                DeadlineMissRatioVerifiedByTarget = profileRuns.Length == 3 && profileRuns.All(static run => run.TargetPassed),
                AllRunsPassed = profileRuns.Length == 3 && profileRuns.All(static run => run.TargetPassed),
            };
        }).ToArray();

        return new Alpha11BenchmarkAggregateArtifact
        {
            ExecutionClass = executionClass,
            SourceCommit = sourceCommit,
            Qa04ManifestSha256 = Program.CanonicalQa04ManifestSha256,
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
