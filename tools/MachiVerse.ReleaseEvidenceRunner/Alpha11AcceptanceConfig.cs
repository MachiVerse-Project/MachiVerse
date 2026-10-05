using System.Text.Json;

// RunnerはCoreの内部型・DLLに依存せず、Core所有の同じ外部Configを読む。
internal sealed class Alpha11AcceptanceConfig
{
    internal static Alpha11AcceptanceConfig Current { get; } = Load();
    public string AcceptanceProfile { get; set; } = "";
    public int StepRateNumerator { get; set; }
    public int StepRateDenominator { get; set; }
    public double DeadlineMissRatioMax { get; set; }
    public int WarmupSteps { get; set; }
    public int MeasurementSteps { get; set; }
    public int[] WorkerCounts { get; set; } = [];
    public int RunsPerWorker { get; set; }
    public long CoreSteadyTargetBytes { get; set; }
    public long CoreHardGuardBytes { get; set; }
    public string Sha256 { get; private set; } = "";
    internal double TickRateHz => StepRateNumerator / (double)StepRateDenominator;
    internal double DeadlineMilliseconds => TimeSpan.FromSeconds(StepRateDenominator / (double)StepRateNumerator).TotalMilliseconds;
    internal int RunCount => checked(WorkerCounts.Length * RunsPerWorker);

    private static Alpha11AcceptanceConfig Load()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "config", "qa04-alpha11.json"));
        var config = JsonSerializer.Deserialize<Alpha11AcceptanceConfig>(bytes, Program.Json)
            ?? throw new InvalidDataException("QA-04 acceptance Config is missing.");
        if (string.IsNullOrWhiteSpace(config.AcceptanceProfile) || config.StepRateNumerator <= 0 || config.StepRateDenominator <= 0 ||
            !double.IsFinite(config.DeadlineMissRatioMax) || config.DeadlineMissRatioMax is < 0 or > 1 ||
            config.WarmupSteps <= 0 || config.MeasurementSteps <= 0 || config.RunsPerWorker <= 0 ||
            config.WorkerCounts.Length == 0 || config.WorkerCounts.Any(static w => w <= 0) ||
            config.WorkerCounts.Distinct().Count() != config.WorkerCounts.Length ||
            config.CoreSteadyTargetBytes <= 0 || config.CoreHardGuardBytes < config.CoreSteadyTargetBytes)
            throw new InvalidDataException("QA-04 acceptance Config is invalid.");
        config.Sha256 = Program.Sha256Hex(bytes);
        return config;
    }
}
