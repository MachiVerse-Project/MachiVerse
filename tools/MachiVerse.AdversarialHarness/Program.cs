using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MachiVerse.AdversarialHarness;

internal static class Program
{
    private const int EnvelopeLimitBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly string[] CrashOperations =
    [
        "audit-append",
        "migration-generation-switch",
        "operation-acceptance",
        "operation-scheduling",
        "snapshot-commit",
        "transition-commit"
    ];

    private static readonly string[] CrashPoints =
    [
        "before-db-begin",
        "before-fsync-or-commit",
        "before-response-or-publication",
        "immediately-after-commit",
        "mid-write"
    ];

    private static readonly string[] FuzzCategories = ["law-ast", "protobuf", "snapshot", "toml", "websocket"];
    private static readonly HashSet<string> Mutations = new(StringComparer.Ordinal)
    {
        "append-zero", "duplicate-prefix", "empty", "flip-first-bit", "oversize-metadata", "truncate-half"
    };
    private static readonly HashSet<string> SecurityDispositions = new(StringComparer.Ordinal)
    {
        "deny", "no-auto-release", "no-secret", "tamper-detected"
    };

    private static int Main(string[] args)
    {
        try
        {
            var command = args.Length == 0 ? "verify" : args[0];
            var root = FindRepositoryRoot(Directory.GetCurrentDirectory());
            var path = Path.Combine(root, "tests", "adversarial-fixtures", "v1", "harness-manifest.json");
            var raw = File.ReadAllText(path, Encoding.UTF8);
            var manifest = JsonSerializer.Deserialize<HarnessManifest>(raw, Json)
                ?? throw new InvalidDataException("QA-03 manifest decoded to null.");

            ValidateManifest(manifest, raw);
            var corpus = BuildCorpus(manifest);
            ValidateCorpus(corpus);

            if (command == "verify")
            {
                var repeat = BuildCorpus(manifest);
                var digest = CorpusDigest(corpus);
                if (!string.Equals(digest, CorpusDigest(repeat), StringComparison.Ordinal))
                    throw new InvalidDataException("Repeated materialization produced a different corpus digest.");

                Console.WriteLine("QA-03 adversarial harness verification PASS");
                Console.WriteLine($"Crash cases: {corpus.CrashCases.Count}");
                Console.WriteLine($"Fuzz mutation cases: {corpus.MutationCases.Count}");
                Console.WriteLine($"Security negative cases: {corpus.SecurityCases.Count}");
                Console.WriteLine($"Corpus SHA-256: {digest}");
                return 0;
            }

            if (command == "materialize")
            {
                var output = args.Length > 1
                    ? Path.GetFullPath(args[1])
                    : Path.Combine(root, "artifacts", "qa-03-adversarial-corpus");
                Materialize(corpus, output);
                return 0;
            }

            throw new ArgumentException("Usage: MachiVerse.AdversarialHarness [verify|materialize <output-directory>]");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"QA-03 harness FAILED: {ex.Message}");
            return 1;
        }
    }

    private static void ValidateManifest(HarnessManifest manifest, string raw)
    {
        if (manifest.SchemaVersion != "1.0")
            throw new InvalidDataException("Unsupported QA-03 manifest schemaVersion.");
        ValidateAsciiToken(manifest.MutationSeed, "mutationSeed");
        RequireExactCanonical(manifest.CrashOperations, CrashOperations, "crashOperations");
        RequireExactCanonical(manifest.CrashPoints, CrashPoints, "crashPoints");

        RequireCanonical(manifest.FuzzTargets.Select(x => x.Id), "fuzzTargets.id");
        var categories = manifest.FuzzTargets
            .Select(x => x.Category)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        if (!categories.SequenceEqual(FuzzCategories, StringComparer.Ordinal))
            throw new InvalidDataException("Fuzz corpus must exactly cover law-ast/protobuf/snapshot/toml/websocket.");

        foreach (var target in manifest.FuzzTargets)
        {
            ValidateAsciiToken(target.Id, "fuzz target id");
            ValidateTestCaseId(target.TestCaseId);
            if (target.SeedText.Length == 0)
                throw new InvalidDataException($"Fuzz target '{target.Id}' has an empty seed.");
            if (target.ExpectedDisposition != "reject-or-explicit-failure")
                throw new InvalidDataException($"Fuzz target '{target.Id}' is not fail-closed.");
            RequireCanonical(target.Mutations, $"{target.Id}.mutations");
            if (target.Mutations.Length < 4 || target.Mutations.Any(x => !Mutations.Contains(x)))
                throw new InvalidDataException($"Fuzz target '{target.Id}' has an invalid mutation plan.");
        }

        RequireCanonical(manifest.SecurityCases.Select(x => x.Id), "securityCases.id");
        foreach (var item in manifest.SecurityCases)
        {
            ValidateAsciiToken(item.Id, "security case id");
            ValidateTestCaseId(item.TestCaseId);
            if (!item.SyntheticOnly)
                throw new InvalidDataException($"Security case '{item.Id}' must be synthetic-only.");
            if (!SecurityDispositions.Contains(item.ExpectedDisposition))
                throw new InvalidDataException($"Security case '{item.Id}' has an unsupported disposition.");
        }

        ValidateAdapter(manifest.AdapterContract);
        RejectSecretMaterial(raw);
    }

    private static MaterializedCorpus BuildCorpus(HarnessManifest manifest)
    {
        var crash = new List<CrashCase>();
        foreach (var operation in manifest.CrashOperations)
        foreach (var point in manifest.CrashPoints)
            crash.Add(new CrashCase(
                CaseId("crash", operation, point),
                operation,
                point,
                point is "immediately-after-commit" or "before-response-or-publication"
                    ? "durable-fact-recovered-without-duplicate-or-false-negative"
                    : "pre-transition-authority-preserved-no-partial-durable-success"));

        var fuzz = new List<MutationCase>();
        foreach (var target in manifest.FuzzTargets)
        {
            var seed = Encoding.UTF8.GetBytes(target.SeedText);
            foreach (var mutation in target.Mutations)
            {
                var bytes = Mutate(seed, mutation, out var declaredLength);
                fuzz.Add(new MutationCase(
                    CaseId("fuzz", target.Id, mutation),
                    target.Id,
                    target.Category,
                    target.TestCaseId,
                    mutation,
                    Convert.ToBase64String(bytes),
                    declaredLength,
                    Sha256(bytes),
                    target.ExpectedDisposition));
            }
        }

        var security = manifest.SecurityCases.Select(item => new SecurityNegativeCase(
            CaseId("security", item.Id), item.Id, item.TestCaseId, item.ExpectedDisposition, item.SyntheticOnly)).ToArray();
        return new MaterializedCorpus(manifest, crash, fuzz, security);
    }

    private static void ValidateCorpus(MaterializedCorpus corpus)
    {
        if (corpus.CrashCases.Count != 30)
            throw new InvalidDataException("Crash matrix must be exactly 6 operations x 5 injection points = 30 cases.");
        Unique(corpus.CrashCases.Select(x => x.CaseId), "crash case ids");
        Unique(corpus.MutationCases.Select(x => x.CaseId), "mutation case ids");
        Unique(corpus.SecurityCases.Select(x => x.CaseId), "security case ids");

        foreach (var item in corpus.MutationCases)
        {
            var bytes = Convert.FromBase64String(item.PayloadBase64);
            if (item.Mutation == "oversize-metadata")
            {
                if (item.DeclaredLength <= EnvelopeLimitBytes)
                    throw new InvalidDataException("oversize-metadata did not cross the 8 MiB limit.");
            }
            else if (item.DeclaredLength != bytes.Length)
            {
                throw new InvalidDataException($"Declared length mismatch in '{item.CaseId}'.");
            }

            if (item.PayloadSha256 != Sha256(bytes))
                throw new InvalidDataException($"Payload digest mismatch in '{item.CaseId}'.");
        }
    }

    private static byte[] Mutate(byte[] seed, string mutation, out int declaredLength)
    {
        byte[] result = mutation switch
        {
            "append-zero" => [.. seed, 0],
            "duplicate-prefix" => [.. seed[..Math.Min(8, seed.Length)], .. seed],
            "empty" => [],
            "flip-first-bit" => FlipFirstBit(seed),
            "oversize-metadata" => seed.ToArray(),
            "truncate-half" => seed[..Math.Max(1, seed.Length / 2)],
            _ => throw new InvalidDataException($"Unsupported mutation '{mutation}'.")
        };
        declaredLength = mutation == "oversize-metadata" ? EnvelopeLimitBytes + 1 : result.Length;
        return result;
    }

    private static byte[] FlipFirstBit(byte[] seed)
    {
        var result = seed.ToArray();
        if (result.Length == 0) return [1];
        result[0] ^= 1;
        return result;
    }

    private static void ValidateAdapter(AdapterContract adapter)
    {
        if (adapter.Protocol != "jsonl") throw new InvalidDataException("Target adapter protocol must be jsonl.");
        RequireCanonical(adapter.RequestFields, "adapter.requestFields");
        RequireCanonical(adapter.ResultFields, "adapter.resultFields");
        RequireCanonical(adapter.AllowedOutcomes, "adapter.allowedOutcomes");
        RequireExactSet(adapter.RequestFields, ["caseId", "category", "declaredLength", "expectedDisposition", "payloadBase64"], "adapter.requestFields");
        RequireExactSet(adapter.ResultFields, ["caseId", "diagnosticDigest", "outcome", "reasonCode"], "adapter.resultFields");
        if (string.IsNullOrWhiteSpace(adapter.Rule)) throw new InvalidDataException("Target adapter authority rule is missing.");
    }

    private static void RejectSecretMaterial(string raw)
    {
        string[] patterns =
        [
            "-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----",
            "(?i)(?:access_token|refresh_token|client_secret|authorization_code)\\s*[:=]\\s*[\\\"']?[A-Za-z0-9._~-]{12,}",
            "__Host-mv_session=[^;\\s]{8,}",
            "eyJ[A-Za-z0-9_-]{8,}\\.[A-Za-z0-9_-]{8,}\\.[A-Za-z0-9_-]{8,}"
        ];
        if (patterns.Any(pattern => Regex.IsMatch(raw, pattern, RegexOptions.CultureInvariant)))
            throw new InvalidDataException("Adversarial corpus appears to contain real credential/private-key material.");
    }

    private static void Materialize(MaterializedCorpus corpus, string output)
    {
        Directory.CreateDirectory(output);
        Write(Path.Combine(output, "crash-matrix.json"), corpus.CrashCases);
        Write(Path.Combine(output, "mutation-cases.json"), corpus.MutationCases);
        Write(Path.Combine(output, "security-negative-corpus.json"), corpus.SecurityCases);
        Write(Path.Combine(output, "target-adapter-contract.json"), corpus.Manifest.AdapterContract);
        Write(Path.Combine(output, "corpus-summary.json"), new CorpusSummary(
            corpus.Manifest.SchemaVersion, corpus.CrashCases.Count, corpus.MutationCases.Count,
            corpus.SecurityCases.Count, CorpusDigest(corpus)));
        Console.WriteLine($"QA-03 corpus materialized at {output}");
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
            adapterContract = corpus.Manifest.AdapterContract
        };
        return Sha256(JsonSerializer.SerializeToUtf8Bytes(canonical));
    }

    private static string CaseId(params string[] parts)
    {
        var semantic = string.Join("\0", parts);
        return $"qa03.{string.Join('.', parts)}.{Sha256(Encoding.UTF8.GetBytes(semantic))[..16]}";
    }

    private static void ValidateAsciiToken(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(ch => ch > 0x7f || char.IsControl(ch) || char.IsWhiteSpace(ch)))
            throw new InvalidDataException($"{field} must be non-empty canonical ASCII token text.");
    }

    private static void ValidateTestCaseId(string value)
    {
        if (!Regex.IsMatch(value, "^[a-z0-9][a-z0-9.-]*$", RegexOptions.CultureInvariant))
            throw new InvalidDataException($"Invalid TestCaseId '{value}'.");
    }

    private static void RequireExactCanonical(string[] actual, string[] expected, string field)
    {
        RequireCanonical(actual, field);
        RequireExactSet(actual, expected, field);
    }

    private static void RequireExactSet(IEnumerable<string> actual, IEnumerable<string> expected, string field)
    {
        var a = actual.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var e = expected.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (!a.SequenceEqual(e, StringComparer.Ordinal))
            throw new InvalidDataException($"{field} does not match the required set.");
    }

    private static void RequireCanonical(IEnumerable<string> values, string field)
    {
        var a = values.ToArray();
        var s = a.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (!a.SequenceEqual(s, StringComparer.Ordinal) || a.Distinct(StringComparer.Ordinal).Count() != a.Length)
            throw new InvalidDataException($"{field} must be unique ordinal-ascending.");
    }

    private static void Unique(IEnumerable<string> values, string field)
    {
        var a = values.ToArray();
        if (a.Distinct(StringComparer.Ordinal).Count() != a.Length)
            throw new InvalidDataException($"Duplicate {field}.");
    }

    private static string Sha256(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static void Write<T>(string path, T value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Json) + Environment.NewLine, new UTF8Encoding(false));

    private static string FindRepositoryRoot(string start)
    {
        for (var current = new DirectoryInfo(start); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "tests", "adversarial-fixtures", "v1", "harness-manifest.json")))
                return current.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate QA-03 manifest from current directory.");
    }
}

