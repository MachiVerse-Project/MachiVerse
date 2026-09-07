using MachiVerse.Gateway.Configuration;
using MachiVerse.Protocol.V1;

namespace MachiVerse.Gateway.Auth;

public static class GatewayAuthEndpoints
{
    public static IEndpointRouteBuilder MapGatewayAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/auth/v1/login", BeginLoginAsync);
        endpoints.MapGet("/auth/v1/callback", CompleteCallbackAsync);
        endpoints.MapPost("/auth/v1/logout", Logout);
        endpoints.MapGet("/auth/v1/session", ReadSession);
        return endpoints;
    }

    private static async Task<IResult> BeginLoginAsync(
        HttpContext context,
        GatewayBrowserAuthFlow flow,
        GatewayConfig config,
        CancellationToken cancellationToken)
    {
        try
        {
            var domain = ParseAuthDomain(context.Request.Query["auth_domain"]);
            var returnPath = OptionalQuery(context, "return_path");
            var now = DateTimeOffset.UtcNow;
            var redirect = await flow.BeginAsync(domain, returnPath, now, cancellationToken);
            context.Response.Cookies.Append(
                AuthHttpBoundary.LoginCookieName,
                AuthSecurityPrimitives.Base64UrlEncode(redirect.Login.LoginTransactionId),
                AuthHttpBoundary.LoginCookieOptions(now.AddSeconds(config.Oidc.LoginTransactionLifetimeSeconds)));
            return Results.Redirect(redirect.AuthorizationUri.AbsoluteUri, permanent: false, preserveMethod: false);
        }
        catch (InvalidDataException ex)
        {
            return AuthFailure(ex);
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(new { code = "auth.invalid-request" });
        }
    }

    private static async Task<IResult> CompleteCallbackAsync(
        HttpContext context,
        GatewayBrowserAuthFlow flow,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!context.Request.Cookies.TryGetValue(AuthHttpBoundary.LoginCookieName, out var loginCookie) ||
                string.IsNullOrWhiteSpace(loginCookie))
                return Results.Unauthorized();

            var transactionId = DecodeId128(loginCookie, "auth.login-state-mismatch");
            var state = RequiredQuery(context, "state");
            var code = RequiredQuery(context, "code");
            var completed = await flow.CompleteCallbackAsync(
                transactionId,
                state,
                code,
                DateTimeOffset.UtcNow,
                cancellationToken);

            context.Response.Cookies.Append(
                AuthHttpBoundary.SessionCookieName,
                completed.SessionHandle,
                AuthHttpBoundary.SessionCookieOptions());
            context.Response.Cookies.Delete(
                AuthHttpBoundary.LoginCookieName,
                AuthHttpBoundary.DeleteCookieOptions(SameSiteMode.Lax));
            return Results.Redirect(completed.RedirectPath, permanent: false, preserveMethod: false);
        }
        catch (InvalidDataException ex)
        {
            return AuthFailure(ex);
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(new { code = "auth.invalid-request" });
        }
    }

    private static IResult Logout(
        HttpContext context,
        GatewaySessionStore sessions,
        GatewayConfig config)
    {
        try
        {
            AuthHttpBoundary.RequireAllowedOrigin(context.Request.Headers.Origin.ToString(), config.Oidc);
            if (!context.Request.Cookies.TryGetValue(AuthHttpBoundary.SessionCookieName, out var handle) ||
                string.IsNullOrWhiteSpace(handle))
                return Results.Unauthorized();

            var session = ResolveBrowserSession(sessions, handle, DateTimeOffset.UtcNow);
            sessions.Revoke(session.SessionId, session.SessionGeneration, DateTimeOffset.UtcNow);
            context.Response.Cookies.Delete(
                AuthHttpBoundary.SessionCookieName,
                AuthHttpBoundary.DeleteCookieOptions(SameSiteMode.Strict));
            return Results.NoContent();
        }
        catch (InvalidDataException ex)
        {
            return AuthFailure(ex);
        }
    }

    private static IResult ReadSession(HttpContext context, GatewaySessionStore sessions)
    {
        if (!context.Request.Cookies.TryGetValue(AuthHttpBoundary.SessionCookieName, out var handle) ||
            string.IsNullOrWhiteSpace(handle))
            return Results.Ok(BrowserSessionStatus.Anonymous);

        try
        {
            var session = ResolveBrowserSession(sessions, handle, DateTimeOffset.UtcNow);
            return Results.Ok(BrowserSessionStatus.From(session));
        }
        catch (InvalidDataException ex) when (ex.Message is
            "auth.unauthenticated" or "auth.session-expired" or "auth.session-revoked" or "auth.session-stale")
        {
            return Results.Ok(BrowserSessionStatus.Anonymous);
        }
    }

    private static GatewaySessionSnapshot ResolveBrowserSession(
        GatewaySessionStore sessions,
        string handle,
        DateTimeOffset now)
    {
        try
        {
            return sessions.ResolveHandle(handle, (AuthDomainWireV1)1, expectedSessionGeneration: null, now);
        }
        catch (InvalidDataException ex) when (ex.Message == "auth.unauthorized")
        {
            return sessions.ResolveHandle(handle, (AuthDomainWireV1)2, expectedSessionGeneration: null, now);
        }
    }

    private static AuthDomainWireV1 ParseAuthDomain(string value)
        => value switch
        {
            "general-view" => (AuthDomainWireV1)1,
            "admin-view" => (AuthDomainWireV1)2,
            _ => throw new InvalidDataException("auth.invalid-domain"),
        };

    private static string RequiredQuery(HttpContext context, string key)
    {
        var value = context.Request.Query[key].ToString();
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"Missing query field: {key}.");
        return value;
    }

    private static string? OptionalQuery(HttpContext context, string key)
    {
        var value = context.Request.Query[key].ToString();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static byte[] DecodeId128(string value, string errorCode)
    {
        try
        {
            var normalized = value.Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
            var bytes = Convert.FromBase64String(normalized);
            if (bytes.Length != 16 || bytes.AsSpan().IndexOfAnyExcept((byte)0) < 0)
                throw new FormatException();
            return bytes;
        }
        catch (FormatException)
        {
            throw new InvalidDataException(errorCode);
        }
    }

    private static IResult AuthFailure(InvalidDataException exception)
        => exception.Message switch
        {
            "auth.unauthenticated" or "auth.session-expired" or "auth.session-revoked" => Results.Unauthorized(),
            "auth.unauthorized" or "auth.origin-rejected" => Results.StatusCode(StatusCodes.Status403Forbidden),
            "auth.master-changed" or "master.authority-unknown" or "master.transition-no-authority" =>
                Results.Json(new { code = "auth.master-changed" }, statusCode: StatusCodes.Status503ServiceUnavailable),
            _ => Results.BadRequest(new { code = exception.Message }),
        };
}
