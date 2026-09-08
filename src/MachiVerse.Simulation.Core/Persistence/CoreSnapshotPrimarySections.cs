using System.Security.Cryptography;
using System.Text;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

internal static class CoreSnapshotProtoV1
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static void WriteUInt64(Stream stream, int field, ulong value)
    {
        WriteTag(stream, field, 0);
        WriteVarUInt64(stream, value);
    }

    internal static void WriteUInt32(Stream stream, int field, uint value)
        => WriteUInt64(stream, field, value);

    internal static void WriteBytes(Stream stream, int field, ReadOnlySpan<byte> value)
    {
        WriteTag(stream, field, 2);
        WriteVarUInt64(stream, checked((ulong)value.Length));
        stream.Write(value);
    }

    internal static void WriteString(Stream stream, int field, string value)
        => WriteBytes(stream, field, StrictUtf8.GetBytes(value));

    internal static void WriteMessage(Stream stream, int field, ReadOnlySpan<byte> value)
        => WriteBytes(stream, field, value);

    internal static int LengthDelimitedFieldLength(int field, int payloadLength)
        => checked(TagLength(field, 2) + VarUInt64Length((ulong)payloadLength) + payloadLength);

    internal static int VarUInt64Length(ulong value)
    {
        var length = 1;
        while (value >= 0x80)
        {
            value >>= 7;
            length++;
        }
        return length;
    }

    internal static string DecodeStrictUtf8(ReadOnlySpan<byte> bytes, string error)
    {
        try { return StrictUtf8.GetString(bytes); }
        catch (DecoderFallbackException ex) { throw new InvalidDataException(error, ex); }
    }

    private static void WriteTag(Stream stream, int field, int wire)
    {
        if (field <= 0) throw new ArgumentOutOfRangeException(nameof(field));
        WriteVarUInt64(stream, checked(((ulong)field << 3) | (uint)wire));
    }

    private static int TagLength(int field, int wire)
        => VarUInt64Length(checked(((ulong)field << 3) | (uint)wire));

    private static void WriteVarUInt64(Stream stream, ulong value)
    {
        while (value >= 0x80)
        {
            stream.WriteByte((byte)((value & 0x7f) | 0x80));
            value >>= 7;
        }
        stream.WriteByte((byte)value);
    }

    internal ref struct Reader
    {
        private ReadOnlySpan<byte> _remaining;

        internal Reader(ReadOnlySpan<byte> encoded) => _remaining = encoded;
        internal bool End => _remaining.IsEmpty;

        internal (int Field, int Wire) ReadTag(string error)
        {
            var tag = ReadVarUInt64(error);
            var field = tag >> 3;
            var wire = (int)(tag & 7);
            if (field == 0 || field > int.MaxValue)
                throw new InvalidDataException(error);
            return ((int)field, wire);
        }

        internal ulong ReadUInt64(int wire, string error)
        {
            if (wire != 0) throw new InvalidDataException(error);
            return ReadVarUInt64(error);
        }

        internal uint ReadUInt32(int wire, string error)
        {
            var value = ReadUInt64(wire, error);
            if (value > uint.MaxValue) throw new InvalidDataException(error);
            return (uint)value;
        }

        internal byte[] ReadBytes(int wire, string error)
        {
            if (wire != 2) throw new InvalidDataException(error);
            var length = ReadVarUInt64(error);
            if (length > int.MaxValue || (ulong)_remaining.Length < length)
                throw new InvalidDataException(error);
            var result = _remaining[..(int)length].ToArray();
            _remaining = _remaining[(int)length..];
            return result;
        }

        internal string ReadString(int wire, string error)
            => DecodeStrictUtf8(ReadBytes(wire, error), error);

        private ulong ReadVarUInt64(string error)
        {
            ulong value = 0;
            for (var shift = 0; shift < 64; shift += 7)
            {
                if (_remaining.IsEmpty) throw new InvalidDataException(error);
                var current = _remaining[0];
                _remaining = _remaining[1..];
                value |= (ulong)(current & 0x7f) << shift;
                if ((current & 0x80) == 0) return value;
            }
            throw new InvalidDataException(error);
        }
    }
}

