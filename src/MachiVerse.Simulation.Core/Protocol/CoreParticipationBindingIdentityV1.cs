using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using MachiVerse.Protocol.V1;

namespace MachiVerse.Simulation.Core.Protocol;

internal static class CoreParticipationBindingIdentityV1
{
    internal const string OperationKind = "participation.binding.create";
    internal const string PayloadSchemaId = "operation.participation.binding.create";
    internal const uint PayloadSchemaMajor = 1;
    internal const uint PayloadSchemaMinor = 0;
    private const string DomainLabel = "mv.operation-payload.v1";

    internal static byte[] ComputeImmutablePayloadDigest(
        OperationSchedulingAdmissionWireV1 admission,
        ParticipationBindingRequestV1 payload)
    {
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(payload);
        if (admission.SchedulingPolicyGeneration == 0)
            throw new InvalidDataException("operation.scheduling-policy-generation-invalid");
        RequireId128(payload.DiverRef, "diver_ref");
        RequireStableToken(payload.PreferenceProfile, "preference_profile");
        ValidateOrderedTokens(payload.PreferenceTokens);

        return DomainHash(DomainLabel, writer =>
        {
            writer.WriteMapStart(5);
            writer.WriteUnsigned(0); writer.WriteAsciiText(OperationKind);
            writer.WriteUnsigned(1); WriteAdmission(writer, admission);
            writer.WriteUnsigned(2); writer.WriteAsciiText(PayloadSchemaId);
            writer.WriteUnsigned(3);
            writer.WriteArrayStart(2);
            writer.WriteUnsigned(PayloadSchemaMajor);
            writer.WriteUnsigned(PayloadSchemaMinor);
            writer.WriteUnsigned(4);
            writer.WriteMapStart(4);
            writer.WriteUnsigned(0); writer.WriteBytes(payload.DiverRef.Span);
            writer.WriteUnsigned(1); writer.WriteUnsigned(payload.ExpectedBindingGeneration);
            writer.WriteUnsigned(2); writer.WriteAsciiText(payload.PreferenceProfile);
            writer.WriteUnsigned(3);
            writer.WriteArrayStart((ulong)payload.PreferenceTokens.Count);
            foreach (var token in payload.PreferenceTokens) writer.WriteAsciiText(token);
        });
    }

    internal static ParticipationBindingRequestV1 ValidateStandardOperation(StandardOperationV1 operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!string.Equals(operation.OperationKind, OperationKind, StringComparison.Ordinal) ||
            !string.Equals(operation.OperationPayloadSchemaId, PayloadSchemaId, StringComparison.Ordinal) ||
            operation.OperationPayloadSchemaVersion is null ||
            operation.OperationPayloadSchemaVersion.Major != PayloadSchemaMajor ||
            operation.OperationPayloadSchemaVersion.Minor != PayloadSchemaMinor)
            throw new InvalidDataException("participation.binding.operation-schema-mismatch");
        if (operation.Admission is null)
            throw new InvalidDataException("operation.scheduling-admission-required");
        RequireId128(operation.OperationId, "operation_id");
        if (operation.ImmutablePayloadDigest.Length != 32)
            throw new InvalidDataException("protocol.invalid-hash:immutable_payload_digest");

        ParticipationBindingRequestV1 payload;
        try
        {
            payload = ParticipationBindingRequestV1.Parser.ParseFrom(operation.OperationPayload);
        }
        catch (InvalidProtocolBufferException ex)
        {
            throw new InvalidDataException("participation.binding.payload-malformed", ex);
        }

        RequireId128(payload.DiverRef, "diver_ref");
        if (!payload.OperationId.Equals(operation.OperationId) ||
            !payload.ImmutablePayloadDigest.Equals(operation.ImmutablePayloadDigest))
            throw new InvalidDataException("participation.binding.inner-identity-mismatch");

