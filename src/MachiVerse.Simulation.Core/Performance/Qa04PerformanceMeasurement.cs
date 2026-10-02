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
    long MaxCoreWorkingSetBytes);

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
/// </summary>
public sealed class Qa04BenchmarkMetricCollectorV1 : IPersistenceCommitMetricSinkV1
{
    private static readonly TimeSpan RollingWindow = TimeSpan.FromSeconds(60);

    private readonly object _stepSync = new();
    private readonly List<Qa04StepWallSampleV1> _stepSamples = [];
    private int _coreWorkingSetSampleCount;
    private long _maxCoreWorkingSetBytes;

    public Qa04DurationSeriesV1 SqliteCommitDurations { get; } = new();
    public Qa04DurationSeriesV1 SnapshotCowBarrierDurations { get; } = new();
    public Qa04DurationSeriesV1 GcPauseDurations { get; } = new();

    public void RecordSuccessfulCommit(TimeSpan elapsed) => SqliteCommitDurations.Record(elapsed);

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
        foreach (var step in steps) stepDurations.Record(step.Duration);

        return new Qa04PerformanceMeasurementSnapshotV1(
            steps.Length,
            stepDurations.SnapshotOrNull(),
            ComputeMaxRolling60SecondMeanMilliseconds(steps),
            SqliteCommitDurations.SnapshotOrNull(),
            SnapshotCowBarrierDurations.SnapshotOrNull(),
            GcPauseDurations.SnapshotOrNull(),
            Volatile.Read(ref _coreWorkingSetSampleCount),
            Interlocked.Read(ref _maxCoreWorkingSetBytes));
    }

    private static double? ComputeMaxRolling60SecondMeanMilliseconds(IReadOnlyList<Qa04StepWallSampleV1> samples)
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

public static class Qa04PerformanceThresholdsV1
{
    public const int ExpectedMeasurementStepCount = 18_000;
    public const long CoreSteadyTargetBytes = 22L * 1024 * 1024 * 1024;
    public const long CoreHardGuardBytes = 28L * 1024 * 1024 * 1024;

    public static readonly TimeSpan StepP50Max = TimeSpan.FromMilliseconds(20);
    public static readonly TimeSpan StepP95Max = TimeSpan.FromTicks(333_330); // 33.333 ms
    public static readonly TimeSpan StepP99Max = TimeSpan.FromMilliseconds(50);
    public const double Rolling60SecondMeanMaxMilliseconds = 30d;
    public static readonly TimeSpan SqliteCommitP95Max = TimeSpan.FromMilliseconds(4);
    public static readonly TimeSpan SqliteCommitP99Max = TimeSpan.FromMilliseconds(8);
    public static readonly TimeSpan SnapshotCowBarrierP95Max = TimeSpan.FromMilliseconds(5);
    public static readonly TimeSpan GcPauseP99Max = TimeSpan.FromMilliseconds(20);

    public static Qa04PerformanceRunAcceptanceV1 EvaluateCompleteMeasurement(
        Qa04PerformanceMeasurementSnapshotV1 measurement,
        ulong acceptedOperationLossCount,
        ulong hiddenSolverIterationReductionCount,
        long persistenceMetricObserverFailureCount)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        var failures = new List<string>();

        if (measurement.StepSampleCount != ExpectedMeasurementStepCount)
            failures.Add("qa04.measurement.step-sample-count");
        if (measurement.SqliteCommitDuration?.SampleCount != ExpectedMeasurementStepCount)
            failures.Add("qa04.measurement.commit-sample-count");
        if (measurement.SnapshotCowBarrierDuration is null)
            failures.Add("qa04.measurement.snapshot-cow-sample-missing");
        if (measurement.CoreWorkingSetSampleCount == 0)
            failures.Add("qa04.measurement.memory-sample-missing");

        if (measurement.StepDuration is { } step)
        {
            if (step.P50 > StepP50Max) failures.Add("qa04.performance.step-p50");
            if (step.P95 > StepP95Max) failures.Add("qa04.performance.step-p95");
            if (step.P99 > StepP99Max) failures.Add("qa04.performance.step-p99");
        }
        else
        {
            failures.Add("qa04.measurement.step-duration-missing");
        }

        if (measurement.MaxRolling60SecondMeanMilliseconds is not { } rollingMean)
            failures.Add("qa04.measurement.rolling-60s-unavailable");
        else if (rollingMean > Rolling60SecondMeanMaxMilliseconds)
            failures.Add("qa04.performance.rolling-60s-mean");

        if (measurement.CoreWorkingSetSampleCount > 0)
        {
            if (measurement.MaxCoreWorkingSetBytes > CoreSteadyTargetBytes)
                failures.Add("qa04.performance.core-memory-target");
            if (measurement.MaxCoreWorkingSetBytes > CoreHardGuardBytes)
                failures.Add("qa04.performance.core-memory-guard");
        }

        if (measurement.SqliteCommitDuration is { } commit)
        {
            if (commit.P95 > SqliteCommitP95Max) failures.Add("qa04.performance.sqlite-commit-p95");
            if (commit.P99 > SqliteCommitP99Max) failures.Add("qa04.performance.sqlite-commit-p99");
        }

        if (measurement.SnapshotCowBarrierDuration is { } cow && cow.P95 > SnapshotCowBarrierP95Max)
            failures.Add("qa04.performance.snapshot-cow-p95");

        if (measurement.GcPauseDuration is { } gc && gc.P99 > GcPauseP99Max)
            failures.Add("qa04.performance.gc-pause-p99");

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
