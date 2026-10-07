using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// QA-04 canonical binding from the 508,192-record on-demand Terrain source to the production
/// streaming Snapshot authority. The supplied frozen header is used only for revision/basis/detail
/// metadata; item count and canonical digest are recomputed from the actual canonical record stream.
/// </summary>
public static class Qa04TerrainCanonicalStreamingSnapshotAuthorityV1
{
    public static SpatialTerrainGeometryStreamingSnapshotAuthorityV2 Create(
        PartitionStateHeaderV1 frozenHeaderTemplate)
    {
        ArgumentNullException.ThrowIfNull(frozenHeaderTemplate);
        Qa04TerrainCanonicalRecordSourceV1.ValidateCanonicalContract();

        var standard = StandardDomainPartitionRegistry.Get(SpatialTerrainGeometryRecordSchemaV2.PartitionId);
        if (frozenHeaderTemplate.PartitionId != standard.PartitionId ||
            frozenHeaderTemplate.OwnerDomain != standard.OwnerDomain ||
            frozenHeaderTemplate.Schema != standard.PartitionSchema)
        {
            throw new InvalidDataException("qa04.terrain.streaming-authority-template-identity");
        }

        var source = Qa04TerrainCanonicalRecordSourceV1.CreateCanonical();
        var authority = SpatialTerrainGeometryStreamingSnapshotAuthorityV2.CreateCanonical(
            source.RecordIdsCanonical,
            source.EnumerateCanonicalRecords,
            frozenHeaderTemplate.Revision,
            frozenHeaderTemplate.BasisStep,
            frozenHeaderTemplate.DetailLevel);

        if (authority.ActualItemCount != Qa04TerrainCanonicalRecordSourceV1.CanonicalRecordCount)
            throw new InvalidDataException("qa04.terrain.streaming-authority-count");
        return authority;
    }
}
