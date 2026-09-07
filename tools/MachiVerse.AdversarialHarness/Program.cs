using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MachiVerse.AdversarialHarness;

internal static class Program
{
    private const int EnvelopeLimitBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly string[] RequiredCrashOperations =
    [
        "audit-append",
        "migration-generation-switch",
        "operation-acceptance",
        "operation-scheduling",
        "snapshot-commit",
        "transition-commit"
    ];

    private static readonly string[] RequiredCrashPoints =
    [
        "before-db-begin",
        "before-fsync-or-commit",
        "before-response-or-publication",
        "immediately-after-commit",
        "mid-write"
    ];

    private static readonly string[] RequiredFuzzCategories =
    [
        "law-ast",
        "protobuf",
        "snapshot",
        "toml",
        "websocket"
    ];

    private static readonly HashSet<string> SupportedMutations = new(StringComparer.Ordinal)
    {
        "append-zero",
        "duplicate-prefix",
        "empty",
        "flip-first-bit",
        "oversize-metadata",
        "truncate-half"
    };

    private static readonly HashSet<string> SecurityDispositions = new(StringComparer.Ordinal)
    {
        "deny",
        "no-auto-release",
        "no-secret",
        "tamper-detected"
    };

    private static int Main(string[] args)
    {
        try
        {
            var command = args.Length == 0 ? "verify" : args[0];
            var root = FindRepositoryRoot(Directory.GetCurrentDirectory());
            var manifestPath = Path.Combine(root, "tests", "adversarial-fixtures", "v1", "harness-manifest.json");
            var raw = File.ReadAllText(manifestPath, Encoding.UTF8);
            var manifest = JsonSerializer.Deserialize<HarnessManifest>(raw, JsonOptions)
                ?? throw new InvalidDataException("QA-03 manifest decoded to null.");

            ValidateManifest(manifest, raw);
            var corpus = BuildCorpus(manifest);
            ValidateCorpus(corpus);

            return command switch
            {
                "verify" => Verify(corpus),
                "materialize" => Materialize(corpus, args.Length >= 2
                    ? Path.GetFullPath(args[1])
                    : Path.Combine(root, "artifacts", "qa-03-adversarial-corpus")),
                _ => throw new ArgumentException("Usage: MachiVerse.AdversarialHarness [verify|materialize <output-directory>]")
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"QA-03 harness FAILED: {ex.Message}");
            return 1;
        }
    }

    private static int Verify(MaterializedCorpus corpus)
    {
        var second = BuildCorpus(corpus.Manifest);
        var digestA = CorpusDigest(corpus);
        var digestB = CorpusDigest(second);
        if (!string.Equals(digestA, digestB, StringComparison.Ordinal))
            throw new InvalidDataException("QA-03 corpus generation is not deterministic across repeated materialization.");

        Console.WriteLine("QA-03 adversarial harness verification PASS");
        Console.WriteLine($"Crash cases: {corpus.CrashCases.Count}");
        Console.WriteLine($"Fuzz mutation cases: {corpus.MutationCases.Count}");
        Console.WriteLine($"Security negative cases: {corpus.SecurityCases.Count}");
        Console.WriteLine($"Corpus SHA-256: {digestA}");
        return 0;
    }

    private static int Materialize(MaterializedCorpus corpus, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        WriteJson(Path.Combine(outputDirectory, "crash-matrix.json"), corpus.CrashCases);
        WriteJson(Path.Combine(outputDirectory, "mutation-cases.json"), corpus.MutationCases);
        WriteJson(Path.Combine(outputDirectory, "security-negative-corpus.json"), corpus.SecurityCases);
        WriteJson(Path.Combine(outputDirectory, "target-adapter-contract.json"), corpus.Manifest.AdapterContract);
        WriteJson(Path.Combine(outputDirectory, "corpus-summary.json"), new CorpusSummary(
            corpus.Manifest.SchemaVersion,
            corpus.CrashCases.Count,
            corpus.MutationCases.Count,
            corpus.SecurityCases.Count,
            CorpusDigest(corpus)));
        Console.WriteLine($"QA-03 corpus materialized at {outputDirectory}");
        return 0;
    }

