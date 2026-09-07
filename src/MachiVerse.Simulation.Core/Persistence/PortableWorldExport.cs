using System.Buffers.Binary;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed record PortableWorldExportPaths(
    string StagingDirectory,
    string FinalDirectory);

public sealed record ExportHistorySegmentHeader(
    ushort FormatMajor,
    ushort FormatMinor,
    ulong FirstSequence,
    ulong LastSequence,
    uint RecordCount,
    byte[] LogicalDigest);

/// <summary>
/// Durable staging and atomic publication boundary for a Phase 4 portable world export.
/// </summary>
public static class PortableWorldExport
{
    public static PortableWorldExportPaths Prepare(string finalDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(finalDirectory);
        var final = Path.GetFullPath(finalDirectory);
        var parent = Path.GetDirectoryName(final)
            ?? throw new ArgumentException("Export destination must have a parent directory.", nameof(finalDirectory));
        Directory.CreateDirectory(parent);
        if (Directory.Exists(final) || File.Exists(final))
            throw new InvalidDataException("persistence.export-target-exists");

        var staging = final + ".staging-" + Guid.NewGuid().ToString("N");
        if (Directory.Exists(staging) || File.Exists(staging))
            throw new InvalidDataException("persistence.export-staging-exists");
        Directory.CreateDirectory(staging);
        return new PortableWorldExportPaths(staging, final);
    }

    public static async Task WriteArtifactDurablyAsync(
        PortableWorldExportPaths export,
        string relativePath,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(export);
        if (bytes.IsEmpty) throw new ArgumentException("Export artifact cannot be empty.", nameof(bytes));
        var path = ResolveStagingPath(export, relativePath);
        var parent = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(parent);
        if (File.Exists(path) || Directory.Exists(path))
            throw new InvalidDataException("persistence.export-artifact-exists");

        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    public static async Task CopyDirectoryDurablyAsync(
        PortableWorldExportPaths export,
        string sourceDirectory,
        string relativeDestination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(export);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        if (!Directory.Exists(sourceDirectory))
            throw new InvalidDataException("persistence.export-source-missing");

        var destination = ResolveStagingPath(export, relativeDestination);
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new InvalidDataException("persistence.export-artifact-exists");
        Directory.CreateDirectory(destination);

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, directory);
            ValidateRelativePath(relative);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(sourceDirectory, file);
            ValidateRelativePath(relative);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous);
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
            await input.CopyToAsync(output, cancellationToken);
            await output.FlushAsync(cancellationToken);
            output.Flush(flushToDisk: true);
        }
    }

    public static async Task FinalizeValidatedAsync(
        PortableWorldExportPaths export,
        Func<string, CancellationToken, Task> verifyBundle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(export);
        ArgumentNullException.ThrowIfNull(verifyBundle);
        if (!Directory.Exists(export.StagingDirectory))
            throw new InvalidDataException("persistence.export-staging-missing");
        if (!Directory.EnumerateFileSystemEntries(export.StagingDirectory).Any())
            throw new InvalidDataException("persistence.export-staging-empty");

        await verifyBundle(export.StagingDirectory, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
            FlushTreeDirectories(export.StagingDirectory);
        DurableFileSystem.AtomicMoveDirectory(export.StagingDirectory, export.FinalDirectory);
        if (!OperatingSystem.IsWindows())
            DurableFileSystem.FlushDirectory(Path.GetDirectoryName(export.FinalDirectory)!);
    }

    public static string ResolveStagingPath(PortableWorldExportPaths export, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(export);
        ValidateRelativePath(relativePath);
        var root = Path.GetFullPath(export.StagingDirectory);
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidDataException("persistence.export-path-invalid");
        return candidate;
    }

    private static void FlushTreeDirectories(string root)
    {
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(static value => value.Length))
            DurableFileSystem.FlushDirectory(directory);
        DurableFileSystem.FlushDirectory(root);
    }

    private static void ValidateRelativePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath))
            throw new InvalidDataException("persistence.export-path-invalid");
        var normalized = relativePath.Replace('\\', '/');
        if (normalized == "." || normalized == ".." ||
            normalized.StartsWith("../", StringComparison.Ordinal) ||
            normalized.Contains("/../", StringComparison.Ordinal) ||
            normalized.EndsWith("/..", StringComparison.Ordinal))
            throw new InvalidDataException("persistence.export-path-invalid");
    }
}

/// <summary>
/// Physical MachiVerseWorldExportV1 contract fixed by the Phase 4 persistence record catalog.
/// Protobuf semantics and logical digest calculation remain schema-owned; this type fixes only
/// the standard bundle paths and MVLOG001 physical framing.
/// </summary>
public static class PortableWorldBundleV1
{
    public const string ManifestRelativePath = "export-manifest.pb";
    public const string SnapshotRelativeDirectory = "snapshot";
    public const string HistoryRelativeDirectory = "history";
    public const string SnapshotManifestRelativePath = "snapshot/manifest.pb";
    public const int HistorySegmentHeaderLength = 64;

    private static ReadOnlySpan<byte> HistoryMagic => "MVLOG001"u8;

    public static string HistorySegmentRelativePath(uint segmentIndex)
        => $"history/{segmentIndex:D8}.mvlog";

    public static Task WriteManifestAsync(
        PortableWorldExportPaths export,
        ReadOnlyMemory<byte> serializedManifest,
        CancellationToken cancellationToken = default)
    {
        if (serializedManifest.IsEmpty)
            throw new ArgumentException("Export manifest protobuf bytes cannot be empty.", nameof(serializedManifest));
        return PortableWorldExport.WriteArtifactDurablyAsync(export, ManifestRelativePath, serializedManifest, cancellationToken);
    }

