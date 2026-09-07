using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using MachiVerse.Gateway.Auth;
using MachiVerse.Gateway.Configuration;
using MachiVerse.Gateway.State;
using MachiVerse.Protocol.V1;
using Microsoft.AspNetCore.Http;

internal static class Gw04AuthSmoke
{
    public static async Task RunAsync(GatewayConfig gatewayConfig)
    {
        ArgumentNullException.ThrowIfNull(gatewayConfig);
        var config = gatewayConfig.Oidc;
        var now = new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero);
        var localGatewayId = Id(90);
        var masterGatewayId = Id(91);
        var masterSessionId = Id(92).ToByteArray();
        var accountId = Id(93).ToByteArray();
        var diverRef = Id(94).ToByteArray();

        var ephemeral = AuthSecurityPrimitives.CreateOidcSecrets();
        var expectedChallenge = AuthSecurityPrimitives.Base64UrlEncode(
            SHA256.HashData(Encoding.ASCII.GetBytes(ephemeral.PkceVerifier)));
        Require(ephemeral.PkceChallenge == expectedChallenge, "PKCE challenge must be S256(verifier).");
        Require(AuthSecurityPrimitives.VerifyAsciiSecret(ephemeral.State, AuthSecurityPrimitives.DigestAsciiSecret(ephemeral.State)),
            "state digest verification failed.");

        var authority = new MasterAuthorityTracker(localGatewayId);
        authority.Apply(new MasterGenerationStateV1
        {
            MasterGeneration = 2,
            CurrentMasterGatewayId = masterGatewayId,
        });

        var secretStore = new InMemoryLoginSecretStore();
        var transactions = new LoginTransactionStore(secretStore);
        var masterClient = new FakeMasterLoginClient(masterGatewayId, masterSessionId, sessionGeneration: 7);
        var coordinator = new MasterLoginCoordinator(
            authority,
            masterClient,
            transactions,
            config,
            localGatewayId);
        var sessions = new GatewaySessionStore(
            config.SessionIdleLifetimeSeconds,
            config.SessionAbsoluteLifetimeSeconds,
            config.MaxActiveSessionsPerAccount);
        var oidc = new FakeOidcBoundary(config.Issuer);
        var grantResolver = new FakeGrantResolver(
            masterSessionId,
            7,
            accountId,
            diverRef,
            (AuthDomainWireV1)1,
            "view.diver",
            ["view.operation.diver", "view.session.read.self", "view.world.read.participant", "view.world.subscribe"],
            masterGeneration: 2);
        var flow = new GatewayBrowserAuthFlow(
            coordinator,
            transactions,
            sessions,
            oidc,
            grantResolver,
            config);

        var begin = await flow.BeginAsync((AuthDomainWireV1)1, "/world", now);
        Require(begin.Login.PkceChallengeMethod == "S256", "Login transaction must require PKCE S256.");
        Require(begin.AuthorizationUri.Scheme == Uri.UriSchemeHttps, "OIDC authorization redirect must use HTTPS.");
        Require(begin.Login.LoginTransactionId.Length == 16, "LoginTransactionId must be Id128.");
        Require(begin.Login.ReturnPath == "/world", "same-origin return path must be retained.");

        var completed = await flow.CompleteCallbackAsync(
            begin.Login.LoginTransactionId,
            begin.Login.State,
            "mock-authorization-code",
            now.AddSeconds(1));
        Require(completed.RedirectPath == "/world", "Completed login must preserve validated relative return path.");
        Require(completed.Session.SessionId.AsSpan().SequenceEqual(masterSessionId),
            "Connected Gateway must preserve Master-issued GatewaySessionId.");
        Require(completed.Session.SessionGeneration == 7, "Connected Gateway must preserve Master-issued session generation.");
        Require(completed.SessionHandle.Length >= 40, "Browser SessionHandle must remain a high-entropy opaque value.");

