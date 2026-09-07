using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using MachiVerse.Protocol.V1;

namespace MachiVerse.Protocol.Canonical;

/// <summary>
/// Canonical immutable Operation payload identity for Standard Protocol v1
/// participation.binding.create. Candidate scheduling is intentionally excluded.
/// The payload's duplicated operation_id/digest identity fields are also excluded
/// from the semantic preimage and must instead match the outer StandardOperationV1.
/// </summary>
public static class ParticipationBindingIdentityV1
{
    public const string OperationKind = "participation.binding.create";
    public const string PayloadSchemaId = "operation.participation.binding.create";
    public const uint PayloadSchemaMajor = 1;
    public const uint PayloadSchemaMinor = 0;
    public const string DomainLabel = "mv.operation-payload.v1";

    public static byte[] ComputeImmutablePayloadDigest(
        OperationSchedulingAdmissionWireV1 admission,
        ParticipationBindingRequestV1 payload)
    {
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(payload);
        if (admission.SchedulingPolicyGeneration == 0)
            throw new InvalidDataException("operation.scheduling-policy-generation-invalid");
        RequireAsciiToken(payload.PreferenceProfile, "preference_profile");
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
            writer.WriteMapStart(2);
            writer.WriteUnsigned(0); writer.WriteAsciiText(payload.PreferenceProfile);
            writer.WriteUnsigned(1);
            writer.WriteArrayStart((ulong)payload.PreferenceTokens.Count);
            foreach (var token in payload.PreferenceTokens) writer.WriteAsciiText(token);
        });
    }

    public static void ValidateStandardOperation(StandardOperationV1 operation)
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
        if (operation.OperationId.Length != 16 || IsZero(operation.OperationId.Span))
            throw new InvalidDataException("protocol.invalid-id:operation_id");
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
        if (!payload.OperationId.Equals(operation.OperationId) ||
            !payload.ImmutablePayloadDigest.Equals(operation.ImmutablePayloadDigest))
            throw new InvalidDataException("participation.binding.inner-identity-mismatch");

        var expected = ComputeImmutablePayloadDigest(operation.Admission, payload);
        if (!CryptographicOperations.FixedTimeEquals(expected, operation.ImmutablePayloadDigest.Span))
            throw new InvalidDataException("operation.payload-digest-mismatch");
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
            RequireAsciiToken(token, "preference_token");
            if (previous is not null && string.CompareOrdinal(previous, token) >= 0)
                throw new InvalidDataException("participation.preference-tokens-not-canonical");
            previous = token;
        }
    }

    private static void RequireAsciiToken(string value, string field)
    {
        if (string.IsNullOrEmpty(value) || value.Any(static ch => ch > 0x7f || char.IsControl(ch) || char.IsWhiteSpace(ch)))
            throw new InvalidDataException($"{field} must be a non-empty canonical ASCII token.");
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

    private static bool IsZero(ReadOnlySpan<byte> value)
    {
        var aggregate = 0;
        foreach (var item in value) aggregate |= item;
        return aggregate == 0;
    }

    private sealed class CanonicalWriter
    {
        private readonly ArrayBufferWriter<byte> _buffer = new();
        public void WriteUnsigned(ulong value) => WriteInitial(0, value);
        public void WriteAsciiText(string value)
        {
            RequireAsciiToken(value, "MV-DCBOR text");
            var bytes = Encoding.ASCII.GetBytes(value);
            WriteInitial(3, (ulong)bytes.Length);
            WriteRaw(bytes);
        }
        public void WriteArrayStart(ulong count) => WriteInitial(4, count);
        public void WriteMapStart(ulong count) => WriteInitial(5, count);
        public byte[] ToArray() => _buffer.WrittenSpan.ToArray();

        private void WriteInitial(byte major, ulong value)
        {
            if (value < 24) { WriteByte((byte)((major << 5) | (byte)value)); return; }
            if (value <= byte.MaxValue) { WriteByte((byte)((major << 5) | 24)); WriteByte((byte)value); return; }
            if (value <= ushort.MaxValue)
            {
                WriteByte((byte)((major << 5) | 25));
                var span = _buffer.GetSpan(2); BinaryPrimitives.WriteUInt16BigEndian(span, (ushort)value); _buffer.Advance(2); return;
            }
            if (value <= uint.MaxValue)
            {
                WriteByte((byte)((major << 5) | 26));
                var span = _buffer.GetSpan(4); BinaryPrimitives.WriteUInt32BigEndian(span, (uint)value); _buffer.Advance(4); return;
            }
            WriteByte((byte)((major << 5) | 27));
            var destination = _buffer.GetSpan(8); BinaryPrimitives.WriteUInt64BigEndian(destination, value); _buffer.Advance(8);
        }
        private void WriteByte(byte value) { var span = _buffer.GetSpan(1); span[0] = value; _buffer.Advance(1); }
        private void WriteRaw(ReadOnlySpan<byte> value) { var span = _buffer.GetSpan(value.Length); value.CopyTo(span); _buffer.Advance(value.Length); }
    }
}
