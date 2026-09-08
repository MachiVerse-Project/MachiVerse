using System.Runtime.CompilerServices;
using Google.Protobuf;
using MachiVerse.Gateway.Protocol;
using MachiVerse.Gateway.State;
using MachiVerse.Protocol.V1;

internal static class Gw03PeerSmoke
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        if (!string.Equals(PeerProtocolClient.ProtocolId, "mv.gateway-gateway", StringComparison.Ordinal))
            throw new InvalidOperationException("Peer protocol id mismatch.");

        var negotiation = new PeerProtocolNegotiationState();
        var accept = new ProtocolAcceptV1
        {
            NegotiatedVersion = new ProtocolVersionV1 { Major = 1, Minor = 0 },
            NegotiationGeneration = 7,
        };
        accept.EffectiveOptionalCapabilities.Add("protocol.protobuf.v1");
        negotiation.Accept(accept);
        if (!negotiation.IsNegotiated || negotiation.NegotiationGeneration != 7 || negotiation.Major != 1)
            throw new InvalidOperationException("Peer protocol negotiation state failed.");

        var localGateway = Id(80);
        var peerGateway = Id(81);
        var authority = new MasterAuthorityTracker(localGateway);
        authority.Apply(new MasterGenerationStateV1
        {
            MasterGeneration = 9,
            CurrentMasterGatewayId = localGateway,
        });
        if (!authority.IsLocalMaster)
            throw new InvalidOperationException("Fixture local Master authority was not established.");

        var peers = new PeerHeartbeatRegistry();
        var heartbeat = peers.Apply(new PeerHeartbeatV1
        {
            GatewayLogicalId = peerGateway,
            ComponentInstanceId = Id(82),
            ObservedMasterGeneration = 8,
            Readiness = (GatewayReadinessV1)3,
        });
        if (heartbeat.ObservedMasterGeneration != 8 || peers.Snapshots.Count != 1)
            throw new InvalidOperationException("Peer heartbeat projection failed.");
        if (!authority.IsLocalMaster || authority.Current?.MasterGeneration != 9)
            throw new InvalidOperationException("Peer heartbeat must not replace Core-authoritative Master state.");

        peers.Apply(new PeerHeartbeatV1
        {
            GatewayLogicalId = peerGateway,
            ComponentInstanceId = Id(83),
            ObservedMasterGeneration = 10,
            Readiness = (GatewayReadinessV1)2,
        });
        if (peers.Snapshots.Count != 1 || peers.Snapshots[0].ComponentInstanceId.AsSpan().SequenceEqual(Id(82).Span))
            throw new InvalidOperationException("Peer restart must replace component instance under the same GatewayLogicalId.");
        if (authority.Current?.MasterGeneration != 9)
            throw new InvalidOperationException("Peer-observed newer generation is not Core authority and must not auto-promote.");
    }

    private static ByteString Id(byte value) => ByteString.CopyFrom(Enumerable.Repeat(value, 16).ToArray());
}
