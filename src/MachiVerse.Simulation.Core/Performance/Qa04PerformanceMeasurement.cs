using MachiVerse.Simulation.Core.Persistence;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04DurationSummaryV1(
    int SampleCount,
    TimeSpan Minimum,
    TimeSpan P50,
    TimeSpan P95,
    TimeSpan P99,
    TimeSpan Maximum,
    double MeanMilliseconds);

public sealed record Qa04StepWallSampleV1(
    TimeSpan MeasurementElapsed,
    TimeSpan Duration);

public sealed record Qa04PerformanceMeasurementSnapshotV1(
    int StepSampleCount,
    Qa04DurationSummaryV1? StepDuration,
    double? MaxRolling60SecondMeanMilliseconds,
    Qa04DurationSummaryV1? SqliteCommitDuration,
    Qa04DurationSummaryV1? SnapshotCowBarrierDuration,
    Qa04DurationSummaryV1? GcPauseDuration,
    int CoreWorkingSetSampleCount,
    long MaxCoreWorkingSetBytes)
{
    public string AcceptanceConfigSha256 { get; init; } = "";
    public double StepDeadlineMilliseconds { get; init; }
    public int StepDeadlineMissCount { get; init; }
    public double StepDeadlineMissRatio { get; init; }
}

public sealed record Qa04PerformanceRunAcceptanceV1(
    bool Passed,
    IReadOnlyList<string> FailureCodes);

/// <summary>
/// Raw operational timing series for QA-04. Percentiles use nearest-rank on the sorted TimeSpan
/// ticks. This is a benchmark measurement convention only; no value from this collector may feed
/// back into authoritative simulation behavior.
/// </summary>
public sealed class Qa04DurationSeriesV1
{
    private readonly object _sync = new();
    private readonly List<long> _ticks = [];

    public int Count
    {
        get
        {
            lock (_sync) return _ticks.Count;
        }
    }

    public void Record(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration));
        lock (_sync) _ticks.Add(duration.Ticks);
    }

    public Qa04DurationSummaryV1? SnapshotOrNull()
    {
        long[] samples;
        lock (_sync)
        {
            if (_ticks.Count == 0) return null;
            samples = _ticks.ToArray();
        }

        Array.Sort(samples);
        decimal sum = 0;
        foreach (var sample in samples) sum += sample;

        return new Qa04DurationSummaryV1(
            samples.Length,
            TimeSpan.FromTicks(samples[0]),
            TimeSpan.FromTicks(NearestRank(samples, 50)),
            TimeSpan.FromTicks(NearestRank(samples, 95)),
            TimeSpan.FromTicks(NearestRank(samples, 99)),
            TimeSpan.FromTicks(samples[^1]),
            (double)(sum / samples.Length) / TimeSpan.TicksPerMillisecond);
    }

    private static long NearestRank(IReadOnlyList<long> sortedSamples, int percentile)
    {
        if (percentile is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(percentile));
        var rank = checked((int)Math.Ceiling(percentile / 100d * sortedSamples.Count));
        return sortedSamples[rank - 1];
    }
}

/// <summary>
/// Benchmark-only measurement sink. Callers supply monotonic measurement elapsed time for finalized
/// Steps; the collector computes the worst trailing 60 wall-second mean over finalized Step samples.
/// Alpha 1.1 deadline telemetry is derived from the same raw Step samples and never feeds back into
/// authoritative simulation behavior.
/// </summary>
public sealed class Qa04BenchmarkMetricCollectorV1 : IPersistenceCommitMetricSinkV1
{
    private readonly Qa04AcceptanceConfigV1 _acceptance;
    private readonly TimeSpan RollingWindow;

    public Qa04BenchmarkMetricCollectorV1(Qa04AcceptanceConfigV1? acceptanceConfig = null)
    {
        _acceptance = acceptanceConfig ?? Qa04AcceptanceConfigV1.Current;
        _acceptance.Validate();
        RollingWindow = TimeSpan.FromSeconds(_acceptance.RollingWindowSeconds);
    }

    private readonly object _stepSync = new();
    private readonly List<Qa04StepWallSampleV1> _stepSamples = [];
    private int _coreWorkingSetSampleCount;
    private long _maxCoreWorkingSetBytes;

    public Qa04DurationSeriesV1 SqliteCommitDurations { get; } = new();
    public Qa04DurationSeriesV1 SnapshotCowBarrierDurations { get; } = new();
    public Qa04DurationSeriesV1 GcPauseDurations { get; } = new();

    public void RecordSuccessfulCommit(TimeSpan elapsed) => SqliteCommitDurations.Record(elapsed);

