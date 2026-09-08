using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace MachiVerse.Simulation.Core.Persistence;

public enum SnapshotCompression : byte
{
    None = 0,
    Zstd = 1
}

public sealed record SnapshotChunkHeader(
    ushort FormatMajor,
    ushort FormatMinor,
    SnapshotCompression Compression,
    ulong UncompressedLength,
    ulong StoredLength,
    byte[] LogicalPayloadDigest,
    byte[] StoredPayloadDigest);

public static class SnapshotChunkFile
{
    public const int HeaderLength = 96;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("MVCHNK01");

    public static string RelativePath(uint chunkIndex) => $"chunks/{chunkIndex:00000000}.mvchunk";

    public static void ValidateRelativePath(string relativePath, uint chunkIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath) || relativePath.Contains("..", StringComparison.Ordinal))
            throw new InvalidDataException("persistence.snapshot.invalid-chunk-path");
        if (!string.Equals(relativePath, RelativePath(chunkIndex), StringComparison.Ordinal))
            throw new InvalidDataException("persistence.snapshot.invalid-chunk-path");
    }

    public static async Task<SnapshotChunkHeader> WriteAsync(
        string path,
        ReadOnlyMemory<byte> storedPayload,
        ulong uncompressedLength,
        ReadOnlyMemory<byte> logicalPayloadDigest,
        SnapshotCompression compression,
        CancellationToken cancellationToken = default)
    {
        if (logicalPayloadDigest.Length != 32)
            throw new ArgumentException("Logical payload digest must be 32 bytes.", nameof(logicalPayloadDigest));
        if (compression is not (SnapshotCompression.None or SnapshotCompression.Zstd))
            throw new InvalidDataException("persistence.snapshot.unsupported-compression");
        if (compression == SnapshotCompression.None && uncompressedLength != (ulong)storedPayload.Length)
            throw new InvalidDataException("persistence.snapshot.none-length-mismatch");

        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Snapshot chunk path must have a parent directory.", nameof(path));
        Directory.CreateDirectory(directory);

        var storedDigest = SHA256.HashData(storedPayload.Span);
        var header = new byte[HeaderLength];
        Magic.CopyTo(header, 0);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(8, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(10, 2), 0);
        header[12] = (byte)compression;
        header[13] = 0;
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(14, 2), 0);
        BinaryPrimitives.WriteUInt64BigEndian(header.AsSpan(16, 8), uncompressedLength);
        BinaryPrimitives.WriteUInt64BigEndian(header.AsSpan(24, 8), (ulong)storedPayload.Length);
        logicalPayloadDigest.Span.CopyTo(header.AsSpan(32, 32));
        storedDigest.CopyTo(header, 64);

        await using (var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await stream.WriteAsync(header, cancellationToken);
            await stream.WriteAsync(storedPayload, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Flush(flushToDisk: true);
        }

        return new SnapshotChunkHeader(1, 0, compression, uncompressedLength, (ulong)storedPayload.Length,
            logicalPayloadDigest.ToArray(), storedDigest);
    }

    public static async Task<SnapshotChunkHeader> ValidateAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        if (stream.Length < HeaderLength)
            throw new InvalidDataException("persistence.snapshot.truncated-chunk");

        var header = new byte[HeaderLength];
        await ReadExactlyAsync(stream, header, cancellationToken);

        if (!header.AsSpan(0, 8).SequenceEqual(Magic))
            throw new InvalidDataException("persistence.snapshot.invalid-chunk-magic");

        var major = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(8, 2));
        var minor = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(10, 2));
        if (major != 1 || minor != 0)
            throw new InvalidDataException("persistence.snapshot.unsupported-chunk-version");

        var compression = (SnapshotCompression)header[12];
        if (compression is not (SnapshotCompression.None or SnapshotCompression.Zstd))
            throw new InvalidDataException("persistence.snapshot.unsupported-compression");
        if (header[13] != 0 || BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(14, 2)) != 0)
            throw new InvalidDataException("persistence.snapshot.unknown-chunk-flags");

        var uncompressedLength = BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(16, 8));
        var storedLength = BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(24, 8));
        if (storedLength > long.MaxValue || stream.Length != HeaderLength + (long)storedLength)
            throw new InvalidDataException("persistence.snapshot.chunk-length-mismatch");
        if (compression == SnapshotCompression.None && uncompressedLength != storedLength)
            throw new InvalidDataException("persistence.snapshot.none-length-mismatch");

        var expectedStoredDigest = header.AsSpan(64, 32).ToArray();
        using var incremental = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            incremental.AppendData(buffer, 0, read);
        }
        var actualStoredDigest = incremental.GetHashAndReset();
        if (!CryptographicOperations.FixedTimeEquals(expectedStoredDigest, actualStoredDigest))
            throw new InvalidDataException("persistence.snapshot.stored-digest-mismatch");

        return new SnapshotChunkHeader(
            major,
            minor,
            compression,
            uncompressedLength,
            storedLength,
            header.AsSpan(32, 32).ToArray(),
            expectedStoredDigest);
    }

    private static async Task ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], cancellationToken);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
    }
}
