using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains.InfrastructureInformation;

public static class InfrastructureNetworkTopologyPartitionIdentityV2
{
    public static DomainPartitionIdentityV1 Identity { get; } = Create();

    public static void ValidateCanonicalContract()
    {
        InfrastructureNetworkTopologyRecordSchemaV2.ValidateCanonicalContract();
        var current = StandardDomainPartitionRegistry.Get(InfrastructureNetworkTopologyRecordSchemaV2.PartitionId);
        if (Identity.PartitionId != current.PartitionId ||
            Identity.OwnerDomain != current.OwnerDomain ||
            Identity.OwnerDomainRank != current.OwnerDomainRank ||
            Identity.PartitionSchema != current.PartitionSchema ||
            Identity.PrimaryKeyKind != current.PrimaryKeyKind ||
            Identity.PersistenceClass != current.PersistenceClass ||
            Identity.CanonicalOrder != current.CanonicalOrder)
            throw new InvalidDataException("infrastructure.network-topology-v2.partition-identity-drift");
        if (Identity.RecordSchema != InfrastructureNetworkTopologyRecordSchemaV2.RecordSchema)
            throw new InvalidDataException("infrastructure.network-topology-v2.partition-record-schema");
        if (current.RecordSchema.Version != new SchemaVersionV1(1, 0))
            throw new InvalidDataException("infrastructure.network-topology-v2.production-registry-flipped");
    }

    private static DomainPartitionIdentityV1 Create()
    {
        var current = StandardDomainPartitionRegistry.Get(InfrastructureNetworkTopologyRecordSchemaV2.PartitionId);
        return current with { RecordSchema = InfrastructureNetworkTopologyRecordSchemaV2.RecordSchema };
    }
}

public sealed class InfrastructureNetworkTopologyRecordSetV2
{
    private readonly SortedDictionary<OpaqueId128, InfrastructureNetworkTopologyRecordMaterialV2> _records = new();

    public InfrastructureNetworkTopologyRecordSetV2(IEnumerable<InfrastructureNetworkTopologyRecordMaterialV2> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        foreach (var record in records)
        {
            ArgumentNullException.ThrowIfNull(record);
            if (!_records.TryAdd(record.RecordId, record))
                throw new InvalidDataException("infrastructure.network-topology-v2.record-id-duplicate");
        }
    }

    public IReadOnlyList<InfrastructureNetworkTopologyRecordMaterialV2> RecordsCanonical
        => Array.AsReadOnly(_records.Values.ToArray());

    public bool TryGet(OpaqueId128 recordId, out InfrastructureNetworkTopologyRecordMaterialV2? record)
        => _records.TryGetValue(recordId, out record);
}

public sealed class InfrastructureNetworkTopologyPartitionStateV2
{
    public InfrastructureNetworkTopologyPartitionStateV2(IEnumerable<InfrastructureNetworkTopologyRecordMaterialV2> records)
    {
        InfrastructureNetworkTopologyPartitionIdentityV2.ValidateCanonicalContract();
        RecordSet = new InfrastructureNetworkTopologyRecordSetV2(records);
        InfrastructureNetworkTopologyReferenceClosureV2.Validate(RecordSet.RecordsCanonical);
        var envelopes = RecordSet.RecordsCanonical.Select(static record =>
            new DomainRecordEnvelopeV1<InfrastructureNetworkTopologyRecordPayloadV2>(
                record.RecordId,
                InfrastructureNetworkTopologyRecordSchemaV2.RecordSchema,
                record.Revision,
                record.CreatedStep,
                record.RetiredStep,
                record.DetailLevel,
                record.LineageRef,
                record.Payload));
        State = new DomainPartitionStateV1<InfrastructureNetworkTopologyRecordPayloadV2>(
            InfrastructureNetworkTopologyPartitionIdentityV2.Identity,
            envelopes);
        if (State.ItemCount != checked((ulong)RecordSet.RecordsCanonical.Count))
            throw new InvalidDataException("infrastructure.network-topology-v2.partition-item-count");
    }

    public InfrastructureNetworkTopologyRecordSetV2 RecordSet { get; }
    public DomainPartitionStateV1<InfrastructureNetworkTopologyRecordPayloadV2> State { get; }
}
