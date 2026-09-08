namespace MachiVerse.Gateway.Configuration;

public static class AlphaGatewayConfigPolicy
{
    private static readonly IReadOnlySet<string> RuntimeMutable = new HashSet<string>(StringComparer.Ordinal)
    {
        "network.reconnect-initial-ms",
        "network.reconnect-max-ms",
        "peer.heartbeat-interval-ms",
    };

    public static IReadOnlySet<string> RuntimeMutableKeys => RuntimeMutable;

    public static bool IsRuntimeMutable(string key)
        => RuntimeMutable.Contains(key);
}
