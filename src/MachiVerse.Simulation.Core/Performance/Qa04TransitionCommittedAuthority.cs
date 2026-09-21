using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04TransitionPartitionDigestV1(string PartitionId, byte[] ResultingDigest);

/// <summary>
/// Canonical Gate4 Step2 transition.committed.v1 authority. The 9-field semantic body is the only
/// history-hashed value. The 10-field physical wrapper additionally carries resulting continuity,
/// which is derived from that history RecordDigest and therefore cannot participate in the semantic
/// body without creating a hash cycle.
/// </summary>
public sealed class Qa04TransitionCommittedAuthorityV1
{
    private const string RecordType = "transition.committed.v1";
    private const string PayloadSchemaId = "persistence.transition-committed";
    private static readonly StableToken RecordTypeToken = new(RecordType);

    private Qa04TransitionCommittedAuthorityV1(
        ulong effectiveStep,
        ulong resultingStep,
        ulong activeConfigGeneration,
        byte[] activeConfigDigest,
        IReadOnlyList<OpaqueId128> appliedOperationIds,
        IReadOnlyList<TerminalOperationCommit> operationOutcomes,
        byte[] previousStateContinuityToken,
        byte[] resultingStateContinuityToken,
        byte[] stateDiagnosticHash,
        IReadOnlyList<Qa04TransitionPartitionDigestV1> partitionDigests,
        HistoryRecordMaterial history)
    {
        EffectiveStep = effectiveStep;
        ResultingStep = resultingStep;
        ActiveConfigGeneration = activeConfigGeneration;
        ActiveConfigDigest = activeConfigDigest;
        AppliedOperationIds = appliedOperationIds;
        OperationOutcomes = operationOutcomes;
        PreviousStateContinuityToken = previousStateContinuityToken;
        ResultingStateContinuityToken = resultingStateContinuityToken;
        StateDiagnosticHash = stateDiagnosticHash;
        PartitionDigests = partitionDigests;
        History = history;
    }

    public ulong EffectiveStep { get; }
    public ulong ResultingStep { get; }
    public ulong ActiveConfigGeneration { get; }
    public byte[] ActiveConfigDigest { get; }
    public IReadOnlyList<OpaqueId128> AppliedOperationIds { get; }
    public IReadOnlyList<TerminalOperationCommit> OperationOutcomes { get; }
    public byte[] PreviousStateContinuityToken { get; }
    public byte[] ResultingStateContinuityToken { get; }
    public byte[] StateDiagnosticHash { get; }
    public IReadOnlyList<Qa04TransitionPartitionDigestV1> PartitionDigests { get; }
    public HistoryRecordMaterial History { get; }
    public byte[] TransitionCommittedItemDigest => History.NormalizedPayloadDigest.ToArray();

    public static Qa04TransitionCommittedAuthorityV1 Create(
        OpaqueId128 worldId,
        ulong historySequence,
        ReadOnlySpan<byte> previousHistoryRecordDigest,
        ulong effectiveStep,
        ulong resultingStep,
        ulong activeConfigGeneration,
        ReadOnlySpan<byte> activeConfigDigest,
        IReadOnlyList<OpaqueId128> appliedOperationIds,
        IReadOnlyList<TerminalOperationCommit> operationOutcomes,
        ReadOnlySpan<byte> previousStateContinuityToken,
        ReadOnlySpan<byte> stateDiagnosticHash,
        IReadOnlyCollection<Qa04TransitionPartitionDigestV1> partitionDigests)
    {
        ArgumentNullException.ThrowIfNull(appliedOperationIds);
        ArgumentNullException.ThrowIfNull(operationOutcomes);
        var operations = ValidateAndCopyOperations(appliedOperationIds, operationOutcomes);
        return CreateCore(
            worldId,
            historySequence,
            previousHistoryRecordDigest,
            effectiveStep,
            resultingStep,
            activeConfigGeneration,
            activeConfigDigest,
            operations,
            previousStateContinuityToken,
            stateDiagnosticHash,
            partitionDigests);
    }

