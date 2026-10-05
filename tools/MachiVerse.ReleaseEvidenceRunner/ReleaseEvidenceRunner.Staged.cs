using System.Text.Json;

internal static partial class ReleaseEvidenceRunner
{
    internal static async Task<int> RunGate4Step3Async(
        string repositoryRoot,
        string executionClass,
        string sourceCommit,
        string adapterExecutable,
        string planDirectory,
        string outputDirectory)
    {
        return await RunGate4Step3Alpha11Async(repositoryRoot, executionClass, sourceCommit,
            adapterExecutable, planDirectory, outputDirectory);
    }

    internal static async Task<int> RunGate4Step4Async(
        string repositoryRoot,
        string executionClass,
        string sourceCommit,
        string adapterExecutable,
        string planDirectory,
        string step3EvidencePath,
        string outputDirectory)
    {
        ValidateStageInputs(executionClass, sourceCommit, adapterExecutable, planDirectory);
        if (!File.Exists(step3EvidencePath))
            throw new FileNotFoundException("Gate4 Step3 benchmark evidence was not found.", step3EvidencePath);

        var step3 = Program.ReadJson<Gate4Step3BenchmarkEvidence>(
            step3EvidencePath,
            "Gate4 Step3 benchmark evidence");
        ValidateStep3Evidence(step3, sourceCommit, executionClass, step3EvidencePath);

        var persistenceProfile = RequirePlanJson(planDirectory, "persistence-profile.json");
        var publicationProfile = RequirePlanJson(planDirectory, "publication-profile.json");
        var soakPlan = RequirePlanJson(planDirectory, "soak-plan.json");

        Directory.CreateDirectory(outputDirectory);
        var reportsDirectory = Path.Combine(outputDirectory, "reports");
        Directory.CreateDirectory(reportsDirectory);
        Program.WriteJson(
            Path.Combine(outputDirectory, "gate4-step4-stage-start.json"),
            new
            {
                schemaVersion = SchemaVersion,
                executionClass,
                sourceCommit,
                qa04ManifestSha256 = Program.CanonicalQa04ManifestSha256,
                step3EvidenceSha256 = Program.Sha256File(step3EvidencePath),
                started = true,
            });

        var step3Directory = Path.GetDirectoryName(step3EvidencePath) ?? Directory.GetCurrentDirectory();
        var step3AggregateSource = ResolveArtifactPath(
            step3Directory,
            step3.ReferenceProfile.ReportRef,
            "Gate4 Step3 reference aggregate");
        var step3AggregateTarget = Path.Combine(reportsDirectory, "perf.reference.v1.aggregate.json");
        var step3Aggregate = Program.ReadJson<Alpha11BenchmarkAggregateArtifact>(step3AggregateSource, "Alpha 1.1 Step3 aggregate");
        foreach (var run in step3Aggregate.Runs)
        {
            var rawSource = ResolveArtifactPath(step3Directory, run.ReportRef, run.RunId);
            var rawTarget = Path.GetFullPath(Path.Combine(outputDirectory, run.ReportRef));
            if (!string.Equals(rawSource, rawTarget, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(rawTarget)!);
                File.Copy(rawSource, rawTarget, overwrite: true);
            }
            VerifyArtifactDigest(outputDirectory, run.ReportRef, run.ReportDigest, run.RunId);
        }
        File.Copy(step3AggregateSource, step3AggregateTarget, overwrite: true);
        var copiedAggregateDigest = Program.Sha256File(step3AggregateTarget);
        if (!string.Equals(copiedAggregateDigest, step3.ReferenceProfile.ReportDigest, StringComparison.Ordinal))
            throw new InvalidDataException("Gate4 Step3 aggregate digest changed while staging Step4.");

        var referenceEvidence = new PerformanceReportEvidence
        {
            ProfileId = ReferenceProfile,
            SourceCommit = sourceCommit,
            ReportRef = RelativeRef(outputDirectory, step3AggregateTarget),
            ReportDigest = copiedAggregateDigest,
            Passed = step3.ReferenceProfile.Passed,
            FailureCodes = step3.ReferenceProfile.FailureCodes,
        };

        var persistenceRequest = NewRequest(
            "persistence-stress",
            executionClass,
            "perf.persistence.v1",
            sourceCommit,
            PersistenceProfile,
            persistenceProfile,
            null);
        var persistenceInvocation = await InvokeAdapterAsync(adapterExecutable, persistenceRequest);
        ValidateResponse(persistenceInvocation.Response, persistenceRequest, "persistence-stress-report-v1");
        var persistenceArtifact = WriteResponseArtifact(
            outputDirectory,
            reportsDirectory,
            "perf.persistence.v1",
            persistenceInvocation.Response);
        var persistenceEvidence = EvaluatePersistence(
            persistenceInvocation.Response,
            sourceCommit,
            persistenceArtifact.Ref,
            persistenceArtifact.Digest);

        if (!persistenceEvidence.Passed)
        {
            WriteStep4PreflightFailure(
                outputDirectory,
                sourceCommit,
                "persistence",
                persistenceEvidence.FailureCodes);
            Console.Error.WriteLine("Gate4 Step4 preflight stopped before publication/soak: perf.persistence.v1 did not PASS.");
            return 2;
        }

        var publicationRequest = NewRequest(
            "publication-stress",
            executionClass,
            "perf.publication.v1",
            sourceCommit,
            PublicationProfile,
            publicationProfile,
            null);
        var publicationInvocation = await InvokeAdapterAsync(adapterExecutable, publicationRequest);
        ValidateResponse(publicationInvocation.Response, publicationRequest, "publication-stress-report-v1");
        var publicationArtifact = WriteResponseArtifact(
            outputDirectory,
            reportsDirectory,
            "perf.publication.v1",
            publicationInvocation.Response);
        var publicationEvidence = EvaluatePublication(
            publicationInvocation.Response,
            sourceCommit,
            publicationArtifact.Ref,
            publicationArtifact.Digest);

        if (!publicationEvidence.Passed)
        {
            WriteStep4PreflightFailure(
                outputDirectory,
                sourceCommit,
                "publication",
                publicationEvidence.FailureCodes);
            Console.Error.WriteLine("Gate4 Step4 preflight stopped before 24h soak: perf.publication.v1 did not PASS.");
            return 2;
        }

        var soakRequest = NewRequest(
            "soak-run",
            executionClass,
            "performance.soak.24h",
            sourceCommit,
            SoakProfile,
            soakPlan,
            null);
        var soakInvocation = await InvokeAdapterAsync(adapterExecutable, soakRequest);
        ValidateResponse(soakInvocation.Response, soakRequest, "soak-report-v1");
        var soakArtifact = WriteResponseArtifact(
            outputDirectory,
            reportsDirectory,
            "performance.soak.24h",
            soakInvocation.Response);
        var soakEvidence = EvaluateSoak(
            soakInvocation.Response,
            sourceCommit,
            soakArtifact.Ref,
            soakArtifact.Digest,
            soakInvocation.Elapsed,
            executionClass);

        var observedFailures = new SortedSet<string>(StringComparer.Ordinal);
        if (step3.FailureCodes.Contains("state-digest-mismatch", StringComparer.Ordinal))
            observedFailures.Add("determinism.divergence");
        if (step3.FailureCodes.Contains("accepted-operation-loss", StringComparer.Ordinal))
            observedFailures.Add("operation.accepted-loss");
        if (!soakEvidence.ParallelVerifierDigestMatched)
            observedFailures.Add("determinism.divergence");
        if (soakEvidence.AcceptedOperationLoss != 0)
            observedFailures.Add("operation.accepted-loss");
        if (!soakEvidence.HistoryAuditChainValid)
            observedFailures.Add("persistence.history-corruption-undetected");

        var releaseEligible = IsReleaseExecutionEligible(executionClass, soakEvidence.DurationSeconds);
        var fragment = new EvidenceFragment
        {
            SchemaVersion = SchemaVersion,
            ExecutionClass = executionClass,
            ReleaseEligible = releaseEligible,
            SourceCommit = sourceCommit,
            Qa04ManifestSha256 = Program.CanonicalQa04ManifestSha256,
            PerformanceReports = [referenceEvidence, persistenceEvidence, publicationEvidence],
            Soak = soakEvidence,
            DeterminismDigestSummary = step3.DeterminismDigestSummary,
            ObservedFailureCodes = observedFailures.ToArray(),
        };
        var fragmentPath = Path.Combine(outputDirectory, "qa04-evidence-fragment.json");
        Program.WriteJson(fragmentPath, fragment);

        Console.WriteLine($"Gate4 Step4 evidence fragment: {fragmentPath}");
        Console.WriteLine($"Execution class: {executionClass}");
        Console.WriteLine($"Source commit: {sourceCommit}");
        Console.WriteLine($"Release eligible: {releaseEligible}");
        Console.WriteLine($"Measured/reported soak evidence seconds: {soakEvidence.DurationSeconds}");
        Console.WriteLine($"Persistence profile: {(persistenceEvidence.Passed ? "PASS" : "FAIL")}");
        Console.WriteLine($"Publication profile: {(publicationEvidence.Passed ? "PASS" : "FAIL")}");
        Console.WriteLine($"Soak profile: {(soakEvidence.Passed ? "PASS" : "FAIL")}");

        if (string.Equals(executionClass, "release", StringComparison.Ordinal) && !releaseEligible)
            return 2;
        return soakEvidence.Passed ? 0 : 2;
    }