public static class CoreWorldStateHeaderSnapshotWireCodecV1
{
    public static byte[] Encode(WorldStateHeaderV1 header)
    {
        ArgumentNullException.ThrowIfNull(header);
        using var stream = new MemoryStream();
        CoreSnapshotProtoV1.WriteBytes(stream, 1, header.WorldId.ToBytes());
        CoreSnapshotProtoV1.WriteUInt64(stream, 2, header.Step);
        CoreSnapshotProtoV1.WriteBytes(stream, 3, header.WorldSeedDigest);
        CoreSnapshotProtoV1.WriteUInt64(stream, 4, header.ConfigGeneration);
        CoreSnapshotProtoV1.WriteUInt64(stream, 5, header.MasterGeneration);
        CoreSnapshotProtoV1.WriteUInt32(stream, 6, header.RateGeneration);
        if (header.PreviousStateDigest is not null)
            CoreSnapshotProtoV1.WriteBytes(stream, 7, header.PreviousStateDigest);
        return stream.ToArray();
    }

    public static WorldStateHeaderV1 Decode(ReadOnlySpan<byte> encoded)
    {
        var reader = new CoreSnapshotProtoV1.Reader(encoded);
        byte[]? worldId = null;
        ulong step = 0;
        byte[]? seedDigest = null;
        ulong configGeneration = 0;
        ulong masterGeneration = 0;
        uint rateGeneration = 0;
        byte[]? previous = null;
        var seen = 0u;

        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag("snapshot-core.header.field");
            switch (field)
            {
                case 1:
                    RequireFirst(ref seen, 1, "snapshot-core.header.duplicate-field");
                    worldId = reader.ReadBytes(wire, "snapshot-core.header.world-id");
                    break;
                case 2:
                    RequireFirst(ref seen, 2, "snapshot-core.header.duplicate-field");
                    step = reader.ReadUInt64(wire, "snapshot-core.header.step");
                    break;
                case 3:
                    RequireFirst(ref seen, 3, "snapshot-core.header.duplicate-field");
                    seedDigest = reader.ReadBytes(wire, "snapshot-core.header.world-seed-digest");
                    break;
                case 4:
                    RequireFirst(ref seen, 4, "snapshot-core.header.duplicate-field");
                    configGeneration = reader.ReadUInt64(wire, "snapshot-core.header.config-generation");
                    break;
                case 5:
                    RequireFirst(ref seen, 5, "snapshot-core.header.duplicate-field");
                    masterGeneration = reader.ReadUInt64(wire, "snapshot-core.header.master-generation");
                    break;
                case 6:
                    RequireFirst(ref seen, 6, "snapshot-core.header.duplicate-field");
                    rateGeneration = reader.ReadUInt32(wire, "snapshot-core.header.rate-generation");
                    break;
                case 7:
                    RequireFirst(ref seen, 7, "snapshot-core.header.duplicate-field");
                    previous = reader.ReadBytes(wire, "snapshot-core.header.previous-state-digest");
                    break;
                default:
                    throw new InvalidDataException("snapshot-core.header.unknown-field");
            }
        }

        RequireSeen(seen, 1, 2, 3, 4, 5, 6);
        if (worldId is null || worldId.Length != 16)
            throw new InvalidDataException("snapshot-core.header.world-id");
        if (seedDigest is null || seedDigest.Length != 32)
            throw new InvalidDataException("snapshot-core.header.world-seed-digest");
        if (previous is not null && previous.Length != 32)
            throw new InvalidDataException("snapshot-core.header.previous-state-digest");
        if (configGeneration == 0 || masterGeneration == 0)
            throw new InvalidDataException("snapshot-core.header.generation-zero");
        return new WorldStateHeaderV1(
            OpaqueId128.FromBytes(worldId),
            step,
            seedDigest,
            configGeneration,
            masterGeneration,
            rateGeneration,
            previous);
    }

    public static byte[] ComputeLogicalDigest(WorldStateHeaderV1 header)
    {
        ArgumentNullException.ThrowIfNull(header);
        return HashSuite.DomainHash(CoreSnapshotSectionWireRegistryV1.WorldStateHeaderDigestDomain, writer =>
        {
            writer.WriteMapStart(8);
            writer.WriteUnsigned(0); writer.WriteAsciiText("core.world-state");
            writer.WriteUnsigned(1); writer.WriteBytes(header.WorldId.ToBytes());
            writer.WriteUnsigned(2); writer.WriteUnsigned(header.Step);
            writer.WriteUnsigned(3); writer.WriteBytes(header.WorldSeedDigest);
            writer.WriteUnsigned(4); writer.WriteUnsigned(header.ConfigGeneration);
            writer.WriteUnsigned(5); writer.WriteUnsigned(header.MasterGeneration);
            writer.WriteUnsigned(6); writer.WriteUnsigned(header.RateGeneration);
            writer.WriteUnsigned(7);
            if (header.PreviousStateDigest is null)
            {
                writer.WriteArrayStart(0);
            }
            else
            {
                writer.WriteArrayStart(1);
                writer.WriteBytes(header.PreviousStateDigest);
            }
        });
    }

    private static void RequireFirst(ref uint seen, int field, string error)
    {
        var bit = 1u << (field - 1);
        if ((seen & bit) != 0) throw new InvalidDataException(error);
        seen |= bit;
    }

    private static void RequireSeen(uint seen, params int[] fields)
    {
        foreach (var field in fields)
        {
            if ((seen & (1u << (field - 1))) == 0)
                throw new InvalidDataException("snapshot-core.header.required-field-missing");
        }
    }
}