    internal static Qa04TransitionCommittedAuthorityV1 CreateFromValidatedCanonicalOutcomes(
        OpaqueId128 worldId,
        ulong historySequence,
        ReadOnlySpan<byte> previousHistoryRecordDigest,
        ulong effectiveStep,
        ulong resultingStep,
        ulong activeConfigGeneration,
        ReadOnlySpan<byte> activeConfigDigest,
        IReadOnlyList<TerminalOperationCommit> operationOutcomes,
        ReadOnlySpan<byte> previousStateContinuityToken,
        ReadOnlySpan<byte> stateDiagnosticHash,
        IReadOnlyCollection<Qa04TransitionPartitionDigestV1> partitionDigests)
    {
        ArgumentNullException.ThrowIfNull(operationOutcomes);
        var operations = CopyValidatedCanonicalOperations(operationOutcomes);
        return CreateCore(
            worldId,
            historySequence,
            previousHistoryRecordDigest,
            effectiveStep,
            resultingStep,
            activeConfigGeneration,
            activeConfigDigest,
            operations,
            previousStateContinuityToken,
            stateDiagnosticHash,
            partitionDigests);
    }

    private static Qa04TransitionCommittedAuthorityV1 CreateCore(
        OpaqueId128 worldId,
        ulong historySequence,
        ReadOnlySpan<byte> previousHistoryRecordDigest,
        ulong effectiveStep,
        ulong resultingStep,
        ulong activeConfigGeneration,
        ReadOnlySpan<byte> activeConfigDigest,
        (IReadOnlyList<OpaqueId128> Ids, IReadOnlyList<TerminalOperationCommit> Outcomes) operations,
        ReadOnlySpan<byte> previousStateContinuityToken,
        ReadOnlySpan<byte> stateDiagnosticHash,
        IReadOnlyCollection<Qa04TransitionPartitionDigestV1> partitionDigests)
    {
        if (worldId.IsZero) throw new ArgumentException("WorldId ZERO is invalid.", nameof(worldId));
        if (historySequence == 0) throw new ArgumentOutOfRangeException(nameof(historySequence));
        if (previousHistoryRecordDigest.Length != 32)
            throw new ArgumentException("Previous history digest must be 32 bytes.", nameof(previousHistoryRecordDigest));
        if (effectiveStep == ulong.MaxValue || resultingStep != effectiveStep + 1UL)
            throw new InvalidDataException("qa04.transition-authority.step-drift");
        if (activeConfigGeneration == 0)
            throw new InvalidDataException("qa04.transition-authority.config-generation-invalid");
        RequireHash256(activeConfigDigest, "qa04.transition-authority.config-digest-invalid");
        RequireHash256(previousStateContinuityToken, "qa04.transition-authority.previous-continuity-invalid");
        RequireHash256(stateDiagnosticHash, "qa04.transition-authority.state-diagnostic-invalid");
        ArgumentNullException.ThrowIfNull(partitionDigests);

        var partitions = ValidateAndCopyPartitions(partitionDigests);
        var configDigest = activeConfigDigest.ToArray();
        var previousContinuity = previousStateContinuityToken.ToArray();
        var stateDigest = stateDiagnosticHash.ToArray();

        var semanticBytes = EncodeSemanticBody(
            effectiveStep,
            resultingStep,
            activeConfigGeneration,
            configDigest,
            operations.Ids,
            operations.Outcomes,
            previousContinuity,
            stateDigest,
            partitions);
        var expectedRecordDigest = HistoryIntegrity.ComputeHistoryRecordDigest(
            worldId,
            historySequence,
            previousHistoryRecordDigest,
            RecordTypeToken,
            semanticBytes);
        var resultingContinuity = HistoryIntegrity.ComputeTransitionContinuityToken(
            worldId,
            resultingStep,
            previousContinuity,
            expectedRecordDigest);
        var physicalBytes = EncodePhysicalWrapper(
            effectiveStep,
            resultingStep,
            activeConfigGeneration,
            configDigest,
            operations.Ids,
            operations.Outcomes,
            previousContinuity,
            resultingContinuity,
            stateDigest,
            partitions);

        var history = HistoryRecordMaterial.Create(
            worldId,
            historySequence,
            previousHistoryRecordDigest,
            RecordType,
            PayloadSchemaId,
            1,
            0,
            physicalBytes,
            writer => writer.WriteCanonicalValue(semanticBytes));
        if (!CryptographicOperations.FixedTimeEquals(history.RecordDigest, expectedRecordDigest) ||
            !history.NormalizedPayloadBytes.AsSpan().SequenceEqual(semanticBytes) ||
            !history.PayloadBytes.AsSpan().SequenceEqual(physicalBytes))
            throw new InvalidDataException("qa04.transition-authority.history-material-drift");

        return new Qa04TransitionCommittedAuthorityV1(
            effectiveStep,
            resultingStep,
            activeConfigGeneration,
            configDigest,
            operations.Ids,
            operations.Outcomes,
            previousContinuity,
            resultingContinuity,
            stateDigest,
            partitions,
            history);
    }

