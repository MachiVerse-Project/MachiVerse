using System.Text.RegularExpressions;
using Google.Protobuf;
using MachiVerse.Protocol.V1;

namespace MachiVerse.Gateway.Protocol;

public static partial class WireEnvelopeValidator
{
    public const int MaxSerializedEnvelopeBytes = 8 * 1024 * 1024;

    public static WireEnvelopeV1 DecodeAndValidate(ReadOnlySpan<byte> serialized, string expectedProtocolId)
    {
        if (serialized.Length > MaxSerializedEnvelopeBytes) throw new InvalidDataException("protocol.limit-exceeded: envelope exceeds 8 MiB.");

        WireEnvelopeV1 envelope;
        try
        {
            envelope = WireEnvelopeV1.Parser.ParseFrom(serialized.ToArray());
        }
        catch (InvalidProtocolBufferException ex)
        {
            throw new InvalidDataException("protocol.structural-decode-failed", ex);
        }

        if (envelope.EnvelopeVersion != 1) throw new InvalidDataException("protocol.envelope-version-unsupported");
        if (!string.Equals(envelope.ProtocolId, expectedProtocolId, StringComparison.Ordinal)) throw new InvalidDataException("protocol.id-mismatch");
        ValidateStableToken(envelope.ProtocolId, nameof(envelope.ProtocolId));
        ValidateStableToken(envelope.MessageType, nameof(envelope.MessageType));
        ValidateStableToken(envelope.PayloadSchemaId, nameof(envelope.PayloadSchemaId));

        if (envelope.ProtocolVersion is null || envelope.ProtocolVersion.Major is 0 or > ushort.MaxValue || envelope.ProtocolVersion.Minor > ushort.MaxValue)
            throw new InvalidDataException("protocol.version-out-of-range");
        if (envelope.PayloadSchemaVersion is null || envelope.PayloadSchemaVersion.Major is 0 or > ushort.MaxValue || envelope.PayloadSchemaVersion.Minor > ushort.MaxValue)
            throw new InvalidDataException("protocol.payload-schema-version-out-of-range");
        if (envelope.NegotiationGeneration == 0) throw new InvalidDataException("protocol.negotiation-generation-invalid");

        ValidateId128(envelope.MessageId, "message_id", allowZero: false);
        ValidateId128(envelope.CorrelationId, "correlation_id", allowZero: false);
        ValidateId128(envelope.SenderInstanceId, "sender_instance_id", allowZero: false);
        if (envelope.HasCausationId) ValidateId128(envelope.CausationId, "causation_id", allowZero: false);

        return envelope;
    }

    public static void ValidateStableToken(string value, string field)
    {
        if (!StableTokenPattern().IsMatch(value)) throw new InvalidDataException($"protocol.invalid-stable-token: {field}");
    }

    public static void ValidateId128(ByteString value, string field, bool allowZero)
    {
        if (value.Length != 16) throw new InvalidDataException($"protocol.invalid-id128-length: {field}");
        if (allowZero) return;

        var allZero = true;
        foreach (var octet in value.Span)
        {
            if (octet == 0) continue;
            allZero = false;
            break;
        }
        if (allZero) throw new InvalidDataException($"protocol.zero-id-not-allowed: {field}");
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._/-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex StableTokenPattern();
}
