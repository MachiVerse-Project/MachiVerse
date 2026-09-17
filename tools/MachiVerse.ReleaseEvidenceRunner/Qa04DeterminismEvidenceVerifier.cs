using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class Qa04DeterminismEvidenceVerifier
{
    private const string ProfileId = "perf.reference.v1";
    private const string SummaryDomain = "qa04.determinism-evidence.v1";
    private const int CanonicalTransitionCount = 27_000;
    private const ulong CanonicalTerminalOperationCount = 136_450_000UL;
    private const ulong CanonicalFinalizedStep = 27_001UL;
    private const int CanonicalMeasurementStepCount = 18_000;
    private const ulong CanonicalSnapshotStep = 18_000UL;
    private const int CanonicalSnapshotSectionCount = 103;

    internal static void VerifyContract()
    {
        var rows = Enumerable.Range(1, 12)
            .Select(index => new EvidenceRow(
                $"selftest.{index:D2}",
                new string('a', 64),
                new string('b', 64),
                new string('c', 64),
                new string('d', 64),
                new string('e', 64)))
            .ToArray();

        var summary = ValidateAndSummarize(rows);
        Program.RequireLowerHex(summary, 64, "QA-04 determinism summary self-test");

        var mismatch = rows.ToArray();
        mismatch[11] = mismatch[11] with { PromotionDeferralOrderDigest = new string('f', 64) };
        RequireThrows<InvalidDataException>(
            () => ValidateAndSummarize(mismatch),
            "QA-04 determinism evidence verifier must reject a cross-run promotion-order mismatch.");

        var actualRows = new List<Gate4Step2ActualRunEvidenceRow>(12);
        foreach (var worker in new[] { 1, 4, 8, 16 })
        foreach (var ordinal in new[] { 1, 2, 3 })
            actualRows.Add(SelfTestActualRow(worker, ordinal));
        var actualSummary = ValidateActualMatrix(actualRows);
        Program.RequireLowerHex(actualSummary, 64, "QA-04 actual Step2 determinism summary self-test");

        var badTransitionCount = actualRows
            .Select(static row => CloneActualRow(row))
            .ToArray();
        badTransitionCount[0].TransitionCount--;
        RequireThrows<InvalidDataException>(
            () => ValidateActualMatrix(badTransitionCount),
            "QA-04 actual Step2 verifier must reject a non-canonical transition count.");
    }

    internal static Gate4Step2ActualRunEvidenceRow ParseActualRun(
        BenchmarkRunDescriptor run,
        Qa04AdapterResponse response)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(response);
        if (response.ReferenceWorldMaterialized != true ||
            response.ReleaseEvidenceCapable != false ||
            (response.BlockingFailureCodes?.Length ?? -1) != 0)
            throw new InvalidDataException($"QA-04 actual Step2 readiness boundary drifted: {run.RunId}.");
        if (!string.Equals(response.ExecutionClass, "release", StringComparison.Ordinal) ||
            !string.Equals(response.ProfileId, ProfileId, StringComparison.Ordinal))
            throw new InvalidDataException($"QA-04 actual Step2 response class/profile drifted: {run.RunId}.");

        var report = response.Report;
        RequireString(report, "benchmark_profile_id", ProfileId, run.RunId);
        RequireString(report, "runtime_version", "assembled-core-production-reference-run.v1", run.RunId);
        RequireInt(report, "worker_count", run.WorkerCount, run.RunId);
        RequireInt(report, "run_ordinal", run.RunOrdinal, run.RunId);
        RequireInt(report, "production_transition_count", CanonicalTransitionCount, run.RunId);
        RequireULong(report, "terminal_operation_count", CanonicalTerminalOperationCount, run.RunId);
        RequireULong(report, "finalized_step", CanonicalFinalizedStep, run.RunId);
        RequireInt(report, "step_count", CanonicalMeasurementStepCount, run.RunId);
        RequireInt(report, "accepted_operation_loss", 0, run.RunId);
        RequireBool(report, "hidden_solver_iteration_reduction", expected: false, run.RunId);
        RequireLong(report, "persistence_metric_observer_failure_count", 0L, run.RunId);

        var snapshot = RequireObject(report, "snapshot_summary", run.RunId);
        RequireULong(snapshot, "snapshot_step", CanonicalSnapshotStep, run.RunId);
        RequireBool(snapshot, "drain_completed", expected: true, run.RunId);
        RequireInt(snapshot, "section_count", CanonicalSnapshotSectionCount, run.RunId);
        var chunkCount = RequiredInt(snapshot, "chunk_count", run.RunId);
        if (chunkCount <= 0)
            throw new InvalidDataException($"QA-04 actual Step2 snapshot chunk count must be positive: {run.RunId}.");
        _ = RequiredDigest(snapshot, "snapshot_digest", run.RunId);
        _ = RequiredDigest(snapshot, "physical_manifest_digest", run.RunId);
        _ = RequiredDigest(snapshot, "recovered_state_digest", run.RunId);

        var evidence = RequireObject(report, "determinism_evidence", run.RunId);
        var finalStateDigest = RequiredDigest(evidence, "final_state_digest", run.RunId);
        var reportFinalStateDigest = RequiredDigest(report, "final_state_digest", run.RunId);
        if (!string.Equals(finalStateDigest, reportFinalStateDigest, StringComparison.Ordinal))
            throw new InvalidDataException($"QA-04 actual Step2 final State digest does not match report: {run.RunId}.");

        var candidateIdSequenceDigest = RequiredDigest(report, "candidate_id_sequence_digest", run.RunId);
        var finalHistoryDigest = RequiredDigest(report, "final_history_digest", run.RunId);
        var finalContinuityToken = RequiredDigest(report, "final_continuity_token", run.RunId);
        var performanceFailures = response.FailureCodes
            .Concat(ReadStringArray(report, "failure_codes"))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();

        return new Gate4Step2ActualRunEvidenceRow
        {
            RunId = run.RunId,
            WorkerCount = run.WorkerCount,
            RunOrdinal = run.RunOrdinal,
            TransitionCount = CanonicalTransitionCount,
            TerminalOperationCount = CanonicalTerminalOperationCount,
            FinalizedStep = CanonicalFinalizedStep,
            MeasurementStepCount = CanonicalMeasurementStepCount,
            SnapshotStep = CanonicalSnapshotStep,
            SnapshotDrainCompleted = true,
            SnapshotSectionCount = CanonicalSnapshotSectionCount,
            SnapshotChunkCount = chunkCount,
            AcceptedOperationLoss = 0,
            HiddenSolverIterationReduction = false,
            PersistenceMetricObserverFailureCount = 0,
            CandidateIdSequenceDigest = candidateIdSequenceDigest,
            FinalHistoryDigest = finalHistoryDigest,
            FinalContinuityToken = finalContinuityToken,
            FinalStateDigest = finalStateDigest,
            TransitionCommittedDigest = RequiredDigest(evidence, "transition_committed_digest", run.RunId),
            OperationTerminalSemanticDigest = RequiredDigest(evidence, "operation_terminal_semantic_digest", run.RunId),
            ConfigHistoryDigest = RequiredDigest(evidence, "config_history_digest", run.RunId),
            PromotionDeferralOrderDigest = RequiredDigest(evidence, "promotion_deferral_order_digest", run.RunId),
            PerformanceFailureCodes = performanceFailures,
        };
    }

    internal static string VerifyActualMatrix(string matrixPath)
    {
        var rows = Program.ReadJson<Gate4Step2ActualRunEvidenceRow[]>(
            matrixPath,
            "Gate4 Step2 actual determinism matrix");
        var summary = ValidateActualMatrix(rows);
        Console.WriteLine("Gate4 Step2 actual determinism matrix PASS");
        Console.WriteLine($"actual_run_count={rows.Length}");
        Console.WriteLine($"determinism_digest_summary={summary}");
        foreach (var row in rows.OrderBy(static value => value.WorkerCount).ThenBy(static value => value.RunOrdinal))
        {
            Console.WriteLine(
                $"run={row.RunId} workers={row.WorkerCount} ordinal={row.RunOrdinal} transitions={row.TransitionCount} terminal_operations={row.TerminalOperationCount} final_state_digest={row.FinalStateDigest}");
            if (row.PerformanceFailureCodes.Length != 0)
                Console.WriteLine(
                    $"run={row.RunId} step3_performance_failures={string.Join(",", row.PerformanceFailureCodes)}");
        }
        return summary;
    }

    private static string ValidateActualMatrix(IReadOnlyCollection<Gate4Step2ActualRunEvidenceRow> rows)
    {
        if (rows.Count != 12)
            throw new InvalidDataException($"QA-04 actual Step2 matrix must contain 12 runs, found {rows.Count}.");
        if (rows.Select(static row => row.RunId).Any(string.IsNullOrWhiteSpace) ||
            rows.Select(static row => row.RunId).Distinct(StringComparer.Ordinal).Count() != rows.Count)
            throw new InvalidDataException("QA-04 actual Step2 matrix contains empty or duplicate run ids.");

        foreach (var worker in new[] { 1, 4, 8, 16 })
        {
            var workerRows = rows.Where(row => row.WorkerCount == worker)
                .OrderBy(static row => row.RunOrdinal)
                .ToArray();
            if (workerRows.Length != 3 ||
                !workerRows.Select(static row => row.RunOrdinal).SequenceEqual(new[] { 1, 2, 3 }))
                throw new InvalidDataException($"QA-04 actual Step2 worker {worker} matrix is incomplete.");
        }
        if (rows.Any(static row => row.WorkerCount is not (1 or 4 or 8 or 16)))
            throw new InvalidDataException("QA-04 actual Step2 matrix contains a non-canonical worker count.");

        foreach (var row in rows)
        {
            if (row.TransitionCount != CanonicalTransitionCount ||
                row.TerminalOperationCount != CanonicalTerminalOperationCount ||
                row.FinalizedStep != CanonicalFinalizedStep ||
                row.MeasurementStepCount != CanonicalMeasurementStepCount ||
                row.SnapshotStep != CanonicalSnapshotStep ||
                !row.SnapshotDrainCompleted ||
                row.SnapshotSectionCount != CanonicalSnapshotSectionCount ||
                row.SnapshotChunkCount <= 0 ||
                row.AcceptedOperationLoss != 0 ||
                row.HiddenSolverIterationReduction ||
                row.PersistenceMetricObserverFailureCount != 0)
                throw new InvalidDataException($"QA-04 actual Step2 production contract drifted: {row.RunId}.");

            Program.RequireLowerHex(row.CandidateIdSequenceDigest, 64, $"candidate_id_sequence_digest:{row.RunId}");
            Program.RequireLowerHex(row.FinalHistoryDigest, 64, $"final_history_digest:{row.RunId}");
            Program.RequireLowerHex(row.FinalContinuityToken, 64, $"final_continuity_token:{row.RunId}");
            if (row.PerformanceFailureCodes.Any(string.IsNullOrWhiteSpace) ||
                row.PerformanceFailureCodes.Distinct(StringComparer.Ordinal).Count() != row.PerformanceFailureCodes.Length)
                throw new InvalidDataException($"QA-04 actual Step2 performance failure codes are malformed: {row.RunId}.");
        }

        return ValidateAndSummarize(rows.Select(static row => new EvidenceRow(
                row.RunId,
                row.FinalStateDigest,
                row.TransitionCommittedDigest,
                row.OperationTerminalSemanticDigest,
                row.ConfigHistoryDigest,
                row.PromotionDeferralOrderDigest))
            .ToArray());
    }

    internal static void VerifyAndBind(
        string planDirectory,
        string outputDirectory,
        string sourceCommit)
    {
        var matrixPath = Path.Combine(planDirectory, "reference-run-matrix.json");
        var runs = Program.ReadJson<BenchmarkRunDescriptor[]>(matrixPath, "QA-04 run matrix for determinism evidence");
        if (runs.Length != 12)
            throw new InvalidDataException("QA-04 determinism evidence requires exactly 12 canonical runs.");

        var rows = new List<EvidenceRow>(12);
        var successfulRunCount = 0;
        var reportsDirectory = Path.Combine(outputDirectory, "reports");

        foreach (var run in runs.OrderBy(static value => value.RunId, StringComparer.Ordinal))
        {
            var reportPath = Path.Combine(reportsDirectory, SafeArtifactName(run.RunId) + ".json");
            var response = Program.ReadJson<Qa04AdapterResponse>(reportPath, $"QA-04 benchmark response {run.RunId}");
            if (!string.Equals(response.ProfileId, ProfileId, StringComparison.Ordinal))
                throw new InvalidDataException($"QA-04 determinism response profile mismatch: {run.RunId}.");

            var reportFailures = ReadStringArray(response.Report, "failure_codes");
            var targetSuccessful = response.Passed &&
                                   response.FailureCodes.Length == 0 &&
                                   reportFailures.Length == 0;
            if (!targetSuccessful)
                continue;

            successfulRunCount++;
            rows.Add(ParseSuccessfulEvidence(run, response.Report));
        }

        // Failed/incomplete targets are already release-ineligible. Do not require them to fabricate
        // semantic digests that only a completed authoritative run can truthfully emit.
        if (successfulRunCount != runs.Length)
            return;

        var summary = ValidateAndSummarize(rows);
        BindSummary(outputDirectory, sourceCommit, rows, summary);
    }

    private static EvidenceRow ParseSuccessfulEvidence(BenchmarkRunDescriptor run, JsonElement report)
    {
        var evidence = report.TryGetProperty("determinism_evidence", out var found) &&
                       found.ValueKind == JsonValueKind.Object
            ? found
            : throw new InvalidDataException($"Successful QA-04 run is missing determinism_evidence: {run.RunId}.");

        var row = new EvidenceRow(
            run.RunId,
            RequiredDigest(evidence, "final_state_digest", run.RunId),
            RequiredDigest(evidence, "transition_committed_digest", run.RunId),
            RequiredDigest(evidence, "operation_terminal_semantic_digest", run.RunId),
            RequiredDigest(evidence, "config_history_digest", run.RunId),
            RequiredDigest(evidence, "promotion_deferral_order_digest", run.RunId));

        var reportFinalStateDigest = RequiredDigest(report, "final_state_digest", run.RunId);
        if (!string.Equals(row.FinalStateDigest, reportFinalStateDigest, StringComparison.Ordinal))
            throw new InvalidDataException($"QA-04 determinism final State digest does not match report: {run.RunId}.");
        return row;
    }

    private static string ValidateAndSummarize(IReadOnlyCollection<EvidenceRow> rows)
    {
        if (rows.Count != 12)
            throw new InvalidDataException("QA-04 determinism evidence row count must be 12.");
        if (rows.Select(static row => row.RunId).Distinct(StringComparer.Ordinal).Count() != rows.Count)
            throw new InvalidDataException("QA-04 determinism evidence contains duplicate run ids.");

        foreach (var row in rows)
        {
            Program.RequireLowerHex(row.FinalStateDigest, 64, $"determinism final_state_digest:{row.RunId}");
            Program.RequireLowerHex(row.TransitionCommittedDigest, 64, $"determinism transition_committed_digest:{row.RunId}");
            Program.RequireLowerHex(row.OperationTerminalSemanticDigest, 64, $"determinism operation_terminal_semantic_digest:{row.RunId}");
            Program.RequireLowerHex(row.ConfigHistoryDigest, 64, $"determinism config_history_digest:{row.RunId}");
            Program.RequireLowerHex(row.PromotionDeferralOrderDigest, 64, $"determinism promotion_deferral_order_digest:{row.RunId}");
        }

        var ordered = rows.OrderBy(static row => row.RunId, StringComparer.Ordinal).ToArray();
        var baseline = ordered[0];
        foreach (var row in ordered.Skip(1))
        {
            RequireEqual(baseline.FinalStateDigest, row.FinalStateDigest, "final-state-digest");
            RequireEqual(baseline.TransitionCommittedDigest, row.TransitionCommittedDigest, "transition-committed-digest");
            RequireEqual(baseline.OperationTerminalSemanticDigest, row.OperationTerminalSemanticDigest, "operation-terminal-semantic-digest");
            RequireEqual(baseline.ConfigHistoryDigest, row.ConfigHistoryDigest, "config-history-digest");
            RequireEqual(baseline.PromotionDeferralOrderDigest, row.PromotionDeferralOrderDigest, "promotion-deferral-order-digest");
        }

        var builder = new StringBuilder(SummaryDomain.Length + rows.Count * 340);
        builder.Append(SummaryDomain).Append('\n');
        foreach (var row in ordered)
        {
            builder.Append(row.RunId).Append('\0')
                .Append(row.FinalStateDigest).Append('\0')
                .Append(row.TransitionCommittedDigest).Append('\0')
                .Append(row.OperationTerminalSemanticDigest).Append('\0')
                .Append(row.ConfigHistoryDigest).Append('\0')
                .Append(row.PromotionDeferralOrderDigest).Append('\n');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static void BindSummary(
        string outputDirectory,
        string sourceCommit,
        IReadOnlyCollection<EvidenceRow> rows,
        string summary)
    {
        var reportsDirectory = Path.Combine(outputDirectory, "reports");
        var sidecarPath = Path.Combine(reportsDirectory, "perf.reference.v1.determinism.json");
        Program.WriteJson(sidecarPath, new
        {
            schema_version = "1.0",
            profile_id = ProfileId,
            source_commit = sourceCommit,
            determinism_digest_summary = summary,
            runs = rows.OrderBy(static row => row.RunId, StringComparer.Ordinal).Select(static row => new
            {
                run_id = row.RunId,
                final_state_digest = row.FinalStateDigest,
                transition_committed_digest = row.TransitionCommittedDigest,
                operation_terminal_semantic_digest = row.OperationTerminalSemanticDigest,
                config_history_digest = row.ConfigHistoryDigest,
                promotion_deferral_order_digest = row.PromotionDeferralOrderDigest,
            }).ToArray(),
        });

        var aggregatePath = Path.Combine(reportsDirectory, "perf.reference.v1.aggregate.json");
        var aggregate = Program.ReadJson<BenchmarkAggregateArtifact>(aggregatePath, "QA-04 reference aggregate");
        aggregate.DeterminismDigestSummary = summary;
        var aggregateDigest = Program.WriteJson(aggregatePath, aggregate);

        var fragmentPath = Path.Combine(outputDirectory, "qa04-evidence-fragment.json");
        var fragment = Program.ReadJson<EvidenceFragment>(fragmentPath, "QA-04 evidence fragment for determinism binding");
        fragment.DeterminismDigestSummary = summary;
        var reference = fragment.PerformanceReports.SingleOrDefault(static report =>
            string.Equals(report.ProfileId, ProfileId, StringComparison.Ordinal))
            ?? throw new InvalidDataException("QA-04 evidence fragment is missing perf.reference.v1 evidence.");
        reference.ReportDigest = aggregateDigest;
        Program.WriteJson(fragmentPath, fragment);
    }

    private static JsonElement RequireObject(JsonElement parent, string property, string runId)
    {
        if (!parent.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"QA-04 actual Step2 object missing: {runId}:{property}.");
        return value;
    }

    private static void RequireString(JsonElement parent, string property, string expected, string runId)
    {
        if (!parent.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            !string.Equals(value.GetString(), expected, StringComparison.Ordinal))
            throw new InvalidDataException($"QA-04 actual Step2 string drift: {runId}:{property}.");
    }

    private static int RequiredInt(JsonElement parent, string property, string runId)
    {
        if (!parent.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out var result))
            throw new InvalidDataException($"QA-04 actual Step2 integer missing: {runId}:{property}.");
        return result;
    }

    private static void RequireInt(JsonElement parent, string property, int expected, string runId)
    {
        if (RequiredInt(parent, property, runId) != expected)
            throw new InvalidDataException($"QA-04 actual Step2 integer drift: {runId}:{property}.");
    }

    private static void RequireLong(JsonElement parent, string property, long expected, string runId)
    {
        if (!parent.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt64(out var actual) ||
            actual != expected)
            throw new InvalidDataException($"QA-04 actual Step2 integer drift: {runId}:{property}.");
    }

    private static void RequireULong(JsonElement parent, string property, ulong expected, string runId)
    {
        if (!parent.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetUInt64(out var actual) ||
            actual != expected)
            throw new InvalidDataException($"QA-04 actual Step2 unsigned integer drift: {runId}:{property}.");
    }

    private static void RequireBool(JsonElement parent, string property, bool expected, string runId)
    {
        if (!parent.TryGetProperty(property, out var value) ||
            value.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            value.GetBoolean() != expected)
            throw new InvalidDataException($"QA-04 actual Step2 boolean drift: {runId}:{property}.");
    }

    private static string RequiredDigest(JsonElement parent, string property, string runId)
    {
        if (!parent.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"QA-04 determinism digest missing: {runId}:{property}.");
        var digest = value.GetString() ?? "";
        Program.RequireLowerHex(digest, 64, $"{runId}:{property}");
        return digest;
    }

    private static string[] ReadStringArray(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"QA-04 benchmark report missing string array: {property}.");
        return value.EnumerateArray()
            .Select(item => item.GetString() ?? throw new InvalidDataException($"QA-04 {property} contains null."))
            .ToArray();
    }

    private static string SafeArtifactName(string value)
        => string.Concat(value.Select(static c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_'));

    private static void RequireEqual(string expected, string actual, string code)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidDataException($"qa04.determinism.{code}");
    }

    private static void RequireThrows<T>(Action action, string message)
        where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }
        throw new InvalidDataException(message);
    }

    private static Gate4Step2ActualRunEvidenceRow SelfTestActualRow(int worker, int ordinal)
        => new()
        {
            RunId = $"selftest.actual.{worker}.{ordinal}",
            WorkerCount = worker,
            RunOrdinal = ordinal,
            TransitionCount = CanonicalTransitionCount,
            TerminalOperationCount = CanonicalTerminalOperationCount,
            FinalizedStep = CanonicalFinalizedStep,
            MeasurementStepCount = CanonicalMeasurementStepCount,
            SnapshotStep = CanonicalSnapshotStep,
            SnapshotDrainCompleted = true,
            SnapshotSectionCount = CanonicalSnapshotSectionCount,
            SnapshotChunkCount = 1,
            AcceptedOperationLoss = 0,
            HiddenSolverIterationReduction = false,
            PersistenceMetricObserverFailureCount = 0,
            CandidateIdSequenceDigest = new string('9', 64),
            FinalHistoryDigest = new string('8', 64),
            FinalContinuityToken = new string('7', 64),
            FinalStateDigest = new string('a', 64),
            TransitionCommittedDigest = new string('b', 64),
            OperationTerminalSemanticDigest = new string('c', 64),
            ConfigHistoryDigest = new string('d', 64),
            PromotionDeferralOrderDigest = new string('e', 64),
            PerformanceFailureCodes = [],
        };

    private static Gate4Step2ActualRunEvidenceRow CloneActualRow(Gate4Step2ActualRunEvidenceRow row)
        => new()
        {
            RunId = row.RunId,
            WorkerCount = row.WorkerCount,
            RunOrdinal = row.RunOrdinal,
            TransitionCount = row.TransitionCount,
            TerminalOperationCount = row.TerminalOperationCount,
            FinalizedStep = row.FinalizedStep,
            MeasurementStepCount = row.MeasurementStepCount,
            SnapshotStep = row.SnapshotStep,
            SnapshotDrainCompleted = row.SnapshotDrainCompleted,
            SnapshotSectionCount = row.SnapshotSectionCount,
            SnapshotChunkCount = row.SnapshotChunkCount,
            AcceptedOperationLoss = row.AcceptedOperationLoss,
            HiddenSolverIterationReduction = row.HiddenSolverIterationReduction,
            PersistenceMetricObserverFailureCount = row.PersistenceMetricObserverFailureCount,
            CandidateIdSequenceDigest = row.CandidateIdSequenceDigest,
            FinalHistoryDigest = row.FinalHistoryDigest,
            FinalContinuityToken = row.FinalContinuityToken,
            FinalStateDigest = row.FinalStateDigest,
            TransitionCommittedDigest = row.TransitionCommittedDigest,
            OperationTerminalSemanticDigest = row.OperationTerminalSemanticDigest,
            ConfigHistoryDigest = row.ConfigHistoryDigest,
            PromotionDeferralOrderDigest = row.PromotionDeferralOrderDigest,
            PerformanceFailureCodes = row.PerformanceFailureCodes.ToArray(),
        };

    private sealed record EvidenceRow(
        string RunId,
        string FinalStateDigest,
        string TransitionCommittedDigest,
        string OperationTerminalSemanticDigest,
        string ConfigHistoryDigest,
        string PromotionDeferralOrderDigest);
}
