using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Runtime;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed record CoreOperationStateSnapshotFragmentV2(
    ulong BasisStep,
    IReadOnlyList<DurableOperationStateV1> Operations,
    IReadOnlyList<CrossDomainTransactionStateV1> Transactions);

public static class CoreOperationStateSnapshotWireCodecV2
{
    public static byte[] Encode(
        ulong basisStep,
        IReadOnlyList<DurableOperationStateV1> operations,
        IReadOnlyList<CrossDomainTransactionStateV1> transactions)
    {
        var authority = CoreOperationStateSnapshotAuthorityV2.Create(operations, transactions, basisStep);
        using var stream = new MemoryStream();
        CoreSnapshotProtoV1.WriteUInt64(stream, 1, basisStep);
        foreach (var operation in authority.Operations)
            CoreSnapshotProtoV1.WriteMessage(stream, 2, EncodeItem(EncodeOperation(operation), 1));
        foreach (var transaction in authority.Transactions)
            CoreSnapshotProtoV1.WriteMessage(stream, 2, EncodeItem(EncodeTransaction(transaction), 2));
        return stream.ToArray();
    }

    public static CoreOperationStateSnapshotFragmentV2 Decode(ReadOnlySpan<byte> encoded)
    {
        var reader = new CoreSnapshotProtoV1.Reader(encoded);
        ulong basisStep = 0;
        var seenBasis = false;
        var operations = new List<DurableOperationStateV1>();
        var transactions = new List<CrossDomainTransactionStateV1>();
        var transactionArmSeen = false;

        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag("snapshot-core.operation-v2.field");
            switch (field)
            {
                case 1:
                    if (seenBasis) throw new InvalidDataException("snapshot-core.operation-v2.duplicate-basis-step");
                    seenBasis = true;
                    basisStep = reader.ReadUInt64(wire, "snapshot-core.operation-v2.basis-step");
                    break;
                case 2:
                {
                    var item = DecodeItem(reader.ReadBytes(wire, "snapshot-core.operation-v2.item"));
                    if (item is DurableOperationStateV1 operation)
                    {
                        if (transactionArmSeen) throw new InvalidDataException("snapshot-core.operation-v2.item-kind-order");
                        operations.Add(operation);
                    }
                    else
                    {
                        transactionArmSeen = true;
                        transactions.Add((CrossDomainTransactionStateV1)item);
                    }
                    break;
                }
                default:
                    throw new InvalidDataException("snapshot-core.operation-v2.unknown-field");
            }
        }

