using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class Qa04DeterminismEvidenceVerifier
{
    private const string ProfileId = "perf.reference.v1";
    private const string SummaryDomain = "qa04.determinism-evidence.v1";
    private const string Step2ProfileId = "gate4.step2.determinism.v1";

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

        var plan = SelfTestPlan();
        ValidateActualPlan(plan);
        var actualRows = new List<Gate4Step2ActualRunEvidenceRow>(12);
        foreach (var worker in plan.WorkerCounts)
        foreach (var ordinal in Enumerable.Range(1, plan.ProcessRunsPerWorker))
            actualRows.Add(SelfTestActualRow(plan, worker, ordinal));
        var actualSummary = ValidateActualMatrix(plan, actualRows);
        Program.RequireLowerHex(actualSummary, 64, "QA-04 actual Step2 determinism summary self-test");

        var badTransitionCount = actualRows
            .Select(static row => CloneActualRow(row))
            .ToArray();
        badTransitionCount[0].TransitionCount--;
        RequireThrows<InvalidDataException>(
            () => ValidateActualMatrix(plan, badTransitionCount),
            "QA-04 actual Step2 verifier must reject a non-plan transition count.");
    }

    internal static void ValidateActualPlan(Gate4Step2DeterminismPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!string.Equals(plan.SchemaVersion, "1.0", StringComparison.Ordinal) ||
            !string.Equals(plan.ProfileId, Step2ProfileId, StringComparison.Ordinal))
            throw new InvalidDataException("Gate4 Step2 determinism plan schema/profile drifted.");
        if (!plan.WorkerCounts.Order().SequenceEqual(new[] { 1, 4, 8, 16 }) ||
            plan.ProcessRunsPerWorker != 3)
            throw new InvalidDataException("Gate4 Step2 determinism plan worker/run matrix drifted.");
        if (plan.TransitionCount <= 0 ||
            plan.RequiredDetailDecisionCount != plan.TransitionCount ||
            plan.ExpectedTerminalOperationCount == 0 ||
            plan.RequiredTurnoverCount <= 0 ||
            plan.RequiredBurstStepCount <= 0 ||
            plan.PersistenceInsertBatchSize <= 0 ||
            plan.ProgressIntervalTransitions <= 0 ||
            plan.HeartbeatIntervalSeconds <= 0)
            throw new InvalidDataException("Gate4 Step2 determinism plan does not cover the required production boundaries.");
        if (!string.Equals(
                plan.SnapshotRecoveryEvidence,
                "reuse-gate3-exact103-production-proof",
                StringComparison.Ordinal) ||
            !string.Equals(
                plan.LongDurationEvidence,
                "gate4-step4-24h-soak",
                StringComparison.Ordinal))
            throw new InvalidDataException("Gate4 Step2 determinism plan proof ownership drifted.");
    }

    internal static Gate4Step2ActualRunEvidenceRow ParseActualRun(
        Gate4Step2DeterminismPlan plan,
        string sourceCommit,
        int workerCount,
        int runOrdinal,
        Gate4Step2CoreRunResult result)
    {
        ValidateActualPlan(plan);
        Program.RequireLowerHex(sourceCommit, 40, "sourceCommit");
        ArgumentNullException.ThrowIfNull(result);

        var runId = $"gate4.step2.w{workerCount}.r{runOrdinal}";
        if (!plan.WorkerCounts.Contains(workerCount) ||
            runOrdinal < 1 ||
            runOrdinal > plan.ProcessRunsPerWorker)
            throw new InvalidDataException($"Gate4 Step2 run identity is outside the plan: {runId}.");

        if (!string.Equals(result.SchemaVersion, "1.0", StringComparison.Ordinal) ||
            !string.Equals(result.ProfileId, Step2ProfileId, StringComparison.Ordinal) ||
            result.WorkerCount != workerCount ||
            result.TransitionCount != plan.TransitionCount ||
            result.TerminalOperationCount != plan.ExpectedTerminalOperationCount ||
            result.FinalizedStep != checked((ulong)plan.TransitionCount + 1UL) ||
            result.TurnoverCount != plan.RequiredTurnoverCount ||
            result.DetailDecisionCount != plan.RequiredDetailDecisionCount ||
            result.BurstStepCount != plan.RequiredBurstStepCount ||
            result.AcceptedOperationLoss != 0 ||
            result.HiddenSolverIterationReduction ||
            result.PersistenceMetricObserverFailureCount != 0 ||
            !result.Passed ||
            result.FailureCodes.Length != 0)
            throw new InvalidDataException($"Gate4 Step2 bounded production contract drifted: {runId}.");

        var evidence = result.DeterminismEvidence
            ?? throw new InvalidDataException($"Gate4 Step2 determinism evidence missing: {runId}.");
        Program.RequireLowerHex(result.FinalStateDigest, 64, $"{runId}:final_state_digest");
        Program.RequireLowerHex(evidence.FinalStateDigest, 64, $"{runId}:determinism.final_state_digest");
        if (!string.Equals(result.FinalStateDigest, evidence.FinalStateDigest, StringComparison.Ordinal))
            throw new InvalidDataException($"Gate4 Step2 final State digest mismatch: {runId}.");

        foreach (var (value, name) in new[]
        {
            (result.CandidateIdSequenceDigest, "candidate_id_sequence_digest"),
            (result.FinalHistoryDigest, "final_history_digest"),
            (result.FinalContinuityToken, "final_continuity_token"),
            (evidence.TransitionCommittedDigest, "transition_committed_digest"),
            (evidence.OperationTerminalSemanticDigest, "operation_terminal_semantic_digest"),
            (evidence.ConfigHistoryDigest, "config_history_digest"),
            (evidence.PromotionDeferralOrderDigest, "promotion_deferral_order_digest"),
        })
            Program.RequireLowerHex(value, 64, $"{runId}:{name}");

        return new Gate4Step2ActualRunEvidenceRow
        {
            RunId = runId,
            WorkerCount = workerCount,
            RunOrdinal = runOrdinal,
            TransitionCount = result.TransitionCount,
            TerminalOperationCount = result.TerminalOperationCount,
            FinalizedStep = result.FinalizedStep,
            TurnoverCount = result.TurnoverCount,
            DetailDecisionCount = result.DetailDecisionCount,
            BurstStepCount = result.BurstStepCount,
            AcceptedOperationLoss = result.AcceptedOperationLoss,
            HiddenSolverIterationReduction = result.HiddenSolverIterationReduction,
            PersistenceMetricObserverFailureCount = result.PersistenceMetricObserverFailureCount,
            CandidateIdSequenceDigest = result.CandidateIdSequenceDigest,
            FinalHistoryDigest = result.FinalHistoryDigest,
            FinalContinuityToken = result.FinalContinuityToken,
            FinalStateDigest = result.FinalStateDigest,
            TransitionCommittedDigest = evidence.TransitionCommittedDigest,
            OperationTerminalSemanticDigest = evidence.OperationTerminalSemanticDigest,
            ConfigHistoryDigest = evidence.ConfigHistoryDigest,
            PromotionDeferralOrderDigest = evidence.PromotionDeferralOrderDigest,
        };
    }

    internal static string VerifyActualMatrix(string repositoryRoot, string matrixPath)
    {
        var planPath = Path.Combine(
            repositoryRoot,
            "tests",
            "performance-fixtures",
            "v1",
            "gate4-step2-determinism-plan.json");
        var plan = Program.ReadJson<Gate4Step2DeterminismPlan>(
            planPath,
            "Gate4 Step2 bounded determinism plan");
        ValidateActualPlan(plan);

        var rows = Program.ReadJson<Gate4Step2ActualRunEvidenceRow[]>(
            matrixPath,
            "Gate4 Step2 actual determinism matrix");
        var summary = ValidateActualMatrix(plan, rows);
        Console.WriteLine("Gate4 Step2 bounded actual determinism matrix PASS");
        Console.WriteLine($"actual_run_count={rows.Length}");
        Console.WriteLine($"transition_count_per_run={plan.TransitionCount}");
        Console.WriteLine($"terminal_operations_per_run={plan.ExpectedTerminalOperationCount}");
        Console.WriteLine($"determinism_digest_summary={summary}");
        foreach (var row in rows.OrderBy(static value => value.WorkerCount).ThenBy(static value => value.RunOrdinal))
            Console.WriteLine(
                $"run={row.RunId} workers={row.WorkerCount} ordinal={row.RunOrdinal} transitions={row.TransitionCount} terminal_operations={row.TerminalOperationCount} final_state_digest={row.FinalStateDigest}");
        return summary;
    }

    private static string ValidateActualMatrix(
        Gate4Step2DeterminismPlan plan,
        IReadOnlyCollection<Gate4Step2ActualRunEvidenceRow> rows)
    {
        ValidateActualPlan(plan);
        var expectedRunCount = checked(plan.WorkerCounts.Length * plan.ProcessRunsPerWorker);
        if (rows.Count != expectedRunCount)
            throw new InvalidDataException(
                $"QA-04 actual Step2 matrix must contain {expectedRunCount} runs, found {rows.Count}.");
        if (rows.Select(static row => row.RunId).Any(string.IsNullOrWhiteSpace) ||
            rows.Select(static row => row.RunId).Distinct(StringComparer.Ordinal).Count() != rows.Count)
            throw new InvalidDataException("QA-04 actual Step2 matrix contains empty or duplicate run ids.");

        foreach (var worker in plan.WorkerCounts)
        {
            var workerRows = rows.Where(row => row.WorkerCount == worker)
                .OrderBy(static row => row.RunOrdinal)
                .ToArray();
            if (workerRows.Length != plan.ProcessRunsPerWorker ||
                !workerRows.Select(static row => row.RunOrdinal)
                    .SequenceEqual(Enumerable.Range(1, plan.ProcessRunsPerWorker)))
                throw new InvalidDataException($"QA-04 actual Step2 worker {worker} matrix is incomplete.");

            foreach (var row in workerRows)
            {
                var expectedRunId = $"gate4.step2.w{worker}.r{row.RunOrdinal}";
                if (!string.Equals(row.RunId, expectedRunId, StringComparison.Ordinal))
                    throw new InvalidDataException($"QA-04 actual Step2 run id drifted: {row.RunId}.");
            }
        }
        if (rows.Any(row => !plan.WorkerCounts.Contains(row.WorkerCount)))
            throw new InvalidDataException("QA-04 actual Step2 matrix contains a non-plan worker count.");

        foreach (var row in rows)
        {
            if (row.TransitionCount != plan.TransitionCount ||
                row.TerminalOperationCount != plan.ExpectedTerminalOperationCount ||
                row.FinalizedStep != checked((ulong)plan.TransitionCount + 1UL) ||
                row.TurnoverCount != plan.RequiredTurnoverCount ||
                row.DetailDecisionCount != plan.RequiredDetailDecisionCount ||
                row.BurstStepCount != plan.RequiredBurstStepCount ||
                row.AcceptedOperationLoss != 0 ||
                row.HiddenSolverIterationReduction ||
                row.PersistenceMetricObserverFailureCount != 0)
                throw new InvalidDataException($"QA-04 actual Step2 bounded production contract drifted: {row.RunId}.");

            Program.RequireLowerHex(row.CandidateIdSequenceDigest, 64, $"candidate_id_sequence_digest:{row.RunId}");
            Program.RequireLowerHex(row.FinalHistoryDigest, 64, $"final_history_digest:{row.RunId}");
            Program.RequireLowerHex(row.FinalContinuityToken, 64, $"final_continuity_token:{row.RunId}");
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

    private static Gate4Step2DeterminismPlan SelfTestPlan()
        => new()
        {
            SchemaVersion = "1.0",
            ProfileId = Step2ProfileId,
            WorkerCounts = [1, 4, 8, 16],
            ProcessRunsPerWorker = 3,
            TransitionCount = 901,
            ExpectedTerminalOperationCount = 4_555_000,
            RequiredTurnoverCount = 3,
            RequiredDetailDecisionCount = 901,
            RequiredBurstStepCount = 1,
            SnapshotRecoveryEvidence = "reuse-gate3-exact103-production-proof",
            LongDurationEvidence = "gate4-step4-24h-soak",
        };

    private static Gate4Step2ActualRunEvidenceRow SelfTestActualRow(
        Gate4Step2DeterminismPlan plan,
        int worker,
        int ordinal)
        => new()
        {
            RunId = $"gate4.step2.w{worker}.r{ordinal}",
            WorkerCount = worker,
            RunOrdinal = ordinal,
            TransitionCount = plan.TransitionCount,
            TerminalOperationCount = plan.ExpectedTerminalOperationCount,
            FinalizedStep = checked((ulong)plan.TransitionCount + 1UL),
            TurnoverCount = plan.RequiredTurnoverCount,
            DetailDecisionCount = plan.RequiredDetailDecisionCount,
            BurstStepCount = plan.RequiredBurstStepCount,
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
            TurnoverCount = row.TurnoverCount,
            DetailDecisionCount = row.DetailDecisionCount,
            BurstStepCount = row.BurstStepCount,
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
        };

    private sealed record EvidenceRow(
        string RunId,
        string FinalStateDigest,
        string TransitionCommittedDigest,
        string OperationTerminalSemanticDigest,
        string ConfigHistoryDigest,
        string PromotionDeferralOrderDigest);
}
