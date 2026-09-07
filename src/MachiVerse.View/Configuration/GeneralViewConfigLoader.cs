using Tomlyn;
using Tomlyn.Model;

namespace MachiVerse.View.Configuration;

public static class GeneralViewConfigLoader
{
    public static GeneralViewConfig LoadText(string text)
    {
        var model = TomlSerializer.Deserialize<TomlTable>(text)
            ?? throw new InvalidDataException("Config TOML could not be deserialized.");
        var meta = Table(model, "meta");
        RequireString(meta, "format", "machiverse-config");
        RequireString(meta, "schema_version", "1.0");
        RequireString(meta, "component", "general-view");

        var render = Table(model, "render");
        var prediction = Table(model, "prediction");
        var reconcile = Table(model, "reconcile");
        var network = Table(model, "network");

        var targetFps = PositiveInt(render, "target-fps");
        var maxPixelRatio = PositiveDouble(render, "max-pixel-ratio");
        var predictionEnabled = Bool(prediction, "enabled");
        var predictionMaxHorizonMs = NonNegativeInt(prediction, "max-horizon-ms");
        var softDuration = NonNegativeInt(reconcile, "soft-duration-ms");
        var maxSoftDuration = NonNegativeInt(reconcile, "max-soft-duration-ms");
        var reconnectInitial = PositiveInt(network, "reconnect-initial-ms");
        var reconnectMax = PositiveInt(network, "reconnect-max-ms");

        if (softDuration > maxSoftDuration)
            throw new InvalidDataException("reconcile.soft-duration-ms must be <= max-soft-duration-ms.");
        if (reconnectMax < reconnectInitial)
            throw new InvalidDataException("network.reconnect-max-ms must be >= reconnect-initial-ms.");

        return new GeneralViewConfig(
            targetFps,
            maxPixelRatio,
            predictionEnabled,
            predictionMaxHorizonMs,
            softDuration,
            maxSoftDuration,
            reconnectInitial,
            reconnectMax);
    }

    private static TomlTable Table(TomlTable parent, string key)
        => parent.TryGetValue(key, out var value) && value is TomlTable table
            ? table
            : throw new InvalidDataException($"Missing TOML table [{key}].");

    private static void RequireString(TomlTable table, string key, string expected)
    {
        if (!table.TryGetValue(key, out var value) || !string.Equals(value as string, expected, StringComparison.Ordinal))
            throw new InvalidDataException($"Invalid config meta field: {key}.");
    }

    private static int PositiveInt(TomlTable table, string key)
    {
        var value = NonNegativeInt(table, key);
        if (value == 0) throw new InvalidDataException($"Config field {key} must be positive.");
        return value;
    }

    private static int NonNegativeInt(TomlTable table, string key)
    {
        if (!table.TryGetValue(key, out var value) || value is not long number || number is < 0 or > int.MaxValue)
            throw new InvalidDataException($"Config field {key} must be a non-negative int32.");
        return (int)number;
    }

    private static double PositiveDouble(TomlTable table, string key)
    {
        if (!table.TryGetValue(key, out var value)) throw new InvalidDataException($"Missing config field {key}.");
        var number = value switch
        {
            double d => d,
            long l => l,
            _ => throw new InvalidDataException($"Config field {key} must be numeric.")
        };
        if (!double.IsFinite(number) || number <= 0) throw new InvalidDataException($"Config field {key} must be a positive finite number.");
        return number;
    }

    private static bool Bool(TomlTable table, string key)
        => table.TryGetValue(key, out var value) && value is bool boolean
            ? boolean
            : throw new InvalidDataException($"Config field {key} must be boolean.");
}
