using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Run-scoped incremental RecordIdPrefixV2 partition digest cache for QA-04 hot partitions.
///
/// Canonical record bytes are retained in stable 14-bit RecordId-prefix slices. A Step rebuilds only
/// slices touched by the authoritative mutation change set; the partition root then hashes the
/// compact ordered slice commitments rather than streaming every record byte.
/// </summary>
internal sealed class Qa04RecordIdPrefixPartitionDigestCacheV2<TPayload>
{
    private readonly Func<DomainRecordEnvelopeV1<TPayload>, byte[]> _encoder;
    private readonly SortedDictionary<ushort, Slice> _slices = new();
    private DomainPartitionIdentityV1? _identity;
    private DomainPartitionStateV1<TPayload>? _lastState;
    private ulong _itemCount;
    private ulong _lastBasisStep;
    private bool _initialized;

    public Qa04RecordIdPrefixPartitionDigestCacheV2(
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
                throw new InvalidDataException("qa04.prefix-digest-cache.partition-identity-drift");

            if (ReferenceEquals(state, _lastState) && basisStep == _lastBasisStep)
            {
                // Exact repeated validation is side-effect free.
            }
            else if (basisStep != checked(_lastBasisStep + 1UL))
            {
                // Focused smoke/replay callers may jump Steps. Rebuild from canonical state rather
                // than applying a delta against an unrelated predecessor.
                Rebuild(state, basisStep);
            }
            else
            {
                ApplyChanges(state, basisStep, changes);
            }
        }

        if (_itemCount != state.ItemCount)
            throw new InvalidDataException("qa04.prefix-digest-cache.item-count-drift");

