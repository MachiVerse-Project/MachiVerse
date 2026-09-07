using Tomlyn;
using Tomlyn.Model;

namespace MachiVerse.Administration.View.Configuration;

public static class AdminViewConfigLoader
{
    public static AdminViewConfig LoadText(string text)
    {
        var model = TomlSerializer.Deserialize<TomlTable>(text)
            ?? throw new InvalidDataException("Config TOML could not be deserialized.");
        var meta = Table(model, "meta");
        RequireString(meta, "format", "machiverse-config");
        RequireString(meta, "schema_version", "1.0");
        RequireString(meta, "component", "admin-view");

        var dashboard = Table(model, "dashboard");
        var metrics = Table(model, "metrics");
        var log = Table(model, "log");
        var audit = Table(model, "audit");
        var request = Table(model, "request");
        var confirmation = Table(model, "confirmation");
        var network = Table(model, "network");

        var reconnectInitial = PositiveInt(network, "reconnect-initial-ms");
        var reconnectMax = PositiveInt(network, "reconnect-max-ms");
        if (reconnectMax < reconnectInitial)
            throw new InvalidDataException("network.reconnect-max-ms must be >= reconnect-initial-ms.");

        return new AdminViewConfig(
            PositiveInt(dashboard, "refresh-ms"),
            PositiveInt(metrics, "local-history-samples"),
            PositiveInt(metrics, "max-series"),
            PositiveInt(log, "default-page-size"),
            PositiveInt(audit, "default-page-size"),
            PositiveInt(request, "presentation-timeout-ms"),
            PositiveInt(confirmation, "ux-timeout-seconds"),
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
        if (!table.TryGetValue(key, out var value) || value is not long number || number is <= 0 or > int.MaxValue)
            throw new InvalidDataException($"Config field {key} must be a positive int32.");
        return (int)number;
    }
}
