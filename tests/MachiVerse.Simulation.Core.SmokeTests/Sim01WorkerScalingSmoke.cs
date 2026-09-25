using MachiVerse.Simulation.Core.Runtime;

internal static class Sim01WorkerScalingSmoke
{
    internal static async Task RunAsync()
    {
        await VerifyAsyncWorkerBudgetBeyondSixteenAsync();
        await VerifyEmptyCpuBatchObservationAsync();
        await VerifySingleEffectiveCpuShardAsync();
        await VerifyCpuWorkerBudgetBeyondSixteenAsync();
        await VerifyCpuWorkerDynamicChunkClaimsAsync();
        await VerifyCpuWorkerSemanticOrderAsync();
        await VerifyCpuWorkerFailurePropagationAsync();
    }

    private static async Task VerifyAsyncWorkerBudgetBeyondSixteenAsync()
    {
        var input = Enumerable.Range(0, 96).ToArray();
        var output = await DeterministicBatchExecutor.RunAsync(
            input,
            workerCount: 64,
            static (value, _) => ValueTask.FromResult(value * value));

        Require(
            output.SequenceEqual(input.Select(static value => value * value)),
            "SIM-01 scalable async worker budget changed semantic output order.");
    }

    private static async Task VerifyEmptyCpuBatchObservationAsync()
    {
        var result = await DeterministicBatchExecutor.RunCpuBoundAsync(
            Array.Empty<int>(),
            workerCount: 16,
            static (value, _) => value);

        Require(result.Outputs.Count == 0,
            "SIM-01 empty CPU batch changed output cardinality.");
        Require(result.Observation.RequestedWorkerCount == 16 &&
                result.Observation.EffectiveWorkerCount == 0 &&
                result.Observation.MaxObservedConcurrency == 0 &&
                result.Observation.MinimumShardItemCount == 0 &&
                result.Observation.MaximumShardItemCount == 0 &&
                result.Observation.ShardItemCountSpread == 0 &&
                result.Observation.MinimumShardElapsedTimeTicks == 0 &&
                result.Observation.MaximumShardElapsedTimeTicks == 0 &&
                result.Observation.ShardElapsedTimeSpreadTicks == 0 &&
                result.Observation.ClaimChunkSize == 0 &&
                result.Observation.ClaimChunkCount == 0,
            "SIM-01 empty CPU batch observation must stay zeroed.");
    }

    private static async Task VerifySingleEffectiveCpuShardAsync()
    {
        var result = await DeterministicBatchExecutor.RunCpuBoundAsync(
            new[] { 21 },
            workerCount: 16,
            static (value, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return value * 2;
            });

        Require(result.Outputs.SequenceEqual(new[] { 42 }),
            "SIM-01 single effective CPU shard changed semantic output.");
        Require(result.Observation.RequestedWorkerCount == 16 &&
                result.Observation.EffectiveWorkerCount == 1 &&
                result.Observation.MaxObservedConcurrency == 1 &&
                result.Observation.MinimumShardItemCount == 1 &&
                result.Observation.MaximumShardItemCount == 1 &&
                result.Observation.ShardItemCountSpread == 0 &&
                result.Observation.MinimumShardElapsedTimeTicks >= 0 &&
                result.Observation.MaximumShardElapsedTimeTicks == result.Observation.MinimumShardElapsedTimeTicks &&
                result.Observation.ShardElapsedTimeSpreadTicks == 0 &&
                result.Observation.ClaimChunkSize == 1 &&
                result.Observation.ClaimChunkCount == 1,
            "SIM-01 single effective CPU shard observation drifted.");
    }

