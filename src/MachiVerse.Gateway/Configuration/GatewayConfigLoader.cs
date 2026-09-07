using System.Text;
using Tomlyn;
using Tomlyn.Model;

namespace MachiVerse.Gateway.Configuration;

public static class GatewayConfigLoader
{
    public static GatewayConfig LoadFile(string path) => LoadText(File.ReadAllText(path));

    public static GatewayConfig LoadText(string text)
    {
        var model = TomlSerializer.Deserialize<TomlTable>(text)
            ?? throw new InvalidDataException("Config TOML could not be deserialized.");
        var meta = Table(model, "meta");
        RequireString(meta, "format", "machiverse-config");
        RequireString(meta, "schema_version", "1.0");
        RequireString(meta, "component", "gateway");

        var network = Table(model, "network");
        var peer = Table(model, "peer");
        var auth = Table(model, "auth");
        var oidc = Table(auth, "oidc");

        var connect = PositiveInt(network, "connect-timeout-ms");
        var reconnectInitial = PositiveInt(network, "reconnect-initial-ms");
        var reconnectMax = PositiveInt(network, "reconnect-max-ms");
        var heartbeatInterval = PositiveInt(peer, "heartbeat-interval-ms");
        var heartbeatTimeout = PositiveInt(peer, "heartbeat-timeout-ms");
        var idle = IntInRange(auth, "session-idle-lifetime-seconds", 300, 86400);
        var absolute = IntInRange(auth, "session-absolute-lifetime-seconds", 900, 604800);
        var loginLifetime = IntInRange(auth, "login-transaction-lifetime-seconds", 60, 1800);
        var maxSessions = IntInRange(auth, "max-active-sessions-per-account", 1, 1024);

        if (reconnectMax < reconnectInitial) throw new InvalidDataException("network.reconnect-max-ms must be >= reconnect-initial-ms.");
        if ((long)heartbeatTimeout < (long)heartbeatInterval * 3) throw new InvalidDataException("peer.heartbeat-timeout-ms must be >= 3 * heartbeat-interval-ms.");
        if (absolute < idle) throw new InvalidDataException("auth.session-absolute-lifetime-seconds must be >= session-idle-lifetime-seconds.");

        var issuer = AbsoluteHttpsUri(oidc, "issuer", allowPath: true, allowQuery: false, allowFragment: false);
        var clientId = Utf8String(oidc, "client-id", 1, 512);
        var clientSecretRef = Utf8String(oidc, "client-secret-ref", 1, 512);
        var redirectBaseUri = AbsoluteHttpsUri(oidc, "redirect-base-uri", allowPath: true, allowQuery: false, allowFragment: false);
        var allowedOrigins = ExactHttpsOrigins(auth, "allowed-origins");

        if (Encoding.UTF8.GetByteCount(issuer.AbsoluteUri) > 2048)
            throw new InvalidDataException("auth.oidc.issuer must be <= 2048 UTF-8 bytes.");
        if (Encoding.UTF8.GetByteCount(redirectBaseUri.AbsoluteUri) > 2048)
            throw new InvalidDataException("auth.oidc.redirect-base-uri must be <= 2048 UTF-8 bytes.");

        var authConfig = new GatewayOidcConfig(
            issuer,
            clientId,
            clientSecretRef,
            redirectBaseUri,
            allowedOrigins,
            loginLifetime,
            idle,
            absolute,
            maxSessions);

        return new GatewayConfig(
            connect,
            reconnectInitial,
            reconnectMax,
            heartbeatInterval,
            heartbeatTimeout,
            idle,
            absolute,
            authConfig,
            model);
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

    private static int IntInRange(TomlTable table, string key, int minimum, int maximum)
    {
        if (!table.TryGetValue(key, out var value) || value is not long number || number < minimum || number > maximum)
            throw new InvalidDataException($"Config field {key} must be in range {minimum}..{maximum}.");
        return checked((int)number);
    }

    private static string Utf8String(TomlTable table, string key, int minimumBytes, int maximumBytes)
    {
        if (!table.TryGetValue(key, out var value) || value is not string text)
            throw new InvalidDataException($"Config field {key} must be a string.");
        var byteCount = Encoding.UTF8.GetByteCount(text);
        if (byteCount < minimumBytes || byteCount > maximumBytes)
            throw new InvalidDataException($"Config field {key} must be {minimumBytes}..{maximumBytes} UTF-8 bytes.");
        return text;
    }

    private static Uri AbsoluteHttpsUri(
        TomlTable table,
        string key,
        bool allowPath,
        bool allowQuery,
        bool allowFragment)
    {
        var text = Utf8String(table, key, 1, 2048);
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            (!allowPath && uri.AbsolutePath != "/") ||
            (!allowQuery && !string.IsNullOrEmpty(uri.Query)) ||
            (!allowFragment && !string.IsNullOrEmpty(uri.Fragment)))
            throw new InvalidDataException($"Config field {key} must be an allowed absolute HTTPS URI.");
        return uri;
    }

    private static IReadOnlySet<string> ExactHttpsOrigins(TomlTable table, string key)
    {
        if (!table.TryGetValue(key, out var value) || value is not TomlArray array || array.Count is < 1 or > 64)
            throw new InvalidDataException($"Config field {key} must contain 1..64 HTTPS origins.");

        var origins = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in array)
        {
            if (item is not string origin ||
                !Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(uri.Host) ||
                !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment) ||
                uri.AbsolutePath != "/" ||
                !string.Equals(uri.GetLeftPart(UriPartial.Authority), origin, StringComparison.Ordinal))
                throw new InvalidDataException($"Config field {key} contains an invalid exact HTTPS origin.");
            if (!origins.Add(origin))
                throw new InvalidDataException($"Config field {key} contains a duplicate origin.");
        }

        return origins;
    }
}