        return RecordIdPrefixPartitionDigestV2.CreateHeaderFromPrevalidatedSlices(
            state.Identity,
            revision,
            basisStep,
            detailLevel,
            state.ItemCount,
            _slices.Values.Select(static slice => slice.Commitment),
            _slices.Count);
    }

    private void Rebuild(
        DomainPartitionStateV1<TPayload> state,
        ulong basisStep)
    {
        _slices.Clear();

        var entries = new List<Entry>();
        ushort? currentPrefix = null;
        OpaqueId128? previous = null;
        ulong count = 0;

        void Flush()
        {
            if (entries.Count == 0 || currentPrefix is null)
                return;
            var slice = BuildSlice(state.Identity, currentPrefix.Value, entries);
            _slices.Add(currentPrefix.Value, slice);
            entries.Clear();
        }

        foreach (var record in state.RecordsCanonical)
        {
            ValidateRecord(record, state.Identity, basisStep);
            if (previous is { } prior && prior.CompareTo(record.RecordId) >= 0)
                throw new InvalidDataException("qa04.prefix-digest-cache.record-order-drift");
            previous = record.RecordId;

            var prefix = RecordIdPrefixPartitionDigestV2.PrefixOf(record.RecordId);
            if (currentPrefix != prefix)
            {
                Flush();
                currentPrefix = prefix;
            }

            entries.Add(new Entry(record.RecordId, Encode(record)));
            count = checked(count + 1UL);
        }
        Flush();

        if (count != state.ItemCount)
            throw new InvalidDataException("qa04.prefix-digest-cache.rebuild-count-drift");

        _identity = state.Identity;
        _lastState = state;
        _itemCount = count;
        _lastBasisStep = basisStep;
        _initialized = true;
        ValidateSliceDirectory();
    }

    private void ApplyChanges(
        DomainPartitionStateV1<TPayload> state,
        ulong basisStep,
        IReadOnlyList<Qa04CanonicalOperationMutationChangeV1> changes)
    {
        var normalized = NormalizeChanges(state, basisStep, changes, out var createdCount);
        var expectedCount = checked(_itemCount + createdCount);
        if (state.ItemCount != expectedCount)
            throw new InvalidDataException("qa04.prefix-digest-cache.delta-count-drift");

        if (normalized.Count == 0)
        {
            if (state.ItemCount != _itemCount)
                throw new InvalidDataException("qa04.prefix-digest-cache.empty-delta-count-drift");
            _lastState = state;
            _lastBasisStep = basisStep;
            return;
        }

        var byPrefix = normalized.Values
            .GroupBy(static update => RecordIdPrefixPartitionDigestV2.PrefixOf(update.RecordId))
            .OrderBy(static group => group.Key)
            .ToArray();

        foreach (var group in byPrefix)
        {
            var prefix = group.Key;
            var updates = group
                .OrderBy(static update => update.RecordId)
                .ToArray();

            _slices.TryGetValue(prefix, out var existing);
            var replacement = MergeSlice(state.Identity, prefix, existing, updates);
            _slices[prefix] = replacement;
        }

        _itemCount = expectedCount;
        _lastState = state;
        _lastBasisStep = basisStep;
        ValidateSliceDirectory();
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
                throw new InvalidDataException("qa04.prefix-digest-cache.change-partition-drift");

            if (!modes.TryGetValue(change.ChangedRecordId, out var mode))
            {
                mode = new ChangeMode();
                modes.Add(change.ChangedRecordId, mode);
            }

            switch (change.MutationMode.Value)
            {
                case "create":
                    if (mode.HasCreate)
                        throw new InvalidDataException("qa04.prefix-digest-cache.duplicate-create");
                    mode.HasCreate = true;
                    break;
                case "revise":
                    mode.HasRevision = true;
                    break;
                default:
                    throw new InvalidDataException("qa04.prefix-digest-cache.mutation-mode");
            }
        }

        var normalized = new SortedDictionary<OpaqueId128, Update>();
        createdCount = 0;
        foreach (var pair in modes)
        {
            var recordId = pair.Key;
            var mode = pair.Value;
            if (!state.TryGet(recordId, out var record) || record is null)
                throw new InvalidDataException("qa04.prefix-digest-cache.changed-record-missing");
            ValidateRecord(record, state.Identity, basisStep);

            var existed = TryFind(recordId, out _);
            if (mode.HasCreate)
            {
                if (existed)
                    throw new InvalidDataException("qa04.prefix-digest-cache.create-collision");
                createdCount = checked(createdCount + 1UL);
            }
            else if (!existed)
            {
                throw new InvalidDataException("qa04.prefix-digest-cache.revision-target-missing");
            }

            normalized.Add(
                recordId,
                new Update(recordId, Encode(record), mode.HasCreate));
        }

        return normalized;
    }

    private static Slice MergeSlice(
        DomainPartitionIdentityV1 identity,
        ushort prefix,
        Slice? existing,
        IReadOnlyList<Update> updates)
    {
        var entries = new List<Entry>((existing?.Count ?? 0) + updates.Count);
        var existingIndex = 0;
        var updateIndex = 0;

        while ((existing is not null && existingIndex < existing.Count) || updateIndex < updates.Count)
        {
            if (existing is null || existingIndex == existing.Count)
            {
                var update = updates[updateIndex++];
                if (!update.IsCreate)
                    throw new InvalidDataException("qa04.prefix-digest-cache.revision-target-missing");
                entries.Add(new Entry(update.RecordId, update.Encoded));
                continue;
            }

            if (updateIndex == updates.Count)
            {
                entries.Add(existing.GetEntry(existingIndex++));
                continue;
            }

            var current = existing.GetEntry(existingIndex);
            var updateCurrent = updates[updateIndex];
            var comparison = current.RecordId.CompareTo(updateCurrent.RecordId);
            if (comparison < 0)
            {
                entries.Add(current);
                existingIndex++;
                continue;
            }

            if (comparison > 0)
            {
                if (!updateCurrent.IsCreate)
                    throw new InvalidDataException("qa04.prefix-digest-cache.revision-target-missing");
                entries.Add(new Entry(updateCurrent.RecordId, updateCurrent.Encoded));
                updateIndex++;
                continue;
            }

            if (updateCurrent.IsCreate)
                throw new InvalidDataException("qa04.prefix-digest-cache.create-collision");
            entries.Add(new Entry(updateCurrent.RecordId, updateCurrent.Encoded));
            existingIndex++;
            updateIndex++;
        }

        if (entries.Count == 0)
            throw new InvalidDataException("qa04.prefix-digest-cache.empty-slice");
        return BuildSlice(identity, prefix, entries);
    }

    private bool TryFind(OpaqueId128 recordId, out Entry entry)
    {
        var prefix = RecordIdPrefixPartitionDigestV2.PrefixOf(recordId);
        if (_slices.TryGetValue(prefix, out var slice) &&
            slice.TryFind(recordId, out var index))
        {
            entry = slice.GetEntry(index);
            return true;
        }

        entry = default;
        return false;
    }

    private byte[] Encode(DomainRecordEnvelopeV1<TPayload> record)
    {
        var encoded = _encoder(record)
            ?? throw new InvalidDataException("qa04.prefix-digest-cache.record-encoding-null");
        if (encoded.Length == 0)
            throw new InvalidDataException("qa04.prefix-digest-cache.record-encoding-empty");
        return encoded;
    }

    private static Slice BuildSlice(
        DomainPartitionIdentityV1 identity,
        ushort prefix,
        IReadOnlyList<Entry> entries)
    {
        if (entries.Count == 0)
            throw new ArgumentException("Slice entries cannot be empty.", nameof(entries));

        var keys = new OpaqueId128[entries.Count];
        var offsets = new int[entries.Count + 1];
        var byteCount = 0;
        OpaqueId128? previous = null;

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (RecordIdPrefixPartitionDigestV2.PrefixOf(entry.RecordId) != prefix)
                throw new InvalidDataException("qa04.prefix-digest-cache.slice-prefix-drift");
            if (previous is { } prior && prior.CompareTo(entry.RecordId) >= 0)
                throw new InvalidDataException("qa04.prefix-digest-cache.slice-order-drift");
            previous = entry.RecordId;

            keys[index] = entry.RecordId;
            offsets[index] = byteCount;
            byteCount = checked(byteCount + entry.Encoded.Length);
        }
        offsets[entries.Count] = byteCount;

        var encoded = new byte[byteCount];
        var canonical = new ReadOnlyMemory<byte>[entries.Count];
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            entry.Encoded.Span.CopyTo(encoded.AsSpan(offsets[index]));
            canonical[index] = encoded.AsMemory(
                offsets[index],
                offsets[index + 1] - offsets[index]);
        }

        var diagnostic = RecordIdPrefixPartitionDigestV2.CreateSlice(
            identity,
            prefix,
            canonical);
        return new Slice(keys, offsets, encoded, diagnostic.ContentDigest);
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

    private void ValidateSliceDirectory()
    {
        ulong count = 0;
        ushort? previousPrefix = null;
        OpaqueId128? previousRecord = null;

        foreach (var pair in _slices)
        {
            var slice = pair.Value;
            if (slice.Prefix != pair.Key || slice.Count == 0)
                throw new InvalidDataException("qa04.prefix-digest-cache.slice-directory-shape");
            if (previousPrefix is { } priorPrefix && priorPrefix >= pair.Key)
                throw new InvalidDataException("qa04.prefix-digest-cache.slice-directory-order");
            previousPrefix = pair.Key;

            if (previousRecord is { } priorRecord &&
                priorRecord.CompareTo(slice.FirstRecordId) >= 0)
                throw new InvalidDataException("qa04.prefix-digest-cache.record-directory-order");
            previousRecord = slice.LastRecordId;
            count = checked(count + (ulong)slice.Count);
        }

        if (count != _itemCount)
            throw new InvalidDataException("qa04.prefix-digest-cache.slice-directory-count");
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

    private sealed class Slice
    {
        private readonly OpaqueId128[] _keys;
        private readonly int[] _offsets;
        private readonly byte[] _encoded;

        public Slice(
            OpaqueId128[] keys,
            int[] offsets,
            byte[] encoded,
            byte[] contentDigest)
        {
            _keys = keys ?? throw new ArgumentNullException(nameof(keys));
            _offsets = offsets ?? throw new ArgumentNullException(nameof(offsets));
            _encoded = encoded ?? throw new ArgumentNullException(nameof(encoded));
            ArgumentNullException.ThrowIfNull(contentDigest);
            if (_keys.Length == 0 || _offsets.Length != _keys.Length + 1 || _encoded.Length == 0)
                throw new InvalidDataException("qa04.prefix-digest-cache.slice-shape");
            if (_offsets[0] != 0 || _offsets[^1] != _encoded.Length)
                throw new InvalidDataException("qa04.prefix-digest-cache.slice-offset");
            if (contentDigest.Length != 32)
                throw new InvalidDataException("qa04.prefix-digest-cache.slice-digest-length");

            Prefix = RecordIdPrefixPartitionDigestV2.PrefixOf(_keys[0]);
            if (_keys.Any(key => RecordIdPrefixPartitionDigestV2.PrefixOf(key) != Prefix))
                throw new InvalidDataException("qa04.prefix-digest-cache.slice-prefix-drift");
            Commitment = new PartitionDigestSliceV2(
                Prefix,
                checked((ulong)_keys.Length),
                contentDigest);
        }

        public ushort Prefix { get; }
        public int Count => _keys.Length;
        public OpaqueId128 FirstRecordId => _keys[0];
        public OpaqueId128 LastRecordId => _keys[^1];
        public PartitionDigestSliceV2 Commitment { get; }

        public Entry GetEntry(int index)
        {
            if ((uint)index >= (uint)_keys.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            return new Entry(
                _keys[index],
                _encoded.AsMemory(
                    _offsets[index],
                    _offsets[index + 1] - _offsets[index]));
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
