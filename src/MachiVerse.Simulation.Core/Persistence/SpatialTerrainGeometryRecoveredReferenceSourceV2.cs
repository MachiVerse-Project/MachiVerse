using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

/// <summary>
/// Phase-1 recovered reference source for the registered spatial.terrain_geometry record schema 2.0.
/// It proves fragment/header/range/order/schema structure and exposes actual record ids without
/// performing cross-partition semantic reference validation, which remains a phase-2 responsibility.
/// </summary>
public sealed class SpatialTerrainGeometryRecoveredReferenceSourceV2 : IDomainPartitionSnapshotReferenceSourceV1
{
    public SpatialTerrainGeometryRecoveredReferenceSourceV2(
        IReadOnlyList<SnapshotSectionFragmentMaterialV1> fragments)
    {
        ArgumentNullException.ThrowIfNull(fragments);
        if (fragments.Count == 0)
            throw new InvalidDataException("persistence.snapshot.recovered-reference-fragment-missing:spatial.terrain_geometry");

        var standard = StandardDomainPartitionRegistry.Get(SpatialTerrainGeometryRecordSchemaV2.PartitionId);
        var ids = new List<OpaqueId128>();
        PartitionStateHeaderV1? repeatedHeader = null;
        OpaqueId128? previous = null;
        ulong total = 0;

        for (var index = 0; index < fragments.Count; index++)
        {
            var fragment = fragments[index]
                ?? throw new InvalidDataException("persistence.snapshot.recovered-reference-fragment-null:spatial.terrain_geometry");
            if (!string.Equals(fragment.SectionId, SpatialTerrainGeometryRecordSchemaV2.PartitionId, StringComparison.Ordinal) ||
                fragment.FragmentIndex != checked((uint)index) ||
                fragment.FragmentCount != checked((uint)fragments.Count))
            {
                throw new InvalidDataException("persistence.snapshot.recovered-reference-fragment-shape:spatial.terrain_geometry");
            }

            var decoded = SpatialTerrainGeometrySnapshotFragmentWireV2.Decode(fragment.FragmentPayload);
            if (repeatedHeader is null)
                repeatedHeader = decoded.Header;
            else
                RequireSameHeader(repeatedHeader, decoded.Header);

            if (decoded.Records.Count != checked((int)fragment.ItemCount))
                throw new InvalidDataException("persistence.snapshot.recovered-reference-item-count:spatial.terrain_geometry");

            if (decoded.Records.Count == 0)
            {
                if (fragment.FirstRecordId is not null || fragment.LastRecordId is not null)
                    throw new InvalidDataException("persistence.snapshot.recovered-reference-range-empty:spatial.terrain_geometry");
            }
            else
            {
                var first = decoded.Records[0].RecordId.ToBytes();
                var last = decoded.Records[^1].RecordId.ToBytes();
                if (fragment.FirstRecordId is null || fragment.LastRecordId is null ||
                    !first.AsSpan().SequenceEqual(fragment.FirstRecordId) ||
                    !last.AsSpan().SequenceEqual(fragment.LastRecordId))
                {
                    throw new InvalidDataException("persistence.snapshot.recovered-reference-range:spatial.terrain_geometry");
                }
            }

            foreach (var record in decoded.Records)
            {
                if (record.RecordSchema != SpatialTerrainGeometryRecordSchemaV2.RecordSchema)
                    throw new InvalidDataException("persistence.snapshot.recovered-reference-schema:spatial.terrain_geometry");
                if (previous is { } prior && prior.CompareTo(record.RecordId) >= 0)
                    throw new InvalidDataException("persistence.snapshot.recovered-reference-order:spatial.terrain_geometry");
                previous = record.RecordId;
                ids.Add(record.RecordId);
            }
            total = checked(total + fragment.ItemCount);
        }

        Header = repeatedHeader
            ?? throw new InvalidDataException("persistence.snapshot.recovered-reference-header-missing:spatial.terrain_geometry");
        if (Header.PartitionId != standard.PartitionId ||
            Header.OwnerDomain != standard.OwnerDomain ||
            Header.Schema != standard.PartitionSchema)
        {
            throw new InvalidDataException("persistence.snapshot.recovered-reference-header-identity:spatial.terrain_geometry");
        }
        if (total != Header.ItemCount || total != checked((ulong)ids.Count))
            throw new InvalidDataException("persistence.snapshot.recovered-reference-total-count:spatial.terrain_geometry");
        if (Header.ItemCount == 0 && fragments.Count != 1)
            throw new InvalidDataException("persistence.snapshot.recovered-reference-empty-fragment-count:spatial.terrain_geometry");

        PartitionId = standard.PartitionId;
        RecordSchema = SpatialTerrainGeometryRecordSchemaV2.RecordSchema;
        ActualItemCount = total;
        RecordIdsCanonical = Array.AsReadOnly(ids.ToArray());
    }

    public StableToken PartitionId { get; }
    public SchemaRefV1 RecordSchema { get; }
    public ulong ActualItemCount { get; }
    public IReadOnlyList<OpaqueId128> RecordIdsCanonical { get; }
    public PartitionStateHeaderV1 Header { get; }

    private static void RequireSameHeader(
        PartitionStateHeaderV1 expected,
        PartitionStateHeaderV1 actual)
    {
        if (expected.PartitionId != actual.PartitionId ||
            expected.OwnerDomain != actual.OwnerDomain ||
            expected.Schema != actual.Schema ||
            expected.Revision != actual.Revision ||
            expected.BasisStep != actual.BasisStep ||
            expected.DetailLevel != actual.DetailLevel ||
            expected.ItemCount != actual.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(expected.CanonicalDigest, actual.CanonicalDigest))
        {
            throw new InvalidDataException("persistence.snapshot.recovered-reference-header-mismatch:spatial.terrain_geometry");
        }
    }
}
