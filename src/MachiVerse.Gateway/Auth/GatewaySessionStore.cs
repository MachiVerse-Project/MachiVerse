using MachiVerse.Protocol.V1;

namespace MachiVerse.Gateway.Auth;

public enum GatewaySessionStatus
{
    Active = 1,
    ReauthRequired = 2,
    Revoked = 3,
    Expired = 4,
}

public sealed record GatewaySessionSnapshot(
    byte[] SessionId,
    byte[] AccountId,
    byte[]? DiverRef,
    AuthDomainWireV1 AuthDomain,
    string EffectiveRoleSet,
    IReadOnlyList<string> EffectivePermissions,
    ulong IssuedMasterGeneration,
    ulong SessionGeneration,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSecurityEventAt,
    DateTimeOffset LastSeenAt,
    GatewaySessionStatus Status);

public sealed record NewGatewaySession(
    GatewaySessionSnapshot Session,
    string SessionHandle);

public sealed class GatewaySessionStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, StoredSession> _bySessionId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _sessionIdByHandleDigest = new(StringComparer.Ordinal);
    private readonly int _idleLifetimeSeconds;
    private readonly int _absoluteLifetimeSeconds;
    private readonly int _maxActiveSessionsPerAccount;

    public GatewaySessionStore(
        int idleLifetimeSeconds,
        int absoluteLifetimeSeconds,
        int maxActiveSessionsPerAccount)
    {
        if (idleLifetimeSeconds is < 300 or > 86400) throw new ArgumentOutOfRangeException(nameof(idleLifetimeSeconds));
        if (absoluteLifetimeSeconds is < 900 or > 604800 || absoluteLifetimeSeconds < idleLifetimeSeconds)
            throw new ArgumentOutOfRangeException(nameof(absoluteLifetimeSeconds));
        if (maxActiveSessionsPerAccount is < 1 or > 1024) throw new ArgumentOutOfRangeException(nameof(maxActiveSessionsPerAccount));
        _idleLifetimeSeconds = idleLifetimeSeconds;
        _absoluteLifetimeSeconds = absoluteLifetimeSeconds;
        _maxActiveSessionsPerAccount = maxActiveSessionsPerAccount;
    }

    public NewGatewaySession Create(
        ReadOnlySpan<byte> accountId,
        ReadOnlySpan<byte> diverRef,
        AuthDomainWireV1 authDomain,
        string effectiveRoleSet,
        IEnumerable<string> effectivePermissions,
        ulong issuedMasterGeneration,
        DateTimeOffset now)
    {
        var account = RequireId128(accountId, "account_id");
        var diver = diverRef.IsEmpty ? null : RequireId128(diverRef, "diver_ref");
        RequireAuthDomain(authDomain);
        if (issuedMasterGeneration == 0) throw new InvalidDataException("auth.master-changed");
        ArgumentException.ThrowIfNullOrWhiteSpace(effectiveRoleSet);
        var permissions = NormalizePermissions(effectivePermissions);

        var sessionId = AuthSecurityPrimitives.RandomId128();
        var handleBytes = AuthSecurityPrimitives.RandomSecret256();
        var handle = AuthSecurityPrimitives.Base64UrlEncode(handleBytes);
        var handleDigest = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(handleBytes));
        var sessionIdHex = Convert.ToHexStringLower(sessionId);

        lock (_gate)
        {
            ExpireDueSessions(now);
            var activeForAccount = _bySessionId.Values.Count(item =>
                item.Snapshot.Status == GatewaySessionStatus.Active &&
                item.Snapshot.AccountId.AsSpan().SequenceEqual(account));
            if (activeForAccount >= _maxActiveSessionsPerAccount)
                throw new InvalidDataException("auth.session-limit");

            var snapshot = new GatewaySessionSnapshot(
                sessionId,
                account,
                diver,
                authDomain,
                effectiveRoleSet,
                permissions,
                issuedMasterGeneration,
                SessionGeneration: 1,
                CreatedAt: now,
                LastSecurityEventAt: now,
                LastSeenAt: now,
                Status: GatewaySessionStatus.Active);
            _bySessionId.Add(sessionIdHex, new StoredSession(snapshot, handleDigest));
            _sessionIdByHandleDigest.Add(handleDigest, sessionIdHex);
            return new NewGatewaySession(Clone(snapshot), handle);
        }
    }

    public GatewaySessionSnapshot ResolveHandle(
        string sessionHandle,
        AuthDomainWireV1 expectedDomain,
        ulong? expectedSessionGeneration,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionHandle);
        RequireAuthDomain(expectedDomain);
        var handleDigest = DigestHandle(sessionHandle);

        lock (_gate)
        {
            if (!_sessionIdByHandleDigest.TryGetValue(handleDigest, out var sessionIdHex) ||
                !_bySessionId.TryGetValue(sessionIdHex, out var stored))
                throw new InvalidDataException("auth.unauthenticated");

            var current = RequireUsable(stored.Snapshot, now);
            if ((int)current.AuthDomain != (int)expectedDomain)
                throw new InvalidDataException("auth.unauthorized");
            if (expectedSessionGeneration is { } generation && generation != current.SessionGeneration)
                throw new InvalidDataException("auth.session-stale");

            var touched = current with { LastSeenAt = now };
            _bySessionId[sessionIdHex] = stored with { Snapshot = touched };
            return Clone(touched);
        }
    }

    public GatewaySessionSnapshot Revoke(
        ReadOnlySpan<byte> sessionId,
        ulong expectedSessionGeneration,
        DateTimeOffset now)
    {
        var id = RequireId128(sessionId, "session_id");
        var key = Convert.ToHexStringLower(id);
        lock (_gate)
        {
            if (!_bySessionId.TryGetValue(key, out var stored))
                throw new InvalidDataException("auth.unauthenticated");
            var current = stored.Snapshot;
            if (current.SessionGeneration != expectedSessionGeneration)
                throw new InvalidDataException("auth.session-stale");
            if (current.Status == GatewaySessionStatus.Revoked) return Clone(current);
            if (current.SessionGeneration == ulong.MaxValue) throw new OverflowException("SessionGeneration cannot wrap.");

            var revoked = current with
            {
                SessionGeneration = current.SessionGeneration + 1,
                LastSecurityEventAt = now,
                Status = GatewaySessionStatus.Revoked,
            };
            _bySessionId[key] = stored with { Snapshot = revoked };
            _sessionIdByHandleDigest.Remove(stored.HandleDigest);
            return Clone(revoked);
        }
    }

    public AuthSessionStateV1 ToWireState(GatewaySessionSnapshot session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var wire = new AuthSessionStateV1
        {
            SessionId = Google.Protobuf.ByteString.CopyFrom(session.SessionId),
            AuthDomain = session.AuthDomain,
            EffectiveRoleSet = session.EffectiveRoleSet,
            SessionGeneration = session.SessionGeneration,
            Status = (SessionWireStatusV1)(int)session.Status,
        };
        wire.EffectivePermissions.AddRange(session.EffectivePermissions);
        return wire;
    }

    private GatewaySessionSnapshot RequireUsable(GatewaySessionSnapshot session, DateTimeOffset now)
    {
        if (session.Status == GatewaySessionStatus.Revoked)
            throw new InvalidDataException("auth.session-revoked");
        if (session.Status == GatewaySessionStatus.ReauthRequired)
            throw new InvalidDataException("auth.session-stale");
        if (session.Status == GatewaySessionStatus.Expired || IsExpired(session, now))
        {
            Expire(session, now);
            throw new InvalidDataException("auth.session-expired");
        }
        return session;
    }

    private bool IsExpired(GatewaySessionSnapshot session, DateTimeOffset now)
        => now >= session.CreatedAt.AddSeconds(_absoluteLifetimeSeconds) ||
           now >= session.LastSeenAt.AddSeconds(_idleLifetimeSeconds);

    private void ExpireDueSessions(DateTimeOffset now)
    {
        foreach (var stored in _bySessionId.Values.ToArray())
        {
            if (stored.Snapshot.Status == GatewaySessionStatus.Active && IsExpired(stored.Snapshot, now))
                Expire(stored.Snapshot, now);
        }
    }

    private void Expire(GatewaySessionSnapshot session, DateTimeOffset now)
    {
        var key = Convert.ToHexStringLower(session.SessionId);
        if (!_bySessionId.TryGetValue(key, out var stored)) return;
        var generation = session.SessionGeneration == ulong.MaxValue
            ? session.SessionGeneration
            : session.SessionGeneration + 1;
        var expired = session with
        {
            SessionGeneration = generation,
            LastSecurityEventAt = now,
            Status = GatewaySessionStatus.Expired,
        };
        _bySessionId[key] = stored with { Snapshot = expired };
        _sessionIdByHandleDigest.Remove(stored.HandleDigest);
    }

    private static IReadOnlyList<string> NormalizePermissions(IEnumerable<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        var result = permissions.ToArray();
        if (result.Length > 1024) throw new InvalidDataException("auth.permission-limit");
        foreach (var permission in result)
        {
            if (string.IsNullOrWhiteSpace(permission) || permission.Any(static ch => ch > 0x7f || char.IsWhiteSpace(ch)))
                throw new InvalidDataException("auth.permission-invalid");
        }
        var ordered = result.OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        if (ordered.Distinct(StringComparer.Ordinal).Count() != ordered.Length)
            throw new InvalidDataException("auth.permission-duplicate");
        return ordered;
    }

    private static string DigestHandle(string handle)
    {
        byte[] bytes;
        try
        {
            var normalized = handle.Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
            bytes = Convert.FromBase64String(normalized);
        }
        catch (FormatException)
        {
            throw new InvalidDataException("auth.unauthenticated");
        }
        if (bytes.Length != 32) throw new InvalidDataException("auth.unauthenticated");
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
    }

    private static byte[] RequireId128(ReadOnlySpan<byte> value, string field)
    {
        if (value.Length != 16 || value.IndexOfAnyExcept((byte)0) < 0)
            throw new InvalidDataException($"protocol.invalid-id:{field}");
        return value.ToArray();
    }

    private static void RequireAuthDomain(AuthDomainWireV1 domain)
    {
        if ((int)domain is not (1 or 2)) throw new InvalidDataException("auth.invalid-domain");
    }

    private static GatewaySessionSnapshot Clone(GatewaySessionSnapshot value)
        => value with
        {
            SessionId = value.SessionId.ToArray(),
            AccountId = value.AccountId.ToArray(),
            DiverRef = value.DiverRef?.ToArray(),
            EffectivePermissions = value.EffectivePermissions.ToArray(),
        };

    private sealed record StoredSession(GatewaySessionSnapshot Snapshot, string HandleDigest);
}