    /// <summary>
    /// Strictly decodes the physical wrapper, reconstructs the 9-field semantic body and validates
    /// normalized payload digest, history RecordDigest, and resulting continuity before returning.
    /// </summary>
    public static Qa04TransitionCommittedAuthorityV1 DecodeAndValidate(HistoryRecordMaterial history)
    {
        ArgumentNullException.ThrowIfNull(history);
        RequireTransitionHistorySchema(
            history.RecordType,
            history.PayloadSchemaId,
            history.PayloadSchemaMajor,
            history.PayloadSchemaMinor);

        var decoded = DecodePhysicalWrapper(history.PayloadBytes);
        var semantic = EncodeSemanticBody(
            decoded.EffectiveStep,
            decoded.ResultingStep,
            decoded.ConfigGeneration,
            decoded.ConfigDigest,
            decoded.OperationIds,
            decoded.Outcomes,
            decoded.PreviousContinuity,
            decoded.StateDiagnostic,
            decoded.Partitions);
        if (!semantic.AsSpan().SequenceEqual(history.NormalizedPayloadBytes))
            throw new InvalidDataException("qa04.transition-decode.semantic-body-drift");

        return ValidateDecodedHistory(history, decoded, semantic);
    }

    /// <summary>
    /// Restores a transition authority directly from the persisted history_record columns.
    /// The normalized semantic bytes are intentionally reconstructed from the canonical physical
    /// wrapper and are never trusted as an independently persisted evidence representation.
    /// </summary>
    public static Qa04TransitionCommittedAuthorityV1 RestorePersistedAndValidate(
        OpaqueId128 worldId,
        ulong sequence,
        ReadOnlySpan<byte> previousRecordDigest,
        string recordType,
        string payloadSchemaId,
        ushort payloadSchemaMajor,
        ushort payloadSchemaMinor,
        ReadOnlySpan<byte> payloadBytes,
        ReadOnlySpan<byte> normalizedPayloadDigest,
        ReadOnlySpan<byte> recordDigest)
    {
        RequireTransitionHistorySchema(
            recordType,
            payloadSchemaId,
            payloadSchemaMajor,
            payloadSchemaMinor);

        var decoded = DecodePhysicalWrapper(payloadBytes);
        var semantic = EncodeSemanticBody(
            decoded.EffectiveStep,
            decoded.ResultingStep,
            decoded.ConfigGeneration,
            decoded.ConfigDigest,
            decoded.OperationIds,
            decoded.Outcomes,
            decoded.PreviousContinuity,
            decoded.StateDiagnostic,
            decoded.Partitions);
        var history = HistoryRecordMaterial.RestoreValidated(
            worldId,
            sequence,
            previousRecordDigest,
            recordType,
            payloadSchemaId,
            payloadSchemaMajor,
            payloadSchemaMinor,
            payloadBytes,
            semantic,
            normalizedPayloadDigest,
            recordDigest);
        return ValidateDecodedHistory(history, decoded, semantic);
    }

    private static Qa04TransitionCommittedAuthorityV1 ValidateDecodedHistory(
        HistoryRecordMaterial history,
        DecodedPhysicalTransitionV1 decoded,
        byte[] semantic)
    {
        var canonicalPhysical = EncodePhysicalWrapper(
            decoded.EffectiveStep,
            decoded.ResultingStep,
            decoded.ConfigGeneration,
            decoded.ConfigDigest,
            decoded.OperationIds,
            decoded.Outcomes,
            decoded.PreviousContinuity,
            decoded.ResultingContinuity,
            decoded.StateDiagnostic,
            decoded.Partitions);
        if (!canonicalPhysical.AsSpan().SequenceEqual(history.PayloadBytes))
            throw new InvalidDataException("qa04.transition-decode.physical-not-canonical");

        var normalizedDigest = HashSuite.Hash256(semantic);
        if (!CryptographicOperations.FixedTimeEquals(normalizedDigest, history.NormalizedPayloadDigest))
            throw new InvalidDataException("qa04.transition-decode.normalized-digest-drift");
        var recordDigest = HistoryIntegrity.ComputeHistoryRecordDigest(
            history.WorldId,
            history.Sequence,
            history.PreviousRecordDigest,
            RecordTypeToken,
            semantic);
        if (!CryptographicOperations.FixedTimeEquals(recordDigest, history.RecordDigest))
            throw new InvalidDataException("qa04.transition-decode.record-digest-drift");
        var expectedResultingContinuity = HistoryIntegrity.ComputeTransitionContinuityToken(
            history.WorldId,
            decoded.ResultingStep,
            decoded.PreviousContinuity,
            recordDigest);
        if (!CryptographicOperations.FixedTimeEquals(expectedResultingContinuity, decoded.ResultingContinuity))
            throw new InvalidDataException("qa04.transition-decode.resulting-continuity-drift");

        return new Qa04TransitionCommittedAuthorityV1(
            decoded.EffectiveStep,
            decoded.ResultingStep,
            decoded.ConfigGeneration,
            decoded.ConfigDigest,
            decoded.OperationIds,
            decoded.Outcomes,
            decoded.PreviousContinuity,
            decoded.ResultingContinuity,
            decoded.StateDiagnostic,
            decoded.Partitions,
            history);
    }

