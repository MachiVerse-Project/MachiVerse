namespace MachiVerse.Simulation.Core.Configuration;

public enum ConfigImpact
{
    Simulation,
    Operational,
    Presentation
}

public enum ConfigMutability
{
    RuntimeSafe,
    RestartRequired,
    WorldRegenerationRequired
}

public sealed record ConfigFieldSpec(
    object DefaultValue,
    ConfigImpact Impact,
    ConfigMutability Mutability,
    Func<object, bool> Validate);

public static class CoreConfigSchema
{
    public const string SchemaVersion = "1.0";
    public const string Component = "simulation-core";

    public static IReadOnlyDictionary<string, ConfigFieldSpec> Fields { get; } = Build();

    private static Dictionary<string, ConfigFieldSpec> Build()
    {
        var fields = new Dictionary<string, ConfigFieldSpec>(StringComparer.Ordinal);

        void UInt(string path, long value, long min, long max, ConfigImpact impact = ConfigImpact.Operational, ConfigMutability mutability = ConfigMutability.RuntimeSafe)
            => fields.Add(path, new ConfigFieldSpec(value, impact, mutability, v => v is long n && n >= min && n <= max));
        void Int(string path, long value, long min, long max, ConfigImpact impact = ConfigImpact.Operational, ConfigMutability mutability = ConfigMutability.RuntimeSafe)
            => fields.Add(path, new ConfigFieldSpec(value, impact, mutability, v => v is long n && n >= min && n <= max));
        void Bool(string path, bool value, ConfigImpact impact = ConfigImpact.Operational, ConfigMutability mutability = ConfigMutability.RuntimeSafe)
            => fields.Add(path, new ConfigFieldSpec(value, impact, mutability, v => v is bool));
        void Enum(string path, string value, string[] allowed, ConfigImpact impact = ConfigImpact.Simulation, ConfigMutability mutability = ConfigMutability.RuntimeSafe)
            => fields.Add(path, new ConfigFieldSpec(value, impact, mutability, v => v is string s && allowed.Contains(s, StringComparer.Ordinal)));

        UInt("simulation.step-rate.numerator", 30, 1, 240, ConfigImpact.Simulation);
        UInt("simulation.step-rate.denominator", 1, 1, 1000, ConfigImpact.Simulation);
        UInt("runtime.worker-count", 4, 1, 16);
        UInt("runtime.domain-timeout-ms", 30000, 100, 300000);

        UInt("scheduling.min-lead-steps", 2, 0, 300, ConfigImpact.Simulation);
        UInt("scheduling.default-deadline-window-steps", 90, 1, 36000, ConfigImpact.Simulation);
        UInt("scheduling.grace-steps", 15, 0, 3600, ConfigImpact.Simulation);
        Enum("scheduling.late-policy", "defer-within-grace", ["reject", "defer-within-grace"]);

        UInt("detail.promotion-hysteresis-steps", 30, 0, 36000, ConfigImpact.Simulation);
        UInt("detail.demotion-quiet-steps", 300, 0, 360000, ConfigImpact.Simulation);
        UInt("detail.minimum-residence-steps", 300, 0, 360000, ConfigImpact.Simulation);
        Enum("detail.bound-resident-floor", "d0-entity", ["d0-entity", "d1-local-aggregate"]);
        Enum("detail.active-transaction-floor", "d0-entity", ["d0-entity", "d1-local-aggregate"]);
        UInt("detail.promotion-max-regions-per-step", 4, 1, 1024, ConfigImpact.Simulation);
        UInt("detail.promotion-max-records-per-step", 20000, 100, 10000000, ConfigImpact.Simulation);
        UInt("detail.demotion-max-regions-per-step", 8, 1, 2048, ConfigImpact.Simulation);
        UInt("detail.demotion-max-records-per-step", 50000, 100, 20000000, ConfigImpact.Simulation);

        AddCadence(fields, "spatial", 1, 10, 60, 600);
        AddCadence(fields, "environment", 1, 5, 30, 300);
        AddCadence(fields, "physical_built", 1, 5, 30, 300);
        AddCadence(fields, "participation", 1, 1, 5, 30);
        AddCadence(fields, "resident", 1, 5, 30, 300);
        AddCadence(fields, "society_economy", 5, 30, 300, 1800);
        AddCadence(fields, "governance_security", 10, 60, 600, 3600);
        AddCadence(fields, "infrastructure_information", 1, 5, 30, 300);

        UInt("persistence.snapshot-interval-steps", 18000, 30, 100000000);
        UInt("persistence.snapshot-retain-count", 12, 2, 1024);
        Enum("persistence.snapshot-compression", "zstd", ["none", "zstd"], ConfigImpact.Operational);
        Int("persistence.snapshot-zstd-level", 3, -5, 19);
        Bool("persistence.recovery-verify-state-digest", true, ConfigImpact.Operational, ConfigMutability.RestartRequired);

        Bool("publication.delta-enabled", true);
        UInt("publication.full-interval-steps", 900, 1, 360000);
        UInt("publication.max-chunk-bytes", 1048576, 16384, 1048576);
        UInt("publication.queue-capacity", 64, 4, 4096);

        UInt("master.heartbeat-interval-ms", 1000, 100, 60000);
        UInt("master.heartbeat-timeout-ms", 5000, 500, 300000);
        UInt("master.min-ready-heartbeats", 2, 1, 20);

        UInt("queue.protocol-ingress-capacity", 8192, 256, 1048576);
        UInt("queue.accepted-operation-admission-limit", 65536, 1024, 16777216);
        UInt("queue.persistence-capacity", 8192, 256, 1048576);

        Enum("observability.log-level", "info", ["trace", "debug", "info", "warn", "error"], ConfigImpact.Operational);
        UInt("observability.metric-export-interval-ms", 1000, 100, 60000);
        UInt("observability.state-digest-every-steps", 1, 1, 10000);

        return fields;
    }

    private static void AddCadence(Dictionary<string, ConfigFieldSpec> fields, string domain, long d0, long d1, long d2, long d3)
    {
        foreach (var item in new[] { ("d0", d0), ("d1", d1), ("d2", d2), ("d3", d3) })
        {
            var path = $"detail.domain.{domain}.{item.Item1}-cadence-steps";
            fields.Add(path, new ConfigFieldSpec(item.Item2, ConfigImpact.Simulation, ConfigMutability.RuntimeSafe, v => v is long n && n is >= 1 and <= 360000));
        }
    }
}
