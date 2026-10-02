using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;

internal static class Qa04MeasurementPhaseContractInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        Qa04MeasurementPhaseContractV1.ValidateCanonicalContract();

        Require(Qa04MeasurementPhaseContractV1.ClassifyFinalizedStep(0) == Qa04MeasurementPhaseV1.Initialization,
            "QA-04 State(0) must remain persistence genesis initialization.");
        Require(Qa04MeasurementPhaseContractV1.ClassifyFinalizedStep(1) == Qa04MeasurementPhaseV1.Initialization,
            "QA-04 State(1) must remain the production initialization basis and outside timing.");
        Require(Qa04MeasurementPhaseContractV1.ClassifyFinalizedStep(2) == Qa04MeasurementPhaseV1.WarmUp,
            "QA-04 first workload result State must be warm-up.");
        Require(Qa04MeasurementPhaseContractV1.ClassifyFinalizedStep(9_001) == Qa04MeasurementPhaseV1.WarmUp,
            "QA-04 warm-up end boundary drifted.");
        Require(Qa04MeasurementPhaseContractV1.ClassifyFinalizedStep(9_002) == Qa04MeasurementPhaseV1.Measurement,
            "QA-04 measurement start boundary drifted.");
        Require(Qa04MeasurementPhaseContractV1.ClassifyFinalizedStep(27_001) == Qa04MeasurementPhaseV1.Measurement,
            "QA-04 measurement end boundary drifted.");
        Require(Qa04MeasurementPhaseContractV1.ClassifyFinalizedStep(27_002) == Qa04MeasurementPhaseV1.Cooldown,
            "QA-04 cooldown boundary drifted.");

        Require(Qa04MeasurementPhaseContractV1.WarmUpLastFinalizedStep -
                Qa04MeasurementPhaseContractV1.WarmUpFirstFinalizedStep + 1 ==
                Qa04ReferenceLoadV1.WarmupSteps,
            "QA-04 warm-up finalized-State count drifted.");
        Require(Qa04MeasurementPhaseContractV1.MeasurementLastFinalizedStep -
                Qa04MeasurementPhaseContractV1.MeasurementFirstFinalizedStep + 1 ==
                Qa04ReferenceLoadV1.MeasurementSteps,
            "QA-04 measurement finalized-State count drifted.");

        var triggers = Qa04MeasurementPhaseContractV1.StandardSnapshotTriggersInsideMeasurement();
        Require(triggers.SequenceEqual(new[] { RunningSnapshotCoordinatorV1.StandardIntervalSteps }),
            "QA-04 measurement interval must contain exactly the standard State(18000) Snapshot trigger.");
        Require(Qa04MeasurementPhaseContractV1.ShouldCollectPerformanceSample(18_000),
            "QA-04 standard Snapshot trigger State must remain inside measurement sampling.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
