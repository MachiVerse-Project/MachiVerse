using Google.Protobuf;
using MachiVerse.Gateway.Configuration;
using MachiVerse.Protocol.V1;

namespace MachiVerse.Gateway.Auth;

public sealed record OidcAuthorizationRedirect(
    LoginAuthorizationMaterial Login,
    Uri AuthorizationUri);

public sealed record VerifiedOidcCallback(
    VerifiedIdentityAssertionV1 Assertion,
    string VerifiedNonce);

public interface IExternalOidcBoundary
{
    Uri CreateAuthorizationUri(LoginAuthorizationMaterial login, GatewayOidcConfig config);

    Task<VerifiedOidcCallback> VerifyCallbackAsync(
        string authorizationCode,
        string pkceVerifier,
        ReadOnlyMemory<byte> loginTransactionId,
        CancellationToken cancellationToken = default);
}

public sealed record MasterSessionGrant(
    byte[] SessionId,
    ulong SessionGeneration,
    byte[] AccountId,
    byte[]? DiverRef,
    AuthDomainWireV1 AuthDomain,
    string EffectiveRoleSet,
    IReadOnlyList<string> EffectivePermissions,
    ulong MasterGeneration);

public interface IMasterSessionGrantResolver
{
    Task<MasterSessionGrant> ResolveAsync(
        AuthLoginResultV1 loginResult,
        CancellationToken cancellationToken = default);
}

public sealed record CompletedBrowserLogin(
    GatewaySessionSnapshot Session,
    string SessionHandle,
    string RedirectPath);

public sealed class GatewayBrowserAuthFlow(
    MasterLoginCoordinator masterLogin,
    LoginTransactionStore transactions,
    GatewaySessionStore sessions,
    IExternalOidcBoundary oidc,
    IMasterSessionGrantResolver sessionGrantResolver,
    GatewayOidcConfig config)
{
    public async Task<OidcAuthorizationRedirect> BeginAsync(
        AuthDomainWireV1 authDomain,
        string? returnPath,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        LoginTransactionStore.ValidateReturnPath(returnPath);
        var authorized = await masterLogin.BeginAsync(authDomain, returnPath, now, cancellationToken);
        var authorizationUri = oidc.CreateAuthorizationUri(authorized.Authorization, config);
        RequireAuthorizationUri(authorizationUri, config.Issuer);
        return new OidcAuthorizationRedirect(authorized.Authorization, authorizationUri);
    }

    public async Task<CompletedBrowserLogin> CompleteCallbackAsync(
        ReadOnlyMemory<byte> loginTransactionId,
        string returnedState,
        string authorizationCode,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (loginTransactionId.Length != 16 || loginTransactionId.Span.IndexOfAnyExcept((byte)0) < 0)
            throw new InvalidDataException("auth.login-state-mismatch");
        ArgumentException.ThrowIfNullOrWhiteSpace(returnedState);
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizationCode);

        var pkceVerifier = masterLogin.AcceptCallback(loginTransactionId.Span, returnedState, now);
        var transaction = transactions.Get(loginTransactionId.Span, now);
        VerifiedOidcCallback verified;
        try
        {
            verified = await oidc.VerifyCallbackAsync(
                authorizationCode,
                pkceVerifier,
                loginTransactionId,
                cancellationToken);
        }
        catch
        {
            transactions.Reject(loginTransactionId.Span);
            throw;
        }

        ArgumentNullException.ThrowIfNull(verified);
        ArgumentNullException.ThrowIfNull(verified.Assertion);
        if (!verified.Assertion.LoginTransactionId.Span.SequenceEqual(loginTransactionId.Span))
        {
            transactions.Reject(loginTransactionId.Span);
            throw new InvalidDataException("auth.login-transaction-mismatch");
        }

        masterLogin.VerifyNonce(loginTransactionId.Span, verified.VerifiedNonce, now);
        var loginResult = await masterLogin.FinalizeAsync(verified.Assertion, now, cancellationToken);
        var grant = await sessionGrantResolver.ResolveAsync(loginResult, cancellationToken);
        ValidateMasterGrant(loginResult, transaction, grant);

        var session = sessions.CreateMasterGranted(
            grant.SessionId,
            grant.SessionGeneration,
            grant.AccountId,
            grant.DiverRef ?? Array.Empty<byte>(),
            grant.AuthDomain,
            grant.EffectiveRoleSet,
            grant.EffectivePermissions,
            grant.MasterGeneration,
            now);

        return new CompletedBrowserLogin(
            session.Session,
            session.SessionHandle,
            transaction.ReturnPath ?? "/");
    }

    private static void ValidateMasterGrant(
        AuthLoginResultV1 loginResult,
        LoginTransactionSnapshot transaction,
        MasterSessionGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        if (loginResult.SessionId.Length != 16 ||
            !grant.SessionId.AsSpan().SequenceEqual(loginResult.SessionId.Span) ||
            grant.SessionGeneration != loginResult.SessionGeneration)
            throw new InvalidDataException("auth.session-stale");
        if ((int)grant.AuthDomain != (int)transaction.AuthDomain)
            throw new InvalidDataException("auth.unauthorized");
        if (grant.MasterGeneration != transaction.MasterGeneration)
            throw new InvalidDataException("auth.master-changed");
        if (grant.AccountId.Length != 16 || grant.AccountId.AsSpan().IndexOfAnyExcept((byte)0) < 0)
            throw new InvalidDataException("auth.identity-verification-failed");
        if (grant.DiverRef is { Length: > 0 } diver &&
            (diver.Length != 16 || diver.AsSpan().IndexOfAnyExcept((byte)0) < 0))
            throw new InvalidDataException("auth.identity-verification-failed");
    }

    private static void RequireAuthorizationUri(Uri uri, Uri issuer)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) || uri.Fragment.Length != 0)
            throw new InvalidDataException("auth.identity-verification-failed");
        if (!issuer.IsAbsoluteUri || !string.Equals(issuer.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("auth.issuer-untrusted");
    }
}
