using MachiVerse.Protocol.V1;

namespace MachiVerse.Gateway.Authorization;

public static class PermissionRegistry
{
    public static readonly IReadOnlyList<string> GeneralViewPermissions = Array.AsReadOnly(new[]
    {
        "view.operation.administration",
        "view.operation.diver",
        "view.operation.moderation",
        "view.participation.bind",
        "view.participation.policy.write",
        "view.session.read.self",
        "view.world.read.participant",
        "view.world.read.public",
        "view.world.subscribe",
    });

    public static readonly IReadOnlyList<string> AdminViewPermissions = Array.AsReadOnly(new[]
    {
        "admin.audit.read",
        "admin.command.execute.high-impact",
        "admin.command.execute.low-impact",
        "admin.config.read",
        "admin.config.write.operational",
        "admin.config.write.presentation",
        "admin.config.write.simulation",
        "admin.health.read",
        "admin.log.read",
        "admin.metrics.read",
        "admin.operation.submit",
        "admin.security.revoke-session",
        "admin.session.read",
    });

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> GeneralRolePermissions =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["view.spectator"] = Permissions(
                "view.session.read.self",
                "view.world.read.public",
                "view.world.subscribe"),
            ["view.diver"] = Permissions(
                "view.operation.diver",
                "view.participation.bind",
                "view.participation.policy.write",
                "view.session.read.self",
                "view.world.read.participant",
                "view.world.read.public",
                "view.world.subscribe"),
            ["view.moderator"] = Permissions(
                "view.operation.diver",
                "view.operation.moderation",
                "view.session.read.self",
                "view.world.read.participant",
                "view.world.read.public",
                "view.world.subscribe"),
            ["view.administrator"] = Permissions(
                "view.operation.administration",
                "view.operation.diver",
                "view.operation.moderation",
                "view.participation.bind",
                "view.participation.policy.write",
                "view.session.read.self",
                "view.world.read.participant",
                "view.world.read.public",
                "view.world.subscribe"),
        };

    static PermissionRegistry()
    {
        ValidateCanonical(GeneralViewPermissions, "general-view registry");
        ValidateCanonical(AdminViewPermissions, "admin-view registry");
        foreach (var (role, permissions) in GeneralRolePermissions)
        {
            RequireStableToken(role, "role");
            ValidateCanonical(permissions, role);
            if (permissions.Any(permission => !GeneralViewPermissions.Contains(permission, StringComparer.Ordinal)))
                throw new InvalidOperationException($"General role contains non-General permission: {role}.");
            if (permissions.Any(permission => permission.StartsWith("admin.", StringComparison.Ordinal)))
                throw new InvalidOperationException($"General role crossed Admin permission domain: {role}.");
        }
    }

    public static IReadOnlyList<string> ResolveGeneralRole(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        return GeneralRolePermissions.TryGetValue(role, out var permissions)
            ? permissions.ToArray()
            : throw new InvalidDataException("auth.role-unknown");
    }

    public static IReadOnlyList<string> ValidateExplicitAdminPermissions(IEnumerable<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        var canonical = permissions.OrderBy(static permission => permission, StringComparer.Ordinal).ToArray();
        if (canonical.Length > 1024) throw new InvalidDataException("auth.permission-limit");
        if (canonical.Distinct(StringComparer.Ordinal).Count() != canonical.Length)
            throw new InvalidDataException("auth.permission-duplicate");
        foreach (var permission in canonical)
        {
            RequireStableToken(permission, "permission");
            if (!AdminViewPermissions.Contains(permission, StringComparer.Ordinal))
                throw new InvalidDataException("auth.permission-unknown");
        }
        return canonical;
    }

    public static void ValidateSessionProjection(AuthSessionStateV1 session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.SessionId.Length != 16 || session.SessionId.Span.IndexOfAnyExcept((byte)0) < 0)
            throw new InvalidDataException("auth.unauthenticated");
        if (session.SessionGeneration == 0)
            throw new InvalidDataException("auth.session-stale");
        if ((int)session.Status != 1)
            throw new InvalidDataException((int)session.Status switch
            {
                3 => "auth.session-revoked",
                4 => "auth.session-expired",
                _ => "auth.session-stale",
            });

        var permissions = session.EffectivePermissions.ToArray();
        ValidateCanonical(permissions, "session effective permissions");
        switch ((int)session.AuthDomain)
        {
            case 1:
                if (!GeneralRolePermissions.ContainsKey(session.EffectiveRoleSet))
                    throw new InvalidDataException("auth.role-unknown");
                if (permissions.Any(permission => !GeneralViewPermissions.Contains(permission, StringComparer.Ordinal)))
                    throw new InvalidDataException("auth.role-domain-mismatch");
                break;
            case 2:
                if (permissions.Any(permission => !AdminViewPermissions.Contains(permission, StringComparer.Ordinal)))
                    throw new InvalidDataException("auth.role-domain-mismatch");
                break;
            default:
                throw new InvalidDataException("auth.invalid-domain");
        }
    }

    public static void RequireStableToken(string value, string field)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 64 ||
            value[0] is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') ||
            value.Any(static ch => !(
                ch is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '/' or '-')))
            throw new InvalidDataException($"auth.invalid-stable-token:{field}");
    }

    private static IReadOnlyList<string> Permissions(params string[] values)
        => Array.AsReadOnly(values.OrderBy(static value => value, StringComparer.Ordinal).ToArray());

    private static void ValidateCanonical(IReadOnlyList<string> values, string source)
    {
        if (values.Count > 1024) throw new InvalidOperationException($"Permission limit exceeded: {source}.");
        string? previous = null;
        foreach (var value in values)
        {
            RequireStableToken(value, "permission");
            if (previous is not null && string.CompareOrdinal(previous, value) >= 0)
                throw new InvalidDataException("auth.permission-order-invalid");
            previous = value;
        }
    }
}
