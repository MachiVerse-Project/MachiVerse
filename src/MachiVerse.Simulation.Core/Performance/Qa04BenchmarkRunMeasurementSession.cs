using System.Diagnostics;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Harness-only wall-time session for one sequential perf.reference.v1 process run. The caller
/// executes every authoritative finalizing Step through this session in finalized-Step order.
/// Warm-up/cooldown Steps are executed but never sampled. Measurement Step duration is timed around
/// the supplied authoritative Step delegate; process working-set observation occurs only after the
/// successful Step has finished and therefore cannot alter the measured Step duration.
/// </summary>
public sealed class Qa04BenchmarkRunMeasurementSessionV1
{
    private readonly Qa04BenchmarkMetricCollectorV1 _collector;
    private readonly Func<long> _workingSetBytes;
    private ulong _lastFinalizedStep;
    private long? _measurementStartTimestamp;
    private int _stepInFlight;

    public Qa04BenchmarkRunMeasurementSessionV1(
        Qa04BenchmarkMetricCollectorV1? collector = null,
        Func<long>? workingSetBytes = null)
    {
        _collector = collector ?? new Qa04BenchmarkMetricCollectorV1();
        _workingSetBytes = workingSetBytes ?? ReadCurrentProcessWorkingSetBytes;
    }

    public Qa04BenchmarkMetricCollectorV1 Collector => _collector;
    public ulong LastFinalizedStep => _lastFinalizedStep;

    public async Task ExecuteFinalizingStepAsync(
        ulong resultingFinalizedStep,
        Func<CancellationToken, Task> executeAuthoritativeStep,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executeAuthoritativeStep);
        if (Interlocked.Exchange(ref _stepInFlight, 1) != 0)
            throw new InvalidOperationException("qa04.measurement.concurrent-step-not-supported");

        try
        {
            var expectedStep = checked(_lastFinalizedStep + 1);
            if (resultingFinalizedStep != expectedStep)
                throw new InvalidDataException("qa04.measurement.finalized-step-order");

            cancellationToken.ThrowIfCancellationRequested();
            var collect = Qa04MeasurementPhaseContractV1.ShouldCollectPerformanceSample(resultingFinalizedStep);
            var stepStarted = Stopwatch.GetTimestamp();
            if (collect && _measurementStartTimestamp is null)
                _measurementStartTimestamp = stepStarted;

            await executeAuthoritativeStep(cancellationToken).ConfigureAwait(false);
            var stepFinished = Stopwatch.GetTimestamp();
            _lastFinalizedStep = resultingFinalizedStep;

            if (!collect)
                return;

            var measurementStart = _measurementStartTimestamp
                ?? throw new InvalidOperationException("qa04.measurement.start-timestamp-missing");
            _collector.RecordStepDuration(
                Stopwatch.GetElapsedTime(measurementStart, stepFinished),
                Stopwatch.GetElapsedTime(stepStarted, stepFinished));
            _collector.RecordCoreWorkingSetBytes(_workingSetBytes());
        }
        finally
        {
            Volatile.Write(ref _stepInFlight, 0);
        }
    }

    public Qa04PerformanceMeasurementSnapshotV1 Snapshot() => _collector.Snapshot();

    private static long ReadCurrentProcessWorkingSetBytes()
    {
        using var process = Process.GetCurrentProcess();
        return process.WorkingSet64;
    }
}

/// <summary>
/// Measures the caller-held running-Snapshot consistency barrier around the actual production
/// RunningSnapshotCoordinator freeze call. A sample is recorded only when a frozen cut is actually
/// exposed. Not-due/in-flight null results and failed freezes create no successful COW sample.
/// </summary>
public static class Qa04RunningSnapshotMeasurementExtensionsV1
{
    public static Task<RunningSnapshotCutV1?> TryFreezeIfDueMeasuredAsync(
        this RunningSnapshotCoordinatorV1 coordinator,
        WorldStateV1 finalizedState,
        SqlitePersistenceStore store,
        Qa04BenchmarkMetricCollectorV1 collector,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(finalizedState);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(collector);

        return MeasureSuccessfulCutAsync(
            collector,
            token => coordinator.TryFreezeIfDueAsync(finalizedState, store, token),
            cancellationToken);
    }

    public static Task<RunningSnapshotCutV1?> TryFreezeWithCoreOwnerMaterialIfDueMeasuredAsync(
        this RunningSnapshotCoordinatorV1 coordinator,
        WorldStateV1 finalizedState,
        SqlitePersistenceStore store,
        IEnumerable<IFrozenCoreSnapshotOwnerMaterialV1> supplementalOwnerMaterial,
        Qa04BenchmarkMetricCollectorV1 collector,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(finalizedState);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(supplementalOwnerMaterial);
        ArgumentNullException.ThrowIfNull(collector);

        return MeasureSuccessfulCutAsync(
            collector,
            token => coordinator.TryFreezeWithCoreOwnerMaterialIfDueAsync(
                finalizedState,
                store,
                supplementalOwnerMaterial,
                token),
            cancellationToken);
    }

    internal static async Task<T?> MeasureSuccessfulMaterializationAsync<T>(
        Qa04BenchmarkMetricCollectorV1 collector,
        Func<CancellationToken, Task<T?>> materialize,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(collector);
        ArgumentNullException.ThrowIfNull(materialize);

        var started = Stopwatch.GetTimestamp();
        var result = await materialize(cancellationToken).ConfigureAwait(false);
        if (result is not null)
            collector.RecordSnapshotCowBarrier(Stopwatch.GetElapsedTime(started));
        return result;
    }

    private static async Task<RunningSnapshotCutV1?> MeasureSuccessfulCutAsync(
        Qa04BenchmarkMetricCollectorV1 collector,
        Func<CancellationToken, Task<RunningSnapshotCutV1?>> freeze,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var cut = await freeze(cancellationToken).ConfigureAwait(false);
        if (cut is not null)
            collector.RecordSnapshotCowBarrier(Stopwatch.GetElapsedTime(started));
        return cut;
    }
}
