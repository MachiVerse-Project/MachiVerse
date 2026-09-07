using Tomlyn.Model;

namespace MachiVerse.Gateway.Configuration;

public sealed record GatewayOidcConfig(
    Uri Issuer,
    string ClientId,
    string ClientSecretRef,
    Uri RedirectBaseUri,
    IReadOnlySet<string> AllowedOrigins,
    int LoginTransactionLifetimeSeconds,
    int SessionIdleLifetimeSeconds,
    int SessionAbsoluteLifetimeSeconds,
    int MaxActiveSessionsPerAccount);

public sealed record GatewayConfig(
    int ConnectTimeoutMs,
    int ReconnectInitialMs,
    int ReconnectMaxMs,
    int HeartbeatIntervalMs,
    int HeartbeatTimeoutMs,
    int SessionIdleLifetimeSeconds,
    int SessionAbsoluteLifetimeSeconds,
    GatewayOidcConfig Oidc,
    TomlTable Raw);