internal sealed record CoreSchedulerSnapshotFragmentV1(
    ulong WorldStep,
    ulong NextSchedulableStep,
    ulong? FreezeStep,
    IReadOnlyList<ScheduledOperationRefV1> ScheduledOperations);

public static class CoreSchedulerStateSnapshotWireCodecV1
{
    public static byte[] Encode(
        ulong worldStep,
        ulong nextSchedulableStep,
        ulong? freezeStep,
        IReadOnlyList<ScheduledOperationRefV1> scheduledOperations)
    {
        ArgumentNullException.ThrowIfNull(scheduledOperations);
        ValidateCanonical(scheduledOperations);
        using var stream = new MemoryStream();
        CoreSnapshotProtoV1.WriteUInt64(stream, 1, worldStep);
        CoreSnapshotProtoV1.WriteUInt64(stream, 2, nextSchedulableStep);
        if (freezeStep is { } frozen)
            CoreSnapshotProtoV1.WriteUInt64(stream, 3, frozen);
        foreach (var operation in scheduledOperations)
            CoreSnapshotProtoV1.WriteMessage(stream, 4, EncodeOperation(operation));
        return stream.ToArray();
    }

    internal static CoreSchedulerSnapshotFragmentV1 Decode(ReadOnlySpan<byte> encoded)
    {
        var reader = new CoreSnapshotProtoV1.Reader(encoded);
        ulong worldStep = 0;
        ulong next = 0;
        ulong? freeze = null;
        var operations = new List<ScheduledOperationRefV1>();
        var seenWorld = false;
        var seenNext = false;
        var seenFreeze = false;

        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag("snapshot-core.scheduler.field");
            switch (field)
            {
                case 1:
                    if (seenWorld) throw new InvalidDataException("snapshot-core.scheduler.duplicate-field");
                    seenWorld = true;
                    worldStep = reader.ReadUInt64(wire, "snapshot-core.scheduler.world-step");
                    break;
                case 2:
                    if (seenNext) throw new InvalidDataException("snapshot-core.scheduler.duplicate-field");
                    seenNext = true;
                    next = reader.ReadUInt64(wire, "snapshot-core.scheduler.next-step");
                    break;
                case 3:
                    if (seenFreeze) throw new InvalidDataException("snapshot-core.scheduler.duplicate-field");
                    seenFreeze = true;
                    freeze = reader.ReadUInt64(wire, "snapshot-core.scheduler.freeze-step");
                    break;
                case 4:
                    operations.Add(DecodeOperation(reader.ReadBytes(wire, "snapshot-core.scheduler.operation")));
                    break;
                default:
                    throw new InvalidDataException("snapshot-core.scheduler.unknown-field");
            }
        }
        if (!seenWorld || !seenNext)
            throw new InvalidDataException("snapshot-core.scheduler.required-field-missing");
        if (freeze is { } frozen && frozen >= next)
            throw new InvalidDataException("snapshot-core.scheduler.freeze-barrier-invalid");
        ValidateCanonical(operations);
        return new CoreSchedulerSnapshotFragmentV1(
            worldStep,
            next,
            freeze,
            Array.AsReadOnly(operations.ToArray()));
    }

    internal static int EncodedOperationFieldLength(ScheduledOperationRefV1 operation)
    {
        var nested = EncodeOperation(operation);
        return CoreSnapshotProtoV1.LengthDelimitedFieldLength(4, nested.Length);
    }

    internal static void ValidateCanonical(IReadOnlyList<ScheduledOperationRefV1> operations)
    {
        OpaqueId128? previousId = null;
        ScheduledOperationRefV1? previous = null;
        var ids = new HashSet<OpaqueId128>();
        foreach (var operation in operations)
        {
            ArgumentNullException.ThrowIfNull(operation);
            operation.Validate();
            if (!ids.Add(operation.OperationId))
                throw new InvalidDataException("snapshot-core.scheduler.duplicate-operation-id");
            if (previous is not null && Compare(previous, operation) >= 0)
                throw new InvalidDataException("snapshot-core.scheduler.noncanonical-order");
            previous = operation;
            previousId = operation.OperationId;
        }
        _ = previousId;
    }

    private static int Compare(ScheduledOperationRefV1 left, ScheduledOperationRefV1 right)
    {
        var result = left.EffectiveStep.CompareTo(right.EffectiveStep);
        if (result != 0) return result;
        result = left.OrderKey.ToDatabaseBytes().AsSpan().SequenceCompareTo(right.OrderKey.ToDatabaseBytes());
        if (result != 0) return result;
        return left.OperationId.CompareTo(right.OperationId);
    }

    private static byte[] EncodeOperation(ScheduledOperationRefV1 operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        operation.Validate();
        using var stream = new MemoryStream();
        CoreSnapshotProtoV1.WriteBytes(stream, 1, operation.OperationId.ToBytes());
        CoreSnapshotProtoV1.WriteUInt64(stream, 2, operation.EffectiveStep);
        CoreSnapshotProtoV1.WriteBytes(stream, 3, operation.OrderKey.ToDatabaseBytes());
        return stream.ToArray();
    }

    private static ScheduledOperationRefV1 DecodeOperation(ReadOnlySpan<byte> encoded)
    {
        var reader = new CoreSnapshotProtoV1.Reader(encoded);
        byte[]? id = null;
        ulong effectiveStep = 0;
        byte[]? order = null;
        var seen = 0u;
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag("snapshot-core.scheduler.operation-field");
            var bit = field is >= 1 and <= 3 ? 1u << (field - 1) : 0;
            if (bit == 0) throw new InvalidDataException("snapshot-core.scheduler.operation-unknown-field");
            if ((seen & bit) != 0) throw new InvalidDataException("snapshot-core.scheduler.operation-duplicate-field");
            seen |= bit;
            switch (field)
            {
                case 1: id = reader.ReadBytes(wire, "snapshot-core.scheduler.operation-id"); break;
                case 2: effectiveStep = reader.ReadUInt64(wire, "snapshot-core.scheduler.effective-step"); break;
                case 3: order = reader.ReadBytes(wire, "snapshot-core.scheduler.order-key"); break;
            }
        }
        if (seen != 0b111 || id is null || id.Length != 16 || order is null || order.Length != SameStepOrderKey.DatabaseKeyLength)
            throw new InvalidDataException("snapshot-core.scheduler.operation-shape");
        return new ScheduledOperationRefV1(
            OpaqueId128.FromBytes(id),
            effectiveStep,
            SameStepOrderKey.FromDatabaseBytes(order));
    }
}

