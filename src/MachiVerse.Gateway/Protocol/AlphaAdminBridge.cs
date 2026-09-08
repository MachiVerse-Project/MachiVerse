using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using Google.Protobuf;
using MachiVerse.Gateway.Audit;
using MachiVerse.Gateway.Configuration;
using MachiVerse.Protocol.V1;

namespace MachiVerse.Gateway.Protocol;

/// <summary>
/// INT-01 loopback-only Gateway -> Administration View bridge.
/// It exercises the canonical Admin protocol while keeping the local Alpha auth profile isolated
/// from the production OIDC/BFF and TLS profile.
/// </summary>
public sealed class AlphaAdminBridge(
    AlphaCoreLinkOptions coreOptions,
    AlphaCoreLinkState coreLinkState,
    AlphaGatewayConfigCoordinator configCoordinator,
    ProtectedAdminAuditGateV1 auditGate,
    ILogger<AlphaAdminBridge> logger)
{
    private const string ProtocolId = "mv.gateway-admin-view";
    private readonly AlphaCoreLinkOptions _coreOptions = coreOptions ?? throw new ArgumentNullException(nameof(coreOptions));
    private readonly AlphaCoreLinkState _coreLinkState = coreLinkState ?? throw new ArgumentNullException(nameof(coreLinkState));
    private readonly AlphaGatewayConfigCoordinator _configCoordinator = configCoordinator ?? throw new ArgumentNullException(nameof(configCoordinator));
    private readonly ProtectedAdminAuditGateV1 _auditGate = auditGate ?? throw new ArgumentNullException(nameof(auditGate));
    private readonly ILogger<AlphaAdminBridge> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task HandleAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        if (!IsLoopback(context.Connection.RemoteIpAddress))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var core = _coreLinkState.Current;
        if (!core.Enabled || !string.Equals(core.Status, "ready", StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        try
        {
            var helloEnvelope = await ReceiveEnvelopeAsync(socket, context.RequestAborted);
            ValidateBootstrapHello(helloEnvelope);
            ValidateHello(ProtocolHelloV1.Parser.ParseFrom(helloEnvelope.Payload));

            var accept = new ProtocolAcceptV1
            {
                NegotiatedVersion = new ProtocolVersionV1 { Major = 1, Minor = 0 },
                NegotiationGeneration = 1,
            };
            accept.EffectiveOptionalCapabilities.Add("protocol.protobuf.v1");
            await SendAsync(socket, BootstrapResponse(
                helloEnvelope,
                "protocol.accept",
                "protocol.accept.v1",
                accept), context.RequestAborted);

            var loginEnvelope = await ReceiveNormalAsync(socket, context.RequestAborted, "auth.login");
            var login = AuthLoginBeginV1.Parser.ParseFrom(loginEnvelope.Payload);
            if ((int)login.AuthDomain != 2)
                throw new InvalidDataException("auth.unauthorized: Alpha Admin bridge accepts ADMIN_VIEW only.");

            var sessionId = RandomId128();
            await SendAsync(socket, NormalResponse(
                loginEnvelope,
                "auth.login.result",
                "protocol.auth-login-result",
                new AuthLoginResultV1
                {
                    Result = Success("auth.login.local-alpha-admin"),
                    SessionId = sessionId,
                    SessionGeneration = 1,
                }), context.RequestAborted);

            var sessionState = new AuthSessionStateV1
            {
                SessionId = sessionId,
                AuthDomain = (AuthDomainWireV1)2,
                EffectiveRoleSet = "admin-view.alpha-config",
                SessionGeneration = 1,
                Status = (SessionWireStatusV1)1,
            };
            sessionState.EffectivePermissions.Add("admin.audit.read");
            sessionState.EffectivePermissions.Add("admin.config.change");
            sessionState.EffectivePermissions.Add("admin.config.read");
            sessionState.EffectivePermissions.Add("admin.health.read");
            await SendAsync(socket, Notification(
                "auth.session.changed",
                "protocol.auth-session-state.v1",
                sessionState), context.RequestAborted);

            _logger.LogInformation("Alpha Admin bridge activated ADMIN_VIEW Config session.");
            while (socket.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
            {
                var request = await ReceiveNormalAsync(socket, context.RequestAborted);
                switch (request.MessageType)
                {
                    case "component.health.query":
                        await HandleHealthAsync(socket, request, context.RequestAborted);
                        break;
                    case "config.read":
                        await HandleConfigReadAsync(socket, request, context.RequestAborted);
                        break;
                    case "config.change":
                        await HandleConfigChangeAsync(socket, request, sessionId, context.RequestAborted);
                        break;
                    default:
                        throw new InvalidDataException($"protocol.unexpected-admin-message:{request.MessageType}");
                }
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
        }
        catch (WebSocketException ex)
        {
            _logger.LogInformation("Alpha Admin bridge WebSocket ended: {Message}", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Alpha Admin bridge failed with {ErrorType}: {Message}", ex.GetType().Name, ex.Message);
            if (socket.State == WebSocketState.Open)
            {
                var description = ex.Message.Length <= 120 ? ex.Message : ex.Message[..120];
                await socket.CloseAsync(WebSocketCloseStatus.ProtocolError, description, CancellationToken.None);
            }
        }
    }

    private async Task HandleHealthAsync(WebSocket socket, WireEnvelopeV1 request, CancellationToken cancellationToken)
    {
        var query = HealthQueryV1.Parser.ParseFrom(request.Payload);
        if (query.Targets.Count != 1 || !IsLocalGatewayTarget(query.Targets[0]))
            throw new InvalidDataException("request.invalid: Alpha Admin health query requires the local Gateway target.");

        var now = checked((ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var core = _coreLinkState.Current;
        var health = new ComponentHealthV1
        {
            Target = LocalGatewayTarget(),
            Health = (HealthStateV1)(string.Equals(core.Status, "ready", StringComparison.Ordinal) ? 1 : 2),
        };
        health.Metrics.Add(new MetricSampleV1
        {
            Name = "gateway.core.negotiated",
            Value = new MetricValueV1 { BoolValue = core.Negotiated },
            ObservedAtUnixMillis = now,
        });
        health.Metrics.Add(new MetricSampleV1
        {
            Name = "gateway.core.local-master",
            Value = new MetricValueV1 { BoolValue = core.LocalMaster },
            ObservedAtUnixMillis = now,
        });
        health.Metrics.Add(new MetricSampleV1
        {
            Name = "gateway.config.generation",
            Value = new MetricValueV1 { UintValue = _configCoordinator.Current.Generation },
            ObservedAtUnixMillis = now,
        });
        if (core.BasisStep is { } basisStep)
        {
            health.Metrics.Add(new MetricSampleV1
            {
                Name = "gateway.confirmed.basis-step",
                Value = new MetricValueV1 { UintValue = basisStep },
                ObservedAtUnixMillis = now,
            });
        }
        health.Conditions.Add(new HealthConditionV1
        {
            Code = "gateway.alpha.ready",
            Severity = (HealthConditionSeverityV1)1,
            Diagnostic = "Local Alpha Gateway is connected to Simulation Core.",
        });

        await SendAsync(socket, NormalResponse(
            request,
            "component.health.result",
            "protocol.component-health.v1",
            health), cancellationToken);
    }

    private async Task HandleConfigReadAsync(WebSocket socket, WireEnvelopeV1 request, CancellationToken cancellationToken)
    {
        var read = ConfigReadRequestV1.Parser.ParseFrom(request.Payload);
        if (read.Target is null || !IsLocalGatewayTarget(read.Target))
            throw new InvalidDataException("request.invalid: Alpha Admin config read requires the local Gateway target.");

        var snapshot = _configCoordinator.Current;
        var requested = read.Keys.Count == 0
            ? snapshot.Values.Keys.OrderBy(static key => key, StringComparer.Ordinal).ToArray()
            : read.Keys.Distinct(StringComparer.Ordinal).OrderBy(static key => key, StringComparer.Ordinal).ToArray();
        var result = new ConfigReadResultV1
        {
            Result = Success("config.read.local-alpha"),
            Target = LocalGatewayTarget(),
            ConfigGeneration = snapshot.Generation,
            ConfigDigest = ByteString.CopyFrom(snapshot.Digest),
        };
        foreach (var key in requested)
        {
            if (snapshot.Values.TryGetValue(key, out var value)) result.Entries.Add(UIntEntry(key, value));
        }

        await SendAsync(socket, NormalResponse(
            request,
            "config.read.result",
            "protocol.config-read-result.v1",
            result), cancellationToken);
    }

    private async Task HandleConfigChangeAsync(
        WebSocket socket,
        WireEnvelopeV1 request,
        ByteString sessionId,
        CancellationToken cancellationToken)
    {
        var change = ConfigChangeRequestV1.Parser.ParseFrom(request.Payload);
        if (change.Target is null || !IsLocalGatewayTarget(change.Target))
            throw new InvalidDataException("request.invalid: Alpha Admin Config change requires the local Gateway target.");
        RequireId128(change.OperationId, "operation_id");
        if (change.ImmutablePayloadDigest.Length != 32)
            throw new InvalidDataException("protocol.invalid-hash:immutable_payload_digest");

        AlphaGatewayConfigApplyResult apply;
        try
        {
            var guarded = await _auditGate.ForwardAsync(
                BuildConfigChangeRequestAudit(request, change, sessionId),
                _ => ValueTask.FromResult(_configCoordinator.Apply(change)),
                result => BuildConfigChangeResultAudit(request, change, sessionId, result),
                cancellationToken);
            apply = guarded.Result;
            if (!guarded.ResultAuditCommitted)
                _logger.LogError("Alpha Config change completed but result audit commit failed for correlation {CorrelationId}.",
                    Convert.ToHexStringLower(request.CorrelationId.Span));
        }
        catch (ProtectedAdminAuditUnavailableException)
        {
            var current = _configCoordinator.Current;
            apply = new AlphaGatewayConfigApplyResult(
                Rejected("component.unavailable", retryAdvice: 3),
                current.Generation,
                current.Digest,
                false);
        }

        var payload = new ConfigChangeResultV1
        {
            Result = apply.Result,
            ResultingGeneration = apply.ResultingGeneration,
            ResultingConfigDigest = ByteString.CopyFrom(apply.ResultingConfigDigest),
        };
        var operation = new OperationContextWireV1
        {
            OperationId = change.OperationId,
            OperationPayloadDigest = change.ImmutablePayloadDigest,
        };
        await SendAsync(socket, NormalResponse(
            request,
            "config.change.result",
            "protocol.config-change-result.v1",
            payload,
            operation), cancellationToken);
    }

    private AuditRecordDraftV1 BuildConfigChangeRequestAudit(
        WireEnvelopeV1 envelope,
        ConfigChangeRequestV1 change,
        ByteString sessionId)
        => new(
            "audit.admin.config-change-requested",
            UnixTimeNs(),
            "gateway",
            _coreOptions.ComponentInstanceId.ToByteArray(),
            "alpha-local-admin",
            SHA256.HashData(sessionId.Span),
            change.OperationId.ToByteArray(),
            envelope.CorrelationId.ToByteArray(),
            "gateway",
            null,
            null,
            change.ExpectedBaseGeneration,
            change.ImmutablePayloadDigest.ToByteArray(),
            "requested",
            "config.change.requested",
            null,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["changed-count"] = change.Changes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });

    private AuditRecordDraftV1 BuildConfigChangeResultAudit(
        WireEnvelopeV1 envelope,
        ConfigChangeRequestV1 change,
        ByteString sessionId,
        AlphaGatewayConfigApplyResult apply)
    {
        var applied = (int)apply.Result.Status is 1 or 4;
        return new AuditRecordDraftV1(
            applied ? "audit.admin.config-change-applied" : "audit.admin.config-change-rejected",
            UnixTimeNs(),
            "gateway",
            _coreOptions.ComponentInstanceId.ToByteArray(),
            "alpha-local-admin",
            SHA256.HashData(sessionId.Span),
            change.OperationId.ToByteArray(),
            envelope.CorrelationId.ToByteArray(),
            "gateway",
            null,
            null,
            apply.ResultingGeneration,
            change.ImmutablePayloadDigest.ToByteArray(),
            applied ? "success" : "rejected",
            apply.Result.Code,
            null,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["replayed"] = apply.Replayed ? "true" : "false",
            });
    }

    private static ConfigEntryWireV1 UIntEntry(string key, ulong value)
        => new()
        {
            Key = key,
            EffectiveValue = new ConfigValueWireV1 { UintValue = value },
            Impact = "operational",
            Mutability = AlphaGatewayConfigPolicy.IsRuntimeMutable(key) ? "runtime-safe-alpha" : "read-only-alpha",
            Sensitive = false,
        };

    private ComponentTargetV1 LocalGatewayTarget()
        => new()
        {
            ComponentKind = (ComponentKindV1)2,
            LogicalInstanceId = _coreOptions.GatewayLogicalId,
        };

    private bool IsLocalGatewayTarget(ComponentTargetV1 target)
        => (int)target.ComponentKind == 2 &&
           (!target.HasLogicalInstanceId || target.LogicalInstanceId.Equals(_coreOptions.GatewayLogicalId));

    private WireEnvelopeV1 BootstrapResponse(WireEnvelopeV1 request, string messageType, string schema, IMessage payload)
        => CreateEnvelope(request, messageType, schema, payload, bootstrap: true, operation: null);

    private WireEnvelopeV1 NormalResponse(
        WireEnvelopeV1 request,
        string messageType,
        string schema,
        IMessage payload,
        OperationContextWireV1? operation = null)
        => CreateEnvelope(request, messageType, schema, payload, bootstrap: false, operation);

    private WireEnvelopeV1 Notification(string messageType, string schema, IMessage payload)
        => new()
        {
            EnvelopeVersion = 1,
            ProtocolId = ProtocolId,
            ProtocolVersion = new ProtocolVersionV1 { Major = 1, Minor = 0 },
            NegotiationGeneration = 1,
            MessageType = messageType,
            MessageId = RandomId128(),
            CorrelationId = RandomId128(),
            SenderInstanceId = _coreOptions.ComponentInstanceId,
            PayloadSchemaId = schema,
            PayloadSchemaVersion = new SchemaVersionWireV1 { Major = 1, Minor = 0 },
            PayloadCompression = (CompressionKindV1)1,
            Payload = payload.ToByteString(),
        };

    private WireEnvelopeV1 CreateEnvelope(
        WireEnvelopeV1 request,
        string messageType,
        string schema,
        IMessage payload,
        bool bootstrap,
        OperationContextWireV1? operation)
    {
        var envelope = new WireEnvelopeV1
        {
            EnvelopeVersion = 1,
            ProtocolId = ProtocolId,
            ProtocolVersion = new ProtocolVersionV1 { Major = bootstrap ? 0u : 1u, Minor = 0 },
            NegotiationGeneration = bootstrap ? 0u : 1u,
            MessageType = messageType,
            MessageId = RandomId128(),
            CorrelationId = request.CorrelationId,
            CausationId = request.MessageId,
            SenderInstanceId = _coreOptions.ComponentInstanceId,
            PayloadSchemaId = schema,
            PayloadSchemaVersion = new SchemaVersionWireV1 { Major = 1, Minor = 0 },
            PayloadCompression = (CompressionKindV1)1,
            Payload = payload.ToByteString(),
        };
        if (operation is not null) envelope.OperationContext = operation;
        return envelope;
    }

    private static ResultV1 Success(string code)
        => new()
        {
            Status = (ResultStatusV1)1,
            Code = code,
            RetryAdvice = (RetryAdviceV1)1,
        };

    private static ResultV1 Rejected(string code, int retryAdvice = 1)
        => new()
        {
            Status = (ResultStatusV1)6,
            Code = code,
            RetryAdvice = (RetryAdviceV1)retryAdvice,
        };

    private static async Task SendAsync(WebSocket socket, WireEnvelopeV1 envelope, CancellationToken cancellationToken)
    {
        var bytes = envelope.ToByteArray();
        if (bytes.Length > WireEnvelopeValidator.MaxSerializedEnvelopeBytes)
            throw new InvalidDataException("protocol.limit-exceeded: envelope exceeds 8 MiB.");
        await socket.SendAsync(bytes, WebSocketMessageType.Binary, endOfMessage: true, cancellationToken);
    }

    private static async Task<WireEnvelopeV1> ReceiveEnvelopeAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        using var stream = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new WebSocketException("Admin View closed the WebSocket connection.");
            if (result.MessageType != WebSocketMessageType.Binary)
                throw new InvalidDataException("protocol.malformed: binary WebSocket message required.");
            stream.Write(buffer, 0, result.Count);
            if (stream.Length > WireEnvelopeValidator.MaxSerializedEnvelopeBytes)
                throw new InvalidDataException("protocol.limit-exceeded: envelope exceeds 8 MiB.");
            if (result.EndOfMessage) break;
        }
        return WireEnvelopeV1.Parser.ParseFrom(stream.ToArray());
    }

    private static async Task<WireEnvelopeV1> ReceiveNormalAsync(
        WebSocket socket,
        CancellationToken cancellationToken,
        string? expectedType = null)
    {
        var envelope = await ReceiveEnvelopeAsync(socket, cancellationToken);
        if (envelope.EnvelopeVersion != 1 || !string.Equals(envelope.ProtocolId, ProtocolId, StringComparison.Ordinal) ||
            envelope.ProtocolVersion is null || envelope.ProtocolVersion.Major != 1 || envelope.ProtocolVersion.Minor != 0 ||
            envelope.NegotiationGeneration != 1)
            throw new InvalidDataException("protocol.negotiation-stale");
        RequireId128(envelope.MessageId, "message_id");
        RequireId128(envelope.CorrelationId, "correlation_id");
        RequireId128(envelope.SenderInstanceId, "sender_instance_id");
        if ((int)envelope.PayloadCompression != 1)
            throw new InvalidDataException("protocol.capability-missing");
        if (expectedType is not null && !string.Equals(envelope.MessageType, expectedType, StringComparison.Ordinal))
            throw new InvalidDataException($"protocol.unexpected-admin-message:{envelope.MessageType}");
        return envelope;
    }

    private static void ValidateBootstrapHello(WireEnvelopeV1 envelope)
    {
        if (envelope.EnvelopeVersion != 1 || !string.Equals(envelope.ProtocolId, ProtocolId, StringComparison.Ordinal) ||
            envelope.ProtocolVersion is null || envelope.ProtocolVersion.Major != 0 || envelope.ProtocolVersion.Minor != 0 ||
            envelope.NegotiationGeneration != 0 || !string.Equals(envelope.MessageType, "protocol.hello", StringComparison.Ordinal) ||
            !string.Equals(envelope.PayloadSchemaId, "protocol.hello.v1", StringComparison.Ordinal))
            throw new InvalidDataException("protocol.malformed: invalid mv.gateway-admin-view bootstrap hello.");
        RequireId128(envelope.MessageId, "message_id");
        RequireId128(envelope.CorrelationId, "correlation_id");
        RequireId128(envelope.SenderInstanceId, "sender_instance_id");
    }

    private static void ValidateHello(ProtocolHelloV1 hello)
    {
        if (!string.Equals(hello.ProtocolId, ProtocolId, StringComparison.Ordinal))
            throw new InvalidDataException("protocol.wrong-protocol");
        if (!hello.SupportedVersions.Any(static range => range.Major == 1 && range.MinMinor <= 0 && range.MaxMinor >= 0))
            throw new InvalidDataException("protocol.version-incompatible");
        if (!hello.ProvidedCapabilities.Contains("protocol.protobuf.v1") ||
            !hello.RequiredCapabilities.Contains("protocol.protobuf.v1"))
            throw new InvalidDataException("protocol.capability-missing");
    }

    private static void RequireId128(ByteString value, string field)
    {
        if (value.Length != 16 || value.Span.IndexOfAnyExcept((byte)0) < 0)
            throw new InvalidDataException($"protocol.invalid-id:{field}");
    }

    private static ByteString RandomId128()
    {
        Span<byte> bytes = stackalloc byte[16];
        do RandomNumberGenerator.Fill(bytes); while (bytes.IndexOfAnyExcept((byte)0) < 0);
        return ByteString.CopyFrom(bytes);
    }

    private static long UnixTimeNs()
        => checked(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000L);

    private static bool IsLoopback(IPAddress? address)
        => address is not null && IPAddress.IsLoopback(address);
}
