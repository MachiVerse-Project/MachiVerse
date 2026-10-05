using System.Text.Json;
using System.Text.Json.Nodes;

internal static partial class ReleaseEvidenceRunner
{
    internal static void VerifyAlpha11Contract()
    {
        var policy = Alpha11AcceptanceConfig.Current;
        var source = new string('1', 40);
        var host = new Alpha11HostEvidence { ProcessVisibleLogicalProcessorCount = policy.WorkerCounts.Max() };
        BenchmarkRunObservation[] PassingRuns() => BuildSyntheticObservations()
            .Where(x => Alpha11WorkerProfiles.Contains(x.WorkerCount)).Select(x =>
            {
                x.StepP99Ms = policy.DeadlineMilliseconds;
                x.StepSampleCount = policy.MeasurementSteps;
                x.MeasurementEvidenceComplete = true;
                x.StepDeadlineMilliseconds = policy.DeadlineMilliseconds;
                x.AcceptanceConfigSha256 = policy.Sha256;
                x.CoreWorkingSetSampleCount = policy.MeasurementSteps;
                x.MaxMemoryBytes = policy.CoreSteadyTargetBytes;
                x.Determinism = new(x.RunId, x.FinalStateDigest, new string('b', 64), new string('c', 64), new string('d', 64), new string('e', 64));
                return x;
            }).ToArray();
        Alpha11BenchmarkAggregateArtifact Evaluate(BenchmarkRunObservation[] runs)
            => EvaluateAlpha11ReferenceProfile(runs, "contract-smoke", source, host);
        void RequireReject(Action<BenchmarkRunObservation> mutate, string code)
        {
            var runs = PassingRuns();
            mutate(runs[0]);
            var result = Evaluate(runs);
            if (result.Passed || !result.FailureCodes.Contains(code))
                throw new InvalidDataException($"Alpha 1.1 regression was accepted: {code}.");
        }
        if (!Evaluate(PassingRuns()).Passed) throw new InvalidDataException("Alpha 1.1 boundary fixture must pass.");
        var exactRatio = PassingRuns();
        foreach (var run in exactRatio)
        {
            run.StepDeadlineMissCount = (int)(policy.MeasurementSteps * policy.DeadlineMissRatioMax);
            run.StepDeadlineMissRatio = run.StepDeadlineMissCount / (double)run.StepSampleCount;
        }
        if (!Evaluate(exactRatio).Passed) throw new InvalidDataException("Exact permitted deadline ratio must pass.");
        RequireReject(x => { x.StepDeadlineMissCount = exactRatio[0].StepDeadlineMissCount + 1; x.StepDeadlineMissRatio = x.StepDeadlineMissCount / (double)x.StepSampleCount; }, "step-deadline-miss-ratio");
        RequireReject(x => x.StepDeadlineMissCount = -1, "step-deadline-miss-count");
        RequireReject(x => x.StepDeadlineMissRatio = double.NaN, "step-deadline-miss-ratio-drift");
        RequireReject(x => x.StepDeadlineMissRatio = 0.005, "step-deadline-miss-ratio-drift");
        RequireReject(x => x.StepSampleCount--, "step-sample-count");
        RequireReject(x => x.StepDeadlineMilliseconds = 50, "step-deadline-contract");
        RequireReject(x => x.AcceptanceConfigSha256 = new string('f', 64), "acceptance-config-mismatch");
        RequireReject(x => x.StepP99Ms = policy.DeadlineMilliseconds + 1, "step-p99");
        RequireReject(x => x.MaxMemoryBytes = policy.CoreSteadyTargetBytes + 1, "memory-target");
        RequireReject(x => x.MaxMemoryBytes = policy.CoreHardGuardBytes + 1, "memory-guard");
        RequireReject(x => x.MaxMemoryBytes = 40L << 30, "memory-guard");
        RequireReject(x => x.CoreWorkingSetSampleCount = 0, "memory-sample-invalid");
        RequireReject(x => x.PersistenceMetricObserverFailureCount = 1, "persistence-observer-failure");
        RequireReject(x => x.AcceptedOperationLoss = 1, "accepted-operation-loss");
        RequireReject(x => x.HiddenSolverIterationReduction = true, "hidden-solver-reduction");

        var valid = PassingRuns();
        var descriptor = Descriptor(valid[0]);
        var report = JsonNode.Parse(Report(valid[0]).GetRawText())!.AsObject();
        foreach (var field in new[] { "step_deadline_ms", "step_deadline_miss_count", "step_deadline_miss_ratio", "acceptance_config_sha256", "core_working_set_sample_count" })
        {
            var missing = report.DeepClone().AsObject();
            missing.Remove(field);
            RequireInvalid(() => ParseBenchmarkObservation(descriptor, Response(valid[0], JsonSerializer.SerializeToElement(missing)), "report.json", new string('b', 64)));
            if (field != "acceptance_config_sha256")
            {
                var wrongType = report.DeepClone().AsObject();
                wrongType[field] = "unmeasured";
                RequireInvalid(() => ParseBenchmarkObservation(descriptor, Response(valid[0], JsonSerializer.SerializeToElement(wrongType)), "report.json", new string('b', 64)));
            }
        }
        foreach (var field in new[] { "final_state_digest", "transition_committed_digest", "operation_terminal_semantic_digest", "config_history_digest", "promotion_deferral_order_digest" })
        {
            var missing = report.DeepClone().AsObject();
            missing["determinism_evidence"]!.AsObject().Remove(field);
            RequireInvalid(() => ParseBenchmarkObservation(descriptor, Response(valid[0], JsonSerializer.SerializeToElement(missing)), "report.json", new string('b', 64)));
            foreach (var bad in new[] { new string('0', 64), new string('A', 64), "abc" })
            {
                var malformed = report.DeepClone().AsObject();
                malformed["determinism_evidence"]![field] = bad;
                RequireInvalid(() => ParseBenchmarkObservation(descriptor, Response(valid[0], JsonSerializer.SerializeToElement(malformed)), "report.json", new string('b', 64)));
            }
            var divergent = report.DeepClone().AsObject();
            divergent["determinism_evidence"]![field] = new string('f', 64);
            if (field == "final_state_digest") divergent[field] = new string('f', 64);
            var rows = PassingRuns();
            rows[0] = ParseBenchmarkObservation(descriptor, Response(valid[0], JsonSerializer.SerializeToElement(divergent)), "report.json", new string('b', 64));
            RequireInvalid(() => Evaluate(rows));
        }

        var directory = Path.Combine(Path.GetTempPath(), "machiverse-step3-regression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "reports"));
        try
        {
            foreach (var run in valid)
            {
                run.ReportRef = "reports/" + run.RunId.Replace('/', '_') + ".json";
                run.ReportDigest = Program.WriteJson(Path.Combine(directory, run.ReportRef), Response(run, Report(run)));
            }
            var aggregate = Evaluate(valid);
            var aggregatePath = Path.Combine(directory, "reports", "aggregate.json");
            var evidence = new Gate4Step3BenchmarkEvidence
            {
                SchemaVersion = SchemaVersion, ExecutionClass = "contract-smoke", SourceCommit = source,
                Qa04ManifestSha256 = Program.CanonicalQa04ManifestSha256, BenchmarkProfileId = ReferenceProfile,
                AcceptanceProfile = policy.AcceptanceProfile, AcceptanceConfigSha256 = policy.Sha256,
                ReferenceProfile = new() { ProfileId = ReferenceProfile, SourceCommit = source, ReportRef = "reports/aggregate.json", Passed = true },
                Passed = true, DeterminismDigestSummary = aggregate.DeterminismDigestSummary,
            };
            evidence.ReferenceProfile.ReportDigest = Program.WriteJson(aggregatePath, aggregate);
            var evidencePath = Path.Combine(directory, "evidence.json");
            ValidateStep3Evidence(evidence, source, "contract-smoke", evidencePath);
            evidence.AcceptanceProfile = "";
            RequireInvalid(() => ValidateStep3Evidence(evidence, source, "contract-smoke", evidencePath));
            evidence.AcceptanceProfile = policy.AcceptanceProfile;
            aggregate.AcceptanceProfile = "";
            evidence.ReferenceProfile.ReportDigest = Program.WriteJson(aggregatePath, aggregate);
            RequireInvalid(() => ValidateStep3Evidence(evidence, source, "contract-smoke", evidencePath));
            aggregate.AcceptanceProfile = policy.AcceptanceProfile;
            aggregate.Runs[0].StepDeadlineMissRatio = 0.001;
            evidence.ReferenceProfile.ReportDigest = Program.WriteJson(aggregatePath, aggregate);
            RequireInvalid(() => ValidateStep3Evidence(evidence, source, "contract-smoke", evidencePath));
            aggregate.Runs[0].StepDeadlineMissRatio = 0;
            aggregate.Runs = aggregate.Runs.Concat(aggregate.Runs).ToArray();
            evidence.ReferenceProfile.ReportDigest = Program.WriteJson(aggregatePath, aggregate);
            RequireInvalid(() => ValidateStep3Evidence(evidence, source, "contract-smoke", evidencePath));

            var incomplete = PassingRuns();
            for (var i = 0; i < incomplete.Length; i++)
            {
                var missing = JsonNode.Parse(Report(incomplete[i]).GetRawText())!.AsObject();
                foreach (var field in new[] { "step_deadline_ms", "step_deadline_miss_count", "step_deadline_miss_ratio", "acceptance_config_sha256", "core_working_set_sample_count", "determinism_evidence" })
                    missing.Remove(field);
                var failed = Response(incomplete[i], JsonSerializer.SerializeToElement(missing));
                failed.Passed = false;
                failed.FailureCodes = ["target-incomplete"];
                incomplete[i] = ParseBenchmarkObservation(Descriptor(incomplete[i]), failed, "unmeasured.json", new string('a', 64));
            }
            var diagnostic = Evaluate(incomplete);
            if (diagnostic.Passed || diagnostic.Runs.Any(static x => x.MeasurementEvidenceComplete) || diagnostic.DeterminismDigestSummary != "")
                throw new InvalidDataException("Incomplete target must retain unmeasured FAIL diagnostics.");
            evidence.Passed = false;
            evidence.ReferenceProfile.Passed = false;
            evidence.FailureCodes = diagnostic.FailureCodes;
            evidence.DeterminismDigestSummary = "";
            Program.WriteJson(Path.Combine(directory, "gate4-step3-benchmark-evidence.json"), evidence);
            WriteStep3BlockedSmokeFragment(source, directory);
            var fragment = Program.ReadJson<EvidenceFragment>(Path.Combine(directory, "qa04-evidence-fragment.json"), "blocked smoke fragment");
            if (fragment.ReleaseEligible || fragment.PerformanceReports.Length != 3 || fragment.PerformanceReports.Any(static x => x.Passed) ||
                fragment.Soak is not { Passed: false, DurationSeconds: 0 })
                throw new InvalidDataException("Blocked smoke cannot become Step4/release evidence.");
        }
        finally { Directory.Delete(directory, recursive: true); }
        Console.WriteLine("Alpha 1.1 deadline/digest/memory/stale-evidence regressions PASS");

        BenchmarkRunDescriptor Descriptor(BenchmarkRunObservation run) => new()
        {
            RunId = run.RunId, WorkerCount = run.WorkerCount, RunOrdinal = run.RunOrdinal,
            BenchmarkProfileId = ReferenceProfile, WarmupSteps = policy.WarmupSteps, MeasurementSteps = policy.MeasurementSteps,
        };
        Qa04AdapterResponse Response(BenchmarkRunObservation run, JsonElement body) => new()
        {
            SchemaVersion = SchemaVersion, ExecutionClass = "contract-smoke", SourceCommit = source,
            RequestId = run.RunId, ProfileId = ReferenceProfile, ResponseKind = "performance-benchmark-report-v1",
            Qa04ManifestSha256 = Program.CanonicalQa04ManifestSha256, Passed = true, Report = body,
            ReferenceWorldMaterialized = false, ReleaseEvidenceCapable = false, BlockingFailureCodes = [],
        };
        JsonElement Report(BenchmarkRunObservation x) => JsonSerializer.SerializeToElement(new
        {
            benchmark_profile_id = ReferenceProfile, worker_count = x.WorkerCount, run_ordinal = x.RunOrdinal,
            build_version = "fixture", runtime_version = "fixture", hardware_profile_digest = new string('a', 64), config_digest = new string('b', 64),
            acceptance_config_sha256 = x.AcceptanceConfigSha256, step_count = x.StepSampleCount,
            step_deadline_ms = x.StepDeadlineMilliseconds, step_deadline_miss_count = x.StepDeadlineMissCount, step_deadline_miss_ratio = x.StepDeadlineMissRatio,
            step_p50_ms = 20, step_p95_ms = x.StepP95Ms, step_p99_ms = x.StepP99Ms, step_mean_60s_ms = x.Mean60sStepMs,
            domain_cpu_summary = new { measured = false, configured_worker_count = x.ConfiguredWorkerCount, effective_worker_count = x.EffectiveWorkerCount,
                max_observed_concurrency = x.MaxObservedCpuConcurrency, worker_budget_applied = x.WorkerBudgetApplied, parallel_execution_observed = x.ParallelExecutionObserved,
                operation_binding = new { }, typed_mutation = new { }, preparation = new { } },
            core_working_set_sample_count = x.CoreWorkingSetSampleCount, max_memory_bytes = x.MaxMemoryBytes,
            persistence_commit_p95_ms = x.SqliteCommitP95Ms, persistence_commit_p99_ms = x.SqliteCommitP99Ms,
            persistence_metric_observer_failure_count = x.PersistenceMetricObserverFailureCount,
            snapshot_summary = new { cow_barrier_p95_ms = x.SnapshotCowBarrierP95Ms }, publication_summary = new { },
            accepted_operation_loss = x.AcceptedOperationLoss, hidden_solver_iteration_reduction = x.HiddenSolverIterationReduction,
            final_state_digest = x.FinalStateDigest,
            determinism_evidence = new { final_state_digest = x.Determinism!.FinalStateDigest, transition_committed_digest = x.Determinism.TransitionCommittedDigest,
                operation_terminal_semantic_digest = x.Determinism.OperationTerminalSemanticDigest, config_history_digest = x.Determinism.ConfigHistoryDigest,
                promotion_deferral_order_digest = x.Determinism.PromotionDeferralOrderDigest }, failure_codes = Array.Empty<string>(),
        });
    }

    private static void RequireInvalid(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is InvalidDataException or KeyNotFoundException or InvalidOperationException) { return; }
        throw new InvalidDataException("Alpha 1.1 invalid evidence was accepted.");
    }
}
