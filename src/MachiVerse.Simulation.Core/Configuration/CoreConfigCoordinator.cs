using System.Security.Cryptography;
using System.Text;
using MachiVerse.Simulation.Core.Determinism;
using Tomlyn;
using Tomlyn.Model;

namespace MachiVerse.Simulation.Core.Configuration;

public sealed record EffectiveCoreConfig(
    ulong Generation,
    IReadOnlyDictionary<string, object> Fields,
    byte[] Digest,
    string NormalizedToml)
{
    public string DigestHex => Convert.ToHexStringLower(Digest);
    public T Get<T>(string path) => Fields.TryGetValue(path, out var value) && value is T typed
        ? typed
        : throw new KeyNotFoundException(path);
}

public sealed record ConfigChange(string Path, object Value);
public sealed record ConfigChangeSet(ulong ExpectedBaseGeneration, IReadOnlyList<ConfigChange> Changes, ulong? EffectiveStep);
public sealed record ValidatedConfigChange(
    EffectiveCoreConfig Candidate,
    bool IsNoChange,
    bool ContainsSimulationImpact,
    ulong? EffectiveStep);

public sealed class CoreConfigCoordinator
{
    private static readonly HashSet<string> AllowedMetaFields =
    [
        "meta.format",
        "meta.schema_version",
        "meta.component"
    ];

    private EffectiveCoreConfig? _current;

    public EffectiveCoreConfig Current => _current ?? throw new InvalidOperationException("Core Config has not been initialized.");

    public EffectiveCoreConfig LoadStartup(string text)
    {
        var fields = ParseAndComplete(text);
        var normalized = Normalize(fields);
        var digest = ComputeDigest(fields);
        _current = new EffectiveCoreConfig(1, fields, digest, normalized);
        return _current;
    }

    public ValidatedConfigChange ValidateRuntimeChange(ConfigChangeSet changeSet, ulong minimumNextApplicableStep)
    {
        var current = Current;
        if (changeSet.ExpectedBaseGeneration != current.Generation)
            throw new InvalidDataException("config.stale-generation");
        if (changeSet.Changes.Count == 0)
            return new ValidatedConfigChange(current, true, false, changeSet.EffectiveStep);
        if (changeSet.Changes.Select(static x => x.Path).Distinct(StringComparer.Ordinal).Count() != changeSet.Changes.Count)
            throw new InvalidDataException("config.duplicate-change-path");

        var candidate = new Dictionary<string, object>(current.Fields, StringComparer.Ordinal);
        var containsSimulation = false;
        foreach (var change in changeSet.Changes.OrderBy(static x => x.Path, StringComparer.Ordinal))
        {
            if (!CoreConfigSchema.Fields.TryGetValue(change.Path, out var spec))
                throw new InvalidDataException($"config.unknown-field:{change.Path}");
            if (spec.Mutability != ConfigMutability.RuntimeSafe)
                throw new InvalidDataException($"config.restart-required:{change.Path}");
            if (!spec.Validate(change.Value))
                throw new InvalidDataException($"config.invalid-value:{change.Path}");
            containsSimulation |= spec.Impact == ConfigImpact.Simulation;
            candidate[change.Path] = change.Value;
        }

        ValidateCrossFields(candidate);
        if (containsSimulation && changeSet.EffectiveStep is null)
            throw new InvalidDataException("config.effective-step-required");
        if (containsSimulation && changeSet.EffectiveStep < minimumNextApplicableStep)
            throw new InvalidDataException("config.effective-step-too-early");

        var digest = ComputeDigest(candidate);
        var noChange = CryptographicOperations.FixedTimeEquals(digest, current.Digest);
        if (noChange)
            return new ValidatedConfigChange(current, true, containsSimulation, changeSet.EffectiveStep);
        if (current.Generation == ulong.MaxValue) throw new OverflowException("ConfigGeneration cannot wrap.");

        return new ValidatedConfigChange(
            new EffectiveCoreConfig(current.Generation + 1, candidate, digest, Normalize(candidate)),
            false,
            containsSimulation,
            changeSet.EffectiveStep);
    }

    public EffectiveCoreConfig ApplyAtBoundary(ValidatedConfigChange validated)
    {
        ArgumentNullException.ThrowIfNull(validated);
        if (validated.ContainsSimulationImpact)
            throw new InvalidDataException("config.simulation-boundary-step-required");
        return ApplyValidated(validated);
    }

    public EffectiveCoreConfig ApplyAtBoundary(ValidatedConfigChange validated, ulong basisStep)
    {
        ArgumentNullException.ThrowIfNull(validated);
        if (validated.ContainsSimulationImpact)
        {
            if (validated.EffectiveStep is null)
                throw new InvalidDataException("config.effective-step-required");
            if (validated.EffectiveStep.Value != basisStep)
                throw new InvalidDataException("config.effective-step-boundary-mismatch");
        }
        return ApplyValidated(validated);
    }

    private EffectiveCoreConfig ApplyValidated(ValidatedConfigChange validated)
    {
        var current = Current;
        if (validated.IsNoChange) return current;
        if (current.Generation == ulong.MaxValue || validated.Candidate.Generation != current.Generation + 1)
            throw new InvalidDataException("config.stale-generation");
        _current = validated.Candidate;
        return _current;
    }

