using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Configuration;
using MachiVerse.Simulation.Core.WorldState;
using ZstdSharp;

namespace MachiVerse.Simulation.Core.Persistence;

public interface ISnapshotChunkCompressionCodecV1 : ISnapshotChunkCompressionDecoderV1
{
    byte[] Encode(ReadOnlyMemory<byte> logicalPayload, int compressionLevel);
}

public sealed class ZstdSnapshotChunkCompressionCodecV1 : ISnapshotChunkCompressionCodecV1
{
    public SnapshotCompression Compression => SnapshotCompression.Zstd;

    public byte[] Encode(ReadOnlyMemory<byte> logicalPayload, int compressionLevel)
    {
        if (compressionLevel is < -5 or > 19)
            throw new InvalidDataException("persistence.snapshot.zstd-level-out-of-range");
        try
        {
            using var compressor = new Compressor(compressionLevel);
            return compressor.Wrap(logicalPayload.Span).ToArray();
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            throw new InvalidDataException("persistence.snapshot.zstd-encode-failed", ex);
        }
    }

    public byte[] Decode(ReadOnlyMemory<byte> storedPayload, ulong expectedUncompressedLength)
    {
        if (expectedUncompressedLength > CanonicalSnapshotSectionValidationV1.HardMaxUncompressedBytes)
            throw new InvalidDataException("persistence.snapshot.uncompressed-length-over-hard-max");
        try
        {
            var decoded = GC.AllocateUninitializedArray<byte>(checked((int)expectedUncompressedLength));
            using var decompressor = new Decompressor();
            if (!decompressor.TryUnwrap(storedPayload.Span, decoded, out var written) || written != decoded.Length)
                throw new InvalidDataException("persistence.snapshot.uncompressed-length-mismatch");
            return decoded;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidDataException("persistence.snapshot.zstd-decode-failed", ex);
        }
    }
}

public sealed record SnapshotCanonicalCompressionPolicyV1(
    SnapshotCompression Compression,
    int ZstdLevel)
{
    public static SnapshotCanonicalCompressionPolicyV1 FromConfig(EffectiveCoreConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var compression = config.Get<string>("persistence.snapshot-compression");
        var level = checked((int)config.Get<long>("persistence.snapshot-zstd-level"));
        if (level is < -5 or > 19)
            throw new InvalidDataException("persistence.snapshot.zstd-level-out-of-range");
        return compression switch
        {
            "none" => new SnapshotCanonicalCompressionPolicyV1(SnapshotCompression.None, level),
            "zstd" => new SnapshotCanonicalCompressionPolicyV1(SnapshotCompression.Zstd, level),
            _ => throw new InvalidDataException("persistence.snapshot.unsupported-compression"),
        };
    }
}

public static class CanonicalSnapshotProductionChunkFileV1
{
    public static async Task<SnapshotChunkHeader> WriteAsync(
        string path,
        SnapshotChunkFragmentPayloadV1 payload,
        EffectiveCoreConfig config,
        ISnapshotChunkCompressionCodecV1? zstdCodec = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(config);

        var policy = SnapshotCanonicalCompressionPolicyV1.FromConfig(config);
        var logicalWire = SnapshotChunkPayloadWireCodecV1.Encode(payload);
        if (logicalWire.Length > CanonicalSnapshotSectionValidationV1.HardMaxUncompressedBytes)
            throw new InvalidDataException("persistence.snapshot-item-too-large");
        var logicalDigest = SnapshotChunkLogicalPayloadDigestV1.Compute(payload);

        ReadOnlyMemory<byte> stored;
        switch (policy.Compression)
        {
            case SnapshotCompression.None:
                stored = logicalWire;
                break;
            case SnapshotCompression.Zstd:
                zstdCodec ??= new ZstdSnapshotChunkCompressionCodecV1();
                if (zstdCodec.Compression != SnapshotCompression.Zstd)
                    throw new InvalidDataException("persistence.snapshot.compression-codec-mismatch");
                stored = zstdCodec.Encode(logicalWire, policy.ZstdLevel);
                break;
            default:
                throw new InvalidDataException("persistence.snapshot.unsupported-compression");
        }

        return await SnapshotChunkFile.WriteAsync(
            path,
            stored,
            checked((ulong)logicalWire.Length),
            logicalDigest,
            policy.Compression,
            cancellationToken).ConfigureAwait(false);
    }
}

