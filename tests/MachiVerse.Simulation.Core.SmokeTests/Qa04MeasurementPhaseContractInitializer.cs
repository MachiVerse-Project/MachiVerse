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
            "QA-04 State(0) must remain outside warm-up/measurement.");
        Require(Qa04MeasurementPhaseContractV1.ClassifyFinalizedStep(1) == Qa04MeasurementPhaseV1.WarmUp,
            "QA-04 first finalized Step must be warm-up.");
        Require(Qa04MeasurementPhaseContractV1.ClassifyFinalizedStep(9_000) == Qa04MeasurementPhaseV1.WarmUp,
            "QA-04 warm-up boundary drifted.");
        Require(Qa04MeasurementPhaseContractV1.ClassifyFinalizedStep(9_001) == Qa04MeasurementPhaseV1.Measurement,
            "QA-04 measurement start boundary drifted.");
        Require(Qa04MeasurementPhaseContractV1.ClassifyFinalizedStep(27_000) == Qa04MeasurementPhaseV1.Measurement,
            "QA-04 measurement end boundary drifted.");
        Require(Qa04MeasurementPhaseContractV1.ClassifyFinalizedStep(27_001) == Qa04MeasurementPhaseV1.Cooldown,
            "QA-04 cooldown boundary drifted.");

        var triggers = Qa04MeasurementPhaseContractV1.StandardSnapshotTriggersInsideMeasurement();
        Require(triggers.SequenceEqual(new[] { RunningSnapshotCoordinatorV1.StandardIntervalSteps }),
            "QA-04 measurement interval must contain exactly the standard State(18000) Snapshot trigger.");
        Require(Qa04MeasurementPhaseContractV1.ShouldCollectPerformanceSample(18_000),
            "QA-04 standard Snapshot trigger Step must remain inside measurement sampling.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
