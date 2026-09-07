using System.Security.Cryptography;
using Google.Protobuf;
using Grpc.Core;
using MachiVerse.Gateway.Configuration;
using MachiVerse.Gateway.State;
using MachiVerse.Protocol.V1;

namespace MachiVerse.Gateway.Protocol;

public sealed record AlphaCoreLinkOptions(
    Uri CoreEndpoint,
    ByteString GatewayLogicalId,
    ByteString ComponentInstanceId)
{
    private const string DefaultGatewayLogicalId = "00000000000000000000000000000003";
    private const string DefaultGatewayComponentInstanceId = "00000000000000000000000000000004";

    public static AlphaCoreLinkOptions? TryFromEnvironment()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("MACHIVERSE_ALPHA_LOCAL"), "1", StringComparison.Ordinal))
            return null;

        var endpointText = Environment.GetEnvironmentVariable("MACHIVERSE_CORE_ENDPOINT")
            ?? throw new InvalidOperationException(
                "MACHIVERSE_CORE_ENDPOINT is required when MACHIVERSE_ALPHA_LOCAL=1 for Gateway.");
        if (!Uri.TryCreate(endpointText, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme is not ("http" or "https") ||
            !endpoint.IsLoopback)
        {
            throw new InvalidDataException(
                "MACHIVERSE_CORE_ENDPOINT must be an absolute loopback http/https endpoint for the INT-01 local Alpha.");
        }

        return new AlphaCoreLinkOptions(
            endpoint,
            ParseId("MACHIVERSE_GATEWAY_LOGICAL_ID", DefaultGatewayLogicalId),
            ParseId("MACHIVERSE_GATEWAY_COMPONENT_INSTANCE_ID", DefaultGatewayComponentInstanceId));
    }

    private static ByteString ParseId(string variable, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(variable) ?? fallback;
        if (value.Length != 32 || value.Any(static c => c is >= 'A' and <= 'F'))
            throw new InvalidDataException($"{variable} must be 32 lowercase hexadecimal digits.");
        try
        {
            var bytes = Convert.FromHexString(value);
            if (bytes.AsSpan().IndexOfAnyExcept((byte)0) < 0)
                throw new InvalidDataException($"{variable} cannot be ZERO.");
            return ByteString.CopyFrom(bytes);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException($"{variable} is not canonical hexadecimal.", ex);
        }
    }
}

public sealed record AlphaCoreLinkSnapshot(
    bool Enabled,
    string Status,
    bool Negotiated,
    string SyncState,
    ulong? BasisStep,
    ulong? MasterGeneration,
    bool LocalMaster,
    string? WorldId,
    string? LastError);

public sealed class AlphaCoreLinkState
{
    private AlphaCoreLinkSnapshot _current = new(
        Enabled: false,
        Status: "disabled",
        Negotiated: false,
        SyncState: GatewaySyncState.Starting.ToString(),
        BasisStep: null,
        MasterGeneration: null,
        LocalMaster: false,
        WorldId: null,
        LastError: null);

    public AlphaCoreLinkSnapshot Current => Volatile.Read(ref _current);

    public void Enable()
        => Volatile.Write(ref _current, Current with { Enabled = true, Status = "starting", LastError = null });

    public void MarkConnecting()
        => Volatile.Write(ref _current, Current with { Status = "connecting", LastError = null });

    public void MarkRegistered(ByteString worldId, ProtocolNegotiationState negotiation, MasterAuthorityTracker master)
        => Volatile.Write(ref _current, Current with
        {
            Status = "registered",
            Negotiated = negotiation.IsNegotiated,
            WorldId = Convert.ToHexStringLower(worldId.Span),
            MasterGeneration = master.Current?.MasterGeneration,
            LocalMaster = master.IsLocalMaster,
            LastError = null,
        });

    public void MarkSynced(
        ByteString worldId,
        ProtocolNegotiationState negotiation,
        ResyncCoordinator resync,
        ConfirmedProjectionCache cache,
        MasterAuthorityTracker master)
        => Volatile.Write(ref _current, Current with
        {
            Status = "ready",
            Negotiated = negotiation.IsNegotiated,
            SyncState = resync.State.ToString(),
            BasisStep = cache.Current?.BasisStep,
            MasterGeneration = master.Current?.MasterGeneration,
            LocalMaster = master.IsLocalMaster,
            WorldId = Convert.ToHexStringLower(worldId.Span),
            LastError = null,
        });