    private static MaterializedCorpus BuildCorpus(HarnessManifest manifest)
    {
        var crashCases = new List<CrashCase>();
        foreach (var operation in manifest.CrashOperations.Order(StringComparer.Ordinal))
        {
            foreach (var point in manifest.CrashPoints.Order(StringComparer.Ordinal))
            {
                crashCases.Add(new CrashCase(
                    CaseId("crash", operation, point),
                    operation,
                    point,
                    CrashInvariant(point)));
            }
        }

        var mutationCases = new List<MutationCase>();
        foreach (var target in manifest.FuzzTargets.OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            var seed = Encoding.UTF8.GetBytes(target.SeedText);
            foreach (var mutation in target.Mutations.Order(StringComparer.Ordinal))
            {
                var mutated = ApplyMutation(seed, mutation, out var declaredLength);
                mutationCases.Add(new MutationCase(
                    CaseId("fuzz", target.Id, mutation),
                    target.Id,
                    target.Category,
                    target.TestCaseId,
                    mutation,
                    Convert.ToBase64String(mutated),
                    declaredLength,
                    Sha256Hex(mutated),
                    target.ExpectedDisposition));
            }
        }

        var securityCases = manifest.SecurityCases
            .OrderBy(x => x.Id, StringComparer.Ordinal)
            .Select(x => new SecurityNegativeCase(
                CaseId("security", x.Id),
                x.Id,
                x.TestCaseId,
                x.ExpectedDisposition,
                x.SyntheticOnly))
            .ToArray();

        return new MaterializedCorpus(manifest, crashCases, mutationCases, securityCases);
    }

    private static void ValidateManifest(HarnessManifest manifest, string raw)
    {
        if (!string.Equals(manifest.SchemaVersion, "1.0", StringComparison.Ordinal))
            throw new InvalidDataException("Unsupported QA-03 manifest schemaVersion.");
        ValidateToken(manifest.MutationSeed, nameof(manifest.MutationSeed));

        RequireCanonicalExact(manifest.CrashOperations, RequiredCrashOperations, "crashOperations");
        RequireCanonicalExact(manifest.CrashPoints, RequiredCrashPoints, "crashPoints");

        if (manifest.FuzzTargets.Length == 0)
            throw new InvalidDataException("At least one fuzz target is required.");
        RequireCanonicalOrder(manifest.FuzzTargets.Select(x => x.Id), "fuzzTargets.id");
        var categories = manifest.FuzzTargets.Select(x => x.Category).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (!categories.SequenceEqual(RequiredFuzzCategories, StringComparer.Ordinal))
            throw new InvalidDataException("QA-03 fuzz target categories must exactly cover law-ast/protobuf/snapshot/toml/websocket.");

        foreach (var target in manifest.FuzzTargets)
        {
            ValidateToken(target.Id, "fuzz target id");
            ValidateTestCaseId(target.TestCaseId);
            if (string.IsNullOrEmpty(target.SeedText))
                throw new InvalidDataException($"Fuzz target '{target.Id}' requires non-empty seedText.");
            if (!string.Equals(target.ExpectedDisposition, "reject-or-explicit-failure", StringComparison.Ordinal))
                throw new InvalidDataException($"Fuzz target '{target.Id}' must fail closed.");
            RequireCanonicalOrder(target.Mutations, $"fuzz target '{target.Id}' mutations");
            if (target.Mutations.Length < 4)
                throw new InvalidDataException($"Fuzz target '{target.Id}' needs at least four mutation classes.");
            foreach (var mutation in target.Mutations)
            {
                if (!SupportedMutations.Contains(mutation))
                    throw new InvalidDataException($"Unsupported mutation '{mutation}'.");
            }
        }

        if (manifest.SecurityCases.Length == 0)
            throw new InvalidDataException("Security negative corpus cannot be empty.");
        RequireCanonicalOrder(manifest.SecurityCases.Select(x => x.Id), "securityCases.id");
        foreach (var securityCase in manifest.SecurityCases)
        {
            ValidateToken(securityCase.Id, "security case id");
            ValidateTestCaseId(securityCase.TestCaseId);
            if (!SecurityDispositions.Contains(securityCase.ExpectedDisposition))
                throw new InvalidDataException($"Security case '{securityCase.Id}' has unsupported disposition.");
            if (!securityCase.SyntheticOnly)
                throw new InvalidDataException($"Security case '{securityCase.Id}' must be synthetic-only; real credentials/certificates are forbidden.");
        }

        ValidateAdapterContract(manifest.AdapterContract);
        ValidateNoSecretMaterial(raw);
    }

