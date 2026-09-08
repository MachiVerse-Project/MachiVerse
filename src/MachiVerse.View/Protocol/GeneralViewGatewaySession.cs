using System.Security.Cryptography;
using Google.Protobuf;
using MachiVerse.Protocol.V1;
using MachiVerse.View.Configuration;
using MachiVerse.View.Operations;
using MachiVerse.View.Participation;
using MachiVerse.View.State;

namespace MachiVerse.View.Protocol;

/// <summary>
/// Owns the General View Alpha protocol session. The browser consumes only Gateway-confirmed
/// publications and explicit binding projections; local operation state is never authoritative.
/// </summary>
public sealed class GeneralViewGatewaySession(
    GeneralViewConfig config,
    GatewayProtocolClient gateway,
    PublicationConsumer publications,
    ViewSessionProjectionStore sessions,
    ParticipationBindingProjectionStore binding,
    ViewOperationController operations)
{
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly ByteString _senderInstanceId = RandomId128();
    private bool _started;
    private uint _negotiationGeneration;

    public string? LastError { get; private set; }
    public string? SessionIdHex { get; private set; }
    public string? SubscriptionIdHex { get; private set; }
    public ulong? ConfirmedBasisStep { get; private set; }
    public ulong? SchedulingPolicyGeneration { get; private set; }
    public uint AutomaticResyncCount { get; private set; }
    public string? LastAutomaticResyncReason { get; private set; }
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
            if (!sessions.TryApply(sessionEnvelope))
                throw new InvalidDataException("auth.session-state-missing");
            var session = sessions.Snapshot;
            if (!string.Equals(session.SessionId, SessionIdHex, StringComparison.Ordinal) ||
                session.SessionGeneration != login.SessionGeneration ||
                session.State != ViewSessionAccessState.Active ||
                string.IsNullOrEmpty(session.DiverRef))
                throw new InvalidDataException("auth.session-stale");

            gateway.MarkSyncing();
            operations.SetAccessState(ViewMutationAccessState.Resyncing, "view.initial-sync");
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
            var bindingEnvelope = await RequireNormalAsync("participation.binding.state", cancellationToken);
            if (!binding.TryApply(bindingEnvelope))
                throw new InvalidDataException("participation.binding-state-not-applied");
            ValidateBindingActorForSession(session, binding.Snapshot);

            operations.SetAccessState(ViewMutationAccessState.Ready);
            gateway.MarkReady();
            _started = true;
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            LastError = BoundedError(ex);
            operations.SetAccessState(ViewMutationAccessState.Blocked, "view.session-start-failed");
            gateway.MarkDegraded();
            Changed?.Invoke();
            throw;
        }
        finally
        {
            _startGate.Release();
        }
    }

    public async Task<TrackedViewOperation> RequestAlphaBindingAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            if (!_started || ConfirmedBasisStep is null || SchedulingPolicyGeneration is null)
                throw new InvalidOperationException("General View must be Ready before requesting a Diver binding.");
            var session = sessions.Snapshot;
            if (!session.HasPermission(ViewPermissionTokens.OperationDiver) ||
                !session.HasPermission(ViewPermissionTokens.ParticipationBind))
                throw new InvalidOperationException("Current General View session lacks Diver binding permissions.");
            if (string.IsNullOrEmpty(session.DiverRef))
                throw new InvalidOperationException("Current General View session has no confirmed Diver identity.");
            if (binding.Snapshot.Freshness != ParticipationProjectionFreshness.Confirmed ||
                binding.Snapshot.State != ParticipationBindingState.None)
                throw new InvalidOperationException("Alpha binding create requires confirmed NONE binding state.");

            var expectedBindingGeneration = binding.Snapshot.BindingGeneration;
            var operationId = RandomId128();
            var admission = new OperationSchedulingAdmissionWireV1
            {
                AdmissionBasisStep = ConfirmedBasisStep.Value,
                SchedulingPolicyGeneration = SchedulingPolicyGeneration.Value,
            };
            var payload = new ParticipationBindingRequestV1
            {
                PreferenceProfile = "alpha.default",
                DiverRef = ByteString.CopyFrom(Convert.FromHexString(session.DiverRef)),
                ExpectedBindingGeneration = expectedBindingGeneration,
            };
            var digestBytes = ViewParticipationBindingIdentityV1.ComputeImmutablePayloadDigest(admission, payload);
            var digest = ByteString.CopyFrom(digestBytes);
            payload.OperationId = operationId;
            payload.ImmutablePayloadDigest = digest;

            var draft = new ViewOperationDraft(
                ViewParticipationBindingIdentityV1.OperationKind,
                admission.AdmissionBasisStep,
                admission.SchedulingPolicyGeneration,
                RequestedNotBeforeStep: null,
                RequestedDeadlineStep: null,
                CandidateStep: null,
                ViewParticipationBindingIdentityV1.PayloadSchemaId,
                ViewParticipationBindingIdentityV1.PayloadSchemaMajor,
                ViewParticipationBindingIdentityV1.PayloadSchemaMinor,
                payload.ToByteString(),
                SemanticTarget: "participation.binding",
                PredictedPayload: ByteString.Empty);
            _ = operations.Prepare(draft, operationId, digest);
            var request = operations.TakeForSubmission(operationId);
            _ = ViewParticipationBindingIdentityV1.ValidateStandardOperation(request);

            var worldId = ParseWorldId(config.WorldIdHex);
            var operationContext = new OperationContextWireV1
            {
                OperationId = operationId,
                OperationPayloadDigest = digest,
            };
            await gateway.SendAsync(NormalEnvelope(
                "participation.binding.request",
                "protocol.standard-operation.v1",
                request,
                new WorldContextWireV1 { WorldId = worldId, BasisStep = ConfirmedBasisStep.Value },
                operationContext), cancellationToken);

            var resultEnvelope = await RequireNormalAsync("operation.result", cancellationToken);
            if (!operations.TryApplyResult(resultEnvelope))
                throw new InvalidDataException("operation.result-not-applied");
            var tracked = operations.Operations.Single(item => string.Equals(item.OperationId, Convert.ToHexStringLower(operationId.Span), StringComparison.Ordinal));
            if (tracked.State is ViewOperationLifecycleState.Rejected or ViewOperationLifecycleState.Failed)
                return tracked;
            if (tracked.State != ViewOperationLifecycleState.Terminal)
                throw new InvalidDataException("alpha.binding-operation-not-terminal");

            var subscriptionId = ByteString.CopyFrom(Convert.FromHexString(SubscriptionIdHex
                ?? throw new InvalidOperationException("SubscriptionId is unavailable.")));
            var confirmed = await ReceiveConfirmedUpdateWithAutomaticResyncAsync(
                worldId,
                subscriptionId,
                session,
                cancellationToken);
            ConfirmedBasisStep = confirmed.BasisStep;
            if (binding.Snapshot.State != ParticipationBindingState.Active ||
                binding.Snapshot.BindingGeneration != checked(expectedBindingGeneration + 1))
                throw new InvalidDataException("participation.binding-confirmation-generation-mismatch");

            Changed?.Invoke();
            return operations.Operations.Single(item => string.Equals(item.OperationId, Convert.ToHexStringLower(operationId.Span), StringComparison.Ordinal));
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<ConfirmedWorldSnapshot> ReceiveConfirmedUpdateWithAutomaticResyncAsync(
        ByteString worldId,
        ByteString subscriptionId,
        ViewSessionProjection session,
        CancellationToken cancellationToken)
    {
        try
        {
            var confirmed = await ReceivePublicationAsync(worldId, subscriptionId, cancellationToken);
            var bindingEnvelope = await RequireNormalAsync("participation.binding.state", cancellationToken);
            ApplyConfirmedBinding(bindingEnvelope, session);
            return confirmed;
        }
        catch (ContinuityMismatchException ex)
        {
            var discardedBindingEnvelope = await RequireNormalAsync("participation.binding.state", cancellationToken);
            ValidateDiscardedBindingEnvelope(discardedBindingEnvelope, worldId);

            publications.BeginResync(ex.Message);
            binding.MarkRefreshRequired("protocol.continuity-mismatch");
            operations.SetAccessState(ViewMutationAccessState.Resyncing, ex.Message);
            gateway.MarkSyncing();
            AutomaticResyncCount = checked(AutomaticResyncCount + 1);
            LastAutomaticResyncReason = ex.Message;
            Changed?.Invoke();

            var resync = publications.CreateResyncRequest(worldId, forceFull: true);
            await gateway.SendAsync(NormalEnvelope(
                "world.state.resync-request",
                "protocol.state-resync-request.v1",
                resync), cancellationToken);

            var recovered = await ReceivePublicationAsync(worldId, subscriptionId, cancellationToken);
            if (!string.Equals(publications.LastAcceptedPublicationKind, "FULL", StringComparison.Ordinal))
                throw new InvalidDataException("protocol.resync-required-full-publication");
            var bindingEnvelope = await RequireNormalAsync("participation.binding.state", cancellationToken);
            ApplyConfirmedBinding(bindingEnvelope, session);

            ConfirmedBasisStep = recovered.BasisStep;
            operations.SetAccessState(ViewMutationAccessState.Ready);
            gateway.MarkReady();
            Changed?.Invoke();
            return recovered;
        }
    }

    private void ApplyConfirmedBinding(WireEnvelopeV1 bindingEnvelope, ViewSessionProjection session)
    {
        if (!binding.TryApply(bindingEnvelope))
            throw new InvalidDataException("participation.binding-state-not-applied");
        ValidateBindingActorForSession(session, binding.Snapshot);
    }

    private static void ValidateDiscardedBindingEnvelope(WireEnvelopeV1 envelope, ByteString worldId)
    {
        if (!string.Equals(envelope.MessageType, "participation.binding.state", StringComparison.Ordinal) ||
            envelope.WorldContext is null || !envelope.WorldContext.HasBasisStep ||
            !envelope.WorldContext.WorldId.Equals(worldId))
            throw new InvalidDataException("protocol.publication-context-mismatch");
        _ = ParticipationBindingViewV1.Parser.ParseFrom(envelope.Payload);
    }

    private static void ValidateBindingActorForSession(
        ViewSessionProjection session,
        ParticipationBindingProjection confirmedBinding)
    {
        if (confirmedBinding.State != ParticipationBindingState.Active)
            return;
        if (string.IsNullOrEmpty(session.DiverRef) ||
            !string.Equals(confirmedBinding.DiverRef, session.DiverRef, StringComparison.Ordinal))
            throw new InvalidDataException("participation.binding-actor-mismatch");
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
        if (beginEnvelope.WorldContext.HasConfigGeneration)
            SchedulingPolicyGeneration = beginEnvelope.WorldContext.ConfigGeneration;
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
        while (true)
        {
            var envelope = await gateway.ReceiveAsync(cancellationToken);
            if (envelope.NegotiationGeneration != _negotiationGeneration)
                throw new InvalidDataException("protocol.negotiation-stale");
            if (string.Equals(envelope.MessageType, type, StringComparison.Ordinal))
                return envelope;

            if (string.Equals(envelope.MessageType, "auth.session.changed", StringComparison.Ordinal))
            {
                if (!sessions.TryApply(envelope))
                    throw new InvalidDataException("auth.session-state-not-applied");
                var session = sessions.Snapshot;
                if (session.State is ViewSessionAccessState.Revoked or ViewSessionAccessState.Expired)
                {
                    operations.SetAccessState(ViewMutationAccessState.SessionRevoked, session.ReasonCode);
                    LastError = session.ReasonCode;
                    Changed?.Invoke();
                    throw new InvalidOperationException(session.ReasonCode ?? "auth.session-terminal");
                }
                if (session.State == ViewSessionAccessState.ReauthenticationRequired)
                {
                    operations.SetAccessState(ViewMutationAccessState.Blocked, session.ReasonCode);
                    LastError = session.ReasonCode;
                    Changed?.Invoke();
                    throw new InvalidOperationException(session.ReasonCode ?? "auth.reauthentication-required");
                }
                Changed?.Invoke();
                continue;
            }

            throw new InvalidDataException($"protocol.unexpected-gateway-message:{envelope.MessageType}");
        }
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
        WorldContextWireV1? world = null,
        OperationContextWireV1? operation = null)
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
        if (operation is not null) envelope.OperationContext = operation;
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
