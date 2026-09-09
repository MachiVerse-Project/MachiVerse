using MachiVerse.Simulation.Core.Persistence;

namespace MachiVerse.Simulation.Core.Performance;

public enum Qa04MeasurementPhaseV1 : byte
{
    Initialization = 0,
    WarmUp = 1,
    Measurement = 2,
    Cooldown = 3,
}

/// <summary>
/// Harness-only finalized-Step phase convention for one perf.reference.v1 process run. Genesis is
/// State(0); 9,000 finalized Steps are warm-up, the following 18,000 finalized Steps are measured,
/// and later work is cooldown/snapshot drain. Phase classification never affects world semantics.
/// </summary>
public static class Qa04MeasurementPhaseContractV1
{
    public const ulong GenesisStep = 0;
    public const ulong WarmUpStepCount = 9_000;
    public const ulong MeasurementStepCount = 18_000;
    public const ulong WarmUpFirstFinalizedStep = 1;
    public const ulong WarmUpLastFinalizedStep = WarmUpStepCount;
    public const ulong MeasurementFirstFinalizedStep = WarmUpLastFinalizedStep + 1;
    public const ulong MeasurementLastFinalizedStep = WarmUpStepCount + MeasurementStepCount;

    public static Qa04MeasurementPhaseV1 ClassifyFinalizedStep(ulong finalizedStep)
        => finalizedStep switch
        {
            GenesisStep => Qa04MeasurementPhaseV1.Initialization,
            <= WarmUpLastFinalizedStep => Qa04MeasurementPhaseV1.WarmUp,
            <= MeasurementLastFinalizedStep => Qa04MeasurementPhaseV1.Measurement,
            _ => Qa04MeasurementPhaseV1.Cooldown,
        };

    public static bool ShouldCollectPerformanceSample(ulong finalizedStep)
        => ClassifyFinalizedStep(finalizedStep) == Qa04MeasurementPhaseV1.Measurement;

    public static IReadOnlyList<ulong> StandardSnapshotTriggersInsideMeasurement()
    {
        var triggers = new List<ulong>();
        var interval = RunningSnapshotCoordinatorV1.StandardIntervalSteps;
        var firstMultiple = checked(((MeasurementFirstFinalizedStep + interval - 1) / interval) * interval);
        for (var step = firstMultiple; step <= MeasurementLastFinalizedStep; step = checked(step + interval))
            triggers.Add(step);
        return Array.AsReadOnly(triggers.ToArray());
    }

    public static void ValidateCanonicalContract()
    {
        if (WarmUpStepCount != Qa04ReferenceLoadV1.WarmupSteps ||
            MeasurementStepCount != Qa04ReferenceLoadV1.MeasurementSteps)
            throw new InvalidDataException("qa04.measurement.phase-count-drift");
        if (MeasurementLastFinalizedStep != 27_000)
            throw new InvalidDataException("qa04.measurement.final-step-drift");

        var snapshotTriggers = StandardSnapshotTriggersInsideMeasurement();
        if (snapshotTriggers.Count != 1 || snapshotTriggers[0] != RunningSnapshotCoordinatorV1.StandardIntervalSteps)
            throw new InvalidDataException("qa04.measurement.standard-snapshot-trigger-drift");
    }
}
