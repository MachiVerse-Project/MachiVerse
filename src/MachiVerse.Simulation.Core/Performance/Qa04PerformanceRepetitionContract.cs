namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04DeterminismEvidenceV1(
    byte[] FinalStateDigest,
    byte[] TransitionCommittedDigest,
    byte[] OperationTerminalSemanticDigest,
    byte[] ConfigHistoryDigest,
    byte[] PromotionDeferralOrderDigest);

public sealed record Qa04MeasuredProcessRunV1(
    int WorkerCount,
    int RunOrdinal,
    Qa04PerformanceMeasurementSnapshotV1 Measurement,
    Qa04DeterminismEvidenceV1 Determinism);

public sealed record Qa04PerformanceRepetitionEvaluationV1(
    bool Passed,
    TimeSpan Worker16MedianRunP95,
    IReadOnlyList<string> FailureCodes);

/// <summary>
/// P4-06 process-repetition and cross-worker determinism gate. The contract consumes completed
/// process-run evidence; it never synthesizes benchmark samples and cannot influence simulation
/// execution. The reference profile requires workers 1/4/8/16, three independent process runs per
/// worker count, and identical determinism evidence across every run.
/// </summary>
public static class Qa04PerformanceRepetitionContractV1
{
    public const int RequiredRunsPerWorker = 3;

    public static IReadOnlyList<int> CanonicalWorkerCounts { get; } =
        Array.AsReadOnly(new[] { 1, 4, 8, 16 });

    public static Qa04PerformanceRepetitionEvaluationV1 Evaluate(
        IReadOnlyCollection<Qa04MeasuredProcessRunV1> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        var failures = new List<string>();

        var expectedRunCount = checked(CanonicalWorkerCounts.Count * RequiredRunsPerWorker);
        if (runs.Count != expectedRunCount)
            failures.Add("qa04.repetition.run-count");

        var byKey = new Dictionary<(int WorkerCount, int RunOrdinal), Qa04MeasuredProcessRunV1>();
        foreach (var run in runs)
        {
            ArgumentNullException.ThrowIfNull(run);
            ArgumentNullException.ThrowIfNull(run.Measurement);
            ArgumentNullException.ThrowIfNull(run.Determinism);

            if (!CanonicalWorkerCounts.Contains(run.WorkerCount))
            {
                failures.Add("qa04.repetition.worker-count");
                continue;
            }
            if (run.RunOrdinal is < 1 or > RequiredRunsPerWorker)
            {
                failures.Add("qa04.repetition.run-ordinal");
                continue;
            }
            if (!byKey.TryAdd((run.WorkerCount, run.RunOrdinal), run))
                failures.Add("qa04.repetition.duplicate-run-key");
        }

        foreach (var workerCount in CanonicalWorkerCounts)
        {
            for (var ordinal = 1; ordinal <= RequiredRunsPerWorker; ordinal++)
            {
                if (!byKey.ContainsKey((workerCount, ordinal)))
                    failures.Add("qa04.repetition.missing-run-key");
            }
        }

        foreach (var run in byKey.Values)
        {
            if (run.Measurement.StepSampleCount != Qa04PerformanceThresholdsV1.ExpectedMeasurementStepCount ||
                run.Measurement.StepDuration?.SampleCount != Qa04PerformanceThresholdsV1.ExpectedMeasurementStepCount)
                failures.Add("qa04.repetition.incomplete-step-measurement");
            ValidateDigestSet(run.Determinism, failures);
        }

        var determinismRuns = byKey.Values
            .OrderBy(static run => run.WorkerCount)
            .ThenBy(static run => run.RunOrdinal)
            .ToArray();
        if (determinismRuns.Length > 0)
        {
            var baseline = determinismRuns[0].Determinism;
            foreach (var run in determinismRuns.Skip(1))
            {
                if (!baseline.FinalStateDigest.AsSpan().SequenceEqual(run.Determinism.FinalStateDigest))
                    failures.Add("qa04.determinism.final-state-digest");
                if (!baseline.TransitionCommittedDigest.AsSpan().SequenceEqual(run.Determinism.TransitionCommittedDigest))
                    failures.Add("qa04.determinism.transition-committed-digest");
                if (!baseline.OperationTerminalSemanticDigest.AsSpan().SequenceEqual(run.Determinism.OperationTerminalSemanticDigest))
                    failures.Add("qa04.determinism.operation-terminal-semantic-digest");
                if (!baseline.ConfigHistoryDigest.AsSpan().SequenceEqual(run.Determinism.ConfigHistoryDigest))
                    failures.Add("qa04.determinism.config-history-digest");
                if (!baseline.PromotionDeferralOrderDigest.AsSpan().SequenceEqual(run.Determinism.PromotionDeferralOrderDigest))
                    failures.Add("qa04.determinism.promotion-deferral-order-digest");
            }
        }

        var worker16P95 = byKey.Values
            .Where(static run => run.WorkerCount == 16)
            .Select(static run => run.Measurement.StepDuration?.P95)
            .Where(static value => value is not null)
            .Select(static value => value!.Value)
            .OrderBy(static value => value)
            .ToArray();

        var medianP95 = TimeSpan.Zero;
        if (worker16P95.Length != RequiredRunsPerWorker)
        {
            failures.Add("qa04.repetition.worker16-p95-run-count");
        }
        else
        {
            medianP95 = worker16P95[1];
            if (medianP95 > Qa04PerformanceThresholdsV1.StepP95Max)
                failures.Add("qa04.performance.worker16-median-run-p95");
        }

        return new Qa04PerformanceRepetitionEvaluationV1(
            failures.Count == 0,
            medianP95,
            Array.AsReadOnly(failures.Distinct(StringComparer.Ordinal).ToArray()));
    }

    private static void ValidateDigestSet(
        Qa04DeterminismEvidenceV1 evidence,
        ICollection<string> failures)
    {
        if (evidence.FinalStateDigest is not { Length: 32 })
            failures.Add("qa04.determinism.final-state-digest-size");
        if (evidence.TransitionCommittedDigest is not { Length: 32 })
            failures.Add("qa04.determinism.transition-committed-digest-size");
        if (evidence.OperationTerminalSemanticDigest is not { Length: 32 })
            failures.Add("qa04.determinism.operation-terminal-semantic-digest-size");
        if (evidence.ConfigHistoryDigest is not { Length: 32 })
            failures.Add("qa04.determinism.config-history-digest-size");
        if (evidence.PromotionDeferralOrderDigest is not { Length: 32 })
            failures.Add("qa04.determinism.promotion-deferral-order-digest-size");
    }
}
