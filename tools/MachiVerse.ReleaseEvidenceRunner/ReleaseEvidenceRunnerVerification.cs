using System.Text.Json;

internal static class ReleaseEvidenceRunnerVerification
{
    internal static void VerifyContract(string repositoryRoot)
    {
        var manifestPath = Path.Combine(repositoryRoot, "tests", "performance-fixtures", "v1", "harness-manifest.json");
        var digest = Program.Sha256File(manifestPath);
        if (!string.Equals(digest, Program.CanonicalQa04ManifestSha256, StringComparison.Ordinal))
            throw new InvalidDataException($"QA-04 manifest digest mismatch: {digest}.");

        using var manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        var root = manifest.RootElement;
        var adapter = root.GetProperty("adapterContract");
        RequireEqual(adapter.GetProperty("protocol").GetString(), "jsonl", "adapter protocol");
        RequireExactSet(ReadStrings(adapter, "requestKinds"),
            ["benchmark-run", "persistence-stress", "publication-stress", "soak-run"], "request kinds");
        RequireExactSet(ReadStrings(adapter, "responseKinds"),
            ["performance-benchmark-report-v1", "persistence-stress-report-v1", "publication-stress-report-v1", "soak-report-v1"], "response kinds");

        var criteria = root.GetProperty("passCriteria");
        var limit = criteria.GetProperty("worker16MedianP95StepMs").GetDouble();
        var boundary = Median([33.333, 33.333, 33.333]);
        var regression = Median([33.333, 33.334, 33.334]);
        if (boundary > limit || regression <= limit)
            throw new InvalidDataException("worker16 median-p95 acceptance boundary self-test failed.");

        Qa04DeterminismEvidenceVerifier.VerifyContract();

        var soak = root.GetProperty("soakProfile");
        if (soak.GetProperty("durationHours").GetInt32() != 24)
            throw new InvalidDataException("QA-04 soak duration must remain 24 hours.");

        var releaseManifestPath = Path.Combine(repositoryRoot, "tests", "release-acceptance-fixtures", "v1", "acceptance-manifest.json");
        using var releaseManifest = JsonDocument.Parse(File.ReadAllBytes(releaseManifestPath));
        RequireEqual(releaseManifest.RootElement.GetProperty("qa04ManifestSha256").GetString(),
            Program.CanonicalQa04ManifestSha256, "release QA-04 digest binding");
        if (releaseManifest.RootElement.GetProperty("soak").GetProperty("minimumDurationSeconds").GetInt64() < 86_400)
            throw new InvalidDataException("Release acceptance soak minimum cannot be shorter than 86400 seconds.");

        Console.WriteLine("INT-03 release evidence runner contract verification PASS");
        Console.WriteLine($"QA-04 manifest SHA-256: {digest}");
        Console.WriteLine("Adapter boundary: external JSONL process only; no production component assembly reference.");
        Console.WriteLine("Contract-smoke output is never release-eligible.");
    }

    private static string[] ReadStrings(JsonElement parent, string property)
        => parent.GetProperty(property).EnumerateArray()
            .Select(static x => x.GetString() ?? throw new InvalidDataException("Array item cannot be null."))
            .ToArray();

    private static double Median(double[] values)
    {
        var ordered = values.OrderBy(static x => x).ToArray();
        return ordered[ordered.Length / 2];
    }

    private static void RequireExactSet(string[] actual, string[] expected, string name)
    {
        if (actual.Length != expected.Length || !actual.ToHashSet(StringComparer.Ordinal).SetEquals(expected))
            throw new InvalidDataException($"{name} does not match the canonical set.");
    }

    private static void RequireEqual(string? actual, string expected, string name)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidDataException($"{name} must be '{expected}', found '{actual}'.");
    }
}