    private static void ValidateStageInputs(
        string executionClass,
        string sourceCommit,
        string adapterExecutable,
        string planDirectory)
    {
        if (executionClass is not ("contract-smoke" or "release"))
            throw new ArgumentException("executionClass must be contract-smoke or release.");
        Program.RequireLowerHex(sourceCommit, 40, "sourceCommit");
        if (!File.Exists(adapterExecutable))
            throw new FileNotFoundException("QA-04 adapter executable was not found.", adapterExecutable);
        if (!Directory.Exists(planDirectory))
            throw new DirectoryNotFoundException($"QA-04 plan directory was not found: {planDirectory}");
    }

    private static void ValidateStep3Evidence(
        Gate4Step3BenchmarkEvidence evidence,
        string sourceCommit,
        string executionClass,
        string evidencePath)
    {
        if (!string.Equals(evidence.SchemaVersion, SchemaVersion, StringComparison.Ordinal))
            throw new InvalidDataException("Gate4 Step3 evidence schemaVersion mismatch.");
        if (!string.Equals(evidence.ExecutionClass, executionClass, StringComparison.Ordinal))
            throw new InvalidDataException("Gate4 Step3 evidence executionClass mismatch.");
        if (!string.Equals(evidence.SourceCommit, sourceCommit, StringComparison.Ordinal))
            throw new InvalidDataException("Gate4 Step3 evidence sourceCommit mismatch.");
        if (!string.Equals(evidence.Qa04ManifestSha256, Program.CanonicalQa04ManifestSha256, StringComparison.Ordinal))
            throw new InvalidDataException("Gate4 Step3 evidence manifest digest is not canonical.");
        if (!string.Equals(evidence.BenchmarkProfileId, ReferenceProfile, StringComparison.Ordinal) ||
            !string.Equals(evidence.ReferenceProfile.ProfileId, ReferenceProfile, StringComparison.Ordinal))
            throw new InvalidDataException("Gate4 Step3 evidence benchmark profile mismatch.");
        if (!string.Equals(evidence.ReferenceProfile.SourceCommit, sourceCommit, StringComparison.Ordinal))
            throw new InvalidDataException("Gate4 Step3 reference evidence sourceCommit mismatch.");
        if (!evidence.Passed || !evidence.ReferenceProfile.Passed || evidence.FailureCodes.Length != 0)
            throw new InvalidDataException("Gate4 Step3 benchmark evidence is not a completed PASS.");
        Program.RequireLowerHex(evidence.DeterminismDigestSummary, 64, "Step3 determinismDigestSummary");

        var evidenceDirectory = Path.GetDirectoryName(evidencePath) ?? Directory.GetCurrentDirectory();
        VerifyArtifactDigest(
            evidenceDirectory,
            evidence.ReferenceProfile.ReportRef,
            evidence.ReferenceProfile.ReportDigest,
            "gate4-step3-reference");
        ValidateAlpha11Step3Aggregate(evidence, evidenceDirectory, sourceCommit, executionClass);
    }

