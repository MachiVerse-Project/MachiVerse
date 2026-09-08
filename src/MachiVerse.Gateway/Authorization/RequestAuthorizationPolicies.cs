using MachiVerse.Gateway.Auth;
using MachiVerse.Protocol.V1;

namespace MachiVerse.Gateway.Authorization;

public enum AdminConfigImpactV1
{
    Operational = 1,
    Presentation = 2,
    Simulation = 3,
}

public static class RequestAuthorizationPolicies
{
    private static readonly IReadOnlyDictionary<string, string> ViewProjectionPermissions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["view.public.v1"] = "view.world.read.public",
            ["view.participant.v1"] = "view.world.read.participant",
            ["view.moderation.v1"] = "view.operation.moderation",
            ["view.administration.v1"] = "view.operation.administration",
        };

    public static AuthorizationDecisionV1 AuthorizeViewSubscription(
        GatewayAuthorizationService service,
        GatewaySessionSnapshot session,
        string projectionProfile,
        ulong expectedSessionGeneration)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(session);
        PermissionRegistry.RequireStableToken(projectionProfile, "projection_profile");
        if (!ViewProjectionPermissions.TryGetValue(projectionProfile, out var profilePermission))
            throw new InvalidDataException("protocol.projection-unsupported");

        var subscribe = service.AuthorizePermission(
            session,
            (AuthDomainWireV1)1,
            "view.world.subscribe",
            "world.subscribe",
            expectedSessionGeneration: expectedSessionGeneration);
        if (subscribe.Outcome != AuthorizationOutcomeV1.Allow)
            return subscribe;

        return service.AuthorizePermission(
            session,
            (AuthDomainWireV1)1,
            profilePermission,
            "world.subscribe/" + projectionProfile,
            expectedSessionGeneration: expectedSessionGeneration);
    }

    public static AuthorizationDecisionV1 AuthorizeAdminHealthQuery(
        GatewayAuthorizationService service,
        GatewaySessionSnapshot session,
        bool includeMetrics,
        ulong expectedSessionGeneration)
    {
        var health = Admin(service, session, "admin.health.read", "component.health.query", expectedSessionGeneration);
        if (health.Outcome != AuthorizationOutcomeV1.Allow || !includeMetrics)
            return health;
        return Admin(service, session, "admin.metrics.read", "component.health.query/metrics", expectedSessionGeneration);
    }

    public static AuthorizationDecisionV1 AuthorizeAdminLogQuery(
        GatewayAuthorizationService service,
        GatewaySessionSnapshot session,
        ulong expectedSessionGeneration)
        => Admin(service, session, "admin.log.read", "component.log.query", expectedSessionGeneration);

    public static AuthorizationDecisionV1 AuthorizeAdminConfigRead(
        GatewayAuthorizationService service,
        GatewaySessionSnapshot session,
        ulong expectedSessionGeneration)
        => Admin(service, session, "admin.config.read", "config.read", expectedSessionGeneration);

    public static AuthorizationDecisionV1 AuthorizeAdminConfigChange(
        GatewayAuthorizationService service,
        GatewaySessionSnapshot session,
        AdminConfigImpactV1 impact,
        ReadOnlySpan<byte> operationId,
        ulong expectedSessionGeneration)
    {
        var permission = impact switch
        {
            AdminConfigImpactV1.Operational => "admin.config.write.operational",
            AdminConfigImpactV1.Presentation => "admin.config.write.presentation",
            AdminConfigImpactV1.Simulation => "admin.config.write.simulation",
            _ => throw new InvalidDataException("auth.config-impact-unknown"),
        };
        return Admin(service, session, permission, "config.change", expectedSessionGeneration, operationId);
    }

    public static AuthorizationDecisionV1 AuthorizeAdminOperationalCommand(
        GatewayAuthorizationService service,
        GatewaySessionSnapshot session,
        bool highImpact,
        ReadOnlySpan<byte> operationId,
        ulong expectedSessionGeneration)
        => Admin(
            service,
            session,
            highImpact ? "admin.command.execute.high-impact" : "admin.command.execute.low-impact",
            highImpact ? "operational.command/high-impact" : "operational.command/low-impact",
            expectedSessionGeneration,
            operationId);

    public static AuthorizationDecisionV1 AuthorizeAdminAuditQuery(
        GatewayAuthorizationService service,
        GatewaySessionSnapshot session,
        ulong expectedSessionGeneration)
        => Admin(service, session, "admin.audit.read", "audit.query", expectedSessionGeneration);

    public static AuthorizationDecisionV1 AuthorizeAdminOperationSubmit(
        GatewayAuthorizationService service,
        GatewaySessionSnapshot session,
        ReadOnlySpan<byte> operationId,
        ulong expectedSessionGeneration)
        => Admin(service, session, "admin.operation.submit", "operation.submit", expectedSessionGeneration, operationId);

    public static AuthorizationDecisionV1 AuthorizeAdminSessionRead(
        GatewayAuthorizationService service,
        GatewaySessionSnapshot session,
        ulong expectedSessionGeneration)
        => Admin(service, session, "admin.session.read", "admin.session.read", expectedSessionGeneration);

    public static AuthorizationDecisionV1 AuthorizeAdminSessionRevoke(
        GatewayAuthorizationService service,
        GatewaySessionSnapshot session,
        ulong expectedSessionGeneration)
        => Admin(service, session, "admin.security.revoke-session", "admin.session.revoke", expectedSessionGeneration);

    private static AuthorizationDecisionV1 Admin(
        GatewayAuthorizationService service,
        GatewaySessionSnapshot session,
        string permission,
        string targetKind,
        ulong expectedSessionGeneration,
        ReadOnlySpan<byte> operationId = default)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(session);
        return service.AuthorizePermission(
            session,
            (AuthDomainWireV1)2,
            permission,
            targetKind,
            operationId,
            expectedSessionGeneration);
    }
}
