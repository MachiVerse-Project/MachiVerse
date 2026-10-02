using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Performance;

internal static class Qa04PerformanceRepetitionContractInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        VerifyCanonicalMatrixPasses();
        VerifyWorker16MedianFailsClosed();
        VerifyDeterminismMismatchFailsClosed();
        VerifyMissingRunFailsClosed();
    }

    private static void VerifyCanonicalMatrixPasses()
    {
        var runs = BuildCanonicalRuns(worker16P95Milliseconds: [30, 32, 33]);
        var result = Qa04PerformanceRepetitionContractV1.Evaluate(runs);
        Require(result.Passed, "Canonical QA-04 repetition fixture must pass.");
        Require(result.Worker16MedianRunP95 == TimeSpan.FromMilliseconds(32),
            "QA-04 median-of-three p95 convention drifted.");
    }

    private static void VerifyWorker16MedianFailsClosed()
    {
        var runs = BuildCanonicalRuns(worker16P95Milliseconds: [30, 34, 40]);
        var result = Qa04PerformanceRepetitionContractV1.Evaluate(runs);
        Require(!result.Passed && result.FailureCodes.Contains("qa04.performance.worker16-median-run-p95", StringComparer.Ordinal),
            "QA-04 worker-16 median p95 must fail above the P4-06 threshold.");
    }

    private static void VerifyDeterminismMismatchFailsClosed()
    {
        var runs = BuildCanonicalRuns(worker16P95Milliseconds: [30, 31, 32]).ToList();
        var index = runs.FindIndex(static run => run.WorkerCount == 8 && run.RunOrdinal == 2);
        var mismatch = runs[index];
        runs[index] = mismatch with
        {
            Determinism = mismatch.Determinism with { FinalStateDigest = Digest(0x7f) },
        };
        var result = Qa04PerformanceRepetitionContractV1.Evaluate(runs);
        Require(!result.Passed && result.FailureCodes.Contains("qa04.determinism.final-state-digest", StringComparer.Ordinal),
            "QA-04 cross-worker final StateDigest mismatch must fail closed.");
    }

    private static void VerifyMissingRunFailsClosed()
    {
        var runs = BuildCanonicalRuns(worker16P95Milliseconds: [30, 31, 32]).ToList();
        runs.RemoveAt(runs.Count - 1);
        var result = Qa04PerformanceRepetitionContractV1.Evaluate(runs);
        Require(!result.Passed &&
                result.FailureCodes.Contains("qa04.repetition.run-count", StringComparer.Ordinal) &&
                result.FailureCodes.Contains("qa04.repetition.missing-run-key", StringComparer.Ordinal),
            "QA-04 missing process repetition must fail closed.");
    }

    private static IReadOnlyList<Qa04MeasuredProcessRunV1> BuildCanonicalRuns(int[] worker16P95Milliseconds)
    {
        var runs = new List<Qa04MeasuredProcessRunV1>();
        foreach (var worker in Qa04PerformanceRepetitionContractV1.CanonicalWorkerCounts)
        {
            for (var ordinal = 1; ordinal <= Qa04PerformanceRepetitionContractV1.RequiredRunsPerWorker; ordinal++)
            {
                var p95 = worker == 16 ? worker16P95Milliseconds[ordinal - 1] : 100;
                runs.Add(new Qa04MeasuredProcessRunV1(
                    worker,
                    ordinal,
                    Measurement(p95),
                    Evidence()));
            }
        }
        return runs;
    }

    private static Qa04PerformanceMeasurementSnapshotV1 Measurement(int p95Milliseconds)
    {
        var summary = new Qa04DurationSummaryV1(
            Qa04PerformanceThresholdsV1.ExpectedMeasurementStepCount,
            TimeSpan.FromMilliseconds(1),
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(p95Milliseconds),
            TimeSpan.FromMilliseconds(40),
            TimeSpan.FromMilliseconds(45),
            20d);
        return new Qa04PerformanceMeasurementSnapshotV1(
            Qa04PerformanceThresholdsV1.ExpectedMeasurementStepCount,
            summary,
            20d,
            summary,
            new Qa04DurationSummaryV1(1, TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1), 1d),
            null,
            1,
            1);
    }

    private static Qa04DeterminismEvidenceV1 Evidence()
        => new(Digest(1), Digest(2), Digest(3), Digest(4), Digest(5));

    private static byte[] Digest(byte value) => Enumerable.Repeat(value, 32).ToArray();

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