        var expected = ComputeImmutablePayloadDigest(operation.Admission, payload);
        if (!CryptographicOperations.FixedTimeEquals(expected, operation.ImmutablePayloadDigest.Span))
            throw new InvalidDataException("operation.payload-digest-mismatch");
        return payload;
    }

    private static void WriteAdmission(CanonicalWriter writer, OperationSchedulingAdmissionWireV1 admission)
    {
        writer.WriteMapStart(4);
        writer.WriteUnsigned(0); writer.WriteUnsigned(admission.AdmissionBasisStep);
        writer.WriteUnsigned(1); writer.WriteUnsigned(admission.SchedulingPolicyGeneration);
        writer.WriteUnsigned(2); WriteOptionalU64(writer, admission.HasRequestedNotBeforeStep, admission.RequestedNotBeforeStep);
        writer.WriteUnsigned(3); WriteOptionalU64(writer, admission.HasRequestedDeadlineStep, admission.RequestedDeadlineStep);
    }

    private static void WriteOptionalU64(CanonicalWriter writer, bool present, ulong value)
    {
        writer.WriteArrayStart(present ? 1UL : 0UL);
        if (present) writer.WriteUnsigned(value);
    }

    private static void ValidateOrderedTokens(IEnumerable<string> tokens)
    {
        string? previous = null;
        foreach (var token in tokens)
        {
            RequireStableToken(token, "preference_token");
            if (previous is not null && string.CompareOrdinal(previous, token) >= 0)
                throw new InvalidDataException("participation.preference-tokens-not-canonical");
            previous = token;
        }
    }

    private static void RequireStableToken(string value, string field)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 64 || !IsLowerAlphaNumeric(value[0]))
            throw new InvalidDataException($"protocol.field-out-of-range:{field}");
        for (var i = 1; i < value.Length; i++)
        {
            var ch = value[i];
            if (!IsLowerAlphaNumeric(ch) && ch is not ('.' or '_' or '/' or '-'))
                throw new InvalidDataException($"protocol.field-out-of-range:{field}");
        }
    }

    private static bool IsLowerAlphaNumeric(char value)
        => value is >= 'a' and <= 'z' or >= '0' and <= '9';

    private static void RequireId128(ByteString value, string field)
    {
        if (value.Length != 16 || value.Span.IndexOfAnyExcept((byte)0) < 0)
            throw new InvalidDataException($"protocol.invalid-id:{field}");
    }

    private static byte[] DomainHash(string label, Action<CanonicalWriter> write)
    {
        var labelBytes = Encoding.ASCII.GetBytes(label);
        var writer = new CanonicalWriter();
        write(writer);
        var value = writer.ToArray();
        var preimage = new byte[labelBytes.Length + 1 + value.Length];
        labelBytes.CopyTo(preimage, 0);
        value.CopyTo(preimage, labelBytes.Length + 1);
        return SHA256.HashData(preimage);
    }

    private sealed class CanonicalWriter
    {
        private readonly ArrayBufferWriter<byte> _buffer = new();

        internal void WriteUnsigned(ulong value) => WriteInitial(0, value);

        internal void WriteBytes(ReadOnlySpan<byte> value)
        {
            WriteInitial(2, (ulong)value.Length);
            WriteRaw(value);
        }

        internal void WriteAsciiText(string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            WriteInitial(3, (ulong)bytes.Length);
            WriteRaw(bytes);
        }

        internal void WriteArrayStart(ulong count) => WriteInitial(4, count);
        internal void WriteMapStart(ulong count) => WriteInitial(5, count);
        internal byte[] ToArray() => _buffer.WrittenSpan.ToArray();

        private void WriteInitial(byte major, ulong value)
        {
            if (value < 24) { WriteByte((byte)((major << 5) | (byte)value)); return; }
            if (value <= byte.MaxValue) { WriteByte((byte)((major << 5) | 24)); WriteByte((byte)value); return; }
            if (value <= ushort.MaxValue)
            {
                WriteByte((byte)((major << 5) | 25));
                var span = _buffer.GetSpan(2);
                BinaryPrimitives.WriteUInt16BigEndian(span, (ushort)value);
                _buffer.Advance(2);
                return;
            }
            if (value <= uint.MaxValue)
            {
                WriteByte((byte)((major << 5) | 26));
                var span = _buffer.GetSpan(4);
                BinaryPrimitives.WriteUInt32BigEndian(span, (uint)value);
                _buffer.Advance(4);
                return;
            }
            WriteByte((byte)((major << 5) | 27));
            var destination = _buffer.GetSpan(8);
            BinaryPrimitives.WriteUInt64BigEndian(destination, value);
            _buffer.Advance(8);
        }

        private void WriteByte(byte value)
        {
            var span = _buffer.GetSpan(1);
            span[0] = value;
            _buffer.Advance(1);
        }

        private void WriteRaw(ReadOnlySpan<byte> value)
        {
            var span = _buffer.GetSpan(value.Length);
            value.CopyTo(span);
            _buffer.Advance(value.Length);
        }
    }
}
