using System.Text.RegularExpressions;
using Google.Protobuf;
using MachiVerse.Protocol.V1;

namespace MachiVerse.View.Protocol;

public static partial class GatewayEnvelopeCodec
{
    public const int MaxSerializedEnvelopeBytes = 8 * 1024 * 1024;
    public const string ProtocolId = "mv.gateway-view";

    public static byte[] Encode(WireEnvelopeV1 envelope)
    {
        Validate(envelope);
        var bytes = envelope.ToByteArray();
        if (bytes.Length > MaxSerializedEnvelopeBytes)
            throw new InvalidDataException("protocol.limit-exceeded: envelope exceeds 8 MiB.");
        return bytes;
    }

    public static WireEnvelopeV1 Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaxSerializedEnvelopeBytes)
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
        if (!string.Equals(envelope.ProtocolId, ProtocolId, StringComparison.Ordinal)) throw new InvalidDataException("protocol.id-mismatch");
        if (envelope.ProtocolVersion is null || envelope.ProtocolVersion.Major != 1 || envelope.ProtocolVersion.Minor > ushort.MaxValue)
            throw new InvalidDataException("protocol.version-unsupported");
        if (envelope.PayloadSchemaVersion is null || envelope.PayloadSchemaVersion.Major is 0 or > ushort.MaxValue || envelope.PayloadSchemaVersion.Minor > ushort.MaxValue)
            throw new InvalidDataException("protocol.payload-schema-version-out-of-range");
        if (envelope.NegotiationGeneration == 0) throw new InvalidDataException("protocol.negotiation-generation-invalid");
        ValidateStableToken(envelope.ProtocolId, "protocol_id");
        ValidateStableToken(envelope.MessageType, "message_type");
        ValidateStableToken(envelope.PayloadSchemaId, "payload_schema_id");
        ValidateId128(envelope.MessageId, "message_id");
        ValidateId128(envelope.CorrelationId, "correlation_id");
        ValidateId128(envelope.SenderInstanceId, "sender_instance_id");
        if (envelope.HasCausationId) ValidateId128(envelope.CausationId, "causation_id");
    }

    private static void ValidateStableToken(string value, string field)
    {
        if (!StableTokenPattern().IsMatch(value)) throw new InvalidDataException($"protocol.invalid-stable-token: {field}");
    }

    private static void ValidateId128(ByteString value, string field)
    {
        if (value.Length != 16) throw new InvalidDataException($"protocol.invalid-id128-length: {field}");
        var allZero = true;
        foreach (var octet in value.Span)
        {
            if (octet != 0) { allZero = false; break; }
        }
        if (allZero) throw new InvalidDataException($"protocol.zero-id-not-allowed: {field}");
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._/-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex StableTokenPattern();
}