        var attached = sessions.ResolveHandle(
            completed.SessionHandle,
            (AuthDomainWireV1)1,
            expectedSessionGeneration: 7,
            now.AddSeconds(2));
        Require(attached.EffectivePermissions.SequenceEqual(attached.EffectivePermissions.OrderBy(static item => item, StringComparer.Ordinal)),
            "Effective permissions must be canonical ASCII order.");

        var adminDomainRejected = false;
        try
        {
            _ = sessions.ResolveHandle(completed.SessionHandle, (AuthDomainWireV1)2, 7, now.AddSeconds(2));
        }
        catch (InvalidDataException ex) when (ex.Message == "auth.unauthorized")
        {
            adminDomainRejected = true;
        }
        Require(adminDomainRejected, "General View session must not attach to Admin View auth domain.");

        var staleGenerationRejected = false;
        try
        {
            _ = sessions.ResolveHandle(completed.SessionHandle, (AuthDomainWireV1)1, 6, now.AddSeconds(2));
        }
        catch (InvalidDataException ex) when (ex.Message == "auth.session-stale")
        {
            staleGenerationRejected = true;
        }
        Require(staleGenerationRejected, "Old session generation must not admit a new attach.");

        var allowedOrigin = config.AllowedOrigins.Single();
        AuthHttpBoundary.RequireAllowedOrigin(allowedOrigin, config);
        var originRejected = false;
        try
        {
            AuthHttpBoundary.RequireAllowedOrigin("https://attacker.example.invalid", config);
        }
        catch (InvalidDataException ex) when (ex.Message == "auth.origin-rejected")
        {
            originRejected = true;
        }
        Require(originRejected, "Cross-origin WebSocket/session attach must fail closed.");

        var sessionCookie = AuthHttpBoundary.SessionCookieOptions();
        Require(sessionCookie.Secure && sessionCookie.HttpOnly && sessionCookie.SameSite == SameSiteMode.Strict &&
                sessionCookie.Path == "/" && sessionCookie.Domain is null,
            "__Host-mv_session cookie profile mismatch.");
        var loginCookie = AuthHttpBoundary.LoginCookieOptions(now.AddMinutes(10));
        Require(loginCookie.Secure && loginCookie.HttpOnly && loginCookie.SameSite == SameSiteMode.Lax &&
                loginCookie.Path == "/" && loginCookie.Domain is null,
            "__Host-mv_login cookie profile mismatch.");

        var returnPathRejected = false;
        try
        {
            LoginTransactionStore.ValidateReturnPath("//evil.example.invalid/");
        }
        catch (InvalidDataException ex) when (ex.Message == "auth.return-path-invalid")
        {
            returnPathRejected = true;
        }
        Require(returnPathRejected, "scheme-relative return path must be rejected.");

        var second = await flow.BeginAsync((AuthDomainWireV1)1, "/", now.AddSeconds(3));
        authority.Apply(new MasterGenerationStateV1
        {
            MasterGeneration = 3,
            CurrentMasterGatewayId = masterGatewayId,
        });
        var oldMasterRejected = false;
        try
        {
            _ = await flow.CompleteCallbackAsync(
                second.Login.LoginTransactionId,
                second.Login.State,
                "mock-authorization-code-2",
                now.AddSeconds(4));
        }
        catch (InvalidDataException ex) when (ex.Message == "auth.master-changed")
        {
            oldMasterRejected = true;
        }
        Require(oldMasterRejected, "Login started under old MasterGeneration must not finalize as current authority.");

