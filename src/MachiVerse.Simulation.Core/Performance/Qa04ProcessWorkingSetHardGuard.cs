using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Harness guard for the complete process run, including world assembly and Snapshot recovery.
/// It does not add recovery observations to the steady-state Step measurement series.
/// </summary>
public static class Qa04ProcessWorkingSetHardGuardV1
{
    public static async Task<T> RunAsync<T>(
        Func<CancellationToken, Task<T>> execute,
        long hardGuardBytes,
        TimeSpan observationInterval,
        CancellationToken cancellationToken = default,
        Func<long>? readWorkingSetBytes = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        if (hardGuardBytes <= 0) throw new ArgumentOutOfRangeException(nameof(hardGuardBytes));
        if (observationInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(observationInterval));
        cancellationToken.ThrowIfCancellationRequested();
        readWorkingSetBytes ??= ReadCurrentProcessWorkingSetBytes;
        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var monitorCancellation = new CancellationTokenSource();
        ExceptionDispatchInfo? guardFailure = null;

        void Observe()
        {
            if (Volatile.Read(ref guardFailure) is not null) return;
            try
            {
                var bytes = readWorkingSetBytes();
                if (bytes < 0)
                    throw new InvalidDataException("qa04.production-run.memory-observation-invalid");
                if (bytes > hardGuardBytes)
                    throw new InvalidDataException(
                        $"qa04.production-run.memory-hard-guard bytes={bytes} limit={hardGuardBytes}");
            }
            catch (Exception failure)
            {
                Interlocked.CompareExchange(ref guardFailure, ExceptionDispatchInfo.Capture(failure), null);
                runCancellation.Cancel();
            }
        }

        async Task MonitorAsync()
        {
            using var timer = new PeriodicTimer(observationInterval);
            try
            {
                while (await timer.WaitForNextTickAsync(monitorCancellation.Token).ConfigureAwait(false))
                {
                    Observe();
                    if (Volatile.Read(ref guardFailure) is not null) return;
                }
            }
            catch (OperationCanceledException) when (monitorCancellation.IsCancellationRequested) { }
        }

        Observe();
        Volatile.Read(ref guardFailure)?.Throw();
        var monitor = MonitorAsync();
        try
        {
            var result = await execute(runCancellation.Token).ConfigureAwait(false);
            Observe();
            return result;
        }
        finally
        {
            monitorCancellation.Cancel();
            await monitor.ConfigureAwait(false);
            // Surface the observed guard failure rather than reporting only its cancellation.
            // A caller that ignores cancellation still cannot publish a successful result.
            Volatile.Read(ref guardFailure)?.Throw();
        }
    }

    private static long ReadCurrentProcessWorkingSetBytes()
    {
        using var process = Process.GetCurrentProcess();
        return process.WorkingSet64;
    }
}
