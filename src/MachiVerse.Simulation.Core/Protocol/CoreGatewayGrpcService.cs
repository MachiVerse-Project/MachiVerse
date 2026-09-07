using Google.Protobuf;
using Grpc.Core;
using MachiVerse.Protocol.V1;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Protocol;

public interface IAuthenticatedGatewayIdentityResolverV1
{
    OpaqueId128 ResolveAuthenticatedGatewayLogicalId(ServerCallContext context);
}

public interface ICoreGatewayWorldAuthorityV1
{
    Task<WorldStateV1> GetCurrentFinalizedStateAsync(CancellationToken cancellationToken = default);
    OperationSchedulingPolicyV1 GetCurrentSchedulingPolicy();
}

public sealed class CoreGatewayGrpcServiceV1 : MachiVerseInternalProtocolV1.MachiVerseInternalProtocolV1Base
{
    private readonly CoreGatewayNegotiationProfileV1 _negotiationProfile;
    private readonly CoreGatewayEnvelopeFactoryV1 _envelopes;
    private readonly IAuthenticatedGatewayIdentityResolverV1 _identityResolver;
    private readonly CoreGatewaySessionRegistryV1 _sessions;
    private readonly CoreMasterAuthorityCoordinatorV1 _master;
    private readonly CoreGatewayOperationProtocolV1 _operations;
    private readonly CoreConfirmedPublicationCoordinatorV1 _publications;
    private readonly ICoreGatewayWorldAuthorityV1 _worldAuthority;
    private readonly SqlitePersistenceStore _store;