    private static void ValidateCorpus(MaterializedCorpus corpus)
    {
        if (corpus.CrashCases.Count != 30)
            throw new InvalidDataException("Persistence crash matrix must contain exactly 6 x 5 = 30 cases.");
        EnsureUnique(corpus.CrashCases.Select(x => x.CaseId), "crash case ids");
        EnsureUnique(corpus.MutationCases.Select(x => x.CaseId), "mutation case ids");
        EnsureUnique(corpus.SecurityCases.Select(x => x.CaseId), "security case ids");

        foreach (var mutation in corpus.MutationCases)
        {
            if (mutation.Mutation == "oversize-metadata")
            {
                if (mutation.DeclaredLength <= EnvelopeLimitBytes)
                    throw new InvalidDataException("oversize-metadata must declare a payload above the 8 MiB envelope limit.");
            }
            else
            {
                var bytes = Convert.FromBase64String(mutation.PayloadBase64);
                if (mutation.DeclaredLength != bytes.Length)
                    throw new InvalidDataException($"Mutation '{mutation.CaseId}' has inconsistent declaredLength.");
                if (!string.Equals(mutation.PayloadSha256, Sha256Hex(bytes), StringComparison.Ordinal))
                    throw new InvalidDataException($"Mutation '{mutation.CaseId}' digest mismatch.");
            }
        }
    }

    private static void ValidateAdapterContract(AdapterContract adapter)
    {
        if (!string.Equals(adapter.Protocol, "jsonl", StringComparison.Ordinal))
            throw new InvalidDataException("QA-03 external target adapter protocol must be jsonl.");
        RequireCanonicalOrder(adapter.RequestFields, "adapter requestFields");
        RequireCanonicalOrder(adapter.ResultFields, "adapter resultFields");
        RequireCanonicalOrder(adapter.AllowedOutcomes, "adapter allowedOutcomes");
        var requiredRequest = new[] { "caseId", "category", "declaredLength", "expectedDisposition", "payloadBase64" }.Order(StringComparer.Ordinal);
        if (!adapter.RequestFields.SequenceEqual(requiredRequest, StringComparer.Ordinal))
            throw new InvalidDataException("Adapter request fields are incomplete.");
        var requiredResult = new[] { "caseId", "diagnosticDigest", "outcome", "reasonCode" }.Order(StringComparer.Ordinal);
        if (!adapter.ResultFields.SequenceEqual(requiredResult, StringComparer.Ordinal))
            throw new InvalidDataException("Adapter result fields are incomplete.");
        if (string.IsNullOrWhiteSpace(adapter.Rule))
            throw new InvalidDataException("Adapter authority rule must be explicit.");
    }

    private static byte[] ApplyMutation(byte[] seed, string mutation, out int declaredLength)
    {
        byte[] result;
        switch (mutation)
        {
            case "empty":
                result = [];
                break;
            case "truncate-half":
                result = seed[..Math.Max(1, seed.Length / 2)];
                break;
            case "flip-first-bit":
                result = seed.ToArray();
                result[0] ^= 0x01;
                break;
            case "append-zero":
                result = [.. seed, 0x00];
                break;
            case "duplicate-prefix":
                var prefixLength = Math.Min(8, seed.Length);
                result = [.. seed[..prefixLength], .. seed];
                break;
            case "oversize-metadata":
                result = seed.ToArray();
                declaredLength = EnvelopeLimitBytes + 1;
                return result;
            default:
                throw new InvalidDataException($"Unsupported mutation '{mutation}'.");
        }

        declaredLength = result.Length;
        return result;
    }

    private static string CrashInvariant(string point)
        => point is "immediately-after-commit" or "before-response-or-publication"
            ? "durable-fact-recovered-without-duplicate-or-false-negative"
            : "pre-transition-authority-preserved-no-partial-durable-success";

    private static string CaseId(params string[] parts)
    {
        var semantic = string.Join('\0', parts);
        var suffix = Sha256Hex(Encoding.UTF8.GetBytes(semantic))[..16];
        return $"qa03.{string.Join('.', parts)}.{suffix}";
    }

    private static string CorpusDigest(MaterializedCorpus corpus)
    {
        var canonical = new
        {
            schemaVersion = corpus.Manifest.SchemaVersion,
            mutationSeed = corpus.Manifest.MutationSeed,
            crashCases = corpus.CrashCases.OrderBy(x => x.CaseId, StringComparer.Ordinal),
            mutationCases = corpus.MutationCases.OrderBy(x => x.CaseId, StringComparer.Ordinal),
            securityCases = corpus.SecurityCases.OrderBy(x => x.CaseId, StringComparer.Ordinal),
            adapter = corpus.Manifest.AdapterContract
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(canonical, new JsonSerializerOptions { WriteIndented = false });
        return Sha256Hex(bytes);
    }

    private static void ValidateNoSecretMaterial(string raw)
    {
        var patterns = new[]
        {
            @"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----",
            @"(?i)(?:access_token|refresh_token|client_secret|authorization_code)\s*[:=]\s*[\"']?[A-Za-z0-9._~-]{12,}",
            @"__Host-mv_session=[^;\s]{8,}",
            @"eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}"
        };
        foreach (var pattern in patterns)
        {
            if (Regex.IsMatch(raw, pattern, RegexOptions.CultureInvariant))
                throw new InvalidDataException("QA-03 corpus appears to contain credential/private-key material.");
        }
    }

    private static void ValidateToken(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(ch => ch > 0x7f || char.IsControl(ch) || char.IsWhiteSpace(ch)))
            throw new InvalidDataException($"{field} must be non-empty canonical ASCII token text.");
    }