internal sealed record CoreOperationSnapshotFragmentV1(
    ulong BasisStep,
    IReadOnlyList<DurableOperationStateV1> Operations);

public static class CoreOperationStateSnapshotWireCodecV1
{
    public static byte[] Encode(ulong basisStep, IReadOnlyList<DurableOperationStateV1> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ValidateCanonical(operations);
        using var stream = new MemoryStream();
        CoreSnapshotProtoV1.WriteUInt64(stream, 1, basisStep);
        foreach (var operation in operations)
            CoreSnapshotProtoV1.WriteMessage(stream, 2, EncodeOperation(operation));
        return stream.ToArray();
    }

    internal static CoreOperationSnapshotFragmentV1 Decode(ReadOnlySpan<byte> encoded)
    {
        var reader = new CoreSnapshotProtoV1.Reader(encoded);
        ulong basisStep = 0;
        var seenBasis = false;
        var operations = new List<DurableOperationStateV1>();
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag("snapshot-core.operation.field");
            switch (field)
            {
                case 1:
                    if (seenBasis) throw new InvalidDataException("snapshot-core.operation.duplicate-field");
                    seenBasis = true;
                    basisStep = reader.ReadUInt64(wire, "snapshot-core.operation.basis-step");
                    break;
                case 2:
                    operations.Add(DecodeOperation(reader.ReadBytes(wire, "snapshot-core.operation.item")));
                    break;
                default:
                    throw new InvalidDataException("snapshot-core.operation.unknown-field");
            }
        }
        if (!seenBasis) throw new InvalidDataException("snapshot-core.operation.required-field-missing");
        ValidateCanonical(operations);
        return new CoreOperationSnapshotFragmentV1(basisStep, Array.AsReadOnly(operations.ToArray()));
    }

    internal static int EncodedOperationFieldLength(DurableOperationStateV1 operation)
    {
        var nested = EncodeOperation(operation);
        return CoreSnapshotProtoV1.LengthDelimitedFieldLength(2, nested.Length);
    }

    internal static void ValidateCanonical(IReadOnlyList<DurableOperationStateV1> operations)
    {
        OpaqueId128? previous = null;
        foreach (var operation in operations)
        {
            ArgumentNullException.ThrowIfNull(operation);
            if (previous is { } prior && prior.CompareTo(operation.OperationId) >= 0)
                throw new InvalidDataException("snapshot-core.operation.noncanonical-order");
            previous = operation.OperationId;
        }
        _ = DurableOperationSubstateV1.Canonicalize(operations);
    }

    private static byte[] EncodeOperation(DurableOperationStateV1 operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
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
        if (operation.ResultCode is not null)
            CoreSnapshotProtoV1.WriteString(stream, 9, operation.ResultCode);
        if (operation.RichResultPayload is not null)
            CoreSnapshotProtoV1.WriteBytes(stream, 10, operation.RichResultPayload);
        return stream.ToArray();
    }

    private static DurableOperationStateV1 DecodeOperation(ReadOnlySpan<byte> encoded)
    {
        var reader = new CoreSnapshotProtoV1.Reader(encoded);
        byte[]? id = null;
        byte[]? digest = null;
        DurableOperationLifecycleV1 lifecycle = 0;
        ulong? accepted = null;
        ulong? scheduled = null;
        ulong? effective = null;
        ulong? terminalSequence = null;
        int? terminalStatus = null;
        string? resultCode = null;
        byte[]? rich = null;
        uint seen = 0;

        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag("snapshot-core.operation.item-field");
            if (field is < 1 or > 10)
                throw new InvalidDataException("snapshot-core.operation.item-unknown-field");
            var bit = 1u << (field - 1);
            if ((seen & bit) != 0)
                throw new InvalidDataException("snapshot-core.operation.item-duplicate-field");
            seen |= bit;
            switch (field)
            {
                case 1: id = reader.ReadBytes(wire, "snapshot-core.operation.operation-id"); break;
                case 2: digest = reader.ReadBytes(wire, "snapshot-core.operation.payload-digest"); break;
                case 3:
                    var lifecycleRaw = reader.ReadUInt32(wire, "snapshot-core.operation.lifecycle");
                    if (!Enum.IsDefined(typeof(DurableOperationLifecycleV1), (int)lifecycleRaw))
                        throw new InvalidDataException("snapshot-core.operation.lifecycle");
                    lifecycle = (DurableOperationLifecycleV1)lifecycleRaw;
                    break;
                case 4: accepted = reader.ReadUInt64(wire, "snapshot-core.operation.accepted-sequence"); break;
                case 5: scheduled = reader.ReadUInt64(wire, "snapshot-core.operation.scheduled-sequence"); break;
                case 6: effective = reader.ReadUInt64(wire, "snapshot-core.operation.effective-step"); break;
                case 7: terminalSequence = reader.ReadUInt64(wire, "snapshot-core.operation.terminal-sequence"); break;
                case 8:
                    var status = reader.ReadUInt32(wire, "snapshot-core.operation.terminal-status");
                    if (status > int.MaxValue || !Enum.IsDefined(typeof(CoreOperationResultStatusV1), (int)status))
                        throw new InvalidDataException("snapshot-core.operation.terminal-status");
                    terminalStatus = (int)status;
                    break;
                case 9:
                    resultCode = reader.ReadString(wire, "snapshot-core.operation.result-code");
                    _ = new StableToken(resultCode);
                    break;
                case 10: rich = reader.ReadBytes(wire, "snapshot-core.operation.rich-result"); break;
            }
        }

        const uint required = (1u << 0) | (1u << 1) | (1u << 2);
        if ((seen & required) != required || id is null || id.Length != 16 || digest is null || digest.Length != 32)
            throw new InvalidDataException("snapshot-core.operation.item-shape");
        var result = new DurableOperationStateV1(
            OpaqueId128.FromBytes(id),
            digest,
            lifecycle,
            accepted,
            scheduled,
            effective,
            terminalSequence,
            terminalStatus,
            resultCode,
            rich);
        _ = DurableOperationSubstateV1.Canonicalize([result]);
        return result;
    }

    private static void WriteOptional(Stream stream, int field, ulong? value)
    {
        if (value is { } present) CoreSnapshotProtoV1.WriteUInt64(stream, field, present);
    }
}

