namespace MachiVerse.Simulation.Core.Persistence;

/// <summary>
/// QA-04 release-only crash injection seam. The normal runtime never arms this path.
/// A case process is expected to terminate abruptly so the parent process can reopen durable state
/// and prove the transaction boundary. This seam must never be driven by world-semantic input.
/// </summary>
internal static class Qa04PersistenceCrashInjectionV1
{
    internal const string ArmedVariable = "MACHIVERSE_QA04_PERSISTENCE_CRASH_ARMED";
    internal const string StageVariable = "MACHIVERSE_QA04_PERSISTENCE_CRASH_STAGE";
    internal const string PointVariable = "MACHIVERSE_QA04_PERSISTENCE_CRASH_POINT";

    internal static void Hit(string stage, string point)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(ArmedVariable), "1", StringComparison.Ordinal))
            return;
        if (!string.Equals(Environment.GetEnvironmentVariable(StageVariable), stage, StringComparison.Ordinal))
            return;
        if (!string.Equals(Environment.GetEnvironmentVariable(PointVariable), point, StringComparison.Ordinal))
            return;

        Environment.FailFast($"qa04.persistence.crash:{stage}:{point}");
    }
}
