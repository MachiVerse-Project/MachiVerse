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
        var gatewayEndpoint = AbsoluteWebSocketUri(network, "gateway-endpoint");
        var worldIdHex = CanonicalId128Hex(network, "world-id");
        var allowInsecureLoopbackAlpha = Bool(network, "allow-insecure-loopback-alpha");

        if (softDuration > maxSoftDuration)
            throw new InvalidDataException("reconcile.soft-duration-ms must be <= max-soft-duration-ms.");
        if (reconnectMax < reconnectInitial)
            throw new InvalidDataException("network.reconnect-max-ms must be >= reconnect-initial-ms.");
        if (string.Equals(gatewayEndpoint.Scheme, "ws", StringComparison.OrdinalIgnoreCase) &&
            (!allowInsecureLoopbackAlpha || !gatewayEndpoint.IsLoopback))
        {
            throw new InvalidDataException(
                "network.gateway-endpoint may use ws:// only for an explicitly enabled loopback Alpha endpoint.");
        }

        return new GeneralViewConfig(
            targetFps,
            maxPixelRatio,
            predictionEnabled,
            predictionMaxHorizonMs,
            softDuration,
            maxSoftDuration,
            reconnectInitial,
            reconnectMax,
            gatewayEndpoint,
            worldIdHex,
            allowInsecureLoopbackAlpha);
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

    private static string String(TomlTable table, string key)
        => table.TryGetValue(key, out var value) && value is string text && !string.IsNullOrWhiteSpace(text)
            ? text
            : throw new InvalidDataException($"Config field {key} must be a non-empty string.");

    private static Uri AbsoluteWebSocketUri(TomlTable table, string key)
    {
        var text = String(table, key);
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("ws" or "wss"))
            throw new InvalidDataException($"Config field {key} must be an absolute ws:// or wss:// URI.");
        return uri;
    }

    private static string CanonicalId128Hex(TomlTable table, string key)
    {
        var text = String(table, key);
        if (text.Length != 32 || text.Any(static c => c is >= 'A' and <= 'F'))
            throw new InvalidDataException($"Config field {key} must be 32 lowercase hexadecimal digits.");
        try
        {
            var bytes = Convert.FromHexString(text);
            if (bytes.AsSpan().IndexOfAnyExcept((byte)0) < 0)
                throw new InvalidDataException($"Config field {key} cannot be ZERO.");
            return text;
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException($"Config field {key} is not canonical hexadecimal.", ex);
        }
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
