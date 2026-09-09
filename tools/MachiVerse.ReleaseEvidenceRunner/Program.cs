using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class Program
{
    internal const string CanonicalQa04ManifestSha256 = "5de8301439ca57080eefa599da284f9271b29366c791bcb9c2f85ddbfa041423";

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static async Task<int> Main(string[] args)
    {
        try
        {
            var root = FindRepositoryRoot(Directory.GetCurrentDirectory());
            if (args.Length == 0 || string.Equals(args[0], "verify", StringComparison.Ordinal))
            {
                ReleaseEvidenceRunnerVerification.VerifyContract(root);
                return 0;
            }

            if (string.Equals(args[0], "run", StringComparison.Ordinal) && args.Length == 6)
            {
                var planDirectory = Path.GetFullPath(args[4]);
                var outputDirectory = Path.GetFullPath(args[5]);
                var result = await ReleaseEvidenceRunner.RunAsync(
                    root,
                    args[1],
                    args[2],
                    Path.GetFullPath(args[3]),
                    planDirectory,
                    outputDirectory);
                Qa04DeterminismEvidenceVerifier.VerifyAndBind(
                    planDirectory,
                    outputDirectory,
                    args[2]);
                return result;
            }

            if (string.Equals(args[0], "apply", StringComparison.Ordinal) && args.Length == 4)
            {
                ReleaseEvidenceRunner.ApplyFragment(
                    Path.GetFullPath(args[1]),
                    Path.GetFullPath(args[2]),
                    Path.GetFullPath(args[3]));
                return 0;
            }

            throw new ArgumentException(
                "Usage: MachiVerse.ReleaseEvidenceRunner [verify|run <contract-smoke|release> <source-commit> <adapter-executable> <plan-directory> <output-directory>|apply <fragment.json> <base-evidence.json> <output-evidence.json>]");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"INT-03 release evidence runner FAILED: {ex.Message}");
            return 1;
        }
    }

    internal static T ReadJson<T>(string path, string name)
        => JsonSerializer.Deserialize<T>(File.ReadAllBytes(path), Json)
            ?? throw new InvalidDataException($"{name} decoded to null.");

    internal static JsonElement ReadJsonElement(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        return document.RootElement.Clone();
    }

    internal static string WriteJson(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory());
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Json) + Environment.NewLine);
        File.WriteAllBytes(path, bytes);
        return Sha256Hex(bytes);
    }

    internal static string Sha256Hex(ReadOnlySpan<byte> bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    internal static string Sha256File(string path) => Sha256Hex(File.ReadAllBytes(path));

    internal static void RequireLowerHex(string value, int length, string name)
    {
        if (value.Length != length || value.Any(static c => !(c is >= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new InvalidDataException($"{name} must be {length} lowercase hexadecimal characters.");
        if (value.All(static c => c == '0'))
            throw new InvalidDataException($"{name} cannot be all zero.");
    }

    private static string FindRepositoryRoot(string start)
    {
        var current = new DirectoryInfo(start);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "global.json"))
                && Directory.Exists(Path.Combine(current.FullName, "tests")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate MachiVerse repository root.");
    }
}
