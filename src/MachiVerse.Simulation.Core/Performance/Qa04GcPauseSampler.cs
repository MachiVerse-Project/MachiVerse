namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04GcMemoryInfoSampleV1(
    long Index,
    IReadOnlyList<TimeSpan> PauseDurations);

public sealed record Qa04GcPauseTelemetrySnapshotV1(
    int PauseSampleCount,
    long MeasurementStartMaxGcIndex,
    long HighestCompletedGcIndexObserved,
    int UnresolvedStartedGcIndexCount)
{
    public bool TelemetryComplete => UnresolvedStartedGcIndexCount == 0;
}

/// <summary>
/// QA-04 GC-pause observer built on GCMemoryInfo for the three disjoint GC kinds. Each measurement
/// Step polls Ephemeral, FullBlocking, and Background independently. Because GCMemoryInfo exposes
/// only the latest completed collection for a kind, the sampler also tracks global GC indices and
/// fails closed when a started-GC index between the measurement baseline and the highest completed
/// index was never observed. This prevents polling gaps from silently producing an optimistic p99.
/// </summary>
public sealed class Qa04GcPauseSamplerV1
{
    private static readonly GCKind[] Kinds =
    [
        GCKind.Ephemeral,
        GCKind.FullBlocking,
        GCKind.Background,
    ];

    private readonly Qa04BenchmarkMetricCollectorV1 _collector;
    private readonly Func<GCKind, Qa04GcMemoryInfoSampleV1> _readInfo;
    private readonly Dictionary<GCKind, long> _baselineByKind = [];
    private readonly HashSet<long> _observedMeasurementIndices = [];
    private long _measurementStartMaxGcIndex;
    private long _highestCompletedGcIndexObserved;
    private int _pauseSampleCount;
    private bool _started;

    public Qa04GcPauseSamplerV1(
        Qa04BenchmarkMetricCollectorV1 collector,
        Func<GCKind, Qa04GcMemoryInfoSampleV1>? readInfo = null)
    {
        _collector = collector ?? throw new ArgumentNullException(nameof(collector));
        _readInfo = readInfo ?? ReadRuntimeInfo;
    }

    public void BeginMeasurement()
    {
        if (_started)
            throw new InvalidOperationException("qa04.gc.measurement-already-started");

        long max = 0;
        foreach (var kind in Kinds)
        {
            var sample = _readInfo(kind);
            ValidateSample(sample);
            _baselineByKind.Add(kind, sample.Index);
            if (sample.Index > max) max = sample.Index;
        }

        _measurementStartMaxGcIndex = max;
        _highestCompletedGcIndexObserved = max;
        _started = true;
    }

    public void SampleCompletedCollections()
    {
        if (!_started)
            throw new InvalidOperationException("qa04.gc.measurement-not-started");

        foreach (var kind in Kinds)
        {
            var sample = _readInfo(kind);
            ValidateSample(sample);
            if (sample.Index == 0 || sample.Index == _baselineByKind[kind])
                continue;
            if (!_observedMeasurementIndices.Add(sample.Index))
                continue;

            foreach (var pause in sample.PauseDurations)
            {
                _collector.RecordGcPause(pause);
                _pauseSampleCount = checked(_pauseSampleCount + 1);
            }

            if (sample.Index > _highestCompletedGcIndexObserved)
                _highestCompletedGcIndexObserved = sample.Index;
        }
    }

    public Qa04GcPauseTelemetrySnapshotV1 Snapshot()
    {
        if (!_started)
            return new Qa04GcPauseTelemetrySnapshotV1(0, 0, 0, 0);

        var unresolved = 0;
        for (var index = checked(_measurementStartMaxGcIndex + 1);
             index <= _highestCompletedGcIndexObserved;
             index++)
        {
            if (!_observedMeasurementIndices.Contains(index))
                unresolved = checked(unresolved + 1);
            if (index == long.MaxValue) break;
        }

        return new Qa04GcPauseTelemetrySnapshotV1(
            _pauseSampleCount,
            _measurementStartMaxGcIndex,
            _highestCompletedGcIndexObserved,
            unresolved);
    }

    private static Qa04GcMemoryInfoSampleV1 ReadRuntimeInfo(GCKind kind)
    {
        var info = GC.GetGCMemoryInfo(kind);
        return new Qa04GcMemoryInfoSampleV1(
            info.Index,
            Array.AsReadOnly(info.PauseDurations.ToArray()));
    }

    private static void ValidateSample(Qa04GcMemoryInfoSampleV1 sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentNullException.ThrowIfNull(sample.PauseDurations);
        if (sample.Index < 0)
            throw new InvalidDataException("qa04.gc.index-negative");
        foreach (var pause in sample.PauseDurations)
        {
            if (pause < TimeSpan.Zero)
                throw new InvalidDataException("qa04.gc.pause-negative");
        }
    }
}
