using System.Diagnostics;
using System.Text;
using System.Text.Json;

internal static class ReleaseEvidenceRunner
{
    private const string SchemaVersion = "1.0";
    private const string ReferenceProfile = "perf.reference.v1";
    private const string PersistenceProfile = "perf.persistence.v1";
    private const string PublicationProfile = "perf.publication.v1";
    private const string SoakProfile = "performance.soak.24h";
    private const long MinimumSoakSeconds = 86_400;

    private static readonly JsonSerializerOptions JsonLine = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    internal static void VerifyContract(string repositoryRoot)
    {
        var manifestPath = Path.Combine(repositoryRoot, "tests", "performance-fixtures", "v1", "harness-manifest.json");
        var digest = Program.Sha256File(manifestPath);
        if (!string.Equals(digest, Program.CanonicalQa04ManifestSha256, StringComparison.Ordinal))
            throw new InvalidDataException($"QA-04 manifest digest mismatch: {digest}.");

        using var manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        var adapter = manifest.RootElement.GetProperty("adapterContract");
        RequireString(adapter, "protocol", "jsonl");
        RequireExactSet(ReadStringArray(adapter, "requestKinds"),
            ["benchmark-run", "persistence-stress", "publication-stress", "soak-run"],
            "QA-04 request kinds");
        RequireExactSet(ReadStringArray(adapter, "responseKinds"),
            ["performance-benchmark-report-v1", "persistence-stress-report-v1", "publication-stress-report-v1", "soak-report-v1"],
            "QA-04 response kinds");

        var releaseManifestPath = Path.Combine(repositoryRoot, "tests", "release-acceptance-fixtures", "v1", "acceptance-manifest.json");
        using var releaseManifest = JsonDocument.Parse(File.ReadAllBytes(releaseManifestPath));
        RequireString(releaseManifest.RootElement, "qa04ManifestSha256", Program.CanonicalQa04ManifestSha256);

        var synthetic = BuildSyntheticObservations();
        var reference = EvaluateReferenceProfile(manifest.RootElement, synthetic, "contract-smoke", new string('1', 40));
        if (!reference.Passed)
            throw new InvalidDataException("Release evidence runner self-test expected reference profile PASS.");

        synthetic[11].StepP95Ms = 33.334;
        var regression = EvaluateReferenceProfile(manifest.RootElement, synthetic, "contract-smoke", new string('1', 40));
        if (regression.Passed || !regression.FailureCodes.Contains("step-p95", StringComparer.Ordinal))
            throw new InvalidDataException("Release evidence runner self-test failed to reject p95 regression.");

        if (IsReleaseExecutionEligible("release", MinimumSoakSeconds - 1)
            || IsReleaseExecutionEligible("contract-smoke", MinimumSoakSeconds))
            throw new InvalidDataException("Release evidence runner self-test failed release/soak eligibility boundary.");

        Console.WriteLine("INT-03 release evidence runner contract verification PASS");
        Console.WriteLine($"QA-04 manifest SHA-256: {digest}");
        Console.WriteLine("Adapter boundary: external JSONL process only; no production component assembly reference.");
        Console.WriteLine("Contract-smoke output is never release-eligible.");
    }

