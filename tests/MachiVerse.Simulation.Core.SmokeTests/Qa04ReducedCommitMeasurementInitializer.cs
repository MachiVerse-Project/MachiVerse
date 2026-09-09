using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Performance;

internal static class Qa04ReducedCommitMeasurementInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var root = Path.Combine(Path.GetTempPath(), "machiverse-qa04-reduced-commit-metric-" + Guid.NewGuid().ToString("N"));
        try
        {
            var proof = Qa04InstrumentedAuthoritativeStepLoopBridgeV1.RunReducedAsync(
                    workerCount: 1,
                    residentRecordCount: 1,
                    persistenceRoot: root)
                .GetAwaiter()
                .GetResult();

            Require(proof.AuthorityProbe.StepCount == Qa04AuthoritativeStepLoopBridgeV1.StandardReducedStepCount,
                "Instrumented QA-04 reduced loop Step count drifted.");
            Require(proof.SqliteCommitSampleCount == checked((int)Qa04AuthoritativeStepLoopBridgeV1.StandardReducedStepCount),
                "Instrumented QA-04 reduced loop must observe exactly one SQLite COMMIT sample per Step.");
            Require(proof.EveryReducedStepHasSqliteCommitSample,
                "Instrumented QA-04 reduced loop COMMIT metric coverage is incomplete.");
            Require(proof.Measurement.SqliteCommitDuration is { Minimum: >= TimeSpan.Zero },
                "Instrumented QA-04 reduced loop must expose a non-negative COMMIT duration summary.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
