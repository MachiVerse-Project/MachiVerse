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
    private IPersistenceCommitMetricSinkV1? _commitMetricSink;
    private long _commitMetricObserverFailureCount;

    public long CommitMetricObserverFailureCount => Interlocked.Read(ref _commitMetricObserverFailureCount);

    public void AttachCommitMetricSink(IPersistenceCommitMetricSinkV1 sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        if (Interlocked.CompareExchange(ref _commitMetricSink, sink, null) is not null)
            throw new InvalidOperationException("persistence.commit-metric-sink-already-attached");
    }

    private void ObserveSuccessfulCommit(TimeSpan elapsed)
    {
        var sink = Volatile.Read(ref _commitMetricSink);
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
}
