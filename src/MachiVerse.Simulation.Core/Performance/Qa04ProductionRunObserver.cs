using System.Diagnostics;
using System.Security.Cryptography;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public interface IQa04ProductionRunObserverV1
{
    ValueTask ObserveFinalizedStateAsync(WorldStateV1 state, CancellationToken cancellationToken = default);
    ValueTask CompleteAsync(CancellationToken cancellationToken = default);
}

internal sealed class Qa04ParallelStateDigestObserverV1(Action progress) : IQa04ProductionRunObserverV1
{
    private Task? _pending;
    private int _checkpointCount;
    private int _mismatchCount;

    public int CheckpointCount => Volatile.Read(ref _checkpointCount);
    public int MismatchCount => Volatile.Read(ref _mismatchCount);

    public async ValueTask ObserveFinalizedStateAsync(
        WorldStateV1 state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (_pending is not null)
            await _pending.WaitAsync(cancellationToken).ConfigureAwait(false);

        progress();
        Interlocked.Increment(ref _checkpointCount);
        var captured = state;
        _pending = Task.Run(() =>
        {
            var rebuilt = new WorldStateV1(
                captured.Header,
                captured.Partitions,
                captured.SchedulerState,
                captured.OperationState,
                captured.DetailState,
                captured.DomainRegistryState,
                captured.Diagnostic.ConfigDigest);
            if (!CryptographicOperations.FixedTimeEquals(
                    rebuilt.Diagnostic.StateDigest,
                    captured.Diagnostic.StateDigest))
            {
                Interlocked.Increment(ref _mismatchCount);
                throw new InvalidDataException(
                    $"qa04.soak.parallel-state-digest-mismatch:step={captured.Header.Step}");
            }
            progress();
        }, cancellationToken);
    }

    public async ValueTask CompleteAsync(CancellationToken cancellationToken = default)
    {
        if (_pending is not null)
            await _pending.WaitAsync(cancellationToken).ConfigureAwait(false);
        progress();
    }
}
