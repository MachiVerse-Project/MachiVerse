using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Performance;

internal static class Qa04PerformanceMeasurementInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        VerifyNearestRankConvention();
        VerifyCanonicalPassingFixture();
        VerifyFailClosedSampleCoverage();
        VerifyMonotonicStepClock();
    }

    private static void VerifyNearestRankConvention()
    {
        var series = new Qa04DurationSeriesV1();
        for (var millisecond = 1; millisecond <= 100; millisecond++)
            series.Record(TimeSpan.FromMilliseconds(millisecond));

        var summary = series.SnapshotOrNull()
            ?? throw new InvalidOperationException("QA-04 duration summary unexpectedly missing.");
        Require(summary.SampleCount == 100, "QA-04 duration sample count drifted.");
        Require(summary.P50 == TimeSpan.FromMilliseconds(50), "QA-04 nearest-rank p50 drifted.");
        Require(summary.P95 == TimeSpan.FromMilliseconds(95), "QA-04 nearest-rank p95 drifted.");
        Require(summary.P99 == TimeSpan.FromMilliseconds(99), "QA-04 nearest-rank p99 drifted.");
    }

    private static void VerifyCanonicalPassingFixture()
    {
        var collector = new Qa04BenchmarkMetricCollectorV1();
        var elapsed = TimeSpan.Zero;
        for (var index = 0; index < Qa04PerformanceThresholdsV1.ExpectedMeasurementStepCount; index++)
        {
            elapsed += TimeSpan.FromMilliseconds(34);
            collector.RecordStepDuration(elapsed, TimeSpan.FromMilliseconds(20));
            collector.RecordSuccessfulCommit(TimeSpan.FromMilliseconds(3));
        }
        collector.RecordSnapshotCowBarrier(TimeSpan.FromMilliseconds(4));
        collector.RecordGcPause(TimeSpan.FromMilliseconds(10));
        collector.RecordCoreWorkingSetBytes(21L * 1024 * 1024 * 1024);

        var snapshot = collector.Snapshot();
        Require(snapshot.StepSampleCount == 18_000, "QA-04 canonical measurement Step count drifted.");
        Require(snapshot.MaxRolling60SecondMeanMilliseconds is { } rolling && Math.Abs(rolling - 20d) < 0.0001,
            "QA-04 trailing 60-second mean convention drifted.");

        var acceptance = Qa04PerformanceThresholdsV1.EvaluateCompleteMeasurement(
            snapshot,
            acceptedOperationLossCount: 0,
            hiddenSolverIterationReductionCount: 0,
            persistenceMetricObserverFailureCount: 0);
        Require(acceptance.Passed && acceptance.FailureCodes.Count == 0,
            "QA-04 canonical passing measurement fixture must pass.");
    }

    private static void VerifyFailClosedSampleCoverage()
    {
        var collector = new Qa04BenchmarkMetricCollectorV1();
        collector.RecordStepDuration(TimeSpan.FromSeconds(61), TimeSpan.FromMilliseconds(10));
        collector.RecordSuccessfulCommit(TimeSpan.FromMilliseconds(1));
        collector.RecordCoreWorkingSetBytes(1);

        var acceptance = Qa04PerformanceThresholdsV1.EvaluateCompleteMeasurement(
            collector.Snapshot(),
            acceptedOperationLossCount: 1,
            hiddenSolverIterationReductionCount: 1,
            persistenceMetricObserverFailureCount: 1);
        Require(!acceptance.Passed, "Incomplete QA-04 measurement fixture must fail closed.");
        Require(acceptance.FailureCodes.Contains("qa04.measurement.step-sample-count", StringComparer.Ordinal),
            "QA-04 missing Step sample failure must be retained.");
        Require(acceptance.FailureCodes.Contains("qa04.measurement.commit-sample-count", StringComparer.Ordinal),
            "QA-04 missing COMMIT sample failure must be retained.");
        Require(acceptance.FailureCodes.Contains("qa04.measurement.snapshot-cow-sample-missing", StringComparer.Ordinal),
            "QA-04 missing Snapshot COW sample failure must be retained.");
        Require(acceptance.FailureCodes.Contains("qa04.performance.accepted-operation-loss", StringComparer.Ordinal),
            "QA-04 accepted Operation loss must fail acceptance.");
        Require(acceptance.FailureCodes.Contains("qa04.performance.hidden-solver-iteration-reduction", StringComparer.Ordinal),
            "QA-04 hidden solver reduction must fail acceptance.");
        Require(acceptance.FailureCodes.Contains("qa04.measurement.persistence-observer-failure", StringComparer.Ordinal),
            "QA-04 observer failure must fail acceptance.");
    }

    private static void VerifyMonotonicStepClock()
    {
        var collector = new Qa04BenchmarkMetricCollectorV1();
        collector.RecordStepDuration(TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(1));
        try
        {
            collector.RecordStepDuration(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1));
        }
        catch (InvalidDataException ex) when (ex.Message == "qa04.measurement.step-elapsed-nonmonotonic")
        {
            return;
        }
        throw new InvalidOperationException("QA-04 measurement clock must reject non-monotonic Step timestamps.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
