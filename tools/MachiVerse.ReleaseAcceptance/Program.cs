using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private static int Main(string[] args)
    {
        try
        {
            var root = FindRepositoryRoot(Directory.GetCurrentDirectory());
            var manifestPath = Path.Combine(root, "tests", "release-acceptance-fixtures", "v1", "acceptance-manifest.json");
            var manifestBytes = File.ReadAllBytes(manifestPath);
            var manifest = Deserialize<AcceptanceManifest>(manifestBytes, "INT-03 acceptance manifest");
            ValidateManifest(manifest);

            var command = args.Length == 0 ? "verify" : args[0];
            return command switch
            {
                "verify" => Verify(manifest, manifestBytes),
                "evaluate" when args.Length == 3 => EvaluateFile(manifest, args[1], args[2]),
                "template" when args.Length == 2 => WriteTemplate(manifest, args[1]),
                _ => throw new ArgumentException(
                    "Usage: MachiVerse.ReleaseAcceptance [verify|evaluate <evidence.json> <record.json>|template <evidence.json>]")
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"INT-03 release acceptance FAILED: {ex.Message}");
            return 1;
        }
    }

    private static int Verify(AcceptanceManifest manifest, byte[] manifestBytes)
    {
        SelfTest(manifest);
        Console.WriteLine("INT-03 ReleaseAcceptanceRecordV1 contract verification PASS");
        Console.WriteLine($"Manifest SHA-256: {Sha256Hex(manifestBytes)}");
        Console.WriteLine($"Required suites: {manifest.RequiredSuiteIds.Length}");
        Console.WriteLine($"Required performance profiles: {string.Join(",", manifest.RequiredPerformanceProfiles.Order(StringComparer.Ordinal))}");
        Console.WriteLine($"Minimum soak duration: {manifest.Soak.MinimumDurationSeconds} seconds");
        Console.WriteLine("Short CI is contract validation only; it is not performance.soak.24h evidence.");
        return 0;
    }

    private static int EvaluateFile(AcceptanceManifest manifest, string evidencePath, string recordPath)
    {
        var evidence = Deserialize<ReleaseEvidence>(File.ReadAllBytes(Path.GetFullPath(evidencePath)), "release evidence");
        var evaluation = Evaluate(manifest, evidence);
        var fullRecordPath = Path.GetFullPath(recordPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullRecordPath) ?? Directory.GetCurrentDirectory());
        File.WriteAllText(fullRecordPath, JsonSerializer.Serialize(evaluation.Record, Json) + Environment.NewLine, new UTF8Encoding(false));

        Console.WriteLine($"ReleaseAcceptanceRecordV1 result: {evaluation.Record.Result}");
        foreach (var item in evaluation.IncompleteReasons) Console.WriteLine($"INCOMPLETE: {item}");
        foreach (var item in evaluation.FailureReasons) Console.WriteLine($"FAIL: {item}");
        Console.WriteLine($"Record: {fullRecordPath}");
        return string.Equals(evaluation.Record.Result, "PASS", StringComparison.Ordinal) ? 0 : 2;
    }

    private static int WriteTemplate(AcceptanceManifest manifest, string evidencePath)
    {
        var template = new ReleaseEvidence
        {
            SchemaVersion = manifest.SchemaVersion,
            BuildVersion = "replace-with-build-version",
            SourceCommit = new string('0', 40),
            SchemaRegistryDigest = new string('0', 64),
            AlgorithmRegistryDigest = new string('0', 64),
            ConfigSchemaDigest = new string('0', 64),
            TestSuiteVersion = manifest.TestSuiteVersion,
            PassedTestIds = [],
            SuiteEvidence = [],
            PerformanceReports = [],
            Soak = null,
            DeterminismDigestSummary = new string('0', 64),
            KnownWaivers = [],
            ObservedFailureCodes = [],
        };
        var fullPath = Path.GetFullPath(evidencePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory());
        File.WriteAllText(fullPath, JsonSerializer.Serialize(template, Json) + Environment.NewLine, new UTF8Encoding(false));
        Console.WriteLine($"INCOMPLETE release evidence template written to {fullPath}");
        return 0;
    }

    private static Evaluation Evaluate(AcceptanceManifest manifest, ReleaseEvidence evidence)
    {
        ValidateEvidenceShape(manifest, evidence);
        var incomplete = new SortedSet<string>(StringComparer.Ordinal);
        var failures = new SortedSet<string>(StringComparer.Ordinal);

        if (!string.Equals(evidence.SchemaVersion, manifest.SchemaVersion, StringComparison.Ordinal))
            incomplete.Add("evidence.schema-version-mismatch");
        if (!string.Equals(evidence.TestSuiteVersion, manifest.TestSuiteVersion, StringComparison.Ordinal))
            incomplete.Add("evidence.test-suite-version-mismatch");

        var suites = UniqueBy(evidence.SuiteEvidence, static x => x.SuiteId, "suiteEvidence.suiteId");
        foreach (var requiredSuite in manifest.RequiredSuiteIds)
        {
            if (!suites.TryGetValue(requiredSuite, out var suite))
            {
                incomplete.Add($"suite.missing:{requiredSuite}");
                continue;
            }
            if (!string.Equals(suite.SourceCommit, evidence.SourceCommit, StringComparison.Ordinal))
                incomplete.Add($"suite.source-commit-mismatch:{requiredSuite}");
            if (string.Equals(suite.Status, "failed", StringComparison.Ordinal))
                failures.Add($"suite.failed:{requiredSuite}");
            else if (!string.Equals(suite.Status, "passed", StringComparison.Ordinal))
                incomplete.Add($"suite.not-passed:{requiredSuite}");
            if (string.IsNullOrWhiteSpace(suite.ArtifactRef))
                incomplete.Add($"suite.artifact-ref-missing:{requiredSuite}");
        }

        var passedIds = evidence.PassedTestIds.ToHashSet(StringComparer.Ordinal);
        foreach (var requiredTest in manifest.RequiredTestCaseIds)
            if (!passedIds.Contains(requiredTest)) incomplete.Add($"test.missing:{requiredTest}");

        var reports = UniqueBy(evidence.PerformanceReports, static x => x.ProfileId, "performanceReports.profileId");
        foreach (var profile in manifest.RequiredPerformanceProfiles)
        {
            if (!reports.TryGetValue(profile, out var report))
            {
                incomplete.Add($"performance.missing:{profile}");
                continue;
            }
            if (!string.Equals(report.SourceCommit, evidence.SourceCommit, StringComparison.Ordinal))
                incomplete.Add($"performance.source-commit-mismatch:{profile}");
            if (string.IsNullOrWhiteSpace(report.ReportRef))
                incomplete.Add($"performance.report-ref-missing:{profile}");
            if (!report.Passed) failures.Add($"performance.failed:{profile}");
            foreach (var code in report.FailureCodes)
                failures.Add($"performance.failure-code:{code}");
        }

        if (evidence.Soak is null)
        {
            incomplete.Add("soak.missing:performance.soak.24h");
        }
        else
        {
            var soak = evidence.Soak;
            if (!string.Equals(soak.TestCaseId, manifest.Soak.TestCaseId, StringComparison.Ordinal))
                incomplete.Add("soak.test-case-id-mismatch");
            if (!string.Equals(soak.SourceCommit, evidence.SourceCommit, StringComparison.Ordinal))
                incomplete.Add("soak.source-commit-mismatch");
            if (string.IsNullOrWhiteSpace(soak.ReportRef)) incomplete.Add("soak.report-ref-missing");
            if (soak.DurationSeconds < manifest.Soak.MinimumDurationSeconds)
                incomplete.Add($"soak.duration-too-short:{soak.DurationSeconds}");
            if (!soak.Passed) failures.Add("performance.soak.24h");
            if (!soak.ParallelVerifierDigestMatched) failures.Add("determinism.divergence");
            if (soak.MaxPostWarmupMemoryGrowthPercent > manifest.Soak.MaxPostWarmupMemoryGrowthPercent)
                failures.Add("soak.memory-growth");
            if (soak.AcceptedOperationLoss != 0) failures.Add("operation.accepted-loss");
            if (!soak.HistoryAuditChainValid) failures.Add("soak.history-audit-chain-invalid");
            if (!soak.NoUnrecoverableQueueDeadlock) failures.Add("soak.queue-deadlock");
        }

        var nonWaivable = manifest.NonWaivableFailureCodes.ToHashSet(StringComparer.Ordinal);
        foreach (var code in evidence.ObservedFailureCodes)
        {
            failures.Add(code);
            if (nonWaivable.Contains(code)) failures.Add($"non-waivable:{code}");
        }
        foreach (var waiver in evidence.KnownWaivers)
            if (nonWaivable.Contains(waiver.Code)) failures.Add($"waiver.forbidden:{waiver.Code}");

        var result = failures.Count != 0 ? "FAIL" : incomplete.Count != 0 ? "INCOMPLETE" : "PASS";
        var record = new ReleaseAcceptanceRecordV1
        {
            BuildVersion = evidence.BuildVersion,
            SourceCommit = evidence.SourceCommit,
            SchemaRegistryDigest = evidence.SchemaRegistryDigest,
            AlgorithmRegistryDigest = evidence.AlgorithmRegistryDigest,
            ConfigSchemaDigest = evidence.ConfigSchemaDigest,
            TestSuiteVersion = evidence.TestSuiteVersion,
            PassedTestIds = evidence.PassedTestIds.Order(StringComparer.Ordinal).ToArray(),
            PerformanceReportRefs = evidence.PerformanceReports
                .Select(static x => x.ReportRef)
                .Where(static x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            DeterminismDigestSummary = evidence.DeterminismDigestSummary,
            KnownWaivers = evidence.KnownWaivers.OrderBy(static x => x.Code, StringComparer.Ordinal).ToArray(),
            Result = result,
        };
        return new Evaluation(record, incomplete.ToArray(), failures.ToArray());
    }

    private static void SelfTest(AcceptanceManifest manifest)
    {
        var valid = BuildValidFixture(manifest);
        RequireResult(Evaluate(manifest, valid), "PASS", "complete evidence");

        var shortSoak = Clone(valid);
        shortSoak.Soak!.DurationSeconds = manifest.Soak.MinimumDurationSeconds - 1;
        RequireResult(Evaluate(manifest, shortSoak), "INCOMPLETE", "short soak");

        var missingSuite = Clone(valid);
        missingSuite.SuiteEvidence = missingSuite.SuiteEvidence.Skip(1).ToArray();
        RequireResult(Evaluate(manifest, missingSuite), "INCOMPLETE", "missing required suite");

        var staleSuite = Clone(valid);
        staleSuite.SuiteEvidence[0].SourceCommit = new string('2', 40);
        RequireResult(Evaluate(manifest, staleSuite), "INCOMPLETE", "wrong-commit suite evidence");

        var missingPerformance = Clone(valid);
        missingPerformance.PerformanceReports = missingPerformance.PerformanceReports.Skip(1).ToArray();
        RequireResult(Evaluate(manifest, missingPerformance), "INCOMPLETE", "missing performance report");

        var nonWaivable = Clone(valid);
        var forbidden = manifest.NonWaivableFailureCodes[0];
        nonWaivable.ObservedFailureCodes = [forbidden];
        nonWaivable.KnownWaivers = [new WaiverEvidence { Code = forbidden, Reason = "must not override" }];
        RequireResult(Evaluate(manifest, nonWaivable), "FAIL", "non-waivable failure with waiver");

        var loss = Clone(valid);
        loss.Soak!.AcceptedOperationLoss = 1;
        RequireResult(Evaluate(manifest, loss), "FAIL", "accepted Operation loss");
    }

    private static ReleaseEvidence BuildValidFixture(AcceptanceManifest manifest)
    {
        const string commit = "1111111111111111111111111111111111111111";
        return new ReleaseEvidence
        {
            SchemaVersion = manifest.SchemaVersion,
            BuildVersion = "int03-contract-fixture",
            SourceCommit = commit,
            SchemaRegistryDigest = new string('a', 64),
            AlgorithmRegistryDigest = new string('b', 64),
            ConfigSchemaDigest = new string('c', 64),
            TestSuiteVersion = manifest.TestSuiteVersion,
            PassedTestIds = manifest.RequiredTestCaseIds.ToArray(),
            SuiteEvidence = manifest.RequiredSuiteIds.Select(id => new SuiteEvidence
            {
                SuiteId = id,
                SourceCommit = commit,
                Status = "passed",
                ArtifactRef = $"fixture://{id}",
            }).ToArray(),
            PerformanceReports = manifest.RequiredPerformanceProfiles.Select(id => new PerformanceReportEvidence
            {
                ProfileId = id,
                SourceCommit = commit,
                ReportRef = $"fixture://{id}",
                Passed = true,
                FailureCodes = [],
            }).ToArray(),
            Soak = new SoakEvidence
            {
                TestCaseId = manifest.Soak.TestCaseId,
                SourceCommit = commit,
                ReportRef = "fixture://performance.soak.24h",
                DurationSeconds = manifest.Soak.MinimumDurationSeconds,
                Passed = true,
                ParallelVerifierDigestMatched = true,
                MaxPostWarmupMemoryGrowthPercent = manifest.Soak.MaxPostWarmupMemoryGrowthPercent,
                AcceptedOperationLoss = 0,
                HistoryAuditChainValid = true,
                NoUnrecoverableQueueDeadlock = true,
            },
            DeterminismDigestSummary = new string('d', 64),
            KnownWaivers = [],
            ObservedFailureCodes = [],
        };
    }

    private static void ValidateManifest(AcceptanceManifest manifest)
    {
        RequireEqual(manifest.SchemaVersion, "1.0", "manifest schemaVersion");
        RequireEqual(manifest.TestSuiteVersion, "p4-08.v1", "testSuiteVersion");
        RequireUniqueNonEmpty(manifest.RequiredSuiteIds, "requiredSuiteIds");
        RequireUniqueNonEmpty(manifest.RequiredTestCaseIds, "requiredTestCaseIds");
        RequireExactSet(manifest.RequiredPerformanceProfiles,
            ["perf.reference.v1", "perf.persistence.v1", "perf.publication.v1"],
            "requiredPerformanceProfiles");
        RequireEqual(manifest.Soak.TestCaseId, "performance.soak.24h", "soak.testCaseId");
        if (manifest.Soak.MinimumDurationSeconds < 86_400)
            throw new InvalidDataException("INT-03 soak minimum cannot be shorter than 24 wall-clock hours.");
        if (manifest.Soak.MaxPostWarmupMemoryGrowthPercent > 10.0)
            throw new InvalidDataException("INT-03 soak memory-growth guard cannot exceed 10%.");
        if (!manifest.Soak.ParallelVerifierDigestRequired || !manifest.Soak.NoAcceptedOperationLoss ||
            !manifest.Soak.HistoryAuditChainValid || !manifest.Soak.NoUnrecoverableQueueDeadlock)
            throw new InvalidDataException("INT-03 soak authority/safety requirements are incomplete.");

        var expectedNonWaivable = new[]
        {
            "determinism.divergence",
            "operation.accepted-loss",
            "operation.double-apply",
            "persistence.history-corruption-undetected",
            "transaction.partial-commit",
            "security.unauthorized-mutation-bypass",
            "security.credential-leakage",
            "protocol.required-compatibility-failure",
            "world.view-camera-authority",
            "world.telemetry-authority",
            "world.wall-clock-authority",
        };
        RequireExactSet(manifest.NonWaivableFailureCodes, expectedNonWaivable, "nonWaivableFailureCodes");

        var expectedFields = new[]
        {
            "build_version", "source_commit", "schema_registry_digest", "algorithm_registry_digest",
            "config_schema_digest", "test_suite_version", "passed_test_ids", "performance_report_refs",
            "determinism_digest_summary", "known_waivers", "result"
        };
        if (!manifest.ReleaseRecordFields.SequenceEqual(expectedFields, StringComparer.Ordinal))
            throw new InvalidDataException("ReleaseAcceptanceRecordV1 field contract drifted from P4-08.");
    }

    private static void ValidateEvidenceShape(AcceptanceManifest manifest, ReleaseEvidence evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence.BuildVersion) || evidence.BuildVersion.Length > 128)
            throw new InvalidDataException("buildVersion must be non-empty and <=128 characters.");
        RequireLowerHex(evidence.SourceCommit, 40, "sourceCommit", allowAllZero: false);
        RequireLowerHex(evidence.SchemaRegistryDigest, 64, "schemaRegistryDigest", allowAllZero: false);
        RequireLowerHex(evidence.AlgorithmRegistryDigest, 64, "algorithmRegistryDigest", allowAllZero: false);
        RequireLowerHex(evidence.ConfigSchemaDigest, 64, "configSchemaDigest", allowAllZero: false);
        RequireLowerHex(evidence.DeterminismDigestSummary, 64, "determinismDigestSummary", allowAllZero: false);
        RequireUniqueNonEmpty(evidence.PassedTestIds, "passedTestIds");
        RequireUniqueNonEmpty(evidence.ObservedFailureCodes, "observedFailureCodes", allowEmpty: true);

        foreach (var suite in evidence.SuiteEvidence)
        {
            if (string.IsNullOrWhiteSpace(suite.SuiteId)) throw new InvalidDataException("suiteId is required.");
            RequireLowerHex(suite.SourceCommit, 40, $"suite sourceCommit:{suite.SuiteId}", allowAllZero: false);
            if (suite.Status is not ("passed" or "failed" or "incomplete"))
                throw new InvalidDataException($"Suite {suite.SuiteId} status must be passed/failed/incomplete.");
        }
        foreach (var report in evidence.PerformanceReports)
        {
            if (string.IsNullOrWhiteSpace(report.ProfileId)) throw new InvalidDataException("performance profileId is required.");
            RequireLowerHex(report.SourceCommit, 40, $"performance sourceCommit:{report.ProfileId}", allowAllZero: false);
            RequireUniqueNonEmpty(report.FailureCodes, $"failureCodes:{report.ProfileId}", allowEmpty: true);
        }
        if (evidence.Soak is { } soak)
        {
            RequireLowerHex(soak.SourceCommit, 40, "soak sourceCommit", allowAllZero: false);
            if (soak.DurationSeconds < 0) throw new InvalidDataException("soak durationSeconds cannot be negative.");
            if (soak.MaxPostWarmupMemoryGrowthPercent < 0) throw new InvalidDataException("soak memory growth cannot be negative.");
            if (soak.AcceptedOperationLoss < 0) throw new InvalidDataException("soak acceptedOperationLoss cannot be negative.");
        }
        foreach (var waiver in evidence.KnownWaivers)
        {
            if (string.IsNullOrWhiteSpace(waiver.Code) || string.IsNullOrWhiteSpace(waiver.Reason))
                throw new InvalidDataException("known waiver requires code and reason.");
        }
    }

    private static Dictionary<string, T> UniqueBy<T>(IEnumerable<T> values, Func<T, string> keySelector, string name)
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            var key = keySelector(value);
            if (string.IsNullOrWhiteSpace(key) || !result.TryAdd(key, value))
                throw new InvalidDataException($"{name} contains an empty or duplicate key: '{key}'.");
        }
        return result;
    }

    private static void RequireUniqueNonEmpty(string[] values, string name, bool allowEmpty = false)
    {
        if (!allowEmpty && values.Length == 0) throw new InvalidDataException($"{name} cannot be empty.");
        if (values.Any(string.IsNullOrWhiteSpace) || values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new InvalidDataException($"{name} contains empty or duplicate values.");
    }

    private static void RequireExactSet(string[] actual, string[] expected, string name)
    {
        if (actual.Length != expected.Length || !actual.ToHashSet(StringComparer.Ordinal).SetEquals(expected))
            throw new InvalidDataException($"{name} does not match the canonical set.");
    }

    private static void RequireLowerHex(string value, int length, string name, bool allowAllZero)
    {
        if (value.Length != length || value.Any(static c => !(c is >= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new InvalidDataException($"{name} must be {length} lowercase hexadecimal characters.");
        if (!allowAllZero && value.All(static c => c == '0'))
            throw new InvalidDataException($"{name} cannot be all zero.");
    }

    private static void RequireEqual(string actual, string expected, string name)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidDataException($"{name} must be '{expected}', found '{actual}'.");
    }

    private static void RequireResult(Evaluation evaluation, string expected, string scenario)
    {
        if (!string.Equals(evaluation.Record.Result, expected, StringComparison.Ordinal))
            throw new InvalidDataException($"INT-03 self-test '{scenario}' expected {expected}, found {evaluation.Record.Result}.");
    }

    private static T Clone<T>(T value) where T : class
        => Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value, Json), "self-test clone");

    private static T Deserialize<T>(byte[] bytes, string name)
        => JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new InvalidDataException($"{name} decoded to null.");

    private static string FindRepositoryRoot(string start)
    {
        var current = new DirectoryInfo(start);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "global.json")) && Directory.Exists(Path.Combine(current.FullName, "tests")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate MachiVerse repository root.");
    }

    private static string Sha256Hex(ReadOnlySpan<byte> bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    internal sealed class AcceptanceManifest
    {
        public string SchemaVersion { get; set; } = "";
        public string TestSuiteVersion { get; set; } = "";
        public string[] RequiredSuiteIds { get; set; } = [];
        public string[] RequiredTestCaseIds { get; set; } = [];
        public string[] RequiredPerformanceProfiles { get; set; } = [];
        public SoakContract Soak { get; set; } = new();
        public string[] NonWaivableFailureCodes { get; set; } = [];
        public string[] ReleaseRecordFields { get; set; } = [];
    }

    internal sealed class SoakContract
    {
        public string TestCaseId { get; set; } = "";
        public long MinimumDurationSeconds { get; set; }
        public double MaxPostWarmupMemoryGrowthPercent { get; set; }
        public bool ParallelVerifierDigestRequired { get; set; }
        public bool NoAcceptedOperationLoss { get; set; }
        public bool HistoryAuditChainValid { get; set; }
        public bool NoUnrecoverableQueueDeadlock { get; set; }
    }

    internal sealed class ReleaseEvidence
    {
        public string SchemaVersion { get; set; } = "";
        public string BuildVersion { get; set; } = "";
        public string SourceCommit { get; set; } = "";
        public string SchemaRegistryDigest { get; set; } = "";
        public string AlgorithmRegistryDigest { get; set; } = "";
        public string ConfigSchemaDigest { get; set; } = "";
        public string TestSuiteVersion { get; set; } = "";
        public string[] PassedTestIds { get; set; } = [];
        public SuiteEvidence[] SuiteEvidence { get; set; } = [];
        public PerformanceReportEvidence[] PerformanceReports { get; set; } = [];
        public SoakEvidence? Soak { get; set; }
        public string DeterminismDigestSummary { get; set; } = "";
        public WaiverEvidence[] KnownWaivers { get; set; } = [];
        public string[] ObservedFailureCodes { get; set; } = [];
    }

    internal sealed class SuiteEvidence
    {
        public string SuiteId { get; set; } = "";
        public string SourceCommit { get; set; } = "";
        public string Status { get; set; } = "";
        public string ArtifactRef { get; set; } = "";
    }

    internal sealed class PerformanceReportEvidence
    {
        public string ProfileId { get; set; } = "";
        public string SourceCommit { get; set; } = "";
        public string ReportRef { get; set; } = "";
        public bool Passed { get; set; }
        public string[] FailureCodes { get; set; } = [];
    }

    internal sealed class SoakEvidence
    {
        public string TestCaseId { get; set; } = "";
        public string SourceCommit { get; set; } = "";
        public string ReportRef { get; set; } = "";
        public long DurationSeconds { get; set; }
        public bool Passed { get; set; }
        public bool ParallelVerifierDigestMatched { get; set; }
        public double MaxPostWarmupMemoryGrowthPercent { get; set; }
        public int AcceptedOperationLoss { get; set; }
        public bool HistoryAuditChainValid { get; set; }
        public bool NoUnrecoverableQueueDeadlock { get; set; }
    }

    internal sealed class WaiverEvidence
    {
        public string Code { get; set; } = "";
        public string Reason { get; set; } = "";
    }

    internal sealed class ReleaseAcceptanceRecordV1
    {
        [JsonPropertyName("build_version")]
        public string BuildVersion { get; set; } = "";

        [JsonPropertyName("source_commit")]
        public string SourceCommit { get; set; } = "";

        [JsonPropertyName("schema_registry_digest")]
        public string SchemaRegistryDigest { get; set; } = "";

        [JsonPropertyName("algorithm_registry_digest")]
        public string AlgorithmRegistryDigest { get; set; } = "";

        [JsonPropertyName("config_schema_digest")]
        public string ConfigSchemaDigest { get; set; } = "";

        [JsonPropertyName("test_suite_version")]
        public string TestSuiteVersion { get; set; } = "";

        [JsonPropertyName("passed_test_ids")]
        public string[] PassedTestIds { get; set; } = [];

        [JsonPropertyName("performance_report_refs")]
        public string[] PerformanceReportRefs { get; set; } = [];

        [JsonPropertyName("determinism_digest_summary")]
        public string DeterminismDigestSummary { get; set; } = "";

        [JsonPropertyName("known_waivers")]
        public WaiverEvidence[] KnownWaivers { get; set; } = [];

        [JsonPropertyName("result")]
        public string Result { get; set; } = "";
    }

    private sealed record Evaluation(
        ReleaseAcceptanceRecordV1 Record,
        string[] IncompleteReasons,
        string[] FailureReasons);
}
