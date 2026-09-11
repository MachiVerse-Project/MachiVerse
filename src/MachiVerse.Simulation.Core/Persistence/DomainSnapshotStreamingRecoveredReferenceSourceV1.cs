using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

/// <summary>
/// Incremental recovered-reference source for one standard-v1 Domain partition.
/// Fragment payloads are decoded one at a time and discarded; only canonical RecordIds are retained.
/// This is the bounded-memory counterpart of <see cref="DomainSnapshotRecoveredReferenceSourceV1"/>.
/// </summary>
public sealed class DomainSnapshotStreamingRecoveredReferenceSourceV1 : IDomainPartitionSnapshotReferenceSourceV1
{
    private readonly OpaqueId128[] _recordIds;
    private readonly IReadOnlyList<OpaqueId128> _recordIdsReadOnly;

    private DomainSnapshotStreamingRecoveredReferenceSourceV1(BuildResult result)
    {
        Header = result.Header;
        PartitionId = result.Identity.PartitionId;
        RecordSchema = result.Identity.RecordSchema;
        ActualItemCount = result.ActualItemCount;
        _recordIds = result.RecordIds;
        _recordIdsReadOnly = Array.AsReadOnly(_recordIds);
    }

    public StableToken PartitionId { get; }
    public SchemaRefV1 RecordSchema { get; }
    public ulong ActualItemCount { get; }
    public IReadOnlyList<OpaqueId128> RecordIdsCanonical => _recordIdsReadOnly;
    public PartitionStateHeaderV1 Header { get; }

