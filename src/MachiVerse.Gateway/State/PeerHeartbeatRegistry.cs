using Google.Protobuf;
using MachiVerse.Protocol.V1;

namespace MachiVerse.Gateway.State;

public sealed record PeerHeartbeatSnapshot(
    byte[] GatewayLogicalId,
    byte[] ComponentInstanceId,
    ulong ObservedMasterGeneration,
    GatewayReadinessV1 Readiness)
{
    public string GatewayLogicalIdHex => Convert.ToHexStringLower(GatewayLogicalId);
}

public sealed class PeerHeartbeatRegistry
{
    private readonly Dictionary<string, PeerHeartbeatSnapshot> _peers = new(StringComparer.Ordinal);

    public IReadOnlyList<PeerHeartbeatSnapshot> Snapshots
        => _peers.Values.OrderBy(static peer => peer.GatewayLogicalIdHex, StringComparer.Ordinal).ToArray();

    public PeerHeartbeatSnapshot Apply(PeerHeartbeatV1 heartbeat)
    {
        ArgumentNullException.ThrowIfNull(heartbeat);
        var logicalId = ValidateId128(heartbeat.GatewayLogicalId, "gateway_logical_id");
        var componentInstanceId = ValidateId128(heartbeat.ComponentInstanceId, "component_instance_id");
        if ((int)heartbeat.Readiness is < 1 or > 5)
            throw new InvalidDataException("protocol.invalid-gateway-readiness");

        var next = new PeerHeartbeatSnapshot(
            logicalId,
            componentInstanceId,
            heartbeat.ObservedMasterGeneration,
            heartbeat.Readiness);
        _peers[Convert.ToHexStringLower(logicalId)] = next;
        return next;
    }

    public bool TryGet(ByteString gatewayLogicalId, out PeerHeartbeatSnapshot? snapshot)
    {
        var id = ValidateId128(gatewayLogicalId, "gateway_logical_id");
        return _peers.TryGetValue(Convert.ToHexStringLower(id), out snapshot);
    }

    private static byte[] ValidateId128(ByteString value, string field)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length != 16 || value.Span.IndexOfAnyExcept((byte)0) < 0)
            throw new InvalidDataException($"protocol.invalid-id:{field}");
        return value.ToByteArray();
    }
}
