using System.Diagnostics;
using System.Text.Json;

internal static class Program
{
    private const string CanonicalManifestSha256 = "5de8301439ca57080eefa599da284f9271b29366c791bcb9c2f85ddbfa041423";
    private const string ReferenceProfile = "perf.reference.v1";
    private const string PersistenceProfile = "perf.persistence.v1";
    private const string PublicationProfile = "perf.publication.v1";
    private const string SoakProfile = "performance.soak.24h";
    private const string IncompleteCode = "qa04.target.reference-world-not-materialized";
    private const string StepLoopCode = "qa04.target.authoritative-step-loop-not-assembled";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private static async Task<int> Main()
    {
        try
        {
            var line = await Console.In.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line)) throw new InvalidDataException("Expected one QA-04 JSONL request line.");
            var request = JsonSerializer.Deserialize<Request>(line, Json)
                ?? throw new InvalidDataException("QA-04 request decoded to null.");
            ValidateRequest(request);

            var coreExecutable = Environment.GetEnvironmentVariable("MACHIVERSE_QA04_CORE_EXECUTABLE");
            if (string.IsNullOrWhiteSpace(coreExecutable) || !File.Exists(coreExecutable))
                throw new FileNotFoundException("MACHIVERSE_QA04_CORE_EXECUTABLE must identify the assembled Simulation Core executable.", coreExecutable);

