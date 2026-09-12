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
        : this(Build(fragments))
    {
    }

    private SpatialTerrainGeometryStreamingRecoveredReferenceSourceV2(BuildResult result)
    {
        Header = result.Header;
        _recordIds = result.RecordIds;
        _recordKinds = result.RecordKinds;
        _recordIdsReadOnly = Array.AsReadOnly(_recordIds);
        _rootClosures = Array.AsReadOnly(result.RootClosures);
        PartitionId = StandardDomainPartitionRegistry.Get(SpatialTerrainGeometryRecordSchemaV2.PartitionId).PartitionId;
        RecordSchema = SpatialTerrainGeometryRecordSchemaV2.RecordSchema;
        ActualItemCount = result.ActualItemCount;
        ValidateInternalTopology();
    }

    public StableToken PartitionId { get; }
    public SchemaRefV1 RecordSchema { get; }
    public ulong ActualItemCount { get; }
    public IReadOnlyList<OpaqueId128> RecordIdsCanonical => _recordIdsReadOnly;
    public PartitionStateHeaderV1 Header { get; }
    public IReadOnlyList<SpatialTerrainGeometryRecoveredRootClosureV2> RootClosures => _rootClosures;

    public static async Task<SpatialTerrainGeometryStreamingRecoveredReferenceSourceV2> CreateAsync(
        IAsyncEnumerable<SnapshotSectionFragmentMaterialV1> fragments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fragments);
        var builder = new Builder();
        await foreach (var fragment in fragments
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            builder.Add(fragment);
        }
        return builder.Complete();
    }

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

    private static BuildResult Build(IEnumerable<SnapshotSectionFragmentMaterialV1> fragments)
    {
        ArgumentNullException.ThrowIfNull(fragments);
        var builder = new Builder();
        foreach (var fragment in fragments) builder.Add(fragment);
        return builder.CompleteResult();
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

    /// <summary>
    /// Incremental phase-1 builder used by staged recovery so Terrain and its external reference
    /// authorities can be scanned together in one physical Snapshot pass.
    /// </summary>
    public sealed class Builder
    {
        private readonly List<OpaqueId128> _ids = [];
        private readonly List<SpatialTerrainGeometryRecoveredRecordKindV2> _kinds = [];
        private readonly List<SpatialTerrainGeometryRecoveredRootClosureV2> _roots = [];
        private PartitionStateHeaderV1? _repeatedHeader;
        private OpaqueId128? _previous;
        private uint _expectedFragmentIndex;
        private uint? _declaredFragmentCount;
        private ulong _total;
        private bool _completed;

        public void Add(SnapshotSectionFragmentMaterialV1 fragment)
        {
            if (_completed) throw new InvalidOperationException("Terrain recovered-reference builder is complete.");
            ArgumentNullException.ThrowIfNull(fragment);
            if (!string.Equals(fragment.SectionId, SpatialTerrainGeometryRecordSchemaV2.PartitionId, StringComparison.Ordinal) ||
                fragment.FragmentCount == 0 ||
                fragment.FragmentIndex != _expectedFragmentIndex)
                throw new InvalidDataException("persistence.snapshot.recovered-reference-fragment-shape:spatial.terrain_geometry");

            _declaredFragmentCount ??= fragment.FragmentCount;
            if (fragment.FragmentCount != _declaredFragmentCount.Value ||
                fragment.FragmentIndex >= fragment.FragmentCount)
                throw new InvalidDataException("persistence.snapshot.recovered-reference-fragment-shape:spatial.terrain_geometry");

            var decoded = SpatialTerrainGeometrySnapshotFragmentWireV2.Decode(fragment.FragmentPayload);
            if (_repeatedHeader is null)
                _repeatedHeader = decoded.Header;
            else
                RequireSameHeader(_repeatedHeader, decoded.Header);

            if (decoded.Records.Count != checked((int)fragment.ItemCount))
                throw new InvalidDataException("persistence.snapshot.recovered-reference-item-count:spatial.terrain_geometry");
            RequireFragmentRange(fragment, decoded.Records);

            foreach (var record in decoded.Records)
            {
                if (record.RecordSchema != SpatialTerrainGeometryRecordSchemaV2.RecordSchema)
                    throw new InvalidDataException("persistence.snapshot.recovered-reference-schema:spatial.terrain_geometry");
                if (_previous is { } prior && prior.CompareTo(record.RecordId) >= 0)
                    throw new InvalidDataException("persistence.snapshot.recovered-reference-order:spatial.terrain_geometry");
                _previous = record.RecordId;

                _ids.Add(record.RecordId);
                switch (record.Payload)
                {
                    case SpatialTerrainBrickPayloadV2:
                        _kinds.Add(SpatialTerrainGeometryRecoveredRecordKindV2.Brick);
                        break;
                    case SpatialTerrainRootPayloadV2 root:
                        _kinds.Add(SpatialTerrainGeometryRecoveredRecordKindV2.Root);
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
                        _roots.Add(new SpatialTerrainGeometryRecoveredRootClosureV2(
                            record.RecordId,
                            root.RootBrickRef.RecordId,
                            Array.AsReadOnly(connectivity)));
                        break;
                    default:
                        throw new InvalidDataException("persistence.snapshot.terrain-v2-record-kind");
                }
            }

            _total = checked(_total + fragment.ItemCount);
            _expectedFragmentIndex = checked(_expectedFragmentIndex + 1);
        }

        public SpatialTerrainGeometryStreamingRecoveredReferenceSourceV2 Complete()
            => new(CompleteResult());

        internal BuildResult CompleteResult()
        {
            if (_completed) throw new InvalidOperationException("Terrain recovered-reference builder is already complete.");
            _completed = true;
            if (_declaredFragmentCount is null || _expectedFragmentIndex != _declaredFragmentCount.Value)
                throw new InvalidDataException("persistence.snapshot.recovered-reference-fragment-missing:spatial.terrain_geometry");

            var header = _repeatedHeader
                ?? throw new InvalidDataException("persistence.snapshot.recovered-reference-header-missing:spatial.terrain_geometry");
            var standard = StandardDomainPartitionRegistry.Get(SpatialTerrainGeometryRecordSchemaV2.PartitionId);
            if (header.PartitionId != standard.PartitionId ||
                header.OwnerDomain != standard.OwnerDomain ||
                header.Schema != standard.PartitionSchema)
                throw new InvalidDataException("persistence.snapshot.recovered-reference-header-identity:spatial.terrain_geometry");
            if (_total != header.ItemCount || _total != checked((ulong)_ids.Count) || _ids.Count != _kinds.Count)
                throw new InvalidDataException("persistence.snapshot.recovered-reference-total-count:spatial.terrain_geometry");
            if (header.ItemCount == 0 && _declaredFragmentCount.Value != 1)
                throw new InvalidDataException("persistence.snapshot.recovered-reference-empty-fragment-count:spatial.terrain_geometry");

            return new BuildResult(
                header,
                _ids.ToArray(),
                _kinds.ToArray(),
                _roots.ToArray(),
                _total);
        }
    }

    internal sealed record BuildResult(
        PartitionStateHeaderV1 Header,
        OpaqueId128[] RecordIds,
        SpatialTerrainGeometryRecoveredRecordKindV2[] RecordKinds,
        SpatialTerrainGeometryRecoveredRootClosureV2[] RootClosures,
        ulong ActualItemCount);

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
