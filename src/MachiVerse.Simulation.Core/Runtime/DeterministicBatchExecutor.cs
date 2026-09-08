namespace MachiVerse.Simulation.Core.Runtime;

public static class DeterministicBatchExecutor
{
    public static async Task<IReadOnlyList<TOutput>> RunAsync<TInput, TOutput>(
        IReadOnlyList<TInput> inputs,
        int workerCount,
        Func<TInput, CancellationToken, ValueTask<TOutput>> execute,
        CancellationToken cancellationToken = default)
    {
        if (workerCount is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(workerCount));
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
}
