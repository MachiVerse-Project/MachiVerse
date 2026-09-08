using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed class HistoryRecordMaterial
{
    private HistoryRecordMaterial(
        OpaqueId128 worldId,
        ulong sequence,
        byte[] previousRecordDigest,
        StableToken recordType,
        StableToken payloadSchemaId,
        ushort payloadSchemaMajor,
        ushort payloadSchemaMinor,
        byte[] payloadBytes,
        byte[] normalizedPayloadBytes,
        byte[] normalizedPayloadDigest,
        byte[] recordDigest)
    {
        WorldId = worldId;
        Sequence = sequence;
        PreviousRecordDigest = previousRecordDigest;
        RecordType = recordType.Value;
        PayloadSchemaId = payloadSchemaId.Value;
        PayloadSchemaMajor = payloadSchemaMajor;
        PayloadSchemaMinor = payloadSchemaMinor;
        PayloadBytes = payloadBytes;
        NormalizedPayloadBytes = normalizedPayloadBytes;
        NormalizedPayloadDigest = normalizedPayloadDigest;
        RecordDigest = recordDigest;
    }

    public OpaqueId128 WorldId { get; }
    public ulong Sequence { get; }
    public byte[] PreviousRecordDigest { get; }
    public string RecordType { get; }
    public string PayloadSchemaId { get; }
    public ushort PayloadSchemaMajor { get; }
    public ushort PayloadSchemaMinor { get; }
    public byte[] PayloadBytes { get; }
    public byte[] NormalizedPayloadBytes { get; }
    public byte[] NormalizedPayloadDigest { get; }
    public byte[] RecordDigest { get; }

    public static HistoryRecordMaterial Create(
        OpaqueId128 worldId,
        ulong sequence,
        ReadOnlySpan<byte> previousRecordDigest,
        string recordType,
        string payloadSchemaId,
        ushort payloadSchemaMajor,
        ushort payloadSchemaMinor,
        ReadOnlySpan<byte> payloadBytes,
        Action<MvDcborWriter> writeNormalizedPayload)
    {
        if (worldId.IsZero) throw new ArgumentException("WorldId ZERO is invalid for history.", nameof(worldId));
        if (sequence == 0) throw new ArgumentOutOfRangeException(nameof(sequence), "HistorySequence starts at 1.");
        if (previousRecordDigest.Length != 32)
            throw new ArgumentException("Previous history digest must be exactly 32 bytes.", nameof(previousRecordDigest));
        ArgumentNullException.ThrowIfNull(writeNormalizedPayload);

        var recordToken = new StableToken(recordType);
        var schemaToken = new StableToken(payloadSchemaId);
        var normalizedWriter = new MvDcborWriter();
        writeNormalizedPayload(normalizedWriter);
        var normalizedPayload = normalizedWriter.ToArray();
        if (normalizedPayload.Length == 0)
            throw new InvalidDataException("persistence.normalized-history-payload-empty");

        var previous = previousRecordDigest.ToArray();
        var normalizedDigest = HashSuite.Hash256(normalizedPayload);
        var recordDigest = HistoryIntegrity.ComputeHistoryRecordDigest(
            worldId,
            sequence,
            previous,
            recordToken,
            normalizedPayload);

        return new HistoryRecordMaterial(
            worldId,
            sequence,
            previous,
            recordToken,
            schemaToken,
            payloadSchemaMajor,
            payloadSchemaMinor,
            payloadBytes.ToArray(),
            normalizedPayload,
            normalizedDigest,
            recordDigest);
    }
}

public static class HistoryIntegrity
{
    public static byte[] ComputeHistoryRecordDigest(
        OpaqueId128 worldId,
        ulong sequence,
        ReadOnlySpan<byte> previousRecordDigest,
        StableToken recordType,
        ReadOnlySpan<byte> normalizedRecordPayload)
    {
        if (worldId.IsZero) throw new ArgumentException("WorldId ZERO is invalid for history digest.", nameof(worldId));
        if (sequence == 0) throw new ArgumentOutOfRangeException(nameof(sequence), "HistorySequence starts at 1.");
        if (previousRecordDigest.Length != 32)
            throw new ArgumentException("Previous history digest must be exactly 32 bytes.", nameof(previousRecordDigest));
        if (normalizedRecordPayload.IsEmpty)
            throw new ArgumentException("Normalized record payload cannot be empty.", nameof(normalizedRecordPayload));

        var previous = previousRecordDigest.ToArray();
        var normalized = normalizedRecordPayload.ToArray();
        return HashSuite.DomainHash("mv.history-record.v1", writer =>
        {
            writer.WriteMapStart(5);
            writer.WriteUnsigned(0); writer.WriteBytes(worldId.ToBytes());
            writer.WriteUnsigned(1); writer.WriteUnsigned(sequence);
            writer.WriteUnsigned(2); writer.WriteBytes(previous);
            writer.WriteUnsigned(3); writer.WriteAsciiText(recordType.Value);
            writer.WriteUnsigned(4); writer.WriteCanonicalValue(normalized);
        });
    }

    public static byte[] ComputeGenesisContinuityToken(OpaqueId128 worldId, ReadOnlySpan<byte> genesisRecordDigest)
    {
        if (worldId.IsZero) throw new ArgumentException("WorldId ZERO is invalid for continuity.", nameof(worldId));
        if (genesisRecordDigest.Length != 32)
            throw new ArgumentException("Genesis record digest must be exactly 32 bytes.", nameof(genesisRecordDigest));

        var genesisDigest = genesisRecordDigest.ToArray();
        return HashSuite.DomainHash("mv.state-continuity.v1", writer =>
        {
            writer.WriteMapStart(3);
            writer.WriteUnsigned(0); writer.WriteBytes(worldId.ToBytes());
            writer.WriteUnsigned(1); writer.WriteUnsigned(0);
            writer.WriteUnsigned(2); writer.WriteBytes(genesisDigest);
        });
    }

    public static byte[] ComputeTransitionContinuityToken(
        OpaqueId128 worldId,
        ulong resultingStep,
        ReadOnlySpan<byte> previousToken,
        ReadOnlySpan<byte> transitionCommitRecordDigest)
    {
        if (worldId.IsZero) throw new ArgumentException("WorldId ZERO is invalid for continuity.", nameof(worldId));
        if (resultingStep == 0) throw new ArgumentOutOfRangeException(nameof(resultingStep), "Transition continuity begins at State(1).");
        if (previousToken.Length != 32)
            throw new ArgumentException("Previous continuity token must be exactly 32 bytes.", nameof(previousToken));
        if (transitionCommitRecordDigest.Length != 32)
            throw new ArgumentException("Transition commit digest must be exactly 32 bytes.", nameof(transitionCommitRecordDigest));

        var previous = previousToken.ToArray();
        var transitionDigest = transitionCommitRecordDigest.ToArray();
        return HashSuite.DomainHash("mv.state-continuity.v1", writer =>
        {
            writer.WriteMapStart(4);
            writer.WriteUnsigned(0); writer.WriteBytes(worldId.ToBytes());
            writer.WriteUnsigned(1); writer.WriteUnsigned(resultingStep);
            writer.WriteUnsigned(2); writer.WriteBytes(previous);
            writer.WriteUnsigned(3); writer.WriteBytes(transitionDigest);
        });
    }
}
