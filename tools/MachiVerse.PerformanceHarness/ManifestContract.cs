using System.Globalization;
using System.Text.Json.Nodes;

namespace MachiVerse.PerformanceHarness;

internal static partial class Program
{
    private static void ValidateManifest(JsonNode manifest, ReadOnlySpan<byte> manifestBytes)
    {
        var actualDigest = Sha256Hex(manifestBytes);
        if (!string.Equals(actualDigest, ExpectedManifestSha256, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"QA-04 canonical manifest digest changed: expected {ExpectedManifestSha256}, found {actualDigest}.");

        RequireString(GetString(manifest, "schemaVersion"), "1.0", "schemaVersion");
        RequireString(GetString(manifest, "benchmarkProfileId"), "perf.reference.v1", "benchmarkProfileId");
        RequireString(GetString(manifest, "worldSeed"), ExpectedWorldSeed, "worldSeed");
        RequireExactSet(ReadIntArray(manifest, "workerCounts"), new[] { 1, 4, 8, 16 }, "workerCounts");
        RequireEqual(GetInt(manifest, "processRunsPerWorker"), 3, "processRunsPerWorker");
        RequireString(GetString(manifest, "loggingLevel"), "warn", "loggingLevel");
        RequireEqual(GetInt(manifest, "warmupSteps"), 9_000, "warmupSteps");
        RequireEqual(GetInt(manifest, "measurementSteps"), 18_000, "measurementSteps");
        RequireTrue(GetBool(manifest, "cooldownSnapshotDrainExcluded"), "cooldownSnapshotDrainExcluded");
        RequireTrue(GetBool(manifest, "normalSnapshotDuringMeasurementRequired"), "normalSnapshotDuringMeasurementRequired");

        RequireEqual(GetInt(manifest, "referenceLoad", "initialWorldPopulation", "residentPersistentIdentity"), 1_000_000, "reference resident count");
        RequireEqual(GetInt(manifest, "referenceLoad", "operationLoad", "steadyPerStep"), 5_000, "steady Operation load");
        RequireEqual(GetInt(manifest, "referenceLoad", "operationLoad", "burstEverySteps"), 900, "burst cadence");
        RequireEqual(GetInt(manifest, "referenceLoad", "operationLoad", "burstOperations"), 50_000, "burst Operation load");
        RequireEqual(GetInt(manifest, "referenceLoad", "crossDomainTransactions", "steadyActiveTarget"), 10_000, "active transaction target");
        RequireEqual(GetInt(manifest, "referenceLoad", "detailTransitionLoad", "everySteps"), 300, "detail transition cadence");
        ValidatePercentTotal(manifest, "referenceLoad", "residentActivityPercent");
        ValidatePercentTotal(manifest, "referenceLoad", "operationLoad", "familyPercent");
        ValidatePercentTotal(manifest, "referenceLoad", "crossDomainTransactions", "kindPercent");
        ValidatePercentTotal(manifest, "referenceLoad", "physicalCollisionPercent");

        RequireString(GetString(manifest, "persistenceProfile", "profileId"), "perf.persistence.v1", "persistence profile");
        RequireEqual(GetInt(manifest, "persistenceProfile", "compressedSnapshotGiB"), 16, "persistence snapshot size");
        RequireTrue(GetBool(manifest, "persistenceProfile", "noDurableFactLoss"), "persistence no durable fact loss");
        RequireTrue(GetBool(manifest, "persistenceProfile", "noUncommittedCandidatePublication"), "persistence no candidate publication");

        RequireString(GetString(manifest, "publicationProfile", "profileId"), "perf.publication.v1", "publication profile");
        RequireEqual(GetInt(manifest, "publicationProfile", "gatewayCount"), 1, "publication Gateway count");
        RequireEqual(GetInt(manifest, "publicationProfile", "viewSubscribers"), 100, "publication View count");
        RequireEqual(GetInt(manifest, "publicationProfile", "slowConsumers"), 10, "publication slow consumers");
        RequireTrue(GetBool(manifest, "publicationProfile", "slowConsumersMustNotBlockCustodyOrResult"), "slow consumer isolation");
        RequireTrue(GetBool(manifest, "publicationProfile", "continuityAfterCoalesceResyncRequired"), "publication continuity");

        RequireString(GetString(manifest, "soakProfile", "testCaseId"), "performance.soak.24h", "soak TestCaseId");
        RequireEqual(GetInt(manifest, "soakProfile", "durationHours"), 24, "soak duration");
        RequireEqual(GetInt(manifest, "soakProfile", "gatewayReconnectFailoverIntervalMinutes"), 30, "soak Gateway churn cadence");
        RequireEqual(GetInt(manifest, "soakProfile", "maxPostWarmupMemoryGrowthPercent"), 10, "soak memory growth guard");
        RequireTrue(GetBool(manifest, "soakProfile", "periodicSnapshotRecoveryCheckpoints"), "soak snapshot/recovery checkpoints");
        RequireTrue(GetBool(manifest, "soakProfile", "viewChurnAndSlowConsumerLoad"), "soak View churn");
        RequireTrue(GetBool(manifest, "soakProfile", "parallelVerifierDigestRequired"), "soak parallel verifier");
        RequireTrue(GetBool(manifest, "soakProfile", "noAcceptedOperationLoss"), "soak no operation loss");
        RequireTrue(GetBool(manifest, "soakProfile", "historyAuditChainValid"), "soak history/audit chain");
        RequireTrue(GetBool(manifest, "soakProfile", "noUnrecoverableQueueDeadlock"), "soak queue deadlock guard");

        var expectedReportFields = new[]
        {
            "benchmark_profile_id", "build_version", "runtime_version", "hardware_profile_digest", "config_digest",
            "worker_count", "run_ordinal", "step_count", "step_p50_ms", "step_p95_ms", "step_p99_ms",
            "domain_cpu_summary", "max_memory_bytes", "persistence_commit_p95_ms", "snapshot_summary",
            "publication_summary", "final_state_digest", "failure_codes"
        };
        if (!ReadStringArray(manifest, "reportFields").SequenceEqual(expectedReportFields, StringComparer.Ordinal))
            throw new InvalidDataException("PerformanceBenchmarkReportV1 field list drifted from P4-06.");

        RequireString(GetString(manifest, "adapterContract", "protocol"), "jsonl", "adapter protocol");
        var authorityRules = ReadStringArray(manifest, "adapterContract", "authorityRules");
        if (authorityRules.Length < 4 || authorityRules.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("QA-04 adapter authority rules are incomplete.");
    }

    private static void ValidatePercentTotal(JsonNode manifest, params string[] path)
    {
        var obj = GetNode(manifest, path).AsObject();
        var total = obj.Select(pair => pair.Value?.GetValue<double>() ?? 0.0).Sum();
        if (Math.Abs(total - 100.0) > 0.000001)
            throw new InvalidDataException(
                $"{string.Join(".", path)} must total 100%, found {total.ToString(CultureInfo.InvariantCulture)}%.");
    }

    private static JsonNode GetNode(JsonNode root, params string[] path)
    {
        JsonNode? current = root;
        foreach (var part in path)
            current = current?[part];
        return current ?? throw new InvalidDataException($"Missing QA-04 manifest path: {string.Join(".", path)}.");
    }

    private static string GetString(JsonNode root, params string[] path) => GetNode(root, path).GetValue<string>();
    private static int GetInt(JsonNode root, params string[] path) => GetNode(root, path).GetValue<int>();
    private static double GetDouble(JsonNode root, params string[] path) => GetNode(root, path).GetValue<double>();
    private static bool GetBool(JsonNode root, params string[] path) => GetNode(root, path).GetValue<bool>();

    private static int[] ReadIntArray(JsonNode root, params string[] path)
        => GetNode(root, path).AsArray().Select(node => node?.GetValue<int>()
            ?? throw new InvalidDataException($"Null array item at {string.Join(".", path)}.")).ToArray();

    private static string[] ReadStringArray(JsonNode root, params string[] path)
        => GetNode(root, path).AsArray().Select(node => node?.GetValue<string>()
            ?? throw new InvalidDataException($"Null array item at {string.Join(".", path)}.")).ToArray();

    private static void RequireEqual(int actual, int expected, string name)
    {
        if (actual != expected)
            throw new InvalidDataException($"{name} must be {expected}, found {actual}.");
    }

    private static void RequireTrue(bool value, string name)
    {
        if (!value)
            throw new InvalidDataException($"{name} must be true.");
    }

    private static void RequireString(string actual, string expected, string name)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidDataException($"{name} must be '{expected}', found '{actual}'.");
    }

    private static void RequireExactSet<T>(IReadOnlyCollection<T> actual, IReadOnlyCollection<T> expected, string name)
        where T : notnull
    {
        if (actual.Count != expected.Count || !actual.ToHashSet().SetEquals(expected))
            throw new InvalidDataException($"{name} does not match the canonical set.");
    }
}
