using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Domains.InfrastructureInformation;

/// <summary>
/// Enforces the heterogeneous intra-partition closure of infrastructure.network_topology /2.0.
/// Networks own their listed nodes/edges; edges may only connect nodes owned by the same network.
/// </summary>
public static class InfrastructureNetworkTopologyReferenceClosureV2
{
    public static void Validate(IReadOnlyList<InfrastructureNetworkTopologyRecordMaterialV2> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var byId = new Dictionary<OpaqueId128, InfrastructureNetworkTopologyRecordMaterialV2>();
        foreach (var record in records)
        {
            ArgumentNullException.ThrowIfNull(record);
            if (!byId.TryAdd(record.RecordId, record))
                throw Error("record-id-duplicate");
        }

        var listedNodes = new HashSet<OpaqueId128>();
        var listedEdges = new HashSet<OpaqueId128>();
        foreach (var record in records)
        {
            if (record.Payload is not InfrastructureNetworkPayloadV2 network) continue;
            foreach (var nodeRef in network.NodeRefs)
            {
                var nodeRecord = RequireTarget(byId, nodeRef.RecordId, InfrastructureNetworkTopologyRecordSchemaV2.NodeKind, "network-node");
                var node = (InfrastructureNetworkNodePayloadV2)nodeRecord.Payload;
                RequireNetworkRef(node.NetworkRef, record.RecordId, "network-node-owner");
                if (!listedNodes.Add(nodeRef.RecordId)) throw Error("node-listed-more-than-once");
            }
            foreach (var edgeRef in network.EdgeRefs)
            {
                var edgeRecord = RequireTarget(byId, edgeRef.RecordId, InfrastructureNetworkTopologyRecordSchemaV2.EdgeKind, "network-edge");
                var edge = (InfrastructureNetworkEdgePayloadV2)edgeRecord.Payload;
                RequireNetworkRef(edge.NetworkRef, record.RecordId, "network-edge-owner");
                if (!listedEdges.Add(edgeRef.RecordId)) throw Error("edge-listed-more-than-once");
            }
        }

        foreach (var record in records)
        {
            switch (record.Payload)
            {
                case InfrastructureNetworkNodePayloadV2 node:
                    RequireTarget(byId, node.NetworkRef.RecordId, InfrastructureNetworkTopologyRecordSchemaV2.NetworkKind, "node-network");
                    if (!listedNodes.Contains(record.RecordId)) throw Error("orphan-node");
                    break;
                case InfrastructureNetworkEdgePayloadV2 edge:
                    RequireTarget(byId, edge.NetworkRef.RecordId, InfrastructureNetworkTopologyRecordSchemaV2.NetworkKind, "edge-network");
                    var from = RequireTarget(byId, edge.FromNodeRef.RecordId, InfrastructureNetworkTopologyRecordSchemaV2.NodeKind, "edge-from-node");
                    var to = RequireTarget(byId, edge.ToNodeRef.RecordId, InfrastructureNetworkTopologyRecordSchemaV2.NodeKind, "edge-to-node");
                    RequireNetworkRef(((InfrastructureNetworkNodePayloadV2)from.Payload).NetworkRef, edge.NetworkRef.RecordId, "edge-from-node-network");
                    RequireNetworkRef(((InfrastructureNetworkNodePayloadV2)to.Payload).NetworkRef, edge.NetworkRef.RecordId, "edge-to-node-network");
                    if (!listedEdges.Contains(record.RecordId)) throw Error("orphan-edge");
                    break;
            }
        }
    }

    private static InfrastructureNetworkTopologyRecordMaterialV2 RequireTarget(
        IReadOnlyDictionary<OpaqueId128, InfrastructureNetworkTopologyRecordMaterialV2> byId,
        OpaqueId128 id,
        string expectedKind,
        string suffix)
    {
        if (!byId.TryGetValue(id, out var target)) throw Error($"{suffix}-missing");
        if (!string.Equals(target.Payload.RecordKind, expectedKind, StringComparison.Ordinal))
            throw Error($"{suffix}-target-kind");
        return target;
    }

    private static void RequireNetworkRef(
        WorldState.PartitionRecordRefV1 reference,
        OpaqueId128 expectedNetworkId,
        string suffix)
    {
        if (reference.PartitionId.Value != InfrastructureNetworkTopologyRecordSchemaV2.PartitionId ||
            reference.RecordId != expectedNetworkId)
            throw Error(suffix);
    }

    private static InvalidDataException Error(string suffix)
        => new($"infrastructure.network-topology-v2.reference-closure:{suffix}");
}
