namespace MachiVerse.Simulation.Core.Persistence;

public sealed record PortableWorldExportPaths(
    string StagingDirectory,
    string FinalDirectory);

/// <summary>
/// Format-neutral durable staging boundary for a future portable world export format.
/// Phase 4 fixes that exports are built from a committed snapshot plus the required history
/// range, but the concrete bundle schema/framing remains a design decision. This helper only
/// provides confined staging, durable artifact writes, validation and atomic publication.
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
        {
            FlushTreeDirectories(export.StagingDirectory);
        }
        DurableFileSystem.AtomicMoveDirectory(export.StagingDirectory, export.FinalDirectory);
        if (!OperatingSystem.IsWindows())
        {
            var parent = Path.GetDirectoryName(export.FinalDirectory)!;
            DurableFileSystem.FlushDirectory(parent);
        }
    }

    public static string ResolveStagingPath(PortableWorldExportPaths export, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(export);
        ValidateRelativePath(relativePath);
        var root = Path.GetFullPath(export.StagingDirectory);
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        var prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
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
