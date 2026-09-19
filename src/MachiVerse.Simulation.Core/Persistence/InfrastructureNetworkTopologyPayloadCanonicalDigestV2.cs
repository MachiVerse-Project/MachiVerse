using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

public static class InfrastructureNetworkTopologyPayloadCanonicalDigestV2
{
    public static byte[] Compute(
        InfrastructureNetworkTopologyRecordPayloadV2 payload,
        IDomainRecordSchemaResolverV1? references = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        InfrastructureNetworkTopologyRecordSchemaV2.ValidateCanonicalContract();
        return HashSuite.DomainHash(StandardDomainPayloadCanonicalDigestV1.HashDomain, writer =>
        {
            writer.WriteMapStart(5);
            writer.WriteUnsigned(0); writer.WriteAsciiText(InfrastructureNetworkTopologyRecordSchemaV2.PartitionId);
            writer.WriteUnsigned(1); writer.WriteAsciiText(InfrastructureNetworkTopologyRecordSchemaV2.RecordSchema.SchemaId.Value);
            writer.WriteUnsigned(2); writer.WriteUnsigned(InfrastructureNetworkTopologyRecordSchemaV2.RecordSchema.Version.Major);
            writer.WriteUnsigned(3); writer.WriteUnsigned(InfrastructureNetworkTopologyRecordSchemaV2.RecordSchema.Version.Minor);
            writer.WriteUnsigned(4); WritePayload(writer, payload, references);
        });
    }

    private static void WritePayload(
        MvDcborWriter writer,
        InfrastructureNetworkTopologyRecordPayloadV2 payload,
        IDomainRecordSchemaResolverV1? references)
    {
        switch (payload)
        {
            case InfrastructureNetworkPayloadV2 network:
                writer.WriteArrayStart(8);
                Field(writer, 0, w => w.WriteAsciiText(network.RecordKind));
                Field(writer, 1, w => w.WriteAsciiText(network.NetworkKind.Value));
                Field(writer, 2, w => WriteReferences(w, network.NodeRefs, references, "node_refs", InfrastructureNetworkTopologyRecordSchemaV2.RecordSchema));
                Field(writer, 3, w => WriteReferences(w, network.EdgeRefs, references, "edge_refs", InfrastructureNetworkTopologyRecordSchemaV2.RecordSchema));
                Field(writer, 4, w => WriteReferences(w, network.OperatorRefs, references, "operator_refs", null));
                Field(writer, 5, w => WriteReferences(w, network.ScopeRefs, references, "scope_refs", StandardDomainPartitionRegistry.Get("spatial.scope_registry").RecordSchema));
                Field(writer, 6, w => w.WriteAsciiText(network.Status.Value));
                Field(writer, 7, w => w.WriteUnsigned(network.TopologyRevision));
                break;
            case InfrastructureNetworkNodePayloadV2 node:
                writer.WriteArrayStart(7);
                Field(writer, 0, w => w.WriteAsciiText(node.RecordKind));
                Field(writer, 1, w => WriteReference(w, node.NetworkRef, references, "network_ref", InfrastructureNetworkTopologyRecordSchemaV2.RecordSchema));
                Field(writer, 2, w => w.WriteAsciiText(node.NodeKind.Value));
                Field(writer, 3, w => WriteReference(w, node.ScopeRef, references, "scope_ref", StandardDomainPartitionRegistry.Get("spatial.scope_registry").RecordSchema));
                Field(writer, 4, w => w.WriteUnsigned(node.CapacityUnits));
                Field(writer, 5, w => w.WriteUnsigned(node.AvailabilityPpm));
                Field(writer, 6, w => w.WriteAsciiText(node.Status.Value));
                break;
            case InfrastructureNetworkEdgePayloadV2 edge:
                writer.WriteArrayStart(9);
                Field(writer, 0, w => w.WriteAsciiText(edge.RecordKind));
                Field(writer, 1, w => WriteReference(w, edge.NetworkRef, references, "network_ref", InfrastructureNetworkTopologyRecordSchemaV2.RecordSchema));
                Field(writer, 2, w => WriteReference(w, edge.FromNodeRef, references, "from_node_ref", InfrastructureNetworkTopologyRecordSchemaV2.RecordSchema));
                Field(writer, 3, w => WriteReference(w, edge.ToNodeRef, references, "to_node_ref", InfrastructureNetworkTopologyRecordSchemaV2.RecordSchema));
                Field(writer, 4, w => w.WriteAsciiText(edge.EdgeKind.Value));
                Field(writer, 5, w => w.WriteUnsigned(edge.Cost));
                Field(writer, 6, w => w.WriteUnsigned(edge.CapacityUnits));
                Field(writer, 7, w => w.WriteUnsigned(edge.AvailabilityPpm));
                Field(writer, 8, w => w.WriteAsciiText(edge.Status.Value));
                break;
            default:
                throw new InvalidDataException("domain.payload.digest-infrastructure-network-v2-kind");
        }
    }

    private static void WriteReferences(
        MvDcborWriter writer,
        IReadOnlyList<PartitionRecordRefV1> referencesValue,
        IDomainRecordSchemaResolverV1? references,
        string field,
        SchemaRefV1? requiredSchema)
    {
        writer.WriteArrayStart(checked((ulong)referencesValue.Count));
        foreach (var reference in referencesValue)
            WriteReference(writer, reference, references, field, requiredSchema);
    }

    private static void WriteReference(
        MvDcborWriter writer,
        PartitionRecordRefV1 reference,
        IDomainRecordSchemaResolverV1? references,
        string field,
        SchemaRefV1? requiredSchema)
    {
        if (reference.RecordId.IsZero)
            throw new InvalidDataException($"domain.payload.digest-infrastructure-network-v2-ref-zero:{field}");
        _ = StandardDomainPartitionRegistry.Get(reference.PartitionId.Value);
        if (references is not null)
        {
            if (!references.TryGetRecordSchema(reference, out var actual))
                throw new InvalidDataException($"domain.payload.digest-reference-missing:{InfrastructureNetworkTopologyRecordSchemaV2.PartitionId}:{field}");
            if (requiredSchema is { } expected && actual != expected)
                throw new InvalidDataException($"domain.payload.digest-reference-schema:{InfrastructureNetworkTopologyRecordSchemaV2.PartitionId}:{field}");
        }
        writer.WriteArrayStart(2);
        writer.WriteAsciiText(reference.PartitionId.Value);
        writer.WriteBytes(reference.RecordId.ToBytes());
    }

    private static void Field(MvDcborWriter writer, ulong ordinal, Action<MvDcborWriter> value)
    {
        writer.WriteArrayStart(2);
        writer.WriteUnsigned(ordinal);
        value(writer);
    }
}
