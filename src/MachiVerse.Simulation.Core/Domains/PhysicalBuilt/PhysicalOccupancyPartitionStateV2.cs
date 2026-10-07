using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains.PhysicalBuilt;

public static class PhysicalOccupancyPartitionIdentityV2
{
    public static DomainPartitionIdentityV1 Identity { get; } = Create();

    public static void ValidateCanonicalContract()
    {
        PhysicalOccupancyRecordSchemaV2.ValidateCanonicalContract();
        var current = StandardDomainPartitionRegistry.Get(PhysicalOccupancyRecordSchemaV2.PartitionId);
        if (Identity.PartitionId != current.PartitionId ||
            Identity.OwnerDomain != current.OwnerDomain ||
            Identity.OwnerDomainRank != current.OwnerDomainRank ||
            Identity.PartitionSchema != current.PartitionSchema ||
            Identity.PrimaryKeyKind != current.PrimaryKeyKind ||
            Identity.PersistenceClass != current.PersistenceClass ||
            Identity.CanonicalOrder != current.CanonicalOrder)
            throw new InvalidDataException("physical.occupancy-v2.partition-identity-drift");
        if (Identity.RecordSchema != PhysicalOccupancyRecordSchemaV2.RecordSchema)
            throw new InvalidDataException("physical.occupancy-v2.partition-record-schema");
        if (current.RecordSchema.Version != new SchemaVersionV1(1, 0))
            throw new InvalidDataException("physical.occupancy-v2.production-registry-flipped");
    }

    private static DomainPartitionIdentityV1 Create()
    {
        var current = StandardDomainPartitionRegistry.Get(PhysicalOccupancyRecordSchemaV2.PartitionId);
        return current with { RecordSchema = PhysicalOccupancyRecordSchemaV2.RecordSchema };
    }
}

public sealed class PhysicalOccupancyRecordSetV2
{
    private readonly SortedDictionary<OpaqueId128, PhysicalOccupancyRecordMaterialV2> _records = new();

    public PhysicalOccupancyRecordSetV2(IEnumerable<PhysicalOccupancyRecordMaterialV2> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        foreach (var record in records)
        {
            ArgumentNullException.ThrowIfNull(record);
            if (!_records.TryAdd(record.RecordId, record))
                throw new InvalidDataException("physical.occupancy-v2.record-id-duplicate");
        }
    }

    public IReadOnlyList<PhysicalOccupancyRecordMaterialV2> RecordsCanonical
        => Array.AsReadOnly(_records.Values.ToArray());

    public bool TryGet(OpaqueId128 recordId, out PhysicalOccupancyRecordMaterialV2? record)
        => _records.TryGetValue(recordId, out record);

    public void ValidateTerrainSchemaReferences(IDomainRecordSchemaResolverV1 references)
    {
        ArgumentNullException.ThrowIfNull(references);
        foreach (var record in _records.Values)
        {
            if (record.Payload is not PhysicalTerrainSdfRefShapePayloadV2 terrain) continue;
            if (!references.TryGetRecordSchema(terrain.TerrainRootRef, out var schema))
                throw new InvalidDataException("physical.occupancy-v2.terrain-root-missing");
            if (schema != new SchemaRefV1("domain.spatial.terrain_geometry.record", 2, 0))
                throw new InvalidDataException("physical.occupancy-v2.terrain-root-schema");
        }
    }
}

public sealed class PhysicalOccupancyPartitionStateV2
{
    public PhysicalOccupancyPartitionStateV2(IEnumerable<PhysicalOccupancyRecordMaterialV2> records)
    {
        PhysicalOccupancyPartitionIdentityV2.ValidateCanonicalContract();
        RecordSet = new PhysicalOccupancyRecordSetV2(records);
        var envelopes = RecordSet.RecordsCanonical.Select(static record =>
            new DomainRecordEnvelopeV1<PhysicalOccupancyRecordPayloadV2>(
                record.RecordId,
                PhysicalOccupancyRecordSchemaV2.RecordSchema,
                record.Revision,
                record.CreatedStep,
                record.RetiredStep,
                record.DetailLevel,
                record.LineageRef,
                record.Payload));
        State = new DomainPartitionStateV1<PhysicalOccupancyRecordPayloadV2>(
            PhysicalOccupancyPartitionIdentityV2.Identity,
            envelopes);
        if (State.ItemCount != checked((ulong)RecordSet.RecordsCanonical.Count))
            throw new InvalidDataException("physical.occupancy-v2.partition-item-count");
    }

    public PhysicalOccupancyRecordSetV2 RecordSet { get; }
    public DomainPartitionStateV1<PhysicalOccupancyRecordPayloadV2> State { get; }
}
