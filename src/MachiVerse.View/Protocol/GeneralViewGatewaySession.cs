using System.Security.Cryptography;
using Google.Protobuf;
using MachiVerse.Protocol.V1;
using MachiVerse.View.Configuration;
using MachiVerse.View.State;

namespace MachiVerse.View.Protocol;

/// <summary>
/// Owns the initial General View protocol session. The browser consumes only Gateway-confirmed
/// publications; it does not infer or reconstruct authoritative world state locally.
/// </summary>
public sealed class GeneralViewGatewaySession(
    GeneralViewConfig config,
    GatewayProtocolClient gateway,
    PublicationConsumer publications)
{
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly ByteString _senderInstanceId = RandomId128();
    private bool _started;
    private uint _negotiationGeneration;

    public string? LastError { get; private set; }
    public string? SessionIdHex { get; private set; }
    public string? SubscriptionIdHex { get; private set; }
    public ulong? ConfirmedBasisStep { get; private set; }
    public bool Started => _started;

    public event Action? Changed;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _startGate.WaitAsync(cancellationToken);
        try
        {
            if (_started) return;
            LastError = null;
            Changed?.Invoke();

            await gateway.ConnectAsync(
                config.GatewayEndpoint,
                config.AllowInsecureLoopbackAlpha,
                cancellationToken);

            var hello = new ProtocolHelloV1 { ProtocolId = GatewayEnvelopeCodec.ProtocolId };
            hello.SupportedVersions.Add(new SupportedVersionRangeV1 { Major = 1, MinMinor = 0, MaxMinor = 0 });
            hello.ProvidedCapabilities.Add("protocol.protobuf.v1");
            hello.RequiredCapabilities.Add("protocol.protobuf.v1");
            await gateway.SendBootstrapAsync(BootstrapEnvelope("protocol.hello", "protocol.hello.v1", hello), cancellationToken);

            var bootstrap = await gateway.ReceiveBootstrapAsync(cancellationToken);
            if (string.Equals(bootstrap.MessageType, "protocol.reject", StringComparison.Ordinal))
            {
                var reject = ProtocolRejectV1.Parser.ParseFrom(bootstrap.Payload);
                throw new InvalidDataException($"{reject.Code}:{reject.Diagnostic}");
            }
            if (!string.Equals(bootstrap.MessageType, "protocol.accept", StringComparison.Ordinal))
                throw new InvalidDataException($"protocol.unexpected-bootstrap-response:{bootstrap.MessageType}");
            var accept = ProtocolAcceptV1.Parser.ParseFrom(bootstrap.Payload);
            if (accept.NegotiatedVersion is null || accept.NegotiatedVersion.Major != 1 || accept.NegotiatedVersion.Minor != 0 ||
                accept.NegotiationGeneration == 0)
                throw new InvalidDataException("protocol.version-incompatible");
            _negotiationGeneration = accept.NegotiationGeneration;

            gateway.MarkAuthenticating();
            await gateway.SendAsync(NormalEnvelope(
                "auth.login",
                "protocol.auth-login-request",
                new AuthLoginBeginV1 { AuthDomain = (AuthDomainWireV1)1 }), cancellationToken);

            var loginEnvelope = await RequireNormalAsync("auth.login.result", cancellationToken);
            var login = AuthLoginResultV1.Parser.ParseFrom(loginEnvelope.Payload);
            if (login.Result is null || (int)login.Result.Status != 1 || !login.HasSessionId || !login.HasSessionGeneration)
                throw new InvalidDataException(login.Result?.Code ?? "auth.unauthenticated");
            RequireId128(login.SessionId, "session_id");
            if (login.SessionGeneration == 0) throw new InvalidDataException("auth.session-stale");
            SessionIdHex = Convert.ToHexStringLower(login.SessionId.Span);

            var sessionEnvelope = await RequireNormalAsync("auth.session.changed", cancellationToken);
            var session = AuthSessionStateV1.Parser.ParseFrom(sessionEnvelope.Payload);
            if (!session.SessionId.Equals(login.SessionId) || (int)session.AuthDomain != 1 || (int)session.Status != 1 ||
                session.SessionGeneration != login.SessionGeneration)
                throw new InvalidDataException("auth.session-stale");

            gateway.MarkSyncing();
            publications.BeginSync();
            var worldId = ParseWorldId(config.WorldIdHex);
            var subscriptionId = RandomId128();
            SubscriptionIdHex = Convert.ToHexStringLower(subscriptionId.Span);
            var subscribe = new ViewSubscriptionRequestV1
            {
                SubscriptionId = subscriptionId,
                ProjectionProfile = "standard",
                PreferDelta = true,
            };
            await gateway.SendAsync(NormalEnvelope(
                "world.subscribe",
                "protocol.view-subscription-request",
                subscribe,
                new WorldContextWireV1 { WorldId = worldId }), cancellationToken);

            var confirmed = await ReceivePublicationAsync(worldId, subscriptionId, cancellationToken);
            ConfirmedBasisStep = confirmed.BasisStep;
            gateway.MarkReady();
            _started = true;
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            LastError = BoundedError(ex);
            gateway.MarkDegraded();
            Changed?.Invoke();
            throw;
        }
        finally
        {
            _startGate.Release();
        }
    }

    private async Task<ConfirmedWorldSnapshot> ReceivePublicationAsync(
        ByteString worldId,
        ByteString subscriptionId,
        CancellationToken cancellationToken)
    {
        var beginEnvelope = await RequireNormalAsync("world.state.begin", cancellationToken);
        if (beginEnvelope.WorldContext is null || !beginEnvelope.WorldContext.HasBasisStep ||
            !beginEnvelope.WorldContext.WorldId.Equals(worldId))
            throw new InvalidDataException("protocol.publication-context-mismatch");
        var publication = StatePublicationV1.Parser.ParseFrom(beginEnvelope.Payload);
        if (publication.ChunkCount is 0 or > 65535)
            throw new InvalidDataException("protocol.invalid-publication-chunks");

        var chunks = new List<StatePublicationChunkV1>(checked((int)publication.ChunkCount));
        for (var i = 0; i < publication.ChunkCount; i++)
        {
            var chunkEnvelope = await RequireNormalAsync("world.state.chunk", cancellationToken);
            if (chunkEnvelope.WorldContext is null || !chunkEnvelope.WorldContext.HasBasisStep ||
                chunkEnvelope.WorldContext.BasisStep != beginEnvelope.WorldContext.BasisStep ||
                !chunkEnvelope.WorldContext.WorldId.Equals(worldId))
                throw new InvalidDataException("protocol.publication-context-mismatch");
            var chunk = StatePublicationChunkV1.Parser.ParseFrom(chunkEnvelope.Payload);
            var payload = DecodeProjectionPayload(chunk);
            if (!payload.SubscriptionId.Equals(subscriptionId))
                throw new InvalidDataException("protocol.subscription-id-mismatch");
            chunks.Add(chunk);
        }

        return publications.Consume(publication, beginEnvelope.WorldContext.BasisStep, chunks);
    }

    private static ProjectionChunkPayloadV1 DecodeProjectionPayload(StatePublicationChunkV1 chunk)
    {
        if ((int)chunk.Compression != 1)
            throw new InvalidDataException("protocol.unsupported-compression");
        var bytes = chunk.Payload.ToByteArray();
        if (bytes.Length > 1024 * 1024)
            throw new InvalidDataException("protocol.limit-exceeded:publication-chunk");
        if (chunk.UncompressedPayloadDigest.Length != 32 ||
            !CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), chunk.UncompressedPayloadDigest.Span))
            throw new InvalidDataException("protocol.chunk-digest-mismatch");
        return ProjectionChunkPayloadV1.Parser.ParseFrom(bytes);
    }

    private async Task<WireEnvelopeV1> RequireNormalAsync(string type, CancellationToken cancellationToken)
    {
        var envelope = await gateway.ReceiveAsync(cancellationToken);
        if (envelope.NegotiationGeneration != _negotiationGeneration)
            throw new InvalidDataException("protocol.negotiation-stale");
        if (!string.Equals(envelope.MessageType, type, StringComparison.Ordinal))
            throw new InvalidDataException($"protocol.unexpected-gateway-message:{envelope.MessageType}");
        return envelope;
    }

    private WireEnvelopeV1 BootstrapEnvelope(string type, string schema, IMessage payload)
        => new()
        {
            EnvelopeVersion = 1,
            ProtocolId = GatewayEnvelopeCodec.ProtocolId,
            ProtocolVersion = new ProtocolVersionV1 { Major = 0, Minor = 0 },
            NegotiationGeneration = 0,
            MessageType = type,
            MessageId = RandomId128(),
            CorrelationId = RandomId128(),
            SenderInstanceId = _senderInstanceId,
            PayloadSchemaId = schema,
            PayloadSchemaVersion = new SchemaVersionWireV1 { Major = 1, Minor = 0 },
            PayloadCompression = (CompressionKindV1)1,
            Payload = payload.ToByteString(),
        };

    private WireEnvelopeV1 NormalEnvelope(
        string type,
        string schema,
        IMessage payload,
        WorldContextWireV1? world = null)
    {
        if (_negotiationGeneration == 0) throw new InvalidOperationException("Gateway protocol is not negotiated.");
        var envelope = new WireEnvelopeV1
        {
            EnvelopeVersion = 1,
            ProtocolId = GatewayEnvelopeCodec.ProtocolId,
            ProtocolVersion = new ProtocolVersionV1 { Major = 1, Minor = 0 },
            NegotiationGeneration = _negotiationGeneration,
            MessageType = type,
            MessageId = RandomId128(),
            CorrelationId = RandomId128(),
            SenderInstanceId = _senderInstanceId,
            PayloadSchemaId = schema,
            PayloadSchemaVersion = new SchemaVersionWireV1 { Major = 1, Minor = 0 },
            PayloadCompression = (CompressionKindV1)1,
            Payload = payload.ToByteString(),
        };
        if (world is not null) envelope.WorldContext = world;
        return envelope;
    }

    private static ByteString ParseWorldId(string value)
    {
        var bytes = Convert.FromHexString(value);
        var id = ByteString.CopyFrom(bytes);
        RequireId128(id, "world_id");
        return id;
    }

    private static ByteString RandomId128()
    {
        var bytes = new byte[16];
        do RandomNumberGenerator.Fill(bytes); while (bytes.AsSpan().IndexOfAnyExcept((byte)0) < 0);
        return ByteString.CopyFrom(bytes);
    }

    private static void RequireId128(ByteString value, string field)
    {
        if (value.Length != 16 || value.Span.IndexOfAnyExcept((byte)0) < 0)
            throw new InvalidDataException($"protocol.invalid-id:{field}");
    }

    private static string BoundedError(Exception ex)
    {
        var value = ex is AggregateException aggregate ? aggregate.GetBaseException().Message : ex.Message;
        return value.Length <= 512 ? value : value[..512];
    }
}
