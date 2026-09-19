using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Persistence;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04PersistenceVolumeStressResultV1(
    string SchemaVersion,
    int TargetStoredGiB,
    long StoredBytes,
    int ChunkCount,
    int LogicalChunkBytes,
    bool ZstdRoundTripValidated);

public static class Qa04PersistenceVolumeStressV1
{
    private const int LogicalChunkBytes = 64 * 1024 * 1024;

    public static async Task<Qa04PersistenceVolumeStressResultV1> RunAsync(
        string outputRoot,
        int targetStoredGiB,
        CancellationToken cancellationToken = default)
    {
        if (targetStoredGiB != 16)
            throw new InvalidDataException("qa04.persistence.volume-target-must-be-16-gib");
        if (string.IsNullOrWhiteSpace(outputRoot))
            throw new ArgumentException("outputRoot is required.", nameof(outputRoot));

        var directory = Path.Combine(Path.GetFullPath(outputRoot), "qa04-compressed-snapshot-volume");
        Directory.CreateDirectory(directory);

        var logical = GC.AllocateUninitializedArray<byte>(LogicalChunkBytes);
        new Random(0x4d565134).NextBytes(logical);
        var logicalDigest = SHA256.HashData(logical);
        var codec = new ZstdSnapshotChunkCompressionCodecV1();
        var stored = codec.Encode(logical, compressionLevel: 3);
        if (stored.Length < LogicalChunkBytes / 2)
            throw new InvalidDataException("qa04.persistence.volume-fixture-compressed-unexpectedly-small");

        var targetBytes = checked((long)targetStoredGiB * 1024L * 1024L * 1024L);
        long written = 0;
        var index = 0;
        string? firstPath = null;
        string? lastPath = null;
        while (written < targetBytes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(directory, $"{index:00000000}.mvchunk");
            _ = await SnapshotChunkFile.WriteAsync(
                path,
                stored,
                checked((ulong)logical.Length),
                logicalDigest,
                SnapshotCompression.Zstd,
                cancellationToken).ConfigureAwait(false);
            firstPath ??= path;
            lastPath = path;
            written = checked(written + SnapshotChunkFile.HeaderLength + stored.LongLength);
            index++;
        }

        if (firstPath is null || lastPath is null)
            throw new InvalidDataException("qa04.persistence.volume-no-chunks");

        long validatedStoredBytes = 0;
        var validatedChunkCount = 0;
        foreach (var path in Directory.EnumerateFiles(directory, "*.mvchunk", SearchOption.TopDirectoryOnly)
                     .OrderBy(static path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var header = await SnapshotChunkFile.ValidateAsync(path, cancellationToken).ConfigureAwait(false);
            if (header.Compression != SnapshotCompression.Zstd ||
                header.UncompressedLength != (ulong)logical.Length ||
                header.StoredLength != (ulong)stored.Length)
                throw new InvalidDataException("qa04.persistence.volume-chunk-header-drift");
            validatedStoredBytes = checked(validatedStoredBytes + SnapshotChunkFile.HeaderLength + (long)header.StoredLength);
            validatedChunkCount++;
        }
        if (validatedChunkCount != index || validatedStoredBytes != written)
            throw new InvalidDataException("qa04.persistence.volume-full-load-validation-drift");

        var decoded = codec.Decode(stored, checked((ulong)logical.Length));
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(decoded), logicalDigest))
            throw new InvalidDataException("qa04.persistence.volume-zstd-roundtrip-drift");

        return new Qa04PersistenceVolumeStressResultV1(
            "1.0",
            targetStoredGiB,
            written,
            index,
            logical.Length,
            ZstdRoundTripValidated: true);
    }
}
