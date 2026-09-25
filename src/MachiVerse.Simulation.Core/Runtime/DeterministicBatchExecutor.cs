namespace MachiVerse.Simulation.Core.Runtime;

public sealed record DeterministicCpuBatchObservationV1(
    int RequestedWorkerCount,
    int EffectiveWorkerCount,
    int MaxObservedConcurrency);

public sealed record DeterministicCpuBatchResultV1<TOutput>(
    IReadOnlyList<TOutput> Outputs,
    DeterministicCpuBatchObservationV1 Observation);

public static class DeterministicBatchExecutor
{
    public static async Task<IReadOnlyList<TOutput>> RunAsync<TInput, TOutput>(
        IReadOnlyList<TInput> inputs,
        int workerCount,
        Func<TInput, CancellationToken, ValueTask<TOutput>> execute,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (workerCount < 1) throw new ArgumentOutOfRangeException(nameof(workerCount));
        ArgumentNullException.ThrowIfNull(execute);

        var output = new TOutput[inputs.Count];
        using var gate = new SemaphoreSlim(workerCount, workerCount);
        var tasks = new Task[inputs.Count];

        for (var index = 0; index < inputs.Count; index++)
        {
            var stableIndex = index;
            tasks[index] = RunOneAsync(stableIndex);
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return output;

        async Task RunOneAsync(int stableIndex)
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                output[stableIndex] = await execute(inputs[stableIndex], cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }
    }

    /// <summary>
    /// Executes independent CPU-bound work with a requested worker budget while preserving the
    /// input index as the only output placement authority. Each worker owns a deterministic static
    /// shard (worker index, worker index + worker count, ...), so scheduling/completion order cannot
    /// influence work assignment or semantic output placement.
    ///
    /// This primitive intentionally has no fixed 16-worker ceiling. The caller owns deployment and
    /// Config policy; the executor only requires a positive worker budget and bounds active workers
    /// by the number of available work items.
    /// </summary>
    public static async Task<DeterministicCpuBatchResultV1<TOutput>> RunCpuBoundAsync<TInput, TOutput>(
        IReadOnlyList<TInput> inputs,
        int workerCount,
        Func<TInput, CancellationToken, TOutput> execute,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (workerCount < 1) throw new ArgumentOutOfRangeException(nameof(workerCount));
        ArgumentNullException.ThrowIfNull(execute);

        var output = new TOutput[inputs.Count];
        if (inputs.Count == 0)
        {
            return new DeterministicCpuBatchResultV1<TOutput>(
                output,
                new DeterministicCpuBatchObservationV1(workerCount, 0, 0));
        }

        var effectiveWorkerCount = Math.Min(workerCount, inputs.Count);
        var activeWorkers = 0;
        var maxObservedConcurrency = 0;

        using var executionCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var executionToken = executionCancellation.Token;
        var workers = new Task[effectiveWorkerCount];

        for (var workerIndex = 0; workerIndex < workers.Length; workerIndex++)
        {
            var stableWorkerIndex = workerIndex;
            workers[workerIndex] = Task.Run(
                () =>
                {
                    try
                    {
                        for (var stableIndex = stableWorkerIndex;
                             stableIndex < inputs.Count;
                             stableIndex += effectiveWorkerCount)
                        {
                            executionToken.ThrowIfCancellationRequested();

                            var active = Interlocked.Increment(ref activeWorkers);
                            UpdateMaximum(ref maxObservedConcurrency, active);
                            try
                            {
                                output[stableIndex] = execute(inputs[stableIndex], executionToken);
                            }
                            finally
                            {
                                Interlocked.Decrement(ref activeWorkers);
                            }
                        }
                    }
                    catch
                    {
                        executionCancellation.Cancel();
                        throw;
                    }
                },
                CancellationToken.None);
        }

        await Task.WhenAll(workers).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        return new DeterministicCpuBatchResultV1<TOutput>(
            output,
            new DeterministicCpuBatchObservationV1(
                workerCount,
                effectiveWorkerCount,
                Volatile.Read(ref maxObservedConcurrency)));
    }

    private static void UpdateMaximum(ref int target, int observed)
    {
        while (true)
        {
            var current = Volatile.Read(ref target);
            if (observed <= current)
                return;
            if (Interlocked.CompareExchange(ref target, observed, current) == current)
                return;
        }
    }
}
