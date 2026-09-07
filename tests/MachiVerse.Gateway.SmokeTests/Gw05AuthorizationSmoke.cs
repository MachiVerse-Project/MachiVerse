using Google.Protobuf;
using MachiVerse.Gateway.Auth;
using MachiVerse.Gateway.Authorization;
using MachiVerse.Protocol.V1;

internal static class Gw05AuthorizationSmoke
{
    public static void Run()
    {
        var spectator = PermissionRegistry.ResolveGeneralRole("view.spectator");
        Require(spectator.SequenceEqual(new[]
        {
            "view.session.read.self",
            "view.world.read.public",
            "view.world.subscribe",
        }), "Spectator role matrix mismatch.");

        var administrator = PermissionRegistry.ResolveGeneralRole("view.administrator");
        Require(administrator.Contains("view.operation.administration", StringComparer.Ordinal),
            "General administrator permission missing.");
        Require(!administrator.Any(static permission => permission.StartsWith("admin.", StringComparison.Ordinal)),
            "General administrator must not inherit Admin View permissions.");

        var adminPermissions = PermissionRegistry.ValidateExplicitAdminPermissions(new[]
        {
            "admin.operation.submit",
            "admin.audit.read",
        });
        Require(adminPermissions.SequenceEqual(new[] { "admin.audit.read", "admin.operation.submit" }),
            "Admin permissions must normalize to canonical order.");

        var crossDomainRejected = false;
        try
        {
            PermissionRegistry.ValidateExplicitAdminPermissions(["view.operation.administration"]);
        }
        catch (InvalidDataException ex) when (ex.Message == "auth.permission-unknown")
        {
            crossDomainRejected = true;
        }
        Require(crossDomainRejected, "General permission must not be accepted as Admin permission.");

        var sessionId = Enumerable.Repeat((byte)0x61, 16).ToArray();
        var accountId = Enumerable.Repeat((byte)0x62, 16).ToArray();
        var diverRef = Enumerable.Repeat((byte)0x63, 16).ToArray();
        var store = new GatewaySessionStore(3600, 43200, 16);
        var now = new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero);
        var diverSession = store.CreateMasterGranted(
            sessionId,
            sessionGeneration: 4,
            accountId,
            diverRef,
            (AuthDomainWireV1)1,
            "view.diver",
            PermissionRegistry.ResolveGeneralRole("view.diver"),
            issuedMasterGeneration: 7,
            now).Session;

        var wire = store.ToWireState(diverSession);
        PermissionRegistry.ValidateSessionProjection(wire);

        var registry = new OperationPermissionRegistry(new[]
        {
            new OperationPermissionRegistrationV1(
                "fixture.resident-action",
                (AuthDomainWireV1)1,
                "view.operation.diver"),
            new OperationPermissionRegistrationV1(
                "fixture.admin-operation",
                (AuthDomainWireV1)2,
                "admin.operation.submit"),
        });
        var service = new GatewayAuthorizationService();
        var operationId = Enumerable.Repeat((byte)0x64, 16).ToArray();
        var operation = new StandardOperationV1
        {
            OperationId = ByteString.CopyFrom(operationId),
            ImmutablePayloadDigest = ByteString.CopyFrom(Enumerable.Repeat((byte)0x65, 32).ToArray()),
            OperationKind = "fixture.resident-action",
            Admission = new OperationSchedulingAdmissionWireV1
            {
                AdmissionBasisStep = 10,
                SchedulingPolicyGeneration = 2,
            },
            OperationPayloadSchemaId = "fixture.operation",
            OperationPayloadSchemaVersion = new SchemaVersionWireV1 { Major = 1, Minor = 0 },
            OperationPayload = ByteString.Empty,
        };

        var allowed = service.AuthorizeOperation(diverSession, operation, registry, expectedSessionGeneration: 4);
        Require(allowed.Outcome == AuthorizationOutcomeV1.Allow && allowed.ReasonCode == "auth.allowed",
            "Diver operation permission should be allowed.");
        Require(allowed.OperationId is not null && allowed.OperationId.AsSpan().SequenceEqual(operationId),
            "AuthorizationDecision must retain immutable OperationId correlation.");
        service.RequireAllowed(allowed);

        var retryDecision = service.AuthorizeOperation(diverSession, operation, registry, expectedSessionGeneration: 4);
        Require(retryDecision.OperationId is not null && retryDecision.OperationId.AsSpan().SequenceEqual(operationId),
            "Same logical retry must not change OperationId during authorization evaluation.");

        var stale = service.AuthorizeOperation(diverSession, operation, registry, expectedSessionGeneration: 3);
        Require(stale.Outcome == AuthorizationOutcomeV1.Deny && stale.ReasonCode == "auth.session-stale",
            "Stale session generation must deny new admission.");

        var spectatorSession = store.CreateMasterGranted(
            Enumerable.Repeat((byte)0x66, 16).ToArray(),
            1,
            Enumerable.Repeat((byte)0x67, 16).ToArray(),
            Array.Empty<byte>(),
            (AuthDomainWireV1)1,
            "view.spectator",
            spectator,
            7,
            now).Session;
        var denied = service.AuthorizeOperation(spectatorSession, operation, registry, expectedSessionGeneration: 1);
        Require(denied.Outcome == AuthorizationOutcomeV1.Deny && denied.ReasonCode == "auth.unauthorized",
            "Missing required permission must reject before forwarding.");

        var wrongDomain = service.AuthorizePermission(
            diverSession,
            (AuthDomainWireV1)2,
            "admin.operation.submit",
            "fixture.admin-operation",
            expectedSessionGeneration: 4);
        Require(wrongDomain.Outcome == AuthorizationOutcomeV1.Deny,
            "General View session must not authorize Admin View request.");

        var unregisteredRejected = false;
        operation.OperationKind = "fixture.unregistered";
        try
        {
            _ = service.AuthorizeOperation(diverSession, operation, registry, expectedSessionGeneration: 4);
        }
        catch (InvalidDataException ex) when (ex.Message == "auth.operation-permission-unregistered")
        {
            unregisteredRejected = true;
        }
        Require(unregisteredRejected, "Unregistered OperationKind permission mapping must fail closed.");

        var revoked = store.Revoke(diverSession.SessionId, diverSession.SessionGeneration, now.AddSeconds(1));
        var revokedDecision = service.AuthorizePermission(
            revoked,
            (AuthDomainWireV1)1,
            "view.operation.diver",
            "fixture.resident-action",
            operationId,
            expectedSessionGeneration: revoked.SessionGeneration);
        Require(revokedDecision.Outcome == AuthorizationOutcomeV1.Deny && revokedDecision.ReasonCode == "auth.session-revoked",
            "Revoked session must deny new admission.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
