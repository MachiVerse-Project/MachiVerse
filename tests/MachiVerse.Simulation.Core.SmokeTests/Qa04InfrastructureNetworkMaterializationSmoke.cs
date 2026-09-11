using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04InfrastructureNetworkMaterializationSmoke
{
    private static readonly StableToken SpatialDomain = new("spatial");
    private static readonly StableToken ScopeKind = new("smoke.infrastructure-tile-scope");

    [ModuleInitializer]
    internal static void Run()
    {
        Qa04InfrastructureNetworkMaterializerV1.ValidateCanonicalContract();
        Require(Qa04InfrastructureNetworkMaterializerV1.CanonicalNetworkCount == 100 &&
                Qa04InfrastructureNetworkMaterializerV1.CanonicalNodeCount == 20_000 &&
                Qa04InfrastructureNetworkMaterializerV1.CanonicalEdgeCount == 100_000 &&
                Qa04InfrastructureNetworkMaterializerV1.CanonicalTopologyRecordCount == 120_100,
            "Canonical Infrastructure topology cardinality drifted.");

        var records = MaterializeNetworkZeroSlice();
        Require(records.Length == 1_201,
            "Reduced Infrastructure smoke slice must contain one network, 200 nodes, and 1,000 edges.");
        Require(records.Count(static record => record.Payload is InfrastructureNetworkPayloadV2) == 1 &&
                records.Count(static record => record.Payload is InfrastructureNetworkNodePayloadV2) == 200 &&
                records.Count(static record => record.Payload is InfrastructureNetworkEdgePayloadV2) == 1_000,
            "Reduced Infrastructure smoke slice cardinality drifted.");
        Require(records.Select(static record => record.RecordId).Distinct().Count() == records.Length,
            "Reduced Infrastructure topology record ids must be unique.");

        InfrastructureNetworkTopologyReferenceClosureV2.Validate(records);

        var firstNetwork = records.Single(static record => record.Payload is InfrastructureNetworkPayloadV2);
        var networkPayload = (InfrastructureNetworkPayloadV2)firstNetwork.Payload;
        Require(networkPayload.NetworkKind.Value == "transport" &&
                networkPayload.NodeRefs.Count == 200 && networkPayload.EdgeRefs.Count == 1_000 &&
                networkPayload.ScopeRefs.Count == 1 && networkPayload.OperatorRefs.Count == 1 &&
                networkPayload.TopologyRevision == 1,
            "Canonical Infrastructure network payload drifted.");

        var firstNode = records.First(static record => record.Payload is InfrastructureNetworkNodePayloadV2);
        var nodePayload = (InfrastructureNetworkNodePayloadV2)firstNode.Payload;
        Require(nodePayload.NetworkRef.RecordId == firstNetwork.RecordId &&
                nodePayload.NodeKind.Value == "junction" && nodePayload.CapacityUnits == 1_000 &&
                nodePayload.AvailabilityPpm == 1_000_000 && nodePayload.Status.Value == "active",
            "Canonical Infrastructure node payload drifted.");

        var firstEdge = records.First(static record => record.Payload is InfrastructureNetworkEdgePayloadV2);
        var edgePayload = (InfrastructureNetworkEdgePayloadV2)firstEdge.Payload;
        Require(edgePayload.NetworkRef.RecordId == firstNetwork.RecordId &&
                edgePayload.FromNodeRef != edgePayload.ToNodeRef &&
                edgePayload.EdgeKind.Value == "link" && edgePayload.Cost == 1 &&
                edgePayload.CapacityUnits == 100 && edgePayload.AvailabilityPpm == 1_000_000,
            "Canonical Infrastructure edge payload drifted.");

        VerifyEveryNodeHasFiveOutgoingEdges(records);
    }

    private static InfrastructureNetworkTopologyRecordMaterialV2[] MaterializeNetworkZeroSlice()
    {
        var records = new List<InfrastructureNetworkTopologyRecordMaterialV2>(1_201);
        records.Add(Qa04InfrastructureNetworkMaterializerV1.CreateNetwork(0, ScopeForTile, out var networkBinding));
        Require(networkBinding.MaterialClass.Value == "network" && networkBinding.LocalOrdinal == 0 &&
                networkBinding.AuthoritativeRecordId == records[0].RecordId,
            "Infrastructure network descriptor mapping evidence drifted.");

        for (uint node = 0; node < Qa04InfrastructureNetworkMaterializerV1.NodesPerNetwork; node++)
            records.Add(Qa04InfrastructureNetworkMaterializerV1.CreateNode(node, ScopeForTile, out _));
        for (uint edge = 0; edge < Qa04InfrastructureNetworkMaterializerV1.EdgesPerNetwork; edge++)
            records.Add(Qa04InfrastructureNetworkMaterializerV1.CreateEdge(edge, out _));

        return records.ToArray();
    }

    private static void VerifyEveryNodeHasFiveOutgoingEdges(
        IReadOnlyList<InfrastructureNetworkTopologyRecordMaterialV2> records)
    {
        var outgoing = records
            .Where(static record => record.Payload is InfrastructureNetworkEdgePayloadV2)
            .Select(static record => (InfrastructureNetworkEdgePayloadV2)record.Payload)
            .GroupBy(static edge => edge.FromNodeRef.RecordId)
            .ToDictionary(static group => group.Key, static group => group.Count());
        var nodeIds = records
            .Where(static record => record.Payload is InfrastructureNetworkNodePayloadV2)
            .Select(static record => record.RecordId)
            .ToArray();
        Require(nodeIds.All(nodeId => outgoing.TryGetValue(nodeId, out var count) && count == 5),
            "Every reduced-slice Infrastructure node must own exactly five outgoing links.");
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

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
