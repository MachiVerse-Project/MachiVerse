namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Completes the QA-04 threshold evaluation with telemetry-integrity evidence that cannot be
/// represented by percentile values alone. In particular, a GC polling index gap is a measurement
/// failure even when the observed pause p99 happens to be below the threshold.
/// </summary>
public static class Qa04PerformanceTelemetryAcceptanceV1
{
    public static Qa04PerformanceRunAcceptanceV1 EvaluateCompleteMeasurement(
        Qa04PerformanceMeasurementSnapshotV1 measurement,
        ulong acceptedOperationLossCount,
        ulong hiddenSolverIterationReductionCount,
        long persistenceMetricObserverFailureCount,
        Qa04GcPauseTelemetrySnapshotV1 gcTelemetry)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        ArgumentNullException.ThrowIfNull(gcTelemetry);

        var baseline = Qa04PerformanceThresholdsV1.EvaluateCompleteMeasurement(
            measurement,
            acceptedOperationLossCount,
            hiddenSolverIterationReductionCount,
            persistenceMetricObserverFailureCount);
        var failures = baseline.FailureCodes.ToList();

        var observedPauseCount = measurement.GcPauseDuration?.SampleCount ?? 0;
        if (observedPauseCount != gcTelemetry.PauseSampleCount)
            failures.Add("qa04.measurement.gc-pause-sample-count-mismatch");
        if (!gcTelemetry.TelemetryComplete)
            failures.Add("qa04.measurement.gc-index-gap");

        return new Qa04PerformanceRunAcceptanceV1(
            failures.Count == 0,
            Array.AsReadOnly(failures.Distinct(StringComparer.Ordinal).ToArray()));
    }
}