public static class CoreSnapshotPrimarySectionProviderV1
{
    public static IReadOnlyList<CanonicalSnapshotSectionMaterialV1> Create(CoreSnapshotOwnerMaterialCutV1 cut)
    {
        ArgumentNullException.ThrowIfNull(cut);
        var sections = new[]
        {
            CreateOperation(cut),
            CreateScheduler(cut),
            CreateWorldStateHeader(cut),
        };
        return Array.AsReadOnly(sections.OrderBy(static section => section.SectionId, StringComparer.Ordinal).ToArray());
    }

    public static CanonicalSnapshotSectionMaterialV1 CreateWorldStateHeader(CoreSnapshotOwnerMaterialCutV1 cut)
    {
        ArgumentNullException.ThrowIfNull(cut);
        var contract = CoreSnapshotSectionWireRegistryV1.Get(CoreSnapshotOwnerSectionRegistryV1.WorldStateHeader);
        var payload = CoreWorldStateHeaderSnapshotWireCodecV1.Encode(cut.Header);
        var fragment = new SnapshotSectionFragmentMaterialV1(
            contract.SectionId,
            0,
            1,
            null,
            null,
            1,
            payload);
        return new CanonicalSnapshotSectionMaterialV1(
            contract.SectionId,
            contract.SectionSchema,
            1,
            CoreWorldStateHeaderSnapshotWireCodecV1.ComputeLogicalDigest(cut.Header),
            Array.AsReadOnly([fragment]));
    }

