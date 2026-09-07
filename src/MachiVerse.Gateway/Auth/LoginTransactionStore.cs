using System.Security.Cryptography;
using MachiVerse.Protocol.V1;

namespace MachiVerse.Gateway.Auth;

public enum LoginTransactionStatus
{
    Created = 1,
    AuthorizationPending = 2,
    CallbackReceived = 3,
    MasterFinalizing = 4,
    Completed = 5,
    Rejected = 6,
    Expired = 7,
}

public sealed record LoginTransactionSnapshot(
    byte[] LoginTransactionId,
    byte[] ConnectedGatewayId,
    ulong MasterGeneration,
    AuthDomainWireV1 AuthDomain,
    string Issuer,
    string? ReturnPath,
    byte[] StateDigest,
    byte[] NonceDigest,
    string PkceVerifierSecretRef,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    LoginTransactionStatus Status);

public sealed record LoginAuthorizationMaterial(
    byte[] LoginTransactionId,
    string State,
    string Nonce,
    string PkceChallenge,
    string PkceChallengeMethod,
    string? ReturnPath);

public sealed class LoginTransactionStore(ILoginSecretStore secretStore)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, LoginTransactionSnapshot> _transactions = new(StringComparer.Ordinal);

    public LoginAuthorizationMaterial BeginMasterAuthorized(
        ReadOnlySpan<byte> loginTransactionId,
        ReadOnlySpan<byte> connectedGatewayId,
        ulong masterGeneration,
        AuthDomainWireV1 authDomain,
        Uri issuer,
        string? returnPath,
        DateTimeOffset now,
        int lifetimeSeconds)
    {
        var transactionId = RequireId128(loginTransactionId, "login_transaction_id");
        var gatewayId = RequireId128(connectedGatewayId, "connected_gateway_id");
        if (masterGeneration == 0) throw new InvalidDataException("auth.master-changed");
        RequireAuthDomain(authDomain);
        ArgumentNullException.ThrowIfNull(issuer);
        if (!issuer.IsAbsoluteUri || !string.Equals(issuer.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("auth.issuer-untrusted");
        ValidateReturnPath(returnPath);
        if (lifetimeSeconds is < 60 or > 1800)
            throw new ArgumentOutOfRangeException(nameof(lifetimeSeconds));

        var ephemeral = AuthSecurityPrimitives.CreateOidcSecrets();
        var expiresAt = now.AddSeconds(lifetimeSeconds);
        var secretRef = secretStore.Put(ephemeral.PkceVerifier, expiresAt);
        var snapshot = new LoginTransactionSnapshot(
            transactionId,
            gatewayId,
            masterGeneration,
            authDomain,
            issuer.AbsoluteUri,
            returnPath,
            AuthSecurityPrimitives.DigestAsciiSecret(ephemeral.State),
            AuthSecurityPrimitives.DigestAsciiSecret(ephemeral.Nonce),
            secretRef,
            now,
            expiresAt,
            LoginTransactionStatus.AuthorizationPending);

        var key = Hex(transactionId);
        lock (_gate)
        {
            if (!_transactions.TryAdd(key, snapshot))
            {
                secretStore.Delete(secretRef);
                throw new InvalidDataException("auth.login-transaction-duplicate");
            }
        }

        return new LoginAuthorizationMaterial(
            transactionId,
            ephemeral.State,
            ephemeral.Nonce,
            ephemeral.PkceChallenge,
            "S256",
            returnPath);
    }

    public string AcceptCallbackAndTakePkceVerifier(
        ReadOnlySpan<byte> loginTransactionId,
        string returnedState,
        ulong currentMasterGeneration,
        DateTimeOffset now)
    {
        var key = Hex(RequireId128(loginTransactionId, "login_transaction_id"));
        lock (_gate)
        {
            var current = ReadRequired(key);
            current = RequireLive(current, now);
            RequireGeneration(current, currentMasterGeneration);
            if (current.Status != LoginTransactionStatus.AuthorizationPending)
                throw new InvalidDataException("auth.login-state-mismatch");
            if (!AuthSecurityPrimitives.VerifyAsciiSecret(returnedState, current.StateDigest))
                throw new InvalidDataException("auth.login-state-mismatch");

            var next = current with { Status = LoginTransactionStatus.CallbackReceived };
            _transactions[key] = next;
            try
            {
                return secretStore.Take(current.PkceVerifierSecretRef, now);
            }
            catch
            {
                _transactions[key] = current with { Status = LoginTransactionStatus.Rejected };
                throw;
            }
        }
    }

    public void VerifyNonce(
        ReadOnlySpan<byte> loginTransactionId,
        string verifiedTokenNonce,
        DateTimeOffset now)
    {
        var key = Hex(RequireId128(loginTransactionId, "login_transaction_id"));
        lock (_gate)
        {
            var current = RequireLive(ReadRequired(key), now);
            if (current.Status is not (LoginTransactionStatus.CallbackReceived or LoginTransactionStatus.MasterFinalizing))
                throw new InvalidDataException("auth.login-state-mismatch");
            if (!AuthSecurityPrimitives.VerifyAsciiSecret(verifiedTokenNonce, current.NonceDigest))
                throw new InvalidDataException("auth.login-nonce-mismatch");
        }
    }

    public LoginTransactionSnapshot BeginMasterFinalization(
        ReadOnlySpan<byte> loginTransactionId,
        ulong currentMasterGeneration,
        DateTimeOffset now)
    {
        var key = Hex(RequireId128(loginTransactionId, "login_transaction_id"));
        lock (_gate)
        {
            var current = RequireLive(ReadRequired(key), now);
            RequireGeneration(current, currentMasterGeneration);
            if (current.Status != LoginTransactionStatus.CallbackReceived)
                throw new InvalidDataException("auth.login-state-mismatch");
            var next = current with { Status = LoginTransactionStatus.MasterFinalizing };
            _transactions[key] = next;
            return Clone(next);
        }
    }

    public LoginTransactionSnapshot Complete(
        ReadOnlySpan<byte> loginTransactionId,
        ulong resultMasterGeneration,
        DateTimeOffset now)
    {
        var key = Hex(RequireId128(loginTransactionId, "login_transaction_id"));
        lock (_gate)
        {
            var current = RequireLive(ReadRequired(key), now);
            RequireGeneration(current, resultMasterGeneration);
            if (current.Status != LoginTransactionStatus.MasterFinalizing)
                throw new InvalidDataException("auth.login-state-mismatch");
            var next = current with { Status = LoginTransactionStatus.Completed };
            _transactions[key] = next;
            secretStore.Delete(next.PkceVerifierSecretRef);
            return Clone(next);
        }
    }

    public void Reject(ReadOnlySpan<byte> loginTransactionId)
    {
        var key = Hex(RequireId128(loginTransactionId, "login_transaction_id"));
        lock (_gate)
        {
            var current = ReadRequired(key);
            _transactions[key] = current with { Status = LoginTransactionStatus.Rejected };
            secretStore.Delete(current.PkceVerifierSecretRef);
        }
    }

    public LoginTransactionSnapshot Get(ReadOnlySpan<byte> loginTransactionId, DateTimeOffset now)
    {
        var key = Hex(RequireId128(loginTransactionId, "login_transaction_id"));
        lock (_gate)
        {
            var current = RequireLive(ReadRequired(key), now);
            return Clone(current);
        }
    }

    public static void ValidateReturnPath(string? returnPath)
    {
        if (returnPath is null) return;
        if (returnPath.Length == 0 ||
            !returnPath.StartsWith("/", StringComparison.Ordinal) ||
            returnPath.StartsWith("//", StringComparison.Ordinal) ||
            returnPath.Contains('\\') ||
            Uri.TryCreate(returnPath, UriKind.Absolute, out _))
            throw new InvalidDataException("auth.return-path-invalid");
    }

    private LoginTransactionSnapshot RequireLive(LoginTransactionSnapshot current, DateTimeOffset now)
    {
        if (current.Status == LoginTransactionStatus.Expired || now >= current.ExpiresAt)
        {
            var expired = current with { Status = LoginTransactionStatus.Expired };
            _transactions[Hex(current.LoginTransactionId)] = expired;
            secretStore.Delete(current.PkceVerifierSecretRef);
            throw new InvalidDataException("auth.login-expired");
        }
        if (current.Status is LoginTransactionStatus.Rejected or LoginTransactionStatus.Completed)
            throw new InvalidDataException("auth.login-state-mismatch");
        return current;
    }

    private LoginTransactionSnapshot ReadRequired(string key)
        => _transactions.TryGetValue(key, out var value)
            ? value
            : throw new InvalidDataException("auth.login-transaction-unknown");

    private static void RequireGeneration(LoginTransactionSnapshot transaction, ulong generation)
    {
        if (generation == 0 || generation != transaction.MasterGeneration)
            throw new InvalidDataException("auth.master-changed");
    }

    private static void RequireAuthDomain(AuthDomainWireV1 domain)
    {
        if ((int)domain is not (1 or 2))
            throw new InvalidDataException("auth.invalid-domain");
    }

    private static byte[] RequireId128(ReadOnlySpan<byte> value, string field)
    {
        if (value.Length != 16 || value.IndexOfAnyExcept((byte)0) < 0)
            throw new InvalidDataException($"protocol.invalid-id:{field}");
        return value.ToArray();
    }

    private static string Hex(ReadOnlySpan<byte> value) => Convert.ToHexStringLower(value);

    private static LoginTransactionSnapshot Clone(LoginTransactionSnapshot value)
        => value with
        {
            LoginTransactionId = value.LoginTransactionId.ToArray(),
            ConnectedGatewayId = value.ConnectedGatewayId.ToArray(),
            StateDigest = value.StateDigest.ToArray(),
            NonceDigest = value.NonceDigest.ToArray(),
        };
}
