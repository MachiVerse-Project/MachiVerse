using System.Text.RegularExpressions;
using Google.Protobuf;
using MachiVerse.Protocol.V1;

namespace MachiVerse.Administration.View.Protocol;

public static partial class AdminGatewayBootstrapEnvelopeCodec
{
    public static byte[] Encode(WireEnvelopeV1 envelope)
    {
        Validate(envelope);
        var bytes = envelope.ToByteArray();
        if (bytes.Length > AdminGatewayEnvelopeCodec.MaxSerializedEnvelopeBytes)
            throw new InvalidDataException("protocol.limit-exceeded: envelope exceeds 8 MiB.");
        return bytes;
    }

    public static WireEnvelopeV1 Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > AdminGatewayEnvelopeCodec.MaxSerializedEnvelopeBytes)
            throw new InvalidDataException("protocol.limit-exceeded: envelope exceeds 8 MiB.");
        WireEnvelopeV1 envelope;
        try
        {
            envelope = WireEnvelopeV1.Parser.ParseFrom(bytes.ToArray());
        }
        catch (InvalidProtocolBufferException ex)
        {
            throw new InvalidDataException("protocol.structural-decode-failed", ex);
        }
        Validate(envelope);
        return envelope;
    }

    private static void Validate(WireEnvelopeV1 envelope)
    {
        if (envelope.EnvelopeVersion != 1) throw new InvalidDataException("protocol.envelope-version-unsupported");
        if (!string.Equals(envelope.ProtocolId, AdminGatewayEnvelopeCodec.ProtocolId, StringComparison.Ordinal))
            throw new InvalidDataException("protocol.id-mismatch");
        if (envelope.ProtocolVersion is null || envelope.ProtocolVersion.Major != 0 || envelope.ProtocolVersion.Minor != 0)
            throw new InvalidDataException("protocol.version-unsupported");
        if (envelope.NegotiationGeneration != 0)
            throw new InvalidDataException("protocol.negotiation-generation-invalid");
        if (envelope.MessageType is not ("protocol.hello" or "protocol.accept" or "protocol.reject"))
            throw new InvalidDataException("protocol.unknown-message-type");
        if (envelope.PayloadSchemaVersion is null || envelope.PayloadSchemaVersion.Major != 1 || envelope.PayloadSchemaVersion.Minor != 0)
            throw new InvalidDataException("protocol.payload-schema-version-out-of-range");
        if ((int)envelope.PayloadCompression != 1)
            throw new InvalidDataException("protocol.capability-missing");
        ValidateToken(envelope.MessageType, "message_type");
        ValidateToken(envelope.PayloadSchemaId, "payload_schema_id");
        ValidateId128(envelope.MessageId, "message_id");
        ValidateId128(envelope.CorrelationId, "correlation_id");
        ValidateId128(envelope.SenderInstanceId, "sender_instance_id");
        if (envelope.HasCausationId) ValidateId128(envelope.CausationId, "causation_id");
    }

    private static void ValidateToken(string value, string field)
    {
        if (!StableTokenPattern().IsMatch(value))
            throw new InvalidDataException($"protocol.invalid-stable-token:{field}");
    }

    private static void ValidateId128(ByteString value, string field)
    {
        if (value.Length != 16 || value.Span.IndexOfAnyExcept((byte)0) < 0)
            throw new InvalidDataException($"protocol.invalid-id128:{field}");
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._/-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex StableTokenPattern();
}