    public void RecordSuccessfulCommit(TimeSpan elapsed, PersistenceCommitKindV1 kind)
    {
        // QA-04 measures the COMMIT that finalizes State(n+1), once per measured Step.
        // Admission/scheduling remains inside Step wall time but is a different transaction.
        if (kind == PersistenceCommitKindV1.FinalizedTransition)
            RecordSuccessfulCommit(elapsed);
        else if (kind != PersistenceCommitKindV1.ScheduledOperationBatch)
            throw new ArgumentOutOfRangeException(nameof(kind));
    }

    public void RecordStepDuration(TimeSpan measurementElapsed, TimeSpan duration)
    {
        if (measurementElapsed < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(measurementElapsed));
        if (duration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration));

        lock (_stepSync)
        {
            if (_stepSamples.Count > 0 && measurementElapsed < _stepSamples[^1].MeasurementElapsed)
                throw new InvalidDataException("qa04.measurement.step-elapsed-nonmonotonic");
            _stepSamples.Add(new Qa04StepWallSampleV1(measurementElapsed, duration));
        }
    }

    public void RecordSnapshotCowBarrier(TimeSpan duration) => SnapshotCowBarrierDurations.Record(duration);

    public void RecordGcPause(TimeSpan duration) => GcPauseDurations.Record(duration);

    public void RecordCoreWorkingSetBytes(long bytes)
    {
        if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        Interlocked.Increment(ref _coreWorkingSetSampleCount);
        while (true)
        {
            var current = Interlocked.Read(ref _maxCoreWorkingSetBytes);
            if (bytes <= current) return;
            if (Interlocked.CompareExchange(ref _maxCoreWorkingSetBytes, bytes, current) == current) return;
        }
    }

    public Qa04PerformanceMeasurementSnapshotV1 Snapshot()
    {
        Qa04StepWallSampleV1[] steps;
        lock (_stepSync) steps = _stepSamples.ToArray();

        var stepDurations = new Qa04DurationSeriesV1();
        var deadlineMissCount = 0;
        foreach (var step in steps)
        {
            stepDurations.Record(step.Duration);
            if (step.Duration > _acceptance.StepDeadline)
                deadlineMissCount++;
        }

        var deadlineMissRatio = steps.Length == 0
            ? 0d
            : deadlineMissCount / (double)steps.Length;

        return new Qa04PerformanceMeasurementSnapshotV1(
            steps.Length,
            stepDurations.SnapshotOrNull(),
            ComputeMaxRolling60SecondMeanMilliseconds(steps),
            SqliteCommitDurations.SnapshotOrNull(),
            SnapshotCowBarrierDurations.SnapshotOrNull(),
            GcPauseDurations.SnapshotOrNull(),
            Volatile.Read(ref _coreWorkingSetSampleCount),
            Interlocked.Read(ref _maxCoreWorkingSetBytes))
        {
            AcceptanceConfigSha256 = _acceptance.Sha256,
            StepDeadlineMilliseconds = _acceptance.StepDeadline.TotalMilliseconds,
            StepDeadlineMissCount = deadlineMissCount,
            StepDeadlineMissRatio = deadlineMissRatio,
        };
    }

    private double? ComputeMaxRolling60SecondMeanMilliseconds(IReadOnlyList<Qa04StepWallSampleV1> samples)
    {
        if (samples.Count == 0) return null;

        var windowTicks = RollingWindow.Ticks;
        var left = 0;
        long durationTicksInWindow = 0;
        double? maxMeanMilliseconds = null;

        for (var right = 0; right < samples.Count; right++)
        {
            durationTicksInWindow = checked(durationTicksInWindow + samples[right].Duration.Ticks);
            var windowStartTicks = samples[right].MeasurementElapsed.Ticks - windowTicks;
            while (left <= right && samples[left].MeasurementElapsed.Ticks <= windowStartTicks)
            {
                durationTicksInWindow = checked(durationTicksInWindow - samples[left].Duration.Ticks);
                left++;
            }

            if (samples[right].MeasurementElapsed < RollingWindow || left > right)
                continue;

            var count = right - left + 1;
            var meanMilliseconds = durationTicksInWindow / (double)count / TimeSpan.TicksPerMillisecond;
            if (maxMeanMilliseconds is null || meanMilliseconds > maxMeanMilliseconds.Value)
                maxMeanMilliseconds = meanMilliseconds;
        }

        return maxMeanMilliseconds;
    }
}