    public void MarkDisconnected(string code, ResyncCoordinator resync)
        => Volatile.Write(ref _current, Current with
        {
            Status = "reconnecting",
            Negotiated = false,
            SyncState = resync.State.ToString(),
            LastError = code,
        });
}

public sealed class AlphaCoreConnectionWorker : BackgroundService
{
    private static readonly string[] BaselineCapabilities =
    [
        "protocol.operation-batch.v1",
        "protocol.operation-status.v1",
        "protocol.protobuf.v1",
        "protocol.state-full.v1",
    ];

    private readonly AlphaCoreLinkOptions _options;
    private readonly AlphaGatewayConfigCoordinator _config;
    private readonly ProtocolNegotiationState _negotiation;
    private readonly SchedulingPolicyProjection _scheduling;
    private readonly MasterAuthorityTracker _master;
    private readonly ConfirmedProjectionCache _cache;
    private readonly ResyncCoordinator _resync;
    private readonly AlphaCoreLinkState _linkState;
    private readonly AlphaCoreEnvelopeFactory _envelopes;

    public AlphaCoreConnectionWorker(
        AlphaCoreLinkOptions options,
        AlphaGatewayConfigCoordinator config,
        ProtocolNegotiationState negotiation,
        SchedulingPolicyProjection scheduling,
        MasterAuthorityTracker master,
        ConfirmedProjectionCache cache,
        ResyncCoordinator resync,
        AlphaCoreLinkState linkState)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _negotiation = negotiation ?? throw new ArgumentNullException(nameof(negotiation));
        _scheduling = scheduling ?? throw new ArgumentNullException(nameof(scheduling));
        _master = master ?? throw new ArgumentNullException(nameof(master));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _resync = resync ?? throw new ArgumentNullException(nameof(resync));
        _linkState = linkState ?? throw new ArgumentNullException(nameof(linkState));
        _envelopes = new AlphaCoreEnvelopeFactory(options.ComponentInstanceId, negotiation);
        _linkState.Enable();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var client = new CoreProtocolClient(_options.CoreEndpoint);
        var reconnectDelayMs = RuntimeMilliseconds("network.reconnect-initial-ms");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _linkState.MarkConnecting();
                await RunSessionAsync(client, stoppingToken);
                reconnectDelayMs = RuntimeMilliseconds("network.reconnect-initial-ms");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _negotiation.Reset();
                _resync.MarkSuspect("component.core-disconnected");
                _linkState.MarkDisconnected(BoundedError(ex), _resync);
                await Task.Delay(reconnectDelayMs, stoppingToken);
                reconnectDelayMs = Math.Min(
                    RuntimeMilliseconds("network.reconnect-max-ms"),
                    checked(reconnectDelayMs <= int.MaxValue / 2 ? reconnectDelayMs * 2 : int.MaxValue));
            }
        }
    }

    private async Task RunSessionAsync(CoreProtocolClient client, CancellationToken cancellationToken)
    {
        _negotiation.Reset();
        using var call = client.Connect(cancellationToken);

        var helloEnvelope = _envelopes.Bootstrap(
            "protocol.hello",
            "protocol.hello.v1",
            BuildHello());
        await call.RequestStream.WriteAsync(helloEnvelope);

        if (!await call.ResponseStream.MoveNext(cancellationToken))
            throw new InvalidDataException("component.core-handshake-closed");
        var handshake = call.ResponseStream.Current;
        ValidateBootstrapResponse(handshake, helloEnvelope);
        if (string.Equals(handshake.MessageType, "protocol.reject", StringComparison.Ordinal))
        {
            var reject = ProtocolRejectV1.Parser.ParseFrom(handshake.Payload);
            throw new InvalidDataException($"{reject.Code}:{reject.Diagnostic}");
        }
        if (!string.Equals(handshake.MessageType, "protocol.accept", StringComparison.Ordinal))
            throw new InvalidDataException("protocol.unexpected-bootstrap-response");
        _negotiation.Accept(ProtocolAcceptV1.Parser.ParseFrom(handshake.Payload));

        await call.RequestStream.WriteAsync(_envelopes.Normal(
            "gateway.register",
            "protocol.gateway-register.v1",
            new GatewayRegisterV1
            {
                GatewayLogicalId = _options.GatewayLogicalId,
                ComponentInstanceId = _options.ComponentInstanceId,
                LastKnownMasterGeneration = _master.Current?.MasterGeneration ?? 0,
                Readiness = (GatewayReadinessV1)2,
            }));

        var worldId = await ReadRegistrationStateAsync(call.ResponseStream, cancellationToken);
        _linkState.MarkRegistered(worldId, _negotiation, _master);

        var request = _resync.BeginResync(worldId, forceFull: true);
        await call.RequestStream.WriteAsync(_envelopes.Normal(
            "world.state.resync-request",
            "protocol.state-resync-request.v1",
            request,
            new WorldContextWireV1 { WorldId = worldId }));

        if (!await call.ResponseStream.MoveNext(cancellationToken))
            throw new InvalidDataException("component.core-resync-closed");
        var beginEnvelope = RequireNormal(call.ResponseStream.Current, "world.state.begin");
        await ApplyPublicationAsync(beginEnvelope, call.ResponseStream, cancellationToken);
        _linkState.MarkSynced(worldId, _negotiation, _resync, _cache, _master);

        var heartbeatTask = SendHeartbeatsAsync(call.RequestStream, worldId, cancellationToken);
        try
        {
            while (await call.ResponseStream.MoveNext(cancellationToken))
            {
                var envelope = RequireNormal(call.ResponseStream.Current);
                switch (envelope.MessageType)
                {
                    case "gateway.role-state":
                        ApplyRoleState(envelope);
                        break;
                    case "master.generation.changed":
                        _master.Apply(MasterGenerationStateV1.Parser.ParseFrom(envelope.Payload));
                        break;
                    case "world.scheduling-policy":
                        _scheduling.Apply(OperationSchedulingPolicyWireV1.Parser.ParseFrom(envelope.Payload));
                        break;
                    case "world.state.begin":
                        await ApplyPublicationAsync(envelope, call.ResponseStream, cancellationToken);
                        break;
                    case "component.health":
                        break;
                    default:
                        throw new InvalidDataException($"protocol.unexpected-core-message:{envelope.MessageType}");
                }
                _linkState.MarkSynced(worldId, _negotiation, _resync, _cache, _master);
            }
            throw new InvalidDataException("component.core-stream-closed");
        }
        finally
        {
            try { await heartbeatTask; }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        }
    }

    private async Task<ByteString> ReadRegistrationStateAsync(
        IAsyncStreamReader<WireEnvelopeV1> responseStream,
        CancellationToken cancellationToken)
    {
        ByteString? worldId = null;
        var sawRole = false;
        var sawMaster = false;
        var sawScheduling = false;

        while (!(sawRole && sawMaster && sawScheduling))
        {
            if (!await responseStream.MoveNext(cancellationToken))
                throw new InvalidDataException("component.core-registration-closed");
            var envelope = RequireNormal(responseStream.Current);
            worldId = RequireConsistentWorldId(worldId, envelope);
            switch (envelope.MessageType)
            {
                case "gateway.role-state":
                    ApplyRoleState(envelope);
                    sawRole = true;
                    break;
                case "master.generation.changed":
                    _master.Apply(MasterGenerationStateV1.Parser.ParseFrom(envelope.Payload));
                    sawMaster = true;
                    break;
                case "world.scheduling-policy":
                    _scheduling.Apply(OperationSchedulingPolicyWireV1.Parser.ParseFrom(envelope.Payload));
                    sawScheduling = true;
                    break;
                default:
                    throw new InvalidDataException($"protocol.unexpected-registration-message:{envelope.MessageType}");
            }
        }

        return worldId ?? throw new InvalidDataException("protocol.registration-world-missing");
    }

    private void ApplyRoleState(WireEnvelopeV1 envelope)
    {
        var state = GatewayRoleStateV1.Parser.ParseFrom(envelope.Payload);
        WireEnvelopeValidator.ValidateId128(state.GatewayLogicalId, "gateway_logical_id", allowZero: false);
        if (!state.GatewayLogicalId.Equals(_options.GatewayLogicalId))
            throw new InvalidDataException("auth.gateway-role-identity-mismatch");
        if (state.MasterGeneration == 0)
            throw new InvalidDataException("master.invalid-generation");
    }

    private async Task ApplyPublicationAsync(
        WireEnvelopeV1 beginEnvelope,
        IAsyncStreamReader<WireEnvelopeV1> responseStream,
        CancellationToken cancellationToken)
    {
        if (beginEnvelope.WorldContext is null || !beginEnvelope.WorldContext.HasBasisStep)
            throw new InvalidDataException("protocol.publication-basis-missing");
        var publication = StatePublicationV1.Parser.ParseFrom(beginEnvelope.Payload);
        var chunks = new List<StatePublicationChunkV1>(checked((int)publication.ChunkCount));
        for (var i = 0; i < publication.ChunkCount; i++)
        {
            if (!await responseStream.MoveNext(cancellationToken))
                throw new InvalidDataException("protocol.incomplete-publication");
            var chunkEnvelope = RequireNormal(responseStream.Current, "world.state.chunk");
            if (chunkEnvelope.WorldContext is null || !chunkEnvelope.WorldContext.HasBasisStep ||
                chunkEnvelope.WorldContext.BasisStep != beginEnvelope.WorldContext.BasisStep)
                throw new InvalidDataException("protocol.publication-basis-mismatch");
            chunks.Add(StatePublicationChunkV1.Parser.ParseFrom(chunkEnvelope.Payload));
        }
        _resync.ApplyOrEnterSuspect(publication, beginEnvelope.WorldContext.BasisStep, chunks);
    }

    private async Task SendHeartbeatsAsync(
        IClientStreamWriter<WireEnvelopeV1> requestStream,
        ByteString worldId,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(RuntimeMilliseconds("peer.heartbeat-interval-ms"), cancellationToken);
            var heartbeat = new GatewayHeartbeatV1
            {
                GatewayLogicalId = _options.GatewayLogicalId,
                ComponentInstanceId = _options.ComponentInstanceId,
                Readiness = (GatewayReadinessV1)(_resync.State == GatewaySyncState.Synced ? 3 : 2),
                PeerConnectionCount = 0,
                ViewConnectionCount = 0,
                AdminConnectionCount = 0,
            };
            if (_cache.Current is { } confirmed)
            {
                heartbeat.ConfirmedBasisStep = confirmed.BasisStep;
                heartbeat.ConfirmedContinuityToken = ByteString.CopyFrom(confirmed.ContinuityToken);
            }
            await requestStream.WriteAsync(_envelopes.Normal(
                "gateway.heartbeat",
                "protocol.gateway-heartbeat.v1",
                heartbeat,
                new WorldContextWireV1 { WorldId = worldId }));
        }
    }

    private int RuntimeMilliseconds(string key)
    {
        var snapshot = _config.Current;
        return snapshot.Values.TryGetValue(key, out var value) && value is > 0 and <= int.MaxValue
            ? checked((int)value)
            : throw new InvalidDataException($"config.runtime-value-invalid:{key}");
    }

    private ProtocolHelloV1 BuildHello()
    {
        var hello = new ProtocolHelloV1 { ProtocolId = ProtocolNegotiationState.ProtocolId };
        hello.SupportedVersions.Add(new SupportedVersionRangeV1 { Major = 1, MinMinor = 0, MaxMinor = 0 });
        hello.ProvidedCapabilities.AddRange(BaselineCapabilities);
        hello.RequiredCapabilities.AddRange(BaselineCapabilities);
        return hello;
    }

    private WireEnvelopeV1 RequireNormal(WireEnvelopeV1 envelope, string? expectedType = null)
    {
        var validated = WireEnvelopeValidator.DecodeAndValidate(
            envelope.ToByteArray(), ProtocolNegotiationState.ProtocolId);
        if (validated.NegotiationGeneration != _negotiation.NegotiationGeneration ||
            validated.ProtocolVersion.Major != _negotiation.Major ||
            validated.ProtocolVersion.Minor != _negotiation.Minor)
            throw new InvalidDataException("protocol.negotiation-stale");
        if (expectedType is not null && !string.Equals(validated.MessageType, expectedType, StringComparison.Ordinal))
            throw new InvalidDataException($"protocol.unexpected-core-message:{validated.MessageType}");
        return validated;
    }

    private static void ValidateBootstrapResponse(WireEnvelopeV1 envelope, WireEnvelopeV1 hello)
    {
        if (envelope.EnvelopeVersion != 1 ||
            !string.Equals(envelope.ProtocolId, ProtocolNegotiationState.ProtocolId, StringComparison.Ordinal) ||
            envelope.ProtocolVersion is null || envelope.ProtocolVersion.Major != 0 || envelope.ProtocolVersion.Minor != 0 ||
            envelope.NegotiationGeneration != 0 ||
            envelope.PayloadSchemaVersion is null || envelope.PayloadSchemaVersion.Major != 1 || envelope.PayloadSchemaVersion.Minor != 0 ||
            (int)envelope.PayloadCompression != 1)
            throw new InvalidDataException("protocol.invalid-bootstrap-response");
        WireEnvelopeValidator.ValidateId128(envelope.MessageId, "message_id", allowZero: false);
        WireEnvelopeValidator.ValidateId128(envelope.CorrelationId, "correlation_id", allowZero: false);
        WireEnvelopeValidator.ValidateId128(envelope.SenderInstanceId, "sender_instance_id", allowZero: false);
        if (!envelope.CorrelationId.Equals(hello.CorrelationId) ||
            !envelope.HasCausationId || !envelope.CausationId.Equals(hello.MessageId))
            throw new InvalidDataException("protocol.bootstrap-correlation-mismatch");
        if (envelope.MessageType is not ("protocol.accept" or "protocol.reject"))
            throw new InvalidDataException("protocol.unexpected-bootstrap-response");
        var expectedSchema = envelope.MessageType == "protocol.accept"
            ? "protocol.accept.v1"
            : "protocol.reject.v1";
        if (!string.Equals(envelope.PayloadSchemaId, expectedSchema, StringComparison.Ordinal))
            throw new InvalidDataException("protocol.payload-schema-mismatch");
    }

    private static ByteString RequireConsistentWorldId(ByteString? current, WireEnvelopeV1 envelope)
    {
        if (envelope.WorldContext is null)
            throw new InvalidDataException("protocol.world-context-missing");
        WireEnvelopeValidator.ValidateId128(envelope.WorldContext.WorldId, "world_id", allowZero: false);
        if (current is not null && !current.Equals(envelope.WorldContext.WorldId))
            throw new InvalidDataException("world.id-changed-within-session");
        return envelope.WorldContext.WorldId;
    }

    private static string BoundedError(Exception ex)
    {
        var value = ex is RpcException rpc ? rpc.Status.Detail : ex.Message;
        if (string.IsNullOrWhiteSpace(value)) value = ex.GetType().Name;
        return value.Length <= 512 ? value : value[..512];
    }
}

