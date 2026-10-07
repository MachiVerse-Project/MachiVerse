using MachiVerse.Simulation.Core.Performance;

internal static class Qa04ProcessWorkingSetGuardSmoke
{
    internal static Task RunAsync() => CheckAsync();

    private static async Task CheckAsync()
    {
        const long limit = 1024; // Injected test cap; production takes the external Config cap.
        var interval = TimeSpan.FromMilliseconds(1);
        var entered = false;
        await RequireFailure(() => Qa04ProcessWorkingSetHardGuardV1.RunAsync(
            _ => { entered = true; return Task.FromResult(1); }, limit, interval,
            readWorkingSetBytes: () => limit + 1), "memory-hard-guard");
        if (entered) throw new InvalidOperationException("An already-over-limit run was started.");

        long bytes = limit;
        if (await Qa04ProcessWorkingSetHardGuardV1.RunAsync(_ => Task.FromResult(7),
            limit, interval, readWorkingSetBytes: () => bytes) != 7)
            throw new InvalidOperationException("The inclusive cap rejected a valid result.");

        // A recovery-stage spike after all measured Steps still rejects the final result.
        await RequireFailure(() => Qa04ProcessWorkingSetHardGuardV1.RunAsync(
            _ => { bytes = limit + 1; return Task.FromResult(1); }, limit, interval,
            readWorkingSetBytes: () => Volatile.Read(ref bytes)), "memory-hard-guard");

        bytes = limit;
        var readCount = 0;
        await RequireFailure(() => Qa04ProcessWorkingSetHardGuardV1.RunAsync(async token =>
        {
            Interlocked.Exchange(ref bytes, limit + 1);
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return 1;
        }, limit, interval, readWorkingSetBytes: () =>
        {
            Interlocked.Increment(ref readCount);
            return Interlocked.Read(ref bytes);
        }), "memory-hard-guard");
        if (readCount < 2) throw new InvalidOperationException("The in-flight run was not monitored.");

        await RequireFailure(() => Qa04ProcessWorkingSetHardGuardV1.RunAsync(
            _ => Task.FromResult(1), limit, interval, readWorkingSetBytes: () => -1),
            "memory-observation-invalid");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        entered = false;
        try
        {
            await Qa04ProcessWorkingSetHardGuardV1.RunAsync(
                _ => { entered = true; return Task.FromResult(1); }, limit, interval, canceled.Token,
                readWorkingSetBytes: () => limit);
            throw new InvalidOperationException("Caller cancellation was ignored.");
        }
        catch (OperationCanceledException) when (canceled.IsCancellationRequested) { }
        if (entered) throw new InvalidOperationException("A canceled run was started.");
        Console.WriteLine("QA-04 process hard guard: initialization / recovery / in-flight / cancellation PASS");
    }

    private static async Task RequireFailure(Func<Task<int>> execute, string code)
    {
        try { await execute().WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (InvalidDataException failure) when (failure.Message.Contains(code, StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException($"Process hard guard did not reject {code}.");
    }
}