        var revoked = sessions.Revoke(completed.Session.SessionId, completed.Session.SessionGeneration, now.AddSeconds(5));
        Require(revoked.Status == GatewaySessionStatus.Revoked && revoked.SessionGeneration == 8,
            "Logout/revoke must advance session generation and revoke browser session.");
        var revokedHandleRejected = false;
        try
        {
            _ = sessions.ResolveHandle(completed.SessionHandle, (AuthDomainWireV1)1, null, now.AddSeconds(6));
        }
        catch (InvalidDataException ex) when (ex.Message == "auth.unauthenticated")
        {
            revokedHandleRejected = true;
        }
        Require(revokedHandleRejected, "Revoked SessionHandle must no longer resolve.");
    }

    private static ByteString Id(byte value) => ByteString.CopyFrom(Enumerable.Repeat(value, 16).ToArray());

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class FakeMasterLoginClient(
        ByteString masterGatewayId,
        byte[] sessionId,
        ulong sessionGeneration) : IMasterLoginAuthorityClient
    {
        public Task<MasterAuthorityReply<AuthLoginProxyResultV1>> BeginAsync(
            AuthLoginProxyV1 request,
            CancellationToken cancellationToken = default)
        {
            var payload = new AuthLoginProxyResultV1
            {
                Result = Success(),
                LoginTransactionId = request.LoginTransactionId,
                MasterGeneration = request.MasterGeneration,
            };
            return Task.FromResult(new MasterAuthorityReply<AuthLoginProxyResultV1>(
                request.MasterGeneration,
                masterGatewayId.ToByteArray(),
                payload));
        }

        public Task<MasterAuthorityReply<AuthLoginResultV1>> FinalizeAsync(
            AuthLoginAssertionV1 request,
            CancellationToken cancellationToken = default)
        {
            var payload = new AuthLoginResultV1
            {
                Result = Success(),
                SessionId = ByteString.CopyFrom(sessionId),
                SessionGeneration = sessionGeneration,
            };
            return Task.FromResult(new MasterAuthorityReply<AuthLoginResultV1>(
                request.MasterGeneration,
                masterGatewayId.ToByteArray(),
                payload));
        }

        private static ResultV1 Success() => new()
        {
            Status = (ResultStatusV1)1,
            Code = "ok",
            RetryAdvice = (RetryAdviceV1)1,
        };
    }

    private sealed class FakeOidcBoundary(Uri issuer) : IExternalOidcBoundary
    {
        private LoginAuthorizationMaterial? _lastLogin;

        public Uri CreateAuthorizationUri(LoginAuthorizationMaterial login, GatewayOidcConfig config)
        {
            _lastLogin = login;
            return new Uri($"https://idp.example.invalid/authorize?state={Uri.EscapeDataString(login.State)}&code_challenge={Uri.EscapeDataString(login.PkceChallenge)}&code_challenge_method=S256");
        }

        public Task<VerifiedOidcCallback> VerifyCallbackAsync(
            string authorizationCode,
            string pkceVerifier,
            ReadOnlyMemory<byte> loginTransactionId,
            CancellationToken cancellationToken = default)
        {
            Require(!string.IsNullOrWhiteSpace(authorizationCode), "Mock OIDC callback requires authorization code.");
            Require(!string.IsNullOrWhiteSpace(pkceVerifier), "PKCE verifier must remain server-side and reach OIDC token verification boundary.");
            var login = _lastLogin ?? throw new InvalidOperationException("Authorization must begin before callback.");
            var assertion = new VerifiedIdentityAssertionV1
            {
                LoginTransactionId = ByteString.CopyFrom(loginTransactionId.Span),
                Issuer = issuer.AbsoluteUri,
                Subject = "mock-subject",
                VerificationDigest = ByteString.CopyFrom(SHA256.HashData("verified-oidc-assertion"u8)),
            };
            assertion.AuthenticationMethods.Add("pwd");
            return Task.FromResult(new VerifiedOidcCallback(assertion, login.Nonce));
        }
    }

    private sealed class FakeGrantResolver(
        byte[] sessionId,
        ulong sessionGeneration,
        byte[] accountId,
        byte[] diverRef,
        AuthDomainWireV1 authDomain,
        string role,
        IReadOnlyList<string> permissions,
        ulong masterGeneration) : IMasterSessionGrantResolver
    {
        public Task<MasterSessionGrant> ResolveAsync(
            AuthLoginResultV1 loginResult,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new MasterSessionGrant(
                sessionId.ToArray(),
                sessionGeneration,
                accountId.ToArray(),
                diverRef.ToArray(),
                authDomain,
                role,
                permissions.ToArray(),
                masterGeneration));
    }
}