        if (!seenBasis) throw new InvalidDataException("snapshot-core.operation-v2.required-field-missing");
        var authority = CoreOperationStateSnapshotAuthorityV2.Create(operations, transactions, basisStep);
        RequireSameOrder(operations, authority.Operations, static value => value.OperationId,
            "snapshot-core.operation-v2.noncanonical-operation-order");
        RequireSameOrder(transactions, authority.Transactions, static value => value.TransactionId,
            "snapshot-core.operation-v2.noncanonical-transaction-order");
        return new CoreOperationStateSnapshotFragmentV2(
            basisStep,
            Array.AsReadOnly(operations.ToArray()),
            Array.AsReadOnly(transactions.ToArray()));
    }

    public static int EncodedOperationItemFieldLength(DurableOperationStateV1 operation)
        => CoreSnapshotProtoV1.LengthDelimitedFieldLength(2, EncodeItem(EncodeOperation(operation), 1).Length);

    public static int EncodedTransactionItemFieldLength(CrossDomainTransactionStateV1 transaction)
        => CoreSnapshotProtoV1.LengthDelimitedFieldLength(2, EncodeItem(EncodeTransaction(transaction), 2).Length);

    private static byte[] EncodeItem(byte[] payload, int arm)
    {
        using var stream = new MemoryStream();
        CoreSnapshotProtoV1.WriteMessage(stream, arm, payload);
        return stream.ToArray();
    }

    private static object DecodeItem(ReadOnlySpan<byte> encoded)
    {
        var reader = new CoreSnapshotProtoV1.Reader(encoded);
        object? result = null;
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag("snapshot-core.operation-v2.item-field");
            if (result is not null) throw new InvalidDataException("snapshot-core.operation-v2.item-oneof");
            result = field switch
            {
                1 => DecodeOperation(reader.ReadBytes(wire, "snapshot-core.operation-v2.durable-operation")),
                2 => DecodeTransaction(reader.ReadBytes(wire, "snapshot-core.operation-v2.cross-domain-transaction")),
                _ => throw new InvalidDataException("snapshot-core.operation-v2.item-unknown-field"),
            };
        }
        return result ?? throw new InvalidDataException("snapshot-core.operation-v2.item-empty");
    }

    private static byte[] EncodeOperation(DurableOperationStateV1 operation)
    {
        _ = DurableOperationSubstateV1.Canonicalize([operation]);
        using var stream = new MemoryStream();
        CoreSnapshotProtoV1.WriteBytes(stream, 1, operation.OperationId.ToBytes());
        CoreSnapshotProtoV1.WriteBytes(stream, 2, operation.OperationPayloadDigest);
        CoreSnapshotProtoV1.WriteUInt32(stream, 3, checked((uint)operation.Lifecycle));
        WriteOptional(stream, 4, operation.AcceptedSequence);
        WriteOptional(stream, 5, operation.ScheduledSequence);
        WriteOptional(stream, 6, operation.EffectiveStep);
        WriteOptional(stream, 7, operation.TerminalSequence);
        if (operation.TerminalStatus is { } terminalStatus)
            CoreSnapshotProtoV1.WriteUInt32(stream, 8, checked((uint)terminalStatus));
        if (operation.ResultCode is not null) CoreSnapshotProtoV1.WriteString(stream, 9, operation.ResultCode);
        if (operation.RichResultPayload is not null) CoreSnapshotProtoV1.WriteBytes(stream, 10, operation.RichResultPayload);
        return stream.ToArray();
    }

    private static DurableOperationStateV1 DecodeOperation(ReadOnlySpan<byte> encoded)
    {
        var reader = new CoreSnapshotProtoV1.Reader(encoded);
        byte[]? id = null;
        byte[]? digest = null;
        DurableOperationLifecycleV1 lifecycle = 0;
        ulong? accepted = null, scheduled = null, effective = null, terminalSequence = null;
        int? terminalStatus = null;
        string? resultCode = null;
        byte[]? rich = null;
        uint seen = 0;
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag("snapshot-core.operation-v2.operation-field");
            if (field is < 1 or > 10) throw new InvalidDataException("snapshot-core.operation-v2.operation-unknown-field");
            RequireOnce(ref seen, field, "snapshot-core.operation-v2.operation-duplicate-field");
            switch (field)
            {
                case 1: id = reader.ReadBytes(wire, "snapshot-core.operation-v2.operation-id"); break;
                case 2: digest = reader.ReadBytes(wire, "snapshot-core.operation-v2.operation-digest"); break;
                case 3:
                {
                    var raw = reader.ReadUInt32(wire, "snapshot-core.operation-v2.operation-lifecycle");
                    if (raw > int.MaxValue || !Enum.IsDefined(typeof(DurableOperationLifecycleV1), (int)raw))
                        throw new InvalidDataException("snapshot-core.operation-v2.operation-lifecycle");
                    lifecycle = (DurableOperationLifecycleV1)(int)raw;
                    break;
                }
                case 4: accepted = reader.ReadUInt64(wire, "snapshot-core.operation-v2.accepted-sequence"); break;
                case 5: scheduled = reader.ReadUInt64(wire, "snapshot-core.operation-v2.scheduled-sequence"); break;
                case 6: effective = reader.ReadUInt64(wire, "snapshot-core.operation-v2.effective-step"); break;
                case 7: terminalSequence = reader.ReadUInt64(wire, "snapshot-core.operation-v2.terminal-sequence"); break;
                case 8:
                {
                    var raw = reader.ReadUInt32(wire, "snapshot-core.operation-v2.terminal-status");
                    if (raw > int.MaxValue || !Enum.IsDefined(typeof(CoreOperationResultStatusV1), (int)raw))
                        throw new InvalidDataException("snapshot-core.operation-v2.terminal-status");
                    terminalStatus = (int)raw;
                    break;
                }
                case 9: resultCode = reader.ReadString(wire, "snapshot-core.operation-v2.result-code"); _ = new StableToken(resultCode); break;
                case 10: rich = reader.ReadBytes(wire, "snapshot-core.operation-v2.rich-result"); break;
            }
        }
        if ((seen & 0b111) != 0b111 || id is null || id.Length != 16 || digest is null || digest.Length != 32)
            throw new InvalidDataException("snapshot-core.operation-v2.operation-shape");
        var result = new DurableOperationStateV1(OpaqueId128.FromBytes(id), digest, lifecycle, accepted, scheduled, effective,
            terminalSequence, terminalStatus, resultCode, rich);
        _ = DurableOperationSubstateV1.Canonicalize([result]);
        return result;
    }

    private static byte[] EncodeTransaction(CrossDomainTransactionStateV1 state)
    {
        using var stream = new MemoryStream();
        CoreSnapshotProtoV1.WriteBytes(stream, 1, state.TransactionId.ToBytes());
        CoreSnapshotProtoV1.WriteString(stream, 2, state.TransactionKind.Value);
        CoreSnapshotProtoV1.WriteUInt32(stream, 3, (uint)state.Lifecycle);
        CoreSnapshotProtoV1.WriteUInt64(stream, 4, state.CreatedStep);
        CoreSnapshotProtoV1.WriteUInt64(stream, 5, state.UpdatedStep);
        WriteOptional(stream, 6, state.TerminalStep);
        CoreSnapshotProtoV1.WriteMessage(stream, 7, EncodeCausality(state.RootCausality));
        foreach (var subject in state.SubjectIds) CoreSnapshotProtoV1.WriteBytes(stream, 8, subject.ToBytes());
        foreach (var participant in state.Participants) CoreSnapshotProtoV1.WriteMessage(stream, 9, EncodeParticipant(participant));
        foreach (var invariant in state.InvariantResults) CoreSnapshotProtoV1.WriteMessage(stream, 10, EncodeInvariant(invariant));
        return stream.ToArray();
    }

    private static CrossDomainTransactionStateV1 DecodeTransaction(ReadOnlySpan<byte> encoded)
    {
        var reader = new CoreSnapshotProtoV1.Reader(encoded);
        byte[]? id = null;
        string? kind = null;
        TransactionLifecycleV1 lifecycle = 0;
        ulong created = 0, updated = 0;
        ulong? terminal = null;
        CausalityRefV1? root = null;
        var subjects = new List<OpaqueId128>();
        var participants = new List<PersistentTransactionParticipantV1>();
        var invariants = new List<InvariantResultV1>();
        uint seen = 0;
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag("snapshot-core.operation-v2.transaction-field");
            switch (field)
            {
                case 1: RequireOnce(ref seen, field, "snapshot-core.operation-v2.transaction-duplicate-field"); id = reader.ReadBytes(wire, "snapshot-core.operation-v2.transaction-id"); break;
                case 2: RequireOnce(ref seen, field, "snapshot-core.operation-v2.transaction-duplicate-field"); kind = reader.ReadString(wire, "snapshot-core.operation-v2.transaction-kind"); break;
                case 3:
                {
                    RequireOnce(ref seen, field, "snapshot-core.operation-v2.transaction-duplicate-field");
                    var raw = reader.ReadUInt32(wire, "snapshot-core.operation-v2.transaction-lifecycle");
                    if (raw > byte.MaxValue || !Enum.IsDefined((TransactionLifecycleV1)(byte)raw))
                        throw new InvalidDataException("snapshot-core.operation-v2.transaction-lifecycle");
                    lifecycle = (TransactionLifecycleV1)(byte)raw;
                    break;
                }
                case 4: RequireOnce(ref seen, field, "snapshot-core.operation-v2.transaction-duplicate-field"); created = reader.ReadUInt64(wire, "snapshot-core.operation-v2.transaction-created-step"); break;
                case 5: RequireOnce(ref seen, field, "snapshot-core.operation-v2.transaction-duplicate-field"); updated = reader.ReadUInt64(wire, "snapshot-core.operation-v2.transaction-updated-step"); break;
                case 6: RequireOnce(ref seen, field, "snapshot-core.operation-v2.transaction-duplicate-field"); terminal = reader.ReadUInt64(wire, "snapshot-core.operation-v2.transaction-terminal-step"); break;
                case 7: RequireOnce(ref seen, field, "snapshot-core.operation-v2.transaction-duplicate-field"); root = DecodeCausality(reader.ReadBytes(wire, "snapshot-core.operation-v2.transaction-root")); break;
                case 8:
                {
                    var bytes = reader.ReadBytes(wire, "snapshot-core.operation-v2.transaction-subject");
                    if (bytes.Length != 16) throw new InvalidDataException("snapshot-core.operation-v2.transaction-subject");
                    subjects.Add(OpaqueId128.FromBytes(bytes));
                    break;
                }
                case 9: participants.Add(DecodeParticipant(reader.ReadBytes(wire, "snapshot-core.operation-v2.transaction-participant"))); break;
                case 10: invariants.Add(DecodeInvariant(reader.ReadBytes(wire, "snapshot-core.operation-v2.transaction-invariant"))); break;
                default: throw new InvalidDataException("snapshot-core.operation-v2.transaction-unknown-field");
            }
        }
        const uint required = (1u << 0) | (1u << 1) | (1u << 2) | (1u << 3) | (1u << 4) | (1u << 6);
        if ((seen & required) != required || id is null || id.Length != 16 || kind is null || root is null)
            throw new InvalidDataException("snapshot-core.operation-v2.transaction-shape");
        return new CrossDomainTransactionStateV1(OpaqueId128.FromBytes(id), new StableToken(kind), lifecycle, created, updated,
            terminal, root, subjects, participants, invariants);
    }

    private static byte[] EncodeCausality(CausalityRefV1 value)
    {
        using var stream = new MemoryStream();
        CoreSnapshotProtoV1.WriteUInt32(stream, 1, checked((uint)value.Kind));
        CoreSnapshotProtoV1.WriteBytes(stream, 2, value.Id);
        WriteOptional(stream, 3, value.BasisStep);
        return stream.ToArray();
    }

    private static CausalityRefV1 DecodeCausality(ReadOnlySpan<byte> encoded)
    {
        var reader = new CoreSnapshotProtoV1.Reader(encoded);
        CausalityRefKindV1 kind = 0;
        byte[]? id = null;
        ulong? basis = null;
        uint seen = 0;
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag("snapshot-core.operation-v2.causality-field");
            RequireOnce(ref seen, field, "snapshot-core.operation-v2.causality-duplicate-field");
            switch (field)
            {
                case 1:
                {
                    var raw = reader.ReadUInt32(wire, "snapshot-core.operation-v2.causality-kind");
                    if (raw > int.MaxValue || !Enum.IsDefined(typeof(CausalityRefKindV1), (int)raw))
                        throw new InvalidDataException("snapshot-core.operation-v2.causality-kind");
                    kind = (CausalityRefKindV1)(int)raw;
                    break;
                }
                case 2: id = reader.ReadBytes(wire, "snapshot-core.operation-v2.causality-id"); break;
                case 3: basis = reader.ReadUInt64(wire, "snapshot-core.operation-v2.causality-basis-step"); break;
                default: throw new InvalidDataException("snapshot-core.operation-v2.causality-unknown-field");
            }
        }
        if ((seen & 0b11) != 0b11 || id is null || id.Length == 0)
            throw new InvalidDataException("snapshot-core.operation-v2.causality-shape");
        return new CausalityRefV1(kind, id, basis);
    }

    private static byte[] EncodeParticipant(PersistentTransactionParticipantV1 value)
    {
        using var stream = new MemoryStream();
        CoreSnapshotProtoV1.WriteString(stream, 1, value.DomainToken.Value);
        CoreSnapshotProtoV1.WriteString(stream, 2, value.PartitionId.Value);
        foreach (var intent in value.IntentIds) CoreSnapshotProtoV1.WriteBytes(stream, 3, intent.ToBytes());
        CoreSnapshotProtoV1.WriteUInt32(stream, 4, value.Required ? 1u : 0u);
        CoreSnapshotProtoV1.WriteUInt32(stream, 5, (uint)value.Outcome);
        CoreSnapshotProtoV1.WriteBytes(stream, 6, value.CandidateEffectDigest);
        if (value.DiagnosticCode is { } diagnostic) CoreSnapshotProtoV1.WriteString(stream, 7, diagnostic.Value);
        return stream.ToArray();
    }

    private static PersistentTransactionParticipantV1 DecodeParticipant(ReadOnlySpan<byte> encoded)
    {
        var reader = new CoreSnapshotProtoV1.Reader(encoded);
        string? domain = null, partition = null, diagnostic = null;
        var intents = new List<OpaqueId128>();
        bool required = false, requiredSeen = false;
        TransactionParticipantOutcomeV1 outcome = 0;
        byte[]? digest = null;
        uint seen = 0;
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag("snapshot-core.operation-v2.participant-field");
            switch (field)
            {
                case 1: RequireOnce(ref seen, field, "snapshot-core.operation-v2.participant-duplicate-field"); domain = reader.ReadString(wire, "snapshot-core.operation-v2.participant-domain"); break;
                case 2: RequireOnce(ref seen, field, "snapshot-core.operation-v2.participant-duplicate-field"); partition = reader.ReadString(wire, "snapshot-core.operation-v2.participant-partition"); break;
                case 3:
                {
                    var bytes = reader.ReadBytes(wire, "snapshot-core.operation-v2.participant-intent");
                    if (bytes.Length != 16) throw new InvalidDataException("snapshot-core.operation-v2.participant-intent");
                    intents.Add(OpaqueId128.FromBytes(bytes));
                    break;
                }
                case 4:
                {
                    RequireOnce(ref seen, field, "snapshot-core.operation-v2.participant-duplicate-field");
                    var raw = reader.ReadUInt32(wire, "snapshot-core.operation-v2.participant-required");
                    if (raw > 1) throw new InvalidDataException("snapshot-core.operation-v2.participant-required");
                    required = raw == 1; requiredSeen = true; break;
                }
                case 5:
                {
                    RequireOnce(ref seen, field, "snapshot-core.operation-v2.participant-duplicate-field");
                    var raw = reader.ReadUInt32(wire, "snapshot-core.operation-v2.participant-outcome");
                    if (raw > byte.MaxValue || !Enum.IsDefined((TransactionParticipantOutcomeV1)(byte)raw))
                        throw new InvalidDataException("snapshot-core.operation-v2.participant-outcome");
                    outcome = (TransactionParticipantOutcomeV1)(byte)raw; break;
                }
                case 6: RequireOnce(ref seen, field, "snapshot-core.operation-v2.participant-duplicate-field"); digest = reader.ReadBytes(wire, "snapshot-core.operation-v2.participant-digest"); break;
                case 7: RequireOnce(ref seen, field, "snapshot-core.operation-v2.participant-duplicate-field"); diagnostic = reader.ReadString(wire, "snapshot-core.operation-v2.participant-diagnostic"); break;
                default: throw new InvalidDataException("snapshot-core.operation-v2.participant-unknown-field");
            }
        }
        if (domain is null || partition is null || !requiredSeen || digest is null || digest.Length != 32 || outcome == 0)
            throw new InvalidDataException("snapshot-core.operation-v2.participant-shape");
        return new PersistentTransactionParticipantV1(new StableToken(domain), new StableToken(partition), intents, required,
            outcome, digest, diagnostic is null ? null : new StableToken(diagnostic));
    }

    private static byte[] EncodeInvariant(InvariantResultV1 value)
    {
        using var stream = new MemoryStream();
        CoreSnapshotProtoV1.WriteString(stream, 1, value.InvariantId.Value);
        CoreSnapshotProtoV1.WriteUInt32(stream, 2, checked((uint)value.Severity));
        CoreSnapshotProtoV1.WriteUInt32(stream, 3, checked((uint)value.Outcome));
        if (value.DiagnosticCode is { } diagnostic) CoreSnapshotProtoV1.WriteString(stream, 4, diagnostic.Value);
        foreach (var reference in value.ParticipantRefs) CoreSnapshotProtoV1.WriteMessage(stream, 5, EncodeCausality(reference));
        return stream.ToArray();
    }

    private static InvariantResultV1 DecodeInvariant(ReadOnlySpan<byte> encoded)
    {
        var reader = new CoreSnapshotProtoV1.Reader(encoded);
        string? invariant = null, diagnostic = null;
        InvariantSeverityV1 severity = 0;
        InvariantOutcomeV1 outcome = 0;
        var severitySeen = false;
        var outcomeSeen = false;
        var refs = new List<CausalityRefV1>();
        uint seen = 0;
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag("snapshot-core.operation-v2.invariant-field");
            switch (field)
            {
                case 1: RequireOnce(ref seen, field, "snapshot-core.operation-v2.invariant-duplicate-field"); invariant = reader.ReadString(wire, "snapshot-core.operation-v2.invariant-id"); break;
                case 2:
                {
                    RequireOnce(ref seen, field, "snapshot-core.operation-v2.invariant-duplicate-field");
                    var raw = reader.ReadUInt32(wire, "snapshot-core.operation-v2.invariant-severity");
                    if (raw > int.MaxValue || !Enum.IsDefined(typeof(InvariantSeverityV1), (int)raw))
                        throw new InvalidDataException("snapshot-core.operation-v2.invariant-severity");
                    severity = (InvariantSeverityV1)(int)raw; severitySeen = true; break;
                }
                case 3:
                {
                    RequireOnce(ref seen, field, "snapshot-core.operation-v2.invariant-duplicate-field");
                    var raw = reader.ReadUInt32(wire, "snapshot-core.operation-v2.invariant-outcome");
                    if (raw > int.MaxValue || !Enum.IsDefined(typeof(InvariantOutcomeV1), (int)raw))
                        throw new InvalidDataException("snapshot-core.operation-v2.invariant-outcome");
                    outcome = (InvariantOutcomeV1)(int)raw; outcomeSeen = true; break;
                }
                case 4: RequireOnce(ref seen, field, "snapshot-core.operation-v2.invariant-duplicate-field"); diagnostic = reader.ReadString(wire, "snapshot-core.operation-v2.invariant-diagnostic"); break;
                case 5: refs.Add(DecodeCausality(reader.ReadBytes(wire, "snapshot-core.operation-v2.invariant-ref"))); break;
                default: throw new InvalidDataException("snapshot-core.operation-v2.invariant-unknown-field");
            }
        }
        if (invariant is null || !severitySeen || !outcomeSeen)
            throw new InvalidDataException("snapshot-core.operation-v2.invariant-shape");
        return new InvariantResultV1(new StableToken(invariant), severity, outcome, refs,
            diagnostic is null ? null : new StableToken(diagnostic));
    }

    private static void WriteOptional(Stream stream, int field, ulong? value)
    {
        if (value is { } present) CoreSnapshotProtoV1.WriteUInt64(stream, field, present);
    }

    private static void RequireOnce(ref uint seen, int field, string error)
    {
        if (field is < 1 or > 31) throw new InvalidDataException(error);
        var bit = 1u << (field - 1);
        if ((seen & bit) != 0) throw new InvalidDataException(error);
        seen |= bit;
    }

    private static void RequireSameOrder<T>(
        IReadOnlyList<T> actual,
        IReadOnlyList<T> expected,
        Func<T, OpaqueId128> id,
        string error)
    {
        if (actual.Count != expected.Count) throw new InvalidDataException(error);
        for (var i = 0; i < actual.Count; i++)
        {
            if (id(actual[i]) != id(expected[i])) throw new InvalidDataException(error);
        }
    }
}