public static class CanonicalSnapshotProductionPhysicalDrainV1
{
    public static async Task<IReadOnlyList<PhysicalSnapshotChunkDescriptor>> StageAsync(
        SnapshotPhysicalPaths physical,
        IEnumerable<CanonicalSnapshotSectionMaterialV1> sections,
        EffectiveCoreConfig config,
        WorldStateV1? frozenState = null,
        ISnapshotChunkCompressionCodecV1? zstdCodec = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(physical);
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(config);
        if (!Directory.Exists(physical.StagingChunksDirectory))
            throw new InvalidDataException("persistence.snapshot-chunks-missing");

        var chunks = SnapshotChunkPackerV1.PackStandard(sections, frozenState);
        var descriptors = new List<PhysicalSnapshotChunkDescriptor>(chunks.Count);
        for (var i = 0; i < chunks.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunk = chunks[i];
            var relative = SnapshotChunkFile.RelativePath((uint)i);
            var path = Path.Combine(physical.StagingDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
            var header = await CanonicalSnapshotProductionChunkFileV1.WriteAsync(
                path,
                chunk,
                config,
                zstdCodec,
                cancellationToken).ConfigureAwait(false);
            descriptors.Add(Descriptor((uint)i, chunk, header, relative));
        }
        return Array.AsReadOnly(descriptors.ToArray());
    }

    /// <summary>
    /// Bounded-memory production drain. Section metadata is validated up front, then fragments are
    /// packed and written one chunk at a time. Only the current chunk plus the compact physical
    /// descriptor list remains resident; no all-fragment or all-chunk payload collection is built.
    /// </summary>
    public static async Task<IReadOnlyList<PhysicalSnapshotChunkDescriptor>> StageStreamingAsync(
        SnapshotPhysicalPaths physical,
        IEnumerable<CanonicalSnapshotStreamingSectionV1> sections,
        EffectiveCoreConfig config,
        WorldStateV1? frozenState = null,
        ISnapshotChunkCompressionCodecV1? zstdCodec = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(physical);
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(config);
        if (!Directory.Exists(physical.StagingChunksDirectory))
            throw new InvalidDataException("persistence.snapshot-chunks-missing");

        var descriptors = new List<PhysicalSnapshotChunkDescriptor>();
        uint chunkIndex = 0;
        foreach (var chunk in SnapshotChunkStreamingPackerV1.PackStandard(sections, frozenState))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = SnapshotChunkFile.RelativePath(chunkIndex);
            var path = Path.Combine(physical.StagingDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
            var header = await CanonicalSnapshotProductionChunkFileV1.WriteAsync(
                path,
                chunk,
                config,
                zstdCodec,
                cancellationToken).ConfigureAwait(false);
            descriptors.Add(Descriptor(chunkIndex, chunk, header, relative));
            chunkIndex = checked(chunkIndex + 1);
        }

        if (descriptors.Count == 0)
            throw new InvalidDataException("persistence.snapshot-fragment-invalid:no-fragments");
        return Array.AsReadOnly(descriptors.ToArray());
    }

    public static Task<IReadOnlyList<PhysicalSnapshotChunkDescriptor>> StageRunningCutAsync(
        RunningSnapshotCutV1 cut,
        SnapshotPhysicalPaths physical,
        IEnumerable<CanonicalSnapshotSectionMaterialV1> sections,
        EffectiveCoreConfig frozenConfig,
        ISnapshotChunkCompressionCodecV1? zstdCodec = null,
        CancellationToken cancellationToken = default)
    {
        ValidateRunningCut(cut, physical, frozenConfig);
        return StageAsync(
            physical,
            sections,
            frozenConfig,
            cut.FrozenState,
            zstdCodec,
            cancellationToken);
    }

    public static Task<IReadOnlyList<PhysicalSnapshotChunkDescriptor>> StageRunningCutStreamingAsync(
        RunningSnapshotCutV1 cut,
        SnapshotPhysicalPaths physical,
        IEnumerable<CanonicalSnapshotStreamingSectionV1> sections,
        EffectiveCoreConfig frozenConfig,
        ISnapshotChunkCompressionCodecV1? zstdCodec = null,
        CancellationToken cancellationToken = default)
    {
        ValidateRunningCut(cut, physical, frozenConfig);
        return StageStreamingAsync(
            physical,
            sections,
            frozenConfig,
            cut.FrozenState,
            zstdCodec,
            cancellationToken);
    }

    public static IReadOnlyList<ISnapshotChunkCompressionDecoderV1> ProductionDecoders(
        ISnapshotChunkCompressionCodecV1? zstdCodec = null)
        => Array.AsReadOnly<ISnapshotChunkCompressionDecoderV1>([
            zstdCodec ?? new ZstdSnapshotChunkCompressionCodecV1()
        ]);

    private static void ValidateRunningCut(
        RunningSnapshotCutV1 cut,
        SnapshotPhysicalPaths physical,
        EffectiveCoreConfig frozenConfig)
    {
        ArgumentNullException.ThrowIfNull(cut);
        ArgumentNullException.ThrowIfNull(physical);
        ArgumentNullException.ThrowIfNull(frozenConfig);
        if (physical.SnapshotId != cut.SnapshotId)
            throw new InvalidDataException("snapshot-running.physical-id-mismatch");
        if (frozenConfig.Generation != cut.FrozenState.Header.ConfigGeneration ||
            !CryptographicOperations.FixedTimeEquals(frozenConfig.Digest, cut.FrozenState.Diagnostic.ConfigDigest))
            throw new InvalidDataException("snapshot-running.compression-config-authority-mismatch");
    }

    private static PhysicalSnapshotChunkDescriptor Descriptor(
        uint chunkIndex,
        SnapshotChunkFragmentPayloadV1 chunk,
        SnapshotChunkHeader header,
        string relative)
        => new(
            chunkIndex,
            chunk.Fragments[0].SectionId,
            chunk.Fragments[^1].SectionId,
            header.UncompressedLength,
            header.StoredLength,
            header.Compression,
            header.LogicalPayloadDigest.ToArray(),
            header.StoredPayloadDigest.ToArray(),
            relative);
}
