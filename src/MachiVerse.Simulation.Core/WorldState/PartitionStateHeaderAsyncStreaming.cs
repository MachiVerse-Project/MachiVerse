using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.WorldState;

/// <summary>
/// Async-enumerable equivalent of PartitionStateHeaderStreamingV1. Canonical record bytes are fed
/// directly into the same mv.state-diagnostic.v1 domain hash across async suspension points.
/// </summary>
public static class PartitionStateHeaderAsyncStreamingV1
{
    public static async Task<PartitionStateHeaderV1> CreateCanonicalAsync<TPayload>(
        DomainPartitionIdentityV1 identity,
        ulong revision,
        ulong basisStep,
        DetailLevelV1 detailLevel,
        ulong itemCount,
        IAsyncEnumerable<DomainRecordEnvelopeV1<TPayload>> recordsCanonical,
        Func<TPayload, byte[]> canonicalPayloadDigest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(recordsCanonical);
        ArgumentNullException.ThrowIfNull(canonicalPayloadDigest);
        if (revision == 0) throw new ArgumentOutOfRangeException(nameof(revision));
        if (!Enum.IsDefined(detailLevel)) throw new ArgumentOutOfRangeException(nameof(detailLevel));

        using var session = HashSuite.BeginDomainHashStreaming("mv.state-diagnostic.v1");
        var writer = session.Writer;
        writer.WriteMapStart(8);
        writer.WriteUnsigned(0); writer.WriteAsciiText(identity.PartitionId.Value);
        writer.WriteUnsigned(1); writer.WriteAsciiText(identity.OwnerDomain.Value);
        writer.WriteUnsigned(2); writer.WriteAsciiText(identity.PartitionSchema.SchemaId.Value);
        writer.WriteUnsigned(3); writer.WriteUnsigned(revision);
        writer.WriteUnsigned(4); writer.WriteUnsigned(basisStep);
        writer.WriteUnsigned(5); writer.WriteUnsigned((byte)detailLevel);
        writer.WriteUnsigned(6); writer.WriteUnsigned(itemCount);
        writer.WriteUnsigned(7);
        writer.WriteArrayStart(itemCount);

        ulong actualCount = 0;
        OpaqueId128? previousRecordId = null;
        await foreach (var record in recordsCanonical
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            ArgumentNullException.ThrowIfNull(record);
            if (record.RecordSchema != identity.RecordSchema)
                throw new InvalidDataException("domain.record-schema-mismatch");
            if (record.CreatedStep > basisStep)
                throw new InvalidDataException("domain.record-created-after-partition-basis");
            if (record.RetiredStep is { } retiredAfterBasis && retiredAfterBasis > basisStep)
                throw new InvalidDataException("domain.record-retired-after-partition-basis");
            if (previousRecordId is { } previous && previous.CompareTo(record.RecordId) >= 0)
                throw new InvalidDataException("domain.streaming-record-order");
            previousRecordId = record.RecordId;
            actualCount = checked(actualCount + 1);
            if (actualCount > itemCount)
                throw new InvalidDataException("domain.streaming-record-count-mismatch");

            var payloadDigest = canonicalPayloadDigest(record.Payload)
                ?? throw new InvalidDataException("domain.payload-digest-null");
            if (payloadDigest.Length != 32)
                throw new InvalidDataException("domain.payload-digest-invalid-length");

            writer.WriteMapStart(10);
            writer.WriteUnsigned(0); writer.WriteBytes(record.RecordId.ToBytes());
            writer.WriteUnsigned(1); writer.WriteAsciiText(record.RecordSchema.SchemaId.Value);
            writer.WriteUnsigned(2); writer.WriteUnsigned(record.RecordSchema.Version.Major);
            writer.WriteUnsigned(3); writer.WriteUnsigned(record.RecordSchema.Version.Minor);
            writer.WriteUnsigned(4); writer.WriteUnsigned(record.Revision);
            writer.WriteUnsigned(5); writer.WriteUnsigned(record.CreatedStep);
            writer.WriteUnsigned(6);
            if (record.RetiredStep is { } retiredValue)
            {
                writer.WriteArrayStart(1);
                writer.WriteUnsigned(retiredValue);
            }
            else writer.WriteArrayStart(0);
            writer.WriteUnsigned(7); writer.WriteUnsigned((byte)record.DetailLevel);
            writer.WriteUnsigned(8);
            if (record.LineageRef is { } lineage)
            {
                writer.WriteArrayStart(1);
                writer.WriteBytes(lineage.ToBytes());
            }
            else writer.WriteArrayStart(0);
            writer.WriteUnsigned(9); writer.WriteBytes(payloadDigest);
        }

        if (actualCount != itemCount)
            throw new InvalidDataException("domain.streaming-record-count-mismatch");

        return new PartitionStateHeaderV1(
            identity,
            revision,
            basisStep,
            detailLevel,
            itemCount,
            session.Complete());
    }
}