    private static void RequireTransitionHistorySchema(
        string recordType,
        string payloadSchemaId,
        ushort payloadSchemaMajor,
        ushort payloadSchemaMinor)
    {
        if (!string.Equals(recordType, RecordType, StringComparison.Ordinal) ||
            !string.Equals(payloadSchemaId, PayloadSchemaId, StringComparison.Ordinal) ||
            payloadSchemaMajor != 1 ||
            payloadSchemaMinor != 0)
            throw new InvalidDataException("qa04.transition-decode.schema-drift");
    }

    private sealed record DecodedPhysicalTransitionV1(
        ulong EffectiveStep,
        ulong ResultingStep,
        ulong ConfigGeneration,
        byte[] ConfigDigest,
        IReadOnlyList<OpaqueId128> OperationIds,
        IReadOnlyList<TerminalOperationCommit> Outcomes,
        byte[] PreviousContinuity,
        byte[] ResultingContinuity,
        byte[] StateDiagnostic,
        IReadOnlyList<Qa04TransitionPartitionDigestV1> Partitions);

    private static DecodedPhysicalTransitionV1 DecodePhysicalWrapper(ReadOnlySpan<byte> payloadBytes)
    {
        var reader = new StrictMvDcborReaderV1(payloadBytes);
        if (reader.ReadMapStart() != 10) throw new InvalidDataException("qa04.transition-decode.wrapper-map-size");

        reader.RequireUnsignedKey(0);
        var effectiveStep = reader.ReadUnsigned();
        reader.RequireUnsignedKey(1);
        var resultingStep = reader.ReadUnsigned();
        if (effectiveStep == ulong.MaxValue || resultingStep != effectiveStep + 1UL)
            throw new InvalidDataException("qa04.transition-decode.step-drift");

        reader.RequireUnsignedKey(2);
        var configGeneration = reader.ReadUnsigned();
        if (configGeneration == 0) throw new InvalidDataException("qa04.transition-decode.config-generation-invalid");
        reader.RequireUnsignedKey(3);
        var configDigest = reader.ReadBytesExact(32);

        reader.RequireUnsignedKey(4);
        var operationCount = reader.ReadArrayStart();
        if (operationCount > int.MaxValue) throw new InvalidDataException("qa04.transition-decode.operation-count-overflow");
        var operationIds = new OpaqueId128[(int)operationCount];
        var seenIds = new HashSet<OpaqueId128>();
        for (var index = 0; index < operationIds.Length; index++)
        {
            var operationId = OpaqueId128.FromBytes(reader.ReadBytesExact(16));
            if (operationId.IsZero || !seenIds.Add(operationId))
                throw new InvalidDataException("qa04.transition-decode.operation-id-invalid");
            operationIds[index] = operationId;
        }

        reader.RequireUnsignedKey(5);
        var outcomeCount = reader.ReadArrayStart();
        if (outcomeCount != operationCount)
            throw new InvalidDataException("qa04.transition-decode.outcome-count-drift");
        var outcomes = new TerminalOperationCommit[operationIds.Length];
        for (var index = 0; index < outcomes.Length; index++)
        {
            if (reader.ReadArrayStart() != 4)
                throw new InvalidDataException("qa04.transition-decode.outcome-shape");
            var operationId = OpaqueId128.FromBytes(reader.ReadBytesExact(16));
            if (operationId != operationIds[index])
                throw new InvalidDataException("qa04.transition-decode.outcome-id-order-drift");
            var terminalStatusRaw = reader.ReadUnsigned();
            if (terminalStatusRaw > int.MaxValue ||
                !Enum.IsDefined(typeof(CoreOperationResultStatusV1), (int)terminalStatusRaw) ||
                !OperationLifecycleRulesV1.IsTerminalResult((CoreOperationResultStatusV1)(int)terminalStatusRaw))
                throw new InvalidDataException("qa04.transition-decode.outcome-status-invalid");
            var resultCode = reader.ReadAsciiText();
            _ = new StableToken(resultCode);
            var richCount = reader.ReadArrayStart();
            byte[]? richResult = richCount switch
            {
                0 => null,
                1 => reader.ReadBytes(),
                _ => throw new InvalidDataException("qa04.transition-decode.rich-result-shape"),
            };
            outcomes[index] = new TerminalOperationCommit(operationId, (int)terminalStatusRaw, resultCode, richResult);
        }

        reader.RequireUnsignedKey(6);
        var previousContinuity = reader.ReadBytesExact(32);
        reader.RequireUnsignedKey(7);
        var resultingContinuity = reader.ReadBytesExact(32);
        reader.RequireUnsignedKey(8);
        var stateDiagnostic = reader.ReadBytesExact(32);
        reader.RequireUnsignedKey(9);
        var partitionCount = reader.ReadArrayStart();
        if (partitionCount > int.MaxValue) throw new InvalidDataException("qa04.transition-decode.partition-count-overflow");
        var partitions = new Qa04TransitionPartitionDigestV1[(int)partitionCount];
        string? previousPartitionId = null;
        for (var index = 0; index < partitions.Length; index++)
        {
            if (reader.ReadArrayStart() != 2)
                throw new InvalidDataException("qa04.transition-decode.partition-shape");
            var partitionId = reader.ReadAsciiText();
            _ = new StableToken(partitionId);
            if (previousPartitionId is not null &&
                StringComparer.Ordinal.Compare(previousPartitionId, partitionId) >= 0)
                throw new InvalidDataException("qa04.transition-decode.partition-order-drift");
            previousPartitionId = partitionId;
            partitions[index] = new Qa04TransitionPartitionDigestV1(partitionId, reader.ReadBytesExact(32));
        }
        reader.RequireEnd();

        return new DecodedPhysicalTransitionV1(
            effectiveStep,
            resultingStep,
            configGeneration,
            configDigest,
            Array.AsReadOnly(operationIds),
            Array.AsReadOnly(outcomes),
            previousContinuity,
            resultingContinuity,
            stateDiagnostic,
            Array.AsReadOnly(partitions));
    }

