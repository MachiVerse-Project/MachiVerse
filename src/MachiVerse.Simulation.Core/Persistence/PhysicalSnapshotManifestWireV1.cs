using System.Security.Cryptography;
using System.Text;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed record PhysicalSnapshotManifestMaterialV1(
    LogicalSnapshotManifest Logical,
    IReadOnlyList<PhysicalSnapshotChunkDescriptor> Chunks,
    byte[] PhysicalManifestDigest);

/// <summary>
/// Exact PhysicalSnapshotManifestV1 wire authority. The physical manifest digest is SHA-256 of
/// this codec's deterministic protobuf serialization with field 3 omitted. The embedded logical
/// manifest remains governed by its MV-DCBOR semantic SnapshotDigest.
/// </summary>
public static class PhysicalSnapshotManifestWireCodecV1
{
    public static PhysicalSnapshotManifestMaterialV1 WithComputedPhysicalDigest(
        LogicalSnapshotManifest logical,
        IReadOnlyList<PhysicalSnapshotChunkDescriptor> chunks,
        RequiredAddonSnapshotMetadataCodecRegistryV1? addonCodecs = null)
    {
        ArgumentNullException.ThrowIfNull(logical);
        ArgumentNullException.ThrowIfNull(chunks);
        Validate(logical, chunks, addonCodecs);
        var digest = ComputePhysicalManifestDigest(logical, chunks, addonCodecs);
        return new PhysicalSnapshotManifestMaterialV1(logical, CloneChunks(chunks), digest);
    }

    public static byte[] ComputePhysicalManifestDigest(
        LogicalSnapshotManifest logical,
        IReadOnlyList<PhysicalSnapshotChunkDescriptor> chunks,
        RequiredAddonSnapshotMetadataCodecRegistryV1? addonCodecs = null)
    {
        Validate(logical, chunks, addonCodecs);
        return SHA256.HashData(EncodeWithoutPhysicalDigest(logical, chunks, addonCodecs));
    }

    public static byte[] Encode(
        PhysicalSnapshotManifestMaterialV1 manifest,
        RequiredAddonSnapshotMetadataCodecRegistryV1? addonCodecs = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.PhysicalManifestDigest is null || manifest.PhysicalManifestDigest.Length != 32)
            throw new InvalidDataException("persistence.snapshot.physical-manifest-digest-length");

        Validate(manifest.Logical, manifest.Chunks, addonCodecs);
        var expected = ComputePhysicalManifestDigest(manifest.Logical, manifest.Chunks, addonCodecs);
        if (!CryptographicOperations.FixedTimeEquals(expected, manifest.PhysicalManifestDigest))
            throw new InvalidDataException("persistence.snapshot.physical-manifest-digest-mismatch");