internal sealed class AlphaCoreEnvelopeFactory
{
    private readonly ByteString _senderInstanceId;
    private readonly ProtocolNegotiationState _negotiation;

    public AlphaCoreEnvelopeFactory(ByteString senderInstanceId, ProtocolNegotiationState negotiation)
    {
        WireEnvelopeValidator.ValidateId128(senderInstanceId, "sender_instance_id", allowZero: false);
        _senderInstanceId = senderInstanceId;
        _negotiation = negotiation ?? throw new ArgumentNullException(nameof(negotiation));
    }

    public WireEnvelopeV1 Bootstrap(string type, string schema, IMessage payload)
        => Create(type, schema, payload, bootstrap: true, world: null);

    public WireEnvelopeV1 Normal(
        string type,
        string schema,
        IMessage payload,
        WorldContextWireV1? world = null)
    {
        if (!_negotiation.IsNegotiated)
            throw new InvalidOperationException("protocol.not-negotiated");
        return Create(type, schema, payload, bootstrap: false, world);
    }

    private WireEnvelopeV1 Create(
        string type,
        string schema,
        IMessage payload,
        bool bootstrap,
        WorldContextWireV1? world)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var messageId = NextId();
        var envelope = new WireEnvelopeV1
        {
            EnvelopeVersion = 1,
            ProtocolId = ProtocolNegotiationState.ProtocolId,
            ProtocolVersion = new ProtocolVersionV1
            {
                Major = bootstrap ? 0u : _negotiation.Major,
                Minor = bootstrap ? 0u : _negotiation.Minor,
            },
            NegotiationGeneration = bootstrap ? 0u : _negotiation.NegotiationGeneration,
            MessageType = type,
            MessageId = messageId,
            CorrelationId = messageId,
            SenderInstanceId = _senderInstanceId,
            PayloadSchemaId = schema,
            PayloadSchemaVersion = new SchemaVersionWireV1 { Major = 1, Minor = 0 },
            PayloadCompression = (CompressionKindV1)1,
            Payload = payload.ToByteString(),
        };
        envelope.WorldContext = world;
        return envelope;
    }

    private static ByteString NextId()
    {
        Span<byte> bytes = stackalloc byte[16];
        do RandomNumberGenerator.Fill(bytes); while (bytes.IndexOfAnyExcept((byte)0) < 0);
        return ByteString.CopyFrom(bytes);
    }
}