    public static Task CopyCommittedSnapshotAsync(
        PortableWorldExportPaths export,
        string committedSnapshotDirectory,
        CancellationToken cancellationToken = default)
        => PortableWorldExport.CopyDirectoryDurablyAsync(export, committedSnapshotDirectory, SnapshotRelativeDirectory, cancellationToken);

    public static async Task WriteHistorySegmentAsync(
        PortableWorldExportPaths export,
        uint segmentIndex,
        ulong firstSequence,
        IReadOnlyList<ReadOnlyMemory<byte>> serializedRecords,
        ReadOnlyMemory<byte> segmentLogicalDigest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(export);
        ArgumentNullException.ThrowIfNull(serializedRecords);
        if (serializedRecords.Count == 0)
            throw new ArgumentException("History segment must contain at least one record.", nameof(serializedRecords));
        if (segmentLogicalDigest.Length != 32)
            throw new ArgumentException("History segment logical digest must be 32 bytes.", nameof(segmentLogicalDigest));

        var lastSequence = checked(firstSequence + (ulong)serializedRecords.Count - 1UL);
        var path = PortableWorldExport.ResolveStagingPath(export, HistorySegmentRelativePath(segmentIndex));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path)) throw new InvalidDataException("persistence.export-history-segment-exists");

        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 128 * 1024,
            options: FileOptions.Asynchronous | FileOptions.WriteThrough);

        var header = new byte[HistorySegmentHeaderLength];
        HistoryMagic.CopyTo(header.AsSpan(0, 8));
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(8, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(10, 2), 0);
        BinaryPrimitives.WriteUInt64BigEndian(header.AsSpan(12, 8), firstSequence);
        BinaryPrimitives.WriteUInt64BigEndian(header.AsSpan(20, 8), lastSequence);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(28, 4), checked((uint)serializedRecords.Count));
        segmentLogicalDigest.Span.CopyTo(header.AsSpan(32, 32));
        await stream.WriteAsync(header, cancellationToken);

        var lengthBuffer = new byte[4];
        foreach (var record in serializedRecords)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (record.IsEmpty) throw new InvalidDataException("persistence.export-history-record-empty");
            BinaryPrimitives.WriteUInt32BigEndian(lengthBuffer, checked((uint)record.Length));
            await stream.WriteAsync(lengthBuffer, cancellationToken);
            await stream.WriteAsync(record, cancellationToken);
        }

        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    public static async Task<ExportHistorySegmentHeader> ValidateHistorySegmentAsync(
        string segmentPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segmentPath);
        await using var stream = new FileStream(
            segmentPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        var header = new byte[HistorySegmentHeaderLength];
        await ReadExactlyAsync(stream, header, cancellationToken);
        if (!header.AsSpan(0, 8).SequenceEqual(HistoryMagic))
            throw new InvalidDataException("persistence.export-history-magic-invalid");

        var major = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(8, 2));
        var minor = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(10, 2));
        if (major != 1 || minor != 0)
            throw new InvalidDataException("persistence.export-history-version-unsupported");

        var first = BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(12, 8));
        var last = BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(20, 8));
        var count = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(28, 4));
        if (count == 0 || last < first || checked(last - first + 1UL) != count)
            throw new InvalidDataException("persistence.export-history-range-invalid");
        var digest = header.AsSpan(32, 32).ToArray();

        var lengthBuffer = new byte[4];
        for (uint index = 0; index < count; index++)
        {
            await ReadExactlyAsync(stream, lengthBuffer, cancellationToken);
            var recordLength = BinaryPrimitives.ReadUInt32BigEndian(lengthBuffer);
            if (recordLength == 0)
                throw new InvalidDataException("persistence.export-history-record-empty");
            await SkipExactlyAsync(stream, recordLength, cancellationToken);
        }
        if (stream.ReadByte() != -1)
            throw new InvalidDataException("persistence.export-history-trailing-bytes");

        return new ExportHistorySegmentHeader(major, minor, first, last, count, digest);
    }

    public static void ValidateBundleStructure(string exportDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exportDirectory);
        var root = Path.GetFullPath(exportDirectory);
        if (!Directory.Exists(root)) throw new InvalidDataException("persistence.export-missing");
        if (!File.Exists(Path.Combine(root, ManifestRelativePath)))
            throw new InvalidDataException("persistence.export-manifest-missing");
        if (!File.Exists(Path.Combine(root, SnapshotManifestRelativePath.Replace('/', Path.DirectorySeparatorChar))))
            throw new InvalidDataException("persistence.export-snapshot-manifest-missing");

        var historyDirectory = Path.Combine(root, HistoryRelativeDirectory);
        if (!Directory.Exists(historyDirectory))
            throw new InvalidDataException("persistence.export-history-directory-missing");

        var segments = Directory.EnumerateFiles(historyDirectory, "*.mvlog", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        for (var i = 0; i < segments.Length; i++)
        {
            if (!string.Equals(segments[i], $"{i:D8}.mvlog", StringComparison.Ordinal))
                throw new InvalidDataException("persistence.export-history-segment-name-invalid");
        }
    }

    private static async Task ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], cancellationToken);
            if (read == 0) throw new EndOfStreamException("persistence.export-history-truncated");
            offset += read;
        }
    }

    private static async Task SkipExactlyAsync(Stream stream, uint count, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        ulong remaining = count;
        while (remaining > 0)
        {
            var requested = (int)Math.Min((ulong)buffer.Length, remaining);
            var read = await stream.ReadAsync(buffer.AsMemory(0, requested), cancellationToken);
            if (read == 0) throw new EndOfStreamException("persistence.export-history-truncated");
            remaining -= (uint)read;
        }
    }
}
