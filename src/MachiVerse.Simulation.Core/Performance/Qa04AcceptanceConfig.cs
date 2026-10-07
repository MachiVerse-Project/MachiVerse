using System.Security.Cryptography;
using System.Text.Json;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>Core所有の外部QA-04 Config。StepRateから処理deadlineを導出する。</summary>
public sealed record Qa04AcceptanceConfigV1
{
    private static readonly Lazy<Qa04AcceptanceConfigV1> DefaultConfig = new(() => Load(
        Path.Combine(AppContext.BaseDirectory, "config", "qa04-alpha11.json")));

    public static Qa04AcceptanceConfigV1 Current => DefaultConfig.Value;
    public string AcceptanceProfile { get; init; } = "";
    public int StepRateNumerator { get; init; }
    public int StepRateDenominator { get; init; }
    public double DeadlineMissRatioMax { get; init; }
    public int WarmupSteps { get; init; }
    public int MeasurementSteps { get; init; }
    public int[] WorkerCounts { get; init; } = [];
    public int RunsPerWorker { get; init; }
    public double RollingWindowSeconds { get; init; }
    public double HealthyMeanMilliseconds { get; init; }
    public long CoreSteadyTargetBytes { get; init; }
    public long CoreHardGuardBytes { get; init; }
    public string Sha256 { get; init; } = "";
    public double TickRateHz => StepRateNumerator / (double)StepRateDenominator;
    public TimeSpan StepDeadline => TimeSpan.FromSeconds(StepRateDenominator / (double)StepRateNumerator);

    public static Qa04AcceptanceConfigV1 Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var config = JsonSerializer.Deserialize<Qa04AcceptanceConfigV1>(bytes,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("qa04.acceptance-config-missing");
        config.Validate();
        return config with { Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() };
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AcceptanceProfile) || StepRateNumerator <= 0 || StepRateDenominator <= 0 ||
            !double.IsFinite(DeadlineMissRatioMax) || DeadlineMissRatioMax is < 0 or > 1 ||
            WarmupSteps <= 0 || MeasurementSteps <= 0 || RunsPerWorker <= 0 ||
            WorkerCounts.Length == 0 || WorkerCounts.Any(static w => w <= 0) || WorkerCounts.Distinct().Count() != WorkerCounts.Length ||
            !double.IsFinite(RollingWindowSeconds) || RollingWindowSeconds <= 0 ||
            !double.IsFinite(HealthyMeanMilliseconds) || HealthyMeanMilliseconds <= 0 ||
            CoreSteadyTargetBytes <= 0 || CoreHardGuardBytes < CoreSteadyTargetBytes)
            throw new InvalidDataException("qa04.acceptance-config-invalid");
    }
}