    private static Dictionary<string, object> ParseAndComplete(string text)
    {
        TomlTable root;
        try
        {
            root = TomlSerializer.Deserialize<TomlTable>(text)
                ?? throw new InvalidDataException("Config TOML could not be deserialized.");
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            throw new InvalidDataException("config.parse-failed", ex);
        }

        var flat = new Dictionary<string, object>(StringComparer.Ordinal);
        Flatten(root, null, flat);
        RequireMeta(flat, "meta.format", "machiverse-config");
        RequireMeta(flat, "meta.schema_version", CoreConfigSchema.SchemaVersion);
        RequireMeta(flat, "meta.component", CoreConfigSchema.Component);

        foreach (var key in flat.Keys)
        {
            if (key.StartsWith("meta.", StringComparison.Ordinal))
            {
                if (!AllowedMetaFields.Contains(key))
                    throw new InvalidDataException($"config.unknown-field:{key}");
                continue;
            }

            if (!CoreConfigSchema.Fields.ContainsKey(key))
                throw new InvalidDataException($"config.unknown-field:{key}");
        }

        var fields = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (path, spec) in CoreConfigSchema.Fields)
        {
            var value = flat.TryGetValue(path, out var parsed) ? parsed : spec.DefaultValue;
            if (!spec.Validate(value)) throw new InvalidDataException($"config.invalid-value:{path}");
            fields[path] = value;
        }

        NormalizeStepRate(fields);
        ValidateCrossFields(fields);
        return fields;
    }

    private static void Flatten(TomlTable table, string? prefix, Dictionary<string, object> output)
    {
        foreach (var (key, value) in table)
        {
            var path = prefix is null ? key : $"{prefix}.{key}";
            if (value is TomlTable child) Flatten(child, path, output);
            else output[path] = value ?? throw new InvalidDataException($"config.null-value:{path}");
        }
    }

    private static void RequireMeta(Dictionary<string, object> flat, string path, string expected)
    {
        if (!flat.TryGetValue(path, out var value) || value is not string text || !string.Equals(text, expected, StringComparison.Ordinal))
            throw new InvalidDataException($"config.invalid-meta:{path}");
    }

    private static void NormalizeStepRate(Dictionary<string, object> fields)
    {
        var numerator = (long)fields["simulation.step-rate.numerator"];
        var denominator = (long)fields["simulation.step-rate.denominator"];
        var gcd = GreatestCommonDivisor(numerator, denominator);
        fields["simulation.step-rate.numerator"] = numerator / gcd;
        fields["simulation.step-rate.denominator"] = denominator / gcd;
    }

    private static long GreatestCommonDivisor(long a, long b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return Math.Abs(a);
    }

    private static void ValidateCrossFields(IReadOnlyDictionary<string, object> fields)
    {
        var numerator = (long)fields["simulation.step-rate.numerator"];
        var denominator = (long)fields["simulation.step-rate.denominator"];
        if (numerator * 10 < denominator || numerator > 240 * denominator)
            throw new InvalidDataException("config.step-rate-out-of-effective-range");

        foreach (var domain in new[] { "spatial", "environment", "physical_built", "participation", "resident", "society_economy", "governance_security", "infrastructure_information" })
        {
            var d0 = (long)fields[$"detail.domain.{domain}.d0-cadence-steps"];
            var d1 = (long)fields[$"detail.domain.{domain}.d1-cadence-steps"];
            var d2 = (long)fields[$"detail.domain.{domain}.d2-cadence-steps"];
            var d3 = (long)fields[$"detail.domain.{domain}.d3-cadence-steps"];
            if (!(d0 <= d1 && d1 <= d2 && d2 <= d3)) throw new InvalidDataException($"config.invalid-detail-cadence:{domain}");
        }

        var interval = (long)fields["master.heartbeat-interval-ms"];
        var timeout = (long)fields["master.heartbeat-timeout-ms"];
        if (timeout < checked(3 * interval)) throw new InvalidDataException("config.invalid-master-heartbeat");
    }

    private static string Normalize(IReadOnlyDictionary<string, object> fields)
    {
        var all = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["meta.component"] = CoreConfigSchema.Component,
            ["meta.format"] = "machiverse-config",
            ["meta.schema_version"] = CoreConfigSchema.SchemaVersion
        };
        foreach (var pair in fields) all[pair.Key] = pair.Value;

        var builder = new StringBuilder();
        foreach (var (path, value) in all)
        {
            builder.Append(path).Append(" = ").Append(FormatToml(value)).Append('\n');
        }
        return builder.ToString();
    }

    private static string FormatToml(object value) => value switch
    {
        bool b => b ? "true" : "false",
        long n => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
        string s => $"\"{s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"",
        _ => throw new InvalidDataException($"Unsupported normalized Config value type: {value.GetType().Name}")
    };

    private static byte[] ComputeDigest(IReadOnlyDictionary<string, object> fields)
        => HashSuite.DomainHash("mv.config.v1", writer =>
        {
            writer.WriteArrayStart(3);
            writer.WriteAsciiText(CoreConfigSchema.SchemaVersion);
            writer.WriteAsciiText(CoreConfigSchema.Component);
            writer.WriteArrayStart((ulong)fields.Count);
            foreach (var (path, value) in fields.OrderBy(static x => x.Key, StringComparer.Ordinal))
            {
                writer.WriteArrayStart(2);
                writer.WriteAsciiText(path);
                switch (value)
                {
                    case long n: writer.WriteInt64(n); break;
                    case bool b: writer.WriteBoolean(b); break;
                    case string s: writer.WriteAsciiText(s); break;
                    default: throw new InvalidDataException($"Unsupported Config digest value type: {value.GetType().Name}");
                }
            }
        });
}