    public static void RequireEquivalent(
        Qa04TransitionCommittedAuthorityV1 actual,
        Qa04TransitionCommittedAuthorityV1 expected,
        string error)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(expected);
        if (actual.EffectiveStep != expected.EffectiveStep ||
            actual.ResultingStep != expected.ResultingStep ||
            actual.ActiveConfigGeneration != expected.ActiveConfigGeneration ||
            !CryptographicOperations.FixedTimeEquals(actual.ActiveConfigDigest, expected.ActiveConfigDigest) ||
            !CryptographicOperations.FixedTimeEquals(actual.PreviousStateContinuityToken, expected.PreviousStateContinuityToken) ||
            !CryptographicOperations.FixedTimeEquals(actual.ResultingStateContinuityToken, expected.ResultingStateContinuityToken) ||
            !CryptographicOperations.FixedTimeEquals(actual.StateDiagnosticHash, expected.StateDiagnosticHash) ||
            actual.AppliedOperationIds.Count != expected.AppliedOperationIds.Count ||
            actual.OperationOutcomes.Count != expected.OperationOutcomes.Count ||
            actual.PartitionDigests.Count != expected.PartitionDigests.Count ||
            !CryptographicOperations.FixedTimeEquals(actual.History.RecordDigest, expected.History.RecordDigest) ||
            !CryptographicOperations.FixedTimeEquals(actual.History.NormalizedPayloadDigest, expected.History.NormalizedPayloadDigest))
            throw new InvalidDataException(error);