    public static async Task<DomainSnapshotStreamingRecoveredReferenceSourceV1> CreateAsync(
        string partitionId,
        IAsyncEnumerable<SnapshotSectionFragmentMaterialV1> fragments,
        DomainNestedSnapshotCodecRegistryV1? nestedCodecs = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fragments);
        var builder = new Builder(partitionId, nestedCodecs);
        await foreach (var fragment in fragments
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            builder.Add(fragment);
        }
        return builder.Complete();
    }

    public sealed class Builder
    {
        private readonly DomainPartitionIdentityV1 _identity;
        private readonly DomainNestedSnapshotCodecRegistryV1? _nestedCodecs;
        private readonly List<OpaqueId128> _recordIds = [];
        private PartitionStateHeaderV1? _repeatedHeader;
        private OpaqueId128? _previous;
        private uint _expectedFragmentIndex;
        private uint? _declaredFragmentCount;
        private ulong _total;
        private bool _completed;

        public Builder(
            string partitionId,
            DomainNestedSnapshotCodecRegistryV1? nestedCodecs = null)
        {
            _identity = StandardDomainPartitionRegistry.Get(partitionId);
            _nestedCodecs = nestedCodecs;
        }

        public void Add(SnapshotSectionFragmentMaterialV1 fragment)
        {
            if (_completed)
                throw new InvalidOperationException("Recovered-reference builder is complete.");
            ArgumentNullException.ThrowIfNull(fragment);

            var partitionId = _identity.PartitionId.Value;
            if (!string.Equals(fragment.SectionId, partitionId, StringComparison.Ordinal) ||
                fragment.FragmentCount == 0 ||
                fragment.FragmentIndex != _expectedFragmentIndex)
            {
                throw new InvalidDataException($"persistence.snapshot.recovered-reference-fragment-shape:{partitionId}");
            }

            _declaredFragmentCount ??= fragment.FragmentCount;
            if (fragment.FragmentCount != _declaredFragmentCount.Value ||
                fragment.FragmentIndex >= fragment.FragmentCount)
            {
                throw new InvalidDataException($"persistence.snapshot.recovered-reference-fragment-shape:{partitionId}");
            }

            var decoded = DomainPartitionSnapshotWireCodecV1.DecodeFragment(
                partitionId,
                fragment.FragmentPayload,
                _nestedCodecs);
            if (_repeatedHeader is null)
                _repeatedHeader = decoded.Header;
            else
                RequireSameHeader(_repeatedHeader, decoded.Header, partitionId);

            if (decoded.Records.Count != checked((int)fragment.ItemCount))
                throw new InvalidDataException($"persistence.snapshot.recovered-reference-item-count:{partitionId}");
            RequireFragmentRange(fragment, decoded.Records, partitionId);

            foreach (var record in decoded.Records)
            {
                if (record.RecordSchema != _identity.RecordSchema)
                    throw new InvalidDataException($"persistence.snapshot.recovered-reference-schema:{partitionId}");
                if (_previous is { } prior && prior.CompareTo(record.RecordId) >= 0)
                    throw new InvalidDataException($"persistence.snapshot.recovered-reference-order:{partitionId}");
                _previous = record.RecordId;
                _recordIds.Add(record.RecordId);
            }

            _total = checked(_total + fragment.ItemCount);
            _expectedFragmentIndex = checked(_expectedFragmentIndex + 1);
        }

        public DomainSnapshotStreamingRecoveredReferenceSourceV1 Complete()
        {
            if (_completed)
                throw new InvalidOperationException("Recovered-reference builder is already complete.");
            _completed = true;

            var partitionId = _identity.PartitionId.Value;
            if (_declaredFragmentCount is null || _expectedFragmentIndex != _declaredFragmentCount.Value)
                throw new InvalidDataException($"persistence.snapshot.recovered-reference-fragment-missing:{partitionId}");

            var header = _repeatedHeader
                ?? throw new InvalidDataException($"persistence.snapshot.recovered-reference-header-missing:{partitionId}");
            if (header.PartitionId != _identity.PartitionId ||
                header.OwnerDomain != _identity.OwnerDomain ||
                header.Schema != _identity.PartitionSchema)
            {
                throw new InvalidDataException($"persistence.snapshot.recovered-reference-header-identity:{partitionId}");
            }
            if (_total != header.ItemCount || _total != checked((ulong)_recordIds.Count))
                throw new InvalidDataException($"persistence.snapshot.recovered-reference-total-count:{partitionId}");
            if (header.ItemCount == 0 && _declaredFragmentCount.Value != 1)
                throw new InvalidDataException($"persistence.snapshot.recovered-reference-empty-fragment-count:{partitionId}");

            return new DomainSnapshotStreamingRecoveredReferenceSourceV1(
                new BuildResult(
                    _identity,
                    header,
                    _recordIds.ToArray(),
                    _total));
        }
    }

    private sealed record BuildResult(
        DomainPartitionIdentityV1 Identity,
        PartitionStateHeaderV1 Header,
        OpaqueId128[] RecordIds,
        ulong ActualItemCount);

    private static void RequireFragmentRange(
        SnapshotSectionFragmentMaterialV1 fragment,
        IReadOnlyList<DomainRecordSnapshotMaterialV1> records,
        string partitionId)
    {
        if (records.Count == 0)
        {
            if (fragment.FirstRecordId is not null || fragment.LastRecordId is not null)
                throw new InvalidDataException($"persistence.snapshot.recovered-reference-range-empty:{partitionId}");
            return;
        }

        var first = records[0].RecordId.ToBytes();
        var last = records[^1].RecordId.ToBytes();
        if (fragment.FirstRecordId is null || fragment.LastRecordId is null ||
            !first.AsSpan().SequenceEqual(fragment.FirstRecordId) ||
            !last.AsSpan().SequenceEqual(fragment.LastRecordId))
        {
            throw new InvalidDataException($"persistence.snapshot.recovered-reference-range:{partitionId}");
        }
    }

    private static void RequireSameHeader(
        PartitionStateHeaderV1 expected,
        PartitionStateHeaderV1 actual,
        string partitionId)
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
            throw new InvalidDataException($"persistence.snapshot.recovered-reference-header-mismatch:{partitionId}");
        }
    }
}