        return Proto.Encode(stream =>
        {
            Proto.WriteMessage(stream, 1, LogicalSnapshotManifestWireCodecV1.Encode(manifest.Logical, addonCodecs));
            foreach (var chunk in manifest.Chunks)
                Proto.WriteMessage(stream, 2, EncodeChunk(chunk));
            Proto.WriteBytes(stream, 3, manifest.PhysicalManifestDigest);
        });
    }

    public static PhysicalSnapshotManifestMaterialV1 Decode(
        ReadOnlySpan<byte> encoded,
        RequiredAddonSnapshotMetadataCodecRegistryV1? addonCodecs = null)
    {
        if (encoded.IsEmpty) throw new InvalidDataException("persistence.snapshot.physical-manifest-empty");
        var reader = new Proto.Reader(encoded);
        byte[]? logicalBytes = null;
        var chunks = new List<PhysicalSnapshotChunkDescriptor>();
        byte[]? physicalDigest = null;
        var singular = new HashSet<int>();

        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (field != 2 && !singular.Add(field))
                throw new InvalidDataException("persistence.snapshot.physical-manifest-duplicate-field");
            switch (field)
            {
                case 1:
                    reader.RequireWire(wire, 2);
                    logicalBytes = reader.ReadBytes();
                    break;
                case 2:
                    reader.RequireWire(wire, 2);
                    chunks.Add(DecodeChunk(reader.ReadBytes()));
                    break;
                case 3:
                    reader.RequireWire(wire, 2);
                    physicalDigest = reader.ReadBytes();
                    break;
                default:
                    throw new InvalidDataException("persistence.snapshot.physical-manifest-unknown-field");
            }
        }

        if (logicalBytes is null || physicalDigest is null)
            throw new InvalidDataException("persistence.snapshot.physical-manifest-required-field");
        if (physicalDigest.Length != 32)
            throw new InvalidDataException("persistence.snapshot.physical-manifest-digest-length");

        var logical = LogicalSnapshotManifestWireCodecV1.Decode(logicalBytes, addonCodecs);
        var materializedChunks = Array.AsReadOnly(chunks.ToArray());
        Validate(logical, materializedChunks, addonCodecs);
        var expected = ComputePhysicalManifestDigest(logical, materializedChunks, addonCodecs);
        if (!CryptographicOperations.FixedTimeEquals(expected, physicalDigest))
            throw new InvalidDataException("persistence.snapshot.physical-manifest-digest-mismatch");

        return new PhysicalSnapshotManifestMaterialV1(
            logical,
            CloneChunks(materializedChunks),
            physicalDigest.ToArray());
    }

    private static byte[] EncodeWithoutPhysicalDigest(
        LogicalSnapshotManifest logical,
        IReadOnlyList<PhysicalSnapshotChunkDescriptor> chunks,
        RequiredAddonSnapshotMetadataCodecRegistryV1? addonCodecs)
        => Proto.Encode(stream =>
        {
            Proto.WriteMessage(stream, 1, LogicalSnapshotManifestWireCodecV1.Encode(logical, addonCodecs));
            foreach (var chunk in chunks)
                Proto.WriteMessage(stream, 2, EncodeChunk(chunk));
        });

    private static void Validate(
        LogicalSnapshotManifest logical,
        IReadOnlyList<PhysicalSnapshotChunkDescriptor> chunks,
        RequiredAddonSnapshotMetadataCodecRegistryV1? addonCodecs)
    {
        ArgumentNullException.ThrowIfNull(logical);
        ArgumentNullException.ThrowIfNull(chunks);

        // Encoding the logical manifest proves its SnapshotDigest and required-addon metadata are
        // semantically valid before physical integrity can be established.
        _ = LogicalSnapshotManifestWireCodecV1.Encode(logical, addonCodecs);
        SnapshotManifestValidation.ValidatePhysicalMapping(logical, chunks);

        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i] ?? throw new InvalidDataException("persistence.snapshot.chunk-descriptor-null");
            if (chunk.ChunkIndex != checked((uint)i))
                throw new InvalidDataException("persistence.snapshot.chunk-index-gap");
        }
    }

    private static byte[] EncodeChunk(PhysicalSnapshotChunkDescriptor chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        SnapshotChunkFile.ValidateRelativePath(chunk.RelativePath, chunk.ChunkIndex);
        if (chunk.LogicalPayloadDigest is null || chunk.LogicalPayloadDigest.Length != 32 ||
            chunk.StoredPayloadDigest is null || chunk.StoredPayloadDigest.Length != 32)
            throw new InvalidDataException("persistence.snapshot.chunk-descriptor-digest-length");
        if (chunk.Compression is not (SnapshotCompression.None or SnapshotCompression.Zstd))
            throw new InvalidDataException("persistence.snapshot.unsupported-compression");

        return Proto.Encode(stream =>
        {
            if (chunk.ChunkIndex != 0) Proto.WriteUInt32(stream, 1, chunk.ChunkIndex);
            Proto.WriteString(stream, 2, chunk.FirstSectionId);
            Proto.WriteString(stream, 3, chunk.LastSectionId);
            Proto.WriteUInt64(stream, 4, chunk.UncompressedLength);
            Proto.WriteUInt64(stream, 5, chunk.StoredLength);
            if (chunk.Compression != SnapshotCompression.None)
                Proto.WriteUInt32(stream, 6, (uint)chunk.Compression);
            Proto.WriteBytes(stream, 7, chunk.LogicalPayloadDigest);
            Proto.WriteBytes(stream, 8, chunk.StoredPayloadDigest);
            Proto.WriteString(stream, 9, chunk.RelativePath);
        });
    }

    private static PhysicalSnapshotChunkDescriptor DecodeChunk(ReadOnlySpan<byte> encoded)
    {
        var reader = new Proto.Reader(encoded);
        uint index = 0;
        string? first = null;
        string? last = null;
        ulong uncompressed = 0;
        ulong stored = 0;
        uint compression = 0;
        byte[]? logicalDigest = null;
        byte[]? storedDigest = null;
        string? relativePath = null;
        var seen = new HashSet<int>();

        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (!seen.Add(field))
                throw new InvalidDataException("persistence.snapshot.chunk-descriptor-duplicate-field");
            switch (field)
            {
                case 1: reader.RequireWire(wire, 0); index = reader.ReadUInt32(); break;
                case 2: reader.RequireWire(wire, 2); first = reader.ReadString(); break;
                case 3: reader.RequireWire(wire, 2); last = reader.ReadString(); break;
                case 4: reader.RequireWire(wire, 0); uncompressed = reader.ReadVarUInt64(); break;
                case 5: reader.RequireWire(wire, 0); stored = reader.ReadVarUInt64(); break;
                case 6: reader.RequireWire(wire, 0); compression = reader.ReadUInt32(); break;
                case 7: reader.RequireWire(wire, 2); logicalDigest = reader.ReadBytes(); break;
                case 8: reader.RequireWire(wire, 2); storedDigest = reader.ReadBytes(); break;
                case 9: reader.RequireWire(wire, 2); relativePath = reader.ReadString(); break;
                default: throw new InvalidDataException("persistence.snapshot.chunk-descriptor-unknown-field");
            }
        }

        if (first is null || last is null || uncompressed == 0 || stored == 0 ||
            logicalDigest is null || storedDigest is null || relativePath is null)
            throw new InvalidDataException("persistence.snapshot.chunk-descriptor-required-field");
        if (compression > (uint)SnapshotCompression.Zstd)
            throw new InvalidDataException("persistence.snapshot.unsupported-compression");
        if (logicalDigest.Length != 32 || storedDigest.Length != 32)
            throw new InvalidDataException("persistence.snapshot.chunk-descriptor-digest-length");

        var value = new PhysicalSnapshotChunkDescriptor(
            index,
            first,
            last,
            uncompressed,
            stored,
            (SnapshotCompression)compression,
            logicalDigest,
            storedDigest,
            relativePath);
        SnapshotChunkFile.ValidateRelativePath(value.RelativePath, value.ChunkIndex);
        return value;
    }

    private static IReadOnlyList<PhysicalSnapshotChunkDescriptor> CloneChunks(
        IReadOnlyList<PhysicalSnapshotChunkDescriptor> chunks)
        => Array.AsReadOnly(chunks.Select(static chunk => chunk with
        {
            LogicalPayloadDigest = chunk.LogicalPayloadDigest.ToArray(),
            StoredPayloadDigest = chunk.StoredPayloadDigest.ToArray(),
        }).ToArray());

    private static class Proto
    {
        public static byte[] Encode(Action<Stream> write)
        {
            using var stream = new MemoryStream();
            write(stream);
            return stream.ToArray();
        }

        public static void WriteMessage(Stream stream, int field, ReadOnlySpan<byte> value)
            => WriteBytes(stream, field, value);

        public static void WriteString(Stream stream, int field, string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            WriteBytes(stream, field, Encoding.UTF8.GetBytes(value));
        }

        public static void WriteBytes(Stream stream, int field, ReadOnlySpan<byte> value)
        {
            WriteVarUInt64(stream, ((ulong)field << 3) | 2UL);
            WriteVarUInt64(stream, checked((ulong)value.Length));
            stream.Write(value);
        }

        public static void WriteUInt32(Stream stream, int field, uint value)
        {
            WriteVarUInt64(stream, (ulong)field << 3);
            WriteVarUInt64(stream, value);
        }

        public static void WriteUInt64(Stream stream, int field, ulong value)
        {
            WriteVarUInt64(stream, (ulong)field << 3);
            WriteVarUInt64(stream, value);
        }

        private static void WriteVarUInt64(Stream stream, ulong value)
        {
            while (value >= 0x80)
            {
                stream.WriteByte((byte)((value & 0x7f) | 0x80));
                value >>= 7;
            }
            stream.WriteByte((byte)value);
        }

        public ref struct Reader
        {
            private ReadOnlySpan<byte> _remaining;

            public Reader(ReadOnlySpan<byte> encoded) => _remaining = encoded;
            public bool End => _remaining.IsEmpty;

            public (int Field, int Wire) ReadTag()
            {
                var tag = ReadVarUInt64();
                if (tag == 0) throw new InvalidDataException("persistence.snapshot.physical-manifest-tag-zero");
                return (checked((int)(tag >> 3)), checked((int)(tag & 7)));
            }

            public void RequireWire(int actual, int expected)
            {
                if (actual != expected)
                    throw new InvalidDataException("persistence.snapshot.physical-manifest-wire-type");
            }

            public ulong ReadVarUInt64()
            {
                ulong value = 0;
                for (var shift = 0; shift < 64; shift += 7)
                {
                    if (_remaining.IsEmpty)
                        throw new InvalidDataException("persistence.snapshot.physical-manifest-truncated-varint");
                    var current = _remaining[0];
                    _remaining = _remaining[1..];
                    value |= (ulong)(current & 0x7f) << shift;
                    if ((current & 0x80) == 0) return value;
                }
                throw new InvalidDataException("persistence.snapshot.physical-manifest-varint-overflow");
            }

            public uint ReadUInt32()
            {
                var value = ReadVarUInt64();
                if (value > uint.MaxValue)
                    throw new InvalidDataException("persistence.snapshot.physical-manifest-uint32-overflow");
                return (uint)value;
            }

            public byte[] ReadBytes()
            {
                var length = ReadVarUInt64();
                if (length > int.MaxValue || (ulong)_remaining.Length < length)
                    throw new InvalidDataException("persistence.snapshot.physical-manifest-truncated-bytes");
                var result = _remaining[..(int)length].ToArray();
                _remaining = _remaining[(int)length..];
                return result;
            }

            public string ReadString()
            {
                var bytes = ReadBytes();
                try
                {
                    return new UTF8Encoding(false, true).GetString(bytes);
                }
                catch (DecoderFallbackException ex)
                {
                    throw new InvalidDataException("persistence.snapshot.physical-manifest-utf8", ex);
                }
            }
        }
    }
}