        for (var index = 0; index < actual.AppliedOperationIds.Count; index++)
        {
            if (actual.AppliedOperationIds[index] != expected.AppliedOperationIds[index] ||
                !TerminalEquals(actual.OperationOutcomes[index], expected.OperationOutcomes[index]))
                throw new InvalidDataException(error);
        }
        for (var index = 0; index < actual.PartitionDigests.Count; index++)
        {
            var left = actual.PartitionDigests[index];
            var right = expected.PartitionDigests[index];
            if (!string.Equals(left.PartitionId, right.PartitionId, StringComparison.Ordinal) ||
                !CryptographicOperations.FixedTimeEquals(left.ResultingDigest, right.ResultingDigest))
                throw new InvalidDataException(error);
        }
    }

    public static bool TerminalEquals(TerminalOperationCommit left, TerminalOperationCommit right)
        => left.OperationId == right.OperationId &&
           left.TerminalStatus == right.TerminalStatus &&
           string.Equals(left.ResultCode, right.ResultCode, StringComparison.Ordinal) &&
           NullableBytesEqual(left.RichResultPayload, right.RichResultPayload);

    private static (IReadOnlyList<OpaqueId128> Ids, IReadOnlyList<TerminalOperationCommit> Outcomes) CopyValidatedCanonicalOperations(
        IReadOnlyList<TerminalOperationCommit> operationOutcomes)
    {
        var ids = new OpaqueId128[operationOutcomes.Count];
        var outcomes = new TerminalOperationCommit[operationOutcomes.Count];
        for (var index = 0; index < operationOutcomes.Count; index++)
        {
            var outcome = operationOutcomes[index]
                ?? throw new InvalidDataException("qa04.transition-authority.outcome-null");
            if (outcome.OperationId.IsZero)
                throw new InvalidDataException("qa04.transition-authority.operation-id-order-drift");
            if (!Enum.IsDefined(typeof(CoreOperationResultStatusV1), outcome.TerminalStatus) ||
                !OperationLifecycleRulesV1.IsTerminalResult((CoreOperationResultStatusV1)outcome.TerminalStatus))
                throw new InvalidDataException("qa04.transition-authority.outcome-status-invalid");
            _ = new StableToken(outcome.ResultCode);

            ids[index] = outcome.OperationId;
            outcomes[index] = new TerminalOperationCommit(
                outcome.OperationId,
                outcome.TerminalStatus,
                outcome.ResultCode,
                outcome.RichResultPayload?.ToArray());
        }

        return (Array.AsReadOnly(ids), Array.AsReadOnly(outcomes));
    }

    private static (IReadOnlyList<OpaqueId128> Ids, IReadOnlyList<TerminalOperationCommit> Outcomes) ValidateAndCopyOperations(
        IReadOnlyList<OpaqueId128> appliedOperationIds,
        IReadOnlyList<TerminalOperationCommit> operationOutcomes)
    {
        if (appliedOperationIds.Count != operationOutcomes.Count)
            throw new InvalidDataException("qa04.transition-authority.operation-outcome-count-drift");
        var ids = new OpaqueId128[appliedOperationIds.Count];
        var outcomes = new TerminalOperationCommit[operationOutcomes.Count];
        var seen = new HashSet<OpaqueId128>();
        for (var index = 0; index < ids.Length; index++)
        {
            var id = appliedOperationIds[index];
            var outcome = operationOutcomes[index];
            if (id.IsZero || !seen.Add(id) || outcome.OperationId != id)
                throw new InvalidDataException("qa04.transition-authority.operation-id-order-drift");
            if (!Enum.IsDefined(typeof(CoreOperationResultStatusV1), outcome.TerminalStatus) ||
                !OperationLifecycleRulesV1.IsTerminalResult((CoreOperationResultStatusV1)outcome.TerminalStatus))
                throw new InvalidDataException("qa04.transition-authority.outcome-status-invalid");
            _ = new StableToken(outcome.ResultCode);
            ids[index] = id;
            outcomes[index] = new TerminalOperationCommit(
                outcome.OperationId,
                outcome.TerminalStatus,
                outcome.ResultCode,
                outcome.RichResultPayload?.ToArray());
        }
        return (Array.AsReadOnly(ids), Array.AsReadOnly(outcomes));
    }

    private static IReadOnlyList<Qa04TransitionPartitionDigestV1> ValidateAndCopyPartitions(
        IReadOnlyCollection<Qa04TransitionPartitionDigestV1> partitionDigests)
    {
        var partitions = partitionDigests
            .Select(partition =>
            {
                ArgumentNullException.ThrowIfNull(partition);
                _ = new StableToken(partition.PartitionId);
                RequireHash256(partition.ResultingDigest, "qa04.transition-authority.partition-digest-invalid");
                return new Qa04TransitionPartitionDigestV1(partition.PartitionId, partition.ResultingDigest.ToArray());
            })
            .OrderBy(static partition => partition.PartitionId, StringComparer.Ordinal)
            .ToArray();
        if (partitions.Select(static partition => partition.PartitionId).Distinct(StringComparer.Ordinal).Count() != partitions.Length)
            throw new InvalidDataException("qa04.transition-authority.partition-duplicate");
        return Array.AsReadOnly(partitions);
    }

    private static byte[] EncodeSemanticBody(
        ulong effectiveStep,
        ulong resultingStep,
        ulong configGeneration,
        ReadOnlySpan<byte> configDigest,
        IReadOnlyList<OpaqueId128> operationIds,
        IReadOnlyList<TerminalOperationCommit> outcomes,
        ReadOnlySpan<byte> previousContinuity,
        ReadOnlySpan<byte> stateDiagnostic,
        IReadOnlyList<Qa04TransitionPartitionDigestV1> partitions)
    {
        var writer = new MvDcborWriter();
        writer.WriteMapStart(9);
        writer.WriteUnsigned(0); writer.WriteUnsigned(effectiveStep);
        writer.WriteUnsigned(1); writer.WriteUnsigned(resultingStep);
        writer.WriteUnsigned(2); writer.WriteUnsigned(configGeneration);
        writer.WriteUnsigned(3); writer.WriteBytes(configDigest);
        writer.WriteUnsigned(4); WriteOperationIds(writer, operationIds);
        writer.WriteUnsigned(5); WriteOutcomes(writer, outcomes);
        writer.WriteUnsigned(6); writer.WriteBytes(previousContinuity);
        writer.WriteUnsigned(7); writer.WriteBytes(stateDiagnostic);
        writer.WriteUnsigned(8); WritePartitions(writer, partitions);
        return writer.ToArray();
    }

    private static byte[] EncodePhysicalWrapper(
        ulong effectiveStep,
        ulong resultingStep,
        ulong configGeneration,
        ReadOnlySpan<byte> configDigest,
        IReadOnlyList<OpaqueId128> operationIds,
        IReadOnlyList<TerminalOperationCommit> outcomes,
        ReadOnlySpan<byte> previousContinuity,
        ReadOnlySpan<byte> resultingContinuity,
        ReadOnlySpan<byte> stateDiagnostic,
        IReadOnlyList<Qa04TransitionPartitionDigestV1> partitions)
    {
        var writer = new MvDcborWriter();
        writer.WriteMapStart(10);
        writer.WriteUnsigned(0); writer.WriteUnsigned(effectiveStep);
        writer.WriteUnsigned(1); writer.WriteUnsigned(resultingStep);
        writer.WriteUnsigned(2); writer.WriteUnsigned(configGeneration);
        writer.WriteUnsigned(3); writer.WriteBytes(configDigest);
        writer.WriteUnsigned(4); WriteOperationIds(writer, operationIds);
        writer.WriteUnsigned(5); WriteOutcomes(writer, outcomes);
        writer.WriteUnsigned(6); writer.WriteBytes(previousContinuity);
        writer.WriteUnsigned(7); writer.WriteBytes(resultingContinuity);
        writer.WriteUnsigned(8); writer.WriteBytes(stateDiagnostic);
        writer.WriteUnsigned(9); WritePartitions(writer, partitions);
        return writer.ToArray();
    }

    private static void WriteOperationIds(MvDcborWriter writer, IReadOnlyList<OpaqueId128> operationIds)
    {
        writer.WriteArrayStart((ulong)operationIds.Count);
        foreach (var operationId in operationIds) writer.WriteBytes(operationId.ToBytes());
    }

    private static void WriteOutcomes(MvDcborWriter writer, IReadOnlyList<TerminalOperationCommit> outcomes)
    {
        writer.WriteArrayStart((ulong)outcomes.Count);
        foreach (var outcome in outcomes)
        {
            writer.WriteArrayStart(4);
            writer.WriteBytes(outcome.OperationId.ToBytes());
            writer.WriteUnsigned(checked((ulong)outcome.TerminalStatus));
            writer.WriteAsciiText(outcome.ResultCode);
            if (outcome.RichResultPayload is null)
            {
                writer.WriteArrayStart(0);
            }
            else
            {
                writer.WriteArrayStart(1);
                writer.WriteBytes(outcome.RichResultPayload);
            }
        }
    }

    private static void WritePartitions(MvDcborWriter writer, IReadOnlyList<Qa04TransitionPartitionDigestV1> partitions)
    {
        writer.WriteArrayStart((ulong)partitions.Count);
        foreach (var partition in partitions)
        {
            writer.WriteArrayStart(2);
            writer.WriteAsciiText(partition.PartitionId);
            writer.WriteBytes(partition.ResultingDigest);
        }
    }

    private static bool NullableBytesEqual(byte[]? left, byte[]? right)
    {
        if (left is null || right is null) return left is null && right is null;
        return left.AsSpan().SequenceEqual(right);
    }

    private static void RequireHash256(ReadOnlySpan<byte> value, string error)
    {
        if (value.Length != 32) throw new InvalidDataException(error);
    }

    private ref struct StrictMvDcborReaderV1
    {
        private readonly ReadOnlySpan<byte> _bytes;
        private int _offset;

        public StrictMvDcborReaderV1(ReadOnlySpan<byte> bytes)
        {
            if (bytes.IsEmpty) throw new InvalidDataException("qa04.transition-decode.empty-payload");
            _bytes = bytes;
            _offset = 0;
        }

        public ulong ReadUnsigned() => ReadLength(0);
        public ulong ReadArrayStart() => ReadLength(4);
        public ulong ReadMapStart() => ReadLength(5);

        public void RequireUnsignedKey(ulong expected)
        {
            if (ReadUnsigned() != expected) throw new InvalidDataException("qa04.transition-decode.map-key-drift");
        }

        public byte[] ReadBytes()
        {
            var length = ReadLength(2);
            if (length > int.MaxValue || length > (ulong)(_bytes.Length - _offset))
                throw new InvalidDataException("qa04.transition-decode.byte-string-length");
            var result = _bytes.Slice(_offset, (int)length).ToArray();
            _offset += (int)length;
            return result;
        }

        public byte[] ReadBytesExact(int expectedLength)
        {
            var value = ReadBytes();
            if (value.Length != expectedLength) throw new InvalidDataException("qa04.transition-decode.byte-string-size");
            return value;
        }

        public string ReadAsciiText()
        {
            var length = ReadLength(3);
            if (length > int.MaxValue || length > (ulong)(_bytes.Length - _offset))
                throw new InvalidDataException("qa04.transition-decode.text-length");
            var span = _bytes.Slice(_offset, (int)length);
            if (span.IndexOfAnyExceptInRange((byte)0, (byte)0x7f) >= 0)
                throw new InvalidDataException("qa04.transition-decode.text-not-ascii");
            _offset += (int)length;
            return Encoding.ASCII.GetString(span);
        }

        public void RequireEnd()
        {
            if (_offset != _bytes.Length) throw new InvalidDataException("qa04.transition-decode.trailing-bytes");
        }

        private ulong ReadLength(byte expectedMajorType)
        {
            if (_offset >= _bytes.Length) throw new InvalidDataException("qa04.transition-decode.unexpected-end");
            var initial = _bytes[_offset++];
            var majorType = (byte)(initial >> 5);
            var additional = (byte)(initial & 0x1f);
            if (majorType != expectedMajorType || additional == 31)
                throw new InvalidDataException("qa04.transition-decode.major-type-drift");
            if (additional < 24) return additional;
            return additional switch
            {
                24 => ReadU8Canonical(),
                25 => ReadU16Canonical(),
                26 => ReadU32Canonical(),
                27 => ReadU64Canonical(),
                _ => throw new InvalidDataException("qa04.transition-decode.additional-info-invalid"),
            };
        }

        private ulong ReadU8Canonical()
        {
            RequireRemaining(1);
            var value = _bytes[_offset++];
            if (value < 24) throw new InvalidDataException("qa04.transition-decode.noncanonical-u8");
            return value;
        }

        private ulong ReadU16Canonical()
        {
            RequireRemaining(2);
            var value = BinaryPrimitives.ReadUInt16BigEndian(_bytes.Slice(_offset, 2));
            _offset += 2;
            if (value <= byte.MaxValue) throw new InvalidDataException("qa04.transition-decode.noncanonical-u16");
            return value;
        }

        private ulong ReadU32Canonical()
        {
            RequireRemaining(4);
            var value = BinaryPrimitives.ReadUInt32BigEndian(_bytes.Slice(_offset, 4));
            _offset += 4;
            if (value <= ushort.MaxValue) throw new InvalidDataException("qa04.transition-decode.noncanonical-u32");
            return value;
        }

        private ulong ReadU64Canonical()
        {
            RequireRemaining(8);
            var value = BinaryPrimitives.ReadUInt64BigEndian(_bytes.Slice(_offset, 8));
            _offset += 8;
            if (value <= uint.MaxValue) throw new InvalidDataException("qa04.transition-decode.noncanonical-u64");
            return value;
        }

        private void RequireRemaining(int count)
        {
            if (count < 0 || _offset > _bytes.Length - count)
                throw new InvalidDataException("qa04.transition-decode.unexpected-end");
        }
    }
}
