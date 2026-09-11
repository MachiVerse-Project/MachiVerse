using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

public enum SpatialTerrainGeometryRecoveredRecordKindV2 : byte
{
    Brick = 1,
    Root = 2,
}

public readonly record struct SpatialTerrainGeometryRecoveredRootClosureV2(
    OpaqueId128 RootId,
    OpaqueId128 RootBrickId,
    IReadOnlyList<OpaqueId128> ConnectivityRootIds);

/// <summary>
/// Bounded-memory Terrain v2 recovery phase-1 source.
///
/// Fragment payloads are decoded one at a time and discarded. The retained recovery index consists
/// of the canonical RecordId array, one kind byte per record, and root-only topology closure data.
/// This is sufficient to prove terrain_root -> terrain_brick and connectivity -> terrain_root target
/// kinds without retaining any 729-SDF / 512-material brick payload after its fragment is processed.
/// Cross-partition reference existence remains a phase-2 responsibility.
/// </summary>
public sealed class SpatialTerrainGeometryStreamingRecoveredReferenceSourceV2 : IDomainPartitionSnapshotReferenceSourceV1
{
    private const string ScopeRegistryPartitionId = "spatial.scope_registry";

    private readonly OpaqueId128[] _recordIds;
    private readonly SpatialTerrainGeometryRecoveredRecordKindV2[] _recordKinds;
    private readonly IReadOnlyList<OpaqueId128> _recordIdsReadOnly;
    private readonly IReadOnlyList<SpatialTerrainGeometryRecoveredRootClosureV2> _rootClosures;

    public SpatialTerrainGeometryStreamingRecoveredReferenceSourceV2(
        IEnumerable<SnapshotSectionFragmentMaterialV1> fragments)
    {
        ArgumentNullException.ThrowIfNull(fragments);
        var standard = StandardDomainPartitionRegistry.Get(SpatialTerrainGeometryRecordSchemaV2.PartitionId);
        var ids = new List<OpaqueId128>();
        var kinds = new List<SpatialTerrainGeometryRecoveredRecordKindV2>();
        var roots = new List<SpatialTerrainGeometryRecoveredRootClosureV2>();
        PartitionStateHeaderV1? repeatedHeader = null;
        OpaqueId128? previous = null;
        uint expectedFragmentIndex = 0;
        uint? declaredFragmentCount = null;
        ulong total = 0;

        foreach (var fragment in fragments)
        {
            ArgumentNullException.ThrowIfNull(fragment);
            if (!string.Equals(fragment.SectionId, SpatialTerrainGeometryRecordSchemaV2.PartitionId, StringComparison.Ordinal) ||
                fragment.FragmentCount == 0 ||
                fragment.FragmentIndex != expectedFragmentIndex)
                throw new InvalidDataException("persistence.snapshot.recovered-reference-fragment-shape:spatial.terrain_geometry");

            declaredFragmentCount ??= fragment.FragmentCount;
            if (fragment.FragmentCount != declaredFragmentCount.Value ||
                fragment.FragmentIndex >= fragment.FragmentCount)
                throw new InvalidDataException("persistence.snapshot.recovered-reference-fragment-shape:spatial.terrain_geometry");

            var decoded = SpatialTerrainGeometrySnapshotFragmentWireV2.Decode(fragment.FragmentPayload);
            if (repeatedHeader is null)
                repeatedHeader = decoded.Header;
            else
                RequireSameHeader(repeatedHeader, decoded.Header);

            if (decoded.Records.Count != checked((int)fragment.ItemCount))
                throw new InvalidDataException("persistence.snapshot.recovered-reference-item-count:spatial.terrain_geometry");
            RequireFragmentRange(fragment, decoded.Records);

            foreach (var record in decoded.Records)
            {
                if (record.RecordSchema != SpatialTerrainGeometryRecordSchemaV2.RecordSchema)
                    throw new InvalidDataException("persistence.snapshot.recovered-reference-schema:spatial.terrain_geometry");
                if (previous is { } prior && prior.CompareTo(record.RecordId) >= 0)
                    throw new InvalidDataException("persistence.snapshot.recovered-reference-order:spatial.terrain_geometry");
                previous = record.RecordId;

                ids.Add(record.RecordId);
                switch (record.Payload)
                {
                    case SpatialTerrainBrickPayloadV2:
                        kinds.Add(SpatialTerrainGeometryRecoveredRecordKindV2.Brick);
                        break;
                    case SpatialTerrainRootPayloadV2 root:
                        kinds.Add(SpatialTerrainGeometryRecoveredRecordKindV2.Root);
                        if (!string.Equals(root.ScopeRef.PartitionId.Value, ScopeRegistryPartitionId, StringComparison.Ordinal))
                            throw new InvalidDataException("persistence.snapshot.terrain-v2-root-scope-owner");
                        if (!string.Equals(root.RootBrickRef.PartitionId.Value, SpatialTerrainGeometryRecordSchemaV2.PartitionId, StringComparison.Ordinal))
                            throw new InvalidDataException("persistence.snapshot.terrain-v2-root-brick-owner");
                        var connectivity = new OpaqueId128[root.ConnectivityRefs.Count];
                        for (var i = 0; i < root.ConnectivityRefs.Count; i++)
                        {
                            var reference = root.ConnectivityRefs[i];
                            if (!string.Equals(reference.PartitionId.Value, SpatialTerrainGeometryRecordSchemaV2.PartitionId, StringComparison.Ordinal))
                                throw new InvalidDataException("persistence.snapshot.terrain-v2-connectivity-owner");
                            connectivity[i] = reference.RecordId;
                        }
                        roots.Add(new SpatialTerrainGeometryRecoveredRootClosureV2(
                            record.RecordId,
                            root.RootBrickRef.RecordId,
                            Array.AsReadOnly(connectivity)));
                        break;
                    default:
                        throw new InvalidDataException("persistence.snapshot.terrain-v2-record-kind");
                }
            }

            total = checked(total + fragment.ItemCount);
            expectedFragmentIndex = checked(expectedFragmentIndex + 1);
        }

        if (declaredFragmentCount is null || expectedFragmentIndex != declaredFragmentCount.Value)
            throw new InvalidDataException("persistence.snapshot.recovered-reference-fragment-missing:spatial.terrain_geometry");

        Header = repeatedHeader
            ?? throw new InvalidDataException("persistence.snapshot.recovered-reference-header-missing:spatial.terrain_geometry");
        if (Header.PartitionId != standard.PartitionId ||
            Header.OwnerDomain != standard.OwnerDomain ||
            Header.Schema != standard.PartitionSchema)
            throw new InvalidDataException("persistence.snapshot.recovered-reference-header-identity:spatial.terrain_geometry");
        if (total != Header.ItemCount || total != checked((ulong)ids.Count) || ids.Count != kinds.Count)
            throw new InvalidDataException("persistence.snapshot.recovered-reference-total-count:spatial.terrain_geometry");
        if (Header.ItemCount == 0 && declaredFragmentCount.Value != 1)
            throw new InvalidDataException("persistence.snapshot.recovered-reference-empty-fragment-count:spatial.terrain_geometry");

        _recordIds = ids.ToArray();
        _recordKinds = kinds.ToArray();
        _recordIdsReadOnly = Array.AsReadOnly(_recordIds);
        _rootClosures = Array.AsReadOnly(roots.ToArray());
        ValidateInternalTopology();

        PartitionId = standard.PartitionId;
        RecordSchema = SpatialTerrainGeometryRecordSchemaV2.RecordSchema;
        ActualItemCount = total;
    }

