using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains.InfrastructureInformation;

/// <summary>
/// Alpha 1.1 exact heterogeneous infrastructure.network_topology record schema v2.
/// StandardDomainPartitionRegistry remains on v1 until the migration-aware production path is selected
/// from the actual authority schema.
/// </summary>
public static class InfrastructureNetworkTopologyRecordSchemaV2
{
    public const string PartitionId = InfrastructureNetworkTopologyPayloadV1.PartitionId;
    public const string NetworkKind = "network";
    public const string NodeKind = "node";
    public const string EdgeKind = "edge";

    public static SchemaRefV1 RecordSchema { get; } =
        new("domain.infrastructure.network_topology.record", 2, 0);

    public static IReadOnlyList<string> RecordKinds { get; } = Array.AsReadOnly(new[]
    {
        EdgeKind,
        NetworkKind,
        NodeKind,
    });

    public static IReadOnlySet<string> NodeKinds { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "facility", "junction", "terminal",
    };

    public static IReadOnlySet<string> Statuses { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "active", "degraded", "offline", "retired",
    };

    public static void ValidateCanonicalContract()
    {
        var current = StandardDomainPartitionRegistry.Get(PartitionId);
        if (current.RecordSchema.SchemaId != RecordSchema.SchemaId ||
            current.RecordSchema.Version != new SchemaVersionV1(1, 0) ||
            RecordSchema.Version != new SchemaVersionV1(2, 0))
            throw new InvalidDataException("infrastructure.network-topology-v2.schema-version-contract");
        if (RecordKinds.Count != 3 ||
            RecordKinds.Distinct(StringComparer.Ordinal).Count() != 3 ||
            !RecordKinds.SequenceEqual(RecordKinds.OrderBy(static value => value, StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException("infrastructure.network-topology-v2.record-kind-contract");
        if (!NodeKinds.SetEquals(new[] { "facility", "junction", "terminal" }))
            throw new InvalidDataException("infrastructure.network-topology-v2.node-kind-contract");
        if (!Statuses.SetEquals(new[] { "active", "degraded", "offline", "retired" }))
            throw new InvalidDataException("infrastructure.network-topology-v2.status-contract");
    }
}

public abstract class InfrastructureNetworkTopologyRecordPayloadV2
{
    public abstract string RecordKind { get; }
}

public sealed class InfrastructureNetworkPayloadV2 : InfrastructureNetworkTopologyRecordPayloadV2
{
    public InfrastructureNetworkPayloadV2(
        StableToken networkKind,
        IEnumerable<PartitionRecordRefV1> nodeRefs,
        IEnumerable<PartitionRecordRefV1> edgeRefs,
        IEnumerable<PartitionRecordRefV1> operatorRefs,
        IEnumerable<PartitionRecordRefV1> scopeRefs,
        StableToken status,
        ulong topologyRevision)
    {
        ArgumentNullException.ThrowIfNull(nodeRefs);
        ArgumentNullException.ThrowIfNull(edgeRefs);
        ArgumentNullException.ThrowIfNull(operatorRefs);
        ArgumentNullException.ThrowIfNull(scopeRefs);
        if (topologyRevision == 0)
            throw new ArgumentOutOfRangeException(nameof(topologyRevision));
        if (!InfrastructureNetworkTopologyRecordSchemaV2.Statuses.Contains(status.Value))
            throw new InvalidDataException("infrastructure.network-topology-v2.network-status");

        NodeRefs = CanonicalRefs(nodeRefs, InfrastructureNetworkTopologyRecordSchemaV2.PartitionId, "network-node-refs");
        EdgeRefs = CanonicalRefs(edgeRefs, InfrastructureNetworkTopologyRecordSchemaV2.PartitionId, "network-edge-refs");
        OperatorRefs = CanonicalRefs(operatorRefs, expectedPartition: null, "network-operator-refs");
        ScopeRefs = CanonicalRefs(scopeRefs, "spatial.scope_registry", "network-scope-refs");
        NetworkKind = networkKind;
        Status = status;
        TopologyRevision = topologyRevision;
    }

    public override string RecordKind => InfrastructureNetworkTopologyRecordSchemaV2.NetworkKind;
    public StableToken NetworkKind { get; }
    public IReadOnlyList<PartitionRecordRefV1> NodeRefs { get; }
    public IReadOnlyList<PartitionRecordRefV1> EdgeRefs { get; }
    public IReadOnlyList<PartitionRecordRefV1> OperatorRefs { get; }
    public IReadOnlyList<PartitionRecordRefV1> ScopeRefs { get; }
    public StableToken Status { get; }
    public ulong TopologyRevision { get; }

    private static IReadOnlyList<PartitionRecordRefV1> CanonicalRefs(
        IEnumerable<PartitionRecordRefV1> source,
        string? expectedPartition,
        string failureSuffix)
    {
        var refs = source.ToArray();
        if (refs.Any(reference => reference.RecordId.IsZero ||
                                  (expectedPartition is not null && reference.PartitionId.Value != expectedPartition)))
            throw new InvalidDataException($"infrastructure.network-topology-v2.{failureSuffix}-target");
        var canonical = refs
            .OrderBy(static reference => reference.PartitionId.Value, StringComparer.Ordinal)
            .ThenBy(static reference => reference.RecordId)
            .ToArray();
        if (!refs.SequenceEqual(canonical) || refs.Distinct().Count() != refs.Length)
            throw new InvalidDataException($"infrastructure.network-topology-v2.{failureSuffix}-canonical");
        return Array.AsReadOnly(refs);
    }
}

public sealed class InfrastructureNetworkNodePayloadV2 : InfrastructureNetworkTopologyRecordPayloadV2
{
    public InfrastructureNetworkNodePayloadV2(
        PartitionRecordRefV1 networkRef,
        StableToken nodeKind,
        PartitionRecordRefV1 scopeRef,
        ulong capacityUnits,
        uint availabilityPpm,
        StableToken status)
    {
        RequireSamePartition(networkRef, "node-network-ref");
        if (scopeRef.PartitionId.Value != "spatial.scope_registry" || scopeRef.RecordId.IsZero)
            throw new InvalidDataException("infrastructure.network-topology-v2.node-scope-ref");
        if (!InfrastructureNetworkTopologyRecordSchemaV2.NodeKinds.Contains(nodeKind.Value))
            throw new InvalidDataException("infrastructure.network-topology-v2.node-kind");
        if (availabilityPpm > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(availabilityPpm));
        if (!InfrastructureNetworkTopologyRecordSchemaV2.Statuses.Contains(status.Value))
            throw new InvalidDataException("infrastructure.network-topology-v2.node-status");

        NetworkRef = networkRef;
        NodeKind = nodeKind;
        ScopeRef = scopeRef;
        CapacityUnits = capacityUnits;
        AvailabilityPpm = availabilityPpm;
        Status = status;
    }

    public override string RecordKind => InfrastructureNetworkTopologyRecordSchemaV2.NodeKind;
    public PartitionRecordRefV1 NetworkRef { get; }
    public StableToken NodeKind { get; }
    public PartitionRecordRefV1 ScopeRef { get; }
    public ulong CapacityUnits { get; }
    public uint AvailabilityPpm { get; }
    public StableToken Status { get; }

    private static void RequireSamePartition(PartitionRecordRefV1 reference, string suffix)
    {
        if (reference.PartitionId.Value != InfrastructureNetworkTopologyRecordSchemaV2.PartitionId || reference.RecordId.IsZero)
            throw new InvalidDataException($"infrastructure.network-topology-v2.{suffix}");
    }
}

public sealed class InfrastructureNetworkEdgePayloadV2 : InfrastructureNetworkTopologyRecordPayloadV2
{
    public InfrastructureNetworkEdgePayloadV2(
        PartitionRecordRefV1 networkRef,
        PartitionRecordRefV1 fromNodeRef,
        PartitionRecordRefV1 toNodeRef,
        StableToken edgeKind,
        ulong cost,
        ulong capacityUnits,
        uint availabilityPpm,
        StableToken status)
    {
        RequireSamePartition(networkRef, "edge-network-ref");
        RequireSamePartition(fromNodeRef, "edge-from-node-ref");
        RequireSamePartition(toNodeRef, "edge-to-node-ref");
        if (fromNodeRef == toNodeRef)
            throw new InvalidDataException("infrastructure.network-topology-v2.edge-self");
        if (edgeKind.Value != "link")
            throw new InvalidDataException("infrastructure.network-topology-v2.edge-kind");
        if (availabilityPpm > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(availabilityPpm));
        if (!InfrastructureNetworkTopologyRecordSchemaV2.Statuses.Contains(status.Value))
            throw new InvalidDataException("infrastructure.network-topology-v2.edge-status");

        NetworkRef = networkRef;
        FromNodeRef = fromNodeRef;
        ToNodeRef = toNodeRef;
        EdgeKind = edgeKind;
        Cost = cost;
        CapacityUnits = capacityUnits;
        AvailabilityPpm = availabilityPpm;
        Status = status;
    }

    public override string RecordKind => InfrastructureNetworkTopologyRecordSchemaV2.EdgeKind;
    public PartitionRecordRefV1 NetworkRef { get; }
    public PartitionRecordRefV1 FromNodeRef { get; }
    public PartitionRecordRefV1 ToNodeRef { get; }
    public StableToken EdgeKind { get; }
    public ulong Cost { get; }
    public ulong CapacityUnits { get; }
    public uint AvailabilityPpm { get; }
    public StableToken Status { get; }

    private static void RequireSamePartition(PartitionRecordRefV1 reference, string suffix)
    {
        if (reference.PartitionId.Value != InfrastructureNetworkTopologyRecordSchemaV2.PartitionId || reference.RecordId.IsZero)
            throw new InvalidDataException($"infrastructure.network-topology-v2.{suffix}");
    }
}

public sealed class InfrastructureNetworkTopologyRecordMaterialV2
{
    public InfrastructureNetworkTopologyRecordMaterialV2(
        OpaqueId128 recordId,
        ulong revision,
        ulong createdStep,
        ulong? retiredStep,
        DetailLevelV1 detailLevel,
        OpaqueId128? lineageRef,
        InfrastructureNetworkTopologyRecordPayloadV2 payload)
    {
        if (recordId.IsZero) throw new ArgumentException("Record id ZERO is invalid.", nameof(recordId));
        if (revision == 0) throw new ArgumentOutOfRangeException(nameof(revision));
        if (retiredStep is { } retired && retired < createdStep)
            throw new ArgumentOutOfRangeException(nameof(retiredStep));
        if (lineageRef is { IsZero: true })
            throw new ArgumentException("Lineage id ZERO is invalid.", nameof(lineageRef));
        if (!Enum.IsDefined(detailLevel)) throw new ArgumentOutOfRangeException(nameof(detailLevel));
        ArgumentNullException.ThrowIfNull(payload);

        RecordId = recordId;
        Revision = revision;
        CreatedStep = createdStep;
        RetiredStep = retiredStep;
        DetailLevel = detailLevel;
        LineageRef = lineageRef;
        Payload = payload;
    }

    public OpaqueId128 RecordId { get; }
    public SchemaRefV1 RecordSchema => InfrastructureNetworkTopologyRecordSchemaV2.RecordSchema;
    public ulong Revision { get; }
    public ulong CreatedStep { get; }
    public ulong? RetiredStep { get; }
    public DetailLevelV1 DetailLevel { get; }
    public OpaqueId128? LineageRef { get; }
    public InfrastructureNetworkTopologyRecordPayloadV2 Payload { get; }

    public static InfrastructureNetworkTopologyRecordMaterialV2 MigrateLegacyNetwork(
        DomainRecordEnvelopeV1<InfrastructureNetworkTopologyPayloadV1> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var current = StandardDomainPartitionRegistry.Get(InfrastructureNetworkTopologyRecordSchemaV2.PartitionId);
        if (source.RecordSchema != current.RecordSchema)
            throw new InvalidDataException("infrastructure.network-topology-v2.migration-source-schema");

        var payload = source.Payload;
        return new InfrastructureNetworkTopologyRecordMaterialV2(
            source.RecordId,
            source.Revision,
            source.CreatedStep,
            source.RetiredStep,
            source.DetailLevel,
            source.LineageRef,
            new InfrastructureNetworkPayloadV2(
                payload.NetworkKind,
                payload.NodeRefs,
                payload.EdgeRefs,
                payload.OperatorRefs,
                payload.ScopeRefs,
                payload.Status,
                payload.TopologyRevision));
    }
}
