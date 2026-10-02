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
    IReadOnlyList<string> FailureCodes)
{
    public TimeSpan Worker8WorstRunP99 { get; init; }
    public TimeSpan Worker16WorstRunP99 { get; init; }
    public double Worker8WorstDeadlineMissRatio { get; init; }
    public double Worker16WorstDeadlineMissRatio { get; init; }
}

/// <summary>
/// Alpha 1.1 process-repetition and cross-profile determinism gate. The contract consumes completed
/// process-run evidence; it never synthesizes benchmark samples and cannot influence simulation
/// execution. Alpha 1.1 requires worker profiles 8 and 16 with three independent process runs per
/// profile. CPU topology/affinity is separate release-machine evidence and must not be inferred from
/// WorkerCount. Determinism evidence must remain identical across every run.
/// </summary>
public static class Qa04PerformanceRepetitionContractV1
{
    public const int RequiredRunsPerWorker = 3;

    public static IReadOnlyList<int> CanonicalWorkerCounts { get; } =
        Array.AsReadOnly(new[] { 8, 16 });

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
            ValidateMeasurement(run.Measurement, failures);
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
                if (!DigestEqual(baseline.FinalStateDigest, run.Determinism.FinalStateDigest))
                    failures.Add("qa04.determinism.final-state-digest");
                if (!DigestEqual(baseline.TransitionCommittedDigest, run.Determinism.TransitionCommittedDigest))
                    failures.Add("qa04.determinism.transition-committed-digest");
                if (!DigestEqual(baseline.OperationTerminalSemanticDigest, run.Determinism.OperationTerminalSemanticDigest))
                    failures.Add("qa04.determinism.operation-terminal-semantic-digest");
                if (!DigestEqual(baseline.ConfigHistoryDigest, run.Determinism.ConfigHistoryDigest))
                    failures.Add("qa04.determinism.config-history-digest");
                if (!DigestEqual(baseline.PromotionDeferralOrderDigest, run.Determinism.PromotionDeferralOrderDigest))
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

        var medianP95 = worker16P95.Length == RequiredRunsPerWorker
            ? worker16P95[1]
            : TimeSpan.Zero;

        var worker8 = ProfileSummary(byKey.Values, 8);
        var worker16 = ProfileSummary(byKey.Values, 16);
        if (worker8.RunCount != RequiredRunsPerWorker)
            failures.Add("qa04.repetition.worker8-run-count");
        if (worker16.RunCount != RequiredRunsPerWorker)
            failures.Add("qa04.repetition.worker16-run-count");

        return new Qa04PerformanceRepetitionEvaluationV1(
            failures.Count == 0,
            medianP95,
            Array.AsReadOnly(failures.Distinct(StringComparer.Ordinal).ToArray()))
        {
            Worker8WorstRunP99 = worker8.WorstP99,
            Worker16WorstRunP99 = worker16.WorstP99,
            Worker8WorstDeadlineMissRatio = worker8.WorstDeadlineMissRatio,
            Worker16WorstDeadlineMissRatio = worker16.WorstDeadlineMissRatio,
        };
    }

    private static void ValidateMeasurement(
        Qa04PerformanceMeasurementSnapshotV1 measurement,
        ICollection<string> failures)
    {
        if (measurement.StepSampleCount != Qa04PerformanceThresholdsV1.ExpectedMeasurementStepCount ||
            measurement.StepDuration?.SampleCount != Qa04PerformanceThresholdsV1.ExpectedMeasurementStepCount)
        {
            failures.Add("qa04.repetition.incomplete-step-measurement");
            return;
        }

        if (measurement.StepDuration.P99 > Qa04PerformanceThresholdsV1.StepP99Max)
            failures.Add("qa04.performance.step-p99");
        if (Math.Abs(measurement.StepDeadlineMilliseconds - Qa04PerformanceThresholdsV1.StepDeadline.TotalMilliseconds) > 0.000001d)
            failures.Add("qa04.repetition.step-deadline-contract");
        if (measurement.StepDeadlineMissCount is < 0 ||
            measurement.StepDeadlineMissCount > measurement.StepSampleCount)
            failures.Add("qa04.repetition.step-deadline-miss-count");

        var expectedMissRatio = measurement.StepDeadlineMissCount / (double)measurement.StepSampleCount;
        if (Math.Abs(measurement.StepDeadlineMissRatio - expectedMissRatio) > 0.000000001d)
            failures.Add("qa04.repetition.step-deadline-miss-ratio-drift");
        if (measurement.StepDeadlineMissRatio > Qa04PerformanceThresholdsV1.DeadlineMissRatioMax)
            failures.Add("qa04.performance.step-deadline-miss-ratio");
    }

    private static ProfileMeasurementSummary ProfileSummary(
        IEnumerable<Qa04MeasuredProcessRunV1> runs,
        int workerCount)
    {
        var profileRuns = runs.Where(run => run.WorkerCount == workerCount).ToArray();
        var p99 = profileRuns
            .Select(static run => run.Measurement.StepDuration?.P99 ?? TimeSpan.Zero)
            .DefaultIfEmpty(TimeSpan.Zero)
            .Max();
        var missRatio = profileRuns
            .Select(static run => run.Measurement.StepDeadlineMissRatio)
            .DefaultIfEmpty(0d)
            .Max();
        return new ProfileMeasurementSummary(profileRuns.Length, p99, missRatio);
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

    private static bool DigestEqual(byte[]? left, byte[]? right)
        => left is { Length: 32 } && right is { Length: 32 } && left.AsSpan().SequenceEqual(right);

    private sealed record ProfileMeasurementSummary(
        int RunCount,
        TimeSpan WorstP99,
        double WorstDeadlineMissRatio);
}