    public StableToken PartitionId { get; }
    public SchemaRefV1 RecordSchema { get; }
    public ulong ActualItemCount { get; }
    public IReadOnlyList<OpaqueId128> RecordIdsCanonical => _recordIdsReadOnly;
    public PartitionStateHeaderV1 Header { get; }
    public IReadOnlyList<SpatialTerrainGeometryRecoveredRootClosureV2> RootClosures => _rootClosures;

    public bool TryGetKind(OpaqueId128 recordId, out SpatialTerrainGeometryRecoveredRecordKindV2 kind)
    {
        var index = FindRecordIndex(recordId);
        if (index < 0)
        {
            kind = default;
            return false;
        }
        kind = _recordKinds[index];
        return true;
    }

    private void ValidateInternalTopology()
    {
        foreach (var root in _rootClosures)
        {
            if (!TryGetKind(root.RootBrickId, out var brickKind) ||
                brickKind != SpatialTerrainGeometryRecoveredRecordKindV2.Brick)
                throw new InvalidDataException("persistence.snapshot.terrain-v2-root-brick-kind");

            foreach (var targetId in root.ConnectivityRootIds)
            {
                if (!TryGetKind(targetId, out var targetKind) ||
                    targetKind != SpatialTerrainGeometryRecoveredRecordKindV2.Root)
                    throw new InvalidDataException("persistence.snapshot.terrain-v2-connectivity-kind");
            }
        }
    }

    private int FindRecordIndex(OpaqueId128 target)
    {
        var low = 0;
        var high = _recordIds.Length - 1;
        while (low <= high)
        {
            var mid = low + ((high - low) >> 1);
            var comparison = _recordIds[mid].CompareTo(target);
            if (comparison == 0) return mid;
            if (comparison < 0) low = mid + 1;
            else high = mid - 1;
        }
        return -1;
    }

    private static void RequireFragmentRange(
        SnapshotSectionFragmentMaterialV1 fragment,
        IReadOnlyList<SpatialTerrainGeometryRecordMaterialV2> records)
    {
        if (records.Count == 0)
        {
            if (fragment.FirstRecordId is not null || fragment.LastRecordId is not null)
                throw new InvalidDataException("persistence.snapshot.recovered-reference-range-empty:spatial.terrain_geometry");
            return;
        }

        var first = records[0].RecordId.ToBytes();
        var last = records[^1].RecordId.ToBytes();
        if (fragment.FirstRecordId is null || fragment.LastRecordId is null ||
            !first.AsSpan().SequenceEqual(fragment.FirstRecordId) ||
            !last.AsSpan().SequenceEqual(fragment.LastRecordId))
            throw new InvalidDataException("persistence.snapshot.recovered-reference-range:spatial.terrain_geometry");
    }

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
            throw new InvalidDataException("persistence.snapshot.recovered-reference-header-mismatch:spatial.terrain_geometry");
    }
}