    public static CanonicalSnapshotSectionMaterialV1 CreateScheduler(CoreSnapshotOwnerMaterialCutV1 cut)
    {
        ArgumentNullException.ThrowIfNull(cut);
        CoreSchedulerStateSnapshotWireCodecV1.ValidateCanonical(cut.ScheduledOperations);
        var groups = FragmentItems(
            cut.ScheduledOperations,
            CoreSchedulerStateSnapshotWireCodecV1.EncodedOperationFieldLength,
            items => CoreSchedulerStateSnapshotWireCodecV1.Encode(cut.BasisStep, cut.BasisStep, null, items));
        var contract = CoreSnapshotSectionWireRegistryV1.Get(CoreSnapshotOwnerSectionRegistryV1.SchedulerState);
        var fragments = BuildFragments(contract.SectionId, groups, items =>
            CoreSchedulerStateSnapshotWireCodecV1.Encode(cut.BasisStep, cut.BasisStep, null, items));
        return new CanonicalSnapshotSectionMaterialV1(
            contract.SectionId,
            contract.SectionSchema,
            checked((ulong)cut.ScheduledOperations.Count),
            cut.RecomputeSchedulerAuthority().CanonicalDigest,
            fragments);
    }

    public static CanonicalSnapshotSectionMaterialV1 CreateOperation(CoreSnapshotOwnerMaterialCutV1 cut)
    {
        ArgumentNullException.ThrowIfNull(cut);
        CoreOperationStateSnapshotWireCodecV1.ValidateCanonical(cut.DurableOperations);
        var groups = FragmentItems(
            cut.DurableOperations,
            CoreOperationStateSnapshotWireCodecV1.EncodedOperationFieldLength,
            items => CoreOperationStateSnapshotWireCodecV1.Encode(cut.BasisStep, items));
        var contract = CoreSnapshotSectionWireRegistryV1.Get(CoreSnapshotOwnerSectionRegistryV1.OperationState);
        var fragments = BuildFragments(contract.SectionId, groups, items =>
            CoreOperationStateSnapshotWireCodecV1.Encode(cut.BasisStep, items));
        return new CanonicalSnapshotSectionMaterialV1(
            contract.SectionId,
            contract.SectionSchema,
            checked((ulong)cut.DurableOperations.Count),
            cut.RecomputeOperationAuthority().CanonicalDigest,
            fragments);
    }

