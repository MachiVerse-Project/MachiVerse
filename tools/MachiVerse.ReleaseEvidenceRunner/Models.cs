using System.Text.Json;

internal sealed class Qa04AdapterRequest
{
    public string SchemaVersion { get; set; } = "1.0";
    public string RequestKind { get; set; } = "";
    public string ExecutionClass { get; set; } = "";
    public string RequestId { get; set; } = "";
    public string SourceCommit { get; set; } = "";
    public string Qa04ManifestSha256 { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public BenchmarkRunDescriptor? Run { get; set; }
    public JsonElement Profile { get; set; }
}

internal sealed class Qa04AdapterResponse
{
    public string SchemaVersion { get; set; } = "";
    public string ResponseKind { get; set; } = "";
    public string ExecutionClass { get; set; } = "";
    public string RequestId { get; set; } = "";
    public string SourceCommit { get; set; } = "";
    public string Qa04ManifestSha256 { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public bool Passed { get; set; }
    public string[] FailureCodes { get; set; } = [];
    public JsonElement Report { get; set; }
}

internal sealed class BenchmarkRunDescriptor
{
    public string RunId { get; set; } = "";
    public string BenchmarkProfileId { get; set; } = "";
    public int WorkerCount { get; set; }
    public int RunOrdinal { get; set; }
    public int WarmupSteps { get; set; }
    public int MeasurementSteps { get; set; }
    public string WorldSeed { get; set; } = "";
}

internal sealed class EvidenceFragment
{
    public string SchemaVersion { get; set; } = "1.0";
    public string ExecutionClass { get; set; } = "";
    public bool ReleaseEligible { get; set; }
    public string SourceCommit { get; set; } = "";
    public string Qa04ManifestSha256 { get; set; } = "";
    public PerformanceReportEvidence[] PerformanceReports { get; set; } = [];
    public SoakEvidence? Soak { get; set; }
    public string DeterminismDigestSummary { get; set; } = "";
    public string[] ObservedFailureCodes { get; set; } = [];
}

internal sealed class PerformanceReportEvidence
{
    public string ProfileId { get; set; } = "";
    public string SourceCommit { get; set; } = "";
    public string ReportRef { get; set; } = "";
    public string ReportDigest { get; set; } = "";
    public bool Passed { get; set; }
    public string[] FailureCodes { get; set; } = [];
}

internal sealed class SoakEvidence
{
    public string TestCaseId { get; set; } = "";
    public string SourceCommit { get; set; } = "";
    public string ReportRef { get; set; } = "";
    public string ReportDigest { get; set; } = "";
    public long DurationSeconds { get; set; }
    public bool Passed { get; set; }
    public bool ParallelVerifierDigestMatched { get; set; }
    public double MaxPostWarmupMemoryGrowthPercent { get; set; }
    public int AcceptedOperationLoss { get; set; }
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
    public string ArtifactDigest { get; set; } = "";
}

internal sealed class WaiverEvidence
{
    public string Code { get; set; } = "";
    public string Reason { get; set; } = "";
}

internal sealed class BenchmarkRunObservation
{
    public string RunId { get; set; } = "";
    public int WorkerCount { get; set; }
    public int RunOrdinal { get; set; }
    public double StepP95Ms { get; set; }
    public double StepP99Ms { get; set; }
    public double Mean60sStepMs { get; set; }
    public long MaxMemoryBytes { get; set; }
    public double SqliteCommitP95Ms { get; set; }
    public double SqliteCommitP99Ms { get; set; }
    public double SnapshotCowBarrierP95Ms { get; set; }
    public int AcceptedOperationLoss { get; set; }
    public bool HiddenSolverIterationReduction { get; set; }
    public string FinalStateDigest { get; set; } = "";
    public string ReportRef { get; set; } = "";
    public string ReportDigest { get; set; } = "";
    public bool TargetPassed { get; set; }
    public string[] TargetFailureCodes { get; set; } = [];
}

internal sealed class BenchmarkAggregateArtifact
{
    public string SchemaVersion { get; set; } = "1.0";
    public string ProfileId { get; set; } = "perf.reference.v1";
    public string ExecutionClass { get; set; } = "";
    public string SourceCommit { get; set; } = "";
    public string Qa04ManifestSha256 { get; set; } = "";
    public BenchmarkRunObservation[] Runs { get; set; } = [];
    public string FinalStateDigest { get; set; } = "";
    public string DeterminismDigestSummary { get; set; } = "";
    public bool Passed { get; set; }
    public string[] FailureCodes { get; set; } = [];
}
