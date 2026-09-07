using System.Globalization;
using System.Text;
using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed record WorldPersistencePaths(
    string Root,
    string WorldDirectory,
    string CurrentPath,
    string GenerationDirectory,
    string DatabasePath,
    string SnapshotsDirectory);

public static class PersistenceLayout
{
    private const int CurrentLength = 17;

    public static WorldPersistencePaths Resolve(string root, OpaqueId128 worldId, ulong generation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (worldId.IsZero) throw new ArgumentException("WorldId ZERO is invalid for persistence layout.", nameof(worldId));
        if (generation == 0) throw new ArgumentOutOfRangeException(nameof(generation), "PersistenceGeneration starts at 1.");

        var worldDirectory = Path.Combine(Path.GetFullPath(root), "worlds", worldId.ToString());
        var generationDirectory = GenerationDirectory(worldDirectory, generation);
        return new WorldPersistencePaths(
            Path.GetFullPath(root),
            worldDirectory,
            Path.Combine(worldDirectory, "CURRENT"),
            generationDirectory,
            Path.Combine(generationDirectory, "world.sqlite3"),
            Path.Combine(generationDirectory, "snapshots"));
    }

    public static void EnsureGenerationDirectories(WorldPersistencePaths paths)
    {
        Directory.CreateDirectory(paths.GenerationDirectory);
        Directory.CreateDirectory(paths.SnapshotsDirectory);
    }

    public static async Task WriteCurrentAsync(WorldPersistencePaths paths, ulong generation, CancellationToken cancellationToken = default)
    {
        if (generation == 0) throw new ArgumentOutOfRangeException(nameof(generation));
        Directory.CreateDirectory(paths.WorldDirectory);

        var targetGenerationDirectory = GenerationDirectory(paths.WorldDirectory, generation);
        if (!Directory.Exists(targetGenerationDirectory))
            throw new InvalidDataException("persistence.current-generation-missing");

        var content = Encoding.ASCII.GetBytes(generation.ToString("x16", CultureInfo.InvariantCulture) + "\n");
        if (content.Length != CurrentLength) throw new InvalidOperationException("CURRENT canonical encoding must be exactly 17 bytes.");

        var temporary = paths.CurrentPath + ".tmp-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        try
        {
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                options: FileOptions.WriteThrough))
            {
                await stream.WriteAsync(content, cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            DurableFileSystem.AtomicReplaceFile(temporary, paths.CurrentPath);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static ulong ReadCurrent(WorldPersistencePaths paths)
    {
        var bytes = File.ReadAllBytes(paths.CurrentPath);
        if (bytes.Length != CurrentLength || bytes[^1] != (byte)'\n')
            throw new InvalidDataException("CURRENT must be exactly 16 lowercase hexadecimal digits plus newline.");

        var text = Encoding.ASCII.GetString(bytes, 0, 16);
        if (text.Any(static c => c is >= 'A' and <= 'F') || text.Any(static c => !IsLowerHex(c)))
            throw new InvalidDataException("CURRENT generation is not canonical lowercase hexadecimal.");
        if (!ulong.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var generation) || generation == 0)
            throw new InvalidDataException("CURRENT references an invalid PersistenceGeneration.");
        if (!Directory.Exists(GenerationDirectory(paths.WorldDirectory, generation)))
            throw new InvalidDataException("persistence.current-generation-missing");
        return generation;
    }

    private static string GenerationDirectory(string worldDirectory, ulong generation)
        => Path.Combine(worldDirectory, "generations", generation.ToString("x16", CultureInfo.InvariantCulture));

    private static bool IsLowerHex(char c) => c is >= '0' and <= '9' or >= 'a' and <= 'f';
}
