using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static PartitionRecordRefV1 ScopeForTile(ushort tile)
    => new(
        "spatial.scope_registry",
        DerivedIdentity.DeriveEntityId(
            Qa04ReferenceLoadV1.WorldId,
            creationStep: 0,
            new StableToken("spatial"),
            OpaqueId128.Zero,
            new StableToken("smoke.infrastructure-tile-scope"),
            tile));

static void VerifyEveryNodeHasFiveOutgoingEdges(
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
        "Every canonical Infrastructure node must own exactly five outgoing links.");
}

static void VerifyWrongNetworkEdgeFailsClosed(
    IReadOnlyList<InfrastructureNetworkTopologyRecordMaterialV2> records)
{
    var network0 = records.Single(record => record.RecordId == Qa04InfrastructureNetworkMaterializerV1.NetworkId(0));
    var nodeIds = ((InfrastructureNetworkPayloadV2)network0.Payload).NodeRefs.Select(static reference => reference.RecordId).ToHashSet();
    var edgeIds = ((InfrastructureNetworkPayloadV2)network0.Payload).EdgeRefs.Select(static reference => reference.RecordId).ToHashSet();
    var slice = records.Where(record => record.RecordId == network0.RecordId || nodeIds.Contains(record.RecordId) || edgeIds.Contains(record.RecordId)).ToArray();
    var edgeIndex = Array.FindIndex(slice, static record => record.Payload is InfrastructureNetworkEdgePayloadV2);
    Require(edgeIndex >= 0, "Network zero closure fixture must include an edge.");
    var original = slice[edgeIndex];
    var edge = (InfrastructureNetworkEdgePayloadV2)original.Payload;
    slice[edgeIndex] = new InfrastructureNetworkTopologyRecordMaterialV2(
        original.RecordId,
        original.Revision,
        original.CreatedStep,
        original.RetiredStep,
        original.DetailLevel,
        original.LineageRef,
        new InfrastructureNetworkEdgePayloadV2(
            new PartitionRecordRefV1(
                InfrastructureNetworkTopologyRecordSchemaV2.PartitionId,
                Qa04InfrastructureNetworkMaterializerV1.NetworkId(1)),
            edge.FromNodeRef,
            edge.ToNodeRef,
            edge.EdgeKind,
            edge.Cost,
            edge.CapacityUnits,
            edge.AvailabilityPpm,
            edge.Status));

    try
    {
        InfrastructureNetworkTopologyReferenceClosureV2.Validate(slice);
    }
    catch (InvalidDataException)
    {
        return;
    }

    throw new InvalidOperationException("Edge owned by a different network must fail topology closure.");
}

Qa04InfrastructureNetworkMaterializerV1.ValidateCanonicalContract();
Console.WriteLine($"Validating full canonical Infrastructure topology ({Qa04InfrastructureNetworkMaterializerV1.CanonicalTopologyRecordCount:N0} records)...");

var records = Qa04InfrastructureNetworkMaterializerV1
    .MaterializeCanonicalTopology(ScopeForTile)
    .ToArray();

Require(records.Length == 120_100,
    "Canonical Infrastructure topology must materialize exactly 120,100 records.");
Require(records.Count(static record => record.Payload is InfrastructureNetworkPayloadV2) == 100,
    "Canonical Infrastructure topology must contain exactly 100 networks.");
Require(records.Count(static record => record.Payload is InfrastructureNetworkNodePayloadV2) == 20_000,
    "Canonical Infrastructure topology must contain exactly 20,000 nodes.");
Require(records.Count(static record => record.Payload is InfrastructureNetworkEdgePayloadV2) == 100_000,
    "Canonical Infrastructure topology must contain exactly 100,000 edges.");
Require(records.Select(static record => record.RecordId).Distinct().Count() == records.Length,
    "Canonical Infrastructure topology record ids must be globally unique.");

InfrastructureNetworkTopologyReferenceClosureV2.Validate(records);

var firstNetwork = Qa04InfrastructureNetworkMaterializerV1.CreateNetwork(0, ScopeForTile, out var networkBinding);
var networkPayload = firstNetwork.Payload as InfrastructureNetworkPayloadV2
    ?? throw new InvalidOperationException("First Infrastructure topology record must be a network.");
Require(networkPayload.NetworkKind.Value == "transport" &&
        networkPayload.NodeRefs.Count == 200 && networkPayload.EdgeRefs.Count == 1_000 &&
        networkPayload.ScopeRefs.Count == 1 && networkPayload.OperatorRefs.Count == 1 &&
        networkPayload.TopologyRevision == 1,
    "Canonical Infrastructure network payload drifted.");
Require(networkBinding.MaterialClass.Value == "network" && networkBinding.LocalOrdinal == 0 &&
        networkBinding.AuthoritativeRecordId == firstNetwork.RecordId,
    "Infrastructure network descriptor mapping evidence drifted.");

var firstNode = Qa04InfrastructureNetworkMaterializerV1.CreateNode(0, ScopeForTile, out var nodeBinding);
var nodePayload = firstNode.Payload as InfrastructureNetworkNodePayloadV2
    ?? throw new InvalidOperationException("First Infrastructure node must use node payload.");
Require(nodePayload.NetworkRef.RecordId == firstNetwork.RecordId &&
        nodePayload.NodeKind.Value == "junction" && nodePayload.CapacityUnits == 1_000 &&
        nodePayload.AvailabilityPpm == 1_000_000 && nodePayload.Status.Value == "active",
    "Canonical Infrastructure node payload drifted.");
Require(nodeBinding.MaterialClass.Value == "node" && nodeBinding.LocalOrdinal == 0,
    "Infrastructure node descriptor mapping evidence drifted.");

var firstEdge = Qa04InfrastructureNetworkMaterializerV1.CreateEdge(0, out var edgeBinding);
var edgePayload = firstEdge.Payload as InfrastructureNetworkEdgePayloadV2
    ?? throw new InvalidOperationException("First Infrastructure edge must use edge payload.");
Require(edgePayload.NetworkRef.RecordId == firstNetwork.RecordId &&
        edgePayload.FromNodeRef != edgePayload.ToNodeRef &&
        edgePayload.EdgeKind.Value == "link" && edgePayload.Cost == 1 &&
        edgePayload.CapacityUnits == 100 && edgePayload.AvailabilityPpm == 1_000_000,
    "Canonical Infrastructure edge payload drifted.");
Require(edgeBinding.MaterialClass.Value == "edge" && edgeBinding.LocalOrdinal == 0,
    "Infrastructure edge descriptor mapping evidence drifted.");

VerifyEveryNodeHasFiveOutgoingEdges(records);
VerifyWrongNetworkEdgeFailsClosed(records);

Console.WriteLine($"infrastructure-full-production-pass records={records.Length}");
