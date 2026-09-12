using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains.Spatial;

/// <summary>
/// Versioned identity for the standalone terrain v2 state foundation.
/// Partition schema/container semantics remain v1; only the record schema advances to 2.0.
/// This identity is deliberately not registered in StandardDomainPartitionRegistry yet.
/// </summary>
public static class SpatialTerrainGeometryPartitionIdentityV2
{
    public static DomainPartitionIdentityV1 Identity { get; } = Create();

    public static void ValidateCanonicalContract()
    {
        var current = StandardDomainPartitionRegistry.Get(SpatialTerrainGeometryRecordSchemaV2.PartitionId);
        if (Identity.PartitionId != current.PartitionId ||
            Identity.OwnerDomain != current.OwnerDomain ||
            Identity.OwnerDomainRank != current.OwnerDomainRank ||
            Identity.PartitionSchema != current.PartitionSchema ||
            Identity.PrimaryKeyKind != current.PrimaryKeyKind ||
            Identity.PersistenceClass != current.PersistenceClass ||
            Identity.CanonicalOrder != current.CanonicalOrder)
            throw new InvalidDataException("spatial.terrain-v2.partition-identity-drift");
        if (Identity.RecordSchema != SpatialTerrainGeometryRecordSchemaV2.RecordSchema)
            throw new InvalidDataException("spatial.terrain-v2.partition-record-schema");
        if (current.RecordSchema.Version != new SchemaVersionV1(1, 0))
            throw new InvalidDataException("spatial.terrain-v2.production-registry-flipped");
    }

    private static DomainPartitionIdentityV1 Create()
    {
        var current = StandardDomainPartitionRegistry.Get(SpatialTerrainGeometryRecordSchemaV2.PartitionId);
        return current with { RecordSchema = SpatialTerrainGeometryRecordSchemaV2.RecordSchema };
    }
}

/// <summary>
/// Mixed-record state foundation for terrain v2. It reuses the standard ordered Domain partition
/// container with the v2 record identity, while keeping production registry/snapshot activation out.
/// </summary>
public sealed class SpatialTerrainGeometryPartitionStateV2
{
    public SpatialTerrainGeometryPartitionStateV2(IEnumerable<SpatialTerrainGeometryRecordMaterialV2> records)
    {
        SpatialTerrainGeometryRecordSchemaV2.ValidateCanonicalContract();
        SpatialTerrainGeometryPartitionIdentityV2.ValidateCanonicalContract();
        RecordSet = new SpatialTerrainGeometryRecordSetV2(records);

        var envelopes = RecordSet.RecordsCanonical.Select(static record =>
            new DomainRecordEnvelopeV1<SpatialTerrainGeometryPayloadV2>(
                record.RecordId,
                SpatialTerrainGeometryRecordSchemaV2.RecordSchema,
                record.Revision,
                record.CreatedStep,
                record.RetiredStep,
                record.DetailLevel,
                record.LineageRef,
                record.Payload));
        State = new DomainPartitionStateV1<SpatialTerrainGeometryPayloadV2>(
            SpatialTerrainGeometryPartitionIdentityV2.Identity,
            envelopes);
        if (State.ItemCount != checked((ulong)RecordSet.RecordsCanonical.Count))
            throw new InvalidDataException("spatial.terrain-v2.partition-item-count");
    }

    public SpatialTerrainGeometryRecordSetV2 RecordSet { get; }
    public DomainPartitionStateV1<SpatialTerrainGeometryPayloadV2> State { get; }
}
