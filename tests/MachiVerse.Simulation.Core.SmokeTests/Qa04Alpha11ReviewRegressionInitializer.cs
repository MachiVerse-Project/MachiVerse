using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Configuration;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Runtime;

internal static class Qa04Alpha11ReviewRegressionInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var policy = Qa04AcceptanceConfigV1.Current;
        var schemaDefault = new CoreConfigCoordinator().LoadStartup("""
            [meta]
            format = "machiverse-config"
            schema_version = "1.0"
            component = "simulation-core"
            """);
        Require(schemaDefault.Get<long>("simulation.step-rate.numerator") == 30 &&
            schemaDefault.Get<long>("simulation.step-rate.denominator") == 1, "Schema v1 default must remain 30/1.");
        var canonical = Qa04ReferenceConfigAuthorityV1.CreateCanonical();
        Require(canonical.Get<long>("simulation.step-rate.numerator") == 10 &&
            canonical.Get<long>("simulation.step-rate.denominator") == 1, "Alpha 1.1 QA-04 authority must be 10/1.");
        var twentyHz = policy with { StepRateNumerator = 20, MeasurementSteps = 4, DeadlineMissRatioMax = 0.25 };
        Require(Qa04ReferenceConfigAuthorityV1.CreateCanonical(8, twentyHz).Get<long>("simulation.step-rate.numerator") == 20,
            "QA-04 Config authority must use the supplied external profile.");
        var collector = new Qa04BenchmarkMetricCollectorV1(twentyHz);
        for (var i = 0; i < twentyHz.MeasurementSteps; i++)
        {
            collector.RecordStepDuration(TimeSpan.FromSeconds(i), TimeSpan.FromMilliseconds(i == 3 ? 51 : 50));
            collector.RecordSuccessfulCommit(TimeSpan.FromMilliseconds(1));
        }
        collector.RecordCoreWorkingSetBytes(1);
        collector.RecordSnapshotCowBarrier(TimeSpan.FromMilliseconds(1));
        var measured = collector.Snapshot();
        Require(measured.StepDeadlineMilliseconds == 50 && measured.StepDeadlineMissCount == 1 && measured.StepDeadlineMissRatio == 0.25,
            "A changed StepRate must change the measured deadline and count strict misses.");
        var evaluation = Qa04PerformanceThresholdsV1.EvaluateCompleteMeasurement(measured, 0, 0, 0, twentyHz);
        Require(!evaluation.FailureCodes.Contains("qa04.measurement.step-sample-count") &&
            !evaluation.FailureCodes.Contains("qa04.performance.step-deadline-miss-ratio") &&
            evaluation.FailureCodes.Contains("qa04.performance.step-p99"), "External count/ratio/deadline must drive acceptance together.");

        var summary = new Qa04DurationSummaryV1(policy.MeasurementSteps, TimeSpan.Zero, TimeSpan.Zero,
            TimeSpan.Zero, policy.StepDeadline, policy.StepDeadline, 0);
        var complete = new Qa04PerformanceMeasurementSnapshotV1(policy.MeasurementSteps, summary, 0, summary,
            summary, null, 1, policy.CoreSteadyTargetBytes) { StepDeadlineMilliseconds = policy.StepDeadline.TotalMilliseconds };
        Require(Qa04PerformanceThresholdsV1.EvaluateCompleteMeasurement(complete, 0, 0, 0).Passed,
            "Exact working-set target boundary must pass.");
        foreach (var bytes in new[] { policy.CoreSteadyTargetBytes + 1, policy.CoreHardGuardBytes, policy.CoreHardGuardBytes + 1, 40L << 30 })
        {
            var result = Qa04PerformanceThresholdsV1.EvaluateCompleteMeasurement(complete with { MaxCoreWorkingSetBytes = bytes }, 0, 0, 0);
            Require(!result.Passed && result.FailureCodes.Contains("qa04.performance.core-memory-target"), "Memory target excess must block release.");
            Require(result.FailureCodes.Contains("qa04.performance.core-memory-guard") == (bytes > policy.CoreHardGuardBytes),
                "Working-set hard guard must preserve its exact boundary.");
        }
        Require(!Qa04PerformanceThresholdsV1.EvaluateCompleteMeasurement(complete with { StepDeadlineMissRatio = double.NaN }, 0, 0, 0).Passed,
            "Nonfinite deadline evidence must fail closed.");
    }

    internal static async Task VerifyExistingWorldAsync()
    {
        var legacy = new CoreConfigCoordinator().LoadStartup(
            await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "config", "simulation-core-v1-legacy.toml")));
        var directory = Path.Combine(Path.GetTempPath(), "machiverse-alpha11-regression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var legacyPath = Path.Combine(directory, "legacy.toml");
            await File.WriteAllTextAsync(legacyPath, legacy.NormalizedToml);
            var options = new AlphaSingleGatewayOptions(legacyPath, Path.Combine(directory, "world"),
                OpaqueId128.Parse("00000000000000000000000000000001"), new WorldSeed256(new byte[32]),
                OpaqueId128.Parse("00000000000000000000000000000002"),
                OpaqueId128.Parse("00000000000000000000000000000003"),
                OpaqueId128.Parse("00000000000000000000000000000004"));
            var first = await AlphaSingleGatewayRuntime.CreateAsync(options);
            await first.Store.DisposeAsync();
            var standardPath = Path.Combine(AppContext.BaseDirectory, "config", "simulation-core.toml");
            var reopened = await AlphaSingleGatewayRuntime.CreateAsync(options with { CoreConfigPath = standardPath });
            var head = await reopened.Store.ReadCoreProtocolHeadAsync();
            Require(head.ConfigDigest.AsSpan().SequenceEqual(legacy.Digest), "Existing 30Hz world must reopen with its persisted Config digest.");
            await reopened.Store.DisposeAsync();
            var fresh = await AlphaSingleGatewayRuntime.CreateAsync(options with { CoreConfigPath = standardPath, PersistenceRoot = Path.Combine(directory, "fresh") });
            var newHead = await fresh.Store.ReadCoreProtocolHeadAsync();
            var standard = new CoreConfigCoordinator().LoadStartup(await File.ReadAllTextAsync(standardPath));
            Require(newHead.ConfigDigest.AsSpan().SequenceEqual(standard.Digest), "New Alpha 1.1 world must use the 10Hz bundled Config.");
            await fresh.Store.DisposeAsync();
            try
            {
                AlphaStartupConfigAuthorityV1.Resolve(standard, new byte[32], Path.Combine(AppContext.BaseDirectory, "config"));
                throw new InvalidOperationException("Unknown persisted Config must fail closed.");
            }
            catch (InvalidDataException) { }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
