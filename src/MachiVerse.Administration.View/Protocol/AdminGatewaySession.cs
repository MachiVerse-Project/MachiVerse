using System.Security.Cryptography;
using Google.Protobuf;
using MachiVerse.Administration.View.Configuration;
using MachiVerse.Administration.View.Modules.Management;
using MachiVerse.Administration.View.Modules.Monitoring;
using MachiVerse.Protocol.Canonical;
using MachiVerse.Protocol.V1;

namespace MachiVerse.Administration.View.Protocol;

/// <summary>
/// Owns the Administration View protocol session for INT-01.
/// Browser code never talks to Gateway protocol DTOs directly; this session owns negotiation,
/// ADMIN_VIEW auth, Config request identity, and correlated response application.
/// </summary>
public sealed class AdminGatewaySession(
    AdminViewConfig config,
    AdminGatewayProtocolClient gateway,
    AdminSessionProjectionStore sessions,
    MonitoringProjectionStore monitoring,
    ManagementProjectionStore management)
{
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
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
            healthQuery.MetricNames.Add("gateway.config.generation");
            await gateway.SendAsync(NormalEnvelope(
                "component.health.query",
                "protocol.health-query.v1",
                healthQuery), cancellationToken);
            var healthEnvelope = await RequireNormalAsync("component.health.result", cancellationToken);
            if (!monitoring.TryApply(healthEnvelope))
                throw new InvalidDataException("component.health.result-not-applied");
            HealthLoaded = true;
            Changed?.Invoke();

            await RefreshConfigAsync(target, cancellationToken);
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

    public async Task<ConfigChangeResultV1> SubmitConfigChangeAsync(
        ConfigChangeDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (!_started) throw new InvalidOperationException("Administration View session is not ready.");

        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            var operationId = RandomId128();
            var provisional = new ConfigChangeRequestV1
            {
                Target = draft.Target.Clone(),
                ExpectedBaseGeneration = draft.BaseConfigGeneration,
            };
            provisional.Changes.Add(draft.Edits
                .OrderBy(static edit => edit.Key, StringComparer.Ordinal)
                .Select(static edit => new ConfigChangeEntryV1
                {
                    Key = edit.Key,
                    Value = edit.Value.Clone(),
                }));
            var digest = ByteString.CopyFrom(ConfigChangeIdentityV1.ComputeImmutablePayloadDigest(provisional));
            var request = management.PrepareConfigChange(draft, operationId, digest);
            management.MarkSubmitted(operationId);

            var operationContext = new OperationContextWireV1
            {
                OperationId = operationId,
                OperationPayloadDigest = digest,
            };
            await gateway.SendAsync(NormalEnvelope(
                "config.change",
                "protocol.config-change-request.v1",
                request,
                operationContext), cancellationToken);

            var resultEnvelope = await RequireNormalAsync("config.change.result", cancellationToken);
            ValidateOperationContext(resultEnvelope, operationId, digest);
            if (!management.TryApply(resultEnvelope))
                throw new InvalidDataException("config.change.result-not-applied");
            var result = ConfigChangeResultV1.Parser.ParseFrom(resultEnvelope.Payload);

            await RefreshConfigAsync(draft.Target, cancellationToken);
            Changed?.Invoke();
            return result;
        }
        catch (Exception ex)
        {
            LastError = BoundedError(ex);
            Changed?.Invoke();
            throw;
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private async Task RefreshConfigAsync(ComponentTargetV1 target, CancellationToken cancellationToken)
    {
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

    private WireEnvelopeV1 NormalEnvelope(
        string type,
        string schema,
        IMessage payload,
        OperationContextWireV1? operationContext = null)
    {
        if (_negotiationGeneration == 0) throw new InvalidOperationException("Admin Gateway protocol is not negotiated.");
        var envelope = new WireEnvelopeV1
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
        if (operationContext is not null) envelope.OperationContext = operationContext;
        return envelope;
    }

    private static void ValidateOperationContext(WireEnvelopeV1 envelope, ByteString operationId, ByteString digest)
    {
        if (envelope.OperationContext is null || !envelope.OperationContext.HasOperationId ||
            !envelope.OperationContext.OperationId.Equals(operationId) ||
            !envelope.OperationContext.HasOperationPayloadDigest ||
            !envelope.OperationContext.OperationPayloadDigest.Equals(digest))
            throw new InvalidDataException("operation.context-mismatch");
    }

    private static ByteString RandomId128()
    {
        Span<byte> bytes = stackalloc byte[16];
        do RandomNumberGenerator.Fill(bytes); while (bytes.IndexOfAnyExcept((byte)0) < 0);
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
