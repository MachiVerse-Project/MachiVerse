using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Performance;

internal static class Qa04BenchmarkRunMeasurementSessionInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var collector = new Qa04BenchmarkMetricCollectorV1();
        var session = new Qa04BenchmarkRunMeasurementSessionV1(
            collector,
            workingSetBytes: static () => 1_234);

        for (ulong step = 1; step <= Qa04MeasurementPhaseContractV1.WarmUpLastFinalizedStep; step++)
        {
            session.ExecuteFinalizingStepAsync(step, static _ => Task.CompletedTask)
                .GetAwaiter()
                .GetResult();
        }

        var warmup = session.Snapshot();
        Require(warmup.StepSampleCount == 0,
            "QA-04 warm-up Steps must not enter performance samples.");
        Require(warmup.CoreWorkingSetSampleCount == 0,
            "QA-04 warm-up Steps must not enter working-set samples.");

        session.ExecuteFinalizingStepAsync(
                Qa04MeasurementPhaseContractV1.MeasurementFirstFinalizedStep,
                static _ => Task.CompletedTask)
            .GetAwaiter()
            .GetResult();

        var measured = session.Snapshot();
        Require(measured.StepSampleCount == 1 && measured.StepDuration?.SampleCount == 1,
            "QA-04 first measurement Step must produce exactly one wall-time sample.");
        Require(measured.CoreWorkingSetSampleCount == 1 && measured.MaxCoreWorkingSetBytes == 1_234,
            "QA-04 measurement Step must sample Core working set after successful finalization.");
        Require(session.LastFinalizedStep == Qa04MeasurementPhaseContractV1.MeasurementFirstFinalizedStep,
            "QA-04 measurement session finalized-Step cursor drifted.");

        RequireThrows<InvalidDataException>(() =>
            session.ExecuteFinalizingStepAsync(
                    Qa04MeasurementPhaseContractV1.MeasurementFirstFinalizedStep + 2,
                    static _ => Task.CompletedTask)
                .GetAwaiter()
                .GetResult(),
            "QA-04 measurement session must reject skipped finalized Steps.");

        var cowCollector = new Qa04BenchmarkMetricCollectorV1();
        var marker = new object();
        var materialized = Qa04RunningSnapshotMeasurementExtensionsV1.MeasureSnapshotCowBarrierAsync(
                cowCollector,
                _ => Task.FromResult<object?>(marker))
            .GetAwaiter()
            .GetResult();
        Require(ReferenceEquals(materialized, marker),
            "QA-04 COW timer must return the production materialization result unchanged.");

        var notDue = Qa04RunningSnapshotMeasurementExtensionsV1.MeasureSnapshotCowBarrierAsync<object>(
                cowCollector,
                _ => Task.FromResult<object?>(null))
            .GetAwaiter()
            .GetResult();
        Require(notDue is null,
            "QA-04 COW timer null path must remain null.");

        RequireThrows<InvalidOperationException>(() =>
            Qa04RunningSnapshotMeasurementExtensionsV1.MeasureSnapshotCowBarrierAsync<object>(
                    cowCollector,
                    _ => Task.FromException<object?>(new InvalidOperationException("expected")))
                .GetAwaiter()
                .GetResult(),
            "QA-04 COW timer must propagate freeze failures.");

        var cow = cowCollector.Snapshot().SnapshotCowBarrierDuration;
        Require(cow?.SampleCount == 1 && cow.Minimum >= TimeSpan.Zero,
            "QA-04 COW timer must record only successful non-null freeze materialization.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void RequireThrows<T>(Action action, string message)
        where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }
}