    private static IReadOnlyList<IReadOnlyList<T>> FragmentItems<T>(
        IReadOnlyList<T> items,
        Func<T, int> itemFieldLength,
        Func<IReadOnlyList<T>, byte[]> encode)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            var empty = Array.Empty<T>();
            var encodedEmpty = encode(empty);
            if (encodedEmpty.Length > CanonicalSnapshotSectionValidationV1.HardMaxUncompressedBytes)
                throw new InvalidDataException("persistence.snapshot-item-too-large");
            return Array.AsReadOnly<IReadOnlyList<T>>([empty]);
        }

        var emptyOverhead = encode(Array.Empty<T>()).Length;
        var result = new List<IReadOnlyList<T>>();
        var current = new List<T>();
        var currentLength = emptyOverhead;
        foreach (var item in items)
        {
            var fieldLength = itemFieldLength(item);
            if (checked(emptyOverhead + fieldLength) > CanonicalSnapshotSectionValidationV1.HardMaxUncompressedBytes)
                throw new InvalidDataException("persistence.snapshot-item-too-large");
            if (current.Count > 0 && checked(currentLength + fieldLength) > CanonicalSnapshotSectionValidationV1.TargetUncompressedBytes)
            {
                result.Add(Array.AsReadOnly(current.ToArray()));
                current = [];
                currentLength = emptyOverhead;
            }
            current.Add(item);
            currentLength = checked(currentLength + fieldLength);
            if (currentLength > CanonicalSnapshotSectionValidationV1.HardMaxUncompressedBytes)
                throw new InvalidDataException("persistence.snapshot-item-too-large");
        }
        if (current.Count > 0) result.Add(Array.AsReadOnly(current.ToArray()));
        return Array.AsReadOnly(result.ToArray());
    }

    private static IReadOnlyList<SnapshotSectionFragmentMaterialV1> BuildFragments<T>(
        string sectionId,
        IReadOnlyList<IReadOnlyList<T>> groups,
        Func<IReadOnlyList<T>, byte[]> encode)
    {
        if (groups.Count == 0 || groups.Count > uint.MaxValue)
            throw new InvalidDataException("snapshot-core.fragment-count");
        var result = new SnapshotSectionFragmentMaterialV1[groups.Count];
        for (var i = 0; i < groups.Count; i++)
        {
            var payload = encode(groups[i]);
            if (payload.Length > CanonicalSnapshotSectionValidationV1.HardMaxUncompressedBytes)
                throw new InvalidDataException("persistence.snapshot-item-too-large");
            result[i] = new SnapshotSectionFragmentMaterialV1(
                sectionId,
                checked((uint)i),
                checked((uint)groups.Count),
                null,
                null,
                checked((ulong)groups[i].Count),
                payload);
        }
        return Array.AsReadOnly(result);
    }
}

public static class CoreSnapshotPrimarySemanticVerifierV1
{
    public static SnapshotSectionSemanticVerifierV1 WorldStateHeader(ulong expectedSnapshotStep)
    {
        var contract = CoreSnapshotSectionWireRegistryV1.Get(CoreSnapshotOwnerSectionRegistryV1.WorldStateHeader);
        return new SnapshotSectionSemanticVerifierV1(
            contract.SectionId,
            contract.SectionSchema,
            fragments => VerifyWorldStateHeader(fragments, expectedSnapshotStep));
    }

    public static SnapshotSectionSemanticVerifierV1 Scheduler(ulong expectedSnapshotStep)
    {
        var contract = CoreSnapshotSectionWireRegistryV1.Get(CoreSnapshotOwnerSectionRegistryV1.SchedulerState);
        return new SnapshotSectionSemanticVerifierV1(
            contract.SectionId,
            contract.SectionSchema,
            fragments => VerifyScheduler(fragments, expectedSnapshotStep));
    }

    public static SnapshotSectionSemanticVerifierV1 Operation(ulong expectedSnapshotStep)
    {
        var contract = CoreSnapshotSectionWireRegistryV1.Get(CoreSnapshotOwnerSectionRegistryV1.OperationState);
        return new SnapshotSectionSemanticVerifierV1(
            contract.SectionId,
            contract.SectionSchema,
            fragments => VerifyOperation(fragments, expectedSnapshotStep));
    }

    private static SnapshotSectionSemanticVerificationV1 VerifyWorldStateHeader(
        IReadOnlyList<SnapshotSectionFragmentMaterialV1> fragments,
        ulong expectedSnapshotStep)
    {
        ValidateOuter(fragments, CoreSnapshotOwnerSectionRegistryV1.WorldStateHeader);
        if (fragments.Count != 1 || fragments[0].ItemCount != 1)
            throw new InvalidDataException("snapshot-core.header.fragment-shape");
        var header = CoreWorldStateHeaderSnapshotWireCodecV1.Decode(fragments[0].FragmentPayload);
        if (header.Step != expectedSnapshotStep)
            throw new InvalidDataException("snapshot-core.header.step-mismatch");
        return new SnapshotSectionSemanticVerificationV1(
            1,
            CoreWorldStateHeaderSnapshotWireCodecV1.ComputeLogicalDigest(header));
    }