    public CoreGatewayGrpcServiceV1(
        CoreGatewayNegotiationProfileV1 negotiationProfile,
        CoreGatewayEnvelopeFactoryV1 envelopes,
        IAuthenticatedGatewayIdentityResolverV1 identityResolver,
        CoreGatewaySessionRegistryV1 sessions,
        CoreMasterAuthorityCoordinatorV1 master,
        CoreGatewayOperationProtocolV1 operations,
        CoreConfirmedPublicationCoordinatorV1 publications,
        ICoreGatewayWorldAuthorityV1 worldAuthority,
        SqlitePersistenceStore store)
    {
        _negotiationProfile = negotiationProfile ?? throw new ArgumentNullException(nameof(negotiationProfile));
        _envelopes = envelopes ?? throw new ArgumentNullException(nameof(envelopes));
        _identityResolver = identityResolver ?? throw new ArgumentNullException(nameof(identityResolver));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _master = master ?? throw new ArgumentNullException(nameof(master));
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        _publications = publications ?? throw new ArgumentNullException(nameof(publications));
        _worldAuthority = worldAuthority ?? throw new ArgumentNullException(nameof(worldAuthority));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public override async Task Connect(
        IAsyncStreamReader<WireEnvelopeV1> requestStream,
        IServerStreamWriter<WireEnvelopeV1> responseStream,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(requestStream);
        ArgumentNullException.ThrowIfNull(responseStream);
        ArgumentNullException.ThrowIfNull(context);
        var cancellationToken = context.CancellationToken;
        var authenticatedGatewayId = _identityResolver.ResolveAuthenticatedGatewayLogicalId(context);
        if (authenticatedGatewayId.IsZero)
            throw new RpcException(new Status(StatusCode.Unauthenticated, "auth.unauthenticated"));

        if (!await requestStream.MoveNext(cancellationToken)) return;
        var helloEnvelope = requestStream.Current;
        try
        {
            CoreGatewayWireValidatorV1.Validate(helloEnvelope, CoreGatewayMessageDirectionV1.GatewayToCore);
            if (!string.Equals(helloEnvelope.MessageType, "protocol.hello", StringComparison.Ordinal))
                throw new CoreGatewayProtocolException("protocol.missing-required", "First stream message must be protocol.hello.");
            var hello = ParsePayload(helloEnvelope, ProtocolHelloV1.Parser);
            var accepted = CoreGatewayNegotiatorV1.Negotiate(hello, _negotiationProfile);
            await responseStream.WriteAsync(_envelopes.BootstrapResponse(helloEnvelope, "protocol.accept", accepted), cancellationToken);
        }
        catch (CoreGatewayProtocolException ex)
        {
            var reject = new ProtocolRejectV1 { Code = ex.Code.Value, Diagnostic = BoundedDiagnostic(ex.Message) };
            try
            {
                await responseStream.WriteAsync(_envelopes.BootstrapResponse(helloEnvelope, "protocol.reject", reject), cancellationToken);
            }
            catch
            {
                // The incoming bootstrap envelope itself may be too malformed to correlate safely.
            }
            return;
        }
        catch (InvalidProtocolBufferException)
        {
            var reject = new ProtocolRejectV1 { Code = "protocol.malformed", Diagnostic = "Handshake payload protobuf decode failed." };
            await responseStream.WriteAsync(_envelopes.BootstrapResponse(helloEnvelope, "protocol.reject", reject), cancellationToken);
            return;
        }

        const uint negotiationGeneration = 1;
        OpaqueId128? registeredComponentInstanceId = null;
        try
        {
            while (await requestStream.MoveNext(cancellationToken))
            {
                var envelope = requestStream.Current;
                try
                {
                    CoreGatewayWireValidatorV1.Validate(envelope, CoreGatewayMessageDirectionV1.GatewayToCore, negotiationGeneration);
                    switch (envelope.MessageType)
                    {
                        case "gateway.register":
                        {
                            var register = ParsePayload(envelope, GatewayRegisterV1.Parser);
                            var registered = _sessions.Register(authenticatedGatewayId, register);
                            registeredComponentInstanceId = registered.ComponentInstanceId;
                            await WritePostRegistrationStateAsync(envelope, responseStream, negotiationGeneration, authenticatedGatewayId, cancellationToken);
                            break;
                        }
                        case "gateway.heartbeat":
                        {
                            RequireRegistration(registeredComponentInstanceId);
                            var heartbeat = ParsePayload(envelope, GatewayHeartbeatV1.Parser);
                            _sessions.Heartbeat(authenticatedGatewayId, heartbeat);
                            break;
                        }
                        case "operation.batch.submit":
                        {
                            RequireRegistration(registeredComponentInstanceId);
                            var world = await RequireEnvelopeWorldAsync(envelope, cancellationToken);
                            var batch = ParsePayload(envelope, OperationBatchV1.Parser);
                            RequireBatchContextMatches(envelope, batch);
                            var masterGeneration = envelope.WorldContext!.MasterGeneration;
                            var result = await _operations.SubmitBatchAsync(authenticatedGatewayId, masterGeneration, batch, cancellationToken);
                            await responseStream.WriteAsync(
                                _envelopes.NormalResponse(
                                    envelope,
                                    "operation.batch.result",
                                    result,
                                    negotiationGeneration,
                                    WorldContext(world, includeBasis: true),
                                    new OperationContextWireV1 { BatchId = batch.BatchId }),
                                cancellationToken);
                            break;
                        }
                        case "operation.status.query":
                        {
                            RequireRegistration(registeredComponentInstanceId);
                            var world = await RequireEnvelopeWorldAsync(envelope, cancellationToken);
                            var query = ParsePayload(envelope, OperationStatusQueryV1.Parser);
                            var result = await _operations.QueryStatusAsync(query, cancellationToken);
                            await responseStream.WriteAsync(
                                _envelopes.NormalResponse(
                                    envelope,
                                    "operation.status.result",
                                    result,
                                    negotiationGeneration,
                                    WorldContext(world, includeBasis: true)),
                                cancellationToken);
                            break;
                        }
                        case "world.state.resync-request":
                        {
                            RequireRegistration(registeredComponentInstanceId);
                            var world = await RequireEnvelopeWorldAsync(envelope, cancellationToken);
                            var request = ParsePayload(envelope, StateResyncRequestV1.Parser);
                            var currentState = await _worldAuthority.GetCurrentFinalizedStateAsync(cancellationToken);
                            if (currentState.Header.WorldId != world.WorldId)
                                throw new CoreGatewayProtocolException("world.invalid-state", "World authority provider returned another WorldId.");
                            var bundle = await _publications.BuildForResyncAsync(request, currentState, cancellationToken);
                            var publicationContext = WorldContext(world, includeBasis: true, basisOverride: bundle.BasisStep);
                            await responseStream.WriteAsync(
                                _envelopes.NormalResponse(envelope, "world.state.begin", bundle.Publication, negotiationGeneration, publicationContext),
                                cancellationToken);
                            foreach (var chunk in bundle.Chunks)
                            {
                                await responseStream.WriteAsync(
                                    _envelopes.NormalResponse(envelope, "world.state.chunk", chunk, negotiationGeneration, publicationContext),
                                    cancellationToken);
                            }
                            break;
                        }
                        default:
                            throw new CoreGatewayProtocolException("protocol.unknown-message-type", "Message is not routable by Core Gateway session.");
                    }
                }
                catch (CoreGatewayProtocolException ex)
                {
                    throw new RpcException(new Status(MapStatus(ex.Code.Value), ex.Code.Value));
                }
                catch (InvalidProtocolBufferException)
                {
                    throw new RpcException(new Status(StatusCode.InvalidArgument, "protocol.malformed"));
                }
            }
        }
        finally
        {
            if (registeredComponentInstanceId is { } componentId)
                _sessions.Disconnect(authenticatedGatewayId, componentId);
        }
    }

    private async Task WritePostRegistrationStateAsync(
        WireEnvelopeV1 request,
        IServerStreamWriter<WireEnvelopeV1> responseStream,
        uint negotiationGeneration,
        OpaqueId128 gatewayLogicalId,
        CancellationToken cancellationToken)
    {
        var head = await _store.ReadCoreProtocolHeadAsync(cancellationToken);
        var worldContext = new WorldContextWireV1
        {
            WorldId = ByteString.CopyFrom(head.WorldId.ToBytes()),
            BasisStep = head.FinalizedStep,
            MasterGeneration = _master.Current.MasterGeneration,
            ConfigGeneration = head.ConfigGeneration,
        };
        await responseStream.WriteAsync(
            _envelopes.NormalResponse(request, "gateway.role-state", _master.ToRoleState(gatewayLogicalId), negotiationGeneration, worldContext),
            cancellationToken);
        await responseStream.WriteAsync(
            _envelopes.NormalResponse(request, "master.generation.changed", _master.ToWireState(), negotiationGeneration, worldContext),
            cancellationToken);

        var policy = _worldAuthority.GetCurrentSchedulingPolicy();
        if (policy.OwnerConfigGeneration != head.ConfigGeneration)
            throw new CoreGatewayProtocolException("world.invalid-state", "Scheduling policy generation does not match durable Config head.");
        await responseStream.WriteAsync(
            _envelopes.NormalResponse(request, "world.scheduling-policy", CoreGatewayOperationProtocolV1.ToWireSchedulingPolicy(policy), negotiationGeneration, worldContext),
            cancellationToken);
    }

    private async Task<CoreProtocolPersistenceHeadV1> RequireEnvelopeWorldAsync(
        WireEnvelopeV1 envelope,
        CancellationToken cancellationToken)
    {
        var head = await _store.ReadCoreProtocolHeadAsync(cancellationToken);
        var worldId = OpaqueId128.FromBytes(envelope.WorldContext!.WorldId.Span);
        if (worldId != head.WorldId)
            throw new CoreGatewayProtocolException("world.not-found", "Envelope WorldId is not hosted by this Core.");
        return head;
    }

    private static WorldContextWireV1 WorldContext(
        CoreProtocolPersistenceHeadV1 head,
        bool includeBasis,
        ulong? basisOverride = null)
    {
        var context = new WorldContextWireV1
        {
            WorldId = ByteString.CopyFrom(head.WorldId.ToBytes()),
            MasterGeneration = head.MasterGeneration,
            ConfigGeneration = head.ConfigGeneration,
        };
        if (includeBasis) context.BasisStep = basisOverride ?? head.FinalizedStep;
        return context;
    }

    private static void RequireBatchContextMatches(WireEnvelopeV1 envelope, OperationBatchV1 batch)
    {
        if (envelope.OperationContext is null || !envelope.OperationContext.HasBatchId ||
            !envelope.OperationContext.BatchId.Equals(batch.BatchId))
            throw new CoreGatewayProtocolException("protocol.payload-schema-mismatch", "Envelope BatchId does not match OperationBatch payload.");
    }

    private static void RequireRegistration(OpaqueId128? registeredComponentInstanceId)
    {
        if (registeredComponentInstanceId is null)
            throw new CoreGatewayProtocolException("protocol.missing-required", "gateway.register is required before normal Core operations.");
    }

    private static T ParsePayload<T>(WireEnvelopeV1 envelope, MessageParser<T> parser) where T : class, IMessage<T>
    {
        try
        {
            return parser.ParseFrom(envelope.Payload);
        }
        catch (InvalidProtocolBufferException)
        {
            throw;
        }
    }

    private static string BoundedDiagnostic(string diagnostic)
    {
        if (string.IsNullOrEmpty(diagnostic)) return string.Empty;
        return diagnostic.Length <= 4096 ? diagnostic : diagnostic[..4096];
    }

    private static StatusCode MapStatus(string code)
        => code switch
        {
            "auth.unauthenticated" => StatusCode.Unauthenticated,
            "auth.unauthorized" => StatusCode.PermissionDenied,
            "component.unavailable" or "component.resyncing" => StatusCode.Unavailable,
            "internal.failure" => StatusCode.Internal,
            _ => StatusCode.InvalidArgument,
        };
}
