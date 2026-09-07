using Google.Protobuf;
using MachiVerse.Gateway.Configuration;
using MachiVerse.Gateway.State;
using MachiVerse.Protocol.V1;

namespace MachiVerse.Gateway.Auth;

public sealed record MasterAuthorityReply<TPayload>(
    ulong MasterGeneration,
    byte[] SenderGatewayId,
    TPayload Payload);

public interface IMasterLoginAuthorityClient
{
    Task<MasterAuthorityReply<AuthLoginProxyResultV1>> BeginAsync(
        AuthLoginProxyV1 request,
        CancellationToken cancellationToken = default);

    Task<MasterAuthorityReply<AuthLoginResultV1>> FinalizeAsync(
        AuthLoginAssertionV1 request,
        CancellationToken cancellationToken = default);
}

public sealed record MasterAuthorizedLogin(
    LoginAuthorizationMaterial Authorization,
    ulong MasterGeneration);

public sealed class MasterLoginCoordinator(
    MasterAuthorityTracker masterAuthority,
    IMasterLoginAuthorityClient masterClient,
    LoginTransactionStore transactions,
    GatewayOidcConfig config,
    ByteString localGatewayId)
{
    private readonly byte[] _localGatewayId = RequireId128(localGatewayId, "local_gateway_id");

    public async Task<MasterAuthorizedLogin> BeginAsync(
        AuthDomainWireV1 authDomain,
        string? returnPath,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        LoginTransactionStore.ValidateReturnPath(returnPath);
        if ((int)authDomain is not (1 or 2)) throw new InvalidDataException("auth.invalid-domain");

        var authority = RequireAuthority();
        var transactionId = AuthSecurityPrimitives.RandomId128();
        var request = new AuthLoginProxyV1
        {
            LoginTransactionId = ByteString.CopyFrom(transactionId),
            ConnectedGatewayLogicalId = ByteString.CopyFrom(_localGatewayId),
            AuthDomain = authDomain,
            MasterGeneration = authority.MasterGeneration,
        };
        if (returnPath is not null) request.ReturnPath = returnPath;

        var reply = await masterClient.BeginAsync(request, cancellationToken);
        ValidateMasterReply(reply.MasterGeneration, reply.SenderGatewayId, authority.MasterGeneration);
        var payload = reply.Payload ?? throw new InvalidDataException("auth.identity-verification-failed");
        if ((int)payload.Result?.Status! != 1)
            throw new InvalidDataException(payload.Result?.Code ?? "auth.identity-verification-failed");
        if (payload.MasterGeneration != authority.MasterGeneration)
            throw new InvalidDataException("auth.master-changed");
        if (!payload.LoginTransactionId.Span.SequenceEqual(transactionId))
            throw new InvalidDataException("auth.login-transaction-mismatch");

        var material = transactions.BeginMasterAuthorized(
            transactionId,
            _localGatewayId,
            authority.MasterGeneration,
            authDomain,
            config.Issuer,
            returnPath,
            now,
            config.LoginTransactionLifetimeSeconds);
        return new MasterAuthorizedLogin(material, authority.MasterGeneration);
    }

    public string AcceptCallback(
        ReadOnlySpan<byte> loginTransactionId,
        string returnedState,
        DateTimeOffset now)
    {
        var authority = RequireAuthority();
        return transactions.AcceptCallbackAndTakePkceVerifier(
            loginTransactionId,
            returnedState,
            authority.MasterGeneration,
            now);
    }

    public void VerifyNonce(
        ReadOnlySpan<byte> loginTransactionId,
        string verifiedTokenNonce,
        DateTimeOffset now)
        => transactions.VerifyNonce(loginTransactionId, verifiedTokenNonce, now);

    public async Task<AuthLoginResultV1> FinalizeAsync(
        VerifiedIdentityAssertionV1 verifiedAssertion,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ValidateVerifiedAssertion(verifiedAssertion);
        var authority = RequireAuthority();
        var transaction = transactions.BeginMasterFinalization(
            verifiedAssertion.LoginTransactionId.Span,
            authority.MasterGeneration,
            now);
        if (!string.Equals(verifiedAssertion.Issuer, config.Issuer.AbsoluteUri, StringComparison.Ordinal))
        {
            transactions.Reject(transaction.LoginTransactionId);
            throw new InvalidDataException("auth.issuer-untrusted");
        }

        var request = new AuthLoginAssertionV1
        {
            Assertion = verifiedAssertion.Clone(),
            ConnectedGatewayLogicalId = ByteString.CopyFrom(_localGatewayId),
            MasterGeneration = authority.MasterGeneration,
        };

        MasterAuthorityReply<AuthLoginResultV1> reply;
        try
        {
            reply = await masterClient.FinalizeAsync(request, cancellationToken);
            ValidateMasterReply(reply.MasterGeneration, reply.SenderGatewayId, authority.MasterGeneration);
        }
        catch
        {
            transactions.Reject(transaction.LoginTransactionId);
            throw;
        }

        var payload = reply.Payload ?? throw new InvalidDataException("auth.identity-verification-failed");
        if ((int)payload.Result?.Status! != 1)
        {
            transactions.Reject(transaction.LoginTransactionId);
            throw new InvalidDataException(payload.Result?.Code ?? "auth.identity-verification-failed");
        }
        if (!payload.HasSessionId || payload.SessionId.Length != 16 || payload.SessionId.Span.IndexOfAnyExcept((byte)0) < 0 ||
            !payload.HasSessionGeneration || payload.SessionGeneration == 0)
        {
            transactions.Reject(transaction.LoginTransactionId);
            throw new InvalidDataException("auth.identity-verification-failed");
        }

        // Re-read Core-authoritative Master projection after the async round-trip. A success from
        // an old Master is never promoted to current login authority.
        var current = RequireAuthority();
        if (current.MasterGeneration != authority.MasterGeneration)
        {
            transactions.Reject(transaction.LoginTransactionId);
            throw new InvalidDataException("auth.master-changed");
        }

        transactions.Complete(transaction.LoginTransactionId, authority.MasterGeneration, now);
        return payload.Clone();
    }

    private MasterAuthoritySnapshot RequireAuthority()
    {
        var current = masterAuthority.Current ?? throw new InvalidDataException("master.authority-unknown");
        if (current.MasterGeneration == 0 || current.CurrentMasterGatewayId is null)
            throw new InvalidDataException("master.transition-no-authority");
        return current;
    }

    private void ValidateMasterReply(ulong generation, ReadOnlySpan<byte> senderGatewayId, ulong expectedGeneration)
    {
        if (generation != expectedGeneration)
            throw new InvalidDataException("auth.master-changed");
        var sender = ByteString.CopyFrom(RequireId128(senderGatewayId, "sender_gateway_id"));
        masterAuthority.RequireCurrentMaster(generation, sender);
    }

    private static void ValidateVerifiedAssertion(VerifiedIdentityAssertionV1 assertion)
    {
        ArgumentNullException.ThrowIfNull(assertion);
        _ = RequireId128(assertion.LoginTransactionId, "login_transaction_id");
        if (string.IsNullOrWhiteSpace(assertion.Issuer) || string.IsNullOrWhiteSpace(assertion.Subject))
            throw new InvalidDataException("auth.identity-verification-failed");
        if (assertion.VerificationDigest.Length != 32)
            throw new InvalidDataException("auth.identity-verification-failed");
        var methods = assertion.AuthenticationMethods.ToArray();
        if (methods.Any(string.IsNullOrWhiteSpace) ||
            methods.OrderBy(static item => item, StringComparer.Ordinal).Distinct(StringComparer.Ordinal).Count() != methods.Length)
            throw new InvalidDataException("auth.identity-verification-failed");
    }

    private static byte[] RequireId128(ByteString value, string field)
        => RequireId128(value.Span, field);

    private static byte[] RequireId128(ReadOnlySpan<byte> value, string field)
    {
        if (value.Length != 16 || value.IndexOfAnyExcept((byte)0) < 0)
            throw new InvalidDataException($"protocol.invalid-id:{field}");
        return value.ToArray();
    }
}