    private static async Task VerifyCpuWorkerBudgetBeyondSixteenAsync()
    {
        var input = Enumerable.Range(0, 256).ToArray();
        var result = await DeterministicBatchExecutor.RunCpuBoundAsync(
            input,
            workerCount: 64,
            static (value, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var accumulator = value;
                for (var iteration = 0; iteration < 2_048; iteration++)
                    accumulator = unchecked((accumulator * 1_664_525) + 1_013_904_223);
                return accumulator;
            });

        Require(result.Observation.RequestedWorkerCount == 64,
            "SIM-01 CPU worker observation lost requested worker budget.");
        Require(result.Observation.EffectiveWorkerCount == 64,
            "SIM-01 CPU worker executor retained a hidden 16-worker ceiling.");
        Require(result.Observation.MaxObservedConcurrency is >= 1 and <= 64,
            "SIM-01 CPU worker concurrency observation is outside its execution budget.");
        Require(result.Observation.MinimumShardItemCount >= 0 &&
                result.Observation.MaximumShardItemCount >= result.Observation.MinimumShardItemCount &&
                result.Observation.MaximumShardItemCount > 0,
            "SIM-01 dynamic CPU worker item observation is invalid.");
        Require(result.Observation.MinimumShardElapsedTimeTicks >= 0 &&
                result.Observation.MaximumShardElapsedTimeTicks >= result.Observation.MinimumShardElapsedTimeTicks &&
                result.Observation.ShardElapsedTimeSpreadTicks >= 0,
            "SIM-01 dynamic CPU worker timing observation is invalid.");
        Require(result.Observation.ClaimChunkSize >= 1 &&
                result.Observation.ClaimChunkCount >= result.Observation.EffectiveWorkerCount &&
                result.Observation.ClaimChunkCount <= input.Length,
            "SIM-01 dynamic CPU chunk claim observation is invalid.");
        Require(result.Outputs.Count == input.Length,
            "SIM-01 CPU worker executor changed output cardinality.");
    }

    private static async Task VerifyCpuWorkerDynamicChunkClaimsAsync()
    {
        var input = Enumerable.Range(0, 5_000).ToArray();
        var result = await DeterministicBatchExecutor.RunCpuBoundAsync(
            input,
            workerCount: 16,
            static (value, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return unchecked((value * 17) + 3);
            });

        Require(result.Outputs.SequenceEqual(input.Select(static value => unchecked((value * 17) + 3))),
            "SIM-01 dynamic CPU chunk claiming changed canonical semantic output order.");
        Require(result.Observation.ClaimChunkSize > 1,
            "SIM-01 dynamic CPU executor fell back to per-item atomic claiming for a large batch.");
        Require(result.Observation.ClaimChunkCount > result.Observation.EffectiveWorkerCount &&
                result.Observation.ClaimChunkCount < input.Length,
            "SIM-01 dynamic CPU executor did not retain enough redistributable chunks while reducing claim atomics.");
    }

    private static async Task VerifyCpuWorkerSemanticOrderAsync()
    {
        var input = Enumerable.Range(0, 512).ToArray();
        int[]? baseline = null;

        foreach (var workerCount in new[] { 1, 4, 8, 16, 32, 64 })
        {
            var result = await DeterministicBatchExecutor.RunCpuBoundAsync(
                input,
                workerCount,
                static (value, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return unchecked((value * 31) ^ 0x5a5a5a5a);
                });
            var actual = result.Outputs.ToArray();

            baseline ??= actual;
            Require(
                actual.SequenceEqual(baseline),
                $"SIM-01 CPU worker semantic output changed at worker-count={workerCount}.");
            Require(
                result.Observation.ClaimChunkSize >= 1 && result.Observation.ClaimChunkCount >= 1,
                $"SIM-01 CPU dynamic chunk claim observation drifted at worker-count={workerCount}.");
        }
    }

    private static async Task VerifyCpuWorkerFailurePropagationAsync()
    {
        var rejected = false;
        try
        {
            _ = await DeterministicBatchExecutor.RunCpuBoundAsync(
                Enumerable.Range(0, 128).ToArray(),
                workerCount: 32,
                static (value, _) => value == 73
                    ? throw new InvalidDataException("sim01.cpu-worker.expected-failure")
                    : value);
        }
        catch (InvalidDataException ex) when (ex.Message == "sim01.cpu-worker.expected-failure")
        {
            rejected = true;
        }

        Require(rejected,
            "SIM-01 CPU worker executor must propagate worker failure instead of returning partial output.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
