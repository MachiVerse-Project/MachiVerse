using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class Qa04DeterminismEvidenceVerifier
{
    private const string ProfileId = "perf.reference.v1";
    private const string SummaryDomain = "qa04.determinism-evidence.v1";

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

    private sealed record EvidenceRow(
        string RunId,
        string FinalStateDigest,
        string TransitionCommittedDigest,
        string OperationTerminalSemanticDigest,
        string ConfigHistoryDigest,
        string PromotionDeferralOrderDigest);
}
