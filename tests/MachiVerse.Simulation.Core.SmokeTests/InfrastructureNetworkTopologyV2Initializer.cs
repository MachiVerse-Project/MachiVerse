using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.WorldState;

internal static class InfrastructureNetworkTopologyV2Initializer
{
    [ModuleInitializer]
    internal static void Run()
    {
        InfrastructureNetworkTopologyRecordSchemaV2.ValidateCanonicalContract();

        var networkId = Id(0x11);
        var nodeAId = Id(0x21);
        var nodeBId = Id(0x22);
        var edgeId = Id(0x31);
        var scopeId = Id(0x41);
        var operatorId = Id(0x51);
        var networkRef = Ref(InfrastructureNetworkTopologyRecordSchemaV2.PartitionId, networkId);
        var nodeARef = Ref(InfrastructureNetworkTopologyRecordSchemaV2.PartitionId, nodeAId);
        var nodeBRef = Ref(InfrastructureNetworkTopologyRecordSchemaV2.PartitionId, nodeBId);
        var edgeRef = Ref(InfrastructureNetworkTopologyRecordSchemaV2.PartitionId, edgeId);
        var scopeRef = Ref("spatial.scope_registry", scopeId);
        var operatorRef = Ref("society.organization", operatorId);

        var network = new InfrastructureNetworkPayloadV2(
            new StableToken("transport"),
            new[] { nodeARef, nodeBRef },
            new[] { edgeRef },
            new[] { operatorRef },
            new[] { scopeRef },
            new StableToken("active"),
            topologyRevision: 1);
        var node = new InfrastructureNetworkNodePayloadV2(
            networkRef,
            new StableToken("junction"),
            scopeRef,
            capacityUnits: 1_000,
            availabilityPpm: 1_000_000,
            new StableToken("active"));
        var edge = new InfrastructureNetworkEdgePayloadV2(
            networkRef,
            nodeARef,
            nodeBRef,
            new StableToken("link"),
            cost: 1,
            capacityUnits: 100,
            availabilityPpm: 1_000_000,
            new StableToken("active"));

        Require(network.RecordKind == InfrastructureNetworkTopologyRecordSchemaV2.NetworkKind &&
                node.RecordKind == InfrastructureNetworkTopologyRecordSchemaV2.NodeKind &&
                edge.RecordKind == InfrastructureNetworkTopologyRecordSchemaV2.EdgeKind,
            "Infrastructure v2 record-kind discriminator drifted.");

        var v1Identity = StandardDomainPartitionRegistry.Get(InfrastructureNetworkTopologyRecordSchemaV2.PartitionId);
        var legacy = new DomainRecordEnvelopeV1<InfrastructureNetworkTopologyPayloadV1>(
            networkId,
            v1Identity.RecordSchema,
            revision: 7,
            createdStep: 3,
            retiredStep: null,
            detailLevel: DetailLevelV1.D2RegionalAggregate,
            lineageRef: null,
            new InfrastructureNetworkTopologyPayloadV1(
                new StableToken("transport"),
                new[] { nodeARef, nodeBRef },
                new[] { edgeRef },
                new[] { operatorRef },
                new[] { scopeRef },
                new StableToken("active"),
                TopologyRevision: 9));
        var migrated = InfrastructureNetworkTopologyRecordMaterialV2.MigrateLegacyNetwork(legacy);
        var migratedPayload = migrated.Payload as InfrastructureNetworkPayloadV2
            ?? throw new InvalidOperationException("Infrastructure v1 migration must produce network arm.");
        Require(migrated.RecordId == legacy.RecordId && migrated.Revision == legacy.Revision &&
                migrated.CreatedStep == legacy.CreatedStep && migrated.DetailLevel == legacy.DetailLevel &&
                migrated.RecordSchema == InfrastructureNetworkTopologyRecordSchemaV2.RecordSchema &&
                migratedPayload.NetworkKind == legacy.Payload.NetworkKind &&
                migratedPayload.NodeRefs.SequenceEqual(legacy.Payload.NodeRefs) &&
                migratedPayload.EdgeRefs.SequenceEqual(legacy.Payload.EdgeRefs) &&
                migratedPayload.TopologyRevision == legacy.Payload.TopologyRevision,
            "Infrastructure v1->v2 migration must preserve legacy network semantics.");

        ExpectFailure(() => new InfrastructureNetworkEdgePayloadV2(
            networkRef, nodeARef, nodeARef, new StableToken("link"), 1, 100, 1_000_000, new StableToken("active")),
            "Self-edge must fail closed.");
        ExpectFailure(() => new InfrastructureNetworkNodePayloadV2(
            networkRef, new StableToken("unknown"), scopeRef, 1_000, 1_000_000, new StableToken("active")),
            "Unknown node kind must fail closed.");
        ExpectFailure(() => new InfrastructureNetworkNodePayloadV2(
            networkRef, new StableToken("junction"), scopeRef, 1_000, 1_000_001, new StableToken("active")),
            "Availability above one million ppm must fail closed.");
        ExpectFailure(() => new InfrastructureNetworkPayloadV2(
            new StableToken("transport"),
            new[] { nodeBRef, nodeARef },
            new[] { edgeRef },
            new[] { operatorRef },
            new[] { scopeRef },
            new StableToken("active"),
            1),
            "Unsorted network node refs must fail closed.");
    }

    private static PartitionRecordRefV1 Ref(string partitionId, OpaqueId128 id) => new(partitionId, id);

    private static OpaqueId128 Id(byte value)
        => OpaqueId128.FromBytes(Enumerable.Repeat(value, 16).ToArray());

    private static void ExpectFailure(Action action, string message)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or ArgumentOutOfRangeException)
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
