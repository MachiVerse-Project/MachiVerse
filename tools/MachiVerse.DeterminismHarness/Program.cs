using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MachiVerse.DeterminismHarness;

internal static class Program
{
    private const int ExpectedRunCount = 1152;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    private static readonly JsonSerializerOptions CompactJson = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };
    private static readonly Regex TokenPattern = new("^[a-z0-9][a-z0-9.-]*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex DigestPattern = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static int Main(string[] args)
    {
        try
        {
            var command = args.Length == 0 ? "verify" : args[0];
            var root = FindRepositoryRoot(Directory.GetCurrentDirectory());
            var manifestPath = Path.Combine(root, "tests", "determinism-fixtures", "v1", "harness-manifest.json");
            var manifest = JsonSerializer.Deserialize<DeterminismManifest>(File.ReadAllText(manifestPath, Encoding.UTF8), Json)
                ?? throw new InvalidDataException("QA-02 manifest decoded to null.");

            ValidateManifest(manifest);
            var matrix = BuildMatrix(manifest);
            ValidateMatrix(manifest, matrix);

            return command switch
            {
                "verify" => Verify(manifest, matrix),
                "materialize" => Materialize(manifest, matrix, args.Length >= 2
                    ? Path.GetFullPath(args[1])
                    : Path.Combine(root, "artifacts", "qa-02-determinism-matrix")),
                "compare" when args.Length == 3 => CompareFiles(manifest, args[1], args[2]),
                _ => throw new ArgumentException(
                    "Usage: MachiVerse.DeterminismHarness [verify|materialize <output-directory>|compare <baseline-trace.json> <candidate-trace.json>]")
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"QA-02 harness FAILED: {ex.Message}");
            return 1;
        }
    }

    private static int Verify(DeterminismManifest manifest, IReadOnlyList<RunDescriptor> matrix)
    {
        var digest = MatrixDigest(matrix);
        var reversed = manifest with
        {
            WorkerCounts = manifest.WorkerCounts.Reverse().ToArray(),
            RestartModes = manifest.RestartModes.Reverse().ToArray(),
            GatewayCounts = manifest.GatewayCounts.Reverse().ToArray(),
            GatewayRoutePermutations = manifest.GatewayRoutePermutations.Reverse().ToArray(),
            ViewSubscriberCounts = manifest.ViewSubscriberCounts.Reverse().ToArray(),
            LoggingLevels = manifest.LoggingLevels.Reverse().ToArray(),
            TelemetryModes = manifest.TelemetryModes.Reverse().ToArray()
        };
        var reversedDigest = MatrixDigest(BuildMatrix(reversed));
        if (!string.Equals(digest, reversedDigest, StringComparison.Ordinal))
            throw new InvalidDataException("QA-02 matrix digest depends on manifest enumeration order.");

        SelfTestComparator(manifest.ProfileId);

        Console.WriteLine("QA-02 determinism/replay harness verification PASS");
        Console.WriteLine($"Run descriptors: {matrix.Count}");
        Console.WriteLine($"Worker counts: {string.Join(',', manifest.WorkerCounts.Order())}");
        Console.WriteLine($"Matrix SHA-256: {digest}");
        Console.WriteLine("Semantic comparator self-test: PASS");
        return 0;
    }

    private static int Materialize(
        DeterminismManifest manifest,
        IReadOnlyList<RunDescriptor> matrix,
        string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var digest = MatrixDigest(matrix);

        WriteJson(Path.Combine(outputDirectory, "run-matrix.json"), matrix.OrderBy(x => x.RunId, StringComparer.Ordinal).ToArray());
        WriteJson(Path.Combine(outputDirectory, "matrix-summary.json"), new MatrixSummary(
            manifest.SchemaVersion,
            manifest.ProfileId,
            matrix.Count,
            digest));
        WriteJson(Path.Combine(outputDirectory, "target-adapter-contract.json"), manifest.AdapterContract);
        WriteJson(Path.Combine(outputDirectory, "semantic-trace-contract.json"), new
        {
            schemaVersion = manifest.SchemaVersion,
            profileId = manifest.ProfileId,
            stepOrdering = "strictly contiguous ascending committed Step sequence",
            semanticFields = manifest.SemanticDigestFields,
            ignoredOperationalFields = manifest.AdapterContract.IgnoredOperationalFields,
            comparisonRule = "All semantic digest fields must match at every committed Step; operational metadata is non-semantic."
        });
        WriteJson(Path.Combine(outputDirectory, "acceptance-test-case-coverage.json"), manifest.TestCaseIds);

        Console.WriteLine($"QA-02 matrix materialized at {outputDirectory}");
        Console.WriteLine($"Run descriptors: {matrix.Count}");
        Console.WriteLine($"Matrix SHA-256: {digest}");
        return 0;
    }

    private static int CompareFiles(DeterminismManifest manifest, string baselinePath, string candidatePath)
    {
        var baseline = ReadTrace(baselinePath);
        var candidate = ReadTrace(candidatePath);
        ValidateTrace(manifest, baseline, "baseline");
        ValidateTrace(manifest, candidate, "candidate");
        var difference = CompareTraces(baseline, candidate);
        if (difference is not null)
            throw new InvalidDataException($"Semantic divergence: {difference}");

        Console.WriteLine("QA-02 semantic trace comparison PASS");
        Console.WriteLine($"Baseline run: {baseline.RunId}");
        Console.WriteLine($"Candidate run: {candidate.RunId}");
        Console.WriteLine($"Committed Steps compared: {baseline.Steps.Length}");
        return 0;
    }

    private static IReadOnlyList<RunDescriptor> BuildMatrix(DeterminismManifest manifest)
    {
        var runs = new List<RunDescriptor>(ExpectedRunCount);
        foreach (var workerCount in manifest.WorkerCounts.Order())
        foreach (var restartMode in manifest.RestartModes.OrderBy(x => x, StringComparer.Ordinal))
        foreach (var gatewayCount in manifest.GatewayCounts.Order())
        foreach (var route in manifest.GatewayRoutePermutations.OrderBy(x => x, StringComparer.Ordinal))
        foreach (var viewSubscribers in manifest.ViewSubscriberCounts.Order())
        foreach (var loggingLevel in manifest.LoggingLevels.OrderBy(x => x, StringComparer.Ordinal))
        foreach (var telemetryMode in manifest.TelemetryModes.OrderBy(x => x, StringComparer.Ordinal))
        {
            var semanticIdentity = string.Join('\0',
                manifest.ProfileId,
                workerCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                restartMode,
                gatewayCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                route,
                viewSubscribers.ToString(System.Globalization.CultureInfo.InvariantCulture),
                loggingLevel,
                telemetryMode);
            var runId = $"qa02.run.{Sha256Hex(Encoding.UTF8.GetBytes(semanticIdentity))[..24]}";
            runs.Add(new RunDescriptor(
                runId,
                manifest.ProfileId,
                workerCount,
                restartMode,
                gatewayCount,
                route,
                viewSubscribers,
                loggingLevel,
                telemetryMode));
        }
        return runs;
    }

    private static void ValidateManifest(DeterminismManifest manifest)
    {
        if (!string.Equals(manifest.SchemaVersion, "1.0", StringComparison.Ordinal))
            throw new InvalidDataException("Unsupported QA-02 manifest schemaVersion.");
        if (!string.Equals(manifest.ProfileId, "perf.reference.v1", StringComparison.Ordinal))
            throw new InvalidDataException("QA-02 standard matrix must target perf.reference.v1.");

        RequireExactSet(manifest.WorkerCounts, new[] { 1, 4, 8, 16 }, "workerCounts");
        RequireExactSet(manifest.RestartModes, new[] { "none", "scenario-checkpoint" }, "restartModes");
        RequireExactSet(manifest.GatewayCounts, new[] { 1, 2, 4 }, "gatewayCounts");
        RequireExactSet(manifest.GatewayRoutePermutations, new[] { "route-0", "route-1", "route-2", "route-3" }, "gatewayRoutePermutations");
        RequireExactSet(manifest.ViewSubscriberCounts, new[] { 0, 100 }, "viewSubscriberCounts");
        RequireExactSet(manifest.LoggingLevels, new[] { "debug", "warn" }, "loggingLevels");
        RequireExactSet(manifest.TelemetryModes, new[] { "disabled", "enabled", "failing" }, "telemetryModes");

        var semanticFields = new[]
        {
            "stateDigest",
            "terminalOperationDigest",
            "transactionResultDigest",
            "configHistoryDigest"
        };
        if (!manifest.SemanticDigestFields.SequenceEqual(semanticFields, StringComparer.Ordinal))
            throw new InvalidDataException("semanticDigestFields must use the canonical QA-02 comparison order.");

        RequireCanonicalTokens(manifest.TestCaseIds, "testCaseIds");
        var requiredTests = new[]
        {
            "config.view-world-independence",
            "config.worker-count-independence",
            "determinism.order.thread-count",
            "observability.exporter-failure",
            "observability.trace.world-independence",
            "persistence.replay.fresh-process",
            "persistence.replay.historical",
            "persistence.replay.no-wall-time",
            "protocol.gateway.arrival-order"
        };
        foreach (var required in requiredTests)
        {
            if (!manifest.TestCaseIds.Contains(required, StringComparer.Ordinal))
                throw new InvalidDataException($"QA-02 manifest is missing required TestCaseId '{required}'.");
        }

        ValidateAdapter(manifest.AdapterContract);
    }

    private static void ValidateAdapter(AdapterContract adapter)
    {
        if (!string.Equals(adapter.Protocol, "jsonl", StringComparison.Ordinal))
            throw new InvalidDataException("QA-02 external target adapter protocol must be jsonl.");

        RequireCanonicalExact(adapter.RunRequestFields,
        [
            "gatewayCount",
            "gatewayRoutePermutation",
            "loggingLevel",
            "profileId",
            "restartCheckpointToken",
            "restartMode",
            "runId",
            "telemetryMode",
            "viewSubscriberCount",
            "workerCount"
        ], "adapter runRequestFields");
        RequireCanonicalExact(adapter.TraceResultFields,
        [
            "operational",
            "profileId",
            "runId",
            "schemaVersion",
            "steps"
        ], "adapter traceResultFields");
        RequireCanonicalExact(adapter.IgnoredOperationalFields,
        [
            "elapsedMs",
            "logDigest",
            "telemetryDigest",
            "traceId"
        ], "adapter ignoredOperationalFields");

        if (string.IsNullOrWhiteSpace(adapter.RestartCheckpointRule) ||
            !adapter.RestartCheckpointRule.Contains("does not invent", StringComparison.Ordinal))
            throw new InvalidDataException("QA-02 restart checkpoint authority rule must remain explicit.");
    }

    private static void ValidateMatrix(DeterminismManifest manifest, IReadOnlyList<RunDescriptor> matrix)
    {
        if (matrix.Count != ExpectedRunCount)
            throw new InvalidDataException($"P4-08 determinism matrix must contain exactly {ExpectedRunCount} runs.");
        if (matrix.Select(x => x.RunId).Distinct(StringComparer.Ordinal).Count() != matrix.Count)
            throw new InvalidDataException("QA-02 matrix contains duplicate RunId values.");

        foreach (var run in matrix)
        {
            ValidateToken(run.RunId, "runId");
            if (!string.Equals(run.ProfileId, manifest.ProfileId, StringComparison.Ordinal))
                throw new InvalidDataException("Run profileId does not match the manifest.");
        }
    }

    private static void ValidateTrace(DeterminismManifest manifest, SemanticTrace trace, string role)
    {
        if (!string.Equals(trace.SchemaVersion, manifest.SchemaVersion, StringComparison.Ordinal))
            throw new InvalidDataException($"{role} trace schemaVersion mismatch.");
        if (!string.Equals(trace.ProfileId, manifest.ProfileId, StringComparison.Ordinal))
            throw new InvalidDataException($"{role} trace profileId mismatch.");
        ValidateToken(trace.RunId, $"{role} runId");
        if (trace.Steps.Length == 0)
            throw new InvalidDataException($"{role} trace must contain at least one committed Step.");

        for (var index = 0; index < trace.Steps.Length; index++)
        {
            var step = trace.Steps[index];
            ValidateDigest(step.StateDigest, $"{role} stateDigest");
            ValidateDigest(step.TerminalOperationDigest, $"{role} terminalOperationDigest");
            ValidateDigest(step.TransactionResultDigest, $"{role} transactionResultDigest");
            ValidateDigest(step.ConfigHistoryDigest, $"{role} configHistoryDigest");

            if (index > 0)
            {
                var previous = trace.Steps[index - 1].Step;
                if (previous == ulong.MaxValue || step.Step != previous + 1)
                    throw new InvalidDataException($"{role} trace committed Steps must be contiguous and strictly ascending.");
            }
        }
    }

    private static string? CompareTraces(SemanticTrace baseline, SemanticTrace candidate)
    {
        if (!string.Equals(baseline.ProfileId, candidate.ProfileId, StringComparison.Ordinal))
            return $"profileId baseline={baseline.ProfileId} candidate={candidate.ProfileId}";
        if (baseline.Steps.Length != candidate.Steps.Length)
            return $"Step count baseline={baseline.Steps.Length} candidate={candidate.Steps.Length}";

        for (var index = 0; index < baseline.Steps.Length; index++)
        {
            var left = baseline.Steps[index];
            var right = candidate.Steps[index];
            if (left.Step != right.Step)
                return $"Step index={index} baseline={left.Step} candidate={right.Step}";
            if (!string.Equals(left.StateDigest, right.StateDigest, StringComparison.Ordinal))
                return $"Step {left.Step} stateDigest";
            if (!string.Equals(left.TerminalOperationDigest, right.TerminalOperationDigest, StringComparison.Ordinal))
                return $"Step {left.Step} terminalOperationDigest";
            if (!string.Equals(left.TransactionResultDigest, right.TransactionResultDigest, StringComparison.Ordinal))
                return $"Step {left.Step} transactionResultDigest";
            if (!string.Equals(left.ConfigHistoryDigest, right.ConfigHistoryDigest, StringComparison.Ordinal))
                return $"Step {left.Step} configHistoryDigest";
        }
        return null;
    }

    private static void SelfTestComparator(string profileId)
    {
        var baseline = new SemanticTrace(
            "1.0",
            profileId,
            "qa02.selftest.baseline",
            [
                TraceStep(10, "baseline-a", Operational("{\"elapsedMs\":10,\"traceId\":\"trace-a\"}")),
                TraceStep(11, "baseline-b", Operational("{\"elapsedMs\":11,\"traceId\":\"trace-b\"}"))
            ]);
        var operationallyDifferent = new SemanticTrace(
            "1.0",
            profileId,
            "qa02.selftest.candidate",
            [
                TraceStep(10, "baseline-a", Operational("{\"elapsedMs\":999,\"traceId\":\"other-a\"}")),
                TraceStep(11, "baseline-b", Operational("{\"elapsedMs\":1,\"traceId\":\"other-b\"}"))
            ]);

        if (CompareTraces(baseline, operationallyDifferent) is not null)
            throw new InvalidDataException("Comparator treated operational metadata as semantic authority.");

        var divergentSteps = operationallyDifferent.Steps.ToArray();
        divergentSteps[1] = divergentSteps[1] with { StateDigest = Digest("semantic-divergence") };
        var divergent = operationallyDifferent with { Steps = divergentSteps };
        var difference = CompareTraces(baseline, divergent);
        if (difference is null || !difference.Contains("stateDigest", StringComparison.Ordinal))
            throw new InvalidDataException("Comparator failed to identify a stateDigest divergence.");

        var missing = operationallyDifferent with { Steps = [operationallyDifferent.Steps[0]] };
        if (CompareTraces(baseline, missing) is null)
            throw new InvalidDataException("Comparator failed closed-check for a missing committed Step.");

        ValidateTrace(new DeterminismManifest(
            "1.0", profileId, [1, 4, 8, 16], ["none", "scenario-checkpoint"], [1, 2, 4],
            ["route-0", "route-1", "route-2", "route-3"], [0, 100], ["debug", "warn"],
            ["disabled", "enabled", "failing"],
            ["stateDigest", "terminalOperationDigest", "transactionResultDigest", "configHistoryDigest"],
            ["config.view-world-independence"],
            new AdapterContract("jsonl", [], [], [], "self-test does not invent checkpoints")), baseline, "self-test");
    }

    private static StepSemanticDigest TraceStep(ulong step, string semanticLabel, JsonElement operational)
        => new(
            step,
            Digest($"state:{semanticLabel}"),
            Digest($"operation:{semanticLabel}"),
            Digest($"transaction:{semanticLabel}"),
            Digest($"config:{semanticLabel}"),
            operational);

    private static JsonElement Operational(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static SemanticTrace ReadTrace(string path)
        => JsonSerializer.Deserialize<SemanticTrace>(File.ReadAllText(Path.GetFullPath(path), Encoding.UTF8), Json)
           ?? throw new InvalidDataException($"Trace decoded to null: {path}");

    private static string MatrixDigest(IReadOnlyList<RunDescriptor> matrix)
    {
        var canonical = matrix.OrderBy(x => x.RunId, StringComparer.Ordinal).ToArray();
        return Sha256Hex(JsonSerializer.SerializeToUtf8Bytes(canonical, CompactJson));
    }

    private static string Digest(string text)
        => Sha256Hex(Encoding.UTF8.GetBytes(text));

    private static string Sha256Hex(ReadOnlySpan<byte> value)
        => Convert.ToHexStringLower(SHA256.HashData(value));

    private static void ValidateDigest(string value, string field)
    {
        if (!DigestPattern.IsMatch(value))
            throw new InvalidDataException($"{field} must be a lowercase 32-byte SHA-256 hex digest.");
    }

    private static void ValidateToken(string value, string field)
    {
        if (!TokenPattern.IsMatch(value))
            throw new InvalidDataException($"{field} is not a canonical StableToken-like identifier.");
    }

    private static void RequireCanonicalTokens(IEnumerable<string> values, string field)
    {
        var array = values.ToArray();
        foreach (var value in array) ValidateToken(value, field);
        var sorted = array.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (!array.SequenceEqual(sorted, StringComparer.Ordinal))
            throw new InvalidDataException($"{field} must be ASCII/ordinal ascending.");
        if (array.Distinct(StringComparer.Ordinal).Count() != array.Length)
            throw new InvalidDataException($"{field} contains duplicates.");
    }

    private static void RequireCanonicalExact(IEnumerable<string> actual, IEnumerable<string> required, string field)
    {
        var actualArray = actual.ToArray();
        RequireCanonicalTokens(actualArray, field);
        var requiredArray = required.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (!actualArray.SequenceEqual(requiredArray, StringComparer.Ordinal))
            throw new InvalidDataException($"{field} does not match the canonical required set.");
    }

    private static void RequireExactSet(IEnumerable<int> actual, IEnumerable<int> required, string field)
    {
        var actualArray = actual.Order().ToArray();
        var requiredArray = required.Order().ToArray();
        if (actualArray.Distinct().Count() != actualArray.Length || !actualArray.SequenceEqual(requiredArray))
            throw new InvalidDataException($"{field} does not match the required P4-08 dimension.");
    }

    private static void RequireExactSet(IEnumerable<string> actual, IEnumerable<string> required, string field)
    {
        var actualArray = actual.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var requiredArray = required.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (actualArray.Distinct(StringComparer.Ordinal).Count() != actualArray.Length ||
            !actualArray.SequenceEqual(requiredArray, StringComparer.Ordinal))
            throw new InvalidDataException($"{field} does not match the required P4-08 dimension.");
    }

    private static string FindRepositoryRoot(string start)
    {
        var current = new DirectoryInfo(start);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "tests", "determinism-fixtures")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate MachiVerse repository root.");
    }

    private static void WriteJson<T>(string path, T value)
        => File.WriteAllText(path, JsonSerializer.Serialize(value, Json) + Environment.NewLine, Encoding.UTF8);

    internal sealed record DeterminismManifest(
        string SchemaVersion,
        string ProfileId,
        int[] WorkerCounts,
        string[] RestartModes,
        int[] GatewayCounts,
        string[] GatewayRoutePermutations,
        int[] ViewSubscriberCounts,
        string[] LoggingLevels,
        string[] TelemetryModes,
        string[] SemanticDigestFields,
        string[] TestCaseIds,
        AdapterContract AdapterContract);

    internal sealed record AdapterContract(
        string Protocol,
        string[] RunRequestFields,
        string[] TraceResultFields,
        string[] IgnoredOperationalFields,
        string RestartCheckpointRule);

    internal sealed record RunDescriptor(
        string RunId,
        string ProfileId,
        int WorkerCount,
        string RestartMode,
        int GatewayCount,
        string GatewayRoutePermutation,
        int ViewSubscriberCount,
        string LoggingLevel,
        string TelemetryMode);

    internal sealed record MatrixSummary(
        string SchemaVersion,
        string ProfileId,
        int RunCount,
        string MatrixSha256);

    internal sealed record SemanticTrace(
        string SchemaVersion,
        string ProfileId,
        string RunId,
        StepSemanticDigest[] Steps);

    internal sealed record StepSemanticDigest(
        ulong Step,
        string StateDigest,
        string TerminalOperationDigest,
        string TransactionResultDigest,
        string ConfigHistoryDigest,
        JsonElement? Operational);
}
