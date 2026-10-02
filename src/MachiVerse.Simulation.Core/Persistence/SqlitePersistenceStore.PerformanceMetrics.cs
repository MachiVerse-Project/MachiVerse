using System.Threading;

namespace MachiVerse.Simulation.Core.Persistence;

/// <summary>
/// Non-authoritative observer for successful SQLite COMMIT wall time. Metric observation must never
/// participate in transaction success, state identity, scheduling, ordering, or any other simulation
/// semantic decision.
/// </summary>
public interface IPersistenceCommitMetricSinkV1
{
    void RecordSuccessfulCommit(TimeSpan elapsed);
}

public sealed partial class SqlitePersistenceStore
{
    private static readonly AsyncLocal<IPersistenceCommitMetricSinkV1?> AmbientCommitMetricSink = new();

    private IPersistenceCommitMetricSinkV1? _commitMetricSink;
    private long _commitMetricObserverFailureCount;

    public long CommitMetricObserverFailureCount => Interlocked.Read(ref _commitMetricObserverFailureCount);

    public void AttachCommitMetricSink(IPersistenceCommitMetricSinkV1 sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        if (Interlocked.CompareExchange(ref _commitMetricSink, sink, null) is not null)
            throw new InvalidOperationException("persistence.commit-metric-sink-already-attached");
    }

    /// <summary>
    /// Installs a diagnostics-only sink for stores opened inside the current async control flow.
    /// This is intended for benchmark/process probes that do not own the store instance directly.
    /// The previous ambient sink is restored when the returned scope is disposed.
    /// </summary>
    public static IDisposable PushAmbientCommitMetricSink(IPersistenceCommitMetricSinkV1 sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        var previous = AmbientCommitMetricSink.Value;
        AmbientCommitMetricSink.Value = sink;
        return new AmbientCommitMetricScope(previous);
    }

    private void ObserveSuccessfulCommit(TimeSpan elapsed)
    {
        var attached = Volatile.Read(ref _commitMetricSink);
        var ambient = AmbientCommitMetricSink.Value;

        ObserveOne(attached, elapsed);
        if (ambient is not null && !ReferenceEquals(ambient, attached))
            ObserveOne(ambient, elapsed);
    }

    private void ObserveOne(IPersistenceCommitMetricSinkV1? sink, TimeSpan elapsed)
    {
        if (sink is null) return;

        try
        {
            sink.RecordSuccessfulCommit(elapsed);
        }
        catch
        {
            // A metric sink is diagnostics-only. A successful durable COMMIT must not be converted
            // into an apparent transition failure because observability code failed after COMMIT.
            Interlocked.Increment(ref _commitMetricObserverFailureCount);
        }
    }

    private sealed class AmbientCommitMetricScope(IPersistenceCommitMetricSinkV1? previous) : IDisposable
    {
        private IPersistenceCommitMetricSinkV1? _previous = previous;
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            AmbientCommitMetricSink.Value = _previous;
            _previous = null;
        }
    }
}
