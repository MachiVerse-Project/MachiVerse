using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;

internal static class CanonicalDomainWireNegativeInitializer
{
    private const string PartitionId = ResidentIdentityLifecyclePayloadV1.PartitionId;

    [ModuleInitializer]
    internal static void Initialize()
    {
        var materialized = Qa04ReferenceWorldMaterializerV1.MaterializeResidentIdentityLifecycle(1);
        var record = materialized.Partition.RecordsCanonical.Single();
        var authority = new DomainPartitionSnapshotAuthorityV1<ResidentIdentityLifecyclePayloadV1>(
            materialized.Partition,
            materialized.PartitionHeader,
            static payload => payload.CanonicalDigest());

        var headerWire = DomainPartitionSnapshotWireCodecV1.EncodeHeader(materialized.PartitionHeader);
        ExpectInvalid(
            "partition header unknown protobuf field",
            () => _ = DomainPartitionSnapshotWireCodecV1.DecodeHeader(
                headerWire.Concat(new byte[] { 0x78, 0x01 }).ToArray()),
            "persistence.snapshot.domain-wire:partition-header-unknown-field");

        var payloadWire = DomainPartitionSnapshotWireCodecV1.EncodePayload(
            PartitionId,
            record.Payload.ToStandardPayload());
        ExpectInvalid(
            "payload unknown protobuf field",
            () => _ = DomainPartitionSnapshotWireCodecV1.DecodePayload(
                PartitionId,
                payloadWire.Concat(new byte[] { 0x10, 0x01 }).ToArray()),
            "persistence.snapshot.domain-wire:payload-unknown-field:");

        var firstEntry = FirstLengthDelimitedField(payloadWire, expectedTag: 0x0A);
        ExpectInvalid(
            "payload descriptor ordinal ordering",
            () => _ = DomainPartitionSnapshotWireCodecV1.DecodePayload(
                PartitionId,
                payloadWire.Concat(firstEntry).ToArray()),
            "persistence.snapshot.domain-wire:payload-field-order:");

        var wrongArm = MutatePayloadValueArm(
            payloadWire,
            targetFieldNumber: 6,
            expectedArmTag: 0x30,
            replacementArmTag: 0x38);
        ExpectInvalid(
            "payload wrong oneof/value arm",
            () => _ = DomainPartitionSnapshotWireCodecV1.DecodePayload(PartitionId, wrongArm),
            "persistence.snapshot.domain-wire:payload-value-kind-mismatch:");

        var recordWire = DomainPartitionSnapshotWireCodecV1.EncodeRecord(
            PartitionId,
            record,
            static payload => payload.ToStandardPayload());
        ExpectInvalid(
            "domain record unknown protobuf field",
            () => _ = DomainPartitionSnapshotWireCodecV1.DecodeRecord(
                PartitionId,
                recordWire.Concat(new byte[] { 0x50, 0x01 }).ToArray(),
                materialized.PartitionHeader.BasisStep),
            "persistence.snapshot.domain-wire:record-unknown-field:");

        var fragmentWire = DomainPartitionSnapshotWireCodecV1.EncodeFragment(
            authority,
            new[] { record },
            static payload => payload.ToStandardPayload());
        ExpectInvalid(
            "domain fragment unknown protobuf field",
            () => _ = DomainPartitionSnapshotWireCodecV1.DecodeFragment(
                PartitionId,
                fragmentWire.Concat(new byte[] { 0x18, 0x01 }).ToArray()),
            "persistence.snapshot.domain-wire:fragment-unknown-field:");
        ExpectInvalid(
            "domain fragment duplicate complete header",
            () => _ = DomainPartitionSnapshotWireCodecV1.DecodeFragment(
                PartitionId,
                fragmentWire.Concat(fragmentWire).ToArray()),
            "persistence.snapshot.domain-wire:fragment-header-duplicate:");
    }

    private static byte[] FirstLengthDelimitedField(byte[] encoded, ulong expectedTag)
    {
        var offset = 0;
        var tag = ReadVarUInt(encoded, ref offset);
        if (tag != expectedTag)
            throw new InvalidOperationException($"Unexpected protobuf tag {tag}; expected {expectedTag}.");
        var length = ReadVarUInt(encoded, ref offset);
        if (length > int.MaxValue || checked(offset + (int)length) > encoded.Length)
            throw new InvalidOperationException("Truncated length-delimited field fixture.");
        return encoded[..checked(offset + (int)length)];
    }

    private static byte[] MutatePayloadValueArm(
        byte[] payloadWire,
        uint targetFieldNumber,
        byte expectedArmTag,
        byte replacementArmTag)
    {
        var offset = 0;
        while (offset < payloadWire.Length)
        {
            var outerTag = ReadVarUInt(payloadWire, ref offset);
            if (outerTag != 0x0A)
                throw new InvalidOperationException("Payload fixture outer tag drifted.");
            var entryLength = ReadVarUInt(payloadWire, ref offset);
            if (entryLength > int.MaxValue)
                throw new InvalidOperationException("Payload fixture entry is too large.");
            var entryEnd = checked(offset + (int)entryLength);
            if (entryEnd > payloadWire.Length)
                throw new InvalidOperationException("Payload fixture entry is truncated.");

            var entryOffset = offset;
            if (ReadVarUInt(payloadWire, ref entryOffset) != 0x08)
                throw new InvalidOperationException("Payload fixture field-number tag drifted.");
            var fieldNumber = ReadVarUInt(payloadWire, ref entryOffset);
            if (ReadVarUInt(payloadWire, ref entryOffset) != 0x12)
                throw new InvalidOperationException("Payload fixture value tag drifted.");
            var valueLength = ReadVarUInt(payloadWire, ref entryOffset);
            if (valueLength == 0 || valueLength > int.MaxValue || checked(entryOffset + (int)valueLength) > entryEnd)
                throw new InvalidOperationException("Payload fixture value is truncated.");

            if (fieldNumber == targetFieldNumber)
            {
                var mutated = payloadWire.ToArray();
                if (mutated[entryOffset] != expectedArmTag)
                    throw new InvalidOperationException(
                        $"Payload fixture arm drifted: expected 0x{expectedArmTag:x2}, found 0x{mutated[entryOffset]:x2}.");
                mutated[entryOffset] = replacementArmTag;
                return mutated;
            }

            offset = entryEnd;
        }

        throw new InvalidOperationException($"Payload fixture field ordinal {targetFieldNumber} is missing.");
    }

    private static ulong ReadVarUInt(byte[] bytes, ref int offset)
    {
        ulong value = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            if (offset >= bytes.Length)
                throw new InvalidOperationException("Truncated protobuf varint fixture.");
            var current = bytes[offset++];
            value |= (ulong)(current & 0x7f) << shift;
            if ((current & 0x80) == 0)
                return value;
        }
        throw new InvalidOperationException("Overflowing protobuf varint fixture.");
    }

    private static void ExpectInvalid(string name, Action action, string expectedPrefix)
    {
        try
        {
            action();
        }
        catch (InvalidDataException ex) when (ex.Message.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            return;
        }

        throw new InvalidOperationException($"Expected InvalidDataException for {name} with prefix {expectedPrefix}.");
    }
}
