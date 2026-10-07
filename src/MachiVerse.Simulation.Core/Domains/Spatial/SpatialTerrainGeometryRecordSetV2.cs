using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Domains.Spatial;

/// <summary>
/// Canonical in-memory semantic closure for standalone spatial.terrain_geometry v2 material.
/// It proves that every terrain_root.root_brick_ref resolves to an actual terrain_brick arm,
/// not merely to an arbitrary record in the same partition.
/// </summary>
public sealed class SpatialTerrainGeometryRecordSetV2
{
    private readonly SortedDictionary<OpaqueId128, SpatialTerrainGeometryRecordMaterialV2> _records;

    public SpatialTerrainGeometryRecordSetV2(IEnumerable<SpatialTerrainGeometryRecordMaterialV2> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        _records = new SortedDictionary<OpaqueId128, SpatialTerrainGeometryRecordMaterialV2>();
        foreach (var record in records)
        {
            ArgumentNullException.ThrowIfNull(record);
            if (!_records.TryAdd(record.RecordId, record))
                throw new InvalidDataException("spatial.terrain-v2.record-id-duplicate");
        }

        ValidateReferences();
    }

    public IReadOnlyList<SpatialTerrainGeometryRecordMaterialV2> RecordsCanonical
        => Array.AsReadOnly(_records.Values.ToArray());

    public bool TryGet(OpaqueId128 recordId, out SpatialTerrainGeometryRecordMaterialV2? record)
        => _records.TryGetValue(recordId, out record);

    public void ValidateReferences()
    {
        foreach (var record in _records.Values)
        {
            if (record.Payload is not SpatialTerrainRootPayloadV2 root) continue;
            if (root.RootBrickRef.PartitionId.Value != SpatialTerrainGeometryRecordSchemaV2.PartitionId)
                throw new InvalidDataException("spatial.terrain-v2.root-brick-owner");
            if (!_records.TryGetValue(root.RootBrickRef.RecordId, out var target))
                throw new InvalidDataException("spatial.terrain-v2.root-brick-missing");
            if (target.Payload is not SpatialTerrainBrickPayloadV2)
                throw new InvalidDataException("spatial.terrain-v2.root-brick-kind");
        }
    }
}
