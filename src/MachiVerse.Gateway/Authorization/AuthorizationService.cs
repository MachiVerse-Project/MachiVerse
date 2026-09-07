using MachiVerse.Gateway.Auth;
using MachiVerse.Protocol.V1;

namespace MachiVerse.Gateway.Authorization;

public enum AuthorizationOutcomeV1
{
    Allow = 1,
    Deny = 2,
}

public sealed record AuthorizationDecisionV1(
    byte[] DecisionId,
    byte[] SessionId,
    ulong SessionGeneration,
    string Permission,
    byte[]? OperationId,
    string TargetKind,
    AuthorizationOutcomeV1 Outcome,
    string ReasonCode)
{
    public AuthorizationDecisionV1 Clone()
        => this with
        {
            DecisionId = DecisionId.ToArray(),
            SessionId = SessionId.ToArray(),
            OperationId = OperationId?.ToArray(),
        };
}

public sealed record OperationPermissionRegistrationV1(
    string OperationKind,
    AuthDomainWireV1 AuthDomain,
    string RequiredPermission);

public sealed class OperationPermissionRegistry
{
    private readonly IReadOnlyDictionary<string, OperationPermissionRegistrationV1> _byKind;

    public OperationPermissionRegistry(IEnumerable<OperationPermissionRegistrationV1> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        var map = new SortedDictionary<string, OperationPermissionRegistrationV1>(StringComparer.Ordinal);
        foreach (var registration in registrations)
        {
            ArgumentNullException.ThrowIfNull(registration);
            PermissionRegistry.RequireStableToken(registration.OperationKind, "operation_kind");
            PermissionRegistry.RequireStableToken(registration.RequiredPermission, "permission");
            var domain = (int)registration.AuthDomain;
            if (domain is not (1 or 2)) throw new InvalidDataException("auth.invalid-domain");
            var allowedRegistry = domain == 1
                ? PermissionRegistry.GeneralViewPermissions
                : PermissionRegistry.AdminViewPermissions;
            if (!allowedRegistry.Contains(registration.RequiredPermission, StringComparer.Ordinal))
                throw new InvalidDataException("auth.role-domain-mismatch");
            if (!map.TryAdd(registration.OperationKind, registration))
                throw new InvalidDataException("auth.operation-permission-duplicate");
        }
        _byKind = map;
    }

    public OperationPermissionRegistrationV1 Require(string operationKind)
        => _byKind.TryGetValue(operationKind, out var registration)
            ? registration
            : throw new InvalidDataException("auth.operation-permission-unregistered");

    public IReadOnlyList<OperationPermissionRegistrationV1> CanonicalEntries => _byKind.Values.ToArray();
}

public sealed class GatewayAuthorizationService
{
    public AuthorizationDecisionV1 AuthorizePermission(
        GatewaySessionSnapshot session,
        AuthDomainWireV1 expectedDomain,
        string requiredPermission,
        string targetKind,
        ReadOnlySpan<byte> operationId = default,
        ulong? expectedSessionGeneration = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        PermissionRegistry.RequireStableToken(requiredPermission, "permission");
        PermissionRegistry.RequireStableToken(targetKind, "target_kind");
        ValidateSessionId(session.SessionId);
        if (expectedSessionGeneration is { } expected && expected != session.SessionGeneration)
            return Deny(session, requiredPermission, targetKind, operationId, "auth.session-stale");
        if (session.Status != GatewaySessionStatus.Active)
            return Deny(session, requiredPermission, targetKind, operationId, session.Status switch
            {
                GatewaySessionStatus.Revoked => "auth.session-revoked",
                GatewaySessionStatus.Expired => "auth.session-expired",
                _ => "auth.session-stale",
            });
        if ((int)session.AuthDomain != (int)expectedDomain)
            return Deny(session, requiredPermission, targetKind, operationId, "auth.unauthorized");
        if (!PermissionBelongsToDomain(requiredPermission, expectedDomain))
            return Deny(session, requiredPermission, targetKind, operationId, "auth.role-domain-mismatch");
        if (!session.EffectivePermissions.Contains(requiredPermission, StringComparer.Ordinal))
            return Deny(session, requiredPermission, targetKind, operationId, "auth.unauthorized");

        return Decision(session, requiredPermission, targetKind, operationId, AuthorizationOutcomeV1.Allow, "auth.allowed");
    }

    public AuthorizationDecisionV1 AuthorizeOperation(
        GatewaySessionSnapshot session,
        StandardOperationV1 operation,
        OperationPermissionRegistry registry,
        ulong? expectedSessionGeneration = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(registry);
        if (operation.OperationId.Length != 16 || operation.OperationId.Span.IndexOfAnyExcept((byte)0) < 0)
            throw new InvalidDataException("protocol.invalid-id:operation_id");
        PermissionRegistry.RequireStableToken(operation.OperationKind, "operation_kind");
        var registration = registry.Require(operation.OperationKind);
        return AuthorizePermission(
            session,
            registration.AuthDomain,
            registration.RequiredPermission,
            operation.OperationKind,
            operation.OperationId.Span,
            expectedSessionGeneration);
    }

    public void RequireAllowed(AuthorizationDecisionV1 decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (decision.Outcome != AuthorizationOutcomeV1.Allow)
            throw new InvalidDataException(decision.ReasonCode);
    }

    private static bool PermissionBelongsToDomain(string permission, AuthDomainWireV1 domain)
        => (int)domain switch
        {
            1 => PermissionRegistry.GeneralViewPermissions.Contains(permission, StringComparer.Ordinal),
            2 => PermissionRegistry.AdminViewPermissions.Contains(permission, StringComparer.Ordinal),
            _ => false,
        };

    private static AuthorizationDecisionV1 Deny(
        GatewaySessionSnapshot session,
        string permission,
        string targetKind,
        ReadOnlySpan<byte> operationId,
        string reason)
        => Decision(session, permission, targetKind, operationId, AuthorizationOutcomeV1.Deny, reason);

    private static AuthorizationDecisionV1 Decision(
        GatewaySessionSnapshot session,
        string permission,
        string targetKind,
        ReadOnlySpan<byte> operationId,
        AuthorizationOutcomeV1 outcome,
        string reason)
    {
        PermissionRegistry.RequireStableToken(reason, "reason_code");
        if (!operationId.IsEmpty && (operationId.Length != 16 || operationId.IndexOfAnyExcept((byte)0) < 0))
            throw new InvalidDataException("protocol.invalid-id:operation_id");
        return new AuthorizationDecisionV1(
            AuthSecurityPrimitives.RandomId128(),
            session.SessionId.ToArray(),
            session.SessionGeneration,
            permission,
            operationId.IsEmpty ? null : operationId.ToArray(),
            targetKind,
            outcome,
            reason);
    }

    private static void ValidateSessionId(byte[] sessionId)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        if (sessionId.Length != 16 || sessionId.AsSpan().IndexOfAnyExcept((byte)0) < 0)
            throw new InvalidDataException("auth.unauthenticated");
    }
}
