using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Performance;

internal static class Qa04PerformanceRepetitionContractInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        VerifyCanonicalMatrixPasses();
        VerifyProfileP99FailsClosed();
        VerifyDeadlineMissRatioFailsClosed();
        VerifyDeterminismMismatchFailsClosed();
        VerifyMissingRunFailsClosed();
    }

    private static void VerifyCanonicalMatrixPasses()
    {
        var runs = BuildCanonicalRuns();
        var result = Qa04PerformanceRepetitionContractV1.Evaluate(runs);
        Require(result.Passed, "Canonical Alpha 1.1 QA-04 repetition fixture must pass.");
        Require(Qa04PerformanceRepetitionContractV1.CanonicalWorkerCounts.SequenceEqual(new[] { 8, 16 }),
            "Alpha 1.1 QA-04 repetition profiles must be workers=8/16 only.");
        Require(result.Worker8WorstRunP99 == TimeSpan.FromMilliseconds(100) &&
                result.Worker16WorstRunP99 == TimeSpan.FromMilliseconds(100),
            "Alpha 1.1 QA-04 profile p99 evidence drifted.");
        Require(Math.Abs(result.Worker8WorstDeadlineMissRatio - 0.01d) < 0.000000001 &&
                Math.Abs(result.Worker16WorstDeadlineMissRatio - 0.01d) < 0.000000001,
            "Alpha 1.1 QA-04 profile deadline-miss evidence drifted.");
    }

    private static void VerifyProfileP99FailsClosed()
    {
        var runs = BuildCanonicalRuns().ToList();
        var index = runs.FindIndex(static run => run.WorkerCount == 16 && run.RunOrdinal == 2);
        var current = runs[index];
        runs[index] = current with
        {
            Measurement = Measurement(
                p99Milliseconds: 101,
                deadlineMissCount: Qa04PerformanceThresholdsV1.ExpectedMeasurementStepCount / 100 + 1),
        };
        var result = Qa04PerformanceRepetitionContractV1.Evaluate(runs);
        Require(!result.Passed && result.FailureCodes.Contains("qa04.performance.step-p99", StringComparer.Ordinal),
            "QA-04 any Alpha 1.1 profile run above 100ms p99 must fail closed.");
    }

    private static void VerifyDeadlineMissRatioFailsClosed()
    {
        var runs = BuildCanonicalRuns().ToList();
        var index = runs.FindIndex(static run => run.WorkerCount == 8 && run.RunOrdinal == 1);
        var current = runs[index];
        runs[index] = current with
        {
            Measurement = Measurement(
                p99Milliseconds: 100,
                deadlineMissCount: Qa04PerformanceThresholdsV1.ExpectedMeasurementStepCount / 100 + 1),
        };
        var result = Qa04PerformanceRepetitionContractV1.Evaluate(runs);
        Require(!result.Passed &&
                result.FailureCodes.Contains("qa04.performance.step-deadline-miss-ratio", StringComparer.Ordinal),
            "QA-04 any Alpha 1.1 profile run above 1% deadline misses must fail closed.");
    }

    private static void VerifyDeterminismMismatchFailsClosed()
    {
        var runs = BuildCanonicalRuns().ToList();
        var index = runs.FindIndex(static run => run.WorkerCount == 8 && run.RunOrdinal == 2);
        var mismatch = runs[index];
        runs[index] = mismatch with
        {
            Determinism = mismatch.Determinism with { FinalStateDigest = Digest(0x7f) },
        };
        var result = Qa04PerformanceRepetitionContractV1.Evaluate(runs);
        Require(!result.Passed && result.FailureCodes.Contains("qa04.determinism.final-state-digest", StringComparer.Ordinal),
            "QA-04 cross-profile final StateDigest mismatch must fail closed.");
    }

    private static void VerifyMissingRunFailsClosed()
    {
        var runs = BuildCanonicalRuns().ToList();
        runs.RemoveAt(runs.Count - 1);
        var result = Qa04PerformanceRepetitionContractV1.Evaluate(runs);
        Require(!result.Passed &&
                result.FailureCodes.Contains("qa04.repetition.run-count", StringComparer.Ordinal) &&
                result.FailureCodes.Contains("qa04.repetition.missing-run-key", StringComparer.Ordinal),
            "QA-04 missing Alpha 1.1 process repetition must fail closed.");
    }

    private static IReadOnlyList<Qa04MeasuredProcessRunV1> BuildCanonicalRuns()
    {
        var runs = new List<Qa04MeasuredProcessRunV1>();
        foreach (var worker in Qa04PerformanceRepetitionContractV1.CanonicalWorkerCounts)
        {
            for (var ordinal = 1; ordinal <= Qa04PerformanceRepetitionContractV1.RequiredRunsPerWorker; ordinal++)
            {
                runs.Add(new Qa04MeasuredProcessRunV1(
                    worker,
                    ordinal,
                    Measurement(
                        p99Milliseconds: 100,
                        deadlineMissCount: Qa04PerformanceThresholdsV1.ExpectedMeasurementStepCount / 100),
                    Evidence()));
            }
        }
        return runs;
    }

    private static Qa04PerformanceMeasurementSnapshotV1 Measurement(
        int p99Milliseconds,
        int deadlineMissCount)
    {
        var sampleCount = Qa04PerformanceThresholdsV1.ExpectedMeasurementStepCount;
        var summary = new Qa04DurationSummaryV1(
            sampleCount,
            TimeSpan.FromMilliseconds(1),
            TimeSpan.FromMilliseconds(70),
            TimeSpan.FromMilliseconds(90),
            TimeSpan.FromMilliseconds(p99Milliseconds),
            TimeSpan.FromMilliseconds(Math.Max(100, p99Milliseconds)),
            80d);
        return new Qa04PerformanceMeasurementSnapshotV1(
            sampleCount,
            summary,
            80d,
            summary,
            new Qa04DurationSummaryV1(
                1,
                TimeSpan.FromMilliseconds(1),
                TimeSpan.FromMilliseconds(1),
                TimeSpan.FromMilliseconds(1),
                TimeSpan.FromMilliseconds(1),
                TimeSpan.FromMilliseconds(1),
                1d),
            null,
            1,
            1)
        {
            StepDeadlineMilliseconds = Qa04PerformanceThresholdsV1.StepDeadline.TotalMilliseconds,
            StepDeadlineMissCount = deadlineMissCount,
            StepDeadlineMissRatio = deadlineMissCount / (double)sampleCount,
        };
    }

    private static Qa04DeterminismEvidenceV1 Evidence()
        => new(Digest(1), Digest(2), Digest(3), Digest(4), Digest(5));

    private static byte[] Digest(byte value) => Enumerable.Repeat(value, 32).ToArray();

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
