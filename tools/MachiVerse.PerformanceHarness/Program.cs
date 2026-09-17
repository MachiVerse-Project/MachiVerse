using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MachiVerse.PerformanceHarness;

internal static partial class Program
{
    private const int ExpectedRunCount = 12;
    private const string ExpectedWorldSeed = "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f";
    private const string ExpectedManifestSha256 = "5de8301439ca57080eefa599da284f9271b29366c791bcb9c2f85ddbfa041423";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true
    };

    private static int Main(string[] args)
    {
        try
        {
            var command = args.Length == 0 ? "verify" : args[0];
            var root = FindRepositoryRoot(Directory.GetCurrentDirectory());
            var manifestPath = Path.Combine(root, "tests", "performance-fixtures", "v1", "harness-manifest.json");
            var manifestBytes = File.ReadAllBytes(manifestPath);
            var manifest = JsonNode.Parse(Encoding.UTF8.GetString(manifestBytes))
                ?? throw new InvalidDataException("QA-04 manifest decoded to null.");

            ValidateManifest(manifest, manifestBytes);
            var workers = ReadIntArray(manifest, "workerCounts");
            var matrix = BuildRunMatrix(manifest, workers);
            ValidateRunMatrix(workers, GetInt(manifest, "processRunsPerWorker"), matrix);

            return command switch
            {
                "verify" => Verify(manifest, workers, matrix),
                "materialize" => Materialize(
                    manifest,
                    matrix,
                    args.Length >= 2
                        ? Path.GetFullPath(args[1])
                        : Path.Combine(root, "artifacts", "qa-04-performance-plan")),
                _ => throw new ArgumentException(
                    "Usage: MachiVerse.PerformanceHarness [verify|materialize <output-directory>]")
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"QA-04 harness FAILED: {ex.Message}");
            return 1;
        }
    }

    private static int Verify(
        JsonNode manifest,
        IReadOnlyList<int> workers,
        IReadOnlyList<BenchmarkRunDescriptor> matrix)
    {
        var digest = PlanDigest(matrix);
        var reversedDigest = PlanDigest(BuildRunMatrix(manifest, workers.Reverse().ToArray()));
        if (!string.Equals(digest, reversedDigest, StringComparison.Ordinal))
            throw new InvalidDataException("QA-04 plan digest depends on worker enumeration order.");

        SelfTestAcceptanceRules(manifest);

        Console.WriteLine("QA-04 performance/soak harness verification PASS");
        Console.WriteLine($"Benchmark descriptors: {matrix.Count}");
        Console.WriteLine($"Worker counts: {string.Join(",", workers.Order())}");
        Console.WriteLine($"Runs per worker: {GetInt(manifest, "processRunsPerWorker")}");
        Console.WriteLine($"Manifest SHA-256: {ExpectedManifestSha256}");
        Console.WriteLine($"Plan SHA-256: {digest}");
        Console.WriteLine("Acceptance-rule self-test: PASS");
        return 0;
    }

    private static int Materialize(
        JsonNode manifest,
        IReadOnlyList<BenchmarkRunDescriptor> matrix,
        string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var digest = PlanDigest(matrix);

        WriteJson(Path.Combine(outputDirectory, "reference-run-matrix.json"),
            matrix.OrderBy(x => x.RunId, StringComparer.Ordinal).ToArray());
        WriteJson(Path.Combine(outputDirectory, "reference-load-contract.json"), GetNode(manifest, "referenceLoad"));
        WriteJson(Path.Combine(outputDirectory, "benchmark-summary.json"), new
        {
            schemaVersion = GetString(manifest, "schemaVersion"),
            benchmarkProfileId = GetString(manifest, "benchmarkProfileId"),
            worldSeed = GetString(manifest, "worldSeed"),
            runCount = matrix.Count,
            manifestSha256 = ExpectedManifestSha256,
            planSha256 = digest,
            loggingLevel = GetString(manifest, "loggingLevel"),
            warmupSteps = GetInt(manifest, "warmupSteps"),
            measurementSteps = GetInt(manifest, "measurementSteps"),
            processRunsPerWorker = GetInt(manifest, "processRunsPerWorker"),
            cooldownSnapshotDrainExcluded = GetBool(manifest, "cooldownSnapshotDrainExcluded"),
            normalSnapshotDuringMeasurementRequired = GetBool(manifest, "normalSnapshotDuringMeasurementRequired"),
            passCriteria = GetNode(manifest, "passCriteria")
        });
        WriteJson(Path.Combine(outputDirectory, "persistence-profile.json"), GetNode(manifest, "persistenceProfile"));
        WriteJson(Path.Combine(outputDirectory, "publication-profile.json"), GetNode(manifest, "publicationProfile"));
        WriteJson(Path.Combine(outputDirectory, "soak-plan.json"), GetNode(manifest, "soakProfile"));
        WriteJson(Path.Combine(outputDirectory, "performance-report-contract.json"), new
        {
            schema = "PerformanceBenchmarkReportV1",
            requiredFields = GetNode(manifest, "reportFields"),
            aggregation = new
            {
                worker16P95 = "median of 3 run-level p95 values",
                determinism = "all worker counts and process runs must produce identical final StateDiagnostic.state_digest",
                operationalTiming = "diagnostic only; not world-semantic authority"
            },
            passCriteria = GetNode(manifest, "passCriteria")
        });
        WriteJson(Path.Combine(outputDirectory, "target-adapter-contract.json"), GetNode(manifest, "adapterContract"));

        Console.WriteLine($"QA-04 performance plan materialized at {outputDirectory}");
        Console.WriteLine($"Benchmark descriptors: {matrix.Count}");
        Console.WriteLine($"Plan SHA-256: {digest}");
        return 0;
    }

    private static IReadOnlyList<BenchmarkRunDescriptor> BuildRunMatrix(JsonNode manifest, IReadOnlyList<int> workers)
    {
        var benchmarkProfileId = GetString(manifest, "benchmarkProfileId");
        var worldSeed = GetString(manifest, "worldSeed");
        var processRunsPerWorker = GetInt(manifest, "processRunsPerWorker");
        var warmupSteps = GetInt(manifest, "warmupSteps");
        var measurementSteps = GetInt(manifest, "measurementSteps");
        var runs = new List<BenchmarkRunDescriptor>(ExpectedRunCount);

        foreach (var workerCount in workers.Order())
        {
            for (var runOrdinal = 1; runOrdinal <= processRunsPerWorker; runOrdinal++)
            {
                var semanticIdentity = string.Join("\0",
                [
                    benchmarkProfileId,
                    worldSeed,
                    workerCount.ToString(CultureInfo.InvariantCulture),
                    runOrdinal.ToString(CultureInfo.InvariantCulture),
                    warmupSteps.ToString(CultureInfo.InvariantCulture),
                    measurementSteps.ToString(CultureInfo.InvariantCulture)
                ]);
                var runId = $"qa04.run.{Sha256Hex(Encoding.UTF8.GetBytes(semanticIdentity))[..24]}";
                runs.Add(new BenchmarkRunDescriptor(
                    runId, benchmarkProfileId, workerCount, runOrdinal, warmupSteps, measurementSteps, worldSeed));
            }
        }

        return runs;
    }

    private static void ValidateRunMatrix(
        IReadOnlyList<int> workers,
        int processRunsPerWorker,
        IReadOnlyList<BenchmarkRunDescriptor> matrix)
    {
        if (matrix.Count != ExpectedRunCount)
            throw new InvalidDataException($"QA-04 matrix must contain {ExpectedRunCount} descriptors, found {matrix.Count}.");
        if (matrix.Select(x => x.RunId).Distinct(StringComparer.Ordinal).Count() != matrix.Count)
            throw new InvalidDataException("QA-04 run IDs are not unique.");

        foreach (var worker in workers)
        {
            var runs = matrix.Where(x => x.WorkerCount == worker).OrderBy(x => x.RunOrdinal).ToArray();
            if (runs.Length != processRunsPerWorker)
                throw new InvalidDataException($"Worker {worker} does not have exactly {processRunsPerWorker} runs.");
            if (!runs.Select(x => x.RunOrdinal).SequenceEqual(Enumerable.Range(1, processRunsPerWorker)))
                throw new InvalidDataException($"Worker {worker} run ordinals are not contiguous from 1.");
        }
    }

    private static string PlanDigest(IEnumerable<BenchmarkRunDescriptor> matrix)
    {
        var canonicalRuns = matrix.OrderBy(x => x.RunId, StringComparer.Ordinal).Select(x => string.Join("\0",
        [
            x.RunId,
            x.BenchmarkProfileId,
            x.WorkerCount.ToString(CultureInfo.InvariantCulture),
            x.RunOrdinal.ToString(CultureInfo.InvariantCulture),
            x.WarmupSteps.ToString(CultureInfo.InvariantCulture),
            x.MeasurementSteps.ToString(CultureInfo.InvariantCulture),
            x.WorldSeed
        ]));
        return Sha256Hex(Encoding.UTF8.GetBytes(ExpectedManifestSha256 + "\n" + string.Join("\n", canonicalRuns)));
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

    private static void WriteJson(string path, object value)
        => File.WriteAllText(path, JsonSerializer.Serialize(value, Json) + Environment.NewLine, new UTF8Encoding(false));

    private static string Sha256Hex(ReadOnlySpan<byte> bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    internal sealed record BenchmarkRunDescriptor(
        string RunId,
        string BenchmarkProfileId,
        int WorkerCount,
        int RunOrdinal,
        int WarmupSteps,
        int MeasurementSteps,
        string WorldSeed);
}
