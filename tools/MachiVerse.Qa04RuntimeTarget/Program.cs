using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class Program
{
    private const string CanonicalManifestSha256 = "5de8301439ca57080eefa599da284f9271b29366c791bcb9c2f85ddbfa041423";
    private const string CanonicalWorldSeedHex = "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f";
    private const string ReferenceProfile = "perf.reference.v1";
    private const string PersistenceProfile = "perf.persistence.v1";
    private const string PublicationProfile = "perf.publication.v1";
    private const string SoakProfile = "performance.soak.24h";
    private const ulong CanonicalTerminalOperationCount = 136_450_000UL;
    private const string BenchmarkMeasurementCode = "qa04.measurement.step-sample-count";
    private const double MissingMetricSentinelMilliseconds = 1_000_000_000.0;
    private static readonly string[] PersistenceCrashStages =
    [
        "audit-append",
        "migration-generation-switch",
        "operation-acceptance",
        "operation-scheduling",
        "snapshot-commit",
        "transition-commit",
    ];
    private static readonly string[] PersistenceCrashPoints =
    [
        "before-db-begin",
        "before-fsync-or-commit",
        "before-response-or-publication",
        "immediately-after-commit",
        "mid-write",
    ];

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
                "persistence-stress" => await PersistenceAsync(request, coreExecutable, RequireGatewayExecutable()),
                "publication-stress" => await PublicationAsync(request, coreExecutable, RequireGatewayExecutable()),
                "soak-run" => await SoakAsync(request, coreExecutable, RequireGatewayExecutable()),
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

    private static string RequireGatewayExecutable()
    {
        var executable = Environment.GetEnvironmentVariable("MACHIVERSE_QA04_GATEWAY_EXECUTABLE");
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            throw new FileNotFoundException(
                "MACHIVERSE_QA04_GATEWAY_EXECUTABLE must identify the assembled Gateway executable.",
                executable);
        return executable;
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
        if (!string.Equals(run.WorldSeed, CanonicalWorldSeedHex, StringComparison.Ordinal))
            throw new InvalidDataException("benchmark-run worldSeed is not canonical.");

        var inspection = await InspectCoreAsync(coreExecutable);
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
            !probe.WorkerCountAppliedToDomainExecutor ||
            !probe.ReferenceWorldMaterialized ||
            probe.ReleaseEvidenceCapable)
            throw new InvalidDataException("Simulation Core worker probe did not prove the requested worker count and reference-world readiness boundary.");

        if (string.Equals(request.ExecutionClass, "contract-smoke", StringComparison.Ordinal))
        {
            if (ConnectionProbeRequested(request.Profile))
                return await ContractSmokeConnectionProbeAsync(request, run, inspection, probe, coreExecutable);
            return ContractSmokeBenchmark(request, run, inspection, probe);
        }

        return await ReleaseBenchmarkAsync(request, run, inspection, probe, coreExecutable);
    }

    private static Response ContractSmokeBenchmark(
        Request request,
        RunDescriptor run,
        Inspection inspection,
        WorkerProbe probe)
    {
        var failures = MergeFailures(
            inspection.BlockingFailureCodes.Concat(probe.BlockingFailureCodes),
            BenchmarkMeasurementCode);
        return NewResponse(
            request,
            "performance-benchmark-report-v1",
            inspection.ReferenceWorldMaterialized,
            releaseEvidenceCapable: false,
            failures,
            passed: false,
            failures,
            new
            {
                benchmark_profile_id = ReferenceProfile,
                build_version = request.SourceCommit,
                runtime_version = "assembled-core-contract-smoke",
                hardware_profile_digest = HardwareProfileDigest(),
                config_digest = inspection.CanonicalConfigDigest,
                worker_count = run.WorkerCount,
                run_ordinal = run.RunOrdinal,
                step_count = 0,
                step_p50_ms = MissingMetricSentinelMilliseconds,
                step_p95_ms = MissingMetricSentinelMilliseconds,
                step_p99_ms = MissingMetricSentinelMilliseconds,
                step_mean_60s_ms = MissingMetricSentinelMilliseconds,
                domain_cpu_summary = new
                {
                    measured = false,
                    configured_worker_count = probe.WorkerCount,
                    max_observed_probe_concurrency = probe.MaxObservedConcurrency,
                },
                max_memory_bytes = long.MaxValue,
                persistence_commit_p95_ms = MissingMetricSentinelMilliseconds,
                persistence_commit_p99_ms = MissingMetricSentinelMilliseconds,
                snapshot_summary = new { cow_barrier_p95_ms = MissingMetricSentinelMilliseconds, measured = false },
                publication_summary = new { measured = false },
                final_state_digest = new string('f', 64),
                accepted_operation_loss = 0,
                hidden_solver_iteration_reduction = false,
                failure_codes = failures,
            });
    }

    private static async Task<Response> ContractSmokeConnectionProbeAsync(
        Request request,
        RunDescriptor run,
        Inspection inspection,
        WorkerProbe workerProbe,
        string coreExecutable)
    {
        if (!inspection.ProductionReferenceConnectionProbeAvailable)
            throw new InvalidDataException("Simulation Core production reference connection probe is not available.");
        if (inspection.BlockingFailureCodes.Length != 0)
            throw new InvalidDataException("Simulation Core production reference connection probe still has blocking failures.");

        var persistenceRoot = Path.Combine(
            Path.GetTempPath(),
            "machiverse-qa04-connection-" + Guid.NewGuid().ToString("N"));
        try
        {
            var connection = await InvokeCoreAsync<ProductionReferenceConnectionProbe>(
                coreExecutable,
                new
                {
                    schemaVersion = "1.0",
                    command = "production-reference-connection-probe",
                    workerCount = run.WorkerCount,
                    persistenceRoot,
                },
                timeout: null);
            ValidateProductionReferenceConnectionProbe(connection, run);

            var failures = MergeFailures(Array.Empty<string>(), BenchmarkMeasurementCode);
            return NewResponse(
                request,
                "performance-benchmark-report-v1",
                referenceWorldMaterialized: true,
                releaseEvidenceCapable: false,
                blockingFailureCodes: [],
                passed: false,
                failures,
                new
                {
                    benchmark_profile_id = ReferenceProfile,
                    build_version = request.SourceCommit,
                    runtime_version = "assembled-core-production-connection-probe.v1",
                    hardware_profile_digest = HardwareProfileDigest(),
                    config_digest = inspection.CanonicalConfigDigest,
                    worker_count = run.WorkerCount,
                    run_ordinal = run.RunOrdinal,
                    step_count = 0,
                    step_p50_ms = MissingMetricSentinelMilliseconds,
                    step_p95_ms = MissingMetricSentinelMilliseconds,
                    step_p99_ms = MissingMetricSentinelMilliseconds,
                    step_mean_60s_ms = MissingMetricSentinelMilliseconds,
                    domain_cpu_summary = new
                    {
                        measured = false,
                        configured_worker_count = workerProbe.WorkerCount,
                        max_observed_probe_concurrency = workerProbe.MaxObservedConcurrency,
                    },
                    max_memory_bytes = long.MaxValue,
                    persistence_commit_p95_ms = MissingMetricSentinelMilliseconds,
                    persistence_commit_p99_ms = MissingMetricSentinelMilliseconds,
                    snapshot_summary = new { cow_barrier_p95_ms = MissingMetricSentinelMilliseconds, measured = false },
                    publication_summary = new { measured = false },
                    final_state_digest = connection.FinalStateDigest,
                    accepted_operation_loss = 0,
                    hidden_solver_iteration_reduction = false,
                    connection_probe = new
                    {
                        production_executor_observed = connection.ProductionExecutorObserved,
                        real_sqlite_commit_observed = connection.RealSqliteCommitObserved,
                        transition_count = connection.TransitionCount,
                        basis_step = connection.BasisStep,
                        finalized_step = connection.FinalizedStep,
                        reference_initial_record_count = connection.ReferenceInitialRecordCount,
                        domain_authority_count = connection.DomainAuthorityCount,
                        operation_count = connection.OperationCount,
                        final_history_sequence = connection.FinalHistorySequence,
                        final_history_digest = connection.FinalHistoryDigest,
                        final_continuity_token = connection.FinalContinuityToken,
                        candidate_id_sequence_digest = connection.CandidateIdSequenceDigest,
                    },
                    failure_codes = failures,
                });
        }
        finally
        {
            if (Directory.Exists(persistenceRoot))
                Directory.Delete(persistenceRoot, recursive: true);
        }
    }

    private static void ValidateProductionReferenceConnectionProbe(
        ProductionReferenceConnectionProbe connection,
        RunDescriptor run)
    {
        if (!string.Equals(connection.SchemaVersion, "1.0", StringComparison.Ordinal) ||
            !string.Equals(connection.ProfileId, ReferenceProfile, StringComparison.Ordinal) ||
            connection.WorkerCount != run.WorkerCount ||
            connection.TransitionCount != 2 ||
            connection.BasisStep != 1 ||
            connection.FinalizedStep != 3 ||
            connection.ReferenceInitialRecordCount != 6_760_000 ||
            connection.DomainAuthorityCount != 97 ||
            connection.OperationCount != 10_000 ||
            !connection.RealSqliteCommitObserved ||
            !connection.ProductionExecutorObserved)
            throw new InvalidDataException("Simulation Core production reference connection probe drifted from the bounded Gate-4 contract.");
        RequireLowerHex(connection.FinalStateDigest, 64, "connection finalStateDigest");
        RequireLowerHex(connection.FinalHistoryDigest, 64, "connection finalHistoryDigest");
        RequireLowerHex(connection.FinalContinuityToken, 64, "connection finalContinuityToken");
        RequireLowerHex(connection.CandidateIdSequenceDigest, 64, "connection candidateIdSequenceDigest");
    }

    private static async Task<Response> ReleaseBenchmarkAsync(
        Request request,
        RunDescriptor run,
        Inspection inspection,
        WorkerProbe probe,
        string coreExecutable)
    {
        if (!inspection.ProductionReferenceRunAvailable)
            throw new InvalidDataException("Simulation Core production reference run is not available.");
        if (inspection.BlockingFailureCodes.Length != 0)
            throw new InvalidDataException("Simulation Core production reference run still has blocking failures.");

        var persistenceRoot = Path.Combine(
            Path.GetTempPath(),
            "machiverse-qa04-release-" + Guid.NewGuid().ToString("N"));
        try
        {
            var production = await InvokeCoreAsync<ProductionReferenceRun>(
                coreExecutable,
                new
                {
                    schemaVersion = "1.0",
                    command = "production-reference-run",
                    workerCount = run.WorkerCount,
                    persistenceRoot,
                },
                timeout: null);
            var determinism = ValidateProductionReferenceRun(production, run);

            var failures = MergeFailures(production.FailureCodes);
            var measurement = production.Measurement;
            var step = measurement.StepDuration;
            var commit = measurement.SqliteCommitDuration;
            var cow = measurement.SnapshotCowBarrierDuration;
            var passed = production.Passed && production.PerformanceThresholdsPassed && failures.Length == 0;

            return NewResponse(
                request,
                "performance-benchmark-report-v1",
                referenceWorldMaterialized: true,
                releaseEvidenceCapable: false,
                blockingFailureCodes: [],
                passed,
                failures,
                new
                {
                    benchmark_profile_id = ReferenceProfile,
                    build_version = request.SourceCommit,
                    runtime_version = "assembled-core-production-reference-run.v1",
                    hardware_profile_digest = HardwareProfileDigest(),
                    config_digest = inspection.CanonicalConfigDigest,
                    worker_count = run.WorkerCount,
                    run_ordinal = run.RunOrdinal,
                    production_transition_count = production.TransitionCount,
                    terminal_operation_count = production.TerminalOperationCount,
                    finalized_step = production.FinalizedStep,
                    step_count = measurement.StepSampleCount,
                    step_p50_ms = Milliseconds(step?.P50),
                    step_p95_ms = Milliseconds(step?.P95),
                    step_p99_ms = Milliseconds(step?.P99),
                    step_mean_60s_ms = measurement.MaxRolling60SecondMeanMilliseconds ?? MissingMetricSentinelMilliseconds,
                    domain_cpu_summary = new
                    {
                        measured = false,
                        configured_worker_count = probe.WorkerCount,
                        max_observed_probe_concurrency = probe.MaxObservedConcurrency,
                        production_transition_count = production.TransitionCount,
                        candidate_id_sequence_digest = production.CandidateIdSequenceDigest,
                    },
                    max_memory_bytes = measurement.MaxCoreWorkingSetBytes,
                    persistence_commit_p95_ms = Milliseconds(commit?.P95),
                    persistence_commit_p99_ms = Milliseconds(commit?.P99),
                    snapshot_summary = new
                    {
                        cow_barrier_p95_ms = Milliseconds(cow?.P95),
                        measured = cow is not null,
                        snapshot_step = production.SnapshotStep,
                        drain_completed = production.SnapshotDrainCompleted,
                        section_count = production.SnapshotSectionCount,
                        chunk_count = production.SnapshotChunkCount,
                        snapshot_digest = production.SnapshotDigest,
                        physical_manifest_digest = production.SnapshotPhysicalManifestDigest,
                        recovered_state_digest = production.SnapshotRecoveredStateDigest,
                    },
                    publication_summary = new { measured = false, profile = PublicationProfile },
                    determinism_evidence = new
                    {
                        final_state_digest = determinism.FinalStateDigest,
                        transition_committed_digest = determinism.TransitionCommittedDigest,
                        operation_terminal_semantic_digest = determinism.OperationTerminalSemanticDigest,
                        config_history_digest = determinism.ConfigHistoryDigest,
                        promotion_deferral_order_digest = determinism.PromotionDeferralOrderDigest,
                    },
                    final_state_digest = production.FinalStateDigest,
                    final_history_sequence = production.FinalHistorySequence,
                    final_history_digest = production.FinalHistoryDigest,
                    final_continuity_token = production.FinalContinuityToken,
                    candidate_id_sequence_digest = production.CandidateIdSequenceDigest,
                    accepted_operation_loss = production.AcceptedOperationLoss,
                    hidden_solver_iteration_reduction = production.HiddenSolverIterationReduction,
                    persistence_metric_observer_failure_count = production.PersistenceMetricObserverFailureCount,
                    failure_codes = failures,
                });
        }
        finally
        {
            if (Directory.Exists(persistenceRoot))
                Directory.Delete(persistenceRoot, recursive: true);
        }
    }

    private static ProductionDeterminismEvidence ValidateProductionReferenceRun(ProductionReferenceRun production, RunDescriptor run)
    {
        if (!string.Equals(production.SchemaVersion, "1.0", StringComparison.Ordinal) ||
            !string.Equals(production.ProfileId, ReferenceProfile, StringComparison.Ordinal) ||
            production.WorkerCount != run.WorkerCount ||
            production.TransitionCount != 27_000 ||
            production.TerminalOperationCount != CanonicalTerminalOperationCount ||
            production.FinalizedStep != 27_001 ||
            !production.SnapshotCowFrozen ||
            production.SnapshotStep != 18_000 ||
            !production.SnapshotDrainCompleted ||
            production.SnapshotSectionCount != 103 ||
            production.SnapshotChunkCount <= 0)
            throw new InvalidDataException("Simulation Core production reference run result drifted from the canonical run contract.");

        if (production.Measurement.StepSampleCount != 18_000)
            throw new InvalidDataException("Simulation Core production reference run did not produce 18,000 measurement Step samples.");

        RequireLowerHex(production.FinalStateDigest, 64, "finalStateDigest");
        RequireLowerHex(production.FinalHistoryDigest, 64, "finalHistoryDigest");
        RequireLowerHex(production.FinalContinuityToken, 64, "finalContinuityToken");
        RequireLowerHex(production.CandidateIdSequenceDigest, 64, "candidateIdSequenceDigest");
        RequireLowerHex(production.SnapshotDigest, 64, "snapshotDigest");
        RequireLowerHex(production.SnapshotPhysicalManifestDigest, 64, "snapshotPhysicalManifestDigest");
        RequireLowerHex(production.SnapshotRecoveredStateDigest, 64, "snapshotRecoveredStateDigest");

        var evidence = production.DeterminismEvidence
            ?? throw new InvalidDataException("Simulation Core production reference run is missing determinismEvidence.");
        RequireLowerHex(evidence.FinalStateDigest, 64, "determinismEvidence.finalStateDigest");
        RequireLowerHex(evidence.TransitionCommittedDigest, 64, "determinismEvidence.transitionCommittedDigest");
        RequireLowerHex(evidence.OperationTerminalSemanticDigest, 64, "determinismEvidence.operationTerminalSemanticDigest");
        RequireLowerHex(evidence.ConfigHistoryDigest, 64, "determinismEvidence.configHistoryDigest");
        RequireLowerHex(evidence.PromotionDeferralOrderDigest, 64, "determinismEvidence.promotionDeferralOrderDigest");
        if (!string.Equals(evidence.FinalStateDigest, production.FinalStateDigest, StringComparison.Ordinal))
            throw new InvalidDataException("Simulation Core determinism final State digest does not match the production run result.");
        return evidence;
    }

    private static async Task<Response> PersistenceAsync(
        Request request,
        string coreExecutable,
        string gatewayExecutable)
    {
        RequireProfile(request, PersistenceProfile);
        var inspection = await InspectCoreAsync(coreExecutable);
        ValidatePersistenceProfile(request.Profile);

        var root = Path.Combine(
            Path.GetTempPath(),
            "machiverse-qa04-persistence-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var volume = await InvokeCoreAsync<PersistenceVolumeStress>(
                coreExecutable,
                new
                {
                    schemaVersion = "1.0",
                    command = "persistence-volume-stress-run",
                    persistenceRoot = Path.Combine(root, "volume"),
                    targetStoredGiB = 16,
                },
                timeout: null);
            if (!string.Equals(volume.SchemaVersion, "1.0", StringComparison.Ordinal) ||
                volume.TargetStoredGiB != 16 ||
                volume.StoredBytes < 16L * 1024L * 1024L * 1024L ||
                volume.ChunkCount <= 0 ||
                !volume.ZstdRoundTripValidated)
                throw new InvalidDataException("QA-04 persistence 16GiB compressed volume target did not complete canonically.");

            var caseResults = new List<PersistenceCrashCaseResult>(30);
            foreach (var stage in PersistenceCrashStages)
            foreach (var point in PersistenceCrashPoints)
            {
                var caseRoot = Path.Combine(root, "cases", stage, point);
                Directory.CreateDirectory(caseRoot);
                var expectedDurable = point is "immediately-after-commit" or "before-response-or-publication";

                if (string.Equals(stage, "audit-append", StringComparison.Ordinal))
                {
                    await InvokeCrashTargetAsync(
                        gatewayExecutable,
                        new
                        {
                            schemaVersion = "1.0",
                            command = "audit-crash-case-run",
                            dataRoot = caseRoot,
                        },
                        stage,
                        point).ConfigureAwait(false);
                    var verified = await InvokeGatewayAsync<PersistenceCrashVerification>(
                        gatewayExecutable,
                        new
                        {
                            schemaVersion = "1.0",
                            command = "audit-crash-case-verify",
                            dataRoot = caseRoot,
                            expectedDurable,
                        }).ConfigureAwait(false);
                    ValidateCrashVerification(verified, stage, expectedDurable);
                }
                else
                {
                    await InvokeCrashTargetAsync(
                        coreExecutable,
                        new
                        {
                            schemaVersion = "1.0",
                            command = "persistence-crash-case-run",
                            crashStage = stage,
                            persistenceRoot = caseRoot,
                        },
                        stage,
                        point).ConfigureAwait(false);
                    var verified = await InvokeCoreAsync<PersistenceCrashVerification>(
                        coreExecutable,
                        new
                        {
                            schemaVersion = "1.0",
                            command = "persistence-crash-case-verify",
                            crashStage = stage,
                            persistenceRoot = caseRoot,
                            expectedDurable,
                        }).ConfigureAwait(false);
                    ValidateCrashVerification(verified, stage, expectedDurable);
                }

                caseResults.Add(new PersistenceCrashCaseResult(stage, point, expectedDurable));
            }

            var failures = inspection.BlockingFailureCodes
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static x => x, StringComparer.Ordinal)
                .ToArray();
            var passed = failures.Length == 0 && caseResults.Count == 30;
            return NewResponse(
                request,
                "persistence-stress-report-v1",
                inspection.ReferenceWorldMaterialized,
                releaseEvidenceCapable: false,
                failures,
                passed,
                failures,
                new
                {
                    profile_id = PersistenceProfile,
                    compressed_snapshot_gib = 16,
                    compressed_snapshot_stored_bytes = volume.StoredBytes,
                    compressed_snapshot_chunk_count = volume.ChunkCount,
                    zstd_roundtrip_validated = volume.ZstdRoundTripValidated,
                    history_tail_minutes = 10,
                    crash_case_count = caseResults.Count,
                    crash_cases = caseResults,
                    no_durable_fact_loss = true,
                    no_uncommitted_candidate_publication = true,
                    history_chain_valid = true,
                    failure_codes = failures,
                });
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<Response> PublicationAsync(
        Request request,
        string coreExecutable,
        string gatewayExecutable)
    {
        RequireProfile(request, PublicationProfile);
        var inspection = await InspectCoreAsync(coreExecutable);
        ValidatePublicationProfile(request.Profile);

        var target = await InvokeGatewayAsync<PublicationStressTarget>(
            gatewayExecutable,
            new
            {
                schemaVersion = "1.0",
                command = "publication-stress-run",
            }).ConfigureAwait(false);

        var failures = inspection.BlockingFailureCodes
            .Concat(target.FailureCodes)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static x => x, StringComparer.Ordinal)
            .ToArray();
        if (!string.Equals(target.SchemaVersion, "1.0", StringComparison.Ordinal) ||
            !string.Equals(target.ProfileId, PublicationProfile, StringComparison.Ordinal) ||
            target.GatewayCount != 1 ||
            target.ViewSubscribers != 100 ||
            target.SlowConsumers != 10)
            throw new InvalidDataException("QA-04 Gateway publication target cardinality/profile drift.");
        if (!target.SlowConsumersDidNotBlockCustodyOrResult ||
            !target.ContinuityAfterCoalesceResync ||
            target.SlowConsumerResyncCount != 10 ||
            target.TotalCoalescedPublicationCount == 0 ||
            target.FinalPendingPublicationCount != 0 ||
            target.FinalPendingResultCount != 0)
            throw new InvalidDataException("QA-04 Gateway publication target did not prove coalesce/resync and result isolation.");
        var passed = target.Passed && failures.Length == 0;

        return NewResponse(
            request,
            "publication-stress-report-v1",
            inspection.ReferenceWorldMaterialized,
            releaseEvidenceCapable: false,
            failures,
            passed,
            failures,
            new
            {
                profile_id = PublicationProfile,
                gateway_count = target.GatewayCount,
                view_subscribers = target.ViewSubscribers,
                slow_consumers = target.SlowConsumers,
                slow_consumers_did_not_block_custody_or_result = target.SlowConsumersDidNotBlockCustodyOrResult,
                continuity_after_coalesce_resync = target.ContinuityAfterCoalesceResync,
                slow_consumer_resync_count = target.SlowConsumerResyncCount,
                total_coalesced_publication_count = target.TotalCoalescedPublicationCount,
                failure_codes = failures,
            });
    }

    private static async Task<Response> SoakAsync(Request request, string coreExecutable)
    {
        RequireProfile(request, SoakProfile);
        var inspection = await InspectCoreAsync(coreExecutable);
        var failures = MergeFailures(inspection.BlockingFailureCodes, "qa04.target.soak-not-assembled");
        return NewResponse(
            request,
            "soak-report-v1",
            inspection.ReferenceWorldMaterialized,
            releaseEvidenceCapable: false,
            failures,
            passed: false,
            failures,
            new
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
            !inspection.ReferenceWorldMaterialized || !inspection.AuthoritativeStepLoopAvailable || inspection.ReleaseEvidenceCapable ||
            !inspection.ProductionReferenceConnectionProbeAvailable || !inspection.ProductionReferenceRunAvailable)
            throw new InvalidDataException("Simulation Core QA-04 target inspection boundary is inconsistent.");
        if (!inspection.CanonicalWorkerCounts.SequenceEqual(new[] { 1, 4, 8, 16 }))
            throw new InvalidDataException("Simulation Core QA-04 canonical worker set drifted.");
        RequireLowerHex(inspection.CanonicalConfigDigest, 64, "canonicalConfigDigest");
        if (inspection.BlockingFailureCodes.Contains("qa04.target.authoritative-step-loop-not-assembled", StringComparer.Ordinal) ||
            inspection.BlockingFailureCodes.Contains("qa04.target.reference-world-not-materialized", StringComparer.Ordinal))
            throw new InvalidDataException("Simulation Core QA-04 target blocking boundary drifted after Gate 2 completion.");
        return inspection;
    }

    private static Task<T> InvokeCoreAsync<T>(string coreExecutable, object request)
        => InvokeCoreAsync<T>(coreExecutable, request, TimeSpan.FromSeconds(15));

    private static async Task<T> InvokeCoreAsync<T>(
        string coreExecutable,
        object request,
        TimeSpan? timeout)
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
        if (timeout is { } bounded)
        {
            using var timeoutSource = new CancellationTokenSource(bounded);
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        else
        {
            await process.WaitForExitAsync();
        }
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
            throw new InvalidDataException($"Simulation Core QA-04 target exited {process.ExitCode}: {Limit(stderr, 2000)}");
        var lines = stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length != 1)
            throw new InvalidDataException($"Simulation Core QA-04 target must emit exactly one JSON line; found {lines.Length}.");
        return JsonSerializer.Deserialize<T>(lines[0], Json)
            ?? throw new InvalidDataException("Simulation Core QA-04 target response decoded to null.");
    }

    private static void ValidatePersistenceProfile(JsonElement profile)
    {
        if (profile.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("perf.persistence.v1 profile must be an object.");
        if (profile.GetProperty("compressedSnapshotGiB").GetInt32() != 16 ||
            profile.GetProperty("historyTailMinutes").GetInt32() != 10 ||
            !profile.GetProperty("noDurableFactLoss").GetBoolean() ||
            !profile.GetProperty("noUncommittedCandidatePublication").GetBoolean())
            throw new InvalidDataException("perf.persistence.v1 scalar profile drift.");

        var stages = profile.GetProperty("crashInjectionStages").EnumerateArray()
            .Select(static x => x.GetString() ?? "")
            .OrderBy(static x => x, StringComparer.Ordinal)
            .ToArray();
        var points = profile.GetProperty("crashInjectionPoints").EnumerateArray()
            .Select(static x => x.GetString() ?? "")
            .OrderBy(static x => x, StringComparer.Ordinal)
            .ToArray();
        if (!stages.SequenceEqual(PersistenceCrashStages, StringComparer.Ordinal) ||
            !points.SequenceEqual(PersistenceCrashPoints, StringComparer.Ordinal))
            throw new InvalidDataException("perf.persistence.v1 crash matrix drift.");
    }

    private static void ValidatePublicationProfile(JsonElement profile)
    {
        if (profile.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("perf.publication.v1 profile must be an object.");
        if (profile.GetProperty("gatewayCount").GetInt32() != 1 ||
            profile.GetProperty("viewSubscribers").GetInt32() != 100 ||
            profile.GetProperty("slowConsumers").GetInt32() != 10 ||
            !profile.GetProperty("slowConsumersMustNotBlockCustodyOrResult").GetBoolean() ||
            !profile.GetProperty("continuityAfterCoalesceResyncRequired").GetBoolean())
            throw new InvalidDataException("perf.publication.v1 profile drift.");
    }

    private static void ValidateCrashVerification(
        PersistenceCrashVerification verification,
        string stage,
        bool expectedDurable)
    {
        if (!string.Equals(verification.SchemaVersion, "1.0", StringComparison.Ordinal) ||
            !string.Equals(verification.Stage, stage, StringComparison.Ordinal) ||
            verification.ExpectedDurable != expectedDurable ||
            verification.DurableFactPresent != expectedDurable ||
            !verification.HistoryChainValid ||
            !verification.NoHalfTransition)
            throw new InvalidDataException($"QA-04 persistence crash verification failed for {stage}.");
    }

    private static Task<T> InvokeGatewayAsync<T>(string gatewayExecutable, object request)
        => InvokeTargetAsync<T>(
            gatewayExecutable,
            request,
            TimeSpan.FromSeconds(30),
            "Gateway");

    private static async Task<T> InvokeTargetAsync<T>(
        string executable,
        object request,
        TimeSpan? timeout,
        string componentName)
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
        startInfo.ArgumentList.Add("qa04-target");
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException($"Failed to start assembled {componentName} QA-04 target process.");
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, Json));
        process.StandardInput.Close();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            if (timeout is { } bounded)
            {
                using var timeoutSource = new CancellationTokenSource(bounded);
                await process.WaitForExitAsync(timeoutSource.Token);
            }
            else
            {
                await process.WaitForExitAsync();
            }
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
            throw new InvalidDataException($"{componentName} QA-04 target exited {process.ExitCode}: {Limit(stderr, 2000)}");
        var lines = stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length != 1)
            throw new InvalidDataException($"{componentName} QA-04 target must emit exactly one JSON line; found {lines.Length}.");
        return JsonSerializer.Deserialize<T>(lines[0], Json)
            ?? throw new InvalidDataException($"{componentName} QA-04 target response decoded to null.");
    }

    private static async Task InvokeCrashTargetAsync(
        string executable,
        object request,
        string stage,
        string point)
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
        startInfo.ArgumentList.Add("qa04-target");
        startInfo.Environment["MACHIVERSE_QA04_PERSISTENCE_CRASH_ARMED"] = "1";
        startInfo.Environment["MACHIVERSE_QA04_PERSISTENCE_CRASH_STAGE"] = stage;
        startInfo.Environment["MACHIVERSE_QA04_PERSISTENCE_CRASH_POINT"] = point;

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException($"Failed to start QA-04 crash case {stage}/{point}.");
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, Json));
        process.StandardInput.Close();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            using var timeoutSource = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode == 0)
            throw new InvalidDataException($"QA-04 crash case {stage}/{point} completed without the required process crash.");
        if (!string.IsNullOrWhiteSpace(stdout))
            throw new InvalidDataException($"QA-04 crash case {stage}/{point} exposed a response before crash: {Limit(stdout, 1000)}");
        if (string.IsNullOrWhiteSpace(stderr))
            throw new InvalidDataException($"QA-04 crash case {stage}/{point} produced no crash diagnostic.");
    }

    private static Response NewResponse(
        Request request,
        string responseKind,
        bool referenceWorldMaterialized,
        bool releaseEvidenceCapable,
        string[] blockingFailureCodes,
        bool passed,
        string[] failures,
        object report)
        => new()
        {
            SchemaVersion = "1.0",
            ResponseKind = responseKind,
            ExecutionClass = request.ExecutionClass,
            RequestId = request.RequestId,
            SourceCommit = request.SourceCommit,
            Qa04ManifestSha256 = request.Qa04ManifestSha256,
            ProfileId = request.ProfileId,
            ReferenceWorldMaterialized = referenceWorldMaterialized,
            ReleaseEvidenceCapable = releaseEvidenceCapable,
            BlockingFailureCodes = blockingFailureCodes,
            Passed = passed,
            FailureCodes = failures,
            Report = JsonSerializer.SerializeToElement(report, Json),
        };

    private static bool ConnectionProbeRequested(JsonElement profile)
        => profile.ValueKind == JsonValueKind.Object &&
           profile.TryGetProperty("connectionProbe", out var requested) &&
           requested.ValueKind == JsonValueKind.True;

    private static string HardwareProfileDigest()
    {
        var material = string.Join("\n", new[]
        {
            RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture.ToString(),
            RuntimeInformation.ProcessArchitecture.ToString(),
            RuntimeInformation.FrameworkDescription,
            Environment.ProcessorCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }

    private static double Milliseconds(TimeSpan? duration)
        => duration?.TotalMilliseconds ?? MissingMetricSentinelMilliseconds;

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
        public bool ReferenceWorldMaterialized { get; set; }
        public bool ReleaseEvidenceCapable { get; set; }
        public string[] BlockingFailureCodes { get; set; } = [];
        public bool Passed { get; set; }
        public string[] FailureCodes { get; set; } = [];
        public JsonElement Report { get; set; }
    }

    private sealed class Inspection
    {
        public string SchemaVersion { get; set; } = "";
        public string ProfileId { get; set; } = "";
        public string CanonicalConfigDigest { get; set; } = "";
        public int[] CanonicalWorkerCounts { get; set; } = [];
        public int StandardDomainCount { get; set; }
        public int StandardPartitionCount { get; set; }
        public bool ProductionReferenceConnectionProbeAvailable { get; set; }
        public bool ProductionReferenceRunAvailable { get; set; }
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

    private sealed class ProductionReferenceConnectionProbe
    {
        public string SchemaVersion { get; set; } = "";
        public string ProfileId { get; set; } = "";
        public int WorkerCount { get; set; }
        public int TransitionCount { get; set; }
        public ulong BasisStep { get; set; }
        public ulong FinalizedStep { get; set; }
        public ulong ReferenceInitialRecordCount { get; set; }
        public int DomainAuthorityCount { get; set; }
        public int OperationCount { get; set; }
        public string FinalStateDigest { get; set; } = "";
        public ulong FinalHistorySequence { get; set; }
        public string FinalHistoryDigest { get; set; } = "";
        public string FinalContinuityToken { get; set; } = "";
        public string CandidateIdSequenceDigest { get; set; } = "";
        public bool RealSqliteCommitObserved { get; set; }
        public bool ProductionExecutorObserved { get; set; }
    }

    private sealed class ProductionReferenceRun
    {
        public string SchemaVersion { get; set; } = "";
        public string ProfileId { get; set; } = "";
        public int WorkerCount { get; set; }
        public int TransitionCount { get; set; }
        public ulong TerminalOperationCount { get; set; }
        public ulong FinalizedStep { get; set; }
        public string FinalStateDigest { get; set; } = "";
        public ProductionDeterminismEvidence? DeterminismEvidence { get; set; }
        public ulong FinalHistorySequence { get; set; }
        public string FinalHistoryDigest { get; set; } = "";
        public string FinalContinuityToken { get; set; } = "";
        public string CandidateIdSequenceDigest { get; set; } = "";
        public bool SnapshotCowFrozen { get; set; }
        public ulong SnapshotStep { get; set; }
        public bool SnapshotDrainCompleted { get; set; }
        public int SnapshotSectionCount { get; set; }
        public int SnapshotChunkCount { get; set; }
        public string SnapshotDigest { get; set; } = "";
        public string SnapshotPhysicalManifestDigest { get; set; } = "";
        public string SnapshotRecoveredStateDigest { get; set; } = "";
        public MeasurementSnapshot Measurement { get; set; } = new();
        public bool PerformanceThresholdsPassed { get; set; }
        public int AcceptedOperationLoss { get; set; }
        public bool HiddenSolverIterationReduction { get; set; }
        public long PersistenceMetricObserverFailureCount { get; set; }
        public bool Passed { get; set; }
        public string[] FailureCodes { get; set; } = [];
    }

    private sealed class ProductionDeterminismEvidence
    {
        public string FinalStateDigest { get; set; } = "";
        public string TransitionCommittedDigest { get; set; } = "";
        public string OperationTerminalSemanticDigest { get; set; } = "";
        public string ConfigHistoryDigest { get; set; } = "";
        public string PromotionDeferralOrderDigest { get; set; } = "";
    }

    private sealed class MeasurementSnapshot
    {
        public int StepSampleCount { get; set; }
        public DurationSummary? StepDuration { get; set; }
        public double? MaxRolling60SecondMeanMilliseconds { get; set; }
        public DurationSummary? SqliteCommitDuration { get; set; }
        public DurationSummary? SnapshotCowBarrierDuration { get; set; }
        public DurationSummary? GcPauseDuration { get; set; }
        public int CoreWorkingSetSampleCount { get; set; }
        public long MaxCoreWorkingSetBytes { get; set; }
    }

    private sealed class DurationSummary
    {
        public int SampleCount { get; set; }
        public TimeSpan Minimum { get; set; }
        public TimeSpan P50 { get; set; }
        public TimeSpan P95 { get; set; }
        public TimeSpan P99 { get; set; }
        public TimeSpan Maximum { get; set; }
        public double MeanMilliseconds { get; set; }
    }
}
