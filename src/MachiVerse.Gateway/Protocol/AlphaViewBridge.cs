using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using MachiVerse.Gateway.State;
using MachiVerse.Protocol.V1;

namespace MachiVerse.Gateway.Protocol;

/// <summary>
/// INT-01 local-only Gateway -> General View bridge. This remains loopback-only and does not
/// replace the release browser auth/TLS path. It forwards exactly one Alpha world-affecting
/// operation kind through the existing Gateway→Core authority path.
/// </summary>
public sealed class AlphaViewBridge(
    AlphaCoreLinkOptions coreOptions,
    AlphaCoreLinkState coreLinkState,
    SchedulingPolicyProjection scheduling,
    ConfirmedProjectionCache confirmedCache,
    AlphaCoreOperationRouter operations,
    AlphaViewSessionControl sessionControl)
{
    private const string ProtocolId = "mv.gateway-view";
    private const string ProjectionProfile = "standard";
    private const string BindingSchemaId = "protocol.participation-binding-view.v1";
    private const int PublicationFull = 1;
    private const int PublicationDelta = 2;
    private const int MutationUpsert = 1;
    private const int MutationDelete = 2;
    private const int ResyncContinueIfPossible = 1;
    private const int ResyncForceFull = 2;
    private readonly AlphaCoreLinkOptions _coreOptions = coreOptions ?? throw new ArgumentNullException(nameof(coreOptions));
    private readonly AlphaCoreLinkState _coreLinkState = coreLinkState ?? throw new ArgumentNullException(nameof(coreLinkState));
    private readonly SchedulingPolicyProjection _scheduling = scheduling ?? throw new ArgumentNullException(nameof(scheduling));
    private readonly ConfirmedProjectionCache _confirmedCache = confirmedCache ?? throw new ArgumentNullException(nameof(confirmedCache));
    private readonly AlphaCoreOperationRouter _operations = operations ?? throw new ArgumentNullException(nameof(operations));
    private readonly AlphaViewSessionControl _sessionControl = sessionControl ?? throw new ArgumentNullException(nameof(sessionControl));

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
            core.WorldId is null || _confirmedCache.Current is null || _scheduling.Current is null)
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
            await SendAsync(socket, BootstrapResponse(helloEnvelope, "protocol.accept", "protocol.accept.v1", accept), context.RequestAborted);

            var loginEnvelope = await ReceiveNormalAsync(socket, context.RequestAborted, "auth.login");
            var login = AuthLoginBeginV1.Parser.ParseFrom(loginEnvelope.Payload);
            if ((int)login.AuthDomain != 1)
                throw new InvalidDataException("auth.unauthorized: Alpha View bridge accepts GENERAL_VIEW only.");

            var sessionId = RandomId128();
            var diverRef = DeriveAlphaDiverRef(_coreOptions.GatewayLogicalId);
            ulong sessionGeneration = 1;
            var terminalKind = AlphaViewSessionTerminalKind.None;
            await SendAsync(socket, NormalResponse(
                loginEnvelope,
                "auth.login.result",
                "protocol.auth-login-result",
                new AuthLoginResultV1
                {
                    Result = Success("auth.login.local-alpha"),
                    SessionId = sessionId,
                    SessionGeneration = sessionGeneration,
                }), context.RequestAborted);

            var sessionState = new AuthSessionStateV1
            {
                SessionId = sessionId,
                AuthDomain = (AuthDomainWireV1)1,
                EffectiveRoleSet = "general-view.alpha",
                SessionGeneration = sessionGeneration,
                Status = (SessionWireStatusV1)1,
                DiverRef = diverRef,
            };
            sessionState.EffectivePermissions.Add("view.operation.diver");
            sessionState.EffectivePermissions.Add("view.participation.bind");
            sessionState.EffectivePermissions.Add("view.session.read.self");
            sessionState.EffectivePermissions.Add("view.world.read.participant");
            sessionState.EffectivePermissions.Add("view.world.subscribe");
            await SendAsync(socket, Notification("auth.session.changed", "protocol.auth-session-state.v1", sessionState), context.RequestAborted);

            ByteString? activeSubscriptionId = null;
            ConfirmedStateSnapshot? viewPublicationBase = null;
            while (socket.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
            {
                var request = await ReceiveNormalAsync(socket, context.RequestAborted);

                if (terminalKind == AlphaViewSessionTerminalKind.None)
                {
                    var scheduledTerminal = _sessionControl.ConsumePending();
                    if (scheduledTerminal != AlphaViewSessionTerminalKind.None)
                    {
                        terminalKind = scheduledTerminal;
                        sessionGeneration = checked(sessionGeneration + 1);
                        var terminalState = CreateTerminalSessionState(sessionId, sessionGeneration, terminalKind);
                        await SendAsync(socket, Notification(
                            "auth.session.changed",
                            "protocol.auth-session-state.v1",
                            terminalState), context.RequestAborted);
                        // The request that surfaced the terminal transition is intentionally not
                        // processed. In particular, no world-affecting Operation crosses into Core.
                        continue;
                    }
                }

                if (terminalKind != AlphaViewSessionTerminalKind.None)
                    throw new InvalidDataException($"auth.session-terminal:{terminalKind.ToString().ToLowerInvariant()}");

                switch (request.MessageType)
                {
                    case "world.subscribe":
                    {
                        var subscription = ViewSubscriptionRequestV1.Parser.ParseFrom(request.Payload);
                        ValidateSubscription(subscription);
                        activeSubscriptionId = subscription.SubscriptionId.Clone();
                        var snapshot = RequireCurrentConfirmedSnapshot();
                        await SendConfirmedFullAsync(socket, activeSubscriptionId, request, context.RequestAborted, snapshot);
                        viewPublicationBase = snapshot;
                        await SendBindingStateAsync(socket, request, context.RequestAborted, snapshot);
                        break;
                    }
                    case "world.state.resync-request":
                    {
                        var resync = StateResyncRequestV1.Parser.ParseFrom(request.Payload);
                        ValidateResyncRequest(resync);
                        activeSubscriptionId ??= RandomId128();
                        var snapshot = RequireCurrentConfirmedSnapshot();
                        if (CanContinueWithDelta(resync, viewPublicationBase, snapshot))
                            await SendConfirmedDeltaAsync(socket, activeSubscriptionId, request, viewPublicationBase!, snapshot, context.RequestAborted);
                        else
                            await SendConfirmedFullAsync(socket, activeSubscriptionId, request, context.RequestAborted, snapshot);
                        viewPublicationBase = snapshot;
                        await SendBindingStateAsync(socket, request, context.RequestAborted, snapshot);
                        break;
                    }
                    case "participation.binding.request":
                    {
                        if (activeSubscriptionId is null)
                            throw new InvalidDataException("protocol.missing-required: world.subscribe is required before binding operations.");
                        viewPublicationBase = await HandleBindingRequestAsync(
                            socket,
                            activeSubscriptionId,
                            diverRef,
                            viewPublicationBase,
                            request,
                            context.RequestAborted);
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

    private static AuthSessionStateV1 CreateTerminalSessionState(
        ByteString sessionId,
        ulong sessionGeneration,
        AlphaViewSessionTerminalKind terminalKind)
    {
        if (terminalKind is not (AlphaViewSessionTerminalKind.Revoked or AlphaViewSessionTerminalKind.Expired))
            throw new InvalidDataException("auth.invalid-alpha-session-terminal");
        return new AuthSessionStateV1
        {
            SessionId = sessionId,
            AuthDomain = (AuthDomainWireV1)1,
            EffectiveRoleSet = "general-view.alpha",
            SessionGeneration = sessionGeneration,
            Status = (SessionWireStatusV1)(int)terminalKind,
        };
    }

    private async Task<ConfirmedStateSnapshot?> HandleBindingRequestAsync(
        WebSocket socket,
        ByteString subscriptionId,
        ByteString authenticatedDiverRef,
        ConfirmedStateSnapshot? viewPublicationBase,
        WireEnvelopeV1 request,
        CancellationToken cancellationToken)
    {
        if (request.WorldContext is null || !request.WorldContext.HasBasisStep)
            throw new InvalidDataException("protocol.world-context-missing");
        var operation = StandardOperationV1.Parser.ParseFrom(request.Payload);
        RequireId128(operation.OperationId, "operation_id");
        if (operation.ImmutablePayloadDigest.Length != 32)
            throw new InvalidDataException("protocol.invalid-hash:immutable_payload_digest");
        if (request.OperationContext is null || !request.OperationContext.HasOperationId ||
            !request.OperationContext.OperationId.Equals(operation.OperationId) ||
            !request.OperationContext.HasOperationPayloadDigest ||
            !request.OperationContext.OperationPayloadDigest.Equals(operation.ImmutablePayloadDigest))
            throw new InvalidDataException("protocol.payload-schema-mismatch: operation context does not match binding request.");
        if (!string.Equals(operation.OperationKind, "participation.binding.create", StringComparison.Ordinal) ||
            !string.Equals(operation.OperationPayloadSchemaId, "operation.participation.binding.create", StringComparison.Ordinal) ||
            operation.OperationPayloadSchemaVersion is null ||
            operation.OperationPayloadSchemaVersion.Major != 1 ||
            operation.OperationPayloadSchemaVersion.Minor != 0)
            throw new InvalidDataException("auth.unauthorized: Alpha View bridge permits only participation.binding.create v1.0.");

        ParticipationBindingRequestV1 bindingRequest;
        try
        {
            bindingRequest = ParticipationBindingRequestV1.Parser.ParseFrom(operation.OperationPayload);
        }
        catch (InvalidProtocolBufferException ex)
        {
            throw new InvalidDataException("request.invalid: malformed Participation binding payload.", ex);
        }
        RequireId128(bindingRequest.DiverRef, "diver_ref");
        if (!bindingRequest.OperationId.Equals(operation.OperationId) ||
            !bindingRequest.ImmutablePayloadDigest.Equals(operation.ImmutablePayloadDigest))
            throw new InvalidDataException("protocol.operation-payload-mismatch");
        if (!bindingRequest.DiverRef.Equals(authenticatedDiverRef))
            throw new InvalidDataException("auth.unauthorized: binding diver_ref does not match authenticated session identity.");

        var before = RequireCurrentConfirmedSnapshot();
        if (request.WorldContext.BasisStep != before.BasisStep)
            throw new InvalidDataException("world.basis-stale");
        var confirmedBinding = ReadConfirmedBinding(before);
        if (bindingRequest.ExpectedBindingGeneration != confirmedBinding.BindingGeneration)
            throw new InvalidDataException("request.stale: binding generation changed.");
        if ((int)confirmedBinding.Status == 2)
            throw new InvalidDataException("request.conflict: Diver already has an active binding.");

        var result = await _operations.SubmitAsync(operation, cancellationToken);
        var entry = result.Entries.SingleOrDefault(item => item.OperationId.Equals(operation.OperationId))
            ?? throw new InvalidDataException("protocol.operation-result-missing");
        var status = new OperationStatusResultV1
        {
            OperationId = entry.OperationId,
            State = entry.Lifecycle,
            OperationPayloadDigest = entry.OperationPayloadDigest,
            RichResultDetailsAvailable = false,
        };
        if (entry.HasEffectiveStep) status.EffectiveStep = entry.EffectiveStep;
        if ((int)entry.Lifecycle == 4) status.TerminalResult = entry.Result?.Clone();
        var operationContext = new OperationContextWireV1
        {
            OperationId = operation.OperationId,
            OperationPayloadDigest = operation.ImmutablePayloadDigest,
        };
        await SendAsync(socket, NormalResponse(
            request,
            "operation.result",
            "protocol.operation-status-result.v1",
            status,
            operation: operationContext), cancellationToken);

        if ((int)entry.Lifecycle != 4 || entry.Result is null || (int)entry.Result.Status is not (1 or 4 or 5))
            return viewPublicationBase;

        var advanced = await WaitForConfirmedAdvanceAsync(before.BasisStep, cancellationToken);
        var resultingBinding = ReadConfirmedBinding(advanced);
        if (!resultingBinding.HasDiverRef || !resultingBinding.DiverRef.Equals(authenticatedDiverRef) ||
            resultingBinding.BindingGeneration != checked(bindingRequest.ExpectedBindingGeneration + 1))
            throw new InvalidDataException("component.binding-projection-actor-generation-mismatch");

        if (CanSendDelta(viewPublicationBase, advanced))
            await SendConfirmedDeltaAsync(socket, subscriptionId, request, viewPublicationBase!, advanced, cancellationToken);
        else
            await SendConfirmedFullAsync(socket, subscriptionId, request, cancellationToken, advanced);
        await SendBindingStateAsync(socket, request, cancellationToken, advanced);
        return advanced;
    }

    private ConfirmedStateSnapshot RequireCurrentConfirmedSnapshot()
        => _confirmedCache.Current ?? throw new InvalidDataException("component.core-confirmed-state-unavailable");

    private static ParticipationBindingViewV1 ReadConfirmedBinding(ConfirmedStateSnapshot snapshot)
    {
        var record = snapshot.Records.Values.SingleOrDefault(item => string.Equals(item.SchemaId, BindingSchemaId, StringComparison.Ordinal))
            ?? throw new InvalidDataException("component.binding-projection-unavailable");
        return ParticipationBindingViewV1.Parser.ParseFrom(record.Payload);
    }

    private async Task<ConfirmedStateSnapshot> WaitForConfirmedAdvanceAsync(ulong previousBasisStep, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_confirmedCache.Current is { } current && current.BasisStep > previousBasisStep)
                return current;
            await Task.Delay(25, cancellationToken);
        }
        throw new InvalidDataException("component.core-confirmed-state-timeout");
    }

    private void ValidateResyncRequest(StateResyncRequestV1 request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireId128(request.WorldId, "world_id");
        var link = _coreLinkState.Current;
        if (link.WorldId is null || !request.WorldId.Span.SequenceEqual(Convert.FromHexString(link.WorldId)))
            throw new InvalidDataException("world.not-found");
        if ((int)request.Preference is not (ResyncContinueIfPossible or ResyncForceFull))
            throw new InvalidDataException("protocol.field-out-of-range:resync_preference");
        if (request.HasClientBasisStep != request.HasClientContinuityToken)
            throw new InvalidDataException("protocol.missing-required:client_resync_base");
        if (request.HasClientContinuityToken && request.ClientContinuityToken.Length != 32)
            throw new InvalidDataException("protocol.invalid-continuity-token-length");
    }

    private static bool CanContinueWithDelta(
        StateResyncRequestV1 request,
        ConfirmedStateSnapshot? retainedBase,
        ConfirmedStateSnapshot current)
    {
        if ((int)request.Preference != ResyncContinueIfPossible ||
            !request.HasClientBasisStep || !request.HasClientContinuityToken || retainedBase is null)
            return false;
        if (request.ClientBasisStep != retainedBase.BasisStep ||
            !request.ClientContinuityToken.Span.SequenceEqual(retainedBase.ContinuityToken))
            return false;
        return CanSendDelta(retainedBase, current);
    }

    private static bool CanSendDelta(ConfirmedStateSnapshot? retainedBase, ConfirmedStateSnapshot current)
        => retainedBase is not null
            && current.BasisStep > retainedBase.BasisStep
            && retainedBase.ContinuityToken.Length == 32
            && current.ContinuityToken.Length == 32
            && retainedBase.ProjectionSchemaDigest.AsSpan().SequenceEqual(current.ProjectionSchemaDigest);

    private async Task SendBindingStateAsync(
        WebSocket socket,
        WireEnvelopeV1 request,
        CancellationToken cancellationToken,
        ConfirmedStateSnapshot? suppliedSnapshot = null)
    {
        var snapshot = suppliedSnapshot ?? RequireCurrentConfirmedSnapshot();
        var binding = ReadConfirmedBinding(snapshot);
        var world = CurrentWorldContext(snapshot.BasisStep);
        await SendAsync(socket, NormalResponse(
            request,
            "participation.binding.state",
            BindingSchemaId,
            binding,
            world), cancellationToken);
    }

    private async Task SendConfirmedFullAsync(
        WebSocket socket,
        ByteString subscriptionId,
        WireEnvelopeV1 request,
        CancellationToken cancellationToken,
        ConfirmedStateSnapshot? suppliedSnapshot = null)
    {
        var snapshot = suppliedSnapshot ?? RequireCurrentConfirmedSnapshot();
        var records = snapshot.Records
            .OrderBy(static item => item.Key.SchemaId, StringComparer.Ordinal)
            .ThenBy(static item => item.Key.RecordIdHex, StringComparer.Ordinal)
            .Select(static item => ToUpsert(item.Value))
            .ToArray();
        await SendConfirmedPublicationAsync(
            socket,
            subscriptionId,
            request,
            snapshot,
            PublicationFull,
            baseContinuityToken: null,
            records,
            cancellationToken);
    }

    private async Task SendConfirmedDeltaAsync(
        WebSocket socket,
        ByteString subscriptionId,
        WireEnvelopeV1 request,
        ConfirmedStateSnapshot retainedBase,
        ConfirmedStateSnapshot current,
        CancellationToken cancellationToken)
    {
        if (!CanSendDelta(retainedBase, current))
            throw new InvalidDataException("protocol.continuity-mismatch:unprovable-alpha-view-delta");
        var records = BuildDeltaRecords(retainedBase, current);
        await SendConfirmedPublicationAsync(
            socket,
            subscriptionId,
            request,
            current,
            PublicationDelta,
            retainedBase.ContinuityToken,
            records,
            cancellationToken);
    }

    private async Task SendConfirmedPublicationAsync(
        WebSocket socket,
        ByteString subscriptionId,
        WireEnvelopeV1 request,
        ConfirmedStateSnapshot snapshot,
        int publicationKind,
        byte[]? baseContinuityToken,
        IReadOnlyCollection<ProjectionRecordV1> records,
        CancellationToken cancellationToken)
    {
        RequireId128(subscriptionId, "subscription_id");
        if (snapshot.ContinuityToken.Length != 32 || snapshot.ProjectionSchemaDigest.Length != 32)
            throw new InvalidDataException("protocol.invalid-confirmed-publication-authority");
        if (publicationKind == PublicationFull && baseContinuityToken is not null)
            throw new InvalidDataException("protocol.full-publication-has-base");
        if (publicationKind == PublicationDelta && (baseContinuityToken is null || baseContinuityToken.Length != 32))
            throw new InvalidDataException("protocol.delta-publication-missing-base");

        var publicationId = RandomId128();
        var payload = new ProjectionChunkPayloadV1
        {
            SubscriptionId = subscriptionId,
            PublicationId = publicationId,
            ChunkIndex = 0,
        };
        payload.Records.AddRange(records);
        var payloadBytes = payload.ToByteArray();
        if (payloadBytes.Length > 1024 * 1024)
            throw new InvalidDataException("protocol.limit-exceeded: Alpha bridge currently requires one <=1MiB projection chunk.");

        var publication = new StatePublicationV1
        {
            PublicationId = publicationId,
            Kind = (PublicationKindV1)publicationKind,
            StateContinuityToken = ByteString.CopyFrom(snapshot.ContinuityToken),
            ChunkCount = 1,
            ProjectionSchemaDigest = ByteString.CopyFrom(snapshot.ProjectionSchemaDigest),
        };
        if (baseContinuityToken is not null)
            publication.BaseStateContinuityToken = ByteString.CopyFrom(baseContinuityToken);

        var world = CurrentWorldContext(snapshot.BasisStep);
        await SendAsync(socket, NormalResponse(
            request,
            "world.state.begin",
            "protocol.state-publication.v1",
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
            "protocol.state-publication-chunk.v1",
            chunk,
            world), cancellationToken);
    }

    private static IReadOnlyCollection<ProjectionRecordV1> BuildDeltaRecords(
        ConfirmedStateSnapshot retainedBase,
        ConfirmedStateSnapshot current)
    {
        var records = new List<ProjectionRecordV1>();
        foreach (var item in current.Records.OrderBy(static item => item.Key.SchemaId, StringComparer.Ordinal)
                     .ThenBy(static item => item.Key.RecordIdHex, StringComparer.Ordinal))
        {
            var record = item.Value;
            if (!retainedBase.Records.TryGetValue(item.Key, out var previous) ||
                previous.Revision != record.Revision ||
                !previous.Payload.AsSpan().SequenceEqual(record.Payload))
            {
                records.Add(ToUpsert(record));
            }
        }

        foreach (var item in retainedBase.Records.OrderBy(static item => item.Key.SchemaId, StringComparer.Ordinal)
                     .ThenBy(static item => item.Key.RecordIdHex, StringComparer.Ordinal))
        {
            if (current.Records.ContainsKey(item.Key)) continue;
            records.Add(new ProjectionRecordV1
            {
                RecordSchemaId = item.Value.SchemaId,
                RecordSchemaVersion = new SchemaVersionWireV1 { Major = 1, Minor = 0 },
                RecordId = ByteString.CopyFrom(item.Value.RecordId),
                RecordRevision = item.Value.Revision == ulong.MaxValue ? ulong.MaxValue : item.Value.Revision + 1,
                MutationKind = (ProjectionMutationKindV1)MutationDelete,
                Payload = ByteString.Empty,
            });
        }

        return records
            .OrderBy(static record => record.RecordSchemaId, StringComparer.Ordinal)
            .ThenBy(static record => Convert.ToHexStringLower(record.RecordId.Span), StringComparer.Ordinal)
            .ToArray();
    }

    private static ProjectionRecordV1 ToUpsert(ConfirmedProjectionRecord record)
        => new()
        {
            RecordSchemaId = record.SchemaId,
            RecordSchemaVersion = new SchemaVersionWireV1 { Major = 1, Minor = 0 },
            RecordId = ByteString.CopyFrom(record.RecordId),
            RecordRevision = record.Revision,
            MutationKind = (ProjectionMutationKindV1)MutationUpsert,
            Payload = ByteString.CopyFrom(record.Payload),
        };

    private WorldContextWireV1 CurrentWorldContext(ulong basisStep)
    {
        var link = _coreLinkState.Current;
        if (link.WorldId is null) throw new InvalidDataException("world.not-found");
        var policy = _scheduling.Current ?? throw new InvalidDataException("component.scheduling-policy-unavailable");
        return new WorldContextWireV1
        {
            WorldId = ByteString.CopyFrom(Convert.FromHexString(link.WorldId)),
            BasisStep = basisStep,
            ConfigGeneration = policy.OwnerConfigGeneration,
        };
    }

    private WireEnvelopeV1 BootstrapResponse(WireEnvelopeV1 request, string messageType, string schema, IMessage payload)
        => CreateEnvelope(request, messageType, schema, payload, bootstrap: true, world: null, operation: null);

    private WireEnvelopeV1 NormalResponse(
        WireEnvelopeV1 request,
        string messageType,
        string schema,
        IMessage payload,
        WorldContextWireV1? world = null,
        OperationContextWireV1? operation = null)
        => CreateEnvelope(request, messageType, schema, payload, bootstrap: false, world, operation);

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
        WorldContextWireV1? world,
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
            WorldContext = world,
            OperationContext = operation,
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

    private static ByteString DeriveAlphaDiverRef(ByteString gatewayLogicalId)
    {
        RequireId128(gatewayLogicalId, "gateway_logical_id");
        var label = Encoding.ASCII.GetBytes("machiverse.alpha.diver-ref.v1");
        var preimage = new byte[gatewayLogicalId.Length + label.Length];
        gatewayLogicalId.Span.CopyTo(preimage);
        label.CopyTo(preimage, gatewayLogicalId.Length);
        var digest = SHA256.HashData(preimage);
        var bytes = digest.AsSpan(0, 16).ToArray();
        if (bytes.AsSpan().IndexOfAnyExcept((byte)0) < 0) bytes[^1] = 1;
        return ByteString.CopyFrom(bytes);
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