    internal static async Task<int> RunAsync(
        string repositoryRoot,
        string executionClass,
        string sourceCommit,
        string adapterExecutable,
        string planDirectory,
        string outputDirectory)
    {
        if (executionClass is not ("contract-smoke" or "release"))
            throw new ArgumentException("executionClass must be contract-smoke or release.");
        Program.RequireLowerHex(sourceCommit, 40, "sourceCommit");
        if (!File.Exists(adapterExecutable))
            throw new FileNotFoundException("QA-04 adapter executable was not found.", adapterExecutable);
        if (!Directory.Exists(planDirectory))
            throw new DirectoryNotFoundException($"QA-04 plan directory was not found: {planDirectory}");

        var manifestPath = Path.Combine(repositoryRoot, "tests", "performance-fixtures", "v1", "harness-manifest.json");
        if (!string.Equals(Program.Sha256File(manifestPath), Program.CanonicalQa04ManifestSha256, StringComparison.Ordinal))
            throw new InvalidDataException("Current QA-04 manifest is not the canonical release profile.");
        using var manifestDocument = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        var manifest = manifestDocument.RootElement;

        var benchmarkSummary = RequirePlanJson(planDirectory, "benchmark-summary.json");
        var persistenceProfile = RequirePlanJson(planDirectory, "persistence-profile.json");
        var publicationProfile = RequirePlanJson(planDirectory, "publication-profile.json");
        var soakPlan = RequirePlanJson(planDirectory, "soak-plan.json");
        var runMatrixPath = Path.Combine(planDirectory, "reference-run-matrix.json");
        if (!File.Exists(runMatrixPath)) throw new InvalidDataException("Missing materialized reference-run-matrix.json.");
        var runs = Program.ReadJson<BenchmarkRunDescriptor[]>(runMatrixPath, "QA-04 run matrix");
        ValidateRunMatrix(runs);

        Directory.CreateDirectory(outputDirectory);
        var reportsDirectory = Path.Combine(outputDirectory, "reports");
        Directory.CreateDirectory(reportsDirectory);
        var observations = new List<BenchmarkRunObservation>(runs.Length);

        foreach (var run in runs.OrderBy(static x => x.RunId, StringComparer.Ordinal))
        {
            var request = NewRequest(
                "benchmark-run", executionClass, run.RunId, sourceCommit, ReferenceProfile, benchmarkSummary, run);
            var invocation = await InvokeAdapterAsync(adapterExecutable, request);
            ValidateResponse(invocation.Response, request, "performance-benchmark-report-v1");
            var artifact = WriteResponseArtifact(outputDirectory, reportsDirectory, run.RunId, invocation.Response);
            observations.Add(ParseBenchmarkObservation(run, invocation.Response, artifact.Ref, artifact.Digest));
        }

        var referenceAggregate = EvaluateReferenceProfile(manifest, observations, executionClass, sourceCommit);
        var aggregatePath = Path.Combine(reportsDirectory, "perf.reference.v1.aggregate.json");
        var referenceDigest = Program.WriteJson(aggregatePath, referenceAggregate);
        var referenceEvidence = new PerformanceReportEvidence
        {
            ProfileId = ReferenceProfile,
            SourceCommit = sourceCommit,
            ReportRef = RelativeRef(outputDirectory, aggregatePath),
            ReportDigest = referenceDigest,
            Passed = referenceAggregate.Passed,
            FailureCodes = referenceAggregate.FailureCodes,
        };

        var persistenceRequest = NewRequest(
            "persistence-stress", executionClass, "perf.persistence.v1", sourceCommit, PersistenceProfile, persistenceProfile, null);
        var persistenceInvocation = await InvokeAdapterAsync(adapterExecutable, persistenceRequest);
        ValidateResponse(persistenceInvocation.Response, persistenceRequest, "persistence-stress-report-v1");
        var persistenceArtifact = WriteResponseArtifact(
            outputDirectory, reportsDirectory, "perf.persistence.v1", persistenceInvocation.Response);
        var persistenceEvidence = EvaluatePersistence(
            persistenceInvocation.Response, sourceCommit, persistenceArtifact.Ref, persistenceArtifact.Digest);

        var publicationRequest = NewRequest(
            "publication-stress", executionClass, "perf.publication.v1", sourceCommit, PublicationProfile, publicationProfile, null);
        var publicationInvocation = await InvokeAdapterAsync(adapterExecutable, publicationRequest);
        ValidateResponse(publicationInvocation.Response, publicationRequest, "publication-stress-report-v1");
        var publicationArtifact = WriteResponseArtifact(
            outputDirectory, reportsDirectory, "perf.publication.v1", publicationInvocation.Response);
        var publicationEvidence = EvaluatePublication(
            publicationInvocation.Response, sourceCommit, publicationArtifact.Ref, publicationArtifact.Digest);

        var soakRequest = NewRequest(
            "soak-run", executionClass, "performance.soak.24h", sourceCommit, SoakProfile, soakPlan, null);
        var soakInvocation = await InvokeAdapterAsync(adapterExecutable, soakRequest);
        ValidateResponse(soakInvocation.Response, soakRequest, "soak-report-v1");
        var soakArtifact = WriteResponseArtifact(
            outputDirectory, reportsDirectory, "performance.soak.24h", soakInvocation.Response);
        var soakEvidence = EvaluateSoak(
            soakInvocation.Response, sourceCommit, soakArtifact.Ref, soakArtifact.Digest, soakInvocation.Elapsed, executionClass);

        var observedFailures = new SortedSet<string>(StringComparer.Ordinal);
        if (referenceAggregate.FailureCodes.Contains("state-digest-mismatch", StringComparer.Ordinal))
            observedFailures.Add("determinism.divergence");
        if (observations.Any(static x => x.AcceptedOperationLoss != 0))
            observedFailures.Add("operation.accepted-loss");
        if (!soakEvidence.ParallelVerifierDigestMatched)
            observedFailures.Add("determinism.divergence");
        if (soakEvidence.AcceptedOperationLoss != 0)
            observedFailures.Add("operation.accepted-loss");
        if (!soakEvidence.HistoryAuditChainValid)
            observedFailures.Add("persistence.history-corruption-undetected");

        var releaseEligible = IsReleaseExecutionEligible(executionClass, soakEvidence.DurationSeconds);
        var fragment = new EvidenceFragment
        {
            SchemaVersion = SchemaVersion,
            ExecutionClass = executionClass,
            ReleaseEligible = releaseEligible,
            SourceCommit = sourceCommit,
            Qa04ManifestSha256 = Program.CanonicalQa04ManifestSha256,
            PerformanceReports = [referenceEvidence, persistenceEvidence, publicationEvidence],
            Soak = soakEvidence,
            DeterminismDigestSummary = referenceAggregate.DeterminismDigestSummary,
            ObservedFailureCodes = observedFailures.ToArray(),
        };
        var fragmentPath = Path.Combine(outputDirectory, "qa04-evidence-fragment.json");
        Program.WriteJson(fragmentPath, fragment);

        Console.WriteLine($"QA-04 evidence fragment: {fragmentPath}");
        Console.WriteLine($"Execution class: {executionClass}");
        Console.WriteLine($"Release eligible: {releaseEligible}");
        Console.WriteLine($"Measured/reported soak evidence seconds: {soakEvidence.DurationSeconds}");
        Console.WriteLine($"Reference profile: {(referenceEvidence.Passed ? "PASS" : "FAIL")}");
        Console.WriteLine($"Persistence profile: {(persistenceEvidence.Passed ? "PASS" : "FAIL")}");
        Console.WriteLine($"Publication profile: {(publicationEvidence.Passed ? "PASS" : "FAIL")}");
        Console.WriteLine($"Soak profile: {(soakEvidence.Passed ? "PASS" : "FAIL")}");

        var anyProfileFailed = !referenceEvidence.Passed || !persistenceEvidence.Passed
            || !publicationEvidence.Passed || !soakEvidence.Passed;
        if (string.Equals(executionClass, "release", StringComparison.Ordinal) && !releaseEligible) return 2;
        return anyProfileFailed ? 2 : 0;
    }