            Response response = request.RequestKind switch
            {
                "benchmark-run" => await BenchmarkAsync(request, coreExecutable),
                "persistence-stress" => await PersistenceAsync(request, coreExecutable),
                "publication-stress" => await PublicationAsync(request, coreExecutable),
                "soak-run" => await SoakAsync(request, coreExecutable),
                _ => throw new InvalidDataException($"Unknown requestKind: {request.RequestKind}"),
            };
            await Console.Out.WriteLineAsync(JsonSerializer.Serialize(response, Json));
            return 0;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"QA-04 runtime adapter FAILED: {ex.Message}");
            return 1;
        }
    }

    private static void ValidateRequest(Request request)
    {
        if (!string.Equals(request.SchemaVersion, "1.0", StringComparison.Ordinal))
            throw new InvalidDataException("Unsupported request schemaVersion.");
        if (request.ExecutionClass is not ("contract-smoke" or "release"))
            throw new InvalidDataException("executionClass must be contract-smoke or release.");
        if (!string.Equals(request.Qa04ManifestSha256, CanonicalManifestSha256, StringComparison.Ordinal))
            throw new InvalidDataException("QA-04 manifest digest is not canonical.");
        RequireLowerHex(request.SourceCommit, 40, "sourceCommit");
        if (string.IsNullOrWhiteSpace(request.RequestId)) throw new InvalidDataException("requestId is required.");
    }

    private static async Task<Response> BenchmarkAsync(Request request, string coreExecutable)
    {
        if (!string.Equals(request.ProfileId, ReferenceProfile, StringComparison.Ordinal))
            throw new InvalidDataException("benchmark-run profileId mismatch.");
        var run = request.Run ?? throw new InvalidDataException("benchmark-run requires run descriptor.");
        if (!new[] { 1, 4, 8, 16 }.Contains(run.WorkerCount))
            throw new InvalidDataException("benchmark-run workerCount is not canonical.");
        if (run.RunOrdinal is < 1 or > 3 || run.WarmupSteps != 9_000 || run.MeasurementSteps != 18_000)
            throw new InvalidDataException("benchmark-run descriptor is not canonical.");
        if (!string.Equals(run.BenchmarkProfileId, ReferenceProfile, StringComparison.Ordinal))
            throw new InvalidDataException("benchmark-run benchmarkProfileId mismatch.");

        var probe = await InvokeCoreAsync<WorkerProbe>(coreExecutable, new
        {
            schemaVersion = "1.0",
            command = "worker-probe",
            workerCount = run.WorkerCount,
        });
        if (!string.Equals(probe.SchemaVersion, "1.0", StringComparison.Ordinal) ||
            !string.Equals(probe.ProfileId, ReferenceProfile, StringComparison.Ordinal) ||
            probe.WorkerCount != run.WorkerCount ||
            probe.DomainCount != 8 ||
            !probe.WorkerCountAppliedToDomainExecutor)
            throw new InvalidDataException("Simulation Core worker probe did not prove the requested worker count reached domain execution.");

        var failures = MergeFailures(probe.BlockingFailureCodes, IncompleteCode, StepLoopCode);
        return NewResponse(request, "performance-benchmark-report-v1", false, failures, new
        {
            benchmark_profile_id = ReferenceProfile,
            build_version = request.SourceCommit,
            runtime_version = "assembled-core-process-foundation",
            hardware_profile_digest = new string('0', 64),
            config_digest = new string('0', 64),
            worker_count = run.WorkerCount,
            run_ordinal = run.RunOrdinal,
            step_count = 0,
            step_p50_ms = 1_000_000_000.0,
            step_p95_ms = 1_000_000_000.0,
            step_p99_ms = 1_000_000_000.0,
            step_mean_60s_ms = 1_000_000_000.0,
            domain_cpu_summary = new
            {
                measured = false,
                configured_worker_count = probe.WorkerCount,
                max_observed_probe_concurrency = probe.MaxObservedConcurrency,
            },
            max_memory_bytes = long.MaxValue,
            persistence_commit_p95_ms = 1_000_000_000.0,
            persistence_commit_p99_ms = 1_000_000_000.0,
            snapshot_summary = new { cow_barrier_p95_ms = 1_000_000_000.0, measured = false },
            publication_summary = new { measured = false },
            final_state_digest = new string('0', 64),
            accepted_operation_loss = 0,
            hidden_solver_iteration_reduction = false,
            failure_codes = failures,
        });
    }

    private static async Task<Response> PersistenceAsync(Request request, string coreExecutable)
    {
        RequireProfile(request, PersistenceProfile);
        var inspection = await InspectCoreAsync(coreExecutable);
        var failures = MergeFailures(inspection.BlockingFailureCodes, IncompleteCode, "qa04.target.persistence-stress-not-assembled");
        return NewResponse(request, "persistence-stress-report-v1", false, failures, new
        {
            profile_id = PersistenceProfile,
            crash_case_count = 0,
            no_durable_fact_loss = false,
            no_uncommitted_candidate_publication = false,
            history_chain_valid = false,
            failure_codes = failures,
        });
    }

    private static async Task<Response> PublicationAsync(Request request, string coreExecutable)
    {
        RequireProfile(request, PublicationProfile);
        var inspection = await InspectCoreAsync(coreExecutable);
        var failures = MergeFailures(inspection.BlockingFailureCodes, IncompleteCode, "qa04.target.publication-stress-not-assembled");
        return NewResponse(request, "publication-stress-report-v1", false, failures, new
        {
            profile_id = PublicationProfile,
            gateway_count = 0,
            view_subscribers = 0,
            slow_consumers = 0,
            slow_consumers_did_not_block_custody_or_result = false,
            continuity_after_coalesce_resync = false,
            failure_codes = failures,
        });
    }

    private static async Task<Response> SoakAsync(Request request, string coreExecutable)
    {
        RequireProfile(request, SoakProfile);
        var inspection = await InspectCoreAsync(coreExecutable);
        var failures = MergeFailures(inspection.BlockingFailureCodes, IncompleteCode, "qa04.target.soak-not-assembled");
        return NewResponse(request, "soak-report-v1", false, failures, new
        {
            test_case_id = SoakProfile,
            duration_seconds = 0,
            parallel_verifier_digest_matched = false,
            max_post_warmup_memory_growth_percent = 100.0,
            accepted_operation_loss = 0,
            history_audit_chain_valid = false,
            no_unrecoverable_queue_deadlock = false,
            failure_codes = failures,
        });
    }

    private static async Task<Inspection> InspectCoreAsync(string coreExecutable)
    {
        var inspection = await InvokeCoreAsync<Inspection>(coreExecutable, new
        {
            schemaVersion = "1.0",
            command = "inspect",
            workerCount = 0,
        });
        if (!string.Equals(inspection.SchemaVersion, "1.0", StringComparison.Ordinal) ||
            !string.Equals(inspection.ProfileId, ReferenceProfile, StringComparison.Ordinal) ||
            inspection.StandardDomainCount != 8 || inspection.StandardPartitionCount != 97 ||
            inspection.ReferenceWorldMaterialized || inspection.AuthoritativeStepLoopAvailable || inspection.ReleaseEvidenceCapable)
            throw new InvalidDataException("Simulation Core QA-04 target inspection boundary is inconsistent.");
        if (!inspection.CanonicalWorkerCounts.SequenceEqual(new[] { 1, 4, 8, 16 }))
            throw new InvalidDataException("Simulation Core QA-04 canonical worker set drifted.");
        return inspection;
    }

    private static async Task<T> InvokeCoreAsync<T>(string coreExecutable, object request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = coreExecutable,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("qa04-target");
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) throw new InvalidOperationException("Failed to start assembled Simulation Core QA-04 target process.");
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, Json));
        process.StandardInput.Close();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await process.WaitForExitAsync(timeout.Token);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
            throw new InvalidDataException($"Simulation Core QA-04 target exited {process.ExitCode}: {Limit(stderr, 1000)}");
        var lines = stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length != 1)
            throw new InvalidDataException($"Simulation Core QA-04 target must emit exactly one JSON line; found {lines.Length}.");
        return JsonSerializer.Deserialize<T>(lines[0], Json)
            ?? throw new InvalidDataException("Simulation Core QA-04 target response decoded to null.");
    }

    private static Response NewResponse(Request request, string responseKind, bool passed, string[] failures, object report)
        => new()
        {
            SchemaVersion = "1.0",
            ResponseKind = responseKind,
            ExecutionClass = request.ExecutionClass,
            RequestId = request.RequestId,
            SourceCommit = request.SourceCommit,
            Qa04ManifestSha256 = request.Qa04ManifestSha256,
            ProfileId = request.ProfileId,
            Passed = passed,
            FailureCodes = failures,
            Report = JsonSerializer.SerializeToElement(report, Json),
        };

    private static void RequireProfile(Request request, string expected)
    {
        if (!string.Equals(request.ProfileId, expected, StringComparison.Ordinal))
            throw new InvalidDataException($"profileId must be {expected} for {request.RequestKind}.");
    }

    private static string[] MergeFailures(IEnumerable<string> existing, params string[] required)
        => existing.Concat(required)
            .Where(static code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static code => code, StringComparer.Ordinal)
            .ToArray();

    private static void RequireLowerHex(string value, int length, string field)
    {
        if (value.Length != length || value.Any(static c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new InvalidDataException($"{field} must be {length} lowercase hexadecimal characters.");
    }

    private static string Limit(string value, int max) => value.Length <= max ? value : value[..max] + "...";

    private sealed class Request
    {
        public string SchemaVersion { get; set; } = "";
        public string RequestKind { get; set; } = "";
        public string ExecutionClass { get; set; } = "";
        public string RequestId { get; set; } = "";
        public string SourceCommit { get; set; } = "";
        public string Qa04ManifestSha256 { get; set; } = "";
        public string ProfileId { get; set; } = "";
        public RunDescriptor? Run { get; set; }
        public JsonElement Profile { get; set; }
    }

    private sealed class RunDescriptor
    {
        public string RunId { get; set; } = "";
        public string BenchmarkProfileId { get; set; } = "";
        public int WorkerCount { get; set; }
        public int RunOrdinal { get; set; }
        public int WarmupSteps { get; set; }
        public int MeasurementSteps { get; set; }
        public string WorldSeed { get; set; } = "";
    }

    private sealed class Response
    {
        public string SchemaVersion { get; set; } = "";
        public string ResponseKind { get; set; } = "";
        public string ExecutionClass { get; set; } = "";
        public string RequestId { get; set; } = "";
        public string SourceCommit { get; set; } = "";
        public string Qa04ManifestSha256 { get; set; } = "";
        public string ProfileId { get; set; } = "";
        public bool Passed { get; set; }
        public string[] FailureCodes { get; set; } = [];
        public JsonElement Report { get; set; }
    }

    private sealed class Inspection
    {
        public string SchemaVersion { get; set; } = "";
        public string ProfileId { get; set; } = "";
        public int[] CanonicalWorkerCounts { get; set; } = [];
        public int StandardDomainCount { get; set; }
        public int StandardPartitionCount { get; set; }
        public bool ReferenceWorldMaterialized { get; set; }
        public bool AuthoritativeStepLoopAvailable { get; set; }
        public bool ReleaseEvidenceCapable { get; set; }
        public string[] BlockingFailureCodes { get; set; } = [];
    }

    private sealed class WorkerProbe
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
}
