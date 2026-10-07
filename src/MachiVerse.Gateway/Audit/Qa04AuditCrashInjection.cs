namespace MachiVerse.Gateway.Audit;

/// <summary>
/// QA-04 release-only audit crash seam. It is inert unless explicitly armed in a dedicated
/// component process. World/runtime behavior must never depend on these environment variables.
/// </summary>
internal static class Qa04AuditCrashInjectionV1
{
    internal static void Hit(string point)
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("MACHIVERSE_QA04_PERSISTENCE_CRASH_ARMED"),
                "1",
                StringComparison.Ordinal))
            return;
        if (!string.Equals(
                Environment.GetEnvironmentVariable("MACHIVERSE_QA04_PERSISTENCE_CRASH_STAGE"),
                "audit-append",
                StringComparison.Ordinal))
            return;
        if (!string.Equals(
                Environment.GetEnvironmentVariable("MACHIVERSE_QA04_PERSISTENCE_CRASH_POINT"),
                point,
                StringComparison.Ordinal))
            return;

        Environment.FailFast($"qa04.persistence.crash:audit-append:{point}");
    }
}