internal sealed class HarnessManifest
{
    public string SchemaVersion { get; init; } = "";
    public string MutationSeed { get; init; } = "";
    public string[] CrashOperations { get; init; } = [];
    public string[] CrashPoints { get; init; } = [];
    public FuzzTarget[] FuzzTargets { get; init; } = [];
    public SecurityCase[] SecurityCases { get; init; } = [];
    public AdapterContract AdapterContract { get; init; } = new();
}

internal sealed class FuzzTarget
{
    public string Id { get; init; } = "";
    public string Category { get; init; } = "";
    public string TestCaseId { get; init; } = "";
    public string SeedText { get; init; } = "";
    public string[] Mutations { get; init; } = [];
    public string ExpectedDisposition { get; init; } = "";
}

internal sealed class SecurityCase
{
    public string Id { get; init; } = "";
    public string TestCaseId { get; init; } = "";
    public string ExpectedDisposition { get; init; } = "";
    public bool SyntheticOnly { get; init; }
}

internal sealed class AdapterContract
{
    public string Protocol { get; init; } = "";
    public string[] RequestFields { get; init; } = [];
    public string[] ResultFields { get; init; } = [];
    public string[] AllowedOutcomes { get; init; } = [];
    public string Rule { get; init; } = "";
}

internal sealed record CrashCase(string CaseId, string Operation, string InjectionPoint, string ExpectedInvariant);
internal sealed record MutationCase(string CaseId, string TargetId, string Category, string TestCaseId, string Mutation, string PayloadBase64, int DeclaredLength, string PayloadSha256, string ExpectedDisposition);
internal sealed record SecurityNegativeCase(string CaseId, string Id, string TestCaseId, string ExpectedDisposition, bool SyntheticOnly);
internal sealed record CorpusSummary(string SchemaVersion, int CrashCaseCount, int MutationCaseCount, int SecurityCaseCount, string CorpusSha256);
internal sealed record MaterializedCorpus(HarnessManifest Manifest, IReadOnlyList<CrashCase> CrashCases, IReadOnlyList<MutationCase> MutationCases, IReadOnlyList<SecurityNegativeCase> SecurityCases);