    private static void ValidateTestCaseId(string value)
    {
        if (!Regex.IsMatch(value, "^[a-z0-9][a-z0-9.-]*$", RegexOptions.CultureInvariant))
            throw new InvalidDataException($"Invalid TestCaseId '{value}'.");
    }

    private static void RequireCanonicalExact(IEnumerable<string> actual, IEnumerable<string> required, string field)
    {
        var actualArray = actual.ToArray();
        RequireCanonicalOrder(actualArray, field);
        var requiredArray = required.Order(StringComparer.Ordinal).ToArray();
        if (!actualArray.SequenceEqual(requiredArray, StringComparer.Ordinal))
            throw new InvalidDataException($"{field} does not match the canonical required set.");
    }

    private static void RequireCanonicalOrder(IEnumerable<string> values, string field)
    {
        var actual = values.ToArray();
        var sorted = actual.Order(StringComparer.Ordinal).ToArray();
        if (!actual.SequenceEqual(sorted, StringComparer.Ordinal))
            throw new InvalidDataException($"{field} must be ASCII/ordinal ascending.");
        if (actual.Distinct(StringComparer.Ordinal).Count() != actual.Length)
            throw new InvalidDataException($"{field} contains duplicates.");
    }

    private static void EnsureUnique(IEnumerable<string> values, string field)
    {
        var array = values.ToArray();
        if (array.Distinct(StringComparer.Ordinal).Count() != array.Length)
            throw new InvalidDataException($"Duplicate {field} detected.");
    }

    private static string Sha256Hex(ReadOnlySpan<byte> bytes)
        => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static void WriteJson<T>(string path, T value)
        => File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions) + Environment.NewLine, new UTF8Encoding(false));

    private static string FindRepositoryRoot(string start)
    {
        var current = new DirectoryInfo(start);
        while (current is not null)
        {
            var manifest = Path.Combine(current.FullName, "tests", "adversarial-fixtures", "v1", "harness-manifest.json");
            if (File.Exists(manifest)) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate MachiVerse repository root containing QA-03 manifest.");
    }
}

internal sealed class HarnessManifest
{
    public string SchemaVersion { get; init; } = string.Empty;
    public string MutationSeed { get; init; } = string.Empty;
    public string[] CrashOperations { get; init; } = [];
    public string[] CrashPoints { get; init; } = [];
    public FuzzTarget[] FuzzTargets { get; init; } = [];
    public SecurityCase[] SecurityCases { get; init; } = [];
    public AdapterContract AdapterContract { get; init; } = new();
}

internal sealed class FuzzTarget
{
    public string Id { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string TestCaseId { get; init; } = string.Empty;
    public string SeedText { get; init; } = string.Empty;
    public string[] Mutations { get; init; } = [];
    public string ExpectedDisposition { get; init; } = string.Empty;
}

internal sealed class SecurityCase
{
    public string Id { get; init; } = string.Empty;
    public string TestCaseId { get; init; } = string.Empty;
    public string ExpectedDisposition { get; init; } = string.Empty;
    public bool SyntheticOnly { get; init; }
}

internal sealed class AdapterContract
{
    public string Protocol { get; init; } = string.Empty;
    public string[] RequestFields { get; init; } = [];
    public string[] ResultFields { get; init; } = [];
    public string[] AllowedOutcomes { get; init; } = [];
    public string Rule { get; init; } = string.Empty;
}

internal sealed record CrashCase(string CaseId, string Operation, string InjectionPoint, string ExpectedInvariant);
internal sealed record MutationCase(string CaseId, string TargetId, string Category, string TestCaseId, string Mutation, string PayloadBase64, int DeclaredLength, string PayloadSha256, string ExpectedDisposition);
internal sealed record SecurityNegativeCase(string CaseId, string Id, string TestCaseId, string ExpectedDisposition, bool SyntheticOnly);
internal sealed record CorpusSummary(string SchemaVersion, int CrashCaseCount, int MutationCaseCount, int SecurityCaseCount, string CorpusSha256);
internal sealed record MaterializedCorpus(HarnessManifest Manifest, IReadOnlyList<CrashCase> CrashCases, IReadOnlyList<MutationCase> MutationCases, IReadOnlyList<SecurityNegativeCase> SecurityCases);
