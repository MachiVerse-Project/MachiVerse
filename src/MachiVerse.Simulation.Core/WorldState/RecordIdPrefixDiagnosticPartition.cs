using System.Globalization;
using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.WorldState;

/// <summary>
/// Stable logical diagnostic partitioning for RecordId-keyed authoritative partitions.
///
/// Version 1 assigns each record to a logical slice by the first byte of its canonical 128-bit
/// RecordId. Slice identity is independent from runtime threads, process shards, database layout,
/// chunk size, and insertion history.
///
/// The canonical authoritative value of a non-empty slice is an MV-DCBOR array containing the
/// already-canonical record values in RecordId bytewise ascending order.
/// </summary>
public static class RecordIdPrefixDiagnosticPartitionV1
{
    public const uint PartitionVersion = 1;
    private const string SliceSuffixPrefix = "/rid-";

    public static StableToken GetSliceKey(
        DomainPartitionIdentityV1 identity,
        OpaqueId128 recordId)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (recordId.IsZero)
            throw new ArgumentException("RecordId ZERO is invalid.", nameof(recordId));

        var prefix = recordId.ToBytes()[0];
        return new StableToken(
            identity.PartitionId.Value +
            SliceSuffixPrefix +
            prefix.ToString("x2", CultureInfo.InvariantCulture));
    }

    public static IReadOnlyList<StateDiagnosticSliceHashV1> CreateSliceHashes<TPayload>(
        OpaqueId128 worldId,
        ulong step,
        DomainPartitionStateV1<TPayload> state,
        Func<DomainRecordEnvelopeV1<TPayload>, byte[]> canonicalRecordEncoding)
    {
        if (worldId.IsZero)
            throw new ArgumentException("WorldId ZERO is invalid.", nameof(worldId));
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(canonicalRecordEncoding);

        var slices = new List<StateDiagnosticSliceHashV1>();
        var currentPrefix = -1;
        var currentRecords = new List<byte[]>();

        void Flush()
        {
            if (currentRecords.Count == 0)
                return;

            var writer = new MvDcborWriter();
            writer.WriteArrayStart(checked((ulong)currentRecords.Count));
            foreach (var encoded in currentRecords)
                writer.WriteCanonicalValue(encoded);

            var sliceKey = new StableToken(
                state.Identity.PartitionId.Value +
                SliceSuffixPrefix +
                currentPrefix.ToString("x2", CultureInfo.InvariantCulture));
            slices.Add(StateDiagnosticHierarchyV1.CreateSliceHash(
                worldId,
                step,
                state.Identity.OwnerDomain,
                PartitionVersion,
                sliceKey,
                writer.ToArray()));
            currentRecords.Clear();
        }

        OpaqueId128? previous = null;
        foreach (var record in state.RecordsCanonical)
        {
            ArgumentNullException.ThrowIfNull(record);
            if (record.RecordSchema != state.Identity.RecordSchema)
                throw new InvalidDataException("domain.record-schema-mismatch");
            if (record.CreatedStep > step)
                throw new InvalidDataException("domain.record-created-after-diagnostic-step");
            if (record.RetiredStep is { } retiredAfterStep && retiredAfterStep > step)
                throw new InvalidDataException("domain.record-retired-after-diagnostic-step");
            if (previous is { } prior && prior.CompareTo(record.RecordId) >= 0)
                throw new InvalidDataException("world-state.diagnostic-record-order-drift");
            previous = record.RecordId;

            var recordBytes = record.RecordId.ToBytes();
            var prefix = recordBytes[0];
            if (currentPrefix != prefix)
            {
                Flush();
                currentPrefix = prefix;
            }

            var encoded = canonicalRecordEncoding(record)
                ?? throw new InvalidDataException("world-state.diagnostic-record-encoding-null");
            if (encoded.Length == 0)
                throw new InvalidDataException("world-state.diagnostic-record-encoding-empty");
            currentRecords.Add(encoded);
        }

        Flush();
        return Array.AsReadOnly(slices.ToArray());
    }
}
