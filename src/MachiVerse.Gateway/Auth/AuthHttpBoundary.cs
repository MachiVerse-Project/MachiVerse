using MachiVerse.Gateway.Configuration;
using MachiVerse.Protocol.V1;
using Microsoft.AspNetCore.Http;

namespace MachiVerse.Gateway.Auth;

public static class AuthHttpBoundary
{
    public const string SessionCookieName = "__Host-mv_session";
    public const string LoginCookieName = "__Host-mv_login";

    public static CookieOptions SessionCookieOptions(DateTimeOffset? expires = null)
        => new()
        {
            Secure = true,
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Domain = null,
            Expires = expires,
            IsEssential = true,
        };

    public static CookieOptions LoginCookieOptions(DateTimeOffset expires)
        => new()
        {
            Secure = true,
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Domain = null,
            Expires = expires,
            IsEssential = true,
        };

    public static CookieOptions DeleteCookieOptions(SameSiteMode sameSite)
        => new()
        {
            Secure = true,
            HttpOnly = true,
            SameSite = sameSite,
            Path = "/",
            Domain = null,
            Expires = DateTimeOffset.UnixEpoch,
            MaxAge = TimeSpan.Zero,
            IsEssential = true,
        };

    public static void RequireAllowedOrigin(string? origin, GatewayOidcConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(origin) || !config.AllowedOrigins.Contains(origin))
            throw new InvalidDataException("auth.origin-rejected");
    }

    public static string? ValidateReturnPath(string? returnPath)
    {
        LoginTransactionStore.ValidateReturnPath(returnPath);
        return returnPath;
    }
}

public sealed class WebSocketSessionGate(
    GatewaySessionStore sessions,
    GatewayOidcConfig config)
{
    public GatewaySessionSnapshot Validate(
        string? origin,
        string? sessionHandle,
        AuthDomainWireV1 expectedDomain,
        ulong? expectedSessionGeneration,
        DateTimeOffset now)
    {
        AuthHttpBoundary.RequireAllowedOrigin(origin, config);
        if (string.IsNullOrWhiteSpace(sessionHandle))
            throw new InvalidDataException("auth.unauthenticated");
        return sessions.ResolveHandle(sessionHandle, expectedDomain, expectedSessionGeneration, now);
    }
}

public sealed record BrowserSessionStatus(
    bool Authenticated,
    string? SessionId,
    int? AuthDomain,
    string? EffectiveRoleSet,
    ulong? SessionGeneration,
    int? Status)
{
    public static BrowserSessionStatus Anonymous { get; } = new(false, null, null, null, null, null);

    public static BrowserSessionStatus From(GatewaySessionSnapshot session)
        => new(
            true,
            Convert.ToHexStringLower(session.SessionId),
            (int)session.AuthDomain,
            session.EffectiveRoleSet,
            session.SessionGeneration,
            (int)session.Status);
}
