using MachiVerse.Simulation.Core.Persistence;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04InstrumentedAuthoritativeStepLoopProbeV1(
    Qa04AuthoritativeStepLoopProbeV1 AuthorityProbe,
    Qa04PerformanceMeasurementSnapshotV1 Measurement)
{
    public int SqliteCommitSampleCount => Measurement.SqliteCommitDuration?.SampleCount ?? 0;
    public bool EveryReducedStepHasSqliteCommitSample
        => AuthorityProbe.RealSqliteCommitObservedThroughLoop &&
           SqliteCommitSampleCount == checked((int)AuthorityProbe.StepCount);
}

/// <summary>
/// Runs the existing reduced authoritative Step loop unchanged while observing successful SQLite
/// COMMIT latency through an AsyncLocal diagnostics scope. The observer cannot participate in world
/// state or Step decisions, and the proof fails closed unless every durable reduced Step emits one
/// successful COMMIT sample.
/// </summary>
public static class Qa04InstrumentedAuthoritativeStepLoopBridgeV1
{
    public static async Task<Qa04InstrumentedAuthoritativeStepLoopProbeV1> RunReducedAsync(
        int workerCount,
        ulong residentRecordCount,
        string persistenceRoot,
        CancellationToken cancellationToken = default)
    {
        var collector = new Qa04BenchmarkMetricCollectorV1();
        Qa04AuthoritativeStepLoopProbeV1 authority;
        using (SqlitePersistenceStore.PushAmbientCommitMetricSink(collector))
        {
            authority = await Qa04AuthoritativeStepLoopBridgeV1.RunReducedAsync(
                    workerCount,
                    residentRecordCount,
                    persistenceRoot,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var measurement = collector.Snapshot();
        var expectedCommitSamples = checked((int)authority.StepCount);
        if (measurement.SqliteCommitDuration?.SampleCount != expectedCommitSamples)
            throw new InvalidDataException("qa04.loop.sqlite-commit-metric-count-mismatch");
        if (!authority.RealSqliteCommitObservedThroughLoop)
            throw new InvalidDataException("qa04.loop.sqlite-commit-metric-without-durable-loop");

        return new Qa04InstrumentedAuthoritativeStepLoopProbeV1(authority, measurement);
    }
}
