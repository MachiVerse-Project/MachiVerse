using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using MachiVerse.Protocol.V1;

namespace MachiVerse.Protocol.Canonical;

/// <summary>
/// Canonical identity codec for Standard Protocol v1 Config changes.
/// The digest deliberately excludes operation_id and immutable_payload_digest themselves.
/// Both browser and Gateway compile this exact source so identity cannot drift across the boundary.
/// </summary>
public static class ConfigChangeIdentityV1
{
    public const string DomainLabel = "mv.config-change.v1";

    public static byte[] ComputeImmutablePayloadDigest(ConfigChangeRequestV1 request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Target is null) throw new InvalidDataException("config.change.target-required");
        if (request.ExpectedBaseGeneration == 0) throw new InvalidDataException("config.change.base-generation-zero");
        if (request.Changes.Count == 0) throw new InvalidDataException("config.change.empty");

        var changes = request.Changes
            .OrderBy(static item => item.Key, StringComparer.Ordinal)
            .ToArray();
        for (var i = 0; i < changes.Length; i++)
        {
            RequireAsciiToken(changes[i].Key, "config key");
            if (i > 0 && string.Equals(changes[i - 1].Key, changes[i].Key, StringComparison.Ordinal))
                throw new InvalidDataException("config.change.duplicate-key");
            if (changes[i].Value is null) throw new InvalidDataException("config.change.value-required");
        }

        return DomainHash(DomainLabel, writer =>
        {
            writer.WriteMapStart(4);
            writer.WriteUnsigned(0); WriteTarget(writer, request.Target);
            writer.WriteUnsigned(1); writer.WriteUnsigned(request.ExpectedBaseGeneration);
            writer.WriteUnsigned(2);
            writer.WriteArrayStart((ulong)changes.Length);
            foreach (var change in changes) WriteChange(writer, change);
            writer.WriteUnsigned(3);
            if (request.HasRequestedEffectiveStep)
            {
                writer.WriteArrayStart(1);
                writer.WriteUnsigned(request.RequestedEffectiveStep);
            }
            else
            {
                writer.WriteArrayStart(0);
            }
        });
    }

    private static void WriteTarget(CanonicalWriter writer, ComponentTargetV1 target)
    {
        writer.WriteMapStart(2);
        writer.WriteUnsigned(0); writer.WriteUnsigned((ulong)(int)target.ComponentKind);
        writer.WriteUnsigned(1);
        if (target.HasLogicalInstanceId)
        {
            if (target.LogicalInstanceId.Length != 16) throw new InvalidDataException("config.change.target-id-length");
            writer.WriteArrayStart(1);
            writer.WriteBytes(target.LogicalInstanceId.Span);
        }
        else
        {
            writer.WriteArrayStart(0);
        }
    }

    private static void WriteChange(CanonicalWriter writer, ConfigChangeEntryV1 change)
    {
        writer.WriteMapStart(2);
        writer.WriteUnsigned(0); writer.WriteAsciiText(change.Key);
        writer.WriteUnsigned(1); WriteValue(writer, change.Value);
    }

    private static void WriteValue(CanonicalWriter writer, ConfigValueWireV1 value)
    {
        var valueCase = (int)value.ValueCase;
        if (valueCase is < 1 or > 6) throw new InvalidDataException("config.change.value-kind-required");

        writer.WriteMapStart(2);
        writer.WriteUnsigned(0); writer.WriteUnsigned((ulong)valueCase);
        writer.WriteUnsigned(1);
        switch (valueCase)
        {
            case 1:
                writer.WriteBoolean(value.BoolValue);
                break;
            case 2:
                writer.WriteInt64(value.IntValue);
                break;
            case 3:
                writer.WriteUnsigned(value.UintValue);
                break;
            case 4:
            {
                if (!double.IsFinite(value.DoubleValue)) throw new InvalidDataException("config.change.double-not-finite");
                Span<byte> bytes = stackalloc byte[8];
                BinaryPrimitives.WriteInt64BigEndian(bytes, BitConverter.DoubleToInt64Bits(value.DoubleValue));
                writer.WriteBytes(bytes);
                break;
            }
            case 5:
                writer.WriteBytes(Encoding.UTF8.GetBytes(value.StringValue));
                break;
            case 6:
                writer.WriteBytes(value.BytesValue.Span);
                break;
        }
    }

    private static byte[] DomainHash(string label, Action<CanonicalWriter> writeValue)
    {
        var labelBytes = Encoding.ASCII.GetBytes(label);
        var writer = new CanonicalWriter();
        writeValue(writer);
        var valueBytes = writer.ToArray();
        var preimage = new byte[labelBytes.Length + 1 + valueBytes.Length];
        labelBytes.CopyTo(preimage, 0);
        valueBytes.CopyTo(preimage, labelBytes.Length + 1);
        return SHA256.HashData(preimage);
    }

    private static void RequireAsciiToken(string value, string field)
    {
        if (string.IsNullOrEmpty(value) || value.Any(static c => c > 0x7f || char.IsControl(c)))
            throw new InvalidDataException($"{field} must be a non-empty ASCII token.");
    }

    private sealed class CanonicalWriter
    {
        private readonly ArrayBufferWriter<byte> _buffer = new();

        public void WriteUnsigned(ulong value) => WriteInitialValue(0, value);
        public void WriteInt64(long value)
        {
            if (value >= 0) WriteInitialValue(0, (ulong)value);
            else WriteInitialValue(1, checked((ulong)(-1 - value)));
        }
        public void WriteBytes(ReadOnlySpan<byte> value)
        {
            WriteInitialValue(2, (ulong)value.Length);
            WriteRaw(value);
        }
        public void WriteAsciiText(string value)
        {
            RequireAsciiToken(value, "MV-DCBOR text");
            var bytes = Encoding.ASCII.GetBytes(value);
            WriteInitialValue(3, (ulong)bytes.Length);
            WriteRaw(bytes);
        }
        public void WriteArrayStart(ulong count) => WriteInitialValue(4, count);
        public void WriteMapStart(ulong count) => WriteInitialValue(5, count);
        public void WriteBoolean(bool value) => WriteByte(value ? (byte)0xf5 : (byte)0xf4);
        public byte[] ToArray() => _buffer.WrittenSpan.ToArray();

        private void WriteInitialValue(byte majorType, ulong value)
        {
            if (value < 24) { WriteByte((byte)((majorType << 5) | (byte)value)); return; }
            if (value <= byte.MaxValue) { WriteByte((byte)((majorType << 5) | 24)); WriteByte((byte)value); return; }
            if (value <= ushort.MaxValue)
            {
                WriteByte((byte)((majorType << 5) | 25));
                var span = _buffer.GetSpan(2);
                BinaryPrimitives.WriteUInt16BigEndian(span, (ushort)value);
                _buffer.Advance(2);
                return;
            }
            if (value <= uint.MaxValue)
            {
                WriteByte((byte)((majorType << 5) | 26));
                var span = _buffer.GetSpan(4);
                BinaryPrimitives.WriteUInt32BigEndian(span, (uint)value);
                _buffer.Advance(4);
                return;
            }
            WriteByte((byte)((majorType << 5) | 27));
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