    internal static void ApplyFragment(string fragmentPath, string baseEvidencePath, string outputEvidencePath)
    {
        var fragment = Program.ReadJson<EvidenceFragment>(fragmentPath, "QA-04 evidence fragment");
        if (!string.Equals(fragment.SchemaVersion, SchemaVersion, StringComparison.Ordinal))
            throw new InvalidDataException("QA-04 evidence fragment schemaVersion mismatch.");
        if (!string.Equals(fragment.ExecutionClass, "release", StringComparison.Ordinal) || !fragment.ReleaseEligible)
            throw new InvalidDataException("Only release-eligible real execution evidence can be applied to INT-03 release evidence.");
        if (!string.Equals(fragment.Qa04ManifestSha256, Program.CanonicalQa04ManifestSha256, StringComparison.Ordinal))
            throw new InvalidDataException("QA-04 evidence fragment manifest digest is not canonical.");
        Program.RequireLowerHex(fragment.SourceCommit, 40, "fragment sourceCommit");
        if (fragment.Soak is null || fragment.Soak.DurationSeconds < MinimumSoakSeconds)
            throw new InvalidDataException("Release fragment does not contain a complete 24-hour soak.");
        RequireExactSet(fragment.PerformanceReports.Select(static x => x.ProfileId).ToArray(),
            [ReferenceProfile, PersistenceProfile, PublicationProfile], "release performance profiles");

        var fragmentDirectory = Path.GetDirectoryName(fragmentPath) ?? Directory.GetCurrentDirectory();
        foreach (var report in fragment.PerformanceReports)
            VerifyArtifactDigest(fragmentDirectory, report.ReportRef, report.ReportDigest, $"performance:{report.ProfileId}");
        VerifyArtifactDigest(fragmentDirectory, fragment.Soak.ReportRef, fragment.Soak.ReportDigest, "soak");

        var evidence = Program.ReadJson<ReleaseEvidence>(baseEvidencePath, "base INT-03 release evidence");
        if (!string.Equals(evidence.SourceCommit, fragment.SourceCommit, StringComparison.Ordinal))
            throw new InvalidDataException("Base release evidence sourceCommit does not match QA-04 release evidence.");

        evidence.PerformanceReports = fragment.PerformanceReports;
        evidence.Soak = fragment.Soak;
        evidence.DeterminismDigestSummary = fragment.DeterminismDigestSummary;
        evidence.ObservedFailureCodes = evidence.ObservedFailureCodes
            .Concat(fragment.ObservedFailureCodes)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static x => x, StringComparer.Ordinal)
            .ToArray();
        if (fragment.Soak.Passed)
        {
            evidence.PassedTestIds = evidence.PassedTestIds
                .Append(SoakProfile)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static x => x, StringComparer.Ordinal)
                .ToArray();
        }

        Program.WriteJson(outputEvidencePath, evidence);
        Console.WriteLine($"Release-eligible QA-04 evidence applied: {outputEvidencePath}");
    }

    private static Qa04AdapterRequest NewRequest(
        string kind,
        string executionClass,
        string requestId,
        string sourceCommit,
        string profileId,
        JsonElement profile,
        BenchmarkRunDescriptor? run)
        => new()
        {
            SchemaVersion = SchemaVersion,
            RequestKind = kind,
            ExecutionClass = executionClass,
            RequestId = requestId,
            SourceCommit = sourceCommit,
            Qa04ManifestSha256 = Program.CanonicalQa04ManifestSha256,
            ProfileId = profileId,
            Run = run,
            Profile = profile.Clone(),
        };

    private static async Task<AdapterInvocation> InvokeAdapterAsync(string executable, Qa04AdapterRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using var process = new Process { StartInfo = startInfo };
        var stopwatch = Stopwatch.StartNew();
        if (!process.Start()) throw new InvalidOperationException($"Failed to start QA-04 adapter: {executable}");
        var requestLine = JsonSerializer.Serialize(request, JsonLine);
        await process.StandardInput.WriteLineAsync(requestLine);
        process.StandardInput.Close();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        stopwatch.Stop();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
            throw new InvalidDataException(
                $"QA-04 adapter exited {process.ExitCode} for {request.RequestId}: {Limit(stderr, 2000)}");

        var lines = stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length != 1)
            throw new InvalidDataException($"QA-04 JSONL adapter must emit exactly one response line for {request.RequestId}; found {lines.Length}.");
        var response = JsonSerializer.Deserialize<Qa04AdapterResponse>(lines[0], JsonLine)
            ?? throw new InvalidDataException($"QA-04 adapter response decoded to null for {request.RequestId}.");
        return new AdapterInvocation(response, stopwatch.Elapsed);
    }

    private static void ValidateResponse(Qa04AdapterResponse response, Qa04AdapterRequest request, string expectedResponseKind)
    {
        RequireEqual(response.SchemaVersion, SchemaVersion, "adapter response schemaVersion");
        RequireEqual(response.ResponseKind, expectedResponseKind, $"responseKind:{request.RequestId}");
        RequireEqual(response.ExecutionClass, request.ExecutionClass, $"executionClass:{request.RequestId}");
        RequireEqual(response.RequestId, request.RequestId, $"requestId:{request.RequestId}");
        RequireEqual(response.SourceCommit, request.SourceCommit, $"sourceCommit:{request.RequestId}");
        RequireEqual(response.Qa04ManifestSha256, Program.CanonicalQa04ManifestSha256, $"qa04ManifestSha256:{request.RequestId}");
        RequireEqual(response.ProfileId, request.ProfileId, $"profileId:{request.RequestId}");
        Program.RequireLowerHex(response.SourceCommit, 40, $"response sourceCommit:{request.RequestId}");
        if (response.FailureCodes.Any(string.IsNullOrWhiteSpace)
            || response.FailureCodes.Distinct(StringComparer.Ordinal).Count() != response.FailureCodes.Length)
            throw new InvalidDataException($"Adapter failureCodes are empty or duplicated for {request.RequestId}.");
        if (response.Report.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"Adapter report must be a JSON object for {request.RequestId}.");
    }

    private static BenchmarkRunObservation ParseBenchmarkObservation(
        BenchmarkRunDescriptor run,
        Qa04AdapterResponse response,
        string reportRef,
        string reportDigest)
    {
        var report = response.Report;
        RequireString(report, "benchmark_profile_id", ReferenceProfile);
        RequireInt(report, "worker_count", run.WorkerCount);
        RequireInt(report, "run_ordinal", run.RunOrdinal);
        foreach (var field in new[]
        {
            "build_version", "runtime_version", "hardware_profile_digest", "config_digest", "step_count",
            "step_p50_ms", "step_p95_ms", "step_p99_ms", "domain_cpu_summary", "max_memory_bytes",
            "persistence_commit_p95_ms", "snapshot_summary", "publication_summary", "final_state_digest", "failure_codes"
        })
            if (!report.TryGetProperty(field, out _)) throw new InvalidDataException($"Benchmark report missing required field: {field}.");

        var reportFailures = ReadStringArray(report, "failure_codes");
        var combinedFailures = response.FailureCodes.Concat(reportFailures).Distinct(StringComparer.Ordinal).ToArray();
        var snapshot = report.GetProperty("snapshot_summary");
        return new BenchmarkRunObservation
        {
            RunId = run.RunId,
            WorkerCount = run.WorkerCount,
            RunOrdinal = run.RunOrdinal,
            StepP95Ms = GetDouble(report, "step_p95_ms"),
            StepP99Ms = GetDouble(report, "step_p99_ms"),
            Mean60sStepMs = GetDouble(report, "step_mean_60s_ms"),
            MaxMemoryBytes = GetLong(report, "max_memory_bytes"),
            SqliteCommitP95Ms = GetDouble(report, "persistence_commit_p95_ms"),
            SqliteCommitP99Ms = GetDouble(report, "persistence_commit_p99_ms"),
            SnapshotCowBarrierP95Ms = GetDouble(snapshot, "cow_barrier_p95_ms"),
            AcceptedOperationLoss = GetInt(report, "accepted_operation_loss"),
            HiddenSolverIterationReduction = GetBool(report, "hidden_solver_iteration_reduction"),
            FinalStateDigest = GetString(report, "final_state_digest"),
            ReportRef = reportRef,
            ReportDigest = reportDigest,
            TargetPassed = response.Passed && combinedFailures.Length == 0,
            TargetFailureCodes = combinedFailures,
        };
    }

    private static BenchmarkAggregateArtifact EvaluateReferenceProfile(
        JsonElement manifest,
        IReadOnlyList<BenchmarkRunObservation> observations,
        string executionClass,
        string sourceCommit)
    {
        var failures = new SortedSet<string>(StringComparer.Ordinal);
        if (observations.Count != 12) failures.Add("reference-run-count");
        foreach (var observation in observations)
        {
            if (!observation.TargetPassed) failures.Add("target-report-failed");
            foreach (var code in observation.TargetFailureCodes) failures.Add($"target:{code}");
        }

        var criteria = manifest.GetProperty("passCriteria");
        var worker16 = observations.Where(static x => x.WorkerCount == 16).Select(static x => x.StepP95Ms).Order().ToArray();
        if (worker16.Length != 3 || Median(worker16) > GetDouble(criteria, "worker16MedianP95StepMs")) failures.Add("step-p95");
        if (observations.Any(x => x.StepP99Ms > GetDouble(criteria, "stepP99Ms"))) failures.Add("step-p99");
        if (observations.Any(x => x.Mean60sStepMs > GetDouble(criteria, "mean60sStepMs"))) failures.Add("step-mean-60s");
        var maxMemoryBytes = (long)GetInt(criteria, "maxMemoryGuardGiB") * 1024L * 1024L * 1024L;
        if (observations.Any(x => x.MaxMemoryBytes > maxMemoryBytes)) failures.Add("memory-guard");
        if (observations.Any(x => x.SqliteCommitP95Ms > GetDouble(criteria, "sqliteCommitP95Ms"))) failures.Add("sqlite-commit-p95");
        if (observations.Any(x => x.SqliteCommitP99Ms > GetDouble(criteria, "sqliteCommitP99Ms"))) failures.Add("sqlite-commit-p99");
        if (observations.Any(x => x.SnapshotCowBarrierP95Ms > GetDouble(criteria, "snapshotCowBarrierP95Ms"))) failures.Add("snapshot-cow-p95");
        if (observations.Any(static x => x.AcceptedOperationLoss != 0)) failures.Add("accepted-operation-loss");
        if (observations.Any(static x => x.HiddenSolverIterationReduction)) failures.Add("hidden-solver-reduction");
        var stateDigests = observations.Select(static x => x.FinalStateDigest).Distinct(StringComparer.Ordinal).ToArray();
        if (stateDigests.Length != 1) failures.Add("state-digest-mismatch");
        foreach (var digest in stateDigests) Program.RequireLowerHex(digest, 64, "final_state_digest");

        var determinismMaterial = string.Join("\n", observations.OrderBy(static x => x.RunId, StringComparer.Ordinal)
            .Select(static x => $"{x.RunId}\0{x.FinalStateDigest}"));
        var determinismDigest = Program.Sha256Hex(Encoding.UTF8.GetBytes(determinismMaterial));
        return new BenchmarkAggregateArtifact
        {
            ExecutionClass = executionClass,
            SourceCommit = sourceCommit,
            Qa04ManifestSha256 = Program.CanonicalQa04ManifestSha256,
            Runs = observations.OrderBy(static x => x.RunId, StringComparer.Ordinal).ToArray(),
            FinalStateDigest = stateDigests.Length == 1 ? stateDigests[0] : new string('0', 64),
            DeterminismDigestSummary = determinismDigest,
            Passed = failures.Count == 0,
            FailureCodes = failures.ToArray(),
        };
    }

    private static PerformanceReportEvidence EvaluatePersistence(
        Qa04AdapterResponse response,
        string sourceCommit,
        string reportRef,
        string reportDigest)
    {
        var report = response.Report;
        RequireString(report, "profile_id", PersistenceProfile);
        var failures = new SortedSet<string>(response.FailureCodes, StringComparer.Ordinal);
        foreach (var code in ReadStringArray(report, "failure_codes")) failures.Add(code);
        if (GetInt(report, "crash_case_count") != 30) failures.Add("crash-case-count");
        if (!GetBool(report, "no_durable_fact_loss")) failures.Add("durable-fact-loss");
        if (!GetBool(report, "no_uncommitted_candidate_publication")) failures.Add("candidate-publication");
        if (!GetBool(report, "history_chain_valid")) failures.Add("history-chain-invalid");
        return new PerformanceReportEvidence
        {
            ProfileId = PersistenceProfile,
            SourceCommit = sourceCommit,
            ReportRef = reportRef,
            ReportDigest = reportDigest,
            Passed = response.Passed && failures.Count == 0,
            FailureCodes = failures.ToArray(),
        };
    }

    private static PerformanceReportEvidence EvaluatePublication(
        Qa04AdapterResponse response,
        string sourceCommit,
        string reportRef,
        string reportDigest)
    {
        var report = response.Report;
        RequireString(report, "profile_id", PublicationProfile);
        var failures = new SortedSet<string>(response.FailureCodes, StringComparer.Ordinal);
        foreach (var code in ReadStringArray(report, "failure_codes")) failures.Add(code);
        if (GetInt(report, "gateway_count") != 1) failures.Add("gateway-count");
        if (GetInt(report, "view_subscribers") != 100) failures.Add("view-subscriber-count");
        if (GetInt(report, "slow_consumers") != 10) failures.Add("slow-consumer-count");
        if (!GetBool(report, "slow_consumers_did_not_block_custody_or_result")) failures.Add("slow-consumer-isolation");
        if (!GetBool(report, "continuity_after_coalesce_resync")) failures.Add("continuity-after-resync");
        return new PerformanceReportEvidence
        {
            ProfileId = PublicationProfile,
            SourceCommit = sourceCommit,
            ReportRef = reportRef,
            ReportDigest = reportDigest,
            Passed = response.Passed && failures.Count == 0,
            FailureCodes = failures.ToArray(),
        };
    }

    private static SoakEvidence EvaluateSoak(
        Qa04AdapterResponse response,
        string sourceCommit,
        string reportRef,
        string reportDigest,
        TimeSpan elapsed,
        string executionClass)
    {
        var report = response.Report;
        RequireString(report, "test_case_id", SoakProfile);
        var reportedDuration = GetLong(report, "duration_seconds");
        var measuredDuration = (long)Math.Floor(elapsed.TotalSeconds);
        var evidenceDuration = string.Equals(executionClass, "release", StringComparison.Ordinal)
            ? Math.Min(reportedDuration, measuredDuration)
            : reportedDuration;
        var reportFailures = ReadStringArray(report, "failure_codes");
        var passed = response.Passed && response.FailureCodes.Length == 0 && reportFailures.Length == 0;
        return new SoakEvidence
        {
            TestCaseId = SoakProfile,
            SourceCommit = sourceCommit,
            ReportRef = reportRef,
            ReportDigest = reportDigest,
            DurationSeconds = evidenceDuration,
            Passed = passed,
            ParallelVerifierDigestMatched = GetBool(report, "parallel_verifier_digest_matched"),
            MaxPostWarmupMemoryGrowthPercent = GetDouble(report, "max_post_warmup_memory_growth_percent"),
            AcceptedOperationLoss = GetInt(report, "accepted_operation_loss"),
            HistoryAuditChainValid = GetBool(report, "history_audit_chain_valid"),
            NoUnrecoverableQueueDeadlock = GetBool(report, "no_unrecoverable_queue_deadlock"),
        };
    }

    private static void ValidateRunMatrix(BenchmarkRunDescriptor[] runs)
    {
        if (runs.Length != 12) throw new InvalidDataException($"QA-04 run matrix must contain 12 descriptors, found {runs.Length}.");
        if (runs.Select(static x => x.RunId).Distinct(StringComparer.Ordinal).Count() != runs.Length)
            throw new InvalidDataException("QA-04 run matrix contains duplicate RunId values.");
        foreach (var worker in new[] { 1, 4, 8, 16 })
        {
            var workerRuns = runs.Where(x => x.WorkerCount == worker).OrderBy(static x => x.RunOrdinal).ToArray();
            if (workerRuns.Length != 3 || !workerRuns.Select(static x => x.RunOrdinal).SequenceEqual([1, 2, 3]))
                throw new InvalidDataException($"QA-04 worker {worker} run matrix is incomplete.");
        }
        foreach (var run in runs)
        {
            if (!string.Equals(run.BenchmarkProfileId, ReferenceProfile, StringComparison.Ordinal))
                throw new InvalidDataException($"Unexpected benchmark profile in run {run.RunId}: {run.BenchmarkProfileId}.");
            if (run.WarmupSteps != 9000 || run.MeasurementSteps != 18000)
                throw new InvalidDataException($"Run {run.RunId} does not preserve canonical warmup/measurement Steps.");
        }
    }

    private static JsonElement RequirePlanJson(string planDirectory, string name)
    {
        var path = Path.Combine(planDirectory, name);
        if (!File.Exists(path)) throw new InvalidDataException($"Missing materialized QA-04 plan file: {name}.");
        return Program.ReadJsonElement(path);
    }

    private static (string Ref, string Digest) WriteResponseArtifact(
        string outputDirectory,
        string reportsDirectory,
        string name,
        Qa04AdapterResponse response)
    {
        var safeName = string.Concat(name.Select(static c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_'));
        var path = Path.Combine(reportsDirectory, safeName + ".json");
        var digest = Program.WriteJson(path, response);
        return (RelativeRef(outputDirectory, path), digest);
    }

    private static string RelativeRef(string root, string path)
        => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static void VerifyArtifactDigest(string fragmentDirectory, string artifactRef, string expectedDigest, string name)
    {
        Program.RequireLowerHex(expectedDigest, 64, $"{name} digest");
        if (string.IsNullOrWhiteSpace(artifactRef) || Path.IsPathRooted(artifactRef))
            throw new InvalidDataException($"{name} artifact ref must be a relative path.");
        var root = Path.GetFullPath(fragmentDirectory) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(fragmentDirectory, artifactRef.Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!candidate.StartsWith(root, comparison)) throw new InvalidDataException($"{name} artifact ref escapes the fragment directory.");
        if (!File.Exists(candidate)) throw new FileNotFoundException($"{name} artifact is missing.", candidate);
        var actual = Program.Sha256File(candidate);
        if (!string.Equals(actual, expectedDigest, StringComparison.Ordinal))
            throw new InvalidDataException($"{name} artifact digest mismatch: expected {expectedDigest}, found {actual}.");
    }

    private static bool IsReleaseExecutionEligible(string executionClass, long soakDurationSeconds)
        => string.Equals(executionClass, "release", StringComparison.Ordinal) && soakDurationSeconds >= MinimumSoakSeconds;

    private static BenchmarkRunObservation[] BuildSyntheticObservations()
    {
        var result = new List<BenchmarkRunObservation>();
        foreach (var worker in new[] { 1, 4, 8, 16 })
        foreach (var ordinal in new[] { 1, 2, 3 })
            result.Add(new BenchmarkRunObservation
            {
                RunId = $"selftest.{worker}.{ordinal}",
                WorkerCount = worker,
                RunOrdinal = ordinal,
                StepP95Ms = worker == 16 ? 33.333 : 25.0,
                StepP99Ms = 50.0,
                Mean60sStepMs = 30.0,
                MaxMemoryBytes = 28L * 1024L * 1024L * 1024L,
                SqliteCommitP95Ms = 4.0,
                SqliteCommitP99Ms = 8.0,
                SnapshotCowBarrierP95Ms = 5.0,
                AcceptedOperationLoss = 0,
                HiddenSolverIterationReduction = false,
                FinalStateDigest = new string('a', 64),
                ReportRef = $"fixture://{worker}/{ordinal}",
                ReportDigest = new string('b', 64),
                TargetPassed = true,
                TargetFailureCodes = [],
            });
        return result.ToArray();
    }

    private static double Median(double[] ordered)
    {
        if (ordered.Length == 0) return double.PositiveInfinity;
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 1 ? ordered[middle] : (ordered[middle - 1] + ordered[middle]) / 2.0;
    }

    private static string[] ReadStringArray(JsonElement parent, string property)
    {
        var node = parent.GetProperty(property);
        if (node.ValueKind != JsonValueKind.Array) throw new InvalidDataException($"{property} must be an array.");
        return node.EnumerateArray().Select(x => x.GetString() ?? throw new InvalidDataException($"{property} contains null.")).ToArray();
    }

    private static string GetString(JsonElement parent, string property)
        => parent.GetProperty(property).GetString() ?? throw new InvalidDataException($"{property} cannot be null.");

    private static int GetInt(JsonElement parent, string property)
        => parent.GetProperty(property).GetInt32();

    private static long GetLong(JsonElement parent, string property)
        => parent.GetProperty(property).GetInt64();

    private static double GetDouble(JsonElement parent, string property)
        => parent.GetProperty(property).GetDouble();

    private static bool GetBool(JsonElement parent, string property)
        => parent.GetProperty(property).GetBoolean();

    private static void RequireString(JsonElement parent, string property, string expected)
        => RequireEqual(GetString(parent, property), expected, property);

    private static void RequireInt(JsonElement parent, string property, int expected)
    {
        var actual = GetInt(parent, property);
        if (actual != expected) throw new InvalidDataException($"{property} must be {expected}, found {actual}.");
    }

    private static void RequireEqual(string actual, string expected, string name)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidDataException($"{name} must be '{expected}', found '{actual}'.");
    }

    private static void RequireExactSet(string[] actual, string[] expected, string name)
    {
        if (actual.Length != expected.Length || !actual.ToHashSet(StringComparer.Ordinal).SetEquals(expected))
            throw new InvalidDataException($"{name} does not match the canonical set.");
    }

    private static string Limit(string value, int max)
        => value.Length <= max ? value : value[..max] + "...";

    private sealed record AdapterInvocation(Qa04AdapterResponse Response, TimeSpan Elapsed);
}
