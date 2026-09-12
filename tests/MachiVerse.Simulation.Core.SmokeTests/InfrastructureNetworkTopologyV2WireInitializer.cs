using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class InfrastructureNetworkTopologyV2WireInitializer
{
    private static readonly StableToken SpatialDomain = new("spatial");
    private static readonly StableToken ScopeKind = new("smoke.infrastructure-wire-scope");

    [ModuleInitializer]
    internal static void Run()
    {
        var network = Qa04InfrastructureNetworkMaterializerV1.CreateNetwork(0, ScopeForTile, out _);
        var node = Qa04InfrastructureNetworkMaterializerV1.CreateNode(0, ScopeForTile, out _);
        var edge = Qa04InfrastructureNetworkMaterializerV1.CreateEdge(0, out _);

        RoundTrip(network);
        RoundTrip(node);
        RoundTrip(edge);

        var encoded = InfrastructureNetworkTopologyRecordWireCodecV2.Encode(edge);
        var tampered = encoded.Concat(new byte[] { 0x50, 0x01 }).ToArray();
        ExpectFailure(() => InfrastructureNetworkTopologyRecordWireCodecV2.Decode(tampered),
            "Infrastructure v2 wire must reject an unknown trailing field.");
    }

    private static void RoundTrip(InfrastructureNetworkTopologyRecordMaterialV2 source)
    {
        var beforeDigest = InfrastructureNetworkTopologyPayloadCanonicalDigestV2.Compute(source.Payload);
        var encoded = InfrastructureNetworkTopologyRecordWireCodecV2.Encode(source);
        var decoded = InfrastructureNetworkTopologyRecordWireCodecV2.Decode(encoded);
        var reencoded = InfrastructureNetworkTopologyRecordWireCodecV2.Encode(decoded);
        var afterDigest = InfrastructureNetworkTopologyPayloadCanonicalDigestV2.Compute(decoded.Payload);

        Require(encoded.SequenceEqual(reencoded),
            "Infrastructure v2 record wire must be canonical under decode->encode.");
        Require(beforeDigest.SequenceEqual(afterDigest),
            "Infrastructure v2 record semantic digest must survive wire round trip.");
        Require(decoded.RecordId == source.RecordId &&
                decoded.Revision == source.Revision &&
                decoded.CreatedStep == source.CreatedStep &&
                decoded.RetiredStep == source.RetiredStep &&
                decoded.DetailLevel == source.DetailLevel &&
                decoded.LineageRef == source.LineageRef &&
                decoded.Payload.RecordKind == source.Payload.RecordKind,
            "Infrastructure v2 record envelope must survive wire round trip.");
    }

    private static PartitionRecordRefV1 ScopeForTile(ushort tile)
        => new(
            "spatial.scope_registry",
            DerivedIdentity.DeriveEntityId(
                Qa04ReferenceLoadV1.WorldId,
                creationStep: 0,
                SpatialDomain,
                OpaqueId128.Zero,
                ScopeKind,
                tile));

    private static void ExpectFailure(Action action, string message)
    {
        try
        {
            action();
        }
        catch (InvalidDataException)
        {
            return;
        }
        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