    private static string ResolveArtifactPath(string rootDirectory, string artifactRef, string name)
    {
        if (string.IsNullOrWhiteSpace(artifactRef) || Path.IsPathRooted(artifactRef))
            throw new InvalidDataException($"{name} artifact ref must be relative.");
        var root = Path.GetFullPath(rootDirectory) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(
            rootDirectory,
            artifactRef.Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!candidate.StartsWith(root, comparison))
            throw new InvalidDataException($"{name} artifact ref escapes the evidence directory.");
        if (!File.Exists(candidate))
            throw new FileNotFoundException($"{name} artifact is missing.", candidate);
        return candidate;
    }

    private static void WriteStep4PreflightFailure(
        string outputDirectory,
        string sourceCommit,
        string failedStage,
        IReadOnlyList<string> failureCodes)
    {
        Program.WriteJson(
            Path.Combine(outputDirectory, "gate4-step4-preflight-failure.json"),
            new
            {
                schemaVersion = SchemaVersion,
                sourceCommit,
                qa04ManifestSha256 = Program.CanonicalQa04ManifestSha256,
                failedStage,
                failureCodes = failureCodes.ToArray(),
            });
    }

    private sealed class Gate4Step3BenchmarkEvidence
    {
        public string SchemaVersion { get; set; } = "";
        public string ExecutionClass { get; set; } = "";
        public string SourceCommit { get; set; } = "";
        public string Qa04ManifestSha256 { get; set; } = "";
        public string AcceptanceProfile { get; set; } = "";
        public string AcceptanceConfigSha256 { get; set; } = "";
        public string BenchmarkProfileId { get; set; } = "";
        public PerformanceReportEvidence ReferenceProfile { get; set; } = new();
        public string DeterminismDigestSummary { get; set; } = "";
        public bool Passed { get; set; }
        public string[] FailureCodes { get; set; } = [];
    }
}
