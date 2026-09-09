using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Performance;

internal static class Qa04GcPauseSamplerInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var phase = 0;
        Qa04GcMemoryInfoSampleV1 Read(GCKind kind)
            => (phase, kind) switch
            {
                (0, GCKind.Ephemeral) => Sample(10),
                (0, GCKind.FullBlocking) => Sample(8),
                (0, GCKind.Background) => Sample(9),

                (1, GCKind.Ephemeral) => Sample(11, 1),
                (1, GCKind.FullBlocking) => Sample(8),
                (1, GCKind.Background) => Sample(9),

                (2, GCKind.Ephemeral) => Sample(13, 3),
                (2, GCKind.FullBlocking) => Sample(8),
                (2, GCKind.Background) => Sample(9),

                (_, GCKind.Ephemeral) => Sample(13, 3),
                (_, GCKind.FullBlocking) => Sample(8),
                (_, GCKind.Background) => Sample(12, 2),
                _ => throw new InvalidOperationException("unexpected GC kind"),
            };

        var collector = new Qa04BenchmarkMetricCollectorV1();
        var sampler = new Qa04GcPauseSamplerV1(collector, Read);
        sampler.BeginMeasurement();

        phase = 1;
        sampler.SampleCompletedCollections();
        var first = sampler.Snapshot();
        Require(first.PauseSampleCount == 1 && first.UnresolvedStartedGcIndexCount == 0,
            "QA-04 GC sampler must capture the first post-baseline collection without a gap.");

        phase = 2;
        sampler.SampleCompletedCollections();
        var gap = sampler.Snapshot();
        Require(gap.PauseSampleCount == 2 && gap.UnresolvedStartedGcIndexCount == 1,
            "QA-04 GC sampler must expose an unseen started-GC index instead of hiding telemetry loss.");

        phase = 3;
        sampler.SampleCompletedCollections();
        sampler.SampleCompletedCollections();
        var recovered = sampler.Snapshot();
        Require(recovered.PauseSampleCount == 3 && recovered.TelemetryComplete,
            "QA-04 GC sampler must accept out-of-order background completion after the index gap is observed.");

        var pauses = collector.Snapshot().GcPauseDuration;
        Require(pauses?.SampleCount == 3 && pauses.P99 == TimeSpan.FromMilliseconds(3),
            "QA-04 GC pause duration series must contain each completed GC exactly once.");
    }

    private static Qa04GcMemoryInfoSampleV1 Sample(long index, int pauseMilliseconds = 0)
        => new(
            index,
            pauseMilliseconds == 0
                ? Array.Empty<TimeSpan>()
                : new[] { TimeSpan.FromMilliseconds(pauseMilliseconds) });

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
