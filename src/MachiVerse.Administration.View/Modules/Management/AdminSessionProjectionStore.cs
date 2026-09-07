using Google.Protobuf;
using MachiVerse.Protocol.V1;

namespace MachiVerse.Administration.View.Modules.Management;

public static class AdminPermissionTokens
{
    public const string OperationSubmit = "admin.operation.submit";
    public const string AuditRead = "admin.audit.read";
    public const string HighImpactCommand = "admin.command.execute.high-impact";
}

public sealed class AdminSessionProjectionStore
{
    private AdminSessionProjection _snapshot = new(
        SessionId: null,
        SessionGeneration: 0,
        State: AdminSessionAccessState.Unavailable,
        EffectiveRoleSet: string.Empty,
        EffectivePermissions: Array.Empty<string>(),
        ReasonCode: "session.not-loaded");

    public AdminSessionProjection Snapshot => _snapshot;

    public event Action<AdminSessionProjection>? Changed;

    public bool TryApply(WireEnvelopeV1 envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (!string.Equals(envelope.MessageType, "auth.session.changed", StringComparison.Ordinal))
        {
            return false;
        }
        if (!string.Equals(envelope.PayloadSchemaId, "protocol.auth-session-state.v1", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"auth.session.changed expected protocol.auth-session-state.v1, received '{envelope.PayloadSchemaId}'.");
        }

        Apply(AuthSessionStateV1.Parser.ParseFrom(envelope.Payload));
        return true;
    }

    public void Apply(AuthSessionStateV1 wire)
    {
        ArgumentNullException.ThrowIfNull(wire);
        ValidateId128(wire.SessionId, nameof(wire.SessionId));
        if ((int)wire.AuthDomain != 2) // AUTH_DOMAIN_ADMIN_VIEW
        {
            throw new InvalidDataException("Admin View requires ADMIN_VIEW auth domain.");
        }
        if (wire.SessionGeneration == 0)
        {
            throw new InvalidDataException("Admin session generation must be non-zero.");
        }

        var permissions = ValidatePermissions(wire.EffectivePermissions);
        if (!string.IsNullOrEmpty(wire.EffectiveRoleSet))
        {
            ValidateStableToken(wire.EffectiveRoleSet, nameof(wire.EffectiveRoleSet));
        }

        _snapshot = new AdminSessionProjection(
            SessionId: Hex(wire.SessionId),
            SessionGeneration: wire.SessionGeneration,
            State: MapStatus(wire.Status),
            EffectiveRoleSet: wire.EffectiveRoleSet,
            EffectivePermissions: permissions,
            ReasonCode: null);
        Changed?.Invoke(_snapshot);
    }

    public void MarkSecurityFailure(string reasonCode)
    {
        ValidateStableToken(reasonCode, nameof(reasonCode));
        var state = reasonCode switch
        {
            "auth.session-revoked" => AdminSessionAccessState.Revoked,
            "auth.session-expired" => AdminSessionAccessState.Expired,
            "auth.session-stale" or "auth.unauthenticated" or "auth.unauthorized" => AdminSessionAccessState.ReauthRequired,
            _ => AdminSessionAccessState.Unavailable,
        };

        _snapshot = _snapshot with { State = state, ReasonCode = reasonCode };
        Changed?.Invoke(_snapshot);
    }

    public void EnsurePermission(string permission)
    {
        ValidateStableToken(permission, nameof(permission));
        if (_snapshot.State != AdminSessionAccessState.Active)
        {
            throw new InvalidOperationException(
                $"Protected Admin request is disabled while session state is '{_snapshot.State}'.");
        }
        if (!_snapshot.EffectivePermissions.Contains(permission, StringComparer.Ordinal))
        {
            throw new UnauthorizedAccessException($"Admin session lacks required permission '{permission}'.");
        }
    }

    private static AdminSessionAccessState MapStatus(SessionWireStatusV1 status)
        => (int)status switch
        {
            1 => AdminSessionAccessState.Active,
            2 => AdminSessionAccessState.ReauthRequired,
            3 => AdminSessionAccessState.Revoked,
            4 => AdminSessionAccessState.Expired,
            _ => AdminSessionAccessState.Unavailable,
        };

    private static IReadOnlyList<string> ValidatePermissions(IEnumerable<string> values)
    {
        var permissions = values.ToArray();
        if (permissions.Length > 1024)
        {
            throw new InvalidDataException("Admin session permission count exceeds 1024.");
        }

        string? previous = null;
        foreach (var permission in permissions)
        {
            ValidateStableToken(permission, "effective_permissions");
            if (previous is not null && string.CompareOrdinal(previous, permission) >= 0)
            {
                throw new InvalidDataException(
                    "Admin effective_permissions must be strictly ASCII-ascending and duplicate-free.");
            }
            previous = permission;
        }
        return permissions;
    }

    internal static void ValidateStableToken(string value, string field)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 64 || !IsLowerAsciiOrDigit(value[0]))
        {
            throw new InvalidDataException($"{field} must be a StableToken.");
        }

        foreach (var ch in value)
        {
            var valid = IsLowerAsciiOrDigit(ch) || ch is '.' or '_' or '/' or '-';
            if (!valid)
            {
                throw new InvalidDataException($"{field} must be a StableToken.");
            }
        }
    }

    internal static string ValidateId128(ByteString value, string field)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length != 16 || IsAllZero(value))
        {
            throw new InvalidDataException($"{field} must be a non-zero Id128.");
        }
        return Hex(value);
    }

    internal static string ValidateHash256(ByteString value, string field)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length != 32)
        {
            throw new InvalidDataException($"{field} must be Hash256.");
        }
        return Hex(value);
    }

    internal static string Hex(ByteString value)
        => Convert.ToHexString(value.ToByteArray()).ToLowerInvariant();

    private static bool IsLowerAsciiOrDigit(char ch)
        => ch is >= 'a' and <= 'z' or >= '0' and <= '9';

    private static bool IsAllZero(ByteString value)
    {
        foreach (var octet in value.Span)
        {
            if (octet != 0)
            {
                return false;
            }
        }
        return true;
    }
}
