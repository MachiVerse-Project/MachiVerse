using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Run-scoped exact-v1 canonical record stream cache for the six QA-04 hot partitions.
///
/// The cache does not change the mv.state-diagnostic.v1 preimage. It keeps canonical record bytes in
/// bounded ordered chunks and updates only chunks touched by the authoritative mutation change set.
/// Partition hashing still streams every canonical byte, but it no longer performs millions of
/// payload/record cache lookups and record-level writer calls on every Step.
/// </summary>
internal sealed class Qa04CanonicalRecordChunkCacheV1<TPayload>
{
    private const int TargetChunkRecords = 512;
    private const int MaxChunkRecords = TargetChunkRecords * 2;

    private readonly Func<DomainRecordEnvelopeV1<TPayload>, byte[]> _encoder;
    private List<Chunk> _chunks = new();
    private DomainPartitionIdentityV1? _identity;
    private DomainPartitionStateV1<TPayload>? _lastState;
    private ulong _itemCount;
    private ulong _lastBasisStep;
    private bool _initialized;

    public Qa04CanonicalRecordChunkCacheV1(
        Func<DomainRecordEnvelopeV1<TPayload>, byte[]> encoder)
        => _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));

    public PartitionStateHeaderV1 CreateHeader(
        DomainPartitionStateV1<TPayload> state,
        ulong revision,
        ulong basisStep,
        DetailLevelV1 detailLevel,
        IReadOnlyList<Qa04CanonicalOperationMutationChangeV1> changes)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(changes);
        if (revision == 0) throw new ArgumentOutOfRangeException(nameof(revision));
        if (!Enum.IsDefined(detailLevel)) throw new ArgumentOutOfRangeException(nameof(detailLevel));

        if (!_initialized)
        {
            Rebuild(state, basisStep);
        }
        else
        {
            if (_identity != state.Identity)
                throw new InvalidDataException("qa04.canonical-chunk-cache.partition-identity-drift");

            if (ReferenceEquals(state, _lastState) && basisStep == _lastBasisStep)
            {
                // Repeated validation of the exact same material must be side-effect free.
            }
            else if (basisStep != checked(_lastBasisStep + 1UL))
            {
                // A cache may be reused by focused smoke/replay callers that jump Steps. Rebuild
                // from canonical state rather than applying a delta to an unrelated predecessor.
                Rebuild(state, basisStep);
            }
            else
            {
                ApplyChanges(state, basisStep, changes);
            }
        }

        if (_itemCount != state.ItemCount)
            throw new InvalidDataException("qa04.canonical-chunk-cache.item-count-drift");

        var canonicalChunks = new CanonicalRecordChunkV1[_chunks.Count];
        for (var index = 0; index < _chunks.Count; index++)
            canonicalChunks[index] = _chunks[index].Canonical;

        return PartitionStateHeaderV1.CreateCanonicalPrevalidatedChunks(
            state.Identity,
            revision,
            basisStep,
            detailLevel,
            state.ItemCount,
            canonicalChunks);
    }

    private void Rebuild(
        DomainPartitionStateV1<TPayload> state,
        ulong basisStep)
    {
        var chunks = new List<Chunk>();
        var entries = new List<Entry>(TargetChunkRecords);
        OpaqueId128? previous = null;
        ulong count = 0;

        foreach (var record in state.RecordsCanonical)
        {
            ValidateRecord(record, state.Identity, basisStep);
            if (previous is { } prior && prior.CompareTo(record.RecordId) >= 0)
                throw new InvalidDataException("qa04.canonical-chunk-cache.record-order-drift");
            previous = record.RecordId;

            entries.Add(new Entry(record.RecordId, Encode(record)));
            count = checked(count + 1UL);
            if (entries.Count == TargetChunkRecords)
            {
                chunks.Add(BuildChunk(entries, 0, entries.Count));
                entries.Clear();
            }
        }

        if (entries.Count != 0)
            chunks.Add(BuildChunk(entries, 0, entries.Count));
        if (count != state.ItemCount)
            throw new InvalidDataException("qa04.canonical-chunk-cache.rebuild-count-drift");

        _chunks = chunks;
        _identity = state.Identity;
        _lastState = state;
        _itemCount = count;
        _lastBasisStep = basisStep;
        _initialized = true;
        ValidateChunkDirectory();
    }

    private void ApplyChanges(
        DomainPartitionStateV1<TPayload> state,
        ulong basisStep,
        IReadOnlyList<Qa04CanonicalOperationMutationChangeV1> changes)
    {
        var normalized = NormalizeChanges(state, basisStep, changes, out var createdCount);
        var expectedCount = checked(_itemCount + createdCount);
        if (state.ItemCount != expectedCount)
            throw new InvalidDataException("qa04.canonical-chunk-cache.delta-count-drift");

        if (normalized.Count == 0)
        {
            if (state.ItemCount != _itemCount)
                throw new InvalidDataException("qa04.canonical-chunk-cache.empty-delta-count-drift");
            _lastState = state;
            _lastBasisStep = basisStep;
            return;
        }

        if (_chunks.Count == 0)
        {
            var entries = normalized.Values
                .Select(static update => new Entry(update.RecordId, update.Encoded))
                .ToArray();
            if (entries.Any((entry, index) => index != 0 && entries[index - 1].RecordId.CompareTo(entry.RecordId) >= 0))
                throw new InvalidDataException("qa04.canonical-chunk-cache.delta-order-drift");
            _chunks = SplitEntries(entries);
        }
        else
        {
            var byChunk = new Dictionary<int, List<Update>>();
            foreach (var update in normalized.Values)
            {
                var chunkIndex = FindChunkIndexForInsertion(update.RecordId);
                if (!byChunk.TryGetValue(chunkIndex, out var list))
                {
                    list = new List<Update>();
                    byChunk.Add(chunkIndex, list);
                }
                list.Add(update);
            }

            var rebuilt = new List<Chunk>(checked(_chunks.Count + normalized.Count / TargetChunkRecords + 2));
            for (var chunkIndex = 0; chunkIndex < _chunks.Count; chunkIndex++)
            {
                if (!byChunk.TryGetValue(chunkIndex, out var updates))
                {
                    rebuilt.Add(_chunks[chunkIndex]);
                    continue;
                }

                updates.Sort(static (left, right) => left.RecordId.CompareTo(right.RecordId));
                rebuilt.AddRange(MergeChunk(_chunks[chunkIndex], updates));
            }
            _chunks = rebuilt;
        }

        _itemCount = expectedCount;
        _lastState = state;
        _lastBasisStep = basisStep;
        ValidateChunkDirectory();
    }

    private SortedDictionary<OpaqueId128, Update> NormalizeChanges(
        DomainPartitionStateV1<TPayload> state,
        ulong basisStep,
        IReadOnlyList<Qa04CanonicalOperationMutationChangeV1> changes,
        out ulong createdCount)
    {
        var modes = new Dictionary<OpaqueId128, ChangeMode>();
        foreach (var change in changes)
        {
            ArgumentNullException.ThrowIfNull(change);
            if (change.PartitionId != state.Identity.PartitionId)
                throw new InvalidDataException("qa04.canonical-chunk-cache.change-partition-drift");

            ref var mode = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(
                modes,
                change.ChangedRecordId,
                out var exists);
            if (!exists)
                mode = new ChangeMode();

            switch (change.MutationMode.Value)
            {
                case "create":
                    if (mode.HasCreate)
                        throw new InvalidDataException("qa04.canonical-chunk-cache.duplicate-create");
                    mode.HasCreate = true;
                    break;
                case "revise":
                    mode.HasRevision = true;
                    break;
                default:
                    throw new InvalidDataException("qa04.canonical-chunk-cache.mutation-mode");
            }
        }

        var normalized = new SortedDictionary<OpaqueId128, Update>();
        createdCount = 0;
        foreach (var pair in modes)
        {
            var recordId = pair.Key;
            var mode = pair.Value;
            if (!state.TryGet(recordId, out var record) || record is null)
                throw new InvalidDataException("qa04.canonical-chunk-cache.changed-record-missing");
            ValidateRecord(record, state.Identity, basisStep);

            var existed = TryFind(recordId, out _);
            if (mode.HasCreate)
            {
                if (existed)
                    throw new InvalidDataException("qa04.canonical-chunk-cache.create-collision");
                createdCount = checked(createdCount + 1UL);
            }
            else if (!existed)
            {
                throw new InvalidDataException("qa04.canonical-chunk-cache.revision-target-missing");
            }

            normalized.Add(
                recordId,
                new Update(recordId, Encode(record), mode.HasCreate));
        }

        return normalized;
    }

    private IReadOnlyList<Chunk> MergeChunk(
        Chunk chunk,
        IReadOnlyList<Update> updates)
    {
        var entries = new List<Entry>(checked(chunk.Count + updates.Count));
        var existingIndex = 0;
        var updateIndex = 0;

        while (existingIndex < chunk.Count || updateIndex < updates.Count)
        {
            if (existingIndex == chunk.Count)
            {
                var update = updates[updateIndex++];
                if (!update.IsCreate)
                    throw new InvalidDataException("qa04.canonical-chunk-cache.revision-target-missing");
                entries.Add(new Entry(update.RecordId, update.Encoded));
                continue;
            }

            if (updateIndex == updates.Count)
            {
                entries.Add(chunk.GetEntry(existingIndex++));
                continue;
            }

            var existing = chunk.GetEntry(existingIndex);
            var updateCurrent = updates[updateIndex];
            var comparison = existing.RecordId.CompareTo(updateCurrent.RecordId);
            if (comparison < 0)
            {
                entries.Add(existing);
                existingIndex++;
                continue;
            }

            if (comparison > 0)
            {
                if (!updateCurrent.IsCreate)
                    throw new InvalidDataException("qa04.canonical-chunk-cache.revision-target-missing");
                entries.Add(new Entry(updateCurrent.RecordId, updateCurrent.Encoded));
                updateIndex++;
                continue;
            }

            if (updateCurrent.IsCreate)
                throw new InvalidDataException("qa04.canonical-chunk-cache.create-collision");
            entries.Add(new Entry(updateCurrent.RecordId, updateCurrent.Encoded));
            existingIndex++;
            updateIndex++;
        }

        return SplitEntries(entries);
    }

    private static List<Chunk> SplitEntries(IReadOnlyList<Entry> entries)
    {
        var chunks = new List<Chunk>();
        if (entries.Count == 0)
            return chunks;

        if (entries.Count <= MaxChunkRecords)
        {
            chunks.Add(BuildChunk(entries, 0, entries.Count));
            return chunks;
        }

        for (var offset = 0; offset < entries.Count; offset += TargetChunkRecords)
        {
            var count = Math.Min(TargetChunkRecords, entries.Count - offset);
            chunks.Add(BuildChunk(entries, offset, count));
        }
        return chunks;
    }

    private int FindChunkIndexForInsertion(OpaqueId128 recordId)
    {
        var low = 0;
        var high = _chunks.Count - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) >> 1);
            if (_chunks[middle].LastRecordId.CompareTo(recordId) >= 0)
                high = middle - 1;
            else
                low = middle + 1;
        }

        return low < _chunks.Count ? low : _chunks.Count - 1;
    }

    private bool TryFind(OpaqueId128 recordId, out Entry entry)
    {
        if (_chunks.Count != 0)
        {
            var index = FindChunkIndexForInsertion(recordId);
            if (_chunks[index].TryFind(recordId, out var recordIndex))
            {
                entry = _chunks[index].GetEntry(recordIndex);
                return true;
            }
        }

        entry = default;
        return false;
    }

    private byte[] Encode(DomainRecordEnvelopeV1<TPayload> record)
    {
        var encoded = _encoder(record)
            ?? throw new InvalidDataException("qa04.canonical-chunk-cache.record-encoding-null");
        if (encoded.Length == 0)
            throw new InvalidDataException("qa04.canonical-chunk-cache.record-encoding-empty");
        return encoded;
    }

    private static void ValidateRecord(
        DomainRecordEnvelopeV1<TPayload> record,
        DomainPartitionIdentityV1 identity,
        ulong basisStep)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.RecordSchema != identity.RecordSchema)
            throw new InvalidDataException("domain.record-schema-mismatch");
        if (record.CreatedStep > basisStep)
            throw new InvalidDataException("domain.record-created-after-partition-basis");
        if (record.RetiredStep is { } retiredAfterBasis && retiredAfterBasis > basisStep)
            throw new InvalidDataException("domain.record-retired-after-partition-basis");
    }

    private static Chunk BuildChunk(
        IReadOnlyList<Entry> entries,
        int offset,
        int count)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));

        var keys = new OpaqueId128[count];
        var offsets = new int[count + 1];
        var byteCount = 0;
        OpaqueId128? previous = null;
        for (var index = 0; index < count; index++)
        {
            var entry = entries[offset + index];
            if (previous is { } prior && prior.CompareTo(entry.RecordId) >= 0)
                throw new InvalidDataException("qa04.canonical-chunk-cache.chunk-order-drift");
            previous = entry.RecordId;
            keys[index] = entry.RecordId;
            offsets[index] = byteCount;
            byteCount = checked(byteCount + entry.Encoded.Length);
        }
        offsets[count] = byteCount;

        var encoded = new byte[byteCount];
        for (var index = 0; index < count; index++)
        {
            var entry = entries[offset + index];
            entry.Encoded.Span.CopyTo(encoded.AsSpan(offsets[index]));
        }

        return new Chunk(keys, offsets, encoded);
    }

    private void ValidateChunkDirectory()
    {
        ulong count = 0;
        OpaqueId128? previous = null;
        foreach (var chunk in _chunks)
        {
            if (chunk.Count == 0)
                throw new InvalidDataException("qa04.canonical-chunk-cache.empty-chunk");
            if (previous is { } prior && prior.CompareTo(chunk.FirstRecordId) >= 0)
                throw new InvalidDataException("qa04.canonical-chunk-cache.chunk-directory-order");
            previous = chunk.LastRecordId;
            count = checked(count + (ulong)chunk.Count);
        }

        if (count != _itemCount)
            throw new InvalidDataException("qa04.canonical-chunk-cache.chunk-directory-count");
    }

    private sealed class ChangeMode
    {
        public bool HasCreate;
        public bool HasRevision;
    }

    private readonly record struct Update(
        OpaqueId128 RecordId,
        ReadOnlyMemory<byte> Encoded,
        bool IsCreate);

    private readonly record struct Entry(
        OpaqueId128 RecordId,
        ReadOnlyMemory<byte> Encoded);

    private sealed class Chunk
    {
        private readonly OpaqueId128[] _keys;
        private readonly int[] _offsets;
        private readonly byte[] _encoded;

        public Chunk(
            OpaqueId128[] keys,
            int[] offsets,
            byte[] encoded)
        {
            _keys = keys ?? throw new ArgumentNullException(nameof(keys));
            _offsets = offsets ?? throw new ArgumentNullException(nameof(offsets));
            _encoded = encoded ?? throw new ArgumentNullException(nameof(encoded));
            if (_keys.Length == 0 || _offsets.Length != _keys.Length + 1 || _encoded.Length == 0)
                throw new InvalidDataException("qa04.canonical-chunk-cache.chunk-shape");
            if (_offsets[0] != 0 || _offsets[^1] != _encoded.Length)
                throw new InvalidDataException("qa04.canonical-chunk-cache.chunk-offset");

            Canonical = new CanonicalRecordChunkV1(checked((ulong)_keys.Length), _encoded);
        }

        public int Count => _keys.Length;
        public OpaqueId128 FirstRecordId => _keys[0];
        public OpaqueId128 LastRecordId => _keys[^1];
        public CanonicalRecordChunkV1 Canonical { get; }

        public Entry GetEntry(int index)
        {
            if ((uint)index >= (uint)_keys.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            return new Entry(
                _keys[index],
                _encoded.AsMemory(_offsets[index], _offsets[index + 1] - _offsets[index]));
        }

        public bool TryFind(OpaqueId128 recordId, out int index)
        {
            var low = 0;
            var high = _keys.Length - 1;
            while (low <= high)
            {
                var middle = low + ((high - low) >> 1);
                var comparison = _keys[middle].CompareTo(recordId);
                if (comparison == 0)
                {
                    index = middle;
                    return true;
                }

                if (comparison < 0) low = middle + 1;
                else high = middle - 1;
            }

            index = -1;
            return false;
        }
    }
}
