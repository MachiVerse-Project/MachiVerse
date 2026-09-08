using System.Text.Json;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private static int Main()
    {
        try
        {
            var line = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(line)) throw new InvalidDataException("Expected one QA-04 JSONL request line.");
            var request = JsonSerializer.Deserialize<Request>(line, Json)
                ?? throw new InvalidDataException("Request decoded to null.");
            if (!string.Equals(request.ExecutionClass, "contract-smoke", StringComparison.Ordinal))
                throw new InvalidDataException("Contract fixture refuses release execution class.");
            if (!string.Equals(request.SchemaVersion, "1.0", StringComparison.Ordinal))
                throw new InvalidDataException("Unsupported request schemaVersion.");

            var response = request.RequestKind switch
            {
                "benchmark-run" => Benchmark(request),
                "persistence-stress" => Persistence(request),
                "publication-stress" => Publication(request),
                "soak-run" => Soak(request),
                _ => throw new InvalidDataException($"Unknown requestKind: {request.RequestKind}"),
            };
            Console.WriteLine(JsonSerializer.Serialize(response, Json));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Contract fixture FAILED: {ex.Message}");
            return 1;
        }
    }

    private static Response Benchmark(Request request)
    {
        var run = request.Run ?? throw new InvalidDataException("benchmark-run requires run descriptor.");
        return NewResponse(request, "performance-benchmark-report-v1", new
        {
            benchmark_profile_id = "perf.reference.v1",
            build_version = "contract-smoke",
            runtime_version = Environment.Version.ToString(),
            hardware_profile_digest = new string('a', 64),
            config_digest = new string('b', 64),
            worker_count = run.WorkerCount,
            run_ordinal = run.RunOrdinal,
            step_count = run.WarmupSteps + run.MeasurementSteps,
            step_p50_ms = 20.0,
            step_p95_ms = run.WorkerCount == 16 ? 33.0 : 25.0,
            step_p99_ms = 40.0,
            step_mean_60s_ms = 25.0,
            domain_cpu_summary = new { },
            max_memory_bytes = 20L * 1024L * 1024L * 1024L,
            persistence_commit_p95_ms = 3.0,
            persistence_commit_p99_ms = 6.0,
            snapshot_summary = new { cow_barrier_p95_ms = 4.0 },
            publication_summary = new { },
            final_state_digest = new string('c', 64),
            accepted_operation_loss = 0,
            hidden_solver_iteration_reduction = false,
            failure_codes = Array.Empty<string>(),
        });
    }

    private static Response Persistence(Request request)
        => NewResponse(request, "persistence-stress-report-v1", new
        {
            profile_id = "perf.persistence.v1",
            crash_case_count = 30,
            no_durable_fact_loss = true,
            no_uncommitted_candidate_publication = true,
            history_chain_valid = true,
            failure_codes = Array.Empty<string>(),
        });

    private static Response Publication(Request request)
        => NewResponse(request, "publication-stress-report-v1", new
        {
            profile_id = "perf.publication.v1",
            gateway_count = 1,
            view_subscribers = 100,
            slow_consumers = 10,
            slow_consumers_did_not_block_custody_or_result = true,
            continuity_after_coalesce_resync = true,
            failure_codes = Array.Empty<string>(),
        });

    private static Response Soak(Request request)
        => NewResponse(request, "soak-report-v1", new
        {
            test_case_id = "performance.soak.24h",
            duration_seconds = 1,
            parallel_verifier_digest_matched = true,
            max_post_warmup_memory_growth_percent = 0.0,
            accepted_operation_loss = 0,
            history_audit_chain_valid = true,
            no_unrecoverable_queue_deadlock = true,
            failure_codes = Array.Empty<string>(),
        });

    private static Response NewResponse(Request request, string kind, object report)
        => new()
        {
            SchemaVersion = "1.0",
            ResponseKind = kind,
            ExecutionClass = request.ExecutionClass,
            RequestId = request.RequestId,
            SourceCommit = request.SourceCommit,
            Qa04ManifestSha256 = request.Qa04ManifestSha256,
            ProfileId = request.ProfileId,
            Passed = true,
            FailureCodes = [],
            Report = JsonSerializer.SerializeToElement(report, Json),
        };

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
    }

    private sealed class RunDescriptor
    {
        public int WorkerCount { get; set; }
        public int RunOrdinal { get; set; }
        public int WarmupSteps { get; set; }
        public int MeasurementSteps { get; set; }
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
}
