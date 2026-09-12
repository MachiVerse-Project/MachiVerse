using System.Text.Json.Nodes;

namespace MachiVerse.PerformanceHarness;

internal static partial class Program
{
    private static void SelfTestAcceptanceRules(JsonNode manifest)
    {
        var criteria = new PassCriteria(
            GetDouble(manifest, "passCriteria", "worker16MedianP95StepMs"),
            GetDouble(manifest, "passCriteria", "stepP99Ms"),
            GetDouble(manifest, "passCriteria", "mean60sStepMs"),
            GetInt(manifest, "passCriteria", "maxMemoryGuardGiB"),
            GetDouble(manifest, "passCriteria", "sqliteCommitP95Ms"),
            GetDouble(manifest, "passCriteria", "sqliteCommitP99Ms"),
            GetDouble(manifest, "passCriteria", "snapshotCowBarrierP95Ms"));

        var pass = new BenchmarkAggregate(16, 33.333, 50.0, 30.0, 28, 4.0, 8.0, 5.0, 0, false, false);
        if (!EvaluateAggregate(criteria, pass).Passed)
            throw new InvalidDataException("QA-04 acceptance self-test expected boundary PASS.");
        if (EvaluateAggregate(criteria, pass with { MedianP95StepMs = 33.334 }).Passed)
            throw new InvalidDataException("QA-04 acceptance self-test failed to reject p95 regression.");
        if (EvaluateAggregate(criteria, pass with { HiddenSolverIterationReduction = true }).Passed)
            throw new InvalidDataException("QA-04 acceptance self-test failed to reject hidden solver reduction.");
        if (EvaluateAggregate(criteria, pass with { StateDigestMismatch = true }).Passed)
            throw new InvalidDataException("QA-04 acceptance self-test failed to reject state-digest mismatch.");
    }

    private static AcceptanceResult EvaluateAggregate(PassCriteria criteria, BenchmarkAggregate aggregate)
    {
        var failures = new List<string>();
        if (aggregate.WorkerCount != 16) failures.Add("reference-worker-count");
        if (aggregate.MedianP95StepMs > criteria.Worker16MedianP95StepMs) failures.Add("step-p95");
        if (aggregate.P99StepMs > criteria.StepP99Ms) failures.Add("step-p99");
        if (aggregate.Mean60sStepMs > criteria.Mean60sStepMs) failures.Add("step-mean-60s");
        if (aggregate.MaxMemoryGiB > criteria.MaxMemoryGuardGiB) failures.Add("memory-guard");
        if (aggregate.SqliteCommitP95Ms > criteria.SqliteCommitP95Ms) failures.Add("sqlite-commit-p95");
        if (aggregate.SqliteCommitP99Ms > criteria.SqliteCommitP99Ms) failures.Add("sqlite-commit-p99");
        if (aggregate.SnapshotCowBarrierP95Ms > criteria.SnapshotCowBarrierP95Ms) failures.Add("snapshot-cow-p95");
        if (aggregate.AcceptedOperationLoss != 0) failures.Add("accepted-operation-loss");
        if (aggregate.HiddenSolverIterationReduction) failures.Add("hidden-solver-reduction");
        if (aggregate.StateDigestMismatch) failures.Add("state-digest-mismatch");
        return new AcceptanceResult(failures.Count == 0, failures.ToArray());
    }

    internal sealed record PassCriteria(
        double Worker16MedianP95StepMs,
        double StepP99Ms,
        double Mean60sStepMs,
        int MaxMemoryGuardGiB,
        double SqliteCommitP95Ms,
        double SqliteCommitP99Ms,
        double SnapshotCowBarrierP95Ms);

    internal sealed record BenchmarkAggregate(
        int WorkerCount,
        double MedianP95StepMs,
        double P99StepMs,
        double Mean60sStepMs,
        double MaxMemoryGiB,
        double SqliteCommitP95Ms,
        double SqliteCommitP99Ms,
        double SnapshotCowBarrierP95Ms,
        int AcceptedOperationLoss,
        bool HiddenSolverIterationReduction,
        bool StateDigestMismatch);

    internal sealed record AcceptanceResult(bool Passed, string[] FailureCodes);
}