    private static SnapshotSectionSemanticVerificationV1 VerifyScheduler(
        IReadOnlyList<SnapshotSectionFragmentMaterialV1> fragments,
        ulong expectedSnapshotStep)
    {
        ValidateOuter(fragments, CoreSnapshotOwnerSectionRegistryV1.SchedulerState);
        var operations = new List<ScheduledOperationRefV1>();
        ulong? next = null;
        ulong? freeze = null;
        ulong total = 0;
        foreach (var fragment in fragments)
        {
            var decoded = CoreSchedulerStateSnapshotWireCodecV1.Decode(fragment.FragmentPayload);
            if (decoded.WorldStep != expectedSnapshotStep)
                throw new InvalidDataException("snapshot-core.scheduler.step-mismatch");
            if (next is null)
            {
                next = decoded.NextSchedulableStep;
                freeze = decoded.FreezeStep;
            }
            else if (next.Value != decoded.NextSchedulableStep || freeze != decoded.FreezeStep)
            {
                throw new InvalidDataException("snapshot-core.scheduler.fragment-metadata-mismatch");
            }
            if (fragment.ItemCount != (ulong)decoded.ScheduledOperations.Count)
                throw new InvalidDataException("snapshot-core.scheduler.fragment-item-count");
            total = checked(total + fragment.ItemCount);
            operations.AddRange(decoded.ScheduledOperations);
        }
        CoreSchedulerStateSnapshotWireCodecV1.ValidateCanonical(operations);
        var scheduler = new OperationSchedulerStateV1(
            next ?? throw new InvalidDataException("snapshot-core.scheduler.fragment-missing"),
            freeze,
            operations);
        var authority = OperationSchedulerSubstateV1.Canonicalize(scheduler, expectedSnapshotStep);
        return new SnapshotSectionSemanticVerificationV1(total, authority.CanonicalDigest);
    }

    private static SnapshotSectionSemanticVerificationV1 VerifyOperation(
        IReadOnlyList<SnapshotSectionFragmentMaterialV1> fragments,
        ulong expectedSnapshotStep)
    {
        ValidateOuter(fragments, CoreSnapshotOwnerSectionRegistryV1.OperationState);
        var operations = new List<DurableOperationStateV1>();
        ulong total = 0;
        foreach (var fragment in fragments)
        {
            var decoded = CoreOperationStateSnapshotWireCodecV1.Decode(fragment.FragmentPayload);
            if (decoded.BasisStep != expectedSnapshotStep)
                throw new InvalidDataException("snapshot-core.operation.step-mismatch");
            if (fragment.ItemCount != (ulong)decoded.Operations.Count)
                throw new InvalidDataException("snapshot-core.operation.fragment-item-count");
            total = checked(total + fragment.ItemCount);
            operations.AddRange(decoded.Operations);
        }
        CoreOperationStateSnapshotWireCodecV1.ValidateCanonical(operations);
        var authority = DurableOperationSubstateV1.Canonicalize(operations);
        return new SnapshotSectionSemanticVerificationV1(total, authority.CanonicalDigest);
    }

    private static void ValidateOuter(
        IReadOnlyList<SnapshotSectionFragmentMaterialV1> fragments,
        string sectionId)
    {
        ArgumentNullException.ThrowIfNull(fragments);
        if (fragments.Count == 0 || fragments.Count > uint.MaxValue)
            throw new InvalidDataException("snapshot-core.fragment-count");
        for (var i = 0; i < fragments.Count; i++)
        {
            var fragment = fragments[i] ?? throw new InvalidDataException("snapshot-core.fragment-null");
            if (!string.Equals(fragment.SectionId, sectionId, StringComparison.Ordinal) ||
                fragment.FragmentIndex != (uint)i ||
                fragment.FragmentCount != (uint)fragments.Count)
                throw new InvalidDataException("snapshot-core.fragment-shape");
            if (fragment.FirstRecordId is not null || fragment.LastRecordId is not null)
                throw new InvalidDataException("snapshot-core.fragment-record-range-forbidden");
            if (fragment.FragmentPayload is null ||
                fragment.FragmentPayload.Length > CanonicalSnapshotSectionValidationV1.HardMaxUncompressedBytes)
                throw new InvalidDataException("snapshot-core.fragment-payload");
        }
    }
}