/// <summary>
/// Alpha 1.1 Gate 4 Step 3 performance acceptance. The production simulation target is 10 tick/s,
/// so one authoritative tick has a 100 ms processing budget. Pacing/sleep time is outside the
/// benchmark measurement body. Older 30Hz-oriented p50/p95/p99 and duration-projection targets are
/// retained only as historical evidence and are not Alpha 1.1 release blockers.
/// </summary>
public static class Qa04PerformanceThresholdsV1
{
    public static int ExpectedMeasurementStepCount => Qa04AcceptanceConfigV1.Current.MeasurementSteps;
    public static double Alpha11TargetTickRateHz => Qa04AcceptanceConfigV1.Current.TickRateHz;
    public static double DeadlineMissRatioMax => Qa04AcceptanceConfigV1.Current.DeadlineMissRatioMax;
    public static double HealthyMeanMilliseconds => Qa04AcceptanceConfigV1.Current.HealthyMeanMilliseconds;
    public static TimeSpan StepDeadline => Qa04AcceptanceConfigV1.Current.StepDeadline;
    public static TimeSpan StepP99Max => StepDeadline;
    public static long CoreSteadyTargetBytes => Qa04AcceptanceConfigV1.Current.CoreSteadyTargetBytes;
    public static long CoreHardGuardBytes => Qa04AcceptanceConfigV1.Current.CoreHardGuardBytes;

    public static Qa04PerformanceRunAcceptanceV1 EvaluateCompleteMeasurement(
        Qa04PerformanceMeasurementSnapshotV1 measurement,
        ulong acceptedOperationLossCount,
        ulong hiddenSolverIterationReductionCount,
        long persistenceMetricObserverFailureCount,
        Qa04AcceptanceConfigV1? acceptanceConfig = null)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        var policy = acceptanceConfig ?? Qa04AcceptanceConfigV1.Current;
        policy.Validate();
        var failures = new List<string>();

        if (measurement.StepSampleCount != policy.MeasurementSteps)
            failures.Add("qa04.measurement.step-sample-count");
        if (measurement.SqliteCommitDuration?.SampleCount != policy.MeasurementSteps)
            failures.Add("qa04.measurement.commit-sample-count");
        if (measurement.SnapshotCowBarrierDuration is null)
            failures.Add("qa04.measurement.snapshot-cow-sample-missing");
        if (measurement.CoreWorkingSetSampleCount <= 0)
            failures.Add("qa04.measurement.memory-sample-missing");

        if (measurement.CoreWorkingSetSampleCount > 0)
        {
            if (measurement.MaxCoreWorkingSetBytes < 0)
                failures.Add("qa04.measurement.memory-invalid");
            if (measurement.MaxCoreWorkingSetBytes > policy.CoreSteadyTargetBytes)
                failures.Add("qa04.performance.core-memory-target");
            if (measurement.MaxCoreWorkingSetBytes > policy.CoreHardGuardBytes)
                failures.Add("qa04.performance.core-memory-guard");
        }
        if (!double.IsFinite(measurement.StepDeadlineMilliseconds) || !double.IsFinite(measurement.StepDeadlineMissRatio))
            failures.Add("qa04.measurement.nonfinite-deadline");

        if (measurement.StepDuration is { } step)
        {
            if (step.SampleCount != measurement.StepSampleCount)
                failures.Add("qa04.measurement.step-duration-sample-count");
            if (step.P99 > policy.StepDeadline)
                failures.Add("qa04.performance.step-p99");
        }
        else
        {
            failures.Add("qa04.measurement.step-duration-missing");
        }

        if (Math.Abs(measurement.StepDeadlineMilliseconds - policy.StepDeadline.TotalMilliseconds) > 0.000001d)
            failures.Add("qa04.measurement.step-deadline-contract");
        if (measurement.StepDeadlineMissCount < 0 ||
            measurement.StepDeadlineMissCount > measurement.StepSampleCount)
            failures.Add("qa04.measurement.step-deadline-miss-count");

        var expectedMissRatio = measurement.StepSampleCount == 0
            ? 0d
            : measurement.StepDeadlineMissCount / (double)measurement.StepSampleCount;
        if (Math.Abs(measurement.StepDeadlineMissRatio - expectedMissRatio) > 0.000000001d)
            failures.Add("qa04.measurement.step-deadline-miss-ratio-drift");
        if (measurement.StepDeadlineMissRatio > policy.DeadlineMissRatioMax)
            failures.Add("qa04.performance.step-deadline-miss-ratio");

        if (acceptedOperationLossCount != 0)
            failures.Add("qa04.performance.accepted-operation-loss");
        if (hiddenSolverIterationReductionCount != 0)
            failures.Add("qa04.performance.hidden-solver-iteration-reduction");
        if (persistenceMetricObserverFailureCount != 0)
            failures.Add("qa04.measurement.persistence-observer-failure");

        return new Qa04PerformanceRunAcceptanceV1(
            failures.Count == 0,
            Array.AsReadOnly(failures.ToArray()));
    }
}
