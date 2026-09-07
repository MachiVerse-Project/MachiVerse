using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using Google.Protobuf;
using MachiVerse.Gateway.State;
using MachiVerse.Protocol.V1;

namespace MachiVerse.Gateway.Protocol;

/// <summary>
/// INT-01 local-only Gateway -> General View bridge. This is intentionally guarded by the
/// local Alpha host configuration and must not be treated as the release browser auth/TLS path.
/// It exercises the canonical mv.gateway-view envelope, login, subscription, and confirmed
/// publication contracts without introducing a second state authority.
/// </summary>
public sealed class AlphaViewBridge(
    AlphaCoreLinkOptions coreOptions,
    AlphaCoreLinkState coreLinkState,
    ConfirmedProjectionCache confirmedCache)
{
    private const string ProtocolId = "mv.gateway-view";
    private const string ProjectionProfile = "standard";
    private readonly AlphaCoreLinkOptions _coreOptions = coreOptions ?? throw new ArgumentNullException(nameof(coreOptions));
    private readonly AlphaCoreLinkState _coreLinkState = coreLinkState ?? throw new ArgumentNullException(nameof(coreLinkState));
    private readonly ConfirmedProjectionCache _confirmedCache = confirmedCache ?? throw new ArgumentNullException(nameof(confirmedCache));

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
        if (!core.Enabled || !string.Equals(core.Status, "ready", StringComparison.Ordinal) ||
            core.WorldId is null || _confirmedCache.Current is null)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        try
        {
            var helloEnvelope = await ReceiveEnvelopeAsync(socket, context.RequestAborted);
            ValidateBootstrapHello(helloEnvelope);
            var hello = ProtocolHelloV1.Parser.ParseFrom(helloEnvelope.Payload);
            ValidateHello(hello);

            var accept = new ProtocolAcceptV1
            {
                NegotiatedVersion = new ProtocolVersionV1 { Major = 1, Minor = 0 },
                NegotiationGeneration = 1,
            };
            accept.EffectiveOptionalCapabilities.Add("protocol.protobuf.v1");
            await SendAsync(socket, BootstrapResponse(helloEnvelope, "protocol.accept", "protocol.accept.v1", accept), context.RequestAborted);

            var loginEnvelope = await ReceiveNormalAsync(socket, context.RequestAborted, "auth.login");
            var login = AuthLoginBeginV1.Parser.ParseFrom(loginEnvelope.Payload);
            if ((int)login.AuthDomain != 1)
                throw new InvalidDataException("auth.unauthorized: Alpha View bridge accepts GENERAL_VIEW only.");

            var sessionId = RandomId128();
            var loginResult = new AuthLoginResultV1
            {
                Result = Success("auth.login.local-alpha"),
                SessionId = sessionId,
                SessionGeneration = 1,
            };
            await SendAsync(socket, NormalResponse(loginEnvelope, "auth.login.result", "protocol.auth-login-result", loginResult), context.RequestAborted);

            var sessionState = new AuthSessionStateV1
            {
                SessionId = sessionId,
                AuthDomain = (AuthDomainWireV1)1,
                EffectiveRoleSet = "general-view.alpha",
                SessionGeneration = 1,
                Status = (SessionWireStatusV1)1,
            };
            sessionState.EffectivePermissions.Add("view.world.read");
            await SendAsync(socket, Notification("auth.session.changed", "protocol.auth-session-state", sessionState), context.RequestAborted);

            while (socket.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
            {
                var request = await ReceiveNormalAsync(socket, context.RequestAborted);
                switch (request.MessageType)
                {
                    case "world.subscribe":
                    {
                        var subscription = ViewSubscriptionRequestV1.Parser.ParseFrom(request.Payload);
                        ValidateSubscription(subscription);
                        await SendConfirmedFullAsync(socket, subscription.SubscriptionId, request, context.RequestAborted);
                        break;
                    }
                    case "world.state.resync-request":
                    {
                        _ = StateResyncRequestV1.Parser.ParseFrom(request.Payload);
                        await SendConfirmedFullAsync(socket, RandomId128(), request, context.RequestAborted);
                        break;
                    }
                    default:
                        throw new InvalidDataException($"protocol.unexpected-view-message:{request.MessageType}");
                }
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
        }
        catch (WebSocketException)
        {
        }
        catch (Exception ex)
        {
            if (socket.State == WebSocketState.Open)
            {
                var description = ex.Message.Length <= 120 ? ex.Message : ex.Message[..120];
                await socket.CloseAsync(WebSocketCloseStatus.ProtocolError, description, CancellationToken.None);
            }
        }
    }

    private async Task SendConfirmedFullAsync(
        WebSocket socket,
        ByteString subscriptionId,
        WireEnvelopeV1 request,
        CancellationToken cancellationToken)
    {
        RequireId128(subscriptionId, "subscription_id");
        var snapshot = _confirmedCache.Current ?? throw new InvalidDataException("component.core-confirmed-state-unavailable");
        var link = _coreLinkState.Current;
        if (link.WorldId is null) throw new InvalidDataException("world.not-found");
        var worldId = ByteString.CopyFrom(Convert.FromHexString(link.WorldId));

        var publicationId = RandomId128();
        var payload = new ProjectionChunkPayloadV1
        {
            SubscriptionId = subscriptionId,
            PublicationId = publicationId,
            ChunkIndex = 0,
        };
        foreach (var item in snapshot.Records.OrderBy(static item => item.Key.SchemaId, StringComparer.Ordinal)
                     .ThenBy(static item => item.Key.RecordIdHex, StringComparer.Ordinal))
        {
            var record = item.Value;
            payload.Records.Add(new ProjectionRecordV1
            {
                RecordSchemaId = record.SchemaId,
                RecordSchemaVersion = new SchemaVersionWireV1 { Major = 1, Minor = 0 },
                RecordId = ByteString.CopyFrom(record.RecordId),
                RecordRevision = record.Revision,
                MutationKind = (ProjectionMutationKindV1)1,
                Payload = ByteString.CopyFrom(record.Payload),
            });
        }

        var payloadBytes = payload.ToByteArray();
        if (payloadBytes.Length > 1024 * 1024)
            throw new InvalidDataException("protocol.limit-exceeded: Alpha bridge currently requires one <=1MiB projection chunk.");

        var publication = new StatePublicationV1
        {
            PublicationId = publicationId,
            Kind = (PublicationKindV1)1,
            StateContinuityToken = ByteString.CopyFrom(snapshot.ContinuityToken),
            ChunkCount = 1,
            ProjectionSchemaDigest = ByteString.CopyFrom(snapshot.ProjectionSchemaDigest),
        };
        var world = new WorldContextWireV1
        {
            WorldId = worldId,
            BasisStep = snapshot.BasisStep,
        };
        await SendAsync(socket, NormalResponse(
            request,
            "world.state.begin",
            "protocol.state-publication",
            publication,
            world), cancellationToken);

        var chunk = new StatePublicationChunkV1
        {
            PublicationId = publicationId,
            ChunkIndex = 0,
            ChunkCount = 1,
            UncompressedPayloadDigest = ByteString.CopyFrom(SHA256.HashData(payloadBytes)),
            Compression = (CompressionKindV1)1,
            Payload = ByteString.CopyFrom(payloadBytes),
        };
        await SendAsync(socket, NormalResponse(
            request,
            "world.state.chunk",
            "protocol.state-publication-chunk",
            chunk,
            world), cancellationToken);
    }

    private WireEnvelopeV1 BootstrapResponse(WireEnvelopeV1 request, string messageType, string schema, IMessage payload)
        => CreateEnvelope(request, messageType, schema, payload, bootstrap: true, world: null);

    private WireEnvelopeV1 NormalResponse(
        WireEnvelopeV1 request,
        string messageType,
        string schema,
        IMessage payload,
        WorldContextWireV1? world = null)
        => CreateEnvelope(request, messageType, schema, payload, bootstrap: false, world);

    private WireEnvelopeV1 Notification(string messageType, string schema, IMessage payload)
    {
        var envelope = new WireEnvelopeV1
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
        return envelope;
    }

    private WireEnvelopeV1 CreateEnvelope(
        WireEnvelopeV1 request,
        string messageType,
        string schema,
        IMessage payload,
        bool bootstrap,
        WorldContextWireV1? world)
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
            WorldContext = world,
        };
        return envelope;
    }

    private static ResultV1 Success(string code)
        => new()
        {
            Status = (ResultStatusV1)1,
            Code = code,
            RetryAdvice = (RetryAdviceV1)1,
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
                throw new WebSocketException("View closed the WebSocket connection.");
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
            throw new InvalidDataException($"protocol.unexpected-view-message:{envelope.MessageType}");
        return envelope;
    }

    private static void ValidateBootstrapHello(WireEnvelopeV1 envelope)
    {
        if (envelope.EnvelopeVersion != 1 || !string.Equals(envelope.ProtocolId, ProtocolId, StringComparison.Ordinal) ||
            envelope.ProtocolVersion is null || envelope.ProtocolVersion.Major != 0 || envelope.ProtocolVersion.Minor != 0 ||
            envelope.NegotiationGeneration != 0 || !string.Equals(envelope.MessageType, "protocol.hello", StringComparison.Ordinal) ||
            !string.Equals(envelope.PayloadSchemaId, "protocol.hello.v1", StringComparison.Ordinal))
            throw new InvalidDataException("protocol.malformed: invalid mv.gateway-view bootstrap hello.");
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

    private static void ValidateSubscription(ViewSubscriptionRequestV1 subscription)
    {
        RequireId128(subscription.SubscriptionId, "subscription_id");
        if (!string.Equals(subscription.ProjectionProfile, ProjectionProfile, StringComparison.Ordinal))
            throw new InvalidDataException("request.invalid: unsupported Alpha projection profile.");
    }

    private static void RequireId128(ByteString value, string field)
    {
        if (value.Length != 16 || value.Span.IndexOfAnyExcept((byte)0) < 0)
            throw new InvalidDataException($"protocol.invalid-id:{field}");
    }

    private static ByteString RandomId128()
    {
        var bytes = new byte[16];
        do RandomNumberGenerator.Fill(bytes); while (bytes.AsSpan().IndexOfAnyExcept((byte)0) < 0);
        return ByteString.CopyFrom(bytes);
    }

    private static bool IsLoopback(IPAddress? address)
        => address is not null && IPAddress.IsLoopback(address);
}
