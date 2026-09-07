using System.Security.Cryptography;
using Google.Protobuf;
using MachiVerse.Administration.View.Configuration;
using MachiVerse.Administration.View.Modules.Management;
using MachiVerse.Administration.View.Modules.Monitoring;
using MachiVerse.Protocol.V1;

namespace MachiVerse.Administration.View.Protocol;

/// <summary>
/// Owns the initial read-only Administration View session for INT-01.
/// It proves auth separation plus health/config read projection without enabling
/// state-changing Admin commands through the local Alpha bridge.
/// </summary>
public sealed class AdminGatewaySession(
    AdminViewConfig config,
    AdminGatewayProtocolClient gateway,
    AdminSessionProjectionStore sessions,
    MonitoringProjectionStore monitoring,
    ManagementProjectionStore management)
{
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly ByteString _senderInstanceId = RandomId128();
    private bool _started;
    private uint _negotiationGeneration;

    public string? LastError { get; private set; }
    public string? SessionIdHex { get; private set; }
    public bool HealthLoaded { get; private set; }
    public bool ConfigLoaded { get; private set; }
    public ulong? ConfigGeneration { get; private set; }
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

            var hello = new ProtocolHelloV1 { ProtocolId = AdminGatewayEnvelopeCodec.ProtocolId };
            hello.SupportedVersions.Add(new SupportedVersionRangeV1 { Major = 1, MinMinor = 0, MaxMinor = 0 });
            hello.ProvidedCapabilities.Add("protocol.protobuf.v1");
            hello.RequiredCapabilities.Add("protocol.protobuf.v1");
            await gateway.SendBootstrapAsync(
                BootstrapEnvelope("protocol.hello", "protocol.hello.v1", hello),
                cancellationToken);

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
                new AuthLoginBeginV1 { AuthDomain = (AuthDomainWireV1)2 }), cancellationToken);

            var loginEnvelope = await RequireNormalAsync("auth.login.result", cancellationToken);
            var login = AuthLoginResultV1.Parser.ParseFrom(loginEnvelope.Payload);
            if (login.Result is null || (int)login.Result.Status != 1 || !login.HasSessionId || !login.HasSessionGeneration)
                throw new InvalidDataException(login.Result?.Code ?? "auth.unauthenticated");
            RequireId128(login.SessionId, "session_id");
            if (login.SessionGeneration == 0) throw new InvalidDataException("auth.session-stale");
            SessionIdHex = Convert.ToHexStringLower(login.SessionId.Span);

            var sessionEnvelope = await RequireNormalAsync("auth.session.changed", cancellationToken);
            if (!sessions.TryApply(sessionEnvelope))
                throw new InvalidDataException("auth.session-state-missing");
            var session = sessions.Snapshot;
            if (!string.Equals(session.SessionId, SessionIdHex, StringComparison.Ordinal) || session.SessionGeneration != login.SessionGeneration ||
                session.State != AdminSessionAccessState.Active)
                throw new InvalidDataException("auth.session-stale");

            gateway.MarkSyncing();
            var target = new ComponentTargetV1 { ComponentKind = (ComponentKindV1)2 };

            var healthQuery = new HealthQueryV1();
            healthQuery.Targets.Add(target.Clone());
            healthQuery.MetricNames.Add("gateway.confirmed.basis-step");
            healthQuery.MetricNames.Add("gateway.core.local-master");
            healthQuery.MetricNames.Add("gateway.core.negotiated");
            await gateway.SendAsync(NormalEnvelope(
                "component.health.query",
                "protocol.health-query.v1",
                healthQuery), cancellationToken);
            var healthEnvelope = await RequireNormalAsync("component.health.result", cancellationToken);
            if (!monitoring.TryApply(healthEnvelope))
                throw new InvalidDataException("component.health.result-not-applied");
            HealthLoaded = true;
            Changed?.Invoke();

            var configRead = management.BuildConfigRead(target, [
                "audit.query-max-page-size",
                "audit.retention-days",
                "network.connect-timeout-ms",
                "network.reconnect-initial-ms",
                "network.reconnect-max-ms",
                "peer.heartbeat-interval-ms",
                "peer.heartbeat-timeout-ms",
                "publication.buffer-ms",
                "publication.max-client-backlog",
                "queue.publication-capacity",
                "queue.result-capacity",
            ]);
            await gateway.SendAsync(NormalEnvelope(
                "config.read",
                "protocol.config-read-request.v1",
                configRead), cancellationToken);
            var configEnvelope = await RequireNormalAsync("config.read.result", cancellationToken);
            if (!management.TryApply(configEnvelope))
                throw new InvalidDataException("config.read.result-not-applied");
            var configTarget = management.Snapshot.ConfigTargets.SingleOrDefault();
            if (configTarget is null || configTarget.ConfigGeneration == 0)
                throw new InvalidDataException("config.read.local-alpha-empty");
            ConfigGeneration = configTarget.ConfigGeneration;
            ConfigLoaded = true;

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
            ProtocolId = AdminGatewayEnvelopeCodec.ProtocolId,
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

    private WireEnvelopeV1 NormalEnvelope(string type, string schema, IMessage payload)
    {
        if (_negotiationGeneration == 0) throw new InvalidOperationException("Admin Gateway protocol is not negotiated.");
        return new WireEnvelopeV1
        {
            EnvelopeVersion = 1,
            ProtocolId = AdminGatewayEnvelopeCodec.ProtocolId,
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
