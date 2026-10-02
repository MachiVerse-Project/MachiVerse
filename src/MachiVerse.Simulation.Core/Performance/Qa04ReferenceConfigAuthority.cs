using MachiVerse.Simulation.Core.Configuration;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Reconstructable Simulation Core Config authority for QA-04 perf.reference.v1.
/// The benchmark profile uses the standard Core defaults, varies worker-count only across
/// the normative {1,4,8,16} test parameter, and fixes observability log-level to warn.
/// </summary>
public static class Qa04ReferenceConfigAuthorityV1
{
    private static readonly int[] AllowedWorkerCounts = [1, 4, 8, 16];

    public const int CanonicalGateWorkerCount = 4;

    public static EffectiveCoreConfig CreateCanonical(int workerCount = CanonicalGateWorkerCount)
    {
        if (!AllowedWorkerCounts.Contains(workerCount))
            throw new ArgumentOutOfRangeException(nameof(workerCount), "QA-04 worker-count must be one of 1, 4, 8, or 16.");

        var toml = $"""
[meta]
format = "machiverse-config"
schema_version = "1.0"
component = "simulation-core"

[runtime]
worker-count = {workerCount}

[observability]
log-level = "warn"
""";
        var config = new CoreConfigCoordinator().LoadStartup(toml);
        if (config.Generation != 1 || config.Get<long>("runtime.worker-count") != workerCount ||
            !string.Equals(config.Get<string>("observability.log-level"), "warn", StringComparison.Ordinal))
            throw new InvalidDataException("qa04.config.canonical-authority-drift");
        return config;
    }
}
